using System.Reactive.Subjects;
using AwesomeAssertions;

namespace Nullean.Entities.Reactive.Tests;

public class StateMachineReactiveExtensionsTests
{
	// ── Domain: an event stream whose own state is derived via stateOf ──────────

	private abstract record OrderState
	{
		public sealed record Draft : OrderState;

		public sealed record Placed(int Lines) : OrderState;

		public sealed record Confirmed : OrderState;

		public sealed record Shipped : OrderState;
	}

	private abstract record OrderEvent
	{
		public OrderState State => this switch
		{
			Created => new OrderState.Draft(),
			LinesAdded la => new OrderState.Placed(la.Lines),
			Confirmed => new OrderState.Confirmed(),
			Shipped => new OrderState.Shipped(),
			_ => throw new ArgumentOutOfRangeException(nameof(OrderEvent)),
		};

		public sealed record Created : OrderEvent;

		public sealed record LinesAdded(int Lines) : OrderEvent;

		public sealed record Confirmed : OrderEvent;

		public sealed record Shipped : OrderEvent;
	}

	private static StateMachine<OrderState> BuildMachine() =>
		StateMachine<OrderState>.Define(
			m => m
				.Initial<OrderState.Draft>(s => s.On("Add").To<OrderState.Placed>())
				.State<OrderState.Placed>(s => s.On("Confirm").To<OrderState.Confirmed>(when: p => p.Lines > 0))
				.State<OrderState.Confirmed>(s => s.To<OrderState.Shipped>())
				.Terminal<OrderState.Shipped>()
		);

	[Test]
	public void Holds_when_every_consecutive_pair_of_events_is_an_allowed_transition()
	{
		var subject = new Subject<OrderEvent>();
		var constraint = BuildMachine().AsReactive<OrderState, OrderEvent>(e => e.State);
		var running = constraint.Attach(subject);

		subject.OnNext(new OrderEvent.Created());
		subject.OnNext(new OrderEvent.LinesAdded(2));
		subject.OnNext(new OrderEvent.Confirmed());
		subject.OnNext(new OrderEvent.Shipped());

		running.Current.IsViolated.Should().BeFalse();
	}

	[Test]
	public void Violated_when_a_consecutive_pair_skips_a_declared_state()
	{
		var subject = new Subject<OrderEvent>();
		var constraint = BuildMachine().AsReactive<OrderState, OrderEvent>(e => e.State);
		var running = constraint.Attach(subject);

		subject.OnNext(new OrderEvent.Created());
		subject.OnNext(new OrderEvent.Shipped()); // Draft -> Shipped skips Placed/Confirmed entirely

		running.Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void Violated_when_a_guard_on_the_source_state_fails()
	{
		var subject = new Subject<OrderEvent>();
		var constraint = BuildMachine().AsReactive<OrderState, OrderEvent>(e => e.State);
		var running = constraint.Attach(subject);

		subject.OnNext(new OrderEvent.Created());
		subject.OnNext(new OrderEvent.LinesAdded(0)); // Placed with 0 lines
		subject.OnNext(new OrderEvent.Confirmed()); // Confirm requires Lines > 0 — guard fails

		running.Current.IsViolated.Should().BeTrue();
	}
}
