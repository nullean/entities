// Reactive extension for Commerce/OrderLifecycle.
//
// Shows how to build a ReactiveConstraintSet over domain events produced by OrderService,
// using the same domain types as examples/Nullean.Entities.Examples.

using System.Reactive.Subjects;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Reactive;
using static Nullean.Entities.Reactive.Constraints<Nullean.Entities.Reactive.Examples.Commerce.OrderEvent>;

namespace Nullean.Entities.Reactive.Examples.Commerce;

// ── Events ────────────────────────────────────────────────────────────────────

internal abstract record OrderEvent
{
	public sealed record Placed(Guid OrderId) : OrderEvent;

	public sealed record Confirmed(Guid OrderId) : OrderEvent;

	public sealed record Shipped(Guid OrderId) : OrderEvent;

	public sealed record Cancelled(Guid OrderId) : OrderEvent;
}

// ── Service ───────────────────────────────────────────────────────────────────

internal sealed class OrderService : IDisposable
{
	private readonly Subject<OrderEvent> _events = new();
	private readonly Dictionary<Guid, string> _states = [];

	public IObservable<OrderEvent> Events => _events;

	public void Dispose() => _events.Dispose();

	public void Place(Guid id)
	{
		if (_states.TryAdd(id, "Pending"))
		{
			_events.OnNext(new OrderEvent.Placed(id));
		}
	}

	public void Confirm(Guid id)
	{
		if (_states.TryGetValue(id, out var s) && s == "Pending")
		{
			_states[id] = "Confirmed";
			_events.OnNext(new OrderEvent.Confirmed(id));
		}
	}

	public void Ship(Guid id)
	{
		if (_states.TryGetValue(id, out var s) && s == "Confirmed")
		{
			_states[id] = "Shipped";
			_events.OnNext(new OrderEvent.Shipped(id));
		}
	}

	public void Cancel(Guid id)
	{
		if (_states.TryGetValue(id, out var s) && s is "Pending" or "Confirmed")
		{
			_states[id] = "Cancelled";
			_events.OnNext(new OrderEvent.Cancelled(id));
		}
	}
}

// ── Spec ──────────────────────────────────────────────────────────────────────

internal static class OrderEventSpec
{
	/// <summary>No order skips Confirmed and jumps straight to Shipped.</summary>
	public static readonly ReactiveConstraint<OrderEvent> NeverShippedWithoutConfirmation = Transitions(
		(prev, next) => !(prev is OrderEvent.Placed p && next is OrderEvent.Shipped s && p.OrderId == s.OrderId)
	).Named(nameof(NeverShippedWithoutConfirmation));

	/// <summary>Every confirmed order eventually reaches a terminal state.</summary>
	public static readonly ReactiveConstraint<OrderEvent> ConfirmedEventuallyTerminates = After<OrderEvent.Confirmed>()
		.Then<OrderEvent.Shipped, OrderEvent.Cancelled>(m1: (c, s) => s.OrderId == c.OrderId, m2: (c, x) => x.OrderId == c.OrderId)
		.Within(TimeSpan.FromSeconds(60));

	/// <summary>Cancelled orders are never shipped.</summary>
	public static readonly ReactiveConstraint<OrderEvent> NeverShippedAfterCancellation = Never<OrderEvent.Cancelled, OrderEvent.Shipped>(
		(c, s) => c.OrderId == s.OrderId
	).Named(nameof(NeverShippedAfterCancellation));

	public static readonly ReactiveConstraintSet<OrderEvent> All = ReactiveConstraintSet.From(
		(nameof(NeverShippedWithoutConfirmation), NeverShippedWithoutConfirmation),
		(nameof(ConfirmedEventuallyTerminates), ConfirmedEventuallyTerminates),
		(nameof(NeverShippedAfterCancellation), NeverShippedAfterCancellation)
	);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class OrderLifecycleEventTests
{
	private static readonly Guid[] Pool = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
	private static readonly Gen<Guid> GenId = Gen.Int[0, Pool.Length - 1].Select(i => Pool[i]);

	private static readonly Script<OrderService> OrderScript = Script
		.For<OrderService>()
		.Step("Place", GenId, (svc, id) => svc.Place(id))
		.Step("Confirm", GenId, (svc, id) => svc.Confirm(id))
		.Step("Ship", GenId, (svc, id) => svc.Ship(id))
		.Step("Cancel", GenId, (svc, id) => svc.Cancel(id))
		.Delays(Gen.Int[0, 500].Select(ms => TimeSpan.FromMilliseconds(ms)))
		.Length(3, 20);

	[Test]
	public void OrderLifecycleSpec_holds_over_random_event_streams() => OrderScript.SampleWith(_ => new OrderService(), svc => svc.Events, OrderEventSpec.All);
}
