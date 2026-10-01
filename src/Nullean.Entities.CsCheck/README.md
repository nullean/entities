# Nullean.Entities.CsCheck

Two things, both [CsCheck](https://github.com/AnthonyLloyd/CsCheck) integration: driving a
`Script<TSut>` action sequence and checking a
[`Nullean.Entities.Reactive`](https://www.nuget.org/packages/Nullean.Entities.Reactive)
`ReactiveConstraintSet<TEvent>` against the resulting event stream — and Bogus/AutoBogus-backed
`Gen<T>` generators for realistic fake data, reproducible and shrinkable because their draws go
through CsCheck's own `PCG` instead of Bogus's default `Random`. The two share a package because
they're the two ways this repo drives CsCheck; nothing below requires using both together.

## Testing qualities, not just assertions

Every CsCheck testing style still needs something to assert. The usual answer is a fresh predicate
per test, rewritten again for whichever sampler you reach for next. Here it's the other way round:
one named, quoted constraint projects onto whichever sampler you pick, so a domain author who has
never heard of model-based, metamorphic, or parallel testing still gets them, for the cost of a
different terminal call on the same spec.

| Quality | CsCheck sampler | Primitive | Status |
|---|---|---|---|
| Absent | `Sample` | `Never<T1,T2>` | Built — `Nullean.Entities.Reactive` |
| Temporal | `Sample` | `After().Then().Within()` | Built — `Nullean.Entities.Reactive` |
| Vacancy, weak | `Sample` + `classify` | coverage table | Built — CsCheck's own `classify` |
| Vacancy, proved | `Spec.Exhaustive`/`Reachable` | — | Needs a pure state machine; separate topic |
| Liveness, unbounded | `Sample` | `Eventually<T>` | Built — `Nullean.Entities.Reactive` |
| Idempotency | `Sample` | `Idempotent(action)` | Built — `Nullean.Entities` |
| Monotonicity | `Sample`, step-scoped | `Monotonic(selector, direction)` | Built — `Nullean.Entities` |
| Conservation | `Sample`, pair-valued | `Conserves(selector)` | Built — `Nullean.Entities` |
| Confluence | `Metamorphic` | spec supplies `equal` | Built — `Script<TSut>.Metamorphic` |

### Absent — a constraint already catches an illegal ordering

```csharp
var script = Script.For<OrderService>()
    .Step("Confirm", Gen.Guid, (svc, id) => svc.Confirm(id))
    .Step("Ship", Gen.Guid, (svc, id) => svc.Ship(id));

script.SampleWith(_ => new OrderService(), svc => svc.Events, OrderStateSpec.All);
```

`NeverShippedWithoutConfirmation` on `OrderStateSpec` is the whole assertion — nothing
CsCheck-specific to write beyond the script's own steps.

### Temporal — the same walk, with generated delays

```csharp
var script = Script.For<OrderService>()
    .Step("Confirm", Gen.Guid, (svc, id) => svc.Confirm(id))
    .Step("Ship", Gen.Guid, (svc, id) => svc.Ship(id))
    .Delays(Gen.Int[0, 72 * 3600].Select(s => TimeSpan.FromSeconds(s)));

script.SampleWith(_ => new OrderService(), svc => svc.Events, OrderStateSpec.All);
```

Generated delays eventually land past the 60-second deadline `ConfirmedEventuallyTerminates` allows
and shrink to the smallest violating gap.

### Vacancy — coverage today, proof later

`Script<TSut>`/`SampleWith` drive CsCheck's boolean `Sample` overload — the string-`classify`
overload needs a `Gen<T>` built by hand instead, so vacancy checking builds its own generator rather
than going through the script:

```csharp
var seenOutcomes = new HashSet<string>();

GenSteps.Sample(
    steps =>
    {
        using var service = new OrderService();
        var reached = new HashSet<string>();
        using var subscription = service.Events.Subscribe(e => reached.Add(e switch
        {
            OrderEvent.Shipped => "Shipped",
            OrderEvent.Cancelled => "Cancelled",
            _ => null!,
        }));

        Apply(service, steps); // Place/AddLine/Confirm/Ship/Cancel over a small pool of order IDs

        var classification = reached.Count == 0 ? "neither" : string.Join("+", reached.OrderBy(x => x, StringComparer.Ordinal));
        seenOutcomes.Add(classification);
        return classification;
    },
    writeLine: Console.WriteLine
);

seenOutcomes.Should().Contain("Shipped");
seenOutcomes.Should().Contain("Cancelled");
```

The coverage table (from `writeLine`) shows whether `Shipped`/`Cancelled` were reached at all
across the sample — that is what makes a constraint like `NeverShippedWithoutConfirmation` meaningful
rather than vacuously true because the interesting case never arose. That's "not seen in N runs,"
not "provably unreachable" — a real finding either way, but proving unreachability needs
`Reachable`/`Exhaustive` against a pure state machine, which is a separate, explicit design (see the
reminder at the end of this section). Full runnable version:
`examples/Nullean.Entities.CsCheck.Examples/OrderDomainExamples.cs`'s `OrderCoverageExamples`.

### Liveness — unconditional, unlike `After().Then().Within()`

```csharp
Constraints<OrderEvent>.Eventually<OrderEvent.Shipped>(
    scope: e => e.OrderId, from: e => e is OrderEvent.Placed,
    "Every placed order eventually ships."
);
```

`After().Then().Within()` needs a bounded trigger. This is unconditional — "did every instance we
opened eventually close" — which means tracking an open set of pending IDs across the whole walk
rather than a sliding window. It only resolves on the stream's `OnCompleted` (unbounded liveness is
undecidable mid-stream), so it's for a bounded run — a CsCheck-generated walk, a recorded session —
not a live production feed, which never completes.

### Idempotency — names the quality instead of hiding it in a predicate

```csharp
[Constraint]
public static readonly Constraint<Order> CancelIsIdempotent =
    Constraints<Order>.Idempotent(o => o.Cancel(), "Cancelling twice must leave the order unchanged.");
```

`Idempotent(action)` applies `action` once and twice from the value being checked, and asserts the
two results are equal — the standard `f(f(x)) == f(x)` law. Point-in-time: `Cancel()` itself supplies
both "once" and "twice", so `Validate` is enough, no mutation needed. The point of a dedicated shape
over an arbitrary `Always(...)`: the failure message says "not idempotent," not the name of an
unrelated-looking rule.

### Monotonicity — step-scoped, not global

```csharp
[Constraint]
public static readonly Constraint<Order> TotalNeverDecreasesUnderAddLine =
    Constraints<Order>.Monotonic(o => o.Total, MonotonicDirection.NonDecreasing);
```

This one differs from everything else here: it's `Sequential`, so it only ever fires from `Apply`
against the specific mutation it's checking, never from `Validate` — which is exactly what makes it
safe to hold across a walk restricted to `AddLine` steps without also attaching it to the type's
whole `Constraints` set. Add a `RemoveLine` step to the same script and it should correctly fail, so
a `Monotonic` constraint declares which steps it applies to by which `Constraint<T>` set it's put in,
not by living in the spec's global set.

### Conservation — the first pair-valued shape

```csharp
[Constraint]
public static readonly Constraint<(Order From, Order To)> LineTransferConservesTotal =
    Constraints<(Order, Order)>.Conserves(p => p.From.Total + p.To.Total,
        "Moving a line must not create or destroy value.");
```

`ConstraintSet<T>` today is single-`T`. Conservation needs a tuple or an explicit before/after
pair — structurally closer to a rule with a `when`/`then` shape than to `Always`. Like `Monotonic`,
it's `Sequential` (by equality rather than ordering), so it only fires from `Apply`.

### Confluence — two paths, compared on fresh instances

```csharp
Script.For<Order>().Metamorphic(
    factory: () => new Order(),
    gen: Gen.Line,
    path1: (o, a, b) => { o.AddLine(a); o.AddLine(b); },
    path2: (o, a, b) => { o.AddLine(b); o.AddLine(a); },
    equal: (o1, o2) => o1.Total == o2.Total && o1.Lines.ToHashSet().SetEquals(o2.Lines)
);
```

`a` and `b` are generated (and shrunk) independently every iteration, each applied to its own fresh
instance via `factory`, so a failure isn't "these two fixed lines commute" but "CsCheck found a pair
that doesn't" — shrunk down to the smallest such pair, the same as every other sampler here.

> Every quality in the table above has a real implementation with passing tests, not a sketch.
> `Idempotent`, `Monotonic` (step-scoped), `Conserves` (pair-valued), and `Eventually` (unbounded
> liveness) land in `Nullean.Entities`/`Nullean.Entities.Reactive`; `Script<TSut>`,
> `SampleWith`/`RunWith`, and `Metamorphic` land here.
>
> **Not yet designed:** declaring an exhaustive, provable state machine (`Spec.Exhaustive`/`Faults`/
> `Reachable`/`Mermaid`) against Nullean entities. It needs a pure `Apply(state, event) -> state`,
> which is a different authoring mode from "drive a real, possibly-impure SUT directly" that
> everything above assumes — a separate design, not a refinement of this one.

## `Script<TSut>`

`Script<TSut>` describes an action sequence CsCheck can generate, shrink, and replay:

```csharp
var script = Script.For<OrderService>()
    .Step("Place", Gen.Guid, (svc, id) => svc.Place(id))
    .Step("Confirm", Gen.Guid, (svc, id) => svc.Confirm(id))
    .Step("Ship", Gen.Guid, (svc, id) => svc.Ship(id))
    .Delays(Gen.Int[0, 500].Select(ms => TimeSpan.FromMilliseconds(ms)))
    .Length(1, 20); // default 0–10 if omitted
```

`Step(name, action)` is a fixed, argument-free step; `Step<TArg>(gen, action)` (no explicit name,
derived from `TArg`'s type and printed value) or `Step<TArg>(name, gen, action)` generates a fresh,
independently-shrinkable argument every time the step is picked. `Delays` draws a `TimeSpan` before
every step — with none configured, steps run back-to-back with no virtual time advance. Each
generated run is a fresh SUT (via the caller's factory) and a shrinkable array of `(delay, step)`
pairs, chosen by `Gen.OneOf` among the declared steps — the same shape `SampleModelBased` itself
generates operations with, just without its dual-model bookkeeping, which this package doesn't need
(see below).

`SampleWith` runs the script against a fresh SUT instance per iteration and checks a constraint set
against the SUT's own event stream:

```csharp
script.SampleWith(
    scheduler => new OrderService(scheduler),
    svc => svc.Events,
    OrderStateSpec.All // a ReactiveConstraintSet<OrderStateEvent>
);
```

If the event type implements `IHaveConstraints<TEvent>` (via a `ReactiveConstraintSet` obtained from
`ConstraintSet<T>.ToReactive()`), the constraint set argument can be dropped. `RunWith` is the same
idea but returns the whole run — `IterationStarted`, `ConstraintResolved`, `SmallerFailure`,
`Completed` — as an `IObservable<EntityEvent>` for a caller that wants to observe the run itself
rather than only its pass/fail outcome. It runs one real `Gen<T>.Sample` call under the hood (so
generation, shrinking and seed replay are all genuine CsCheck behaviour, not a re-implementation of
it) and, on failure, replays the already-shrunk seed once more to read back which named constraints
were actually violated for `ConstraintResolved`/`SmallerFailure` — it does not emit a
`ConstraintResolved` per iteration while sampling is in progress, because `Sample` doesn't expose a
per-iteration hook to observe from outside.

There is no hand-rolled shrinking anywhere in this file: `SampleWith`/`RunWith`/`Metamorphic` all
build a real `Gen<T>` (a fresh SUT plus generated data) and hand it to `Gen<T>.Sample` — the
`equal`/predicate is just `!monitored.AnyViolated`, since the "model" a model-based test would
otherwise compare against is exactly the attached `MonitoredConstraintSet<TEvent>`'s own state.
CsCheck owns every other part: generation, shrinking, multithreading, and seed-based replay.

## Bogus/AutoBogus generators

Bogus and CsCheck each have their own random source. Left alone, a Bogus-backed `Gen<T>` can't
replay a specific seed or shrink toward a minimal failing case, because CsCheck never sees the draws
that produced the value. This package makes Bogus draw from CsCheck's `PCG` instead of its own
`Random`, so every `Faker<T>`/`AutoFaker` value becomes a real CsCheck generator: reproducible by
seed, and shrinkable by draw count and magnitude.

`GenFakerExtensions.Faker<T>` wraps a `Faker<T>` configuration in a `Gen<T>`:

```csharp
using global::Bogus;
using Nullean.Entities.CsCheck;

Gen<Order> genOrder = GenFakerExtensions.Faker<Order>(faker => faker
    .CustomInstantiator(f => new Order(f.Random.Guid()))
    .RuleFor(o => o.Email, f => f.Internet.Email())
    .RuleFor(o => o.Lines, f => f.Make(f.Random.Int(0, 10), () => new OrderLine
    {
        Sku = f.Commerce.Ean13(),
        Quantity = f.Random.Int(1, 100),
        Price = f.Finance.Amount(),
    }))
);

genOrder.Sample(order => order.Total.Should().Be(order.Lines.Sum(l => l.Quantity * l.Price)));
```

A failing case shrinks toward the order with the fewest lines and the smallest values, because
fewer/smaller Bogus draws produce a smaller CsCheck `Size`.

`GenAuto.Of<T>` fills every public member automatically via `AutoFaker`, still routed through the
same `PCG` — useful for types where hand-writing every `RuleFor` isn't worth it:

```csharp
Gen<Customer> genCustomer = GenAuto.Of<Customer>();

genCustomer.Sample(c => c.Email.Should().NotBeNullOrWhiteSpace());
// Nested objects (Address, etc.) are populated too — no configuration needed.
```

Pass a `configure` delegate to `GenAuto.Of` to add overrides or skip rules on the underlying
`AutoFaker`, same as configuring `AutoFaker` directly.

`Script<TSut>.Step<TArg>(Gen<TArg>, ...)` takes either kind of generator directly for a step's
argument — the two halves of this package compose, not just coexist.

### What's routed through the PCG, and why it matters

- One `Faker<T>`/`AutoFaker` per thread, created on first use and reused — Bogus takes a global lock
  on every draw, so this only avoids repeated setup, not the lock itself.
- `configure` must be pure: it runs once per thread, not once per sample. Don't capture mutable
  state you intend to change per call.
- Only draws that represent real domain data count toward `Size` — a flag or a value that's always
  the same regardless of the input doesn't shrink meaningfully, so it isn't recorded.
- **Routing `Random` through the PCG is not the whole story.** `Faker.IndexFaker`/`IndexVariable`
  are separate public counters Bogus increments on every `Generate()` call, independent of
  `Random` entirely — and since the same `Faker<T>`/`AutoFaker` is reused for every draw on a
  thread, a rule reading `f.IndexFaker` would otherwise drift across unrelated draws regardless of
  the PCG state, silently breaking reproducibility for any `configure` that happens to use the
  well-known Bogus idiom of `f.IndexFaker` as a "the Nth record" sequence. Both are reset to zero
  before every draw to close that gap — at the cost of `f.IndexFaker` no longer producing a useful
  sequence, which was never going to survive shrink-and-replay anyway (shrinking retries draws out
  of call order, not sequence position). `f.IndexGlobal`/`f.UniqueIndex`/`Faker.GlobalUniqueIndex`
  remain open: they're process-wide statics Bogus itself owns, shared with every other `Faker` in
  the process, so resetting them here would corrupt unrelated code instead of fixing this
  generator's — avoid them in `configure` if you need the reproducibility/shrink guarantee to hold.

## Install

```sh
dotnet add package Nullean.Entities.CsCheck
```

## Links

- [Source & full documentation](https://github.com/nullean/entities) — see `AGENTS.md` for what's
  built and what's still open here.
- [Nullean.Entities.Reactive](https://www.nuget.org/packages/Nullean.Entities.Reactive) — the
  constraint sets `Script<TSut>` will check against a generated run.
- License: MIT
