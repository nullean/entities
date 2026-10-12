---
navigation_title: "08 Confluence"
description: "Axis 08, Confluence: Does the order inputs arrive in matter?"
---

# Axis 08 · Confluence: Does the order inputs arrive in matter?

## In plain words

Two people editing a shared list. It should not matter who adds their item first: the final list should be the same. A result is *confluent* when the order of the inputs does not change it.

## The bug you would ship

The result was taken from the first line, so adding the same lines in another order gave another result. Two rebuilds of the same inputs published different output, depending only on the order a directory listing happened to return them.

## You declare

```csharp
public static readonly Constraint<Order> LabelIgnoresLineOrder = Constraints<Order>.OrderIndependent(
	o => o.Lines,                                  // the items
	(o, lines) => o with { Lines = lines },        // how to rebuild with them in another order
	o => o.Label);                                 // the result that must not change
```

## The library does

It takes many orders, lists their items in a different order each time, and compares the result. When it finds a difference it shrinks the reordering to the smallest one that still changes the result. It checks that some order had at least two items, since one item has no other order.

## When it fails, you read

:::{include} ../_snippets/confluence.txt
:language: text
:::

How to read this:

- **Started from** is the order. **Items reordered as** `[1, 0]` means the second item moved to the front.
- **Result** says it differs from the original order's result.

## Why you did not write this test

A scenario test lists items in the order the author typed them. The bug only shows when someone lists them another way.

## In real code

In docs-builder, merging release-note bundles and building the version index are both tested to give the same result whatever order the inputs arrive in.

---

Previous: [07 Liveness](07-liveness.md) · Next: [09 Vacancy](09-vacancy.md) · [All nine axes](index.md)
