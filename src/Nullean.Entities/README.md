# Nullean.Entities

Synchronous domain constraints for .NET: name a rule once, check it against a value or a proposed
mutation. No `System.Reactive`, no `CsCheck` — this package has no dependency beyond the BCL.

## The idea

A `Constraint<T>` is a named rule, built through `Constraints<T>` rather than a public constructor:

```csharp
var alwaysPositive = Constraints<Item>.Always(item => item.Value > 0, "Value must stay positive.");
```

`message` is optional on every builder in this package (`Always`, `Transitions`, `Idempotent`,
`Monotonic`, `Conserves`, `Requires`). Omitted, the violation names the constraint itself — whatever
`.Named(...)` or `[Constraint]`'s generated `(nameof(field), field)` tuple gave it — and attaches the
offending value(s) as `Violation.Evidence`, so a quick rule doesn't need prose until it's worth
writing:

```csharp
public static readonly Constraint<Item> AlwaysPositive = Constraints<Item>.Always(item => item.Value > 0);
// violated: "AlwaysPositive was violated.", Evidence == the offending Item
```

It lives anywhere — a `static readonly` field, a local variable, a constructor parameter. A
`ConstraintSet<T>` is an ordered collection of them, named from the tuple you build it with:

```csharp
var constraints = ConstraintSet.From((nameof(alwaysPositive), alwaysPositive), (nameof(neverOverHundred), neverOverHundred));

IReadOnlyList<Violation> violations = constraints.Validate(new Item(-1));
// violations[0].ConstraintId == "alwaysPositive"
```

`Always(predicate, message)` covers the common fixed-message case. `Constraints<T>.Check(check)` is
the escape hatch for a message built from the value itself:

```csharp
var alwaysPositive = Constraints<Item>.Check(
    item => item.Value > 0 ? [] : [Violation.Error($"Value {item.Value} is not positive")]
);
```

## The problem with guards

A guard in a service method enforces a rule at one call site. Add a second path into the same
operation and the guard has to be copied. Forget to copy it and nothing fails at compile time — the
rule just silently stops applying.

```csharp
// Before: a rule written three ways, in three places, drifting apart
public void Confirm(Order order)
{
    if (order.Lines.Count == 0)
        throw new InvalidOperationException("Cannot confirm an empty order.");
    // ...
}

public async Task ConfirmAsync(Order order)
{
    if (order.Lines.Count == 0)           // same rule, copy-pasted
        throw new InvalidOperationException("Cannot confirm an empty order.");
    // ...
}

public void BulkConfirm(IEnumerable<Order> orders)
{
    foreach (var o in orders.Where(o => o.Lines.Count > 0)) // different expression, same intent
    {
        // ...
    }
}
```

```csharp
// After: one rule, applied at every call site
[Constraints]
public static partial class OrderConstraints
{
    public static readonly Constraint<Order> MustHaveLines =
        Constraints<Order>.Always(o => o.Lines.Count > 0, "An order must have at least one line.");

    public static readonly Constraint<Order> LegalStatusTransitions = Constraints<Order>
        .Transitions(o => o.Status)
        .Allow(OrderStatus.Pending, OrderStatus.Confirmed)
        .Allow(OrderStatus.Confirmed, OrderStatus.Shipped)
        .Allow(OrderStatus.Pending, OrderStatus.Cancelled)
        .Allow(OrderStatus.Confirmed, OrderStatus.Cancelled);
}

// Every caller uses the same set — the rule cannot drift
var result = OrderConstraints.All.Apply(order, o => o.Confirm());
```

`[Constraints]` auto-discovers every `Constraint<Order>` field on `OrderConstraints` and generates
`OrderConstraints.All` — a `ConstraintSet<Order>`. Add a new field and it's included on the next
build; remove one and it disappears. No list to maintain, no drift between field name and constraint
name.

## Building a value that cannot be invalid

Constraints guard changes (`Apply`) and check values you already have (`Validate`). To make a type that cannot be *created* invalid, put the check in a factory, not in the constructor:

