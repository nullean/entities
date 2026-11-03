---
navigation_title: System.Reactive
description: Rules about order, gaps and completion checked on an IObservable as events arrive, and generated tests on a virtual Rx clock.
---

# System.Reactive

```bash
dotnet add package Nullean.Entities.Reactive            # rules on a live IObservable<T>
dotnet add package Nullean.Entities.CsCheck.Reactive    # tests only: generated scripts on a virtual Rx clock
```

The core package and the default CsCheck package do not depend on System.Reactive. A service that records timestamped events is tested without it (see [Time and events](../guides/time.md)).
Add these packages when the service is built on observables.

- **`Nullean.Entities.Reactive`** attaches a set of named rules to an `IObservable<TEvent>` and gives a verdict per rule, `Pending`, `Holds` or `Violated`, as events arrive:
  "was `Shipped` ever emitted after `Cancelled`?", "did every placed order ship within an hour?". It is trim and NativeAOT clean.

  ```csharp
  var spec = ReactiveConstraintSet.From(("never-shipped-after-cancellation",
      Never<OrderEvent.Cancelled, OrderEvent.Shipped>((c, s) => c.OrderId == s.OrderId)));

  MonitoredConstraintSet<OrderEvent> monitored = spec.Attach(events);   // monitored.AnyViolated, monitored.Violations
  ```

- **`Nullean.Entities.CsCheck.Reactive`** runs a generated `Script<TSut>` against the service on a `HistoricalScheduler` that the generated delays advance, then checks the rules against
  the events it emitted. A 48-hour deadline is tested without waiting, and a failure comes back as the shortest call sequence with a seed:

  ```csharp
  script.SampleWith(scheduler => new Shipments(scheduler), s => s.Events, shipsWithinAMinute);
  ```

The terminals and their options are in [Rx scripts](../reference/rx.md).
