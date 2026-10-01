#if NET11_0_OR_GREATER
// Example 3, union edition: the exact same order state machine as OrderStateExamples.cs, but
// OrderStateEvent is a real C# 11 discriminated union instead of a record hierarchy.
//
// Two things had to change to make this work, both confirmed by hand against the installed
// 11.0.100-preview.7 SDK before writing any of this:
//
//   1. `readonly union Name(Case1, Case2, ...);` only parses under LangVersion=preview — see the
//      net11.0-scoped LangVersion override in Directory.Build.props.
//   2. Union case types are NOT subtypes of the union. `readonly union OrderStateEventUnion(...)`
//      compiles to a sealed type, and a case type like OrderStateEventCases.Placed is an ordinary
//      top-level sibling with compiler-synthesized pattern-matching support — not a class deriving
//      from OrderStateEventUnion. `where T : OrderStateEventUnion` rejects the union type itself
//      with CS0701 ("not a valid constraint"), so a case type can never satisfy it either. Every
//      operator in Constraints<TEvent> that used to read `Never<T1,T2>() where T1 : TEvent` is
//      unusable here — hence the predicate-based overloads (Never/NeverTwice/Always/Eventually/
//      AfterUntil, no type parameters) added to ReactiveConstraints.cs alongside this example.
//      `Transitions(Func<TEvent,TEvent,bool>)` already took no type parameter, so it needed no
//      change and is used unmodified below.

using System.Reactive.Subjects;
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using static Nullean.Entities.Reactive.Constraints<Nullean.Entities.Reactive.Examples.OrderStateEventUnion>;

namespace Nullean.Entities.Reactive.Examples;

// ── Domain (union) ───────────────────────────────────────────────────────────

/// <summary>
/// Case types for <see cref="OrderStateEventUnion"/>. Grouped in a container rather than nested
/// inside the union itself — a union's case list names pre-existing sibling types
/// (`readonly union Name(Case1, Case2);`), it does not declare and auto-nest them the way a
/// record hierarchy's nested records do, so there is no `OrderStateEventUnion.Placed`. Grouping
/// them here gives callers `OrderStateEventCases.Placed` as the closest equivalent.
/// </summary>
public static class OrderStateEventCases
{
	public sealed record Placed(Guid OrderId);

	public sealed record Confirmed(Guid OrderId);

	public sealed record Shipped(Guid OrderId);

	public sealed record Cancelled(Guid OrderId);
}

/// <summary>
/// The closed, compiler-checked union of every <see cref="OrderStateEventCases"/> case. A
/// <c>switch</c> over this type covering all four cases needs no discard arm — see
/// <see cref="OrderStateUnionExamples.Describe"/> below — and the compiler flags CS8509 the moment
/// a case is missing, which <c>TreatWarningsAsErrors</c> turns into a hard build failure.
/// </summary>
public readonly union OrderStateEventUnion(OrderStateEventCases.Placed, OrderStateEventCases.Confirmed, OrderStateEventCases.Shipped, OrderStateEventCases.Cancelled);

// OrderState (Pending/Confirmed/Shipped/Cancelled) is the internal enum declared in
// OrderStateExamples.cs — same assembly, same namespace, reused rather than duplicated.

internal sealed class OrderServiceUnion : IDisposable
{
	private readonly Subject<OrderStateEventUnion> _events = new();
	private readonly Dictionary<Guid, OrderState> _orders = [];

	public IObservable<OrderStateEventUnion> Events => _events;

	public void Dispose() => _events.Dispose();

	public void Place(Guid orderId)
	{
		if (_orders.ContainsKey(orderId))
		{
			return; // already placed — a fresh Place is a no-op, not a re-placement
		}
		_orders[orderId] = OrderState.Pending;
		_events.OnNext(new OrderStateEventCases.Placed(orderId));
	}

	public void Confirm(Guid orderId)
	{
		if (_orders.TryGetValue(orderId, out var state) && state == OrderState.Pending)
		{
			_orders[orderId] = OrderState.Confirmed;
			_events.OnNext(new OrderStateEventCases.Confirmed(orderId));
		}
	}

	public void Ship(Guid orderId)
	{
		if (_orders.TryGetValue(orderId, out var state) && state == OrderState.Confirmed)
		{
			_orders[orderId] = OrderState.Shipped;
			_events.OnNext(new OrderStateEventCases.Shipped(orderId));
		}
	}

	public void Cancel(Guid orderId)
	{
		if (_orders.TryGetValue(orderId, out var state) && state is OrderState.Pending or OrderState.Confirmed)
		{
			_orders[orderId] = OrderState.Cancelled;
			_events.OnNext(new OrderStateEventCases.Cancelled(orderId));
		}
	}
}

