---
navigation_title: "03 Monotonicity"
description: "Axis 03, Monotonicity: Does it ever go backwards?"
---

# Axis 03 · Monotonicity: Does it ever go backwards?

## In plain words

An odometer. It only goes up. A version number, a count of processed items, a "last updated" time: values that may only move one way. If one goes back, something has overwritten newer data with older data.

## The bug you would ship

An operation reset the version to zero. An older result silently overwrote a newer one, and nothing said so. In a distributed system this is how a stale write wins.

## You declare

```csharp
public static readonly Constraint<Order> VersionNeverGoesBack = Constraints<Order>.Monotonic(o => o.Version, MonotonicDirection.NonDecreasing);
```
For a value kept per key (an entry per repository and branch), `MonotonicPerKey`.

## The library does

It runs the operations of the type from many starting orders, and compares the value before and after each. It also checks that some operation actually moved the value; a rule watching a number nothing ever changes is a rule that was never tested.

## When it fails, you read

:::{include} ../_snippets/monotonicity.txt
:language: text
:::

How to read this:

- **Rule broken** says which way it was allowed to move and how it moved: `moved from 1 to 0`.
- **Operation** is `Reset()`. **Started from** and **Produced** differ only in the version.

## Why you did not write this test

A scenario test checks the version after one operation. It does not check, for every operation from every state, that the version never decreases.

## In real code

In docs-builder, a rule says an entry of the link registry never moves back to an older update time, and the operation that adds an entry is tested against it.

---

Previous: [02 Idempotency](02-idempotency.md) · Next: [04 Conservation](04-conservation.md) · [All nine axes](index.md)
