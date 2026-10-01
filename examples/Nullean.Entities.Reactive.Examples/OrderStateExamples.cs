// Example 3: an order state machine.
//
// An order starts as Pending, moves to Confirmed, then to Shipped or Cancelled.
// The spec verifies that the state machine's transitions are legal and that
// every order that is confirmed eventually ships or is cancelled.

using System.Reactive.Subjects;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Reactive;
using static Nullean.Entities.Reactive.Constraints<Nullean.Entities.Reactive.Examples.OrderStateEvent>;

namespace Nullean.Entities.Reactive.Examples;

// ── Domain ───────────────────────────────────────────────────────────────────

internal abstract record OrderStateEvent
{
	public sealed record Placed(Guid OrderId) : OrderStateEvent;

	public sealed record Confirmed(Guid OrderId) : OrderStateEvent;

	public sealed record Shipped(Guid OrderId) : OrderStateEvent;

	public sealed record Cancelled(Guid OrderId) : OrderStateEvent;
}

internal enum OrderState
{
	Pending,
	Confirmed,
	Shipped,
	Cancelled,
}

internal sealed class OrderService : IDisposable
{
	private readonly Subject<OrderStateEvent> _events = new();
	private readonly Dictionary<Guid, OrderState> _orders = [];

	public IObservable<OrderStateEvent> Events => _events;

	public void Dispose() => _events.Dispose();

	public void Place(Guid orderId)
	{
		if (_orders.ContainsKey(orderId))
		{
			return; // already placed — a fresh Place is a no-op, not a re-placement
		}
		_orders[orderId] = OrderState.Pending;
		_events.OnNext(new OrderStateEvent.Placed(orderId));
	}

	public void Confirm(Guid orderId)
	{
		if (_orders.TryGetValue(orderId, out var state) && state == OrderState.Pending)
		{
			_orders[orderId] = OrderState.Confirmed;
			_events.OnNext(new OrderStateEvent.Confirmed(orderId));
		}
	}

	public void Ship(Guid orderId)
	{
		if (_orders.TryGetValue(orderId, out var state) && state == OrderState.Confirmed)
		{
			_orders[orderId] = OrderState.Shipped;
			_events.OnNext(new OrderStateEvent.Shipped(orderId));
		}
	}

	public void Cancel(Guid orderId)
	{
		if (_orders.TryGetValue(orderId, out var state) && state is OrderState.Pending or OrderState.Confirmed)
		{
			_orders[orderId] = OrderState.Cancelled;
			_events.OnNext(new OrderStateEvent.Cancelled(orderId));
		}
	}
}

// ── Constraints class ──────────────────────────────────────────────────────────

internal static class OrderStateSpec
{
	/// <summary>
	/// No order skips the Confirmed state to reach Shipped. Deliberately <c>Transitions</c>
	/// (raw-stream adjacency), not <c>Never&lt;Placed,Shipped&gt;</c>: <c>Never&lt;T1,T2&gt;</c>
	/// filters the stream down to <c>{T1,T2}</c> events *before* checking adjacency, so a genuinely
	/// legal Placed → Confirmed → Shipped sequence makes Placed and Shipped look falsely adjacent
	/// the moment Confirmed gets filtered out from between them — a real false positive CsCheck's
	/// own generated runs found on the very first pass.
	/// </summary>
	public static readonly ReactiveConstraint<OrderStateEvent> NeverShippedWithoutConfirmation = Transitions(
		(prev, next) => !(prev is OrderStateEvent.Placed p && next is OrderStateEvent.Shipped s && p.OrderId == s.OrderId)
	).Named(nameof(NeverShippedWithoutConfirmation));

	/// <summary>Every confirmed order eventually reaches a terminal state.</summary>
	public static readonly ReactiveConstraint<OrderStateEvent> ConfirmedEventuallyTerminates = After<OrderStateEvent.Confirmed>()
		.Then<OrderStateEvent.Shipped, OrderStateEvent.Cancelled>(
			m1: (confirmed, shipped) => shipped.OrderId == confirmed.OrderId,
			m2: (confirmed, cancelled) => cancelled.OrderId == confirmed.OrderId
		)
		.Within(TimeSpan.FromSeconds(60));

	/// <summary>Cancelled orders are never shipped.</summary>
	public static readonly ReactiveConstraint<OrderStateEvent> NeverShippedAfterCancellation = Never<OrderStateEvent.Cancelled, OrderStateEvent.Shipped>(
		(c, s) => c.OrderId == s.OrderId
	).Named(nameof(NeverShippedAfterCancellation));

