using AwesomeAssertions;
using CsCheck;
using Nullean.Entities;

namespace Nullean.Entities.CsCheck.Tests;

public class StateMachineRealizedWalksTests
{
	private abstract record OrderState
	{
		public sealed record Draft(int ItemCount) : OrderState;

		public sealed record Placed(decimal Total) : OrderState;

		public sealed record Shipped : OrderState;

		public sealed record Cancelled : OrderState;
	}

	// Place is guarded by a real property of the generated Draft value — exactly the thing the
	// type-only Walks() cannot evaluate, because it never has a value, only a Type.
	private static StateMachine<OrderState> BuildMachine() =>
		StateMachine<OrderState>.Define(
			m => m
				.Initial<OrderState.Draft>(s => s.On("Place").To<OrderState.Placed>(d => d.ItemCount > 0))
				.State<OrderState.Placed>(s => s.To<OrderState.Shipped>().On("Cancel").To<OrderState.Cancelled>())
				.Terminal<OrderState.Shipped>()
				.Terminal<OrderState.Cancelled>()
		);

	[Test]
	public void Every_step_is_allowed_by_the_machine_against_the_real_values_it_carries()
	{
		var machine = BuildMachine();
		// Allows() re-derives the case type from the real value and re-checks the guard against it —
		// so this is an independent, public-API check that RealizedWalks never fabricates a step its
		// own guard would have rejected.
		machine.RealizedWalks(minLength: 1, maxLength: 10).Sample(walk => walk.All(step => machine.Allows(step.From, step.To)));
	}

	[Test]
	public void A_walk_is_a_connected_chain_of_real_values()
	{
		var machine = BuildMachine();
		machine.RealizedWalks(minLength: 2, maxLength: 10).Sample(walk =>
		{
			for (var i = 1; i < walk.Count; i++)
			{
				if (!Equals(walk[i].From, walk[i - 1].To))
				{
					return false;
				}
			}
			return true;
		});
	}

	[Test]
	public void Generated_Draft_values_carry_real_varied_ItemCount_not_a_default()
	{
		var machine = BuildMachine();
		var itemCounts = new HashSet<int>();
		machine.RealizedWalks(minLength: 1, maxLength: 1).Sample(walk =>
		{
			if (walk.Count == 1 && walk[0].From is OrderState.Draft draft)
			{
				itemCounts.Add(draft.ItemCount);
			}
			return true;
		});

		// AutoFaker fills ItemCount from real random data, not a type-level placeholder — across the
		// default sample run this should produce more than one distinct value.
		itemCounts.Count.Should().BeGreaterThan(1);
	}

	[Test]
	public void An_empty_walk_is_generated_when_minLength_is_zero()
	{
		var machine = BuildMachine();
		machine.RealizedWalks(minLength: 0, maxLength: 0).Sample(walk => walk.Count == 0);
	}

	private abstract record GateState
	{
		public sealed record Open(bool CanClose) : GateState;

		public sealed record Closed : GateState;
	}

	private static StateMachine<GateState> BuildAlwaysRejectingMachine() =>
		StateMachine<GateState>.Define(m => m.Initial<GateState.Open>(s => s.To<GateState.Closed>(_ => false)).Terminal<GateState.Closed>());

	[Test]
	public void A_walk_stops_immediately_when_every_live_edges_guard_rejects_the_generated_value()
	{
		var machine = BuildAlwaysRejectingMachine();
		// Open's one edge always refuses, regardless of the generated value — unlike Walks(), which
		// ignores the guard entirely and would happily report this edge as live.
		machine.RealizedWalks(minLength: 1, maxLength: 5).Sample(walk => walk.Count == 0);
	}
}
