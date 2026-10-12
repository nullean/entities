namespace Nullean.Entities.Quickstart;

public enum OrderStatus { Pending, Confirmed, Shipped, Cancelled }

public sealed record OrderLine(string Sku, int Quantity, decimal Price);

// An immutable record: every method returns the next order.
public sealed record Order(IReadOnlyList<OrderLine> Lines, OrderStatus Status, int Version)
{
	public static Order New() => new([], OrderStatus.Pending, 0);

	public decimal Total => Lines.Sum(l => l.Quantity * l.Price);

	public string Label => string.Join(",", Lines.Select(l => l.Sku).Order(StringComparer.Ordinal));

	public Order AddLine(OrderLine line) =>
		Status == OrderStatus.Pending && line.Quantity > 0 && line.Price >= 0 ? this with { Lines = [.. Lines, line], Version = Version + 1 } : this;

	public Order Confirm() => Status == OrderStatus.Pending && Lines.Count > 0 ? this with { Status = OrderStatus.Confirmed, Version = Version + 1 } : this;

	public Order Ship() => Status == OrderStatus.Confirmed ? this with { Status = OrderStatus.Shipped, Version = Version + 1 } : this;

	public Order Cancel() =>
		Status is OrderStatus.Pending or OrderStatus.Confirmed ? this with { Status = OrderStatus.Cancelled, Version = Version + 1 } : this;
}
