---
navigation_title: What this adds to CsCheck
description: CsCheck is excellent on its own and {{product}} is built on it. Here is what you write with plain CsCheck, and what the package adds.
---

# What this adds to CsCheck

[CsCheck](https://github.com/AnthonyLloyd/CsCheck) is a very good property-testing library: fast generators, shrinking that works, seeds that replay a failure exactly, model-based and parallel testing. {{product}} does not replace it. Every generator here is a CsCheck `Gen<T>`, every failure is a CsCheck failure with its shrunk case and seed, and you can mix the two in one test class.

If you are testing an algorithm or a pure function, use CsCheck on its own.

## What you write with plain CsCheck

The same `Order` as in [getting started](../getting-started/index.md), three of its rules, tested by hand:

<!-- file: tests/Nullean.Entities.Quickstart.Tests/PlainCsCheckTests.cs -->
```csharp
using CsCheck;
using Nullean.Entities.Quickstart;

namespace Nullean.Entities.Quickstart.Tests;

// The same Order, tested with plain CsCheck: a library worth using on its own. Every property is written by hand.
public class PlainCsCheckTests
{
	private static readonly Gen<OrderLine> Lines = Gen.Select(
		Gen.OneOfConst("apple", "pear", "fig"),
		Gen.Int[1, 5],
		Gen.Int[1, 20],
		(sku, quantity, price) => new OrderLine(sku, quantity, price)
	);

	private static readonly Gen<Order> Pending = Lines.Array[0, 3].Select(lines => lines.Aggregate(Order.New(), (order, line) => order.AddLine(line)));

	private static readonly Gen<Order> Orders = Gen.Select(Pending, Gen.Int[0, 3], (order, step) =>
			step switch
			{
				1 => order.Confirm(),
				2 => order.Confirm().Ship(),
				3 => order.Cancel(),
				_ => order
			});

	private static readonly Func<Order, Order>[] Operations = [o => o.Confirm(), o => o.Ship(), o => o.Cancel()];

	[Test]
	public void Cancelling_twice_is_cancelling_once() => Orders.Sample(o => o.Cancel().Cancel() == o.Cancel());

	[Test]
	public void No_operation_lowers_the_version() =>
		Gen.Select(Orders, Gen.OneOfConst(Operations)).Sample((order, operation) => operation(order).Version >= order.Version);

	[Test]
	public void The_total_is_never_negative() => Orders.Sample(o => o.Total >= 0);
}
```

That is good code, and it works. Notice what it is made of: a generator for the type, and one property per rule, each written by hand. For every rule you decide the inputs, the law, and the comparison. For an operation law you also list the operations. It tests the rules you thought of.

## What the package adds

| CsCheck gives you | {{product}} adds |
|---|---|
| Generators (`Gen.Int`, `Gen.Select`, …) and shrinking | **Generators from the shape of your type** (`GenAuto`), with hostile values mixed in by default (blank strings, `NaN`, an undefined enum, a duplicate id), and relations you declare once (`Reference`, `Identity`, `Normalize`). Every draw goes through CsCheck's own random source, so it shrinks. |
| `Sample(property)`: you write the property | **The rule is the property.** You declare `Idempotent(f)`, `Monotonic(select)`, `OrderIndependent(...)`, a deadline, and the library chooses the technique and the inputs. The same declaration also validates a value where it is built. |
| A seed that replays a failure | The same, plus **the name of the rule that broke**, so a finding can be filtered, escalated, or turned from a warning into an error on a date. |
| A property that passes | **A check that it was exercised.** If no generated order was ever confirmed, the rule about confirmed orders was never checked, and the test fails as vacuous. CsCheck cannot know what you meant to cover. |
| Whatever counter-examples you think of | **A counter-example per rule**, to show it can fire: found by search for most, written by you for the rest. See [counter-examples](../guides/counter-examples.md). |
| Operations and model-based testing | **The operations of your type, discovered** (every public method that returns it), run against the rules, with arguments harvested from the value they act on. |
| (no clock) | **A virtual clock** and `EventAutoTest`: random calls with delays chosen from your deadlines, judged against rules about order and time, with no real waiting. |

## The same tests, with the package

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

The three plain tests above are three of the six rules the package tests here, and the package also tests the confluence rule, the legal-move rule and the guard rule, checks that each was reached, and keeps the operations in step with the type as it changes.

## Using both

Nothing stops you writing a plain CsCheck property next to an `AutoTest`. Reuse the generator: `AutoTest.For(rules, gen)` takes any `Gen<T>`, and `gen.Valid(rules)` keeps only the values that satisfy a rule set.
