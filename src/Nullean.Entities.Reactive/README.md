# Nullean.Entities.Reactive

Temporal and structural constraints over `IObservable<TEvent>` streams, for the events a
[`Nullean.Entities`](https://www.nuget.org/packages/Nullean.Entities) constraint set can't reach on
its own: "never twice", "after X, then Y within N seconds", legal state transitions.

## The idea

A `ReactiveConstraint<TEvent>` is an immutable descriptor — like `Constraint<T>`, it lives anywhere,
and attaching it to a stream is a separate step from declaring it:

```csharp
using static Nullean.Entities.Reactive.Constraints<OrderEvent>;

var neverShippedAfterCancellation = Never<OrderEvent.Cancelled, OrderEvent.Shipped>(
    (cancelled, shipped) => cancelled.OrderId == shipped.OrderId
);

MonitoredConstraint<OrderEvent> running = neverShippedAfterCancellation.Attach(orderEvents);
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

`Never<T1, T2>`'s "consecutive" filters every other event type out *first* — it means consecutive
among `T1`/`T2` occurrences, not consecutive in the raw stream. That's the right tool only when
`T1`/`T2` are the sole events of interest for the key (a terminal state that precludes ever seeing
the other again, e.g. `Never<Cancelled, Shipped>`). If a legitimate event type can sit between them
— `Placed → Confirmed → Shipped`, skipping straight to `Never<Placed, Shipped>` to forbid skipping
`Confirmed` — that intermediate `Confirmed` gets filtered out too, making `Placed`/`Shipped` look
falsely adjacent for the ordinary, legal path. Use `Transitions((TEvent, TEvent) -> bool)` instead;
it checks raw-stream-adjacent pairs, so the intervening `Confirmed` actually counts.

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

### Liveness, unbounded

Everything above is bounded — `.Then().Within()` needs a trigger and a deadline. `Eventually<T>` is
the unconditional "did this ever happen at all" shape instead: it tracks an open set of pending
instances (keyed by `scope`) across the whole stream rather than a sliding window from the last
trigger, and the only point "never" can be told apart from "not yet" is when the stream itself
completes — so it resolves to `Violated` (naming whichever scope keys are still open) only on
`OnCompleted`, and stays `Pending` forever against a stream that never completes. That makes it the
right fit for a bounded run (a CsCheck-generated walk, a recorded session), not a live dashboard.

```csharp
Constraints<OrderEvent>.Eventually<OrderEvent.Shipped>(
    scope: e => e.OrderId, from: e => e is OrderEvent.Placed,
    message: "Every placed order eventually ships."
);
```

See [`Nullean.Entities.CsCheck`'s testing-qualities table](../Nullean.Entities.CsCheck#testing-qualities-not-just-assertions)
for how this and the rest of the qualities line up against CsCheck's different samplers.

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

`MonitoredConstraintSet<TEvent>.AnyViolated` is a cheap synchronous snapshot — "has anything violated
so far" — for a caller that just needs a yes/no rather than subscribing to `State` and tracking it
itself. It's what lets `Nullean.Entities.CsCheck`'s `SampleWith` use a `MonitoredConstraintSet` as
CsCheck's own `equal` check (`equal: (sut, _) => !monitored.AnyViolated`), so a violation becomes a
real, reproducible CsCheck failure. `.Violations` is the detail behind it: every constraint currently
`Violated`, by name, which is what `RunWith` reports on a shrunk failing case.

## Predicate-based operators, for event shapes without a shared base type

Every operator above is generic over a case type with `where T : TEvent` — exactly right for a
record hierarchy, where a case genuinely *is* a `TEvent`. It does not typecheck for a C# 11
discriminated union: `readonly union OrderEvent(Placed, Shipped);` makes `Placed`/`Shipped` ordinary
sibling top-level types with compiler-synthesized pattern-matching support, not real subtypes of
`OrderEvent` — `where T : OrderEvent` is rejected outright (CS0701, "not a valid constraint") since
the union itself compiles to a sealed type.

`Never`, `NeverTwice`, `Always`, `Eventually`, and `After().Then().Within()` (collapsed into one
`AfterUntil` primitive, since there's no type parameter to hang builder steps off) each have a
predicate-based counterpart that classifies with a `Func<TEvent,bool>` instead:

```csharp
Constraints<OrderEventUnion>.Never(
    isT1: e => e is Cancelled, isT2: e => e is Shipped,
    pair: (c, s) => c is Cancelled cc && s is Shipped ss && cc.OrderId == ss.OrderId
);

Constraints<OrderEventUnion>.AfterUntil(
    isTrigger: e => e is Confirmed,
    isNext: (trigger, e) => trigger is Confirmed c
        && e switch { Shipped s => s.OrderId == c.OrderId, Cancelled ca => ca.OrderId == c.OrderId, _ => false },
    window: TimeSpan.FromSeconds(60)
);
```

They work identically against a plain record hierarchy too — reach for the typed overloads there,
since `e is Placed p` reads better than a bare `Func<TEvent,bool>` when the compiler can check the
type parameter for you. `Transitions(Func<TEvent,TEvent,bool>)` already took no type parameter and
needs no counterpart. See `examples/Nullean.Entities.Reactive.Examples`' net11.0-only
`OrderStateExamplesNet11.cs` for a full worked example against a real union, including a `switch`
with compiler-enforced exhaustiveness.

## Lifting the sync core

`ConstraintSet<T>.ToReactive()` converts a plain, synchronous `Nullean.Entities` constraint set into
a `ReactiveConstraintSet<T>` that re-checks every constraint on each event — for constraints that
were written once and need to run continuously, without the temporal/structural vocabulary above. A
point-in-time constraint (`Always`, `When().Requires()`) checks every event on its own; a sequential
one (`Transitions`) checks each consecutive pair, the same way `ConstraintSet<T>.Apply` would:

```csharp
ReactiveConstraintSet<Item> reactive = itemConstraints.ToReactive();
```

If `TEvent` implements `IHaveConstraints<TEvent>` (from `Nullean.Entities`), `AttachConstraints()`
does both steps at once: `events.AttachConstraints()`.

## Exhaustive constraint sets: `[Constraint]`

The same `[Constraint]` attribute and generator from `Nullean.Entities` bundle `ReactiveConstraint<T>`
fields too — tag every one that belongs to an event union with `[Constraint]` on a companion "spec"
class (there's no reactive-native self-attach shape; see below):

```csharp
// Cancelled is terminal — nothing ever un-cancels an order back onto a path to Shipped — so
// Never<Cancelled,Shipped> is safe regardless of what else the domain adds between them. See the
// caveat above the operator table: for a non-terminal pair, with a real intermediate state (e.g.
// forbidding Shipped without a prior Confirmed), Transitions is the correct choice, not Never<T1,T2>.
public abstract record OrderEvent
{
    public sealed record Placed(Guid OrderId) : OrderEvent;
    public sealed record Cancelled(Guid OrderId) : OrderEvent;
    public sealed record Shipped(Guid OrderId) : OrderEvent;
}

public static partial class OrderEventSpec
{
    [Constraint]
    public static readonly ReactiveConstraint<OrderEvent> NeverShippedAfterCancellation =
        Constraints<OrderEvent>.Never<OrderEvent.Cancelled, OrderEvent.Shipped>((c, s) => c.OrderId == s.OrderId);
}

// Generated: public static ReactiveConstraintSet<OrderEvent> Constraints { get; } = ...
```

Every generated spec class also gets a nested `TypeConstraints` struct implementing
`IReactiveConstraintsFor<TEvent>` — a plain instance interface, so `OrderEventSpec.Instance`
(a forwarding shortcut for `OrderEventSpec.TypeConstraints.Instance`, generated on the outer class so
you never have to spell out `TypeConstraints`) is a value generic code can take as a parameter and
infer `TEvent` from, the same as the sync `IConstraintsFor<T>` in `Nullean.Entities`. It's never on
the spec class's own base list (`Constraints` is `static` there regardless of whether the class
itself is). There's no reactive self-attach interface parallel to `IHaveConstraints<T>`: an event
union that wants to declare its own reactive rules on itself still goes through a companion spec
class, or lifts a sync `IHaveConstraints<T>` set with `ToReactive()`/`AttachConstraints()` above.

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
