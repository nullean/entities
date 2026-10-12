---
navigation_title: Concepts
description: The few ideas the rest of the docs use, in plain words. Read this once; every other page links back here.
---

# Concepts, in plain words

You do not need to know property testing or domain modelling to use {{product}}. You need about ten words. Here they are.

## Domain model

The types that describe your business: an `Order`, a `Ledger`, a `Docset`. In C#, records. A domain model says what things *are*.

## Rule (constraint)

A statement about the domain that must be true: "an order total is never negative", "you can only ship a confirmed order". {{product}} calls a rule a **constraint**, and writes it as a named value next to the type. A type cannot say these things, so they usually live in `if` statements and validators.

## Operation

A method that takes a value and gives you the next one: `order.Confirm()`, `ledger.MoveToSavings(10)`. The library finds the operations of your type by itself (every public method that returns the type).

## Example test and property test

An **example test** says: for this input, expect this output. It covers the case its author thought of.

A **property test** says: for *every* input of this kind, this is true. A tool makes up many inputs, runs your check on each, and reports the first one that breaks it. {{product}} writes the property tests for you, from the rules.

## Generated data

The inputs a property test makes up: many orders, with different lines, in different states. {{product}} builds them from the shape of your type, and mixes in nasty values on purpose (empty strings, negative numbers, duplicates) because that is where bugs are.

## Smallest case (shrinking)

When a generated input breaks a rule, it is usually large and noisy. The tool then tries smaller and smaller versions of it and keeps the smallest one that still breaks the rule. A failure that says "an order with no lines" is far easier to understand than one with five lines and a made-up address. The library calls this "smallest case found".

## Replay (seed)

Generated data is random, but the randomness is repeatable. A failure prints a short code, the **seed**. Pass it back with `.Seed("…")` and the test makes exactly the same inputs and fails the same way, so you can fix the bug and re-run the same case.

## Counter-example

To prove a rule *can* fail, you need one value that breaks it. That value is its counter-example: "an order with a negative price" for "the total is never negative". If a rule has no counter-example, you cannot tell a rule that holds from a rule that is never checked. The library finds most counter-examples itself; see [counter-examples](guides/counter-examples.md).

## Axis

A kind of question you can ask of a rule. {{product}} tests nine of them: [the nine axes](axes/index.md). Each is a different kind of bug.

## Vacuous

A test is *vacuous* when it passes without proving anything, because the data never reached the rule it was meant to check. A smoke alarm test where nobody lit a match. {{product}} fails a vacuous test instead of letting it look green.

## Virtual clock

For rules about time ("ships within a minute"), the test does not wait. It gives your service a clock it controls and moves that clock forward by chosen amounts, so a test of a 60-second deadline takes milliseconds.
