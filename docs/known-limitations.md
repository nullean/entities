---
navigation_title: Known limitations
description: What {{product}} does not do, and what to do instead.
---

# Known limitations

## Rules are runtime checks

A rule is checked when you validate a value or run a test, not when you compile. The `[Constraints]` generator makes the set exhaustive at build time (a rule you declare is in `All`), but whether a value satisfies a rule is decided at run time.

## Rules are synchronous and pure

A rule cannot call a database or a service. "Is this email taken" is not a rule here; do it in the application service. This is on purpose: a rule that does I/O cannot be generated against.

## The generator is yours to get right

`GenAuto` builds values from the shape of a type. It cannot guess a relation between members, a key that is its value's own id, or what makes a whole structure valid. You declare those. A generator that never reaches a rule is reported (the Vacancy axis), but you have to fix it.

## Some counter-examples need a person

The search finds a counter-example for most rules, not all. A rule about a specific value, or about a structure built to your domain's shape, needs one you write. See [counter-examples](guides/counter-examples.md).

## Immutable types fit best

A record whose methods return the next value is what the step axes run on. A class that other code mutates in place can be tested with a replay (copy it, run the operation, compare), but it is a second-class path. Entities mapped by an ORM are the same: declare the rules on the domain record and map to it.

## The test is only as good as the operations you give it

The step axes run the operations of the type (every public method that returns the type, or the ones you add). An operation that is not in the list is not attacked.

## Packages

The packages are not published to NuGet yet. Until they are, reference them from a checkout.
