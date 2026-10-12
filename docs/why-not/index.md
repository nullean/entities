---
navigation_title: Why not
description: How {{product}} differs from validation libraries, database constraints, plain property testing, and hand-written scenario tests.
---

# Why not something else

## FluentValidation and DataAnnotations

They validate one value, at one moment: is this request valid. {{product}} also validates that way (`Validate(value)`), and then goes where they cannot: a rule about a step (twice is once), a rule about two states (a version never goes back), a rule about a sequence (a deadline). Those rules are declared the same way and attacked by generated tests; a validator has nothing to say about them.

## Database constraints

`CHECK`, `UNIQUE` and foreign keys are about one row at a time. Rules about how a row changes, or about what happens in time, do not fit. They also live in the database, away from the type, and run too late for a service to reason about. Keep the ones that belong there; declare the rest next to the type.

## Plain CsCheck, FsCheck or Hypothesis

They are the engine, and {{product}} uses CsCheck. With a property-testing library you write every property: which inputs, which law, which comparison. With {{product}} you declare the rule and the shape picks the property. The library also checks the thing property tests leave to you: that the generated inputs reached the rule.

## Scenario tests

A scenario test covers the case its author thought of. The cases that matter (a retry, an order of events, a deadline missed by one gap) are the ones nobody wrote. Keep scenario tests for what a rule cannot say; let rules cover the laws.

## Types alone

A record and a union say what shape a value has, and the compiler checks the shape for free. They cannot say what must be true of the values, of the steps between them, or of the events over time. {{product}} adds that, and does not replace the types.
