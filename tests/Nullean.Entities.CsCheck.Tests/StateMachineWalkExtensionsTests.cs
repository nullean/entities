using AwesomeAssertions;
using CsCheck;
using Nullean.Entities;

namespace Nullean.Entities.CsCheck.Tests;

public class StateMachineWalkExtensionsTests
{
	private abstract record OrderState
	{
		public sealed record Draft : OrderState;

		public sealed record Placed : OrderState;

		public sealed record Shipped : OrderState;

		public sealed record Cancelled : OrderState;
	}

	private static StateMachine<OrderState> BuildMachine() =>
		StateMachine<OrderState>.Define(
			m =>
				m
					.Initial<OrderState.Draft>(s => s.On("Place").To<OrderState.Placed>())
					.State<OrderState.Placed>(s => s.To<OrderState.Shipped>().On("Cancel").To<OrderState.Cancelled>())
					.Terminal<OrderState.Shipped>()
					.Terminal<OrderState.Cancelled>()
		);

	[Test]
	public void Every_generated_walk_starts_at_Initial()
	{
		var machine = BuildMachine();
		machine.Walks(minLength: 1, maxLength: 10).Sample(walk => walk.Count == 0 || walk[0].From == machine.Initial);
	}

	[Test]
	public void Every_step_in_a_walk_follows_a_declared_edge()
	{
		var machine = BuildMachine();
		var declared = machine.Edges.Select(e => (e.From, e.To, e.Name)).ToHashSet();

		machine.Walks(minLength: 1, maxLength: 10).Sample(walk => walk.All(step => declared.Contains((step.From, step.To, step.Name))));
	}

	[Test]
	public void A_walk_is_a_connected_chain()
	{
		var machine = BuildMachine();

		machine.Walks(minLength: 2, maxLength: 10).Sample(walk =>
		{
			for (var i = 1; i < walk.Count; i++)
			{
				if (walk[i].From != walk[i - 1].To)
				{
					return false;
				}
			}
			return true;
		});
	}

	[Test]
	public void A_walk_shorter_than_its_requested_length_stopped_at_a_state_with_no_outgoing_edge()
	{
		var machine = BuildMachine();
		// Fixing minLength == maxLength pins the hidden choice array to exactly 5 entries, so a
		// walk shorter than 5 can only mean Interpret broke out of its loop early — i.e. the state
		// it stopped at genuinely has no outgoing edge (Shipped/Cancelled, the two Terminal<T>()s).
		machine.Walks(minLength: 5, maxLength: 5).Sample(walk =>
		{
			if (walk.Count == 5)
			{
				return true;
			}
			var last = walk.Count == 0 ? machine.Initial : walk[^1].To;
			return machine.Edges.All(e => e.From != last);
		});
	}

	[Test]
	public void An_empty_walk_is_generated_when_minLength_is_zero()
	{
		var machine = BuildMachine();
		// Just proves Walks(0, 0) is a valid, non-throwing degenerate case.
		machine.Walks(minLength: 0, maxLength: 0).Sample(walk => walk.Count == 0);
	}
}
