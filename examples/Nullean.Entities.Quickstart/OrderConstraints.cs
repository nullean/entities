namespace Nullean.Entities.Quickstart;

// Every rule the Order must keep, written once, next to the type. [Constraints] gathers the static fields into OrderConstraints.All.
[Constraints<Order>]
public static partial class OrderConstraints
{
	// one state: true in every state
	public static readonly Constraint<Order> TotalNeverNegative = Constraints<Order>.Always(o => o.Total >= 0, "An order total is never negative.");

	public static readonly Constraint<Order> ConfirmedOrdersHaveLines = Constraints<Order>
		.When(o => o.Status == OrderStatus.Confirmed)
		.Requires(o => o.Lines.Count > 0, "A confirmed order has at least one line.");

	// two states: the legal moves between them
	public static readonly Constraint<Order> LegalMoves = Constraints<Order>
		.Transitions(o => o.Status)
		.Allow(OrderStatus.Pending, OrderStatus.Confirmed)
		.Allow(OrderStatus.Confirmed, OrderStatus.Shipped)
		.Allow(OrderStatus.Pending, OrderStatus.Cancelled)
		.Allow(OrderStatus.Confirmed, OrderStatus.Cancelled);

	// a step: doing it twice is doing it once
	public static readonly Constraint<Order> CancelIsRepeatable = Constraints<Order>.Idempotent(o => o.Cancel());

	// a step: a value that only moves one way
	public static readonly Constraint<Order> VersionNeverGoesBack = Constraints<Order>.Monotonic(o => o.Version, MonotonicDirection.NonDecreasing);

	// order: the label does not depend on the order the lines were added in
	public static readonly Constraint<Order> LabelIgnoresLineOrder = Constraints<Order>.OrderIndependent(
		o => o.Lines,
		(o, lines) => o with { Lines = lines },
		o => o.Label
	);
}
