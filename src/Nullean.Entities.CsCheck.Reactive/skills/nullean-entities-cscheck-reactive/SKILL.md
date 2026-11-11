---
name: nullean-entities-cscheck-reactive
description: >-
  Use when property-testing a service built on IObservable<T> and Rx timers against Nullean.Entities.Reactive rules. It runs a generated
  Script<TSut> on a virtual HistoricalScheduler and fails with the shortest call-and-delay sequence and a CsCheck seed.
---

# Nullean.Entities.CsCheck.Reactive

Extension methods on `Script<TSut>` (namespace `Nullean.Entities.CsCheck`): `SampleWith`, `SampleParallelWith` and `RunWith`. For a service
that takes a `TimeProvider` or records timestamped events, skip Rx and use `EventAutoTest` from `Nullean.Entities.CsCheck`.

## The calls you need

```csharp
// using static Nullean.Entities.Reactive.Constraints<ShipmentEvent>;
var rules = ReactiveConstraintSet.From(("ships-within-a-minute",
	After<ShipmentEvent.Confirmed>().Then<ShipmentEvent.Shipped>().Within(TimeSpan.FromSeconds(60)).Required()));

var script = Script.For<Shipments>()
	.Step("Confirm", s => s.Confirm())
	.Delays(Gen.Int[0, 120].Select(seconds => TimeSpan.FromSeconds(seconds)))
	.Length(1, 5);

script.SampleWith(scheduler => new Shipments(scheduler), s => s.Events, rules);
```

```csharp
// also fail as vacuous when an expected event never happened
var coverage = new Coverage<ShipmentEvent>().Expect("Shipped", e => e is ShipmentEvent.Shipped);
script.SampleWith(scheduler => new Shipments(scheduler), s => s.Events, rules, coverage);
```

```csharp
// the run as events instead of an exception
var run = await script.RunWith(scheduler => new Shipments(scheduler), s => s.Events, rules, iter: 200).ToList();
var failure = run.OfType<EntityEvent.SmallerFailure>().SingleOrDefault();
```

```csharp
// races: steps run concurrently on one instance, no delays
script.SampleParallelWith(scheduler => new Shipments(scheduler), s => s.Events, rules, iter: 100);
```

## Gotchas

- The factory is `Func<IScheduler, TSut>`. The service must use that scheduler for all time (`Observable.Timer(t, scheduler)`,
  `scheduler.Now`); `Task.Delay` or `DateTime.UtcNow` never advance, so deadlines never expire.
- Without `.Delays(...)` every delay is zero and virtual time never moves. A delay runs before its step, never after the last one.
- `Within(...)` without `.Required()` holds vacuously when the trigger never fires.
- Steps must be synchronous: a `StepAsync` step fails the run with a `NotSupportedException` pointing at the `TimeProvider` terminals.
- A script with no `Step(...)` throws `InvalidOperationException`. Default length is 0 to 10 steps.
- Failures throw `CsCheckException` with `Set seed: "..."`; pass it back as `seed:`. Coverage is not checked when `seed` is given.
- `RunWith` reports `ConstraintResolved` only for the final shrunk failure, not every iteration.
- `SampleParallelWith` tests thread safety too: an unsynchronised `Subject<T>` fails for reasons unrelated to your rules.
- In a namespace under `Nullean.Entities`, `Constraints<T>` resolves to the synchronous builder. Use an alias or `using static`.

Docs: https://entities.nullean.net
