// The flagship scenario: a nested Order/OrderLine domain that demonstrates every sync
// constraint kind (Always, When/Requires, Idempotent, Monotonic, Conserves, Transitions)
// plus a reactive spec over OrderEvents. Ported from examples/Nullean.Entities.CsCheck.Examples
// with the same domain logic, re-namespaced and using [Constraints] for auto-discovery.

namespace Nullean.Entities.Examples.Commerce.OrderLifecycle;

public enum OrderStatus
{
	Pending,
	Confirmed,
	Shipped,
	Cancelled
}

public sealed record OrderLine(string Sku, int Quantity, decimal Price)
{
	public decimal Subtotal => Quantity * Price;
}

/// <summary>
/// Immutable order value. Every mutation is a pure function returning the next value —
/// illegal transitions are no-ops (return the same value) so Cancel is idempotent and
/// ApplyOutcome.Noop fires whenever nothing changed.
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
	/// <summary>Moves every line matching <paramref name="sku"/> from one order to another.</summary>
	public static (Order From, Order To) TransferLine(Order from, Order to, string sku)
	{
		var moving = from.Lines.Where(l => l.Sku == sku).ToList();
		if (moving.Count == 0)
		{
			return (from, to);
		}
		var newFrom = from.RemoveLine(sku);
		var newTo = moving.Aggregate(to, (o, l) => o.AddLine(l));
		return (newFrom, newTo);
	}
}
