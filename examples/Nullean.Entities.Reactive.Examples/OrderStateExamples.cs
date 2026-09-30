// Example 3: an order state machine.
//
// An order starts as Pending, moves to Confirmed, then to Shipped or Cancelled.
// The spec verifies that the state machine's transitions are legal and that
// every order that is confirmed eventually ships or is cancelled.

using System.Reactive.Concurrency;
using System.Reactive.Linq;
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

internal sealed class OrderService(IScheduler scheduler)
{
	public IObservable<OrderStateEvent> Events { get; } = Observable.Empty<OrderStateEvent>(scheduler);

	public void Place(Guid orderId) => throw new NotImplementedException("SUT stub");
	public void Confirm(Guid orderId) => throw new NotImplementedException("SUT stub");
	public void Ship(Guid orderId) => throw new NotImplementedException("SUT stub");
	public void Cancel(Guid orderId) => throw new NotImplementedException("SUT stub");
}

// ── Constraints class ──────────────────────────────────────────────────────────

internal static class OrderStateSpec
{
	/// <summary>No order skips the Confirmed state to reach Shipped.</summary>
	public static readonly ReactiveConstraint<OrderStateEvent> NeverShippedWithoutConfirmation = Never<OrderStateEvent.Placed, OrderStateEvent.Shipped>(
		(p, s) => p.OrderId == s.OrderId
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
	private static readonly Gen<Guid> GenOrderId = Gen.Guid;

	private static readonly Script<OrderService> OrderScript = Script
		.For<OrderService>()
		.Step(GenOrderId, (svc, id) => svc.Place(id))
		.Step(GenOrderId, (svc, id) => svc.Confirm(id))
		.Step(GenOrderId, (svc, id) => svc.Ship(id))
		.Step(GenOrderId, (svc, id) => svc.Cancel(id))
		.Delays(Gen.Int[0, 500].Select(ms => TimeSpan.FromMilliseconds(ms)));

	private static readonly ReactiveConstraintSet<OrderStateEvent> Spec = ReactiveConstraintSet.From(
		("NeverShippedBeforeConfirmation", Never<OrderStateEvent.Placed, OrderStateEvent.Shipped>((p, s) => p.OrderId == s.OrderId)),
		("ConfirmedOrderTerminates", After<OrderStateEvent.Confirmed>()
			.Then<OrderStateEvent.Shipped, OrderStateEvent.Cancelled>(m1: (c, s) => s.OrderId == c.OrderId, m2: (c, ca) => ca.OrderId == c.OrderId)
			.Within(TimeSpan.FromSeconds(60))),
		("NeverShippedAfterCancellation", Never<OrderStateEvent.Cancelled, OrderStateEvent.Shipped>((c, s) => c.OrderId == s.OrderId))
	);

	[Test]
	public void OrderTransitionsAreValid() => OrderScript.SampleWith(scheduler => new OrderService(scheduler), svc => svc.Events, Spec);
}

// ── Constraints class style ───────────────────────────────────────────────────

public class OrderStateSpecClassExamples
{
	private static readonly Script<OrderService> OrderScript = Script
		.For<OrderService>()
		.Step(Gen.Guid, (svc, id) => svc.Place(id))
		.Step(Gen.Guid, (svc, id) => svc.Confirm(id))
		.Step(Gen.Guid, (svc, id) => svc.Ship(id))
		.Step(Gen.Guid, (svc, id) => svc.Cancel(id))
		.Delays(Gen.Int[0, 500].Select(ms => TimeSpan.FromMilliseconds(ms)));

	[Test]
	public void OrderTransitionsAreValid() => OrderScript.SampleWith(scheduler => new OrderService(scheduler), svc => svc.Events, OrderStateSpec.All);
}
