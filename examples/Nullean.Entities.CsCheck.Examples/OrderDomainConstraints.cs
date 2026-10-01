// Sync (Nullean.Entities) constraints over the pure Order value from OrderDomain.cs.
//
// Three separate ConstraintSet<T>s, deliberately not one: General holds for every Order at any
// time (Validate-able); AddLineStep is step-scoped — it only makes sense attached to the AddLine
// mutation specifically, per Monotonic's own doc comment, since RemoveLine legitimately shrinks the
// value it checks; Transfer is pair-valued, over (Order From, Order To), and only ever runs via
// Apply (a standalone pair has no previous to conserve against).

using static Nullean.Entities.Constraints<Nullean.Entities.CsCheck.Examples.Order>;

namespace Nullean.Entities.CsCheck.Examples;

public static class OrderDomainConstraints
{
	/// <summary>Holds for any Order, independent of which mutation produced it.</summary>
	public static readonly Constraint<Order> TotalMatchesLines = Always(
		o => o.Total == o.Lines.Sum(l => l.Subtotal),
		"Total must equal the sum of its lines' subtotals."
	);

	/// <summary>A Confirmed or Shipped order was never confirmed empty.</summary>
	public static readonly Constraint<Order> ConfirmedOrdersHaveLines = When(o => o.Status is OrderStatus.Confirmed or OrderStatus.Shipped).Requires(
		o => o.Lines.Count > 0,
		"A Confirmed or Shipped order must have at least one line."
	);

	/// <summary>Cancelling twice lands on the same value as cancelling once.</summary>
	public static readonly Constraint<Order> CancelIsIdempotent = Idempotent(o => o.Cancel(), "Cancel must be idempotent.");

	public static readonly ConstraintSet<Order> General = ConstraintSet.From(
		(nameof(TotalMatchesLines), TotalMatchesLines),
		(nameof(ConfirmedOrdersHaveLines), ConfirmedOrdersHaveLines),
		(nameof(CancelIsIdempotent), CancelIsIdempotent)
	);

	/// <summary>
	/// Step-scoped: attached only to the AddLine mutation (see OrderDomainExamples), never to
	/// General — a RemoveLine step is allowed to shrink the total, and would wrongly fail this if
	/// it were part of the type's whole constraint set instead of the one step it actually governs.
	/// </summary>
	public static readonly Constraint<Order> TotalNeverShrinksOnAddLine = Monotonic(
		o => o.Total,
		MonotonicDirection.NonDecreasing,
		"AddLine must never shrink the running total."
	);

	public static readonly ConstraintSet<Order> AddLineStep = ConstraintSet.From((nameof(TotalNeverShrinksOnAddLine), TotalNeverShrinksOnAddLine));

	/// <summary>Pair-valued: a line transfer must not create or destroy quantity across the two halves.</summary>
	public static readonly Constraint<(Order From, Order To)> QuantityIsConservedAcrossTransfer = Nullean
		.Entities
		.Constraints<(Order From, Order To)>
		.Conserves(p => p.From.TotalQuantity + p.To.TotalQuantity, "A line transfer must not create or destroy quantity.");

	public static readonly ConstraintSet<(Order From, Order To)> Transfer = ConstraintSet.From(
		(nameof(QuantityIsConservedAcrossTransfer), QuantityIsConservedAcrossTransfer)
	);
}
