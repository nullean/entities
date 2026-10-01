# Nullean.Entities

C# records let you model the shape of your domain precisely, and unions seal it: add a case and the
compiler finds every `switch` that needs updating. But the type system stops there. It cannot say
`Total >= 0`, `a Confirmed order needs lines`, `you can only Ship from Confirmed`, or `every
confirmed order must close within 48 hours`. Those constraints end up scattered across service
methods, validators and tests — rewritten in slightly different forms for each context, silently
drifting apart as the model evolves. Nullean.Entities gives them a home: declare each rule once,
next to the type it governs, and enforce it at mutations, watch it over live event streams, and hand
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
// A switch over OrderEvent that omits any case won't build.

// ── What the type system can't say about Order ───────────────────────────
public static partial class OrderConstraints
{
    // Total >= 0
    [Constraint] public static readonly Constraint<Order> TotalNeverNegative =
        Constraints<Order>.Always(o => o.Total >= 0, "Order total can't be negative.");

    // A Confirmed order has at least one line.
    [Constraint] public static readonly Constraint<Order> ConfirmedOrdersHaveLines =
        Constraints<Order>.When(o => o.Status == OrderStatus.Confirmed)
                          .Requires(o => o.Lines.Count > 0, "Confirm requires at least one line.");

    // Cancel twice = cancel once.
    [Constraint] public static readonly Constraint<Order> CancelIsIdempotent =
        Constraints<Order>.Idempotent(o => o.Cancel(), "Cancel must be idempotent.");

    // You can only Ship a Confirmed order.
    [Constraint] public static readonly Constraint<Order> LegalTransitions =
        Constraints<Order>.Transitions(
            (prev, next) => (prev.Status, next.Status) is
                (Pending, Confirmed) or (Confirmed, Shipped) or
                (Pending or Confirmed, Cancelled),
            "Illegal status transition.");
    // Generated: OrderConstraints.Constraints (ConstraintSet<Order>)
    // Forget [Constraint] on a field, or target the wrong type — the build fails, not a warning.
}

// ── What the type system can't say about sequences of OrderEvents ────────
public static partial class OrderEventSpec
{
    private static Guid OrderIdOf(OrderEvent e) => e switch
    {
        Placed p    => p.OrderId,
        Confirmed c => c.OrderId,
        Shipped s   => s.OrderId,
        Cancelled c => c.OrderId,
    };

    // Cancelled orders are never shipped.
    [Constraint] public static readonly ReactiveConstraint<OrderEvent> NeverShippedAfterCancellation =
        Constraints<OrderEvent>.Never<Cancelled, Shipped>((c, s) => c.OrderId == s.OrderId)
            .Named(nameof(NeverShippedAfterCancellation));

    // Confirmed orders close (ship or cancel) within 48 h.
    [Constraint] public static readonly ReactiveConstraint<OrderEvent> ConfirmedOrdersClose =
        Constraints<OrderEvent>.After<Confirmed>()
            .Then<Shipped, Cancelled>(
                m1: (c, s) => s.OrderId == c.OrderId,
                m2: (c, ca) => ca.OrderId == c.OrderId)
            .Within(TimeSpan.FromHours(48))
            .Required()
            .Named(nameof(ConfirmedOrdersClose));

    // Every confirmed order eventually closes — no deadline, but it must happen.
    [Constraint] public static readonly ReactiveConstraint<OrderEvent> EveryConfirmedOrderCloses =
        Constraints<OrderEvent>.Eventually<Shipped>(
            scope: OrderIdOf,
            from:  e => e is Confirmed)
            .Named(nameof(EveryConfirmedOrderCloses));
    // Generated: OrderEventSpec.Constraints (ReactiveConstraintSet<OrderEvent>)
}
```

Enforce at mutations. Watch in production. Break with a property tester.

```csharp
// Enforce: only commit if every constraint passes.
var channel = ConstraintChannel.For(order).Enforce(OrderConstraints.Constraints);
channel.Apply(o => o.Ship()) switch
{
    Result<Order>.Ok(var shipped)       => Save(shipped),
    Result<Order>.Fail(_, var problems) => Reject(problems),   // order unchanged
};

// Watch: attach to a live stream, read verdicts any time.
var monitored = OrderEventSpec.Constraints.Attach(service.Events);
monitored[OrderEventSpec.ConfirmedOrdersClose].Current;   // Verdict.Pending / Violated

// Break: hundreds of random action sequences, failures shrunk and seed-reproducible.
Script.For<OrderService>()
    .Step("Place",   Gen.Guid, (svc, id) => svc.Place(id))
    .Step("Confirm", Gen.Guid, (svc, id) => svc.Confirm(id))
    .Step("Ship",    Gen.Guid, (svc, id) => svc.Ship(id))
    .Step("Cancel",  Gen.Guid, (svc, id) => svc.Cancel(id))
    .Length(3, 25)
    .SampleWith(_ => new OrderService(), svc => svc.Events, OrderEventSpec.Constraints);