	public static readonly ReactiveConstraintSet<OrderStateEvent> All = ReactiveConstraintSet.From(
		(nameof(NeverShippedWithoutConfirmation), NeverShippedWithoutConfirmation),
		(nameof(ConfirmedEventuallyTerminates), ConfirmedEventuallyTerminates),
		(nameof(NeverShippedAfterCancellation), NeverShippedAfterCancellation)
	);
}

// ── Fluent style ─────────────────────────────────────────────────────────────

public class OrderStateFluentExamples
{
	// A small fixed pool, not a fresh Guid per step — otherwise Confirm/Ship/Cancel almost always
	// land on an order nothing else ever touched, and every constraint above holds only vacuously.
	private static readonly Guid[] Pool = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
	private static readonly Gen<Guid> GenOrderId = Gen.Int[0, Pool.Length - 1].Select(i => Pool[i]);

	private static readonly Script<OrderService> OrderScript = Script
		.For<OrderService>()
		.Step("Place", GenOrderId, (svc, id) => svc.Place(id))
		.Step("Confirm", GenOrderId, (svc, id) => svc.Confirm(id))
		.Step("Ship", GenOrderId, (svc, id) => svc.Ship(id))
		.Step("Cancel", GenOrderId, (svc, id) => svc.Cancel(id))
		.Delays(Gen.Int[0, 500].Select(ms => TimeSpan.FromMilliseconds(ms)))
		.Length(3, 20);

	// NeverShippedBeforeConfirmation deliberately uses Transitions (raw-stream adjacency), not
	// Never<Placed,Shipped> — see OrderStateSpec's own copy of this constraint for why
	// Never<T1,T2>'s filter-then-adjacency semantics produce a false positive here.
	private static readonly ReactiveConstraintSet<OrderStateEvent> Spec = ReactiveConstraintSet.From(
		("NeverShippedBeforeConfirmation", Transitions(
			(prev, next) => !(prev is OrderStateEvent.Placed p && next is OrderStateEvent.Shipped s && p.OrderId == s.OrderId)
		)),
		("ConfirmedOrderTerminates", After<OrderStateEvent.Confirmed>()
			.Then<OrderStateEvent.Shipped, OrderStateEvent.Cancelled>(m1: (c, s) => s.OrderId == c.OrderId, m2: (c, ca) => ca.OrderId == c.OrderId)
			.Within(TimeSpan.FromSeconds(60))),
		("NeverShippedAfterCancellation", Never<OrderStateEvent.Cancelled, OrderStateEvent.Shipped>((c, s) => c.OrderId == s.OrderId))
	);

	// A green run only means something if the interesting events happened: without this, a pool/step mix that never
	// confirms an order would "prove" NeverShippedBeforeConfirmation. SampleWith fails with VacuousPropertyException instead.
	private static readonly Coverage<OrderStateEvent> Seen = new Coverage<OrderStateEvent>()
		.Expect("Confirmed", e => e is OrderStateEvent.Confirmed)
		.Expect("Shipped", e => e is OrderStateEvent.Shipped)
		.Expect("Cancelled", e => e is OrderStateEvent.Cancelled);

	[Test]
	public void OrderTransitionsAreValid() => OrderScript.SampleWith(_ => new OrderService(), svc => svc.Events, Spec, Seen);
}

// ── Constraints class style ───────────────────────────────────────────────────

public class OrderStateSpecClassExamples
{
	private static readonly Guid[] Pool = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
	private static readonly Gen<Guid> GenOrderId = Gen.Int[0, Pool.Length - 1].Select(i => Pool[i]);

	private static readonly Script<OrderService> OrderScript = Script
		.For<OrderService>()
		.Step("Place", GenOrderId, (svc, id) => svc.Place(id))
		.Step("Confirm", GenOrderId, (svc, id) => svc.Confirm(id))
		.Step("Ship", GenOrderId, (svc, id) => svc.Ship(id))
		.Step("Cancel", GenOrderId, (svc, id) => svc.Cancel(id))
		.Delays(Gen.Int[0, 500].Select(ms => TimeSpan.FromMilliseconds(ms)))
		.Length(3, 20);

	// A green run only means something if the interesting events happened: without this, a pool/step mix that never
	// confirms an order would "prove" NeverShippedBeforeConfirmation. SampleWith fails with VacuousPropertyException instead.
	private static readonly Coverage<OrderStateEvent> Seen = new Coverage<OrderStateEvent>()
		.Expect("Confirmed", e => e is OrderStateEvent.Confirmed)
		.Expect("Shipped", e => e is OrderStateEvent.Shipped)
		.Expect("Cancelled", e => e is OrderStateEvent.Cancelled);

	[Test]
	public void OrderTransitionsAreValid() => OrderScript.SampleWith(_ => new OrderService(), svc => svc.Events, OrderStateSpec.All, Seen);
}
