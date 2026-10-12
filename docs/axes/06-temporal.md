---
navigation_title: "06 Temporal"
description: "Axis 06, Temporal: Does B follow A within a deadline?"
---

# Axis 06 · Temporal: Does B follow A within a deadline?

## In plain words

A promise with a deadline. A confirmed order ships within a minute. The question is not whether it ships, but whether it ships in time, however the time between calls is spent.

## The bug you would ship

The service shipped 90 seconds after confirming. The promise says 60. With nobody watching a clock, the order sat "in progress" for good.

## You declare

```csharp
Temporal<FulfilmentEvent>.AfterUntil(
	e => e is FulfilmentEvent.Confirmed,
	(_, next) => next is FulfilmentEvent.Shipped,
	TimeSpan.FromSeconds(60),
	isInterrupt: e => e is FulfilmentEvent.Cancelled)   // a cancelled order is excused
```
Or in the fluent form: `After<Confirmed>().Then<Shipped>().Within(60s)`.

## The library does

It chooses the delays itself: none, and just before and just after the deadline, so a deadline is both met and missed within a few tries. The clock is virtual, so a 60-second deadline costs milliseconds. It fails as vacuous if no run ever went past the deadline, since then the rule could never have failed.

## When it fails, you read

:::{include} ../_snippets/temporal.txt
:language: text
:::

How to read this:

- **Noticed at** is when, after the first event, the deadline passed with nothing.
- **Events recorded** shows the confirmation at `+0s` and the shipment at `+90s`: too late.
- **Calls made** shows the delay the test chose before confirming.

## Why you did not write this test

You would have to pick a delay yourself, and you would pick one that works. The bug lives at the delay you did not try.

## In real code

In docs-builder, a build that overruns half an hour is a rule: otherwise the editor's panel can say "building" forever.

---

Previous: [05 Absence](05-absence.md) · Next: [07 Liveness](07-liveness.md) · [All nine axes](index.md)
