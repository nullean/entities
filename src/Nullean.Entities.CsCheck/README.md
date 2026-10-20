# Nullean.Entities.CsCheck

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.CsCheck.svg)](https://www.nuget.org/packages/Nullean.Entities.CsCheck)

**Declare the rules of your domain type once, and get tests you did not write: a retry that double-charges, a version that goes backwards and a deadline nobody met are each caught by a named test with the smallest failing case and a seed that replays it.**

Without this package you write a generator for every type and a property for every rule, and the tests cover the rules you thought of. With it, `GenAuto` builds data from the shape of the type (with hostile values such as blank strings, `NaN`, undefined enums and duplicate ids mixed in), and the shape of each rule you declare picks the test, on nine axes (invariant, idempotency, monotonicity, conservation, absence, temporal, liveness, confluence, vacancy). A test that never reached its rule fails as vacuous instead of passing.

It builds on [CsCheck](https://github.com/AnthonyLloyd/CsCheck), which this package does not replace, and on [`Nullean.Entities`](https://www.nuget.org/packages/Nullean.Entities), where the rules (`Constraint<T>`) live. It is a test-time package: it uses reflection and is not trim or AOT clean, by design. It does not depend on System.Reactive. If your service is built on `IObservable<T>`, add [`Nullean.Entities.CsCheck.Reactive`](https://www.nuget.org/packages/Nullean.Entities.CsCheck.Reactive).

## Install

```bash
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.CsCheck   # tests only
```

The packages target `net10.0` and `net11.0`. `Nullean.Entities.CsCheck` brings `CsCheck` 4.9.1 and `Bogus` 35.6.5 with it. The examples below use [TUnit](https://tunit.dev) for the test attributes, and any test framework works because every axis is also a plain `Task`.

## Get going

An order, two rules about it, and one bug. `Nullean.Entities` supplies `[Constraints]` and `Constraint<T>`.

```csharp
using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;

public enum OrderStatus { Pending, Confirmed, Cancelled }

public sealed record Order(OrderStatus Status, int Version)
{
	public static Order New() => new(OrderStatus.Pending, 0);

	public Order Confirm() => Status == OrderStatus.Pending ? this with { Status = OrderStatus.Confirmed, Version = Version + 1 } : this;

	// The bug: cancelling an already cancelled order bumps the version again.
	public Order Cancel() => this with { Status = OrderStatus.Cancelled, Version = Version + 1 };
}

// The rules, declared once next to the type. [Constraints] gathers every static field into OrderConstraints.All.
[Constraints<Order>]
public static partial class OrderConstraints
{
	public static readonly Constraint<Order> CancelIsRepeatable = Constraints<Order>.Idempotent(o => o.Cancel());

	public static readonly Constraint<Order> VersionNeverGoesBack = Constraints<Order>.Monotonic(o => o.Version, MonotonicDirection.NonDecreasing);
}

public class OrderTests
{
	private static readonly Gen<Order> Orders = Gen.Select(Gen.Int[0, 2], Gen.Int[0, 5], (status, version) => new Order((OrderStatus)status, version));

	// One test per axis the rules declare
	public static IEnumerable<Func<AxisCase>> Axes() => AutoTest.For(OrderConstraints.Instance, Orders).AsDataSource();

	[Test, MethodDataSource(nameof(Axes))]
	public Task EveryDeclaredAxisHolds(AxisCase axis) => axis.RunAsync();
}
```

`AutoTest.For(OrderConstraints.Instance, Orders)` reads the rules and finds the operations of `Order` itself (every public method that returns an `Order`). Run it. `ReportAsync()` returns the same result as text for a console. The idempotency test fails and the other axes report what they did:

```text
Invariant: not applicable (no rule declared on this axis)
Idempotency failed.

Rule broken   cancel-is-repeatable: doing it twice is not the same as doing it once.
Started from  Order { Status = Pending, Version = 0 }
Once gave     Order { Status = Cancelled, Version = 1 }
Twice gave    Order { Status = Cancelled, Version = 2 }

Law           Doing an operation twice gives what doing it once gives, for every generated value.
Consequence   A retry, a double submit or a re-run changes the state a second time, so it ends up different from a single run.

Smallest case found, after 1 shrink. To run this case again: .Seed("5gGXNkqJNXB1")
Monotonicity: passed (1 rule: version-never-goes-back; 200 samples) (moved a value in 133 of 200 runs)
Conservation: not applicable (no rule declared on this axis)
Confluence: not applicable (no rule declared on this axis)
Vacancy: passed (2 rules: cancel-is-repeatable, version-never-goes-back; 200 samples) (1 counter-example found by search; AutoTest.CounterExampleReport() lists them)
```

You wrote no property. The rule's shape (`Idempotent`) chose the test, the failure names the rule, shows the value before and after, and gives the smallest case. `.Seed("...")` on the `AutoTest` replays exactly that case while you fix the bug. An axis with no rule is reported as not applicable and never as a silent pass.

### The main value: a test that proves it ran

Suppose the generator only made pending orders and you had a rule about confirmed ones. Every rule would hold and every plain property would pass. This package fails it:

```text
Vacancy: the test passed without proving anything.

Rule                   confirmed-orders-have-lines
Not exercised because  its guard was never true for the 200 generated values, so the rule held without being exercised.
Next                   give the auto-test starts where the guard is true (for example a generator that sometimes draws the case the guard names), or mix such values into it.

Law                    Every rule can fire, and every rule's guard is reached by the data the tests use.
Consequence            A rule that can never fire, or a test that never reached it, reports green while checking nothing.
```

The same check covers time. Declare "a confirmed order ships within a minute" and `EventAutoTest` drives your service through random calls with delays chosen from your deadlines, on a virtual clock, with no real waiting. This example uses the `Fulfilment` service and `FulfilmentEvent` types from the [Quickstart tests](https://github.com/nullean/entities/blob/main/tests/Nullean.Entities.Quickstart.Tests/BugGalleryTests.cs). The service takes a `TimeProvider` and records `Timestamped<FulfilmentEvent>` entries:

```csharp
var rules = TemporalRuleSet<FulfilmentEvent>.From(
	("confirmed-ships-within-a-minute",
		Temporal<FulfilmentEvent>.AfterUntil(
			e => e is FulfilmentEvent.Confirmed,
			(_, next) => next is FulfilmentEvent.Shipped,
			TimeSpan.FromSeconds(60),
			isInterrupt: e => e is FulfilmentEvent.Cancelled)));

await EventAutoTest.For(
	rules,
	Script.For<Fulfilment>().Step("Confirm", f => f.Confirm()).Step("Cancel", f => f.Cancel()).Length(1, 4),
	time => new Fulfilment(time, shipsAfter: TimeSpan.FromSeconds(90)),
	f => f.Log
).RunAsync();
```

A service that ships after 90 seconds fails with:

```text
Temporal failed.

Rule broken      confirmed-ships-within-a-minute
Noticed at       +60s
What happened    No matching event within 00:01:00 after Confirmed
Events recorded  +0s  Confirmed
                 +90s  Shipped
Calls made       wait 1s, Confirm

Law              What must follow something within a deadline does, however the time between calls is spent.
Consequence      A deadline is missed under timing the tests never tried, so a stuck state is reported as in progress for good.

Smallest case found, after 5 shrinks. To run this case again: .Seed("0i1vmP3ChLc5")
```

## What you get

### Test a type from its rules (`AutoTest`)

| API | Why you would use it |
|---|---|
| `AutoTest.For(rules, gen)`, `AutoTest.For(companion, gen)`, `AutoTest.For<T>(gen)` | Start from a `ConstraintSet<T>`, a `[Constraints]` companion, or a type that implements `IHaveConstraints<T>` |
| `.RunAsync()`, `.ReportAsync()`, `.AsDataSource()`, `.Cases` | Throw on any failing axis, read the result, or give your framework one test per axis |
| `.InvariantAsync()`, `.IdempotencyAsync()`, `.MonotonicityAsync()`, `.ConservationAsync()`, `.ConfluenceAsync()`, `.VacancyAsync()` | Run one axis |
| `.Action(name, apply)`, `.Action<TArg>(name, apply, arguments)` | Add an operation the type does not expose as a public method |
| `.CounterExample(rule, breakIt)`, `.CounterExamples(...)`, `.CounterExampleReport()` | Show each rule can fire, and list which rules got a counter-example |
| `.Samples(n)`, `.Seed(s)`, `.Data(mode)`, `.Options(...)`, `.Output(writeLine)`, `.RequireAxes(...)` | Tune samples, replay a seed, choose data, and turn "not applicable" into a failure |
| `.Deeper(...)` | Prove every rule over every reachable state (`Exhaustive`) and mutation-test the rules (`Faults`) when the state space closes |

### Test a service or a class that changes in place

| API | Why you would use it |
|---|---|
| `AutoTest.For(rules, () => new Service())` and `AutoTest.For(rules, clock => new Service(clock))` | The same axes on a mutable class, with a fresh instance per run |
| `.Mutate(...)`, `.MutateAsync(...)`, `.Steps(min, max)` | Register operations (awaited, nothing blocks) and apply several in a row |
| `Script.For<TSut>()` with `.Step(...)`, `.StepAsync(...)`, `.Delays(...)`, `.Length(min, max)` | Describe generated call sequences that CsCheck shrinks |
| `script.SampleStateAsync(...)`, `machine.ConformsToAsync(...)`, `machine.ConformToServiceAsync(...)` | Check a service by the state it exposes, or against a declared state machine, with no Rx |

### Test events and time

| API | Why you would use it |
|---|---|
| `EventAutoTest.For(rules, script, factory, log)`, `EventAutoTest.ForAsync(...)` | The event axes (absence, temporal, liveness) on a virtual clock, from a list or an `IAsyncEnumerable` |
| `script.SampleWithAsync(factory, log, rules)` | Check core temporal rules against a recorded run, with an optional `Coverage<TEvent>` |
| `new Coverage<TEvent>().Expect(name, matches)` | Fail with `VacuousPropertyException` when an event you care about never occurred |

### Generate data

| API | Why you would use it |
|---|---|
| `GenAuto.Of<T>(mode, configure)` | A `Gen<T>` from the shape of `T`: constructors, properties, collections, enums, nullability, foreign keys and DataAnnotations |
| `AutoMode.Mixed` (default), `Plausible`, `Adversarial` | Mostly realistic with about one leaf in four an edge value, realistic only, or edge values only |
| `AutoOptions`: `Use<T>(gen)`, `AddStringEdges(...)`, `EdgePercent`, `MaxDepth`, `MaxCollection`, `References`, `Identity`, `Normalize` | Override a type, add edge strings, and declare relations the generator cannot guess |
| `GenFakerExtensions.Faker<T>(...)` | Your own Bogus rules as a shrinkable, seed-reproducible `Gen<T>` (draws go through CsCheck's `PCG`) |

```csharp
GenAuto.Of<Order>()                          // Mixed: mostly plausible, about one leaf in four an edge value
GenAuto.Of<Order>(AutoMode.Plausible)        // what a demo would show
GenAuto.Of<Order>(AutoMode.Adversarial)      // edge values only
GenAuto.Of<Order>(AutoMode.Mixed, o => o.Use(Gen.Const(OrderStatus.Pending)).AddStringEdges("N/A"))
```

### Properties, boundaries and models

| API | Why you would use it |
|---|---|
| `rules.ForAll(property)`, `rules.Preserves(actions)` | Your own property over valid values; every action keeps a valid value valid |
| `rules.Probe()`, `rules.RejectsInvalid()`, `rules.Boundaries(...)` | Find rules that never fail or never hold, prove every rule is named and enforced, and list hostile values nothing rejects |
| `gen.Valid(rules)`, `gen.ShouldSatisfy(rules)` | Keep only valid values, or test a factory against the rules the domain declares |
| `script.Metamorphic(factory, gen, path1, path2, equal)` | Two paths with two generated values, applied to fresh instances, must agree (confluence) |
| `machine.Walks()`, `machine.RealizedWalks()` | Generated walks through a `StateMachine<TState>`, structural or with guards evaluated on drawn values |
| `rules.Spec(...)` (`EntitySpec<T>`) | `Exhaustive`, `Sample`, `Mermaid`, `Faults`, `Conform` and `Commutes` as one-line tests |

## Pitfalls and limits

- **The generator is yours to get right.** `GenAuto` cannot guess a relation between members or what makes a whole structure valid. A generator that never reaches a rule is reported by the Vacancy axis, and you fix it.
- **Hostile data finds hostile bugs.** Zero-config tries `Reserve(-1)`, an undefined enum and `decimal.MaxValue`. For a domain you have not hardened, start with `.Data(AutoMode.Plausible)` and treat the rest as a to-do list.
- **Services must be deterministic.** In-place operations are replayed on a fresh instance to keep the "before", so the factory and the steps must reach the same state every time. Use in-memory fakes and a `TimeProvider`, not a real database or HTTP call, or a seed cannot replay.
- **A service reads time only from its `TimeProvider`.** A call to `DateTime.UtcNow` or `Task.Delay` is invisible to the virtual clock, so a missed deadline never happens in a test.
- **Some counter-examples need a person.** The search finds one for most rules, not all.
- **Some axes are not run on in-place classes.** Idempotency of an in-place operation and custom `CheckTransition` rules are not checked on a service; state them over the state record instead.
- **Not trim or AOT clean.** The generators use reflection. Run these in a test project and keep production code on `Nullean.Entities`.
- **Rx steps are synchronous.** `Nullean.Entities.CsCheck.Reactive` runs on a scheduler, and a `StepAsync` step there is rejected with a pointer to the `TimeProvider` terminals.
- **Bogus takes a process-wide lock per draw.** `GenAuto.Of<T>` avoids Bogus at runtime for that reason. `Faker<T>` rules you write run one faker per thread.

## Next

- [Getting started](https://entities.nullean.net/getting-started/): five minutes, one order, one red test
- [Why](https://entities.nullean.net/why/) and [concepts in plain words](https://entities.nullean.net/concepts/)
- [The nine axes](https://entities.nullean.net/axes/) and [what this adds to CsCheck](https://entities.nullean.net/why-not/cscheck/), with the same tests written both ways
- [Testing API](https://entities.nullean.net/reference/testing-api/), [generated data](https://entities.nullean.net/reference/generated-data/) and [state machines](https://entities.nullean.net/reference/state-machines/)
- [Services and mutable classes](https://entities.nullean.net/guides/services/), [time and events](https://entities.nullean.net/guides/time/) and [adopting in an existing codebase](https://entities.nullean.net/guides/adopting/)
- [Quickstart example](https://github.com/nullean/entities/tree/main/examples/Nullean.Entities.Quickstart) and [source](https://github.com/nullean/entities)
- Related packages: [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities), [Nullean.Entities.Reactive](https://www.nuget.org/packages/Nullean.Entities.Reactive), [Nullean.Entities.CsCheck.Reactive](https://www.nuget.org/packages/Nullean.Entities.CsCheck.Reactive)
- License: MIT
