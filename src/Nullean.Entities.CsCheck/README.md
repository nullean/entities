# Nullean.Entities.CsCheck

Eight CsCheck testing styles — random, model-based, metamorphic, performance, specification,
regression, parallel, and equality — driven by the same named constraint sets you already declared
in [`Nullean.Entities`](https://www.nuget.org/packages/Nullean.Entities), plus shrinkable Bogus
generators so your fake data reproduces and shrinks the same way CsCheck's own primitives do.

## What plain CsCheck looks like first

Here is a realistic Order domain tested with plain CsCheck across the eight axes. The friction
points are annotated — they're what this package removes.

```csharp
// The domain
public sealed record OrderLine(string Sku, int Quantity, decimal Price);
public sealed record Order(Guid Id, OrderStatus Status, IReadOnlyList<OrderLine> Lines)
{
    public decimal Total => Lines.Sum(l => l.Quantity * l.Price);
    public Order AddLine(OrderLine line) => this with { Lines = [.. Lines, line] };
    public Order Confirm() => this with { Status = OrderStatus.Confirmed };
    public Order Cancel() => this with { Status = OrderStatus.Cancelled };
}

// ── Random testing ───────────────────────────────────────────────────────────
// Pain: you write a generator by hand and repeat the predicate for every test.
Gen<OrderLine> genLine = Gen.Select(
    Gen.String["ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"].Where(s => s.Length > 0),
    Gen.Int[1, 100],
    Gen.Decimal[0.01m, 999.99m],
    (sku, qty, price) => new OrderLine(sku, qty, price)
);
Gen<Order> genOrder = Gen.Select(genLine.Array[1, 5],
    lines => new Order(Guid.NewGuid(), OrderStatus.Pending, lines));

genOrder.Sample(order => order.Total == order.Lines.Sum(l => l.Quantity * l.Price));

// ── Model-based testing ──────────────────────────────────────────────────────
// Pain: the model is usually just the domain record — you're duplicating the check.
genOrder.SampleModelBased(
    initial: _ => (new Order(Guid.NewGuid(), OrderStatus.Pending, []), new Order(Guid.NewGuid(), OrderStatus.Pending, [])),
    operations: [
        Gen.Operation<Order>(genLine, (model, o, line) =>
        {
            var nextSut = o.AddLine(line);
            var nextModel = model.AddLine(line);
            if (nextSut.Total != nextModel.Total)
                throw new Exception($"Total mismatch: sut={nextSut.Total} model={nextModel.Total}");
            return (nextModel, nextSut);
        })
    ]
);

// ── Metamorphic testing ──────────────────────────────────────────────────────
// Pain: two orderings are hand-wired, equal is an ad-hoc delegate.
genLine.Metamorphic(
    factory: () => new Order(Guid.NewGuid(), OrderStatus.Pending, []),
    gen: Gen.Select(genLine, genLine, (a, b) => (a, b)),
    path1: (o, pair) => { o = o.AddLine(pair.a); o = o.AddLine(pair.b); return o; },
    path2: (o, pair) => { o = o.AddLine(pair.b); o = o.AddLine(pair.a); return o; },
    equal: (o1, o2) => o1.Total == o2.Total && o1.Lines.Count == o2.Lines.Count
);

// ── Performance testing ──────────────────────────────────────────────────────
// Pain: you supply the baseline yourself; every refactor means re-tuning the baseline.
genOrder.Faster(
    f1: o => o.Lines.Sum(l => l.Quantity * l.Price),
    f2: o => o.Total
);

// ── Specification / Regression testing ──────────────────────────────────────
// Pain: seed is a stringly-typed string you copy from a failure and paste into a [Test].
genOrder.Sample(
    o => o.Total >= 0,
    seed: "0000000000000001ZZZZZZZZZZZZZZZZ" // CsCheck seed from a previous failure
);

// ── Parallel testing ─────────────────────────────────────────────────────────
// Pain: the SUT needs a lock; the violation message is "did not return consistent results".
var inventory = new Dictionary<string, int> { ["SKU-1"] = 100 };
var _lock = new object();
genLine.SampleParallel(
    initial: () => new Dictionary<string, int> { ["SKU-1"] = 100 },
    op1: (inv, line) => { lock (_lock) inv[line.Sku] = inv.GetValueOrDefault(line.Sku) + line.Quantity; },
    op2: (inv, line) => { lock (_lock) inv[line.Sku] = inv.GetValueOrDefault(line.Sku) - line.Quantity; },
    equal: (inv1, inv2) => inv1.All(kv => inv2.TryGetValue(kv.Key, out var v) && v == kv.Value)
);

// ── Equality testing ─────────────────────────────────────────────────────────
// Pain: equal is always a one-off delegate; there's no shared definition of "same order".
Gen.Select(genOrder, genOrder).Sample((a, b) =>
{
    var combined1 = a.Lines.Concat(b.Lines).ToList();
    var combined2 = b.Lines.Concat(a.Lines).ToList();
    return combined1.Sum(l => l.Quantity * l.Price) == combined2.Sum(l => l.Quantity * l.Price);
});
```

The pattern that emerges: every test re-derives the same predicate logic, every generator is
hand-rolled with the same charset/range choices, failures don't name which rule was violated, and
Bogus data doesn't shrink because Bogus's `Random` isn't CsCheck's `PCG`.

## With Nullean.Entities.CsCheck

### Shrinkable, reproducible generators

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

// Zero-configuration — fills every public member via AutoFaker, still PCG-routed
Gen<Customer> genCustomer = GenAuto.Of<Customer>();
```

A failing case shrinks toward the order with the fewest lines and the smallest values — not because
you wrote a shrinker, but because fewer/smaller Bogus draws produce a smaller CsCheck `Size`.

### The constraint set is the assertion

Every test axis that needs a predicate uses `OrderConstraints.All` instead of re-deriving it:

```csharp
// Random — every generated order satisfies its own constraints
genOrder.Sample(o => OrderConstraints.All.Validate(o).Count == 0);

// Confirming an order with lines always succeeds
genOrder.Where(o => o.Lines.Count > 0).Sample(o =>
    OrderConstraints.All.Apply(o, x => x.Confirm()) is ChannelResult<Order>.Applied
);

// A deliberate mutant — proves the constraint actually fires
var act = () => genOrder.Sample(o =>
    OrderConstraints.All.Apply(o, x => x with { Total = x.Total - 1 }) is not ChannelResult<Order>.Failure
);
act.Should().Throw<CsCheckException>(); // the mutant is always caught and shrunk
```

### Model-based: the domain record IS the model

```csharp
genOrder.SampleModelBased(
    initial: _ => new Order(Guid.NewGuid(), OrderStatus.Pending, []),
    operations: [
        Gen.Operation<Order>("AddLine", genLine, (model, sut, line) =>
        {
            var nextModel = model.AddLine(line);
            var result = OrderConstraints.All.Apply(sut, x => x.AddLine(line));
            // Applied means the SUT matches what the model expects
            return result is ChannelResult<Order>.Applied(var next)
                ? (nextModel, next)
                : throw new Exception("Constraint rejected a line addition the model allows.");
        })
    ]
);
```

Because the domain record is the model and the constraint set enforces the rules, you're not
maintaining a parallel assertion — you're using the same spec twice.

### Performance: stable baseline from the constraint set itself

```csharp
genOrder.Faster(
    f1: o => OrderConstraints.All.Validate(o),
    f2: o => o.Lines.Sum(l => l.Quantity * l.Price) >= 0 ? new List<Violation>() : [Violation.Error("negative")]
);
```

### Script: action sequences CsCheck generates and shrinks

```csharp
var script = Script.For<OrderService>()
    .Step("Place", genOrder, (svc, o) => svc.Place(o))
    .Step("AddLine", genLine, (svc, line) => svc.AddLine(activeOrderId, line))
    .Step("Confirm", (svc) => svc.Confirm(activeOrderId))
    .Step("Ship", (svc) => svc.Ship(activeOrderId))
    .Step("Cancel", (svc) => svc.Cancel(activeOrderId))
    .Length(1, 20);
```

`SampleWith` runs the script against a fresh SUT per iteration and checks a
`ReactiveConstraintSet<TEvent>` against the resulting event stream. A specification, regression,
parallel, and equality test all share the same script:

```csharp
// Specification — named rules are the assertion
script.SampleWith(
    scheduler => new OrderService(scheduler),
    svc => svc.Events,
    OrderEventSpec.All  // ReactiveConstraintSet<OrderEvent> — temporal + structural
);

// Parallel — the SUT handles concurrent steps; violations surface as CsCheck failures
script.SampleParallel(
    factory: () => new OrderService(),
    getState: svc => svc.CurrentOrders,
    equal: (state1, state2) => state1.SequenceEqual(state2)
);
```

### Regression: seed replay is free

Every `Sample` / `SampleWith` failure prints a seed. Paste it into a `[Test]` to fix a specific
case:

```csharp
script.SampleWith(
    _ => new OrderService(),
    svc => svc.Events,
    OrderEventSpec.All,
    seed: "0000000000000001ZZZZZZZZZZZZZZZZ"
);
```

### Metamorphic: two paths, shrinkable pair

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

### State machine walks

A `StateMachine<TState>` from `Nullean.Entities` produces two walk generators:

```csharp
// Structural walk — follows the declared graph by type, guards ignored
// Good for coverage: did the generator reach Terminal states?
OrderLifecycle.Walks().Sample(steps =>
{
    var reached = steps.Select(s => s.To.Name).ToHashSet();
    return reached.Contains("Shipped") || reached.Contains("Cancelled");
});

// Guard-aware walk — generates a real value at every step via AutoFaker, evaluates guards
// A guarded edge is only taken when its guard holds for the generated source value
OrderLifecycle.RealizedWalks().Sample(steps =>
{
    foreach (var step in steps)
    {
        var result = OrderConstraints.All.Apply(step.From, _ => step.To);
        if (result is ChannelResult<OrderState>.Failure(_, var violations))
            throw new Exception(violations[0].Message);
    }
    return true;
});
```

`Walks()` is for structural coverage — "does the graph have paths to all terminal states." `RealizedWalks()`
is for exercising guards: edges with `when:` predicates are skipped when the predicate fails on the
generated source value, so a walk is always a valid path through the machine.

## Testing qualities — all eight axes

| Quality | CsCheck sampler | How this package helps |
|---|---|---|
| **Random** | `Gen.Sample` | `Faker<T>` / `GenAuto.Of<T>` — shrinkable, seed-reproducible, no charset hand-wiring |
| **Model-based** | `SampleModelBased` | domain record is the model; constraint set is the assertion |
| **Metamorphic** | `Metamorphic` | `Script.For<T>.Metamorphic` — two paths over fresh instances, shrinkable pair |
| **Performance** | `Faster` / `SampleTime` | same `Gen<T>` drives both baselines |
| **Specification** | `SampleWith(spec)` | `ReactiveConstraintSet` — named temporal + structural rules |
| **Regression** | seed replay | CsCheck `Seed` — paste a failure seed into a `[Test]` |
| **Parallel** | `SampleParallel` | same script, same generators, concurrent execution |
| **Equality** | `equal:` delegate | `Script<TSut>` confluence and conservation use the constraint set as `equal` |
| State machine walks | `machine.Walks()` | structural, guard-free — covers the declared graph by type |
| Guard-aware walks | `machine.RealizedWalks()` | real AutoFaker values, guards evaluated — only valid paths generated |

## What's routed through the PCG, and why it matters

- One `Faker<T>` / `AutoFaker` per thread, created on first use and reused.
- `configure` must be pure — it runs once per thread, not once per `Sample`.
- `f.IndexFaker` / `f.IndexVariable` are reset to zero before every draw. Bogus increments these
  independently of `Random`, so leaving them alone would silently break reproducibility for any
  `configure` that uses `f.IndexFaker` as a sequence counter.
- `f.IndexGlobal` / `f.UniqueIndex` / `Faker.GlobalUniqueIndex` are process-wide statics — resetting
  them would corrupt unrelated code. Avoid them in `configure` if reproducibility must hold.

## Install

```sh
dotnet add package Nullean.Entities.CsCheck
```

## Links

- [Source & full documentation](https://github.com/nullean/entities)
- [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities) — constraint sets, `ChannelResult<T>`, state machines
- [Nullean.Entities.Reactive](https://www.nuget.org/packages/Nullean.Entities.Reactive) — the event
  specs `Script<TSut>.SampleWith` checks
- License: MIT
