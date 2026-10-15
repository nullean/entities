---
navigation_title: "Testing API"
description: "Everything Nullean.Entities.CsCheck adds to CsCheck beyond AutoTest: shrinkable generators, model-based testing, Script, regression by seed, metamorphic tests, state machine walks, and testing a service."
---

# Testing API: generators, scripts, model-based, metamorphic

## Shrinkable, reproducible generators

Bogus and CsCheck each have their own random source. A `Faker<T>` backed by Bogus's own `Random`
cannot reproduce a specific seed or shrink toward a minimal failing case. This package routes Bogus's
draws through CsCheck's `PCG` — every `Faker<T>` value becomes a real CsCheck generator:

```csharp
using Nullean.Entities.CsCheck;

// Hand-configured, realistic fake data — shrinkable, seed-reproducible
Gen<Order> genOrder = GenFakerExtensions.Faker<Order>(faker => faker
    .CustomInstantiator(f => new Order(
        f.Random.Guid(),
        OrderStatus.Pending,
        f.Make(f.Random.Int(1, 5), () => new OrderLine(
            f.Commerce.Ean13(),
            f.Random.Int(1, 100),
            f.Finance.Amount()
        ))
    ))
);

// Zero-configuration — fills every public member via AutoGen, still PCG-routed
Gen<Customer> genCustomer = GenAuto.Of<Customer>();
```

A failing case shrinks toward the order with the fewest lines and the smallest values — not because
you wrote a shrinker, but because fewer/smaller Bogus draws produce a smaller CsCheck `Size`.

## The constraint set is the assertion

Every test axis that needs a predicate uses `OrderConstraints.All` instead of re-deriving it:

```csharp
// Random — every generated order satisfies its own constraints
genOrder.Sample(o => OrderConstraints.All.Validate(o).Count == 0);

// Confirming an order with lines always succeeds
genOrder.Where(o => o.Lines.Count > 0).Sample(o =>
    OrderConstraints.All.ApplyDetailed(o, x => x.Confirm()) is ApplyOutcome<Order>.Applied
);

// A deliberate mutant — proves the constraint actually fires
var act = () => genOrder.Sample(o =>
    OrderConstraints.All.ApplyDetailed(o, x => x with { Total = x.Total - 1 }) is not ApplyOutcome<Order>.Failure
);
act.Should().Throw<CsCheckException>(); // the mutant is always caught and shrunk
```

## Model-based: the domain record IS the model

```csharp
genOrder.SampleModelBased(
    initial: _ => new Order(Guid.NewGuid(), OrderStatus.Pending, []),
    operations: [
        Gen.Operation<Order>("AddLine", genLine, (model, sut, line) =>
        {
            var nextModel = model.AddLine(line);
            var result = OrderConstraints.All.ApplyDetailed(sut, x => x.AddLine(line));
            // Applied means the SUT matches what the model expects
            return result is ApplyOutcome<Order>.Applied(var next)
                ? (nextModel, next)
                : throw new Exception("Constraint rejected a line addition the model allows.");
        })
    ]
);
```

Because the domain record is the model and the constraint set enforces the rules, you're not
maintaining a parallel assertion — you're using the same spec twice.

## Performance: stable baseline from the constraint set itself

```csharp
genOrder.Faster(
    f1: o => OrderConstraints.All.Validate(o),
    f2: o => o.Lines.Sum(l => l.Quantity * l.Price) >= 0 ? new List<Violation>() : [Violation.Error("negative")]
);
```

## Script: action sequences CsCheck generates and shrinks

```csharp
var script = Script.For<OrderService>()
    .Step("Place", genOrder, (svc, o) => svc.Place(o))
    .Step("AddLine", genLine, (svc, line) => svc.AddLine(activeOrderId, line))
    .Step("Confirm", (svc) => svc.Confirm(activeOrderId))
    .Step("Ship", (svc) => svc.Ship(activeOrderId))
    .Step("Cancel", (svc) => svc.Cancel(activeOrderId))
    .Length(1, 20);
```

