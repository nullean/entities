---
navigation_title: The bugs
description: Six bugs a scenario test rarely catches, the rule that catches each one, and the real output it produces.
---

# The bugs you did not write a test for

Each of these is planted in the [Quickstart example](https://github.com/nullean/entities/tree/main/tests/Nullean.Entities.Quickstart.Tests) and caught by a test that runs in CI. The output below is the text those tests wrote, not something typed for this page.

## 1. A test that never reached the rule

Every rule held, and the suite was green. The generator never produced a confirmed order, so the rule about confirmed orders was never checked.

**Caught by** the `ConfirmedOrdersHaveLines` axis. **You declare:**

No declaration: this one is automatic. Every `AutoTest` runs the Vacancy axis.

**You read:**

:::{include} ../_snippets/vacancy.txt
:language: text
:::

## 2. An operation that lets a bad value in

A method added a line with a negative price and the total went below zero. Every generated value was valid, so a test of values alone would never see it.

**Caught by** the `Invariant` axis. **You declare:**

```csharp
public static readonly Constraint<Order> TotalNeverNegative = Constraints<Order>.Always(o => o.Total >= 0, "An order total is never negative.");
```

**You read:**

:::{include} ../_snippets/invariant.txt
:language: text
:::

## 3. A retry that changes state twice

Cancelling an order that is already cancelled bumped its version again. A retry, a double submit or a re-run ends up different from a single run.

**Caught by** the `Idempotency` axis. **You declare:**

```csharp
public static readonly Constraint<Order> CancelIsRepeatable = Constraints<Order>.Idempotent(o => o.Cancel());
```

**You read:**

:::{include} ../_snippets/idempotency.txt
:language: text
:::

## 4. A value that goes backwards

An operation reset the version. An older result overwrote a newer one, and nothing said so.

**Caught by** the `Monotonicity` axis. **You declare:**

```csharp
public static readonly Constraint<Order> VersionNeverGoesBack = Constraints<Order>.Monotonic(o => o.Version, MonotonicDirection.NonDecreasing);
```

**You read:**

:::{include} ../_snippets/monotonicity.txt
:language: text
:::

## 5. Output that depends on the order of the input

The result was taken from the first line, so adding the same lines in another order gave another result. Two rebuilds of one input published different output.

**Caught by** the `Confluence` axis. **You declare:**

```csharp
public static readonly Constraint<Order> LabelIgnoresLineOrder = Constraints<Order>.OrderIndependent(
	o => o.Lines, (o, lines) => o with { Lines = lines }, o => o.Label);
```

**You read:**

:::{include} ../_snippets/confluence.txt
:language: text
:::

## 6. A deadline missed by one unlucky gap

The service shipped 90 seconds after confirming. The rule says 60. The test did not choose the delay: it tried 0 and just either side of 60 seconds, and the clock is virtual, so it took no time.

**Caught by** the `Temporal` axis. **You declare:**

```csharp
Temporal<FulfilmentEvent>.AfterUntil(
	e => e is FulfilmentEvent.Confirmed,
	(_, next) => next is FulfilmentEvent.Shipped,
	TimeSpan.FromSeconds(60));
```

**You read:**

:::{include} ../_snippets/temporal.txt
:language: text
:::

## What they have in common

None of these is a case someone thought to write. In each, the rule is about more than one value: a step, an order, a deadline, or the test itself. You declared the rule; the library chose the inputs.
