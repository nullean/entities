using Nullean.Entities;

namespace OrderFulfilment;

/// <summary>Where an order is in its life. Each state is its own type, so <c>Placed</c> carries what only a placed order has.</summary>
public abstract record OrderState
{
	public sealed record Draft : OrderState;

	public sealed record Placed(int Lines) : OrderState;

	public sealed record Confirmed : OrderState;

	public sealed record Shipped : OrderState;

	public sealed record Cancelled : OrderState;
}

public sealed record Order(Guid Id, Email Customer, OrderState State, string? PaymentRef = null)
{
	public static Order Draft(Email customer) => new(Guid.NewGuid(), customer, new OrderState.Draft());
}

/// <summary>
/// The lifecycle as a machine that runs: every edge has a factory, so <c>Fire</c> builds the next state. The graph is checked when the field is
/// initialised (every state declared, reachable, able to finish), so a wrong machine fails at startup, not in a log at three in the morning.
/// </summary>
public static class OrderLifecycle
{
	public static readonly StateMachine<OrderState> Machine = StateMachine<OrderState>.Define(
		m =>
			m
				.Initial<OrderState.Draft>(s => s.On("Place").To<OrderState.Placed>(_ => new OrderState.Placed(0)))
				.State<OrderState.Placed>(
					s =>
						s
							.On("AddLine").To<OrderState.Placed>(p => p with { Lines = p.Lines + 1 })
							.On("Confirm").To<OrderState.Confirmed>(_ => new OrderState.Confirmed(), when: p => p.Lines > 0)
							.On("Cancel").To<OrderState.Cancelled>(_ => new OrderState.Cancelled())
				)
				.State<OrderState.Confirmed>(
					s =>
						s
							.On("Ship").To<OrderState.Shipped>(_ => new OrderState.Shipped())
							.On("Cancel").To<OrderState.Cancelled>(_ => new OrderState.Cancelled())
				)
				.Terminal<OrderState.Shipped>()
				.Terminal<OrderState.Cancelled>()
	);

	/// <summary>The machine bound to the aggregate that holds the state: fire a trigger on an <see cref="Order"/> and get an <see cref="Order"/> back.</summary>
	public static readonly MachineBinding<Order, OrderState> Binding = Machine.For<Order>(o => o.State, (o, s) => o with { State = s });
}

/// <summary>
/// Every rule about an order, in one set. <c>[Constraints]</c> collects the static fields into <c>OrderConstraints.All</c>;
/// the lifecycle rule is the machine's own, so a state change made by hand (outside <c>Fire</c>) is refused too.
/// </summary>
[Constraints]
public static partial class OrderConstraints
{
	public static readonly Constraint<Order> FollowsTheLifecycle = OrderLifecycle.Binding.AsConstraint();

	public static readonly Constraint<Order> ShippedOrdersArePaid = Constraints<Order>.Always(
		o => o.State is not OrderState.Shipped || !string.IsNullOrWhiteSpace(o.PaymentRef),
		"A shipped order needs a payment reference"
	);
}
