---
navigation_title: Home
description: Types hold data. Nothing constrains it beyond its type. Nullean.Entities makes those rules first-class, and tests them on nine axes you have probably never considered.
---

# Nullean.Entities

**Types hold data. Nothing constrains it beyond its type.**

A well-typed `Order` can still have a negative total, ship after it was cancelled, or lose a version. C# records and unions say what an order holds and which cases exist. They cannot say that a total is never negative, that cancelling twice is cancelling once, that a version never goes back, or that a confirmed order ships within a minute. Those rules end up in `if` statements, validators and people's heads, written again for each use, and they drift apart.

{{product}} makes them **first-class**: a rule is a named value, declared once, next to the type.

```csharp
[Constraints<Order>]
public static partial class OrderConstraints
{
	public static readonly Constraint<Order> TotalNeverNegative = Constraints<Order>.Always(o => o.Total >= 0, "An order total is never negative.");

	public static readonly Constraint<Order> CancelIsRepeatable = Constraints<Order>.Idempotent(o => o.Cancel());

	public static readonly Constraint<Order> VersionNeverGoesBack = Constraints<Order>.Monotonic(o => o.Version, MonotonicDirection.NonDecreasing);
}
```

## One declaration, three jobs

1. **It validates.** `OrderConstraints.All.Validate(order)` checks a value where it is built, and a finding names the rule.
2. **It is tested, automatically.** One line runs a test for each of the nine axes your rules declare. You write none of them. [`.Deeper()`](guides/deeper.md) and `.Commutes()` add a proof over every reachable state and a check that two operations commute.
3. **It watches events.** The same vocabulary describes rules about order and time in a live stream.

```csharp
await AutoTest.For(OrderConstraints.Instance, orders).RunAsync();
```

## Nine questions you have probably never asked

The shape of each rule tells the library what to test. It tests things a scenario test almost never covers:

| | The question | The bug it catches |
|---|---|---|
| [01](axes/01-invariant.md) | Is it true in every state? | an operation that lets a bad value in |
| [02](axes/02-idempotency.md) | Does doing it twice change anything? | a retry that changes state twice |
| [03](axes/03-monotonicity.md) | Does it ever go backwards? | an older result overwriting a newer one |
| [04](axes/04-conservation.md) | Did anything appear or vanish? | totals that drift |
| [05](axes/05-absence.md) | Can this bad sequence ever happen? | a cancelled order that still ships |
| [06](axes/06-temporal.md) | Does B follow A within a deadline? | a promise missed by one unlucky gap |
| [07](axes/07-liveness.md) | Does it eventually finish? | the endless spinner |
| [08](axes/08-confluence.md) | Does the order inputs arrive in matter? | two rebuilds publishing different output |
| [09](axes/09-vacancy.md) | Did the test even reach the rule? | a green test that checked nothing |

The ninth is what keeps the others honest. A test that passes because the data never reached the rule is worse than no test, so {{product}} fails it.

## What a failure looks like

Here a method of `Order` resets the version. You wrote no test for it. You read:

:::{include} _snippets/monotonicity.txt
:language: text
:::

Which rule broke, what was done, what it started from and produced, the question it asks and what it costs, and how to run exactly this case again. [How to read it](axes/index.md).

## Where next

- [Why](why.md): the argument, in a page.
- [Concepts](concepts.md): the few words used throughout, in plain words.
- [Getting started](getting-started/index.md): one order, one red test, five minutes.
- [The nine axes](axes/index.md): one page each, with the real output of each.
