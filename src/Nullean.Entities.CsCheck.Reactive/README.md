# Nullean.Entities.CsCheck.Reactive

Rx scheduler terminals for [`Nullean.Entities.CsCheck`](https://www.nuget.org/packages/Nullean.Entities.CsCheck): drive a script against a service that emits `IObservable<TEvent>`, on a virtual clock, and check the [`Nullean.Entities.Reactive`](https://www.nuget.org/packages/Nullean.Entities.Reactive) rules against what it emitted.

This package is opt-in. The default CsCheck package has no `System.Reactive` dependency, and a service that records timestamped events or exposes its state is tested without it (`EventAutoTest`, `Script.SampleStateAsync`). Reach for this one when the service is built on observables.

## What it adds

| Method | What it does |
|---|---|
| `script.SampleWith(factory, events, constraints)` | Runs generated steps on an Rx `HistoricalScheduler` that the generated delays advance, and fails on any violation with a shrunk walk and a seed |
| `script.SampleWith(..., coverage)` | The same, plus checks that the run reached the rules (a rule that never fired fails as vacuous) |
| `script.SampleParallelWith(...)` | The same steps run concurrently, for a service that handles overlapping calls |
| `script.RunWith(...)` | Runs one script and returns the events, to assert on by hand |

The steps on these terminals are synchronous: the scheduler drives time, so an `await` inside a step has nothing to wait on. A step written with `StepAsync` is reported with a pointer to the `TimeProvider`-based terminals (`SampleWithAsync`, `EventAutoTest`) in the main package.

## Install

```sh
dotnet add package Nullean.Entities.CsCheck.Reactive   # tests only
```

## Where to go next

- [Rx and observable streams](../../docs/reference/rx.md)
- [Time and deadlines](../../docs/guides/time.md), for the `TimeProvider` route that needs no Rx
- [Source](https://github.com/nullean/entities)

License: MIT.
