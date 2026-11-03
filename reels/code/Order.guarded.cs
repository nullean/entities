namespace Shop;

// The usual way: the rules are there, written as code where the data is built and changed.
public sealed class Order
{
	public Order(IReadOnlyList<OrderLine> lines, OrderStatus status, int version)
	{
		if (lines.Any(l => l.Price < 0)) throw new ArgumentException("bad line");
		if (status == OrderStatus.Confirmed && lines.Count == 0) throw new InvalidOperationException();
		(Lines, Status, Version) = (lines, status, version);
	}

	public IReadOnlyList<OrderLine> Lines { get; }
	public OrderStatus Status { get; }
	public int Version { get; }

	public decimal Total
	{
		get
		{
			var total = Lines.Sum(l => l.Quantity * l.Price);
			Debug.Assert(total >= 0);
			return total;
		}
	}

	public Order Cancel()
	{
		if (Status == OrderStatus.Cancelled) return this; // twice is fine?
		if (Status == OrderStatus.Shipped) throw new InvalidOperationException("too late");
		return new Order(Lines, OrderStatus.Cancelled, Version + 1);
	}
}

public sealed class OrderService(IOrderStore store)
{
	public async Task SaveAsync(Order order)
	{
		var stored = await store.CurrentAsync();
		if (order.Version < stored.Version) throw new ConcurrencyException();
		await store.SaveAsync(order);
	}
}
