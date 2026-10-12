---
navigation_title: The vocabulary
description: The declarations on Constraints<T> and Temporal<T>, grouped by shape, with the axis each belongs to and how the test knows it was exercised.
---

# The vocabulary

`Constraints<T>` has 36 factories, and `Temporal<T>` has a handful more. They fall into a few shapes. The shape is what the test reads (see [principle 3](index.md)).

| Shape | Declarations | Axis | Exercised when |
|---|---|---|---|
| A predicate on one value | `Always`, `Check`, `Matches`, `OneOf`, `NotInFuture`, `NotOlderThan`, `RatioAtMost`, … | 01 | A counter-example breaks it |
| A predicate on parts | `Each`, `EachCheck`, `Unique`, `References`, `Acyclic`, `NoChains`, `Sorted`, `CountBetween`, … | 01 | A counter-example breaks it |
| If X then Y | `When(x).Requires(y)` | 01 | The guard is true for a generated value |
| Legal moves | `Transitions(select).Allow(a, b)` | 01 | Every edge is reached |
| A step is repeatable | `Idempotent(f)` | 02 | `f` changed a generated value |
| A step never goes back | `Monotonic`, `MonotonicPerKey` | 03 | An operation moved the value |
| A step keeps a quantity | `Conserves(select)` | 04 | An operation changed the value |
| Order does not matter | `OrderIndependent(items, withItems, result)` | 08 | A value had two or more items |
| Events over time | `Temporal<T>`: `Never`, `Always`, `Transitions`, `After…Within`, `AfterUntil`, `Eventually` | 05 to 07 | The trigger was seen; a deadline was crossed |

## Reading the table

Most declarations are invariants. That is expected: most rules about a domain are. The shapes that carry more than a predicate (`Idempotent`, `Monotonic`, `Conserves`, `OrderIndependent`, `Transitions`, and the temporal rules) are the ones that unlock a test you would not otherwise have written, so prefer them when they say what you mean.

## When to use which

| You mean | Use |
|---|---|
| "this is never true" / "this is always true" | `Always` |
| "if it is confirmed, it has lines" | `When(...).Requires(...)` |
| "only these moves are legal" | `Transitions(...).Allow(...)` |
| "doing it again changes nothing" | `Idempotent` |
| "it only goes up" | `Monotonic` |
| "the total is unchanged by this operation" | `Conserves` |
| "the result does not depend on the order of the inputs" | `OrderIndependent` |
| "this must follow that within a minute" | `Temporal<T>.AfterUntil` or `After().Then().Within()` |
| "every one of these must end" | `Temporal<T>.Eventually` |

A rule none of these can express (a relation between two values nothing here names) is still a `Check` or a `CheckTransition`. It is validated, but no axis can aim at it, and the report lists it as unclassified so you know.
