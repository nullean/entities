// Three constraint sets for the Order domain — deliberately kept separate:
//   General: holds for every Order at any time (Validate-able).
//   AddLineStep: step-scoped monotonicity, only valid alongside the AddLine mutation.
//   TransferStep: pair-valued conservation, only valid alongside a TransferLine call.

using static Nullean.Entities.Constraints<Nullean.Entities.Examples.Commerce.OrderLifecycle.Order>;

namespace Nullean.Entities.Examples.Commerce.OrderLifecycle;

// [Constraints] auto-discovers all static Constraint<Order> fields — except step-scoped
// ones excluded via [ConstraintIgnore].
[Constraints]
public static partial class OrderConstraints
{
	public static readonly Constraint<Order> TotalMatchesLines = Always(
		o => o.Total == o.Lines.Sum(l => l.Subtotal),
		"Total must equal the sum of its lines' subtotals."
	);

	public static readonly Constraint<Order> ConfirmedOrdersHaveLines = When(o => o.Status is OrderStatus.Confirmed or OrderStatus.Shipped).Requires(
		o => o.Lines.Count > 0,
		"A Confirmed or Shipped order must have at least one line."
	);

	public static readonly Constraint<Order> CancelIsIdempotent = Idempotent(o => o.Cancel(), "Cancel must be idempotent.");

	public static readonly Constraint<Order> LegalStatusTransitions = Nullean
		.Entities
		.Constraints<Order>
		.Transitions(o => o.Status)
		.Allow(OrderStatus.Pending, OrderStatus.Confirmed)
		.Allow(OrderStatus.Confirmed, OrderStatus.Shipped)
		.Allow(OrderStatus.Pending, OrderStatus.Cancelled)
		.Allow(OrderStatus.Confirmed, OrderStatus.Cancelled);

	// Step-scoped: only applies to the AddLine mutation; excluded from the General set.
	[ConstraintIgnore]
	public static readonly Constraint<Order> TotalNeverShrinksOnAddLine = Monotonic(
		o => o.Total,
		MonotonicDirection.NonDecreasing,
		"AddLine must never shrink the running total."
	);
}

// Manually managed step sets (source gen can't express step-scoping).
public static class OrderStepConstraints
{
	public static readonly ConstraintSet<Order> AddLineStep = ConstraintSet.From(
		(nameof(OrderConstraints.TotalNeverShrinksOnAddLine), OrderConstraints.TotalNeverShrinksOnAddLine)
	);
}

// Pair-valued: a line transfer must not create or destroy quantity across the two halves.
[Constraints]
public static partial class OrderTransferConstraints
{
	public static readonly Constraint<(Order From, Order To)> QuantityIsConserved = Nullean
		.Entities
		.Constraints<(Order From, Order To)>
		.Conserves(p => p.From.TotalQuantity + p.To.TotalQuantity, "A line transfer must not create or destroy quantity.");
}
