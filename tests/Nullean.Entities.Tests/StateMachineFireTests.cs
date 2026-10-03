using AwesomeAssertions;

namespace Nullean.Entities.Tests;

// The type-state machine as something a service runs: the same lifecycle as StateMachineTests, with a factory on each edge.
public class StateMachineFireTests
{
	private abstract record OrderState
	{
		public sealed record Draft : OrderState;

		public sealed record Placed(int Lines) : OrderState;

		public sealed record Confirmed : OrderState;

		public sealed record Shipped : OrderState;

		public sealed record Cancelled : OrderState;
	}

	private sealed record Order(Guid Id, OrderState State, string? PaymentRef = null);

	private static readonly StateMachine<OrderState> Lifecycle = StateMachine<OrderState>.Define(
		m =>
			m
				.Initial<OrderState.Draft>(s => s.On("Place").To<OrderState.Placed>(_ => new OrderState.Placed(0)))
				.State<OrderState.Placed>(
					s =>
						s
							.On<int>("AddLines").To<OrderState.Placed>((p, n) => p with { Lines = p.Lines + n })
							.On("Confirm").To<OrderState.Confirmed>(_ => new OrderState.Confirmed(), when: p => p.Lines > 0)
							.On("Cancel").To<OrderState.Cancelled>(_ => new OrderState.Cancelled())
				)
				.State<OrderState.Confirmed>(
					s =>
						s
							.To<OrderState.Shipped>(_ => new OrderState.Shipped())
							.On("Cancel").To<OrderState.Cancelled>(_ => new OrderState.Cancelled())
				)
				.Terminal<OrderState.Shipped>()
				.Terminal<OrderState.Cancelled>()
	);

	[Test]
	public void Fire_walks_the_lifecycle_building_each_next_state()
	{
		OrderState state = new OrderState.Draft();

		state = Lifecycle.Fire(state, "Place").Unwrap();
		state.Should().Be(new OrderState.Placed(0));

		state = Lifecycle.Fire(state, "AddLines", 3).Unwrap();
		state = Lifecycle.Fire(state, "AddLines", 2).Unwrap();
		state.Should().Be(new OrderState.Placed(5));

		state = Lifecycle.Fire(state, "Confirm").Unwrap();
		state = Lifecycle.Fire(state, "Shipped").Unwrap(); // the unnamed edge is named after its target
		state.Should().Be(new OrderState.Shipped());
	}

	[Test]
	public void A_guard_refuses_the_transition_and_says_so()
	{
		OrderState empty = new OrderState.Placed(0);

		var result = Lifecycle.Fire(empty, "Confirm");

		var fail = result.Should().BeOfType<Result<OrderState>.Fail>().Subject;
		fail.Attempted.Should().Be(empty);
		fail.Violations.Should().ContainSingle().Which.Message.Should().Contain("guard rejected");
		Lifecycle.CanFire(empty, "Confirm").Should().BeFalse();
		Lifecycle.CanFire(new OrderState.Placed(1), "Confirm").Should().BeTrue();
	}

	[Test]
	public void A_trigger_that_is_not_declared_for_the_case_names_what_is_permitted()
	{
		var result = Lifecycle.Fire(new OrderState.Draft(), "Ship");

		result.ViolationsOrEmpty.Should().ContainSingle().Which.Message.Should().Contain("not a transition out of Draft").And.Contain("permitted: Place");
	}

	[Test]
	public void A_terminal_state_has_nothing_to_fire()
	{
		Lifecycle.Permitted(new OrderState.Shipped()).Should().BeEmpty();
		Lifecycle.Fire(new OrderState.Shipped(), "Cancel").IsFail.Should().BeTrue();
	}

	[Test]
	public void Permitted_follows_the_guards()
	{
		Lifecycle.Permitted(new OrderState.Placed(0)).Should().BeEquivalentTo("AddLines", "Cancel");
		Lifecycle.Permitted(new OrderState.Placed(2)).Should().BeEquivalentTo("AddLines", "Confirm", "Cancel");
	}

	[Test]
	public void An_argument_is_required_exactly_where_the_edge_declares_one()
	{
		OrderState placed = new OrderState.Placed(1);

		Lifecycle.Fire(placed, "AddLines").ViolationsOrEmpty.Should().ContainSingle().Which.Message.Should().Contain("needs an argument of type Int32");
		Lifecycle.Fire(placed, "AddLines", "three").ViolationsOrEmpty.Should().ContainSingle().Which.Message.Should().Contain("does not take a String");
		Lifecycle.Fire(placed, "Cancel", 1).ViolationsOrEmpty.Should().ContainSingle().Which.Message.Should().Contain("does not take a Int32");
	}

