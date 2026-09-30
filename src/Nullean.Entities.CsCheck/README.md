# Nullean.Entities.CsCheck

Drives a `Script<TSut>` action sequence under [CsCheck](https://github.com/AnthonyLloyd/CsCheck)
and checks a [`Nullean.Entities.Reactive`](https://www.nuget.org/packages/Nullean.Entities.Reactive)
`ReactiveConstraintSet<TEvent>` against the resulting event stream on every iteration.

> **This package is intentionally unimplemented.** `Script<TSut>`'s builder methods are no-op
> stubs, and every `SampleWith`/`RunWith` overload throws `NotImplementedException`. The API below
> is declared against its final shape — the examples compile and show the intended call pattern —
> but nothing here executes yet. If you need a working property-test driver today, drive CsCheck
> directly and attach a `ReactiveConstraintSet<TEvent>` to your event stream by hand via
> `Nullean.Entities.Reactive`.

## The intended shape

`Script<TSut>` describes an action sequence CsCheck can generate, shrink, and replay:

```csharp
var script = Script.For<OrderService>()
    .Step(Gen.Guid, (svc, id) => svc.Place(id))
    .Step(Gen.Guid, (svc, id) => svc.Confirm(id))
    .Step(Gen.Guid, (svc, id) => svc.Ship(id))
    .Delays(Gen.Int[0, 500].Select(ms => TimeSpan.FromMilliseconds(ms)));
```

`SampleWith` runs the script against a fresh SUT instance per iteration and checks a constraint set
against the SUT's own event stream:

```csharp
script.SampleWith(
    scheduler => new OrderService(scheduler),
    svc => svc.Events,
    OrderStateSpec.All // a ReactiveConstraintSet<OrderStateEvent>
);
```

If the event type implements `IHaveConstraints<TEvent>` (via a `ReactiveConstraintSet` obtained from
`ConstraintSet<T>.ToReactive()`), the constraint set argument can be dropped. `RunWith` is the same
idea but returns the whole run — `IterationStarted`, `ConstraintResolved`, `SmallerFailure`,
`Completed` — as an `IObservable<EntityEvent>` for a caller that wants to observe the run itself
rather than only its pass/fail outcome.

## Install

```sh
dotnet add package Nullean.Entities.CsCheck
```

## Links

- [Source & full documentation](https://github.com/nullean/entities) — see `AGENTS.md` for what's
  built and what's still open here.
- [Nullean.Entities.Reactive](https://www.nuget.org/packages/Nullean.Entities.Reactive) — the
  constraint sets this package checks against a generated run.
- [Nullean.Entities.Bogus](https://www.nuget.org/packages/Nullean.Entities.Bogus) — CsCheck
  generators for realistic fake data, usable independently of this package.
- License: MIT
