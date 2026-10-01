// The complex example this repo's design conversation asked for: a nested Order/OrderLine domain,
// deliberately built twice over — once as a pure, immutable value (this file) that the sync
// Nullean.Entities constraint shapes (Idempotent/Monotonic/Conserves) check directly and shrink
// against via plain Gen<T>.Sample, and once as an impure, event-emitting service
// (OrderEvents.cs/OrderService) that Nullean.Entities.Reactive's temporal/structural constraints
// check against a real Script<OrderService> run. Both halves model the same domain; neither is a
// stub of the other. See OrderDomainExamples.cs for every quality this exercises.

namespace Nullean.Entities.CsCheck.Examples;

public enum OrderStatus
{
	Pending,
	Confirmed,
	Shipped,
	Cancelled,
}

public sealed record OrderLine(string Sku, int Quantity, decimal Price)
{
	public decimal Subtotal => Quantity * Price;
}

/// <summary>
/// An immutable order value. Every mutation is a pure function returning the next value — an
/// illegal one (e.g. adding a line to a Confirmed order) is a no-op, returning the same value
/// unchanged, rather than throwing. That "illegal transitions are no-ops" rule is what makes
/// <see cref="Cancel"/> idempotent: cancelling an already-cancelled order is just another no-op.
/// </summary>
public sealed record Order(Guid Id, OrderStatus Status, IReadOnlyList<OrderLine> Lines)
{
	public decimal Total => Lines.Sum(l => l.Subtotal);
	public int TotalQuantity => Lines.Sum(l => l.Quantity);

	public static Order New(Guid id) => new(id, OrderStatus.Pending, []);

	public Order AddLine(OrderLine line) => Status == OrderStatus.Pending ? this with { Lines = [.. Lines, line] } : this;

	public Order RemoveLine(string sku) => Status == OrderStatus.Pending ? this with { Lines = [.. Lines.Where(l => l.Sku != sku)] } : this;

	public Order Confirm() => Status == OrderStatus.Pending && Lines.Count > 0 ? this with { Status = OrderStatus.Confirmed } : this;

	public Order Ship() => Status == OrderStatus.Confirmed ? this with { Status = OrderStatus.Shipped } : this;

	public Order Cancel() => Status is OrderStatus.Pending or OrderStatus.Confirmed ? this with { Status = OrderStatus.Cancelled } : this;
}

public static class OrderTransfers
{
	/// <summary>
	/// Moves every line matching <paramref name="sku"/> from <paramref name="from"/> to
	/// <paramref name="to"/> — both must be Pending, same rule <see cref="Order.AddLine"/>/
	/// <see cref="Order.RemoveLine"/> already enforce individually. A missing sku is a no-op on
	/// both halves, not a failure — nothing to conserve. Moves every match, not just the first:
	/// <see cref="Order.RemoveLine"/> already removes every line sharing a sku (an order can
	/// legitimately hold two separate lines for the same sku, added at different times/prices), so
	/// moving only one of them here would silently drop the rest — a real bug CsCheck's own
	/// Conserves check on <see cref="Order.TotalQuantity"/> across the pair found on the first run.
	/// </summary>
	public static (Order From, Order To) TransferLine(Order from, Order to, string sku)
	{
		var moving = from.Lines.Where(l => l.Sku == sku).ToList();
		if (moving.Count == 0)
		{
			return (from, to);
		}
		var newFrom = from.RemoveLine(sku);
		var newTo = moving.Aggregate(to, (order, line) => order.AddLine(line));
		return (newFrom, newTo);
	}
}