```

> `union` is a C# 11 feature. On .NET 10 and earlier, use `abstract record OrderEvent` with nested
> `sealed record` cases — every API above works unchanged.

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
`[Constraint]` makes the *set* exhaustive at build time: add a field and forget to tag it, and the
build refuses. `ConstraintChannel` turns "parse, don't validate" into **"apply, don't mutate"**: an
invalid next state is never committed, so it never reaches downstream code.

`OrderConstraints.Instance` and `OrderEventSpec.Instance` (both generated) implement
`IConstraintsFor<T>` / `IReactiveConstraintsFor<T>`, so generic utilities can receive either by
interface and infer `T` without knowing the concrete class.

If you prefer the rules on the domain type itself — tag fields directly on the type and the generator
adds `IHaveConstraints<T>` to its partial declaration, enabling `events.AttachConstraints()` without
naming the spec class.

## Let a computer try to break them

Hand-written tests check the scenarios you thought of. Declared constraints let a property tester
check the ones you didn't. A failure shrinks automatically to the smallest reproduction and comes
with a seed to replay it.

### Testing axes

| Axis | The question | Declaration |
|---|---|---|
| Invariant | Is every generated value valid? | `Constraints<Order>.Always(o => o.Total >= 0, ...)` |
| Idempotency | Twice == once? | `Constraints<Order>.Idempotent(o => o.Cancel(), ...)` |
| Monotonicity | Only moves one way? | `Constraints<Order>.Monotonic(o => o.Total, NonDecreasing)` |
| Conservation | Nothing created or destroyed? | `Constraints<(Order, Order)>.Conserves(p => ...)` |
| Absence (safety) | The bad sequence never happens? | `Never<Cancelled, Shipped>((c, s) => ...)` |
| Temporal (bounded liveness) | The good thing happens within a deadline? | `After<Confirmed>().Then<Shipped, Cancelled>(...).Within(48h).Required()` |
| Liveness (unbounded) | The good thing eventually happens? | `Eventually<Shipped>(scope: OrderIdOf, from: ...)` |
| Confluence | Two orderings must agree? | `script.Metamorphic(factory, gen, path1, path2, equal)` |
| Vacancy | Did the generator hit the interesting states? | `coverage: OrderLifecycle.Machine` *(coming)* |

### CsCheck integrations

| Method | What you get | Status |
|---|---|---|
| `Gen<T>.Sample` | Random values + shrinking | ✅ works with `ConstraintSet<T>.Validate`; Bogus/AutoBogus generators stay shrinkable via `GenFakerExtensions.Faker<T>` and `GenAuto.Of<T>` |
| `Script.SampleWith` | Random action sequences in virtual time against a live SUT | ✅ |
| `Script.RunWith` | Progress and shrink events as an observable stream | ✅ returns `IObservable<EntityEvent>` |
| `Script.Metamorphic` | Two orderings of the same operations must agree (confluence) | ✅ |
| Model-based testing | SUT state must match the machine's predicted state after every step | ⏳ needs `StateMachine` |
| Parallel / linearizability | Concurrent steps, same reactive spec | ⏳ |
| Coverage | Fail if some declared transition was never exercised | ⏳ needs `StateMachine` |

```csharp
var script = Script.For<OrderService>()
    .Step("Place",   Gen.Guid, (svc, id) => svc.Place(id))
    .Step("Confirm", Gen.Guid, (svc, id) => svc.Confirm(id))
    .Step("Ship",    Gen.Guid, (svc, id) => svc.Ship(id))
    .Step("Cancel",  Gen.Guid, (svc, id) => svc.Cancel(id))
    .Delays(Gen.Int[0, 500].Select(ms => TimeSpan.FromMilliseconds(ms)))
    .Length(3, 25);

// The same spec declared above — no re-declaration.
script.SampleWith(_ => new OrderService(), svc => svc.Events, OrderEventSpec.Constraints);
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

Swap `Gen.Guid` for a Bogus generator to get realistic-looking data — still shrinkable and
seed-reproducible, because `GenFakerExtensions.Faker<T>` routes Bogus draws through CsCheck's own
random source:

```csharp
var genCustomer = GenAuto.Of<Customer>(); // every field, nested objects, all shrinkable
var genSku      = GenFakerExtensions.Faker<string>(faker => faker.Commerce.Ean13());
```

## The same rules, in production

Attach `OrderEventSpec.Constraints` to any live event stream:

```csharp
using var service = new OrderService();
var monitored = OrderEventSpec.Constraints.Attach(service.Events);

service.Place(orderId);
service.Confirm(orderId);

monitored.AnyViolated;                                   // false — nothing has broken yet
monitored[OrderEventSpec.ConfirmedOrdersClose].Current;  // Verdict.Pending — clock still running
```

`Pending` means "still watching." `Violated` is final. `.Required()` means a trigger that never fires
is itself a violation, not a vacuous pass.

Subscribe to violations in a dashboard or health check:

```csharp
monitored[OrderEventSpec.ConfirmedOrdersClose].Verdicts
    .Where(v => v.IsViolated)
    .Subscribe(v => alerting.Emit("order-sla-breach", v));
```

The spec does not know or care whether it is running inside a test or in production.

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
| `Nullean.Entities` | `Constraint<T>`, `ConstraintChannel`, `[Constraint]` source generator | nothing |
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