```csharp
public sealed record Money
{
    private Money(decimal amount, string currency) => (Amount, Currency) = (amount, currency);
    public decimal Amount { get; }
    public string Currency { get; }

    private static readonly ConstraintSet<Money> Rules = ConstraintSet.From(
        ("non-negative", Constraints<Money>.Always(m => m.Amount >= 0, "Amount cannot be negative")),
        ("iso-currency", Constraints<Money>.Always(m => m.Currency.Length == 3, "Currency must be a 3-letter code")));

    public static Result<Money> Create(decimal amount, string currency) => Rules.Validated(new Money(amount, currency));
}
```

- `Validated(value)` returns `Ok` unless an `Error` rule is violated (warnings and hints do not reject); `Ensure(value)` is the same but throws `ConstraintViolationException`, for a place where an invalid value is a bug.
- Compose with `Map`, `Bind` and query syntax (`from m in Money.Create(...) from c in Currency.Parse(...) select ...`), which stop at the first failure, or with `Result.Combine(a, b, (x, y) => ...)`, which keeps **every** violation: use it to report all that is wrong with a form at once.
- Why not validate inside the constructor? The rules usually live in a static field of the same type, so a constructor that reads them depends on static initialisation order; and `with` copies and deserialisers go around constructors anyway. Check at the boundary instead: the factory, and `Apply` for everything after.
- `ConstrainedValue.For(x).Enforce(set)` does not check `x`, so a channel can be started over legacy data and repaired through `Apply`. Use `ConstrainedValue.For(set.Ensure(x))` to refuse an invalid start.

## Safe mutation

`Apply` runs a mutation, checks every constraint against the result, and only commits if all of
them pass:

```csharp
Result<Item> result = constraints.Apply(current, item => item with { Value = item.Value + 1 });

var next = result.Match(
    ok => ok,
    violations => throw new InvalidOperationException(violations[0].Message)
);
```

`Apply` treats a mutation that changes nothing as `Ok`. When "nothing happened" matters (skip a
save, skip an audit event), `ApplyDetailed` returns an `ApplyOutcome<T>` — `Applied`, `Noop` or
`Failure` — instead. `ApplyOrThrow` is `Apply` without the `Match`. `ConstrainedValue<T>` wraps a single mutable
`Current` value behind the same `Apply` semantics, so a caller can keep proposing mutations without
re-plumbing the constraint set each time:

```csharp
var channel = ConstrainedValue.For(new Item(1)).Enforce(constraints);
channel.Apply(item => item with { Value = 5 });   // Current is now 5
channel.Apply(item => item with { Value = -1 });  // rejected; Current is still 5
```

## Exhaustive constraint sets: `[Constraint]`

