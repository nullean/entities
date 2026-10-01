# Nullean.Entities

C# records let you model the shape of your domain precisely, and unions seal it: add a case and the
compiler finds every `switch` that needs updating.

But the type system stops there.

It cannot say `Total >= 0`, `a Confirmed order needs lines`, `you can only Ship from Confirmed`, or `every
confirmed order must close within 48 hours`.

Those constraints end up scattered across service methods, validators and tests, rewritten in slightly different forms for each context, silently
drifting apart as the model evolves.

Nullean.Entities gives them a home: declare each rule once, next to the type it governs, and enforce it at mutations, watch it over live event streams, and hand
it to a property tester — the same declaration every time.

```csharp
// The domain. Plain records and a union — nothing references the library.
public sealed record Order(Guid Id, OrderStatus Status, IReadOnlyList<OrderLine> Lines)
{
    public decimal Total => Lines.Sum(l => l.Quantity * l.Price);
}

public sealed record Placed(Guid OrderId);
public sealed record Confirmed(Guid OrderId);
public sealed record Shipped(Guid OrderId);
public sealed record Cancelled(Guid OrderId);
public readonly union OrderEvent(Placed, Confirmed, Shipped, Cancelled);
```

## What the type system can't say

| You want to say | C# enforces it? | The type-theory idea | This library |
|---|---|---|---|
| An order is Pending, Confirmed, Shipped or Cancelled | ✅ unions / sealed records | Sum types (ADTs) | — |
| Every case is handled | ✅ exhaustive `switch` | Exhaustiveness | — |
| `Total >= 0` | ❌ | Refinement types (Liquid Haskell, F\*) | `Always` |
| A Confirmed order has ≥ 1 line | ❌ | Dependent types (Idris, Agda) | `When(...).Requires(...)` |
| You can only Ship a Confirmed order | ❌ | Typestate (Rust, Plaid) | `Transitions`, `StateMachine` |
| Cancel twice = cancel once | ❌ | Algebraic laws | `Idempotent` |
| Confirmed orders ship within 48 h | ❌ | Session types / temporal logic (LTL) | `Reactive` |
| Every state is reachable, none is a dead end | ❌ | Model checking | `StateMachine` *(coming)* |

These are runtime checks, not compile-time ones — but declared once, named, and next to the type.
`[Constraints]` makes the *set* exhaustive at build time: tag the class once, and every static rule
field declared on it is picked up automatically — add one and forget nothing, because there is
nothing left to forget. `ConstraintChannel` turns "parse, don't validate" into **"apply, don't
mutate"**: an invalid next state is never committed, so it never reaches downstream code.

## Have `Nullean.Entities` say/enforce it in its stead

```csharp
// A switch over OrderEvent that omits any case won't build.

// ── What the type system can't say about Order ───────────────────────────
[Constraints] // every static Constraint<Order> field below is found automatically — no per-field tag
public static partial class OrderConstraints
{
    // Total >= 0. message is optional: omitted, a violation names the constraint itself
    // ("TotalNeverNegative was violated.") and attaches the offending Order as evidence.
    public static readonly Constraint<Order> TotalNeverNegative = Constraints<Order>.Always(o => o.Total >= 0);

    // A Confirmed order has at least one line.
    public static readonly Constraint<Order> ConfirmedOrdersHaveLines = Constraints<Order>
        .When(o => o.Status == OrderStatus.Confirmed)
        .Requires(o => o.Lines.Count > 0, "Confirm requires at least one line.");

    // Cancel twice = cancel once.
    public static readonly Constraint<Order> CancelIsIdempotent = Constraints<Order>.Idempotent(o => o.Cancel());

    // You can only Ship a Confirmed order — one Allow(from, to) per legal arc, instead of a
    // hand-written tuple pattern-match that is easy to get backwards or miss a case in.
    public static readonly Constraint<Order> LegalTransitions = Constraints<Order>
        .Transitions(o => o.Status)
        .Allow(OrderStatus.Pending, OrderStatus.Confirmed)
        .Allow(OrderStatus.Confirmed, OrderStatus.Shipped)
        .Allow(OrderStatus.Pending, OrderStatus.Cancelled)
        .Allow(OrderStatus.Confirmed, OrderStatus.Cancelled);

    // Generated: OrderConstraints.All (ConstraintSet<Order>), from all four fields above.
    // Add a fifth Constraint<Order> field and it joins the set on the next build with no extra
    // step; make the type non-partial, or target the wrong type, and the build still fails outright.
}
```

