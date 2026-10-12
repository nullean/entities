---
navigation_title: "01 Invariant"
description: "Axis 01, Invariant: Is it true in every state?"
---

# Axis 01 · Invariant: Is it true in every state?

## In plain words

A rule that must be true every single time you look at the thing. An order total is never negative. A confirmed order has lines. It does not matter how the order got into its state: if you look, the rule holds.

## The bug you would ship

A method added a line with a negative price and the total dropped below zero. Every order the tests had made was valid, so a test that only looked at made-up orders never saw it. The bug was not in a value. It was in an operation.

## You declare

```csharp
public static readonly Constraint<Order> TotalNeverNegative = Constraints<Order>.Always(o => o.Total >= 0, "An order total is never negative.");

public static readonly Constraint<Order> ConfirmedOrdersHaveLines = Constraints<Order>
	.When(o => o.Status == OrderStatus.Confirmed)
	.Requires(o => o.Lines.Count > 0, "A confirmed order has at least one line.");

public static readonly Constraint<Order> LegalMoves = Constraints<Order>
	.Transitions(o => o.Status)
	.Allow(OrderStatus.Pending, OrderStatus.Confirmed)
	.Allow(OrderStatus.Confirmed, OrderStatus.Shipped);
```
Also `Each`, `Unique`, `References`, `OneOf` and the others in [the vocabulary](../design-principles/vocabulary.md).

## The library does

It makes many valid orders and checks every rule on each. Then it runs every operation of the type (every public method that returns an `Order`) starting from valid orders, and checks the rules again on what comes out. A rule that is only a warning is allowed through, as your own operations allow it.

## When it fails, you read

:::{include} ../_snippets/invariant.txt
:language: text
:::

How to read this:

- **Rule broken** names the rule that did not hold, and says what it required.
- **Operation** is the call that did it. `AddRawLine()` took no argument.
- **Started from** is a valid order. **Produced** is what the operation gave back, and it is not valid.

## Why you did not write this test

A scenario test builds the orders its author imagined. A rule broken by a method nobody thought to call with a bad input stays invisible until production.

## In real code

In docs-builder, running the rules over generated data found two rules that crashed on hostile values (a null default call-to-action; a path prefix that is not a legal URL), before any user did.

---

Next: [02 Idempotency](02-idempotency.md) · [All nine axes](index.md)
