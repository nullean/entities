---
navigation_title: Adopting in existing code
description: Five steps to bring a rule-heavy codebase under declared rules without a rewrite, each one useful on its own.
---

# Adopting in an existing codebase

You do not rewrite anything. Pick one type, declare rules you already believe, and let the tests tell you what you did not know. Each step ships on its own.

## 1. Pick one record

Start with a type that is already immutable (a record, or a class you only ever copy). A type that other code mutates in place can be adopted later; see [known limitations](../known-limitations.md).

Good first types are the ones with the most `if` statements about validity in the code around them: a configuration, an order, a plan, a registry.

## 2. Declare three rules you already believe

Write the rules your code already enforces, in your own words, as `Constraint<T>` fields with `[Constraints]` on the class. Do not look for new rules yet.

Then call `OrderConstraints.All.Validate(value)` in the place the value is built (a loader, a constructor, a factory). The rules now run once, in one place, and each finding has a name. If the code already had checks there, delete them as you confirm the rules cover them.

## 3. Make new rules warnings

A rule that is true of your data today but has never been enforced is a rule you cannot make an error on day one. Declare it as a warning, with a date:

```csharp
.WithSeverityUntil(Severity.Warning, new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), Severity.Error)
```

It is reported until the date, and fails the build after it. See [rolling out a rule](rollout.md).

## 4. Add `AutoTest`

```csharp
AutoTest.For(MyConstraints.Instance, myGenerator)
```

You need a generator of valid values. `GenAuto` builds one from the shape of the type; add what a generator cannot guess (a key that is its value's own id, a derived member) with `Normalize<T>` and `Reference<...>`. Run it. Read the first failures: some are bugs, some are rules that are wrong, and some are a generator that never reaches the rule.

For the counter-examples, let the library find what it can and write only the rest: see [counter-examples](counter-examples.md).

## 5. Add the step axes for the operations that have a law

For the operations where "twice is once", "never goes back" or "the total is unchanged" is true, declare `Idempotent`, `Monotonic` or `Conserves`. These are the rules a scenario test rarely covers, so they are where adoption starts to pay back. An operation that reports errors instead of throwing (returns a `Result<T>` with diagnostics) is adapted in one line: an entry it reports errors for is rejected, so the value stays as it was.

## What to leave alone

- Types other code mutates in place.
- Anything that needs I/O to decide (is this email taken). Rules here are synchronous and pure on purpose.
- Rules you cannot state in a sentence. If you cannot say it, you cannot test it.

## What it costs

A rule is a field. A generator is a few lines per type. A counter-example is a line for the rules the library cannot find one for. The tests are one method per type.
