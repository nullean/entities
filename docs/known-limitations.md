---
navigation_title: Known limitations
description: What {{product}} does not do, and what to do instead.
---

# Known limitations

## Rules are runtime checks

A rule is checked when you validate a value or run a test, not when you compile. The `[Constraints]` generator makes the set exhaustive at build time (a rule you declare is in `All`), but whether a value satisfies a rule is decided at run time.

## Rules are synchronous and pure

A rule cannot call a database or a service. "Is this email taken" is not a rule here; do it in the application service. This is on purpose: a rule that does I/O cannot be generated against.

## Everything awaits; nothing blocks

Every terminal is async (`RunAsync`, `SampleWithAsync`, `SampleStateAsync`, `ConformsToAsync`), because the operations under test may be. The library never blocks on a task. What async cannot do is make real I/O repeatable: a step that awaits a real database or HTTP call gives a different answer next time, so a failing seed cannot be replayed. Replace I/O with in-memory fakes and a `ManualTimeProvider`. The Rx terminals are scheduler-driven and take synchronous steps only; an async step there is reported with a pointer to the `TimeProvider` terminals.

## The generator is yours to get right

`GenAuto` builds values from the shape of a type. It cannot guess a relation between members, a key that is its value's own id, or what makes a whole structure valid. You declare those. A generator that never reaches a rule is reported (the Vacancy axis), but you have to fix it.

## Some counter-examples need a person

The search finds a counter-example for most rules, not all. A rule about a specific value, or about a structure built to your domain's shape, needs one you write. See [counter-examples](guides/counter-examples.md).

## Immutable types fit best

A record whose methods return the next value is what the step axes run on. A service or a class that is changed in place is tested through the same `AutoTest` (see [services and mutable classes](guides/services.md)): the "after" is built on a fresh instance, which needs the factory and the operations to be deterministic. Idempotency of an in-place operation and custom before-and-after rules are not checked on it. Entities mapped by an ORM: declare the rules on the domain record and map to it.

## The test is only as good as the operations you give it

The step axes run the operations of the type (every public method that returns the type, or the ones you add). An operation that is not in the list is not attacked.

## Packages

The packages are not published to NuGet yet. Until they are, reference them from a checkout.
