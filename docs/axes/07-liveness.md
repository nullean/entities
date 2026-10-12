---
navigation_title: "07 Liveness"
description: "Axis 07, Liveness: Does it eventually finish?"
---

# Axis 07 · Liveness: Does it eventually finish?

## In plain words

Every request eventually gets an answer. Not "within a minute", just "at some point". The bug is the endless spinner: something was started and never finished.

## The bug you would ship

A confirmed order was never shipped (and never cancelled). Nothing crashed. Nothing was reported. The order just stayed confirmed forever.

## You declare

```csharp
Temporal<FulfilmentEvent>.Eventually(
	_ => "order",                                   // which things to track
	e => e is FulfilmentEvent.Confirmed,            // what starts it
	e => e is FulfilmentEvent.Shipped or FulfilmentEvent.Cancelled)   // what ends it
```

## The library does

It runs the same random calls on the virtual clock, lets the run settle, and then checks that everything that was started has ended. Liveness can only be judged at the end of a run, which is why it is a separate question from a deadline.

## When it fails, you read

:::{include} ../_snippets/liveness.txt
:language: text
:::

How to read this:

- **What happened** says what never ended: `1 instance(s) never reached the target event`.
- **Events recorded** shows a confirmation and nothing after it.

## Why you did not write this test

A failure of omission leaves nothing to look at. No error, no log line. Only a rule that says "this must end" notices.

## In real code

In docs-builder, every started validation build must end in "complete" or "cancelled", however many findings it makes on the way.

---

Previous: [06 Temporal](06-temporal.md) · Next: [08 Confluence](08-confluence.md) · [All nine axes](index.md)
