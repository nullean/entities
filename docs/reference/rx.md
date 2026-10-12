---
navigation_title: "Rx scripts (opt-in)"
description: "The Rx scheduler terminals in the separate `Nullean.Entities.CsCheck.Reactive` package."
---

# Rx scripts (opt-in)

## Rx (opt-in): `Nullean.Entities.CsCheck.Reactive`

Nothing above needs System.Reactive, and this package does not reference it. If your service is built on `IObservable<T>` and an Rx scheduler, add the separate package
`Nullean.Entities.CsCheck.Reactive` (it brings `Nullean.Entities.Reactive` and `System.Reactive`); the same `Script<TSut>` then has Rx terminals, with the same shrinking and seeds:

```csharp
script.SampleWith(scheduler => new OrderService(scheduler), svc => svc.Events, OrderEventSpec.All);   // ReactiveConstraintSet<OrderEvent>
```

| Call | Does |
|---|---|
| `script.SampleWith(factory, events, constraints)` | Runs on an Rx `HistoricalScheduler` that the generated delays advance; fails on any violation, with a shrunk walk and seed. Your SUT must take that scheduler for time (`Observable.Timer(..., scheduler)`, `scheduler.Now`). |
| `script.SampleWith(..., coverage)` | The same, plus vacuity checks. |
| `script.SampleParallelWith(...)` | Runs steps concurrently and fails if an interleaving breaks a constraint that no sequential order does. Delays are not used. |
| `script.RunWith(...)` | The whole run as an `IObservable<EntityEvent>`, for a caller who wants the play-by-play instead of an exception. |

If the event type implements `IHaveConstraints<TEvent>`, the constraint argument can be dropped. The Rx rules and the core `Temporal<TEvent>` rules check the same operators, and a differential test keeps them in agreement.
