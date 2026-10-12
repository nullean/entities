---
navigation_title: The nine axes
description: The nine kinds of test, the question each asks, the rule you declare for it, and what the test does.
---

# The nine axes

Every rule is about one state, two states, or a sequence of events. These are the nine tests that follow. `AutoTest` runs the ones your rules declare; an axis with no rule is reported as not applicable, and `RequireAxes(...)` turns that into a failure.

| Rule is about | Axis | The question | You declare | The test does |
|---|---|---|---|---|
| One state | 01 Invariant | Is it true in every state? | `Always`, `When…Requires`, `Each`, `Unique`, … | Generated valid values break nothing; every operation keeps the rules |
| Two states (a step) | 02 Idempotency | Does doing it twice change anything? | `Idempotent(f)` | Runs `f` twice on generated values |
| | 03 Monotonicity | Does it ever go backwards? | `Monotonic`, `MonotonicPerKey` | Runs operations, compares before and after |
| | 04 Conservation | Did anything appear or vanish? | `Conserves(select)` | Runs operations, compares the quantity |
| Events over time | 05 Absence | Can this sequence ever happen? | `Temporal<T>.Never`, `Always`, `Transitions` | Random calls on a virtual clock |
| | 06 Temporal | Does B follow A within a deadline? | `After().Then().Within()`, `AfterUntil` | Delays of 0 and just either side of each deadline |
| | 07 Liveness | Does it eventually finish? | `Temporal<T>.Eventually` | Judged when the run settles |
| Arrival order | 08 Confluence | Does the order inputs arrive in matter? | `OrderIndependent` | Shrinkable permutations of the items |
| The test itself | 09 Vacancy | Did the test reach the rule? | nothing: automatic | Fails if a rule could not fire or was never exercised |

## How to read a result

Each axis returns one of four results.

- **Passed**, with the rules it attacked and a note saying how much it exercised.
- **Not applicable**: the type declares no rule on this axis. This is reported, never a silent pass.
- **Skipped**, with the reason (for example, no operations were given for a step axis).
- **Failed**, with the law that broke, what it costs when it does, the smallest case and a seed.

Run them all with `Run()`, which throws one exception listing every failed axis. Or run one at a time: `Invariant()`, `Idempotency()`, `Monotonicity()`, `Conservation()`, `Confluence()`, `Vacancy()`. For the event axes, `EventAutoTest` has `Absence()`, `Temporal()`, `Liveness()` and `Vacancy()`.

## What the nine have in common

Each asks a question that cannot be asked of one value on its own. That is why a scenario test rarely covers them, and why [the gallery](../bugs/index.md) is full of bugs that were green in review.
