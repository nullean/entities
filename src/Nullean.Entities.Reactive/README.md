# Nullean.Entities.Reactive

The events a point-in-time constraint can't see — temporal ordering, gaps between events, and
liveness over an `IObservable<TEvent>` stream.

A `Constraint<T>` from [`Nullean.Entities`](https://www.nuget.org/packages/Nullean.Entities) checks
a value at a point in time. It can't answer "was Shipped ever emitted after a Cancelled?" or "did
every Placed order eventually ship?" — those need a running observer on the stream. That's what this
package is.

## Structural operators

| Operator | Violated when |
|---|---|
| `Never<T1, T2>(pair)` | two consecutive events of type `T1`/`T2` (among those two types) satisfy `pair` |
| `NeverTwice<T>(key)` | two events of type `T`, anywhere in the stream, share the same `key` |
| `Always<T>(predicate)` | any event of type `T` fails `predicate` |
| `Transitions((from, to)[])` | a raw-stream-adjacent pair isn't in the allowed-transition table |

```csharp
using static Nullean.Entities.Reactive.Constraints<OrderEvent>;

// A cancelled order can never be shipped
var neverShippedAfterCancellation = Never<OrderEvent.Cancelled, OrderEvent.Shipped>(
    (cancelled, shipped) => cancelled.OrderId == shipped.OrderId
);

// The same order cannot be placed twice in a stream
var neverPlacedTwice = NeverTwice<OrderEvent.Placed>(e => e.OrderId);
```

`Never<T1, T2>` filters every other event type out first — "consecutive" means consecutive among
`T1`/`T2` occurrences, not in the raw stream. Use it only when `T1`/`T2` are terminal relative to
each other (a `Cancelled` that truly precludes `Shipped`). When a legitimate intermediate event type
sits between them — `Placed → Confirmed → Shipped`, where you want to forbid skipping `Confirmed` —
use `Transitions(...)` instead, which checks raw-stream-adjacent pairs and counts the intervening
`Confirmed`.

## Temporal operators

`After<TTrigger>().Then<TNext>().Within(window)` watches for a trigger event and requires a
matching follow-up within a deadline:

```csharp
var confirmedShipsOrCancels = After<OrderEvent.Confirmed>()
    .Then<OrderEvent.Shipped, OrderEvent.Cancelled>(
        m1: (confirmed, shipped) => shipped.OrderId == confirmed.OrderId,
        m2: (confirmed, cancelled) => cancelled.OrderId == confirmed.OrderId
    )
    .Within(TimeSpan.FromHours(48));
```

`.Required()` makes an unmatched trigger (no follow-up at all before stream end) a violation rather
than leaving it `Pending`. `.UnlessInterruptedBy<TInterrupt>()` lifts the obligation if a specific
event fires first — useful for SLA windows that don't apply once a ticket is closed:

```csharp
var firstResponseWithinSla = After<TicketEvent.Opened>()
    .Then<TicketEvent.Responded>((opened, responded) => opened.TicketId == responded.TicketId)
    .Within(TimeSpan.FromHours(1))
    .Required()
    .UnlessInterruptedBy<TicketEvent.Closed>();
```

## Liveness

`Eventually<T>` is the unconditional "did this ever happen for every instance we opened" shape —
no deadline, no trigger, just a tracked open set across the whole stream:

```csharp
Constraints<OrderEvent>.Eventually<OrderEvent.Shipped>(
    scope: e => e.OrderId,
    from: e => e is OrderEvent.Placed,
    message: "Every placed order eventually ships."
);
```

It resolves to `Violated` (listing whichever scope keys are still open) only on `OnCompleted` —
"never" and "not yet" are indistinguishable mid-stream. That makes it the right tool for a bounded
run (a CsCheck-generated walk, a recorded integration test session), not a live production feed
that never completes.

## Declared state machines

A `StateMachine<TState>` defined in `Nullean.Entities` lifts directly onto an event stream:

```csharp
var reactive = OrderLifecycle.AsReactive();
// ReactiveConstraint<OrderState> — checked on every event
```

The same graph that drives `AsConstraint()` (for point-in-time checks against a current state value)
drives `AsReactive()` (for a running observer over the stream) — one declaration, two uses.

## Sets and live state

`ReactiveConstraintSet<TEvent>.From(...)` groups named constraints and shares one subscription to the
source stream across all of them:

```csharp
var spec = ReactiveConstraintSet.From(
    (nameof(neverShippedAfterCancellation), neverShippedAfterCancellation),
    (nameof(confirmedShipsOrCancels), confirmedShipsOrCancels)
);

MonitoredConstraintSet<OrderEvent> monitored = spec.Attach(orderEvents);

// Synchronous snapshot — "has anything violated so far"
bool clean = !monitored.AnyViolated;

// Subscribe to live verdict changes
monitored.State.Subscribe(state =>
{
    foreach (var (name, verdict) in state.Verdicts)
        dashboard.Update(name, verdict);
});
```

`Verdict` is `Pending`, `Holds`, or `Violated(at, evidence)`. `MonitoredConstraint<TEvent>.Verdicts`
replays the current verdict to a late subscriber, so nothing is silently missed because a constraint
already settled before something subscribed. `AnyViolated` and `Violations` are the two cheap
synchronous reads — `Nullean.Entities.CsCheck`'s `SampleWith` uses `AnyViolated` as CsCheck's own
`equal` check so a violation becomes a real, reproducible, shrinkable CsCheck failure.

## Auto-discovery: `[Constraints]`

The same `[Constraints]` attribute from `Nullean.Entities` bundles `ReactiveConstraint<TEvent>`
fields too:

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

`OrderEventSpec.All` is what you pass to `SampleWith` in `Nullean.Entities.CsCheck` — or attach
directly to a live stream.

## Lifting the sync core

`ConstraintSet<T>.ToReactive()` re-checks a plain synchronous constraint set on every event —
point-in-time constraints fire per event, sequential ones fire on each consecutive pair:

```csharp
ReactiveConstraintSet<Order> reactive = OrderConstraints.All.ToReactive();
```

If `TEvent` implements `IHaveConstraints<TEvent>`, `events.AttachConstraints()` does both steps at
once.

## C# 11 discriminated unions

Every typed operator (`Never<T1, T2>`, `After<TTrigger>()`, etc.) requires `where T : TEvent`,
which a C# 11 union's case types cannot satisfy. Predicate-based counterparts handle them:

```csharp
Constraints<OrderEventUnion>.Never(
    isT1: e => e is Cancelled,
    isT2: e => e is Shipped,
    pair: (c, s) => c is Cancelled cc && s is Shipped ss && cc.OrderId == ss.OrderId
);
```

## Install

```sh
dotnet add package Nullean.Entities.Reactive
```

## Links

- [Source & full documentation](https://github.com/nullean/entities)
- [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities) — the synchronous core
- [Nullean.Entities.CsCheck](https://www.nuget.org/packages/Nullean.Entities.CsCheck) — drives a
  generated action sequence through `SampleWith` and checks these constraints against the resulting
  event stream
- License: MIT