Tag every `Constraint<T>` field or property that belongs to one type with `[Constraint]`, and
[`Nullean.Entities.SourceGenerators`](../Nullean.Entities.SourceGenerators) (bundled into this
package's own nupkg as an analyzer — no separate install) bundles them into a `Constraints`
property automatically:

```csharp
public sealed partial record Item(int Value)
{
    [Constraint]
    public static readonly Constraint<Item> AlwaysPositive = Constraints<Item>.Always(i => i.Value > 0, "...");

    [Constraint]
    public static readonly Constraint<Item> NeverOverHundred = Constraints<Item>.Always(i => i.Value <= 100, "...");

    // Generated: public static ConstraintSet<Item> Constraints { get; } =
    //     ConstraintSet.From((nameof(AlwaysPositive), AlwaysPositive), (nameof(NeverOverHundred), NeverOverHundred));
}
```

A type whose own `[Constraint]` fields target itself also gets `IHaveConstraints<T>` added to its
partial declaration for free, so `Item.Constraints` and generic code written against
`IHaveConstraints<T>` both just work. A separate "spec" companion class tagging someone else's type
gets a nested `TypeConstraints` struct instead, implementing `IConstraintsFor<T>` — a plain instance
interface, not F-bounded and not static-abstract, since the spec class and `T` are different types
and the whole point is a *value* generic code can take as a parameter:

```csharp
public sealed record Order(Guid Id, decimal Total, string? PaymentRef);

public static partial class OrderSpec
{
    [Constraint]
    public static readonly Constraint<Order> TotalNonNegative = Constraints<Order>.Always(o => o.Total >= 0, "...");
}

// Generic code takes the value and infers T from it — no need to know OrderSpec exists:
static ConstraintSet<T> ConstraintsFor<T>(IConstraintsFor<T> spec) => spec.All;

ConstraintsFor(OrderSpec.Instance); // == OrderSpec.All
```

The interface is never on the spec class's own base list, even if you declare it as an ordinary
(non-static) class: `IConstraintsFor<T>.All` is an instance member, and `OrderSpec.All`
is (and stays) `static` either way, so there is nothing on `OrderSpec` itself that could implement it.
A nested `OrderSpec.TypeConstraints` is a zero-size singleton (`readonly struct`, one static field)
that forwards to it instead — one path, regardless of how the spec class is declared. `OrderSpec.Instance`
is itself a forwarding shortcut for `OrderSpec.TypeConstraints.Instance`, generated on the outer class,
so a caller never has to spell out `TypeConstraints` at all. The set can never drift from the fields
actually declared, since there is no separate list to keep in sync and adding a field updates it
automatically.

Generic types are supported (the type parameters are carried into the generated declaration). A type nested in other types is supported when every type around it is `partial` too (the generator re-declares them). Any misused `[Constraint]` member — not static, wrong type,
not partial (or nested in something that is not), or an
inconsistent type argument across members on the same type — refuses to generate anything for that
type at all, rather than silently generating from the valid subset. See
[the generator's own README](../Nullean.Entities.SourceGenerators/README.md) for the full rules.

### Tagging the type instead of each field: `[Constraints]`

`[Constraint]` is per-member — tag the field, it's included. `[Constraints]` is per-type instead:
put it on the type once, and every static `Constraint<T>`/`ReactiveConstraint<TEvent>`-typed field or
property declared directly on it is picked up with no tagging at all:

```csharp
[Constraints]
public static partial class OrderConstraints
{
    public static readonly Constraint<Order> TotalNeverNegative = Constraints<Order>.Always(o => o.Total >= 0);
    public static readonly Constraint<Order> CancelIsIdempotent = Constraints<Order>.Idempotent(o => o.Cancel());

    // Generated: OrderConstraints.All (ConstraintSet<Order>), from both fields above —
    // neither needed [Constraint]. Add a third static Constraint<Order> field and it's included
    // automatically on the next build; forget to make it static, or declare the type non-partial,
    // and the build fails the same way an [Constraint] mistake would (NECG001 etc.) — see the
    // generator's own README for the full diagnostic list.
}
```

`[ConstraintIgnore]` is the per-member opt-out, for the rare private helper that builds a constraint
but shouldn't be counted as one of the type's own rules:

```csharp
[Constraints]
public static partial class OrderConstraints
{
    [ConstraintIgnore]
    private static readonly Constraint<Order> BuildingBlock = Constraints<Order>.Always(o => o.Total >= 0);

    public static readonly Constraint<Order> TotalMatchesLines = /* ...composes BuildingBlock with something else... */;
}
```

A type using `[Constraints]` doesn't also need `[Constraint]` on its own members — if both appear,
the explicit tag is simply redundant (already found by auto-discovery), never double-counted.

## Sequential constraints: `Transitions`

`Always` and `Check` only ever see the value being checked. A constraint that depends on what the
value *was* — a legal state-machine transition, a value that must never decrease — needs
`Transitions`, which only `Apply` (never `Validate`) can run, since a standalone instance has no
"previous" to compare against:

```csharp
var validTransitions = Constraints<Order>.Transitions(
    (prev, next) => (prev.Status, next.Status) switch
    {
        (OrderStatus.Pending, OrderStatus.Confirmed) => true,
        (OrderStatus.Confirmed, OrderStatus.Shipped) => true,
        (_, OrderStatus.Cancelled) => true,
        _ => false,
    },
    "Illegal order status transition."
);
```

`When(guard).Requires(requirement)` adds a conditional constraint — violated only when `guard` is
true for the value but `requirement` fails:

```csharp
var paymentRefRequired = Constraints<Order>
    .When(o => o.Status == OrderStatus.Confirmed)
    .Requires(o => o.PaymentRef != null, "PaymentRef is required once an order is confirmed.");
```

### A declarative allow-list: `Transitions<TState>`

Hand-writing a `switch` of legal `(prev, next)` pairs works, but it's tedious and easy to get wrong
(a missed case, a backwards tuple) once there are more than two or three. `Transitions<TState>`
takes a selector and an explicit allow-list instead — one `Allow(from, to)` per legal edge, violated
on any change of the selected value that is not in it. A mutation that leaves the selected value
alone (adding a line to a `Pending` order) is not a transition and is never rejected:

```csharp
var legalTransitions = Constraints<Order>
    .Transitions(o => o.Status)
    .Allow(OrderStatus.Pending, OrderStatus.Confirmed)
    .Allow(OrderStatus.Confirmed, OrderStatus.Shipped)
    .Allow(OrderStatus.Pending, OrderStatus.Cancelled)
    .Allow(OrderStatus.Confirmed, OrderStatus.Cancelled);
```

It converts implicitly to `Constraint<T>`, so it drops straight into a field or a `ConstraintSet.From`
tuple with no extra call. Reach for the predicate-based `Transitions(Func<T, T, bool>, string?)`
overload above instead when a transition's legality depends on more than just the two state values
(for example, a quantity carried on the event) — `Transitions<TState>` is for the common case where
the thing moving through states is a single comparable value, usually an enum.

## Named qualities beyond a plain predicate

> See [`Nullean.Entities.CsCheck`'s testing-qualities table](../Nullean.Entities.CsCheck#testing-qualities-not-just-assertions)
> for the full set, including the reactive (`Eventually`) and CsCheck-side (`Metamorphic`) ones.

`Always` and `Check` can express any of these by hand, but naming the quality instead of hiding it
in an arbitrary predicate means a failure says *what kind* of thing broke, not just which rule did:

```csharp
// Idempotent: applying an action once or twice from the same starting value gives the same result
// — the standard f(f(x)) == f(x) law. Point-in-time: Cancel() supplies both "once" and "twice", so
// Validate is enough, no mutation needed.
Constraints<Order>.Idempotent(o => o.Cancel(), "Cancelling twice must leave the order unchanged.");

// Monotonic: a selected value never moves the wrong way across a mutation. Sequential, so it only
// ever fires from Apply — step-scoped by construction, since it's meaningless without a previous
// value. Attach it to the Constraints backing the specific step(s) meant to hold it (e.g. AddLine),
// not a type's whole Constraints set — a step that's allowed to shrink the value (RemoveLine) would
// otherwise wrongly fail it.
Constraints<Order>.Monotonic(o => o.Total, MonotonicDirection.NonDecreasing);

// Conserves: a quantity is preserved across a transition that moves it between two values, not one.
// Sequential, like Monotonic, but by equality instead of ordering — the first shape here that's
// naturally exercised with a pair-valued T rather than a single entity.
Constraints<(Order From, Order To)>.Conserves(p => p.From.Total + p.To.Total, "Moving a line must not create or destroy value.");
```

## `IHaveConstraints<TSelf>`

A type can declare its own constraint set as a static member, using a C# 11 static abstract
interface member — this is what `[Constraint]` generates for you, but it can be written by hand
too:

```csharp
public sealed partial record Item(int Value) : IHaveConstraints<Item>
{
    private static readonly Constraint<Item> AlwaysPositive = Constraints<Item>.Always(i => i.Value > 0, "...");

    public static ConstraintSet<Item> Constraints { get; } = ConstraintSet.From((nameof(AlwaysPositive), AlwaysPositive));
}
```

This is what [`Nullean.Entities.Reactive`](https://www.nuget.org/packages/Nullean.Entities.Reactive)
and [`Nullean.Entities.CsCheck`](https://www.nuget.org/packages/Nullean.Entities.CsCheck) attach to
without this package depending on either of them.

## Time: `TimeProvider`, and temporal rules with no Rx

Everything that needs "now" takes a `TimeProvider` (default `TimeProvider.System`), so a test controls the clock instead of sleeping:

```csharp
static readonly Constraint<Reading> Fresh = Constraints<Reading>.NotOlderThan(r => r.At, TimeSpan.FromMinutes(5));
static readonly Constraint<Reading> NotFromTheFuture = Constraints<Reading>.NotInFuture(r => r.At, tolerance: TimeSpan.FromSeconds(2));

var time = new ManualTimeProvider();                      // virtual time, ships in this package
set.Validate(reading, time);                              // Validate, Apply, ApplyDetailed, ApplyOrThrow and ConstrainedValue.Enforce all take one
time.Advance(TimeSpan.FromMinutes(6));                    // moves the clock and fires every timer that falls due, in order
```

A rule that is about **sequences in time** ("a confirmed order ships within 60 seconds", "never ship after cancelling") needs events,
not a single value. `Temporal<TEvent>` builds those rules, with the same operators as `Nullean.Entities.Reactive` and no Rx:

```csharp
var rules = TemporalRuleSet<OrderEvent>.From(
    ("ConfirmedShips", Temporal<OrderEvent>.After<Confirmed>().Then<Shipped>((c, s) => c.OrderId == s.OrderId).Within(TimeSpan.FromSeconds(60))),
    ("NoShipAfterCancel", Temporal<OrderEvent>.Never<Cancelled, Shipped>((c, s) => c.OrderId == s.OrderId)),
    ("EveryOrderCloses", Temporal<OrderEvent>.Eventually<Shipped>(e => e.OrderId, e => e is Placed)));
```

| You have | Use | You get |
|---|---|---|
| A recorded session, audit log or event-store read | `Trace.Check(events, rules)` over `Timestamped<TEvent>` | A `TraceReport`: every violation with its time and evidence, what is still pending |
| A live `IAsyncEnumerable<TEvent>` | `events.MonitorAsync(rules, time)` | Each verdict change as it happens; deadlines run on the provider's timers |
| Events you push yourself | `new TemporalMonitor<TEvent>(rules, time)`, `.Observe(e)`, `.Changes()`, `.Complete()` | The same, without an enumerable |
| A rule you want to run step by step | `rule.CreateMonitor()`, `.Observe(e, at)`, `.Tick(now)`, `.Finish(at)` | The pure state machine behind all of the above |

```csharp
await foreach (var change in orderEvents.MonitorAsync(rules, TimeProvider.System))
{
    if (change.Verdict.IsViolated) alert(change.Rule, change.Verdict.ViolationAt, change.Verdict.ViolationEvidence);
}
```

Two ordering rules are fixed, because they decide edge cases: a deadline is reached **before** an event at the same instant is considered
(so an event arriving exactly at the deadline is too late), and an event **never answers the obligation it opens**. A violation is reported at
the instant of the deadline, not when it was noticed. `Eventually` and `Required()` are only decided at `Finish`/`Complete`, so they are for a
bounded run.

Not rewritten, kept in agreement: `Nullean.Entities.Reactive` implements the same operators over `IObservable<T>` (Rx schedulers, debounce,
windows) and is the right package when the stream is already Rx. A differential test in the CsCheck test project generates timestamped
traces and requires both to give the same verdict and the same violation time for every operator.

## Async: awaiting a mutation, validating a stream

```csharp
// the mutation awaits something; constraints are checked once it returns, the token is passed through
Result<Order> result = await OrderConstraints.All.ApplyAsync(order, async (o, ct) => await shipping.ShipAsync(o, ct));

var value = ConstrainedValue.For(order).Enforce(OrderConstraints.All);
await value.ApplyAsync(async (o, ct) => await pricing.RepriceAsync(o, ct));          // Current changes only if accepted
await foreach (var outcome in value.ApplyAllAsync(commands)) { ... }                 // IAsyncEnumerable<Func<Order, Order>>, in order

await foreach (var (order, violations) in OrderConstraints.All.ValidateAsync(db.ReadOrdersAsync())) { ... }  // validate a cursor without materialising it
```

`ApplyDetailedAsync` returns the three-way `ApplyOutcome<T>` (`Applied`, `Noop`, `Failure`). Constraints themselves stay synchronous: a rule that
has to call a service is better expressed as a mutation that does the call and a constraint on its result.

## State machines as data

A lifecycle is often a picture first. `StateMachine<TState, TTrigger>` declares it as a value (states, triggers, which trigger moves which
state where, optional timeouts) and nothing more: it does not run your entity, it describes it.

```csharp
var machine = StateMachine
    .Define<OrderStatus, string>(OrderStatus.Pending)
    .Permit("Confirm", OrderStatus.Pending,   OrderStatus.Confirmed)
    .Permit("Ship",    OrderStatus.Confirmed, OrderStatus.Shipped)
    .Permit("Cancel",  [OrderStatus.Pending, OrderStatus.Confirmed], OrderStatus.Cancelled)
    .Build();

machine.Fire(OrderStatus.Pending, "Confirm");            // Confirmed; machine.TryFire(...) for "what if", Permitted(state) for "what can I do"
machine.AsConstraint<Order>(o => o.Status);               // the Transitions allow-list, derived from the diagram
machine.ToMermaid();                                      // stateDiagram-v2
StateMachine.FromMermaid(text);                           // and back: a diagram in the docs can be the specification
```

`Nullean.Entities.CsCheck` explores a machine (`machine.Spec().Exhaustive()`) and checks a real entity follows it (`machine.ConformTo(...)`).
`Constraint<T>.Map` / `ConstraintSet<T>.Map` lift constraints through a projection while keeping kind, name, guards and edges.

## Declared type-state machines

There are two ways to declare a machine. [State machines as data](#state-machines-as-data) above (`StateMachine<TState, TTrigger>`) is for states that are values: an enum or a string, moved by named triggers. This one is for domains where each state is its own shape — not just an enum value but a type carrying data —
`StateMachine<TState>` declares the graph once and validates it eagerly at startup:

```csharp
private abstract record OrderState
{
    public sealed record Draft : OrderState;
    public sealed record Placed(int Lines) : OrderState;
    public sealed record Confirmed : OrderState;
    public sealed record Shipped : OrderState;
    public sealed record Cancelled : OrderState;
}

public static readonly StateMachine<OrderState> OrderLifecycle = StateMachine<OrderState>.Define(m => m
    .Initial<OrderState.Draft>(s => s.On("Place").To<OrderState.Placed>())
    .State<OrderState.Placed>(s => s
        .On("Confirm").To<OrderState.Confirmed>(when: p => p.Lines > 0)
        .On("Cancel").To<OrderState.Cancelled>())
    .State<OrderState.Confirmed>(s => s.To<OrderState.Shipped>().On("Cancel").To<OrderState.Cancelled>())
    .Terminal<OrderState.Shipped>()
    .Terminal<OrderState.Cancelled>()
);
```

As written this is a *declaration*: the graph, the guards, the diagram and the tests below. Nothing in it says how to build a `Placed` from a `Draft`, so `Fire` cannot run it
(it says so: "declared but cannot be fired"). Give an edge a factory and the same machine is the thing your service runs:

```csharp
public static readonly StateMachine<OrderState> OrderLifecycle = StateMachine<OrderState>.Define(m => m
    .Initial<OrderState.Draft>(s => s.On("Place").To<OrderState.Placed>(_ => new OrderState.Placed(0)))
    .State<OrderState.Placed>(s => s
        .On<int>("AddLines").To<OrderState.Placed>((p, n) => p with { Lines = p.Lines + n })   // takes an argument
        .On("Confirm").To<OrderState.Confirmed>(_ => new OrderState.Confirmed(), when: p => p.Lines > 0)
        .On("Cancel").To<OrderState.Cancelled>(_ => new OrderState.Cancelled()))
    .State<OrderState.Confirmed>(s => s
        .To<OrderState.Shipped>(_ => new OrderState.Shipped())                                  // unnamed: the trigger is "Shipped"
        .On("Cancel").To<OrderState.Cancelled>(_ => new OrderState.Cancelled()))
    .Terminal<OrderState.Shipped>()
    .Terminal<OrderState.Cancelled>()
);

Result<OrderState> placed = OrderLifecycle.Fire(new OrderState.Draft(), "Place");           // Ok(Placed(0))
OrderLifecycle.Fire(new OrderState.Placed(0), "Confirm");                                    // Fail: "'Confirm' is not permitted for this Placed: its guard rejected it"
OrderLifecycle.Fire(new OrderState.Placed(2), "AddLines", 3);                                // Ok(Placed(5))
OrderLifecycle.Permitted(new OrderState.Placed(2));                                          // ["AddLines", "Confirm", "Cancel"]
OrderLifecycle.Transition(new OrderState.Placed(2), "Confirm");                              // Ok(Transitioned(From, "Confirm", To)): the event to publish
```

`Fire` never mutates anything and never throws for a refused transition: the failure carries the reason (wrong case, guard, missing or wrong argument, no factory) and the state it was
attempted on. The factory runs only after the guard held, so it can assume the data the guard checked.

To fire on the aggregate that holds the state, bind the machine to it. The result is the entity, and with a constraint set the new entity is checked against **every** rule, the
sequential ones included (the machine's own rule is one: a change of state made by hand, outside `Fire`, is refused too):

```csharp
public sealed record Order(Guid Id, OrderState State, string? PaymentRef = null);

var binding = OrderLifecycle.For<Order>(o => o.State, (o, s) => o with { State = s });
var rules   = ConstraintSet.Merge(OrderConstraints.All, ConstraintSet.From(("lifecycle", binding.AsConstraint())));
var service = binding.Enforce(rules);

Result<Order> shipped = service.Fire(order, "Shipped");   // Fail if the order has no PaymentRef and a rule says shipped orders need one
```

`Define` throws `StateMachineDefinitionException` (NESM001–NESM004) if the graph fails exhaustiveness,
reachability, dead-end, or trap checks — caught at startup, not buried in a log at runtime.

From a defined machine you get:

```csharp
OrderLifecycle.AsConstraint()  // Constraint<OrderState> — drops into ConstraintSet.From(...)
OrderLifecycle.AsReactive()    // ReactiveConstraint<OrderState> — checks an event stream
OrderLifecycle.ToMermaid()     // Mermaid diagram — paste into your README or docs
OrderLifecycle.Walks()         // Gen<IReadOnlyList<StateMachineStep<T>>> — CsCheck graph walks
OrderLifecycle.RealizedWalks() // Gen<IReadOnlyList<RealizedStep<T>>> — guard-aware walks with real values
```

For enum-shaped state machines, `Constraints<T>.Transitions(selector).Allow(from, to)` stays the right
tool — `StateMachine<TState>` is for the case where each state carries its own data and a guard needs
to inspect it.

## Severity, locations, and what `Apply` rejects

`constraint.WithSeverity(Severity.Warning)` (or `Hint`) sets the severity of a rule's violations, on any shape. `Validate` returns all of them; `Apply` rejects a change only for **errors**, and the
warnings and hints it let through are on `ApplyOutcome.Applied.Notices`. A violation carries `File`, `Line`, `Column` and `Length`; `Unique`, `References`, `Acyclic`, `NoChains`, `KeysMatch` and the
selector form of `Always` take `at:` (a function from the offending item to a `SourceLocation`) so a finding can point back at the line it came from.

More shapes for the references real models use (`Tree.Map` rebuilds an immutable tree bottom-up, for rewriting one; `Acyclic` is iterative, so a chain a million deep is fine): `KeysMatch(a => a.Repositories, r => r.Name)` (a dictionary keyed by its entity's own name), `Tree.Flatten(roots, n => n.Children)` (every node of a
tree, so `Unique` and `References` run across all levels), and `ConstraintSet.Pair(a, b)` / `ConstraintSet.Merge(a, b)` for a rule over two aggregates and for joining sets. `set.Map(project, prefix: "Catalog.")` lifts a set onto a bigger value: pass a prefix when two lifted sets would otherwise have a rule of the same name (names are how a violation, `Probe` and `Laws` tell rules apart).

## DataAnnotations are constraints too

A type with `[Constraints]` (or `[Constraint]` members) gets one generated constraint for each `[Required]`, `[Range]`, `[StringLength]`, `[MaxLength]`, `[MinLength]` and
`[RegularExpression]` on its properties or primary-constructor parameters, with no opt-in and no declared constraint needed. The model you already annotate becomes a set you can
`Apply`, `Probe` and `Laws`:

```csharp
[Constraints]
public sealed partial record SignUp([Required] string Name, [Range(18, 120)] int Age, [RegularExpression("^[A-Z]{3}-[0-9]{3}$")] string Reference);

SignUp.Constraints.Validate(new SignUp("Ada", 12, "ABC-123"));   // AgeRange, Member = "Age"
```

A companion class picks them up too, even with no rule of its own: `[Constraints<Order>] public static partial class OrderRules;` generates `OrderRules.All` (and `OrderRules.Instance`, `OrderRules.Actions`) from the
annotations on `Order`, plus any `Constraint<Order>` field you add. A plain `[Constraints]` companion that already declares `Constraint<Order>` fields gets them as well. A member about a different type is an error (NECG007).

Names are `<Property><Attribute>` (`NameRequired`, `AgeRange`, `ReferencePattern`); if that collides with a constraint you declared, yours keeps the name and the annotation's gets an `Annotation` suffix.
Each attribute means what DataAnnotations says it means: null passes everything except `[Required]`, an empty string passes `[RegularExpression]`, and a pattern must match the whole string.
The generated code is plain C# with no `Validator` call and no reflection, so it is trim and AOT safe. `[ConstraintIgnore]` on a property leaves its annotations out. Any other validation
attribute (`[Phone]`, a custom one, `IValidatableObject`) is reported as an Info diagnostic, `NECG010`, not skipped silently; write those as a `Constraint<T>`. Cross-field rules, collections and
sequential rules still need constraints: annotations cover one property at a time.

## Rules over collections and graphs

Most of the rules a real domain has are about *several* things at once: two items with one id, a reference to something that is not there, an include that loops.
Four shapes cover them, each reporting every offender with the offending value as `Evidence`, and none of them throws on bad data (a duplicate id
does not break the cycle check):

```csharp
Constraints<Site>.Unique(s => s.Pages, p => p.Url);                                        // no two pages at one URL
Constraints<Site>.References<Mount, string>(s => s.Mounts, m => [m.TocId], s => s.Tocs.Select(t => t.Id));   // every mount names a toc that exists
Constraints<Site>.Acyclic(s => s.Tocs, t => t.Id, t => t.IncludedIds);                      // "a -> b -> a" is the evidence
Constraints<Site>.NoChains(s => s.Redirects, r => r.From, r => r.To);                       // A to B while B goes to C
```

## Reporting violations

A violation carries the rule (`ConstraintId`), a message, the `Evidence`, and, for the selector form of `Always`, the **member** it is about:
`Constraints<Customer>.Always(c => c.Email, e => e.Contains('@'))` reports `Member = "Email"` (read from the selector's text, so there is no expression tree and
it stays trim and AOT safe). That is what a validation response is keyed by. `ApplyOrThrow` and `Result.Unwrap` throw a `ConstraintViolationException` whose
`Violations` are the list itself, not the messages joined into one string. [`docs/integration.md`](https://github.com/nullean/entities/blob/main/docs/integration.md)
shows the ASP.NET Core `ValidationProblem` mapping.

A rule that two sets name differently keeps each name: `ConstraintSet.From` copies a rule that is already named. `Describe("...")` adds prose.

## Entities that mutate in place

`Apply` is built for values that are replaced (records). A class that is changed in place (an EF Core entity, a classic aggregate) has no "before" to compare to:
when the mutation returns the very same object of a type without value equality, `Apply` does not call it a no-op; it runs the point-in-time constraints on the result.
For the sequential rules (`Transitions`, `Monotonic`, `Conserves`) keep a snapshot and use `Check`:

```csharp
var before = Snapshot(order);          // a copy, or the ORM's original values
order.Ship();
var violations = constraints.Check(before, order);
```

## Trim and AOT

`Nullean.Entities` and `Nullean.Entities.Reactive` build with the trim and AOT analyzers on (`IsAotCompatible`), and `./build.sh aot` publishes a small app that uses the generated
constraints, `Apply`, a declared machine with its Mermaid round trip, temporal rules, `TemporalMonitor` and a Reactive constraint set as **NativeAOT**, and runs it.
The `Nullean.Entities.CsCheck` packages are not AOT-compatible: generated data is built by reflection, which is fine in tests.

## Install

```sh
dotnet add package Nullean.Entities
```

## Links

- [Source & full documentation](https://github.com/nullean/entities)
- [Nullean.Entities.Reactive](https://www.nuget.org/packages/Nullean.Entities.Reactive) — lifts a
  `ConstraintSet<T>` onto an `IObservable<TEvent>` stream, plus temporal/structural operators that
  only make sense against a stream.
- License: MIT
