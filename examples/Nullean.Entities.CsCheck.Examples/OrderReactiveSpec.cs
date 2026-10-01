// Nullean.Entities.Reactive constraints over OrderEvent — the structural (Absent) and temporal
// (After/Then/Within, Eventually) qualities, checked against a real Script<OrderService> run
// rather than a hand-picked example. `using static` (not `using Nullean.Entities.Reactive;`)
// deliberately avoids ever naming the bare `Constraints<T>` type in this file — see
// ScriptTests.cs's own note on why the sync and reactive builders collide by name once a file's
// namespace nests inside Nullean.Entities.

using System.Reactive.Concurrency;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Reactive;
using static Nullean.Entities.Reactive.Constraints<Nullean.Entities.CsCheck.Examples.OrderEvent>;

namespace Nullean.Entities.CsCheck.Examples;

public static class OrderReactiveSpec
{
	/// <summary>Absent: a cancelled order is never shipped afterward — checked by matching OrderId.</summary>
	public static readonly ReactiveConstraint<OrderEvent> NeverShippedAfterCancellation = Never<OrderEvent.Cancelled, OrderEvent.Shipped>(
		(c, s) => c.OrderId == s.OrderId
	).Named(nameof(NeverShippedAfterCancellation));

	/// <summary>Temporal: every confirmed order reaches a terminal state within the deadline.</summary>
	public static readonly ReactiveConstraint<OrderEvent> ConfirmedOrdersCloseWithinDeadline = After<OrderEvent.Confirmed>()
		.Then<OrderEvent.Closed>((confirmed, closed) => confirmed.OrderId == closed.OrderId)
		.Within(TimeSpan.FromSeconds(30))
		.Required();

	/// <summary>
	/// Liveness, unbounded: every confirmed order eventually closes — no deadline, unlike the
	/// temporal constraint above, which additionally bounds how long that's allowed to take.
	/// </summary>
	public static readonly ReactiveConstraint<OrderEvent> EveryConfirmedOrderEventuallyCloses = Eventually<OrderEvent.Closed>(
		scope: e =>
			e switch
			{
				OrderEvent.Confirmed c => c.OrderId,
				OrderEvent.Closed cl => cl.OrderId,
				_ => Guid.Empty,
			},
		from: e => e is OrderEvent.Confirmed,
		message: "Every confirmed order must eventually close (ship or cancel)."
	).Named(nameof(EveryConfirmedOrderEventuallyCloses));

	public static readonly ReactiveConstraintSet<OrderEvent> All = ReactiveConstraintSet.From(
		(nameof(NeverShippedAfterCancellation), NeverShippedAfterCancellation),
		(nameof(ConfirmedOrdersCloseWithinDeadline), ConfirmedOrdersCloseWithinDeadline),
		(nameof(EveryConfirmedOrderEventuallyCloses), EveryConfirmedOrderEventuallyCloses)
	);
}

public static class OrderScripts
{
	/// <summary>
	/// A small, fixed pool of order IDs — not a fresh Guid per step — so that Confirm/Ship/Cancel
	/// steps actually land on an order Place/AddLine already touched often enough to exercise real
	/// state transitions, rather than mostly no-op Rejected events against unknown orders.
	/// </summary>
	private static readonly Guid[] Pool = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

	private static readonly Gen<Guid> GenOrderId = Gen.Int[0, Pool.Length - 1].Select(i => Pool[i]);
	private static readonly Gen<string> GenSku = Gen.Int[0, 2].Select(i => $"SKU-{i}");
	private static readonly Gen<int> GenQuantity = Gen.Int[1, 5];

	public static readonly Script<OrderService> Lifecycle = Script
		.For<OrderService>()
		.Step("Place", GenOrderId, (svc, id) => svc.Place(id))
		.Step("AddLine", Gen.Select(GenOrderId, GenSku, GenQuantity, (id, sku, qty) => (id, sku, qty)), (svc, t) => svc.AddLine(t.id, t.sku, t.qty))
		.Step("Confirm", GenOrderId, (svc, id) => svc.Confirm(id))
		.Step("Ship", GenOrderId, (svc, id) => svc.Ship(id))
		.Step("Cancel", GenOrderId, (svc, id) => svc.Cancel(id))
		.Delays(Gen.Int[0, 500].Select(ms => TimeSpan.FromMilliseconds(ms)))
		.Length(3, 25);
}
