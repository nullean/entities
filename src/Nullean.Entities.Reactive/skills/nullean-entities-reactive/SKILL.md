---
name: nullean-entities-reactive
description: >-
  Use when checking rules about the order, timing or completion of events on an IObservable<T> stream, such as a cancelled order that ships or a missed deadline. Nullean.Entities.Reactive attaches named temporal constraints to the stream and keeps a live Pending, Holds or Violated verdict per rule.
---

# Nullean.Entities.Reactive

Attach a `ReactiveConstraintSet<TEvent>` to an Rx stream and read one `Verdict` (from `Nullean.Entities`) per rule. Without Rx (a recorded trace, an `IAsyncEnumerable`), use `Temporal<TEvent>` and `TemporalMonitor` from core `Nullean.Entities`; to check these rules in generated tests, use `Nullean.Entities.CsCheck.Reactive`.

## The calls you need

```csharp
using Nullean.Entities.Reactive;
using Rx = Nullean.Entities.Reactive.Constraints<OrderEvent>;

var events = new Subject<OrderEvent>();
var spec = ReactiveConstraintSet.From(
	("never-shipped-after-cancel", Rx.Never<OrderEvent.Cancelled, OrderEvent.Shipped>((c, s) => c.Id == s.Id)),
	("placed-once", Rx.NeverTwice<OrderEvent.Placed>(p => p.Id)));
MonitoredConstraintSet<OrderEvent> monitored = spec.Attach(events);

events.OnNext(new OrderEvent.Cancelled(1));
events.OnNext(new OrderEvent.Shipped(1));
foreach (var (name, verdict) in monitored.Violations)
	Console.WriteLine($"{name}: {verdict.ViolationEvidence}");
```

A deadline, on a virtual clock:

```csharp
ReactiveConstraint<OrderEvent> shipsInTime = Rx.After<OrderEvent.Confirmed>()
	.Then<OrderEvent.Shipped>((c, s) => c.Id == s.Id)
	.Within(TimeSpan.FromHours(48))
	.UnlessInterruptedBy<OrderEvent.Cancelled>();
var clock = new HistoricalScheduler();
var running = shipsInTime.Attach(events, clock);
events.OnNext(new OrderEvent.Confirmed(2));
clock.AdvanceBy(TimeSpan.FromHours(49)); // running.Current.IsViolated is now true
```

Liveness, and lifting a synchronous set:

```csharp
var everyOrderShips = Rx.Eventually<OrderEvent.Shipped>(scope: e => e.Id, from: e => e is OrderEvent.Placed);
ReactiveConstraintSet<OrderEvent> lifted = OrderRules.All.ToReactive();
```

## Gotchas

- `Attach` without a scheduler runs on real time: a 48-hour window waits 48 hours. Pass a `HistoricalScheduler` in tests.
- `.Required()` and `.UnlessInterruptedBy<T>()` both return the finished `ReactiveConstraint`, so they cannot be chained. With neither, assign `Within(...)` to a `ReactiveConstraint<TEvent>`; `var` keeps the builder.
- `Never<T1, T2>` drops other event types first; use `Transitions((prev, next) => ...)` when a legal event sits between.
- `Eventually` settles only on `OnCompleted`; a live stream stays `Pending`. `After...Within` never reports `Holds`. Only the first violation is kept.
- With both `using Nullean.Entities;` and `using Nullean.Entities.Reactive;`, `Constraints<T>` is ambiguous. Use an alias as above.
- Typed operators need `where T : TEvent`. For a C# union use the predicate overloads (`Never(isT1, isT2, pair)`, `AfterUntil`).

Docs: https://entities.nullean.net