Validate a single instance at any point in time, or wrap a mutation in a channel that only commits
if every constraint passes — violations are returned, nothing is left in an invalid state:

```csharp
// Point-in-time: is this order valid right now?
IReadOnlyList<Violation> problems = OrderConstraints.All.Validate(order);

// Guarded mutation: only commit if every constraint passes.
var channel = ConstraintChannel.For(order).Enforce(OrderConstraints.All);

channel.Apply(o => o.Ship()) switch
{
    ChannelResult<Order>.Applied(var shipped)       => Save(shipped),
    ChannelResult<Order>.Noop(var current)          => /* already shipped */ _ = current,
    ChannelResult<Order>.Failure(_, var problems)   => Reject(problems),   // order is unchanged
};
```

## `Nullean.Entities.Reactive` to enforce constraints over a stream of events

```csharp
// using Nullean.Entities;           — for [Constraints] itself
// using Nullean.Entities.Reactive;  — for ReactiveConstraint<T>
// using static Nullean.Entities.Reactive.Constraints<OrderEvent>; — see below

// ── What the type system can't say about sequences of OrderEvents ────────
[Constraints] // same auto-discovery as the sync side — no per-field tag below either
public static partial class OrderEventSpec
{
    private static Guid OrderIdOf(OrderEvent e) => e switch
    {
        Placed p    => p.OrderId,
        Confirmed c => c.OrderId,
        Shipped s   => s.OrderId,
        Cancelled c => c.OrderId,
    };

    // Cancelled orders are never shipped. Predicate-based (isT1/isT2), not Never<T1,T2> — the
    // sync and reactive packages each declare their own Constraints<T> (the using static above
    // picks the reactive one unambiguously once [Constraints], from Nullean.Entities, is also in
    // scope), and separately, a union case has no inheritance relationship to the union itself, so
    // "is this a Cancelled" can only ever be a runtime check (e is Cancelled), never a generic
    // constraint — every operator below has this predicate-based form for exactly that reason.
    public static readonly ReactiveConstraint<OrderEvent> NeverShippedAfterCancellation = Never(
        isT1: e => e is Cancelled,
        isT2: e => e is Shipped,
        pair:  (c, s) => OrderIdOf(c) == OrderIdOf(s));

    // Confirmed orders close (ship or cancel) within 48 h. AfterUntil collapses the whole
    // After<T>().Then().Within() builder into one call, since there's no type parameter for a
    // union case to hang separate builder steps off.
    public static readonly ReactiveConstraint<OrderEvent> ConfirmedOrdersClose = AfterUntil(
        isTrigger: e => e is Confirmed,
        isNext:    (trigger, next) => next is Shipped or Cancelled && OrderIdOf(trigger) == OrderIdOf(next),
        window:    TimeSpan.FromHours(48));

    // Every confirmed order eventually closes — no deadline, but it must happen.
    public static readonly ReactiveConstraint<OrderEvent> EveryConfirmedOrderCloses = Eventually(
        scope:    e => OrderIdOf(e),
        from:     e => e is Confirmed,
        isTarget: e => e is Shipped or Cancelled);

    // Generated: OrderEventSpec.All (ReactiveConstraintSet<OrderEvent>), from all three
    // fields above and named from them — .Named(...) is for a constraint built outside a
    // [Constraints] type, which has no field name of its own to borrow.
}
```

Attach the spec to any `IObservable<T>` and read verdicts at any time — in a test, in production, or
wired into a health check. The spec does not know or care which:

```csharp
var monitored = OrderEventSpec.All.Attach(service.Events);

service.Place(orderId);
service.Confirm(orderId);

monitored[OrderEventSpec.ConfirmedOrdersClose].Current;  // Verdict.Pending — clock still running
monitored.AnyViolated;                                   // false

monitored[OrderEventSpec.ConfirmedOrdersClose].Verdicts
    .Where(v => v.IsViolated)
    .Subscribe(v => alerting.Emit("order-sla-breach", v));
```

`Pending` means "still watching." `Violated` is final. `.Required()` means a trigger that never
fires is itself a violation, not a vacuous pass.

> `union` is a C# 11 feature. On .NET 10 and earlier, use `abstract record OrderEvent` with nested
> `sealed record` cases — every API above works unchanged.

## Let a computer try to break them

Hand-written tests check the scenarios you thought of. Declared constraints let a property tester
check the ones you didn't. A failure shrinks automatically to the smallest reproduction and comes
with a seed to replay it.

The testing axes map to how you think about correctness, not to any specific framework method:

| Axis | The question |
|---|---|
| Invariant | Is every generated value valid? |
| Idempotency | Doing it twice is the same as once? |
| Monotonicity | Does this quantity only ever move one way? |
| Conservation | Is nothing created or destroyed by this operation? |
| Absence (safety) | Does this bad sequence of events never happen? |
| Temporal (bounded liveness) | Does the good thing happen within a deadline? |
| Liveness (unbounded) | Does the good thing eventually happen at all? |
| Confluence | Do two orderings of the same operations agree? |
| Vacancy | Did the test generator actually reach the interesting states? |

`Nullean.Entities.CsCheck` maps each axis to a test entry point. `Script` builds a sequence of
named steps with generated arguments and optional virtual-time delays, then drives a real SUT with
random orderings. The same `OrderEventSpec.All` declared above runs unchanged — no
re-declaration for tests:

```csharp
var script = Script.For<OrderService>()
    .Step("Place",   Gen.Guid, (svc, id) => svc.Place(id))
    .Step("Confirm", Gen.Guid, (svc, id) => svc.Confirm(id))
    .Step("Ship",    Gen.Guid, (svc, id) => svc.Ship(id))
    .Step("Cancel",  Gen.Guid, (svc, id) => svc.Cancel(id))
    .Delays(Gen.Int[0, 500].Select(ms => TimeSpan.FromMilliseconds(ms)))
    .Length(3, 25);

// Generates hundreds of random step sequences, runs each against a fresh OrderService,
// checks every reactive constraint after each run. Finds the cases you didn't think of.
script.SampleWith(_ => new OrderService(), svc => svc.Events, OrderEventSpec.All);
```

A failure prints the shrunk trace and a replay seed:

```
Spec violated: ConfirmedOrdersClose
  +000ms  Place(a1b2c3d4-...)
  +120ms  Confirm(a1b2c3d4-...)
  +49h    [timeout — no Shipped or Cancelled within 48 h]
Shrunk to 3 steps from 19.
CsCheck_Seed=0Hk1u3ZbQ0p1
```

When steps need realistic domain data, swap any `Gen<T>` for a Bogus-backed generator. It routes
every Bogus draw through CsCheck's own random source, so shrinking and seed replay work identically:

```csharp
var genLine = GenFakerExtensions.Faker<OrderLine>(faker => faker
    .RuleFor(l => l.Sku,      f => f.Commerce.Ean13())
    .RuleFor(l => l.Quantity, f => f.Random.Int(1, 100))
    .RuleFor(l => l.Price,    f => f.Finance.Amount()));

var genCustomer = GenAuto.Of<Customer>(); // fills every field, nested objects too — all shrinkable

var script = Script.For<OrderService>()
    .Step("Place",   genLine,       (svc, line)     => svc.PlaceOrder(line))
    .Step("Confirm", genCustomer,   (svc, customer) => svc.ConfirmWith(customer))
    .Step("Cancel",  Gen.Guid,      (svc, id)       => svc.Cancel(id))
    .Length(3, 25);
```

