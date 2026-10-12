---
navigation_title: Home
description: Declare each rule of your domain once. The library picks the test, runs it on generated data, and fails if the test never reached the rule.
---

# Nullean.Entities

Declare each rule of your domain once. {{product}} picks the test, runs it on generated data, and fails if the test never reached the rule.

C# records and unions say what shape your domain has. They cannot say that a total is never negative, that cancelling twice is cancelling once, that a version never goes back, or that a confirmed order ships within a minute. Those rules end up in service methods, validators and scenario tests, written again for each use, and they drift apart.

Here is an order with a few of those rules, written once, next to the type:

```csharp
[Constraints]
public static partial class OrderConstraints
{
	public static readonly Constraint<Order> TotalNeverNegative = Constraints<Order>.Always(o => o.Total >= 0, "An order total is never negative.");

	public static readonly Constraint<Order> CancelIsRepeatable = Constraints<Order>.Idempotent(o => o.Cancel());

	public static readonly Constraint<Order> VersionNeverGoesBack = Constraints<Order>.Monotonic(o => o.Version, MonotonicDirection.NonDecreasing);
}
```

One line runs every test those rules imply:

```csharp
AutoTest.For(OrderConstraints.Instance, orders).Run();
```

You wrote no test. The shape of each rule chose the technique: `Idempotent` runs the action twice, `Monotonic` runs the operations and compares before and after. And when a method of `Order` breaks a rule, this is what you read:

:::{include} _snippets/monotonicity.txt
:language: text
:::

It names the axis, the rule, the smallest case that breaks it, and a seed that replays it.

## What you get

- **Rules in one place.** A rule is a named value. The generator gathers them into `All`, so a rule you add cannot be forgotten.
- **Tests you do not write.** Nine kinds of test, chosen from the shape of the rule. See [the nine axes](design-principles/axes.md).
- **A test that proves it ran.** A rule the generated data never reached fails as vacuous, instead of passing quietly.
- **Failures you can act on.** The smallest failing case, the rule that broke, and a seed.
- **A way in for existing code.** Rules start as warnings and become errors on a date. See [adopting in an existing codebase](guides/adopting.md).

## Where next

- [Getting started](getting-started/index.md): five minutes, one order, one red test.
- [The bugs you did not write a test for](bugs/index.md): six bugs, the rule that catches each, the real output.
- [Design principles](design-principles/index.md): the five ideas everything else follows from.
