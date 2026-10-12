---
navigation_title: "02 Idempotency"
description: "Axis 02, Idempotency: Does doing it twice change anything?"
---

# Axis 02 · Idempotency: Does doing it twice change anything?

## In plain words

Pressing the lift button twice. The lift still comes once. An operation is *idempotent* when doing it again changes nothing: cancelling a cancelled order leaves it cancelled and untouched.

## The bug you would ship

Cancelling an order that was already cancelled bumped its version again. A retry after a timeout, a user double-clicking, a message delivered twice: each one changed the state a second time, so the order ended up different from a single run.

## You declare

```csharp
public static readonly Constraint<Order> CancelIsRepeatable = Constraints<Order>.Idempotent(o => o.Cancel());
```

## The library does

It makes many orders, applies the operation, applies it again to the result, and compares the two. It also checks that the operation changed at least some of the orders: if it never changed any, the test would pass without proving anything, so it fails as vacuous instead.

## When it fails, you read

:::{include} ../_snippets/idempotency.txt
:language: text
:::

How to read this:

- **Started from** is the order before the operation.
- **Once gave** is the order after one cancel. **Twice gave** is the order after cancelling that again. They differ (the version), and they must not.

## Why you did not write this test

An example test calls the operation once and checks the result. Calling it twice is a different test, and it only gets written by someone who has been bitten by retries.

## In real code

In docs-builder, applying a changelog bundle's amends twice used to give a different bundle from applying them once.

---

Previous: [01 Invariant](01-invariant.md) · Next: [03 Monotonicity](03-monotonicity.md) · [All nine axes](index.md)
