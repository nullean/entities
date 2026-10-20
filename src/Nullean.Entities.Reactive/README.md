# Nullean.Entities.Reactive

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.Reactive.svg)](https://www.nuget.org/packages/Nullean.Entities.Reactive)

**Find out the moment an event stream breaks a rule about order, gaps or completion, such as a cancelled order that ships or a ticket nobody answered in an hour.**

A point-in-time check sees one value. It cannot answer "was `Shipped` ever emitted after `Cancelled`?" or "did every placed order eventually ship?", because those are rules about a sequence. This package lifts named constraints onto an `IObservable<TEvent>`: you attach a set of rules to the stream and read a verdict per rule, `Pending`, `Holds` or `Violated`, as events arrive.

It builds on [`Nullean.Entities`](https://www.nuget.org/packages/Nullean.Entities) (the synchronous core) and adds `System.Reactive`. It is optional, and the core never depends on it. The package is trim and NativeAOT clean (the repository publishes it NativeAOT in `examples/Nullean.Entities.AotSmoke`).

## Install

```bash
dotnet add package Nullean.Entities.Reactive
```

The package targets `net10.0` and `net11.0` and depends on `Nullean.Entities` and `System.Reactive` 7.0.0. To run these rules inside generated tests, add [`Nullean.Entities.CsCheck.Reactive`](https://www.nuget.org/packages/Nullean.Entities.CsCheck.Reactive) as well.

## Get going

Declare the events, name a rule, attach it to a stream, and push events. This is a complete program (a `Subject<T>` stands in for your service's event stream).

```csharp
using System.Reactive.Subjects;
using Nullean.Entities.Reactive;
using static Nullean.Entities.Reactive.Constraints<OrderEvent>;

var events = new Subject<OrderEvent>();

// A cancelled order can never be shipped
var neverShippedAfterCancellation = Never<OrderEvent.Cancelled, OrderEvent.Shipped>(
	(cancelled, shipped) => cancelled.OrderId == shipped.OrderId
);

var spec = ReactiveConstraintSet.From(("never-shipped-after-cancellation", neverShippedAfterCancellation));
MonitoredConstraintSet<OrderEvent> monitored = spec.Attach(events);

var id = Guid.NewGuid();
events.OnNext(new OrderEvent.Placed(id));
events.OnNext(new OrderEvent.Cancelled(id));
events.OnNext(new OrderEvent.Shipped(id)); // the bug

Console.WriteLine(monitored.AnyViolated);
foreach (var (name, verdict) in monitored.Violations)
{
	Console.WriteLine($"{name} at {verdict.ViolationAt} evidence {verdict.ViolationEvidence}");
}

public abstract record OrderEvent
{
	public sealed record Placed(Guid OrderId) : OrderEvent;

	public sealed record Confirmed(Guid OrderId) : OrderEvent;

	public sealed record Shipped(Guid OrderId) : OrderEvent;

	public sealed record Cancelled(Guid OrderId) : OrderEvent;
}
```

The output names the rule and carries the two events that broke it:

```text
True
never-shipped-after-cancellation at 10/20/2026 3:09:53 PM +00:00 evidence (Cancelled { OrderId = 1a67938d-... }, Shipped { OrderId = 1a67938d-... })
```

The second example shows the main value: a deadline. "A confirmed order ships or is cancelled within 48 hours" is one expression, and it needs no timer code of your own.

```csharp
var confirmedShipsOrCancels = After<OrderEvent.Confirmed>()
	.Then<OrderEvent.Shipped, OrderEvent.Cancelled>(
		m1: (confirmed, shipped) => shipped.OrderId == confirmed.OrderId,
		m2: (confirmed, cancelled) => cancelled.OrderId == confirmed.OrderId
	)
	.Within(TimeSpan.FromHours(48));
```

If no matching event arrives in the window, the verdict becomes `Violated` with evidence such as `No Shipped within 48:00:00 after Confirmed { ... }`. Pass an Rx `IScheduler` as the second argument to `Attach` to control that clock (see the pitfalls).

## What you get

### Structural operators

Each is a static method on `Constraints<TEvent>`.

| Operator | Violated when |
|---|---|
| `Never<T1, T2>(pair)` | two consecutive events of type `T1`/`T2` (among those two types) satisfy `pair` |
| `NeverTwice<T>(key)` | two events of type `T`, anywhere in the stream, share the same `key` |
| `Always<T>(predicate)` | any event of type `T` fails `predicate` |
| `Transitions((from, to)[])` | a raw-stream-adjacent pair isn't in the allowed-transition table |
| `Transitions((prev, next) => bool)` | a raw-stream-adjacent pair fails the guard, which sees both events and so can compare payloads |
| `When(guard).Requires(requirement)` | an event satisfies `guard` but fails `requirement`; it looks only at the current event |

```csharp
using static Nullean.Entities.Reactive.Constraints<OrderEvent>;

// The same order cannot be placed twice in a stream
var neverPlacedTwice = NeverTwice<OrderEvent.Placed>(e => e.OrderId);
```

`Never<T1, T2>` filters every other event type out first, so "consecutive" means consecutive among `T1`/`T2` occurrences, not in the raw stream. Use it only when `T1`/`T2` are terminal relative to each other (a `Cancelled` that truly precludes `Shipped`). When a legitimate intermediate event type sits between them, as in `Placed -> Confirmed -> Shipped` where you want to forbid skipping `Confirmed`, use `Transitions(...)` instead. It checks raw-stream-adjacent pairs and counts the intervening `Confirmed`:

```csharp
var neverShippedWithoutConfirmation = Transitions(
	(prev, next) => !(prev is OrderEvent.Placed p && next is OrderEvent.Shipped s && p.OrderId == s.OrderId)
);
```

### Temporal operators

`After<TTrigger>().Then<TNext>().Within(window)` watches for a trigger event and requires a matching follow-up within a deadline. `Then<TNext1, TNext2>` accepts either of two follow-ups.

- `.Required()` also makes a stream in which the trigger never fired a violation. Without it, a stream with no trigger leaves the rule `Pending`. A trigger with no follow-up inside the window is a violation either way.
- `.UnlessInterruptedBy<TInterrupt>()` lifts the obligation if a specific event fires first. This suits SLA windows that don't apply once a ticket is closed.

```csharp
var firstResponseWithinSla = After<TicketEvent.Opened>()
	.Then<TicketEvent.Responded>((opened, responded) => opened.TicketId == responded.TicketId)
	.Within(TimeSpan.FromHours(1))
	.Required()
	.UnlessInterruptedBy<TicketEvent.Closed>();
```

### Liveness

`Eventually<T>` is the unconditional "did this ever happen for every instance we opened" shape. It has no deadline and no trigger, only a tracked open set across the whole stream:

```csharp
var everyPlacedOrderShips = Constraints<OrderEvent>.Eventually<OrderEvent.Shipped>(
	scope: e => e switch
	{
		OrderEvent.Placed p => p.OrderId,
		OrderEvent.Shipped s => s.OrderId,
		_ => Guid.Empty,
	},
	from: e => e is OrderEvent.Placed,
	message: "Every placed order eventually ships."
);
```

`scope` returns the key that ties an opening event (`from`) to the event that answers it (`T`). Return a different key per instance, here the order id.

It resolves to `Violated` (listing whichever scope keys are still open) only on `OnCompleted`, because "never" and "not yet" are indistinguishable mid-stream. That makes it the right tool for a bounded run (a CsCheck-generated walk, a recorded integration test session), not a live production feed that never completes.

### Sets and live state

`ReactiveConstraintSet.From(...)` groups named constraints. `Attach` shares one subscription to the source stream across all of them:

```csharp
var spec = ReactiveConstraintSet.From(
	(nameof(neverShippedAfterCancellation), neverShippedAfterCancellation),
	(nameof(confirmedShipsOrCancels), confirmedShipsOrCancels)
);

MonitoredConstraintSet<OrderEvent> monitored = spec.Attach(orderEvents);

// Synchronous snapshot: "has anything violated so far"
bool clean = !monitored.AnyViolated;

// Subscribe to live verdict changes
monitored.State.Subscribe(state =>
{
	foreach (var (name, verdict) in state.Verdicts)
	{
		dashboard.Update(name, verdict);
	}
});
```

`Verdict` is `Pending`, `Holds`, or `Violated(at, evidence)`. Read it with `IsPending`, `IsHolds`, `IsViolated`, `ViolationAt` and `ViolationEvidence`. `MonitoredConstraint<TEvent>.Verdicts` replays the current verdict to a late subscriber, so nothing is silently missed because a constraint already settled before something subscribed. `AnyViolated` and `Violations` are the two cheap synchronous reads. Look up one rule with `monitored["name"]`.

`Nullean.Entities.CsCheck`'s `SampleWith` uses `AnyViolated` as CsCheck's own `equal` check, so a violation becomes a real, reproducible, shrinkable CsCheck failure.

### Declared state machines

A `StateMachine<TState>` defined in `Nullean.Entities` lifts directly onto an event stream. `stateOf` maps each event to the state it represents:

```csharp
// `OrderLifecycle` is a StateMachine<OrderState>; each StateChanged event carries the state it moved to
ReactiveConstraint<StateChanged> reactive = OrderLifecycle.AsReactive<OrderState, StateChanged>(e => e.State);
// violated when two consecutive events' states are not a transition the machine allows
```

The same graph that drives `AsConstraint()` (for point-in-time checks against a current state value) drives `AsReactive()` (for a running observer over the stream): one declaration, two uses.

### Auto-discovery with `[Constraints]`

The same `[Constraints]` attribute from `Nullean.Entities` bundles `ReactiveConstraint<TEvent>` fields too:

```csharp
[Constraints]
public static partial class OrderEventSpec
{
	public static readonly ReactiveConstraint<OrderEvent> NeverShippedAfterCancellation =
		Constraints<OrderEvent>.Never<OrderEvent.Cancelled, OrderEvent.Shipped>(
			(c, s) => c.OrderId == s.OrderId
		);

	public static readonly ReactiveConstraint<OrderEvent> ConfirmedShipsOrCancels =
		Constraints<OrderEvent>.After<OrderEvent.Confirmed>()
			.Then<OrderEvent.Shipped, OrderEvent.Cancelled>(
				m1: (c, s) => s.OrderId == c.OrderId,
				m2: (c, ca) => ca.OrderId == c.OrderId
			)
			.Within(TimeSpan.FromHours(48));
}

// Generated: public static ReactiveConstraintSet<OrderEvent> All { get; }
```

`OrderEventSpec.All` is what you pass to `SampleWith` in `Nullean.Entities.CsCheck.Reactive`, or attach directly to a live stream.

### Lifting the sync core

`ConstraintSet<T>.ToReactive()` re-checks a plain synchronous constraint set on every event. Point-in-time constraints fire per event, and sequential ones fire on each consecutive pair (the first event alone never triggers a sequential one):

```csharp
ReactiveConstraintSet<Order> reactive = OrderConstraints.All.ToReactive();
```

If `TEvent` implements `IHaveConstraints<TEvent>`, `events.AttachConstraints()` does both steps at once. `ToReactive()` does not gain the temporal and structural operators above. Those are written in Reactive terms, because "after X, then Y, within a window" has no synchronous equivalent.

### C# 11 discriminated unions

Every typed operator (`Never<T1, T2>`, `After<TTrigger>()`, etc.) requires `where T : TEvent`, which a C# 11 union's case types cannot satisfy. Predicate-based counterparts (`Never`, `NeverTwice`, `Always`, `Eventually`, `AfterUntil`) handle them:

```csharp
Constraints<OrderEventUnion>.Never(
	isT1: e => e is Cancelled,
	isT2: e => e is Shipped,
	pair: (c, s) => c is Cancelled cc && s is Shipped ss && cc.OrderId == ss.OrderId
);
```

`AfterUntil(isTrigger, isNext, window, isInterrupt)` replaces the whole `After().Then().Within().UnlessInterruptedBy()` chain. The union syntax itself needs `net11.0` and `LangVersion` `preview`; the worked example is `OrderStateExamplesNet11.cs` in the examples project.

### Relationship to the core temporal rules

`Nullean.Entities` has the same operators (`Temporal<TEvent>.Never/NeverTwice/Always/Transitions/When/After().Then().Within()/AfterUntil/Eventually`) as a pure state machine over timestamped events, with `Trace.Check` for recorded sessions and `MonitorAsync` for an `IAsyncEnumerable` on a `TimeProvider`: no Rx, no scheduler. Use this package when the stream already is an `IObservable<T>` or you want Rx's own operators next to the rules. The two are checked against each other (same generated traces, same verdict and violation time per operator), so a rule means the same thing in either.

`Verdict` (`Pending`, `Holds`, `Violated(at, evidence)`) lives in `Nullean.Entities` and is shared. Add `using Nullean.Entities;` where you use it. `AfterUntil` and `After().Then().Within()` report only `Pending` or `Violated`, never `Holds`: an answered obligation simply stays quiet.

## Pitfalls and limits

- **`Attach` runs on real time unless you pass a scheduler.** Without one, `After().Within()` uses Rx's `DefaultScheduler`, so a 48-hour window really waits 48 hours. In tests pass a `HistoricalScheduler` (`Nullean.Entities.CsCheck.Reactive` does this for you).
- **`Never<T1, T2>` filters first.** It does not mean "adjacent in the stream". Use `Transitions` when an intermediate event is legal.
- **`Eventually` only settles on completion.** On a stream that never completes it stays `Pending`.
- **A violation is sticky.** A rule reports the first violation and stops; it does not report later ones.
- **`Named(...)` mutates the constraint** and returns the same instance. Names in a `ReactiveConstraintSet` come from the tuple you pass to `From`.
- **Name clash with the core.** Inside a namespace that nests under `Nullean.Entities`, the synchronous `Constraints<T>` is found before the reactive one. Alias the closed generic: `using ReactiveConstraints = Nullean.Entities.Reactive.Constraints<OrderEvent>;`.
- **Shared subscription.** A set subscribes once and fans out, so a cold source observable is subscribed one time per `Attach`, not one time per rule.
- `Transitions((Type, Type)[])` matches by `IsAssignableFrom` on the runtime types of the events.

## Next

- Docs: [Rx and observable streams](https://entities.nullean.net/reference/rx/), [temporal rules](https://entities.nullean.net/axes/06-temporal/) and [liveness](https://entities.nullean.net/axes/07-liveness/)
- Source examples: [order, retry, search box, commerce and traffic](https://github.com/nullean/entities/tree/main/examples/Nullean.Entities.Reactive.Examples)
- [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities): the synchronous core
- [Nullean.Entities.CsCheck.Reactive](https://www.nuget.org/packages/Nullean.Entities.CsCheck.Reactive): drive a generated script and check these rules against what your service emitted
- [Nullean.Entities.CsCheck](https://www.nuget.org/packages/Nullean.Entities.CsCheck): the rest of the generated tests
- Source and full documentation: https://github.com/nullean/entities and https://entities.nullean.net/
- License: MIT
