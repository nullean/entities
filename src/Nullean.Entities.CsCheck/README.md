# Nullean.Entities.CsCheck

Two things, both [CsCheck](https://github.com/AnthonyLloyd/CsCheck) integration: driving a
`Script<TSut>` action sequence and checking a
[`Nullean.Entities.Reactive`](https://www.nuget.org/packages/Nullean.Entities.Reactive)
`ReactiveConstraintSet<TEvent>` against the resulting event stream — and Bogus/AutoBogus-backed
`Gen<T>` generators for realistic fake data, reproducible and shrinkable because their draws go
through CsCheck's own `PCG` instead of Bogus's default `Random`. The two share a package because
they're the two ways this repo drives CsCheck; nothing below requires using both together.

## New to property testing?

A normal test picks inputs by hand and checks the output. A **property test** states a rule that
must hold for *every* input, then lets a library generate hundreds of inputs hunting for one that
breaks it. [CsCheck](https://github.com/AnthonyLloyd/CsCheck) is that library, and it adds three
things worth knowing:

- **Shrinking.** When it finds a failure it keeps simplifying the input until it is the smallest one
  that still fails, so you get "Place, Ship" instead of a 40-step walk.
- **Seeds.** Every failure prints a seed. Pass it back (`seed: "..."`) and the exact failing run replays.
- **Virtual time.** Under this package, delays between steps advance a fake clock instead of sleeping,
  so a 72-hour deadline tests in milliseconds.

The rules here are the **constraints you already declared** on your domain (`Nullean.Entities`,
`Nullean.Entities.Reactive`). You do not write assertions. You describe what the system *can do*
(a `Script<TSut>` of steps) and hand over the constraints. The package does the rest:

```csharp
// 1. Steps the system can take. Arguments are generated (and shrunk) each time a step is picked.
var script = Script.For<OrderService>()
    .Step("Place",   OrderIds, (svc, id) => svc.Place(id))
    .Step("Confirm", OrderIds, (svc, id) => svc.Confirm(id))
    .Step("Ship",    OrderIds, (svc, id) => svc.Ship(id));

// 2. Run random walks of those steps and check every constraint on the resulting events.
script.SampleWith(_ => new OrderService(), svc => svc.Events, OrderStateSpec.All);
```

`OrderIds` is a small pool of IDs (see the next section for why it must be small). If a walk
violates a constraint, the test fails with the shrunk walk and the seed.

### Why the ID pool matters

`Gen.Guid` looks like the obvious choice for an ID, and it makes this test pass without testing
anything: a random GUID never matches an order that `Place` created, so `Confirm` and `Ship` are
silent no-ops and `NeverShippedWithoutConfirmation` is never exercised. Use a handful of IDs so steps
collide:

```csharp
static readonly Gen<Guid> OrderIds = Gen.Int[1, 3].Select(i => new Guid(i, 0, 0, new byte[8]));
```

That a green test can prove nothing is the most common property-testing mistake, so the package
checks for it. See [Vacuity](#vacuity--a-green-test-that-proved-nothing).

## One line per test style

If your entity is an immutable record whose methods return the next value (`order.Confirm()`), you
declared everything a test needs when you wrote its constraints. `[Constraints]` also generates
`OrderConstraints.Actions` (every public method on `Order` that returns `Order` and takes at most one
argument), and `.Spec(...)` hands both to CsCheck's state-machine engine:

```csharp
var spec = OrderConstraints.All
    .Spec(Order.New(id), OrderConstraints.Actions)
    .Domain(lineA, lineB)            // the values to try for AddLine(OrderLine)
    .Domain("A", "B")                // ...and for RemoveLine(string sku)
    .Boundary(o => o.Lines.Count < 2)
    .Key(o => (o.Status, string.Join(",", o.Lines.Select(l => l.Sku))));   // what makes two orders "the same state"
```

| You want to know | One line | What CsCheck does |
|---|---|---|
| Can any sequence of actions break a constraint? | `spec.Exhaustive()` | Visits **every** reachable state and step. Passing is a proof, not a sample. Failing gives the shortest counterexample. |
| Did that proof exercise anything? | (built into `Exhaustive`) | Fails if a `When` guard is never true or an `Allow`ed transition is never taken. |
| The state space is too big to close | `spec.Sample()` | Random walks, shrunk to the shortest failing walk. |
| What does the lifecycle look like? | `spec.Mermaid()` | The reachable state graph as a Mermaid diagram. |
| Did behaviour change by accident? | `spec.Snapshot(expected)` | Hashes the state graph (pass `0`, copy the value from the failure). |
| Are my constraints strong enough? | `spec.Faults()` | Mutation testing: breaks each action in turn; a break nothing catches is a missing constraint. |
| Does a real implementation match the entity? | `spec.Conform(() => new Repo(), (r, step) => r.Apply(step), (r, expected) => r.Get() == expected)` | Same walks drive both, compared after every step. |
| Does the order of two actions matter? | `spec.Commutes("AddLine", "AddLine")` | From every reachable state, for every argument pair. |
| Is an action repeatable? | `spec.Idempotent("Cancel")` | `f(f(x)) == f(x)` from every reachable state. |
| Does a rule hold for one action only? | `.StepScoped("AddLine", OrderStepConstraints.AddLineStep)` | Attaches e.g. `Monotonic` to that action instead of the whole type. |
| Does my factory/parser only produce valid values? | `gen.ShouldSatisfy(OrderConstraints.All)` | Generates, validates, shrinks to the smallest invalid value. |
| Valid test data for another test | `gen.Valid(OrderConstraints.All)` | A generator filtered by the constraints. |
| Is a service race-free? | `script.SampleParallelWith(factory, svc => svc.Events, spec)` | Runs steps concurrently; constraints must hold however they interleave. |

How the constraints become requirements:

| Constraint | Checked as |
|---|---|
| `Always`, `Check`, `Idempotent`, `NotInFuture`, `NotOlderThan` | holds in every reachable state |
| `Transitions`, `Monotonic`, `Conserves`, `CheckTransition` | holds over every step (a step that changes nothing is skipped, exactly as `Apply` treats a `Noop`) |
| `When(guard).Requires(...)` | holds whenever the guard is true, **and** the guard must be true somewhere |
| `Transitions(selector).Allow(a, b)` | every other change is rejected, **and** each listed edge must be taken somewhere |

Things to know:

- **A finite domain is the price of a proof.** `Exhaustive` tries each action with each value you list
  for its argument parameter type, so use two or three representative values, not `Gen.Int`.
  An action that takes an argument and has no `.Domain<T>(...)` fails with a message naming the call to add.
- **`Key` and `Boundary` make the space close.** Records holding lists compare by reference, so without
  a `Key` every state looks new. A counter that can grow forever needs a `Boundary`. If the space does
  not close, `Exhaustive` says so rather than passing: reaching `maxStates` proves nothing.
- **Time is fixed.** Time-aware constraints read a constant clock (`.Clock(...)` to change it), so
  exploration is deterministic.
- **It finds real bugs in your rules.** Running this against the example domains showed that
  `Transitions(...).Allow(...)` was rejecting mutations that left the state alone (adding a line to a
  `Pending` order), and that `BankAccount.Withdraw` never checks the overdraft limit.
- **More than one argument.** The generator skips methods with two or more parameters; add them with
  `.Action("Transfer", account => ...)`.

Runnable versions of all of the above: `tests/Nullean.Entities.Examples.Tests/AutoTests/AutoTestExamples.cs`
and `tests/Nullean.Entities.CsCheck.Tests/EntitySpecTests.cs`.

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
| Vacuity, sampled | `SampleWith(..., Coverage)` | `Coverage<TEvent>.Expect` | Built — this package |
| Vacuity, proved | `EntitySpec.Exhaustive()` | `When` guards, `Allow` edges, `Reachable` | Built — for entities with pure actions (see above) |
| Liveness, unbounded | `Sample` | `Eventually<T>` | Built — `Nullean.Entities.Reactive` |
| Idempotency | `Sample` | `Idempotent(action)` | Built — `Nullean.Entities` |
| Monotonicity | `Sample`, step-scoped | `Monotonic(selector, direction)` | Built — `Nullean.Entities` |
| Conservation | `Sample`, pair-valued | `Conserves(selector)` | Built — `Nullean.Entities` |
| Confluence | `Metamorphic` | spec supplies `equal` | Built — `Script<TSut>.Metamorphic` |

### Absent — a constraint already catches an illegal ordering

```csharp
var script = Script.For<OrderService>()
    .Step("Place", OrderIds, (svc, id) => svc.Place(id))
    .Step("Confirm", OrderIds, (svc, id) => svc.Confirm(id))
    .Step("Ship", OrderIds, (svc, id) => svc.Ship(id));

script.SampleWith(_ => new OrderService(), svc => svc.Events, OrderStateSpec.All);
```

`NeverShippedWithoutConfirmation` on `OrderStateSpec` is the whole assertion — nothing
CsCheck-specific to write beyond the script's own steps.

### Temporal — the same walk, with generated delays

Deadlines ("a confirmed order ships or is cancelled within 60 seconds", "a retry waits at least 2 seconds") are
where hand-written tests are weakest: you pick one delay and hope. Add `.Delays(...)` and CsCheck
picks them, including ones you would not have thought to try:

```csharp
// The rule, declared once on the domain (Nullean.Entities.Reactive):
public static readonly ReactiveConstraint<OrderStateEvent> ConfirmedEventuallyTerminates =
    After<OrderStateEvent.Confirmed>()
        .Then<OrderStateEvent.Shipped, OrderStateEvent.Cancelled>(
            m1: (confirmed, shipped) => shipped.OrderId == confirmed.OrderId,
            m2: (confirmed, cancelled) => cancelled.OrderId == confirmed.OrderId)
        .Within(TimeSpan.FromSeconds(60));

// The test: the same steps, plus a generated pause before every step.
var script = Script.For<OrderService>()
    .Step("Place", OrderIds, (svc, id) => svc.Place(id))
    .Step("Confirm", OrderIds, (svc, id) => svc.Confirm(id))
    .Step("Ship", OrderIds, (svc, id) => svc.Ship(id))
    .Delays(Gen.Int[0, 72 * 3600].Select(s => TimeSpan.FromSeconds(s)))
    .Length(2, 12);

script.SampleWith(scheduler => new OrderService(scheduler), svc => svc.Events, OrderStateSpec.All);
```

What happens under the hood:

- **The clock is virtual.** `SampleWith` hands your factory an Rx `HistoricalScheduler`. Before each
  step it advances that scheduler by the step's generated delay, so a 72-hour gap costs nothing.
- **Your SUT must use that scheduler for time.** Pass it to anything that waits or timestamps
  (`Observable.Timer(..., scheduler)`, `scheduler.Now`). A SUT that calls `DateTime.UtcNow` or
  `Task.Delay` is invisible to virtual time and the deadline can never be missed. Time-aware
  constraints from the core package (`NotInFuture`, `NotOlderThan`, `Always((x, now) => ...)`) read the
  same virtual clock when attached with `ToReactive()`.
- **Delays shrink.** A failure reports the smallest gap that still breaks the deadline, e.g. 61
  seconds rather than 41 hours.
- **A pending deadline at the end of a run is not a violation.** Delays apply *before* each step, never
  after the last one, so an obligation still open when the walk ends stays `Pending`. If you want
  the final deadline checked, end the script with a step whose pre-delay is long enough to cross it
  (a no-op `Step("Wait", _ => { })` works) or use `Eventually<T>` for unbounded liveness.
- **Pick a delay range that straddles the deadline.** `Gen.Int[0, 5]` against a 60-second deadline
  can never fail, which is a vacuous test again. `Coverage` (next section) is how you catch it.

### Vacuity — a green test that proved nothing

A constraint like `NeverShippedWithoutConfirmation` passes trivially if no run ever ships. The
same goes for `After<Confirmed>().Then<Shipped, Cancelled>().Within(60s)` when no run ever confirms. Green
means "no violation found", which is only evidence if the interesting events happened. Name the
events that must show up and `SampleWith` fails the test when they do not:

```csharp
var coverage = new Coverage<OrderStateEvent>()
    .Expect("Confirmed", e => e is OrderStateEvent.Confirmed)                  // seen in at least one run
    .Expect("Shipped",   e => e is OrderStateEvent.Shipped, minPercentOfRuns: 5) // and in 5%+ of runs
    .Expect("Cancelled", e => e is OrderStateEvent.Cancelled);

script.SampleWith(
    scheduler => new OrderService(scheduler),
    svc => svc.Events,
    OrderStateSpec.All,
    coverage,
    writeLine: Console.WriteLine
);
```

`writeLine` prints how often each event occurred:

```
Coverage over 100 runs:
  Confirmed                          61 runs (61.0%)
  Shipped                            23 runs (23.0%)
  Cancelled                          34 runs (34.0%)
```

If an expectation is not met the test throws `VacuousPropertyException` naming it, even though no
constraint was violated:

```
The sample passed vacuously: "Shipped" never occurred.
Widen the script (more steps, a longer Length, different Delays) or fix the generator so these events are reachable.
```

Rules of thumb:

- Add one `Expect` for the **trigger** of every temporal constraint (`Confirmed` for
  `After<Confirmed>()...`) and one for the **thing it forbids or promises** (`Shipped`).
- Use `minPercentOfRuns` when rare is not good enough. A deadline test that crosses the deadline in
  2% of runs is thin evidence.
- A real violation still fails as a normal CsCheck failure with a shrunk walk and seed. Coverage
  is only checked once the sample otherwise passes.
- Coverage is skipped when you pass `seed:`. Replaying one seed is a single run, too few to judge.
- This is "not seen in N runs", not "provably unreachable". Proving a state unreachable needs an
  exhaustive search over a pure state machine, which is a separate design (see the note at the end
  of this section).

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
> Entities with pure actions get the exhaustive, provable mode too — see "One line per test style"
> above. A real, possibly-impure SUT driven through events (everything else in this section) still
> goes through `Script<TSut>`.

## `Script<TSut>`

`Script<TSut>` describes an action sequence CsCheck can generate, shrink, and replay:

```csharp
var script = Script.For<OrderService>()
    .Step("Place", Gen.Guid, (svc, id) => svc.Place(id))
    .Step("Place", OrderIds, (svc, id) => svc.Place(id))
    .Step("Confirm", OrderIds, (svc, id) => svc.Confirm(id))
    .Step("Ship", OrderIds, (svc, id) => svc.Ship(id))
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
- [CsCheck](https://github.com/AnthonyLloyd/CsCheck) — the property-testing library underneath:
  generators, shrinking, seeds.
- [Nullean.Entities.Reactive](https://www.nuget.org/packages/Nullean.Entities.Reactive) — the
  constraint sets `Script<TSut>` will check against a generated run.
- License: MIT
