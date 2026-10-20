# Nullean.Entities.CsCheck.Reactive

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.CsCheck.Reactive.svg)](https://www.nuget.org/packages/Nullean.Entities.CsCheck.Reactive)

**Find the missed deadline and the out-of-order event in a service built on `IObservable<T>`, without waiting for real time: CsCheck generates the calls and the delays, and hands you the shortest failing sequence and a seed that replays it.**

A service that emits events on Rx timers is hard to test. A 48-hour deadline cannot be waited out, and the interleavings that break a rule are the ones you did not write. This package runs a generated `Script<TSut>` against your service on a virtual Rx clock (a `HistoricalScheduler` that the generated delays advance), and checks a set of [`Nullean.Entities.Reactive`](https://www.nuget.org/packages/Nullean.Entities.Reactive) rules against the events it emitted.

It sits on [`Nullean.Entities.CsCheck`](https://www.nuget.org/packages/Nullean.Entities.CsCheck) (the `Script<TSut>` and the generators) and `Nullean.Entities.Reactive` (the rules), and it is opt-in. The default CsCheck package has no `System.Reactive` dependency, and a service that records timestamped events or exposes its state is tested without it (`EventAutoTest`, `Script.SampleStateAsync`). Reach for this package when the service is built on observables. It is a test-time package and is not trim or AOT clean (the CsCheck package it builds on uses reflection).

## Install

```bash
dotnet add package Nullean.Entities.CsCheck.Reactive   # tests only
```

The package targets `net10.0` and `net11.0`. It brings `Nullean.Entities.CsCheck`, `Nullean.Entities.Reactive` and `System.Reactive` 7.0.0 with it.

## Get going

A shipment service confirms an order and ships it later. The rule says it ships within 60 seconds. The service has a bug: it ships after 90. The service takes an Rx `IScheduler` and uses it for every delay, which is what lets the test control time.

```csharp
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Reactive;
using static Nullean.Entities.Reactive.Constraints<ShipmentEvent>;

var shipsWithinAMinute = ReactiveConstraintSet.From(
	("confirmed-ships-within-a-minute",
		After<ShipmentEvent.Confirmed>()
			.Then<ShipmentEvent.Shipped>()
			.Within(TimeSpan.FromSeconds(60))
			.Required())
);

var script = Script
	.For<Shipments>()
	.Step("Confirm", s => s.Confirm())
	.Delays(Gen.Int[0, 120].Select(seconds => TimeSpan.FromSeconds(seconds)))
	.Length(1, 5);

script.SampleWith(scheduler => new Shipments(scheduler), s => s.Events, shipsWithinAMinute);

public abstract record ShipmentEvent
{
	public sealed record Confirmed : ShipmentEvent;

	public sealed record Shipped : ShipmentEvent;
}

public sealed class Shipments(IScheduler scheduler)
{
	private readonly Subject<ShipmentEvent> _events = new();

	public IObservable<ShipmentEvent> Events => _events;

	public void Confirm()
	{
		_events.OnNext(new ShipmentEvent.Confirmed());
		// The bug: ships after 90 seconds, not 60. The delay runs on the scheduler the test passes in.
		Observable.Timer(TimeSpan.FromSeconds(90), scheduler).Subscribe(_ => _events.OnNext(new ShipmentEvent.Shipped()));
	}
}
```

`SampleWith` throws a `CsCheckException` that carries the seed and the shortest walk it found. Nothing waited: the 90 seconds are virtual.

```text
CsCheck.CsCheckException: Set seed: "4Oi0_ySUefcc" or -e CsCheck_Seed=4Oi0_ySUefcc to reproduce (2 shrinks, 57 skipped, 100 total).
[+00:00:28] Confirm -> [+00:01:10] Confirm
```

Read the second line as the shrunk walk: advance 28 seconds, call `Confirm`, advance 70 more seconds, call `Confirm` again. By then the first `Confirm` has waited longer than 60 seconds with no `Shipped`, so the rule is violated. Pass the seed back as `seed: "..."` (or set `CsCheck_Seed`) to replay exactly that run while you fix the service.

### Make sure the run reached the rules

A run that never produced the events you care about proves nothing. Add a `Coverage<TEvent>`, and the sample fails with `VacuousPropertyException` if an expected event never occurred:

```csharp
var coverage = new Coverage<ShipmentEvent>()
	.Expect("Confirmed", e => e is ShipmentEvent.Confirmed)
	.Expect("Shipped", e => e is ShipmentEvent.Shipped);

script.SampleWith(scheduler => new Shipments(scheduler), s => s.Events, shipsWithinAMinute, coverage);
```

`Expect(name, matches, minPercentOfRuns)` also takes a minimum share of runs that must contain the event. A real rule violation still surfaces as a CsCheck failure, not a vacuity failure.

## What you get

| Method | Why you would use it |
|---|---|
| `script.SampleWith(factory, events, constraints)` | Runs generated steps on an Rx `HistoricalScheduler` that the generated delays advance, and fails on any violation with a shrunk walk and a seed |
| `script.SampleWith(factory, events, constraints, coverage)` | The same, plus checks that the run reached the rules (a rule that never fired fails as vacuous) |
| `script.SampleParallelWith(factory, events, constraints)` | Runs the same steps concurrently and fails if an interleaving breaks a constraint that no sequential order does (a race). Delays are not used |
| `script.RunWith(factory, events, constraints)` | Runs the script and returns the whole run as an `IObservable<EntityEvent>` (`IterationStarted`, `ConstraintResolved`, `SmallerFailure`, `Completed`), so you can assert on the play-by-play instead of catching an exception |

`factory` is `Func<IScheduler, TSut>`: build your service around the scheduler it is given. `events` is `Func<TSut, IObservable<TEvent>>`. `constraints` is a `ReactiveConstraintSet<TEvent>`, for example the `All` that `[Constraints]` generates (see `Nullean.Entities.Reactive`). If the event type implements `IHaveConstraints<TEvent>`, you can drop the `constraints` argument of `SampleWith` and `RunWith`. Every terminal takes `iter` (iterations) and `seed`.

## Pitfalls and limits

- **Your service must take the scheduler for time.** Use `Observable.Timer(..., scheduler)` and `scheduler.Now`. A service that calls `Task.Delay` or `DateTime.UtcNow` cannot be driven by the virtual clock, and a missed deadline never happens in the test.
- **Steps are synchronous.** The scheduler drives time, so an `await` inside a step has nothing to wait on. A step written with `StepAsync` is rejected with a pointer to the `TimeProvider`-based terminals (`SampleWithAsync`, `EventAutoTest`) in `Nullean.Entities.CsCheck`.
- **The script needs steps.** A script with no `Step(...)` throws `InvalidOperationException`.
- **A delay comes before its step, never after the last.** A deadline that only expires after the final step is not reached, so give the script enough length for the deadline to pass. Run with `.Required()` and a `Coverage` to see that the interesting events happened.
- **`SampleParallelWith` tests your thread safety.** The service must tolerate concurrent calls and raise events in a thread-safe way, which is exactly what it checks. An unsynchronised `Subject<T>` fails for reasons that are not your rules.
- **`RunWith` reports the final failure only.** It emits `ConstraintResolved` for the shrunk failing case (replayed from the seed), not for every iteration.
- **Name clash.** In a test namespace nested under `Nullean.Entities`, the synchronous `Constraints<T>` is found first. Use `using static Nullean.Entities.Reactive.Constraints<TEvent>;` with an alias as in the Reactive package.

## Next

- Docs: [Rx and observable streams](https://entities.nullean.net/reference/rx/) and [time and deadlines](https://entities.nullean.net/guides/time/), for the `TimeProvider` route that needs no Rx
- Source examples: [order state and retry examples](https://github.com/nullean/entities/tree/main/examples/Nullean.Entities.Reactive.Examples)
- [Nullean.Entities.Reactive](https://www.nuget.org/packages/Nullean.Entities.Reactive): the rules this package checks
- [Nullean.Entities.CsCheck](https://www.nuget.org/packages/Nullean.Entities.CsCheck): the generators, `AutoTest` and the no-Rx terminals
- [Source](https://github.com/nullean/entities) and [documentation](https://entities.nullean.net/)
- License: MIT
