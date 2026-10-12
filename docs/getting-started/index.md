---
navigation_title: Getting started
description: Declare the rules of an order, run every test they imply, and read the first failure. About five minutes.
---

# Getting started

You will declare the rules of an order, run the tests they imply, and see what the first failure looks like. The code is the [Quickstart example](https://github.com/nullean/entities/tree/main/examples/Nullean.Entities.Quickstart) in the repository; it compiles and runs in CI, so what you read here works.

## Install

```sh
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.CsCheck   # tests only
```

## 1. The type

An immutable record. Every method returns the next order. Nothing refers to the library.

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

Each rule is a named value next to the type. `[Constraints]` gathers every static field into `OrderConstraints.All`, so a rule you add and forget to register cannot happen.

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

Read the comments. The first two rules are about one state. `LegalMoves` is about the move between two. `CancelIsRepeatable` and `VersionNeverGoesBack` are about a step. `LabelIgnoresLineOrder` is about the order things arrive in. Each of these is a different kind of test; you did not pick one. See [the nine axes](../design-principles/axes.md).

## 3. The test

You need two things: a generator of orders, and one test method that runs one test per axis the rules declare.

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
	public void EveryDeclaredAxisHolds(AxisCase axis) => axis.Run();
}
```

`AutoTest.For(OrderConstraints.Instance, Orders)` reads the rules and the operations of `Order` (every public method that returns an `Order`). `AsDataSource()` gives your test framework one case per axis, so a failing axis fails only its own test.

Run it. It passes. Now look at what the generator does, because that is the part to get right.

## 4. The first red

The generator above draws orders in every state. Suppose it drew only pending ones:

```csharp
AutoTest.For(OrderConstraints.Instance, pendingOnly)
```

Every rule would hold, and the tests would pass. They would also prove nothing about `ConfirmedOrdersHaveLines`, because no generated order was ever confirmed. {{product}} says so:

:::{include} ../_snippets/vacancy.txt
:language: text
:::

That is the fourth principle: [a green test must prove it ran](../design-principles/index.md). Mix confirmed, shipped and cancelled orders into the generator, as above, and it passes for the right reason.

## 5. Break something

Add an operation that forgets a check:

```csharp
AutoTest.For(OrderConstraints.Instance, orders)
	.Action("AddRawLine", o => o with { Lines = [.. o.Lines, new OrderLine("fig", 1, -5)] })
```

:::{include} ../_snippets/invariant.txt
:language: text
:::

The case is shrunk to the smallest one, and the seed replays it exactly. Paste it into the test (`.Seed("...")`) while you fix the bug.

## Where next

- [The bugs you did not write a test for](../bugs/index.md): five more, and the output each produces.
- [Adopting in an existing codebase](../guides/adopting.md).
- [Counter-examples](../guides/counter-examples.md): why every rule needs one, and how the library finds most of them.