The supported axes today and what's coming:

| Axis | Status |
|---|---|
| Invariant, Idempotency, Monotonicity, Conservation | ✅ `ConstraintSet<T>.Validate`, `Idempotent`, `Monotonic`, `Conserves` |
| Absence, Temporal, Liveness | ✅ `Script.SampleWith` + `ReactiveConstraintSet` |
| Confluence | ✅ `Script.Metamorphic` |
| Model-based (SUT state vs machine prediction) | ⏳ needs `StateMachine` |
| Parallel / linearizability | ⏳ |
| Vacancy (every transition exercised) | ⏳ needs `StateMachine` |

See [Nullean.Entities.CsCheck](src/Nullean.Entities.CsCheck/README.md) for the full API.

## State machines *(coming next)*

`Transitions(...)` already checks individual state-transition rules. What is not yet built is a
first-class machine — something that proves the declared states form a structurally sound graph and
then derives all the checks from that one declaration.

The planned API groups transitions by `From` state, so the declaration reads like a diagram, and
lives in a companion class:

```csharp
public readonly union OrderState(Draft, Placed, Confirmed, Shipped, Cancelled);

public static partial class OrderLifecycle
{
    [StateMachine]
    public static readonly StateMachine<OrderState> Machine = StateMachine<OrderState>.Define(m => m
        .Initial<Draft>(s => s
            .On("Place").To<Placed>())

        .State<Placed>(s => s
            .On("Confirm").To<Confirmed>(when: p => p.Lines.Count > 0)
            .On("Cancel").To<Cancelled>())

        .State<Confirmed>(s => s
            .To<Shipped>()             // .On() is optional — unnamed transitions take their target's name
            .On("Cancel").To<Cancelled>())

        .Terminal<Shipped>()
        .Terminal<Cancelled>());
}
```

The build refuses to compile if:

- any case of `OrderState` is not mentioned in the machine *(NESM001 — exhaustiveness)*
- any state is unreachable from `Initial` *(NESM002)*
- any non-terminal state has no outgoing transition *(NESM003 — dead end)*
- any state cannot reach a terminal *(NESM004 — trap)*

These are structural checks; guards are deliberately ignored. Whether a guard actually lets a
transition fire at runtime is exactly what the property tester answers.

From one declaration, everything else derives:

```csharp
Constraint<OrderState>         guard   = OrderLifecycle.Machine.AsConstraint();
ReactiveConstraint<OrderEvent> monitor = OrderLifecycle.Machine.AsReactive(stateOf: e => ...);
Gen<IReadOnlyList<Step>>       walks   = OrderLifecycle.Machine.Walks();
string                         diagram = OrderLifecycle.Machine.ToMermaid();

// Once Walks() is available:
script.SampleWith(factory, events, spec, coverage: OrderLifecycle.Machine);
// ↑ fails the run if some declared transition was never exercised
```

## Packages

| Package | What it adds | Depends on |
|---|---|---|
| `Nullean.Entities` | `Constraint<T>`, `ConstraintChannel`, `[Constraint]`/`[Constraints]` source generator | nothing |
| `Nullean.Entities.Reactive` | `ReactiveConstraint<T>`, temporal operators, live monitoring | `System.Reactive` |
| `Nullean.Entities.CsCheck` | `Script`, action-sequence testing, Bogus/AutoBogus generators | `CsCheck`, `Bogus` |

```sh
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.Reactive
dotnet add package Nullean.Entities.CsCheck
```

## Building from source

Requires the .NET 11 preview SDK (pinned in `global.json`) — every project multi-targets
`net10.0;net11.0`. Expect an `NETSDK1057` preview-SDK notice on every build; that is expected.

```sh
git clone https://github.com/nullean/entities.git nullean-entities
cd nullean-entities
./build.sh build
./build.sh test
```

See [AGENTS.md](AGENTS.md) for the full build target list, package layout, and design conventions.

## License

MIT — see [LICENSE.txt](LICENSE.txt).
