---
navigation_title: "04 Conservation"
description: "Axis 04, Conservation: Did anything appear or vanish?"
---

# Axis 04 · Conservation: Did anything appear or vanish?

## In plain words

Moving money between your checking and savings accounts. The total is the same before and after. Nothing was created, nothing was destroyed. A quantity that must be *conserved* is one that operations move around but never change.

## The bug you would ship

A transfer took a one-unit fee. The two accounts together held less after the transfer than before. Totals drift, a little at a time, and nobody notices until the books do not balance.

## You declare

```csharp
public static readonly Constraint<Ledger> MoneyIsConserved = Constraints<Ledger>.Conserves(l => l.Total);
```

## The library does

It runs the operations from many starting values and compares the quantity you named (here, the total) before and after each. It checks that some operation changed the value at all.

## When it fails, you read

:::{include} ../_snippets/conservation.txt
:language: text
:::

How to read this:

- **Rule broken** says what was conserved and what happened: it was 39 and became 38.
- **Operation** is the transfer, with its amount. **Started from** and **Produced** show where the unit went.

## Why you did not write this test

A scenario test checks the amounts it moved. It rarely checks that the sum of everything is unchanged across every operation.

## In real code

In docs-builder, a deploy planner counted skipped files in its total, so the total no longer equalled additions plus updates plus deletions.

---

Previous: [03 Monotonicity](03-monotonicity.md) · Next: [05 Absence](05-absence.md) · [All nine axes](index.md)
