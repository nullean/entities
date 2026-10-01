#if NET11_0_OR_GREATER
// A real C# 11 union as TState, proving StateMachineCases.Discover's union branch (reflection
// over op_Implicit operators — see StateMachine.cs's header) finds the right cases, not just the
// abstract-record hierarchy fallback StateMachineTests.cs exercises. See
// examples/Nullean.Entities.Reactive.Examples/OrderStateExamplesNet11.cs for the header comment
// confirming the union compiles to implicit conversions from each case, by hand against the
// installed preview SDK.

using AwesomeAssertions;

namespace Nullean.Entities.Tests;

public static class UnionOrderStateCases
{
	public sealed record Draft;

	public sealed record Placed(int Lines);

	public sealed record Shipped;
}

public readonly union UnionOrderState(UnionOrderStateCases.Draft, UnionOrderStateCases.Placed, UnionOrderStateCases.Shipped);

public class StateMachineUnionTests
{
	private static StateMachine<UnionOrderState> BuildMachine() =>
		StateMachine<UnionOrderState>.Define(m =>
			m.Initial<UnionOrderStateCases.Draft>(s => s.On("Place").To<UnionOrderStateCases.Placed>())
				.State<UnionOrderStateCases.Placed>(s => s.On("Ship").To<UnionOrderStateCases.Shipped>(when: p => p.Lines > 0))
				.Terminal<UnionOrderStateCases.Shipped>()
		);

	[Test]
	public void Define_discovers_every_union_case_via_its_implicit_conversion_operators()
	{
		var machine = BuildMachine();
		machine.States.Should().BeEquivalentTo([typeof(UnionOrderStateCases.Draft), typeof(UnionOrderStateCases.Placed), typeof(UnionOrderStateCases.Shipped)]);
	}

	[Test]
	public void Define_throws_NESM001_when_a_union_case_is_not_declared()
	{
		var act = () => StateMachine<UnionOrderState>.Define(m => m.Initial<UnionOrderStateCases.Draft>().Terminal<UnionOrderStateCases.Placed>());
		// UnionOrderStateCases.Shipped is never mentioned.

		act.Should().Throw<StateMachineDefinitionException>().Where(e => e.Code == "NESM001");
	}

	[Test]
	public void AsConstraint_honours_a_guard_over_a_union_case()
	{
		var constraint = BuildMachine().AsConstraint();

		UnionOrderState placedEmpty = new UnionOrderStateCases.Placed(0);
		UnionOrderState placedWithLines = new UnionOrderStateCases.Placed(2);
		UnionOrderState shipped = new UnionOrderStateCases.Shipped();

		constraint.Check(placedEmpty, shipped).Should().NotBeEmpty();
		constraint.Check(placedWithLines, shipped).Should().BeEmpty();
	}
}
#endif