	[Test]
	public void An_edge_declared_without_a_factory_cannot_be_fired_but_is_still_allowed()
	{
		var declaredOnly = StateMachine<OrderState>.Define(
			m =>
				m
					.Initial<OrderState.Draft>(s => s.On("Place").To<OrderState.Placed>())
					.State<OrderState.Placed>(s => s.On("Confirm").To<OrderState.Confirmed>().On("Cancel").To<OrderState.Cancelled>())
					.State<OrderState.Confirmed>(s => s.To<OrderState.Shipped>().On("Cancel").To<OrderState.Cancelled>())
					.Terminal<OrderState.Shipped>()
					.Terminal<OrderState.Cancelled>()
		);

		declaredOnly.Allows(new OrderState.Draft(), new OrderState.Placed(1)).Should().BeTrue();
		declaredOnly.Fire(new OrderState.Draft(), "Place").ViolationsOrEmpty.Should().ContainSingle().Which.Message.Should().Contain("cannot be fired");
		declaredOnly.CanFire(new OrderState.Draft(), "Place").Should().BeFalse();
	}

	[Test]
	public void Transition_returns_the_event_a_service_publishes()
	{
		var placed = new OrderState.Placed(2);

		var transitioned = Lifecycle.Transition(placed, "Confirm").Unwrap();

		transitioned.Should().Be(new Transitioned<OrderState>(placed, "Confirm", new OrderState.Confirmed()));
	}

	// ── bound to an aggregate ────────────────────────────────────────────────────

	private static readonly MachineBinding<Order, OrderState> Binding = Lifecycle.For<Order>(o => o.State, (o, s) => o with { State = s });

	private static readonly ConstraintSet<Order> Rules = ConstraintSet.From(
		("lifecycle", Binding.AsConstraint()),
		("shipped-needs-payment", Constraints<Order>.Always(
			o => o.State is not OrderState.Shipped || o.PaymentRef is not null,
			"A shipped order needs a payment reference"
		))
	);

	private static readonly MachineBinding<Order, OrderState> Enforced = Binding.Enforce(Rules);

	[Test]
	public void Binding_returns_the_entity_with_its_new_state()
	{
		var order = new Order(Guid.NewGuid(), new OrderState.Draft());

		var placed = Binding.Fire(order, "Place").Unwrap();

		placed.Id.Should().Be(order.Id);
		placed.State.Should().Be(new OrderState.Placed(0));
		Binding.Permitted(placed).Should().BeEquivalentTo("AddLines", "Cancel");
	}

	[Test]
	public void Binding_with_rules_checks_the_resulting_entity_against_them()
	{
		var confirmed = new Order(Guid.NewGuid(), new OrderState.Confirmed());

		var withoutPayment = Enforced.Fire(confirmed, "Shipped");
		withoutPayment.ViolationsOrEmpty.Should().ContainSingle().Which.Message.Should().Contain("payment reference");

		var paid = Enforced.Fire(confirmed with { PaymentRef = "pay-1" }, "Shipped").Unwrap();
		paid.State.Should().Be(new OrderState.Shipped());
	}

	[Test]
	public void Binding_reports_the_machines_own_reason_when_the_transition_is_refused()
	{
		var empty = new Order(Guid.NewGuid(), new OrderState.Placed(0));

		var result = Enforced.Fire(empty, "Confirm");

		var fail = result.Should().BeOfType<Result<Order>.Fail>().Subject;
		fail.Attempted.Should().Be(empty);
		fail.Violations.Single().Message.Should().Contain("guard rejected");
	}

	[Test]
	public void The_machines_constraint_also_rejects_a_state_change_made_by_hand()
	{
		var draft = new Order(Guid.NewGuid(), new OrderState.Draft());

		var result = Rules.Apply(draft, o => o with { State = new OrderState.Shipped(), PaymentRef = "pay-1" });

		result.IsFail.Should().BeTrue();
	}

	[Test]
	public void Binding_FireWithEvent_returns_both()
	{
		var order = new Order(Guid.NewGuid(), new OrderState.Draft());

		var (next, evt) = Binding.FireWithEvent(order, "Place").Unwrap();

		next.State.Should().Be(new OrderState.Placed(0));
		evt.Should().Be(new Transitioned<OrderState>(new OrderState.Draft(), "Place", new OrderState.Placed(0)));
	}

	[Test]
	public void Binding_passes_an_argument_to_the_factory()
	{
		var order = new Order(Guid.NewGuid(), new OrderState.Placed(1));

		Binding.Fire(order, "AddLines", 4).Unwrap().State.Should().Be(new OrderState.Placed(5));
	}
}