// ── Constraints, predicate-based (see the file header for why) ───────────────

internal static class OrderStateSpecUnion
{
	/// <summary>Same rule and the same Transitions-not-Never reasoning as OrderStateSpec's own copy.</summary>
	public static readonly ReactiveConstraint<OrderStateEventUnion> NeverShippedWithoutConfirmation = Transitions(
		(prev, next) => !(prev is OrderStateEventCases.Placed p && next is OrderStateEventCases.Shipped s && p.OrderId == s.OrderId)
	).Named(nameof(NeverShippedWithoutConfirmation));

	/// <summary>Every confirmed order eventually reaches a terminal state — AfterUntil, the predicate-based counterpart to After&lt;T&gt;().Then().Within().</summary>
	public static readonly ReactiveConstraint<OrderStateEventUnion> ConfirmedEventuallyTerminates = AfterUntil(
		isTrigger: e => e is OrderStateEventCases.Confirmed,
		isNext: (trigger, next) => trigger is OrderStateEventCases.Confirmed c
			&& next switch
			{
				OrderStateEventCases.Shipped s => s.OrderId == c.OrderId,
				OrderStateEventCases.Cancelled ca => ca.OrderId == c.OrderId,
				_ => false,
			},
		window: TimeSpan.FromSeconds(60)
	).Named(nameof(ConfirmedEventuallyTerminates));

	/// <summary>Cancelled orders are never shipped — predicate-based counterpart to Never&lt;T1,T2&gt;.</summary>
	public static readonly ReactiveConstraint<OrderStateEventUnion> NeverShippedAfterCancellation = Never(
		e => e is OrderStateEventCases.Cancelled,
		e => e is OrderStateEventCases.Shipped,
		(c, s) => c is OrderStateEventCases.Cancelled cc && s is OrderStateEventCases.Shipped ss && cc.OrderId == ss.OrderId
	).Named(nameof(NeverShippedAfterCancellation));

	public static readonly ReactiveConstraintSet<OrderStateEventUnion> All = ReactiveConstraintSet.From(
		(nameof(NeverShippedWithoutConfirmation), NeverShippedWithoutConfirmation),
		(nameof(ConfirmedEventuallyTerminates), ConfirmedEventuallyTerminates),
		(nameof(NeverShippedAfterCancellation), NeverShippedAfterCancellation)
	);
}

// ── Example ────────────────────────────────────────────────────────────────────

public class OrderStateUnionExamples
{
	private static readonly Guid[] Pool = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
	private static readonly Gen<Guid> GenOrderId = Gen.Int[0, Pool.Length - 1].Select(i => Pool[i]);

	private static readonly Script<OrderServiceUnion> OrderScript = Script
		.For<OrderServiceUnion>()
		.Step("Place", GenOrderId, (svc, id) => svc.Place(id))
		.Step("Confirm", GenOrderId, (svc, id) => svc.Confirm(id))
		.Step("Ship", GenOrderId, (svc, id) => svc.Ship(id))
		.Step("Cancel", GenOrderId, (svc, id) => svc.Cancel(id))
		.Delays(Gen.Int[0, 500].Select(ms => TimeSpan.FromMilliseconds(ms)))
		.Length(3, 20);

	[Test]
	public void OrderTransitionsAreValid() => OrderScript.SampleWith(_ => new OrderServiceUnion(), svc => svc.Events, OrderStateSpecUnion.All);

	[Test]
	public void SwitchOverTheUnionIsCompilerCheckedExhaustive()
	{
		// The real point of this test: Describe below has no discard arm and still compiles.
		// Delete any one of its four arms and TreatWarningsAsErrors turns the resulting CS8509
		// ("switch expression does not handle all possible values") into a build failure —
		// confirmed by hand against the 11.0.100-preview.7 SDK while designing this example.
		OrderStateEventUnion placed = new OrderStateEventCases.Placed(Guid.NewGuid());
		OrderStateEventUnion confirmed = new OrderStateEventCases.Confirmed(Guid.NewGuid());
		OrderStateEventUnion shipped = new OrderStateEventCases.Shipped(Guid.NewGuid());
		OrderStateEventUnion cancelled = new OrderStateEventCases.Cancelled(Guid.NewGuid());

		Describe(placed).Should().Be("Placed");
		Describe(confirmed).Should().Be("Confirmed");
		Describe(shipped).Should().Be("Shipped");
		Describe(cancelled).Should().Be("Cancelled");
	}

	private static string Describe(OrderStateEventUnion e) => e switch
	{
		OrderStateEventCases.Placed => "Placed",
		OrderStateEventCases.Confirmed => "Confirmed",
		OrderStateEventCases.Shipped => "Shipped",
		OrderStateEventCases.Cancelled => "Cancelled",
	};
}
#endif
