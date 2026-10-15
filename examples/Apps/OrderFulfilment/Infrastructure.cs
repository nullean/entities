using System.Collections.Concurrent;
using Nullean.Entities;

namespace OrderFulfilment;

/// <summary>What the service publishes after every accepted transition.</summary>
public sealed record OrderTransitioned(Guid OrderId, Transitioned<OrderState> Change, DateTimeOffset At);

public interface IOrderRepository
{
	Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken = default);

	Task SaveAsync(Order order, CancellationToken cancellationToken = default);
}

public interface IEventPublisher
{
	Task PublishAsync(OrderTransitioned transitioned, CancellationToken cancellationToken = default);
}

// In memory, so the app and its tests need no database. A real repository would also reload the aggregate and check it (see docs/integration.md).
public sealed class InMemoryOrderRepository : IOrderRepository
{
	private readonly ConcurrentDictionary<Guid, Order> _orders = new();

	/// <summary>The order as it is now, without awaiting: for a test that reads state between steps.</summary>
	public Order? Get(Guid id) => _orders.TryGetValue(id, out var order) ? order : null;

	/// <summary>Puts an order in without awaiting, so a test can build its world in a constructor.</summary>
	public void Seed(Order order) => _orders[order.Id] = order;

	public Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Get(id));

	public Task SaveAsync(Order order, CancellationToken cancellationToken = default)
	{
		_orders[order.Id] = order;
		return Task.CompletedTask;
	}
}

public sealed class InMemoryEventPublisher : IEventPublisher
{
	private readonly ConcurrentQueue<OrderTransitioned> _published = new();

	public IReadOnlyList<OrderTransitioned> Published => [.. _published];

	public Task PublishAsync(OrderTransitioned transitioned, CancellationToken cancellationToken = default)
	{
		_published.Enqueue(transitioned);
		return Task.CompletedTask;
	}
}
