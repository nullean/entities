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
	public void Two_operations_that_do_not_commute_fail_with_the_law_in_the_message()
	{
		var act = () => Auto().Commutes("Confirm", "Cancel").Cases.Single(c => c.ToString() == "Commutes(Confirm, Cancel)").Run();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("Confluence (Commutes(Confirm, Cancel)) failed").And.Contain("Law");
	}
}
