---
navigation_title: "05 Absence"
description: "Axis 05, Absence: Can this bad sequence ever happen?"
---

# Axis 05 · Absence: Can this bad sequence ever happen?

## In plain words

Something that must never happen, whatever else happens first. A cancelled order is never shipped. This is not about one value; it is about the order of events over time.

## The bug you would ship

A service scheduled a shipment when an order was confirmed, and cancelling the order did not cancel the shipment. Confirm, then cancel, then the shipment still went out.

## You declare

```csharp
Temporal<FulfilmentEvent>.Never<FulfilmentEvent.Cancelled, FulfilmentEvent.Shipped>((_, _) => true)
```
Also `Always` for a rule about every event, and `Transitions` for the legal order of events.

## The library does

It drives your real service with random calls (confirm, cancel, in any order) on a virtual clock, records the events the service emits with their times, and checks the rule against that log. Nothing waits in real time.

## When it fails, you read

:::{include} ../_snippets/absence.txt
:language: text
:::

How to read this:

- **Events recorded** is what the service did, with times relative to the first event.
- **Calls made** is what the test did to cause it: wait 11 seconds, confirm, wait 6 seconds, cancel.
- **What happened** names the forbidden sequence: `Cancelled, then Shipped`.

## Why you did not write this test

A scenario test picks one order of calls. The forbidden sequence happens under the order, or the timing, the author did not pick.

## In real code

In docs-builder, the live build behind `docs-builder serve` is tested this way: its events must always arrive as start, then findings, then an end, whatever the saves and timing.

---

Previous: [04 Conservation](04-conservation.md) · Next: [06 Temporal](06-temporal.md) · [All nine axes](index.md)
