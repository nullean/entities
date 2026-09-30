# Nullean.Entities.Reactive

Temporal and structural constraints over `IObservable<TEvent>` streams, for the events a
[`Nullean.Entities`](https://www.nuget.org/packages/Nullean.Entities) constraint set can't reach on
its own: "never twice", "after X, then Y within N seconds", legal state transitions.

## The idea

A `ReactiveConstraint<TEvent>` is an immutable descriptor — like `Constraint<T>`, it lives anywhere,
and attaching it to a stream is a separate step from declaring it:

```csharp
using static Nullean.Entities.Reactive.Constraints<OrderEvent>;

var neverShippedBeforeConfirmed = Never<OrderEvent.Placed, OrderEvent.Shipped>(
    (placed, shipped) => placed.OrderId == shipped.OrderId
);

MonitoredConstraint<OrderEvent> running = neverShippedBeforeConfirmed.Attach(orderEvents);
running.Verdicts.Subscribe(verdict => Console.WriteLine(verdict));
```

`using static Constraints<TEvent>` is the intended way to read these — `Never`, `NeverTwice`,
`Always`, `Transitions`, `After` read like prose once the type parameter is fixed for the file.

## Structural operators

| Operator | Violated when |
|---|---|
| `Never<T1, T2>(pair)` | two *consecutive* events of type `T1`/`T2` satisfy `pair` |
| `NeverTwice<T>(key)` | two *non-adjacent* events of type `T` share the same `key` |
| `Always<T>(predicate)` | any event of type `T` fails `predicate` |
| `Transitions((from, to)[])` | a consecutive pair isn't in the allowed-transition table |
| `Transitions((TEvent, TEvent) -> bool)` | a consecutive pair fails a transition guard |

## Temporal operators

`After<TTrigger>().Then<TNext>().Within(window)` builds a constraint that watches for `TTrigger`,
then requires `TNext` within `window`:

```csharp
var confirmedShipsOrCancels = After<OrderEvent.Confirmed>()
    .Then<OrderEvent.Shipped, OrderEvent.Cancelled>(
        m1: (confirmed, shipped) => shipped.OrderId == confirmed.OrderId,
        m2: (confirmed, cancelled) => cancelled.OrderId == confirmed.OrderId
    )
    .Within(TimeSpan.FromSeconds(60));
```

Without `.Required()`, a trigger that never fires leaves the constraint `Pending` — vacuously
`Holds`, not violated. `.Required()` makes a fired trigger with no matching follow-up a violation.
`.UnlessInterruptedBy<TInterrupt>()` lifts the obligation (to `Holds`) if `TInterrupt` fires first.

## Sets and live state

`ReactiveConstraintSet<TEvent>.From(...)` groups named constraints and shares one subscription to
the source stream across all of them (`events.Publish().RefCount()`):

```csharp
var spec = ReactiveConstraintSet.From(
    (nameof(neverShippedBeforeConfirmed), neverShippedBeforeConfirmed),
    (nameof(confirmedShipsOrCancels), confirmedShipsOrCancels)
);

MonitoredConstraintSet<OrderEvent> monitored = spec.Attach(orderEvents);
monitored["neverShippedBeforeConfirmed"].Current; // Pending | Holds | Violated
monitored.State.Subscribe(state => Dashboard.Render(state.Verdicts));
```

`Verdict` is `Pending`, `Holds`, or `Violated(at, evidence)` — `MonitoredConstraint<TEvent>.Verdicts`
replays the current verdict to a subscriber that arrives late, so `MonitoredConstraintSet.State`
never silently drops a constraint that had already settled before something subscribed to it.

## Lifting the sync core

`ConstraintSet<T>.ToReactive()` converts a plain, synchronous `Nullean.Entities` constraint set into
a `ReactiveConstraintSet<T>` that re-checks every constraint on each event — for constraints that
were written once and need to run continuously, without the temporal/structural vocabulary above:

```csharp
ReactiveConstraintSet<Item> reactive = itemConstraints.ToReactive();
```

If `TEvent` implements `IHaveConstraints<TEvent>` (from `Nullean.Entities`), `AttachConstraints()`
does both steps at once: `events.AttachConstraints()`.

## Install

```sh
dotnet add package Nullean.Entities.Reactive
```

## Links

- [Source & full documentation](https://github.com/nullean/entities)
- [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities) — the synchronous core this
  package lifts onto a stream.
- [Nullean.Entities.CsCheck](https://www.nuget.org/packages/Nullean.Entities.CsCheck) — drives a
  `ReactiveConstraintSet<TEvent>` from a CsCheck-generated action sequence.
- License: MIT
