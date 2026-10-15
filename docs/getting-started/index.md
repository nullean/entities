---
navigation_title: Getting started
description: Declare the rules of an order, run the tests they imply, and learn to read the first failure. About five minutes.
---

# Getting started

You will declare the rules of an order, run the tests they imply, and learn to read a failure. The code is the [Quickstart example](https://github.com/nullean/entities/tree/main/examples/Nullean.Entities.Quickstart) in the repository. It compiles and runs in CI, so what you read here works. New words are explained in [concepts](../concepts.md).

## Install

```sh
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.CsCheck   # tests only
```

## 1. The type

An order is an immutable record. Every method returns the next order. Nothing here refers to the library.

<!-- file: examples/Nullean.Entities.Quickstart/Order.cs -->
```csharp
namespace Nullean.Entities.Quickstart;

public enum OrderStatus { Pending, Confirmed, Shipped, Cancelled }

public sealed record OrderLine(string Sku, int Quantity, decimal Price);

// An immutable record: every method returns the next order.
public sealed record Order(IReadOnlyList<OrderLine> Lines, OrderStatus Status, int Version)
{
	public static Order New() => new([], OrderStatus.Pending, 0);

	public decimal Total => Lines.Sum(l => l.Quantity * l.Price);

	public string Label => string.Join(",", Lines.Select(l => l.Sku).Order(StringComparer.Ordinal));

	public Order AddLine(OrderLine line) =>
		Status == OrderStatus.Pending && line.Quantity > 0 && line.Price >= 0 ? this with { Lines = [.. Lines, line], Version = Version + 1 } : this;

	public Order Confirm() => Status == OrderStatus.Pending && Lines.Count > 0 ? this with { Status = OrderStatus.Confirmed, Version = Version + 1 } : this;

	public Order Ship() => Status == OrderStatus.Confirmed ? this with { Status = OrderStatus.Shipped, Version = Version + 1 } : this;

	public Order Cancel() =>
		Status is OrderStatus.Pending or OrderStatus.Confirmed ? this with { Status = OrderStatus.Cancelled, Version = Version + 1 } : this;
}
```

## 2. The rules

Each rule is a named value next to the type. `[Constraints]` gathers every static field into `OrderConstraints.All`, so a rule you add cannot be forgotten.

<!-- file: examples/Nullean.Entities.Quickstart/OrderConstraints.cs -->
```csharp
namespace Nullean.Entities.Quickstart;

// Every rule the Order must keep, written once, next to the type. [Constraints] gathers the static fields into OrderConstraints.All.
[Constraints]
public static partial class OrderConstraints
{
	// one state: true in every state
	public static readonly Constraint<Order> TotalNeverNegative = Constraints<Order>.Always(o => o.Total >= 0, "An order total is never negative.");

	public static readonly Constraint<Order> ConfirmedOrdersHaveLines = Constraints<Order>
		.When(o => o.Status == OrderStatus.Confirmed)
		.Requires(o => o.Lines.Count > 0, "A confirmed order has at least one line.");

	// two states: the legal moves between them
	public static readonly Constraint<Order> LegalMoves = Constraints<Order>
		.Transitions(o => o.Status)
		.Allow(OrderStatus.Pending, OrderStatus.Confirmed)
		.Allow(OrderStatus.Confirmed, OrderStatus.Shipped)
		.Allow(OrderStatus.Pending, OrderStatus.Cancelled)
		.Allow(OrderStatus.Confirmed, OrderStatus.Cancelled);

	// a step: doing it twice is doing it once
	public static readonly Constraint<Order> CancelIsRepeatable = Constraints<Order>.Idempotent(o => o.Cancel());

	// a step: a value that only moves one way
	public static readonly Constraint<Order> VersionNeverGoesBack = Constraints<Order>.Monotonic(o => o.Version, MonotonicDirection.NonDecreasing);

	// order: the label does not depend on the order the lines were added in
	public static readonly Constraint<Order> LabelIgnoresLineOrder = Constraints<Order>.OrderIndependent(
		o => o.Lines,
		(o, lines) => o with { Lines = lines },
		o => o.Label
	);
}
```

Read the comments. Each kind of rule is a different kind of question: one state, a move between states, a step, an order. You did not choose a test for any of them. The shape of the rule does that: see [the nine axes](../axes/index.md).

## 3. The test

You need two things: a generator of orders, and one test method that runs one test per axis your rules declare.

<!-- file: tests/Nullean.Entities.Quickstart.Tests/QuickstartTests.cs -->
```csharp
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Quickstart;

namespace Nullean.Entities.Quickstart.Tests;

// The whole test suite for the Order: one declaration of its rules, one auto-test, one test per axis the rules declare.
public class QuickstartTests
{
	private static readonly Gen<OrderLine> Lines = Gen.Select(
		Gen.OneOfConst("apple", "pear", "fig"),
		Gen.Int[1, 5],
		Gen.Int[1, 20],
		(sku, quantity, price) => new OrderLine(sku, quantity, price)
	);

	private static readonly Gen<Order> Pending = Lines.Array[0, 3].Select(lines => lines.Aggregate(Order.New(), (order, line) => order.AddLine(line)));

	// orders in every state, so every rule has something to attack
	private static readonly Gen<Order> Orders = Gen.Select(Pending, Gen.Int[0, 3], (order, step) =>
			step switch
			{
				1 => order.Confirm(),
				2 => order.Confirm().Ship(),
				3 => order.Cancel(),
				_ => order
			});

	public static AutoTest<Order> Auto() => AutoTest.For(OrderConstraints.Instance, Orders);

	public static IEnumerable<Func<AxisCase>> Axes() => Auto().AsDataSource();

	[Test]
	[MethodDataSource(nameof(Axes))]
	public Task EveryDeclaredAxisHolds(AxisCase axis) => axis.RunAsync();
}
```

`AutoTest.For(OrderConstraints.Instance, Orders)` reads the rules and finds the operations of `Order` itself: every public method that returns an `Order`. `AsDataSource()` hands your test framework one case per axis, so a failing axis fails only its own test.

Run it. It passes. Now look at the generator, because that is the part to get right.

## 4. The first red: a test that proved nothing

Suppose the generator made only pending orders. Every rule holds, so every test would pass. But no generated order is ever confirmed, so nothing checked the rule about confirmed orders. {{product}} refuses to call that a pass:

:::{include} ../_snippets/vacancy.txt
:language: text
:::

Read it top to bottom. **Rule** is the rule that was never checked. **Not exercised because** says why. **Next** says what to change. This is [axis 09, Vacancy](../axes/09-vacancy.md), and it applies to every other axis too: a test must prove it ran. The fix is the generator above, which makes orders in every state.

## 5. The second red: a real bug

Add an operation that forgets a check:

```csharp
AutoTest.For(OrderConstraints.Instance, orders)
	.Action("AddRawLine", o => o with { Lines = [.. o.Lines, new OrderLine("fig", 1, -5)] })
```

:::{include} ../_snippets/invariant.txt
:language: text
:::

How to read this:

| Line | What it tells you |
|---|---|
| **Rule broken** | The rule that did not hold, by name. Here, a total went below zero. |
| **Operation** | What was done. `AddRawLine()` is the operation you just added. |
| **Started from** | The order before. It was valid: empty and pending. |
| **Produced** | The order after. A line with a price of -5 gave a total of -5. |
| **Law** | The question this test asks, in one sentence. |
| **Consequence** | What this kind of bug does in production. |
| **This is the smallest case** | The library tried smaller orders and kept the smallest that still breaks the rule. A short failure is easy to read. |
| **.Seed("…")** | The inputs are random but repeatable. Pass this back to run exactly the same case while you fix the bug. |

## Where next

- [The nine axes](../axes/index.md): each question, the bug it catches, and the real output.
- [Adopting in an existing codebase](../guides/adopting.md).
- [Counter-examples](../guides/counter-examples.md): why every rule needs one, and how the library finds most of them.
- [Services and mutable classes](../guides/services.md): the same `AutoTest` for a service or a class that changes in place.
