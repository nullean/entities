// The impure half of the Order domain: a service that owns a small pool of Order aggregates
// (mutated through the same pure Order methods in OrderDomain.cs) and reports every attempted
// action as an OrderEvent on an observable stream. Illegal actions (e.g. Ship on an unconfirmed
// order) are reported as Rejected rather than throwing — a real SUT that a generated random walk
// drives has to tolerate being asked to do the wrong thing in the wrong order, since that is most
// of what CsCheck will actually generate.

using System.Reactive.Subjects;

namespace Nullean.Entities.CsCheck.Examples;

public abstract record OrderEvent
{
	public sealed record Placed(Guid OrderId) : OrderEvent;

	public sealed record LineAdded(Guid OrderId, string Sku, int Quantity) : OrderEvent;

	public sealed record Confirmed(Guid OrderId) : OrderEvent;

	public sealed record Shipped(Guid OrderId) : OrderEvent;

	public sealed record Cancelled(Guid OrderId) : OrderEvent;

	/// <summary>
	/// Fires alongside <see cref="Shipped"/> or <see cref="Cancelled"/> — a dedicated "this order
	/// will never change state again" signal, so an unbounded-liveness check
	/// (<c>Eventually&lt;T&gt;</c> only ever names one target type) can ask "did every confirmed
	/// order eventually reach *a* terminal state" without caring which one.
	/// </summary>
	public sealed record Closed(Guid OrderId) : OrderEvent;

	/// <summary>An action was attempted against an order it isn't legal for right now.</summary>
	public sealed record Rejected(Guid OrderId, string Action) : OrderEvent;
}

public sealed class OrderService : IDisposable
{
	private readonly Subject<OrderEvent> _events = new();
	private readonly Dictionary<Guid, Order> _orders = [];

	public IObservable<OrderEvent> Events => _events;

	public void Dispose() => _events.Dispose();

	public void Place(Guid id)
	{
		if (_orders.ContainsKey(id))
		{
			_events.OnNext(new OrderEvent.Rejected(id, nameof(Place)));
			return;
		}
		_orders[id] = Order.New(id);
		_events.OnNext(new OrderEvent.Placed(id));
	}

	public void AddLine(Guid id, string sku, int quantity)
	{
		if (!_orders.TryGetValue(id, out var order) || order.Status != OrderStatus.Pending)
		{
			_events.OnNext(new OrderEvent.Rejected(id, nameof(AddLine)));
			return;
		}
		_orders[id] = order.AddLine(new OrderLine(sku, quantity, 1m));
		_events.OnNext(new OrderEvent.LineAdded(id, sku, quantity));
	}

	public void Confirm(Guid id)
	{
		if (!_orders.TryGetValue(id, out var order) || order.Status != OrderStatus.Pending || order.Lines.Count == 0)
		{
			_events.OnNext(new OrderEvent.Rejected(id, nameof(Confirm)));
			return;
		}
		_orders[id] = order.Confirm();
		_events.OnNext(new OrderEvent.Confirmed(id));
	}

	public void Ship(Guid id)
	{
		if (!_orders.TryGetValue(id, out var order) || order.Status != OrderStatus.Confirmed)
		{
			_events.OnNext(new OrderEvent.Rejected(id, nameof(Ship)));
			return;
		}
		_orders[id] = order.Ship();
		_events.OnNext(new OrderEvent.Shipped(id));
		_events.OnNext(new OrderEvent.Closed(id));
	}

	public void Cancel(Guid id)
	{
		if (!_orders.TryGetValue(id, out var order) || order.Status is OrderStatus.Shipped or OrderStatus.Cancelled)
		{
			_events.OnNext(new OrderEvent.Rejected(id, nameof(Cancel)));
			return;
		}
		_orders[id] = order.Cancel();
		_events.OnNext(new OrderEvent.Cancelled(id));
		_events.OnNext(new OrderEvent.Closed(id));
	}
}
