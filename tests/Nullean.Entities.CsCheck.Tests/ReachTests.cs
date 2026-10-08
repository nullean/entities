using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// Probe says how big the values it drew were, so "never violated" can be told apart from "about more than the generator ever produced".

public sealed record Branch(string Name, IReadOnlyList<Branch> Children);

public class ReachTests
{
	private static Branch Chain(int length) => length == 0 ? new Branch("leaf", []) : new Branch($"b{length}", [Chain(length - 1)]);

	[Test]
	public void Probe_reports_the_largest_collection_and_the_deepest_nesting_it_saw()
	{
		var tree = new Branch("root", [Chain(2), new Branch("a", []), new Branch("b", []), new Branch("c", [])]);
		var rules = ConstraintSet.From(("Named", Constraints<Branch>.Always(b => b.Name.Length > 0, "needs a name")));

		var report = rules.Probe(Gen.Const(tree), samples: 20, throwOnContradiction: false);

		report.Reach.Should().NotBeNull();
		report.Reach!.LargestCollection.Should().Be(4);
		report.Reach.Depth.Should().Be(8, "root 1, its list 2, then Chain(2): a branch, its list, a branch, its list, the leaf, and the leaf's empty list");
	}

	[Test]
	public void The_report_warns_that_a_never_violated_rule_may_be_about_more_than_was_generated()
	{
		var rules = ConstraintSet.From(("FewChildren", Constraints<Branch>.Always(b => b.Children.Count <= 6, "at most six")));

		var report = rules.Probe(Gen.Const(new Branch("root", [new Branch("a", [])])), samples: 20, throwOnContradiction: false);

		report.NeverViolated.Should().Contain("FewChildren");
		report.ToString().Should().Contain("went up to 1 items").And.Contain("raise AutoOptions.MaxCollection or MaxDepth");
	}

	[Test]
	public void Nothing_is_said_when_every_rule_was_violated()
	{
		var rules = ConstraintSet.From(("NoChildren", Constraints<Branch>.Always(b => b.Children.Count == 0, "no children")));

		var report = rules.Probe(Gen.Const(new Branch("root", [new Branch("a", [])])), samples: 20, throwOnContradiction: false);

		report.ToString().Should().Contain("went up to").And.NotContain("raise AutoOptions");
	}

	[Test]
	public void The_default_generator_is_shown_to_stop_at_the_depth_a_rule_needs_more_of()
	{
		var rules = ConstraintSet.From(("ShallowTree", Constraints<Branch>.Always(b => Depth(b) < 6, "six levels at most")));

		var report = rules.Probe(samples: 600, throwOnContradiction: false);

		report.NeverViolated.Should().Contain("ShallowTree");
		report.Reach!
			.Depth
			.Should()
			.BeLessThan(12, "the default MaxDepth never produced a tree six levels of branches deep, which is why the rule was never violated");

		var deeper = rules.Probe(GenAuto.Of<Branch>(AutoMode.Adversarial, o =>
		{
			o.MaxDepth = 16;
			o.MaxCollection = 10;
		}), samples: 600, throwOnContradiction: false);

		deeper.NeverViolated.Should().BeEmpty("with deeper trees the same rule can be seen to fail");
		deeper.Reach!.Depth.Should().BeGreaterThan(report.Reach.Depth);
	}

	private static int Depth(Branch branch) => 1 + (branch.Children.Count == 0 ? 0 : branch.Children.Max(Depth));
}
