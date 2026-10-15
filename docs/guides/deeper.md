---
navigation_title: Going deeper
description: When the state space is small enough to close, prove every rule over every reachable state, and mutation-test the rules, from the same AutoTest.
---

# Going deeper

`AutoTest` samples: it draws many values and runs many sequences, and a rule that holds across all of them is very likely to hold. When the set of states your type can reach is small enough to close, you can do more with the same rules and the same operations.

`Deeper()` adds two checks as extra cases next to the axes:

- **Exhaustive** walks every state reachable from a start by any sequence of operations and checks every rule on every step. It either proves the rules over the whole state space, or hands back the shortest sequence that breaks one. It also fails if a guard or a legal move was never exercised, since the rule then proved nothing.
- **Faults** mutation-tests the rules themselves. Each operation is broken in turn (for example, made to do nothing), and some rule must notice. A fault no rule catches means a rule is missing.

`Commutes("A", "B")` adds a third: applying A then B must give what B then A gives, from every reachable state.

<!-- file: tests/Nullean.Entities.Quickstart.Tests/DeeperTests.cs -->
```csharp
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Quickstart;

namespace Nullean.Entities.Quickstart.Tests;

// AutoTest samples. Deeper() adds the checks that need a closed state space: proof over every reachable state, and mutation-testing of the rules.
public class DeeperTests
{
	private static readonly Gen<Order> Orders = Gen.Const(Order.New());

	private static AutoTest<Order> Auto() =>
		AutoTest
			.For(OrderConstraints.Instance, Orders)
			.Deeper(
				spec =>
					spec.Domain(new OrderLine("apple", 1, 5)).Boundary(o => o.Lines.Count < 2 && o.Version < 6).Key(o => (o.Status, o.Lines.Count, o.Version))
			);

	[Test]
	public void Deeper_adds_one_case_each_next_to_the_axes()
	{
		var names = Auto().Cases.Select(c => c.ToString()).ToList();

		names.Should().Contain("Exhaustive").And.Contain("Faults");
	}

	[Test]
	public void Exhaustive_proves_every_rule_over_every_reachable_state() =>
		Auto().Exhaustive().Should().BeOfType<AxisResult.Passed>().Which.Note.Should().Contain("reachable states");

	[Test]
	public async Task Two_operations_that_do_not_commute_fail_with_the_law_in_the_message()
	{
		var act = async () => await Auto().Commutes("Confirm", "Cancel").Cases.Single(c => c.ToString() == "Commutes(Confirm, Cancel)").RunAsync();

		(await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("Confluence (Commutes(Confirm, Cancel)) failed").And.Contain("Law");
	}
}
```

## Making the state space close

An order can grow without bound (more lines, a higher version), so you bound it. The configuration passed to `Deeper` is the `EntitySpec` for the type:

- `Domain(values)` gives the arguments the operations take.
- `Boundary(predicate)` says which states are inside the exploration. Anything that only grows needs one.
- `Key(selector)` says what makes two states the same, so the walk does not revisit them.
- `From(starts)` replaces the generated start with the values you name.

If the state space does not close, the failure says so and lists what to try. When it cannot close at all, use the sampled version of the same checks through `EntitySpec` directly (see the [testing reference](../reference/testing-api.md)).

## When to use it

Use `AutoTest` for everything first. Add `Deeper()` for the types where a missed sequence would be expensive and the state space is small: a lifecycle, a protocol, an approval flow. The rest of your types do not need it.
