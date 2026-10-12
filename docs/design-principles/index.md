---
navigation_title: Design principles
description: The five ideas {{product}} follows from. If you remember these, you can predict what the library does.
---

# Design principles

Five ideas. The rest of the library follows from them. Words in italics are explained in [concepts](../concepts.md).

## 1. Say a rule once, next to the type

A rule is a named value (`Constraint<T>`) declared beside the record it governs. `[Constraints]` gathers every static rule on the class into one set, so a rule you add is in the set without a registration step to forget.

The same declaration checks a value where it is built, is attacked by tests, and can watch a live stream of events. You do not write the rule again for each of those.

## 2. A rule is about one state, two states, or a sequence of events

That is all a rule can be about, and it gives the nine kinds of test in [the nine axes](../axes/index.md):

- **One state.** True in every state: a total is never negative.
- **Two states** (a step). What an operation must or must not do: twice is once, a version never goes back, a quantity is conserved, the move is a legal one.
- **Events over time.** What must never happen, what must follow within a deadline, what must eventually happen.

If you can say which of those a rule is, you know which test it needs.

## 3. The declaration has a shape, and the shape picks the test

`Always(predicate)` is only a predicate, so the test is "generated valid values satisfy it". `Idempotent(f)` carries `f`, so the test runs `f` twice. `Monotonic(select, direction)` carries what to compare, so the test runs operations and compares before and after. `OrderIndependent(items, withItems, result)` carries how to reorder the items, so the test permutes them.

You do not choose a test. You choose the declaration that says what you mean, and the library reads its shape. [The vocabulary](vocabulary.md) lists the declarations and what each is tested with.

## 4. A green test must prove it ran

A test that passes because it never reached the rule is worse than no test, because it looks like evidence. So:

- every rule needs a *counter-example* (a valid value changed so that exactly this rule breaks) to show it can fire (the library finds most of them; see [counter-examples](../guides/counter-examples.md)),
- every `When(...).Requires(...)` guard must be true for some generated value,
- an `Idempotent` action must change some generated value, a `Monotonic` rule must see a value move, an `OrderIndependent` rule must see a value with two items to reorder,
- a deadline rule must see a run that crosses its deadline.

When one of these fails, the test fails as *vacuous* and says what to do next. This is the ninth axis, and it applies to all the others.

## 5. A failure is the smallest case plus a way to replay it

A failing case is cut down to the smallest one that still fails (*shrinking*) and printed with the rule that broke and a *seed* that replays it exactly. Every rule has a name, so a finding can be filtered, escalated, or treated as a warning that becomes an error on a date. That last part is what makes adoption gradual: see [rolling out a rule](../guides/rollout.md).

## In one line

Declare each rule once. The library picks the test, runs it on generated data, and fails if the test never reached the rule.