`SampleWithAsync` runs the script against a fresh SUT per iteration. The SUT takes a `TimeProvider` (a `ManualTimeProvider` that the script's delays advance),
records timestamped events, and a `TemporalRuleSet<TEvent>` is checked against them. A specification, regression,
parallel, and equality test all share the same script:

```csharp
// Specification — named rules are the assertion
await script.SampleWithAsync(
    time => new OrderService(time),
    svc => svc.Log,         // IEnumerable<Timestamped<OrderEvent>>
    OrderEventRules         // TemporalRuleSet<OrderEvent> — temporal + structural
);

// Parallel — the SUT handles concurrent steps; violations surface as CsCheck failures
script.SampleParallel(
    factory: () => new OrderService(),
    getState: svc => svc.CurrentOrders,
    equal: (state1, state2) => state1.SequenceEqual(state2)
);
```

## Regression: seed replay is free

Every `Sample` / `SampleWithAsync` failure prints a seed. Paste it into a `[Test]` to fix a specific
case:

```csharp
await script.SampleWithAsync(
    time => new OrderService(time),
    svc => svc.Log,
    OrderEventRules,
    seed: "0000000000000001ZZZZZZZZZZZZZZZZ"
);
```

## Metamorphic: two paths, shrinkable pair

```csharp
Script.For<Order>().Metamorphic(
    factory: () => new Order(Guid.NewGuid(), OrderStatus.Pending, []),
    gen: Gen.Select(genLine, genLine, (a, b) => (a, b)),
    path1: (o, pair) => { o.AddLine(pair.a); o.AddLine(pair.b); },
    path2: (o, pair) => { o.AddLine(pair.b); o.AddLine(pair.a); },
    equal: (o1, o2) => OrderConstraints.All.Validate(o1).Count == 0
                    && o1.Total == o2.Total
                    && o1.Lines.ToHashSet().SetEquals(o2.Lines)
);
```

`a` and `b` are generated and shrunk independently every iteration, applied to fresh instances via
`factory`, so a failure isn't "these two fixed lines commute" but "CsCheck found the smallest pair
that doesn't."

## State machine walks

A `StateMachine<TState>` from `Nullean.Entities` produces two walk generators:

```csharp
// Structural walk — follows the declared graph by type, guards ignored
// Good for coverage: did the generator reach Terminal states?
OrderLifecycle.Walks().Sample(steps =>
{
    var reached = steps.Select(s => s.To.Name).ToHashSet();
    return reached.Contains("Shipped") || reached.Contains("Cancelled");
});

// Guard-aware walk — generates a real value at every step via AutoGen, evaluates guards
// A guarded edge is only taken when its guard holds for the generated source value
OrderLifecycle.RealizedWalks().Sample(steps =>
{
    foreach (var step in steps)
    {
        var result = OrderConstraints.All.ApplyDetailed(step.From, _ => step.To);
        if (result is ApplyOutcome<OrderState>.Failure(_, _, var violations))
            throw new Exception(violations[0].Message);
    }
    return true;
});
```

`Walks()` is for structural coverage — "does the graph have paths to all terminal states." `RealizedWalks()`
is for exercising guards: edges with `when:` predicates are skipped when the predicate fails on the
generated source value, so a walk is always a valid path through the machine.

## Testing a service

A service holds a repository, a clock and the entity's state, and its operations are async. Two checks drive it without Rx or an event log; both run on CsCheck's `Sample`, so a failure is a shrunk sequence with a seed.

```csharp
// 1. By the state it exposes: after every step, check the rules on (before, after).
await Script.For<OrderService>()
    .StepAsync("Place",   s => s.PlaceAsync())
    .StepAsync("Confirm", s => s.ConfirmAsync())
    .StepAsync("Ship",    s => s.ShipAsync())
    .Length(1, 12)
    .SampleStateAsync(clock => new OrderService(clock), s => s.State, ConstraintSet.From(("lifecycle", OrderLifecycle.AsConstraint())));

// 2. Against the declared machine: generate its triggers, run them on the service, and compare with what the machine predicts.
await OrderLifecycle.ConformsToAsync(clock => new OrderService(clock), s => s.State, (s, trigger) => trigger switch
{
    "Place" => s.PlaceAsync(), "Confirm" => s.ConfirmAsync(), "Ship" => s.ShipAsync(), "Cancel" => s.CancelAsync(),
    _ => throw new InvalidOperationException(trigger),
});
```

- `SampleStateAsync` reads the state after each step and runs the `ConstraintSet` on it, so an invariant (`Always`) and a rule about changes (`Transitions`, `Monotonic`, `Conserves`) both count. A step that leaves the state unchanged, because the service refused it, is not a transition: only the invariants are asked.
- `ConformsToAsync` is the stronger one. After each trigger the service must be where the machine says: the next state when `Fire` accepts it, the *same* state when it refuses (a guard, a case the trigger is not declared for). It finds a service that ships an unconfirmed order and one that refuses a transition the machine allows. Triggers that take an argument are not explored; the machine cannot predict what an argument builds.
- Operations are really awaited (nothing blocks), but a run must be repeatable for a failure to replay, so use a service whose awaits complete by themselves (an in-memory repository, a `ManualTimeProvider`), not a real database or HTTP call. The clock passed to the factory is advanced by `Delays(...)`.
- For a value machine (`StateMachine<TState, TTrigger>`), `ConformToServiceAsync(create, stateOf, bindings: [("Open", d => d.OpenAsync()), ...])` walks the machine's triggers (permitted and refused) through async bindings on a fresh service per walk.

## Simple property tests

No state machine needed. The constraints are the oracle and AutoBogus supplies the values; CsCheck shrinks any failure
and prints a seed.

```csharp
OrderConstraints.All.ForAll(o => o.Cancel().Cancel() == o.Cancel());     // a property over valid orders
OrderConstraints.All.Preserves(OrderConstraints.Actions);                // every action keeps a valid order valid
OrderConstraints.All.Probe();                                            // which constraints can this generator break?
OrderConstraints.All.RejectsInvalid();                                   // invalid values are named and rejected
```

| Call | Checks | Use it when |
|---|---|---|
| `ForAll(property)` | `property` holds for every generated value that satisfies the constraints | You have a rule of your own (`Total == sum of lines`) and want valid inputs for free. |
| `Preserves(actions)` | From a valid start, any action with any generated argument gives a result that satisfies the constraints. The action runs **raw**, not through `Apply`, which would reject a bad result and so could never show one | You want the random cousin of `Exhaustive` that also starts from states `New()` never produces. |
| `Probe()` | Per point-in-time constraint, how often it held and was violated over 1,000 generated values | A constraint that **never fails** may be a tautology or a derived value; one that **never holds** is a contradiction (or something the generator cannot build) and throws. |
| `RejectsInvalid()` | Values violating a constraint appear in `Validate` under its name, and `Apply` to them is refused naming it | You want proof every rule is named and enforced. Constraints the generator never violates are listed as unfalsifiable. |
| `Commands.Over(make, values...)`, `Commands.FromDomain(make)` | A list of commands for `EventSourced`/`FromReducer`: one per value of an argument, or one per value of its default domain (the same plausible values and edges an action argument gets) | A command carries a value (`Pay(50m)`) and you want the amounts explored, not just the ones you thought of. |
| `Laws(gen, counterExamples)` (obsolete: use `AutoTest.For(set, gen).CounterExamples(...)`) | The same laws over **generated** data: a few hundred values from a fixed seed (`samples`, `seed`) break no rule (a failure prints the value and the rules, the model and its rules disagreeing); every rule is **reachable** (its counter-example, applied to the first `derivedFrom` generated values, breaks it for at least one); and a counter-example derived from any generated value breaks only its own rule. A counter-example that changes nothing for some value is skipped for it. | The constraint set is checked against what the model can generate rather than one value you wrote. Use `GenAuto.Of<T>(AutoMode.Plausible, o => ...)` with `Reference`, `Identity` and `Normalize` declarations for what a loaded value promises. |
| `Laws(valid, counterExamples)` (obsolete: use `AutoTest.For(set, Gen.Const(valid))...`) | Four laws in one line: a valid value breaks nothing; every rule has a name and names are unique; every rule has a **counter-example** (yours, or generated from a single-property edge value that only that rule rejects); each counter-example breaks **only its own rule** | You want a constraint set to be trustworthy: no rule that can never fire, no two rules that always fire together. It reports every failure at once and tells you when two rules overlap so a single cause cannot be isolated. |

`ForAll` and `Preserves` take `gen:`/`start:` to replace the default (AutoBogus, kept only if valid). For types whose
valid values are hard to hit by filtering, use the entities the actions can reach instead:

```csharp
var orders = OrderConstraints.Instance.Spec().States();
OrderConstraints.All.ForAll(o => o.Total >= 0, gen: orders);
```

And to test a factory or parser against the rules the domain already declares:

```csharp
Gen.Int[1, 10].Select(n => Build(n)).ShouldSatisfy(OrderConstraints.All);    // fails naming the violated constraints, shrunk
```
