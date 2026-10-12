---
navigation_title: The nine axes
description: Nine questions the library asks of the rules you declare, each a different kind of bug. One page each.
---

# The nine axes

An **axis** is one kind of question you can ask of a rule. Nine of them cover the behaviour a type cannot state. You declare a rule; the library asks every question that applies to its shape.

| Axis | The question |
|---|---|
| [01 Invariant](01-invariant.md) | Is it true in every state? |
| [02 Idempotency](02-idempotency.md) | Does doing it twice change anything? |
| [03 Monotonicity](03-monotonicity.md) | Does it ever go backwards? |
| [04 Conservation](04-conservation.md) | Did anything appear or vanish? |
| [05 Absence](05-absence.md) | Can this bad sequence ever happen? |
| [06 Temporal](06-temporal.md) | Does B follow A within a deadline? |
| [07 Liveness](07-liveness.md) | Does it eventually finish? |
| [08 Confluence](08-confluence.md) | Does the order inputs arrive in matter? |
| [09 Vacancy](09-vacancy.md) | Did the test even reach the rule? |

Each page is built the same way: the question in plain words, the bug you would ship, the rule you declare, what the library does, and the real failure you read. Start with whichever sounds like a bug you have had.

## Which questions are about what

| The rule is about | Axes |
|---|---|
| One state | 01 Invariant |
| A step from one state to the next | 02 Idempotency, 03 Monotonicity, 04 Conservation |
| Events over time | 05 Absence, 06 Temporal, 07 Liveness |
| The order inputs arrive in | 08 Confluence |
| The test itself | 09 Vacancy |

## Running them

```csharp
AutoTest.For(OrderConstraints.Instance, orders).Run();          // the state axes: 01 to 04, 08 and 09
EventAutoTest.For(rules, script, time => new Service(time), s => s.Log).Run();   // the event axes: 05 to 07
```

`Run()` throws one exception listing every axis that failed. To run one: `Invariant()`, `Idempotency()`, `Monotonicity()`, `Conservation()`, `Confluence()`, `Vacancy()`; for events `Absence()`, `Temporal()`, `Liveness()`. For your test framework, `AsDataSource()` gives one test per axis the rules declare.

An axis the type has no rule for is reported as *not applicable*; it is never a silent pass. `RequireAxes(...)` turns "not applicable" into a failure when you expect a rule there.

## How to read a failure

Every failure has the same labels, so you learn them once.

| Label | Meaning |
|---|---|
| **Rule broken** | The rule, by name, and what it required |
| **Operation** / **Calls made** | What was done to cause it |
| **Started from** / **Produced** | The value before and after |
| **Events recorded** | For the event axes: what the service did, with times |
| **Law** | The question this axis asks |
| **Consequence** | What goes wrong in production if it fails |
| **Smallest case found** | The library tried smaller cases and kept the smallest that still fails |
| **.Seed("…")** | Pass it back to repeat exactly this case while you fix the bug |

The [concepts](../concepts.md) page explains smallest case and seed.
