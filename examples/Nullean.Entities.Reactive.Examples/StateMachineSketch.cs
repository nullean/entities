// ────────────────────────────────────────────────────────────────────────────
// Checkpoint A+: state machine and domain-model design sketch.
//
// Key questions being explored:
//   Q1. Is the core useful without CsCheck?            → yes; split into two packages
//   Q2. Can constraints be first-class domain members?  → yes; via static fields
//   Q3. How do structural vs temporal constraints differ? → different operators
//
// Nothing in this file requires CsCheck. The Script<T>/SampleWith layer
// just becomes one more driver of a constraint set that stands on its own.
// ────────────────────────────────────────────────────────────────────────────

// Sketch file — SUT stubs throw NotImplementedException.

using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Microsoft.Reactive.Testing;
using Nullean.Entities.Reactive;

namespace Nullean.Entities.Reactive.Examples;

// ── Domain ─────────────────────────────────────────────────────────────────
//
// Plain records. Nothing here knows about constraints or observables.
// The C# exhaustive-switch and pattern-matching story is preserved exactly.

internal abstract record OrderEvent
{
	public sealed record Placed(Guid OrderId, decimal Total) : OrderEvent;

	public sealed record PaymentAuthorised(Guid OrderId, string Ref) : OrderEvent;

	public sealed record PaymentFailed(Guid OrderId, string Reason) : OrderEvent;

	public sealed record Fulfilled(Guid OrderId, string TrackingRef) : OrderEvent;

	public sealed record Cancelled(Guid OrderId, string Reason) : OrderEvent;

	public sealed record RefundIssued(Guid OrderId, decimal Amount) : OrderEvent;
}

internal abstract record TrafficLightState
{
	public sealed record Red(TimeSpan Elapsed) : TrafficLightState;

	public sealed record Amber(TimeSpan Elapsed) : TrafficLightState;

	public sealed record Green(TimeSpan Elapsed) : TrafficLightState;
}

// ═══════════════════════════════════════════════════════════════════════════
// PATTERN 1: Separate monitoring spec
// ═══════════════════════════════════════════════════════════════════════════
//
// The spec is defined independently of the domain type. In production you
// attach it to an event bus; in tests to a TestScheduler-driven observable.
// This is the monitoring-layer pattern.

internal static class OrderMonitoringSpec
{
	// ── Structural: forbidden state-machine arcs ────────────────────────

	// "No fulfilled order is subsequently cancelled."
	public static readonly ReactiveConstraint<OrderEvent> NoFulfilmentThenCancellation = Constraints<OrderEvent>.Never<OrderEvent.Fulfilled, OrderEvent.Cancelled>(
		(f, c) => f.OrderId == c.OrderId
	).Named(nameof(NoFulfilmentThenCancellation));

	// "No duplicate placement."
	// NeverTwice catches any two Placed events with the same OrderId,
	// regardless of intervening events — unlike Never<(T,T)>.
	public static readonly ReactiveConstraint<OrderEvent> NoDuplicatePlacement = Constraints<OrderEvent>.NeverTwice<OrderEvent.Placed>(p => p.OrderId).Named(
		nameof(NoDuplicatePlacement)
	);

	// ── Temporal: what must happen within a time window ─────────────────

	// "Every placed order is authorised or cancelled within 15 minutes."
	// Required() means: Pending at end-of-trace is a violation.
	// Without Required(), Pending means the trigger never fired (vacuous Holds).
	public static readonly ReactiveConstraint<OrderEvent> PlacedOrderResolves = Constraints<OrderEvent>
		.After<OrderEvent.Placed>()
		.Then<OrderEvent.PaymentAuthorised, OrderEvent.Cancelled>(
			m1: (placed, auth) => auth.OrderId == placed.OrderId,
			m2: (placed, cancelled) => cancelled.OrderId == placed.OrderId
		)
		.Within(TimeSpan.FromMinutes(15))
		.Required()
		.Named(nameof(PlacedOrderResolves));

	// "After authorisation, fulfilment happens within 48 hours
	//  — unless the order is cancelled first."
	// UnlessInterruptedBy lifts the obligation (result = Holds, not Pending)
	// when the interruption fires before the Then-event.
	public static readonly ReactiveConstraint<OrderEvent> AuthorisedOrderFulfils = Constraints<OrderEvent>
		.After<OrderEvent.PaymentAuthorised>()
		.Then<OrderEvent.Fulfilled>((auth, f) => f.OrderId == auth.OrderId)
		.Within(TimeSpan.FromHours(48))
		.UnlessInterruptedBy<OrderEvent.Cancelled>()
		.Named(nameof(AuthorisedOrderFulfils));

	public static readonly ReactiveConstraintSet<OrderEvent> All = ReactiveConstraintSet.From(
		(nameof(NoFulfilmentThenCancellation), NoFulfilmentThenCancellation),
		(nameof(NoDuplicatePlacement), NoDuplicatePlacement),
		(nameof(PlacedOrderResolves), PlacedOrderResolves),
		(nameof(AuthorisedOrderFulfils), AuthorisedOrderFulfils)
	);
}

// Production use — attach to the event bus; subscribe to violations.
internal sealed class OrderEventMonitor
{
	public OrderEventMonitor(IObservable<OrderEvent> bus)
	{
		var spec = OrderMonitoringSpec.All.Attach(bus);

		spec[OrderMonitoringSpec.PlacedOrderResolves].Verdicts.Where(v => v.IsViolated).Subscribe(v => /* emit metric, alert, span */  _ = v);

		// All constraints as a single stream — useful for a dashboard.
		spec.State.Subscribe(s => /* log s.Verdicts */  _ = s);
	}
}

// ═══════════════════════════════════════════════════════════════════════════
// PATTERN 2: Constraints as a static member of the union
// ═══════════════════════════════════════════════════════════════════════════
//
// The union owns its constraints. "If you produce OrderEvents, these are the rules."
// No consumer needs to discover or construct the constraint set independently —
// OrderEvent2.Spec is the canonical reference.

internal abstract record OrderEvent2
{
	public sealed record Placed(Guid OrderId) : OrderEvent2;

	public sealed record Authorised(Guid OrderId) : OrderEvent2;

	public sealed record Fulfilled(Guid OrderId) : OrderEvent2;

	public sealed record Cancelled(Guid OrderId) : OrderEvent2;

	// Private backing constraints — referenced by the canonical Spec below.
	private static readonly ReactiveConstraint<OrderEvent2> EventualResolution = Constraints<OrderEvent2>
		.After<Placed>()
		.Then<Fulfilled, Cancelled>(m1: (placed, f) => f.OrderId == placed.OrderId, m2: (placed, c) => c.OrderId == placed.OrderId)
		.Within(TimeSpan.FromHours(48))
		.Required()
		.Named("EventualResolution");

	private static readonly ReactiveConstraint<OrderEvent2> NoFulfilmentBeforeAuth = Constraints<OrderEvent2>.Never<Placed, Fulfilled>(
		(p, f) => p.OrderId == f.OrderId
	).Named("NoFulfilmentBeforeAuth");

	// Canonical constraint set — a static member of the union type itself.
	public static readonly ReactiveConstraintSet<OrderEvent2> Spec = ReactiveConstraintSet.From(
		("EventualResolution", EventualResolution),
		("NoFulfilmentBeforeAuth", NoFulfilmentBeforeAuth)
	);
}

// Consumer attaches the union's own constraint set:
internal sealed class OrderProcessor
{
	public OrderProcessor(IObservable<OrderEvent2> events) => OrderEvent2.Spec.Attach(events);
}

// ═══════════════════════════════════════════════════════════════════════════
// PATTERN 3: Constraints as live properties on a domain object
// ═══════════════════════════════════════════════════════════════════════════
//
// Simplified — a source generator could add IHaveConstraints<TEvent> and auto-name
// constraints from field names. For now we build the set manually.

internal static class OrderAggregate
{
	public static readonly ReactiveConstraint<OrderEvent> PlacedOrderResolves = Constraints<OrderEvent>
		.After<OrderEvent.Placed>()
		.Then<OrderEvent.PaymentAuthorised, OrderEvent.Cancelled>(
			m1: (placed, auth) => auth.OrderId == placed.OrderId,
			m2: (placed, cancelled) => cancelled.OrderId == placed.OrderId
		)
		.Within(TimeSpan.FromMinutes(15))
		.Required()
		.Named(nameof(PlacedOrderResolves));

	public static readonly ReactiveConstraint<OrderEvent> AuthorisedOrderFulfils = Constraints<OrderEvent>
		.After<OrderEvent.PaymentAuthorised>()
		.Then<OrderEvent.Fulfilled>((auth, f) => f.OrderId == auth.OrderId)
		.Within(TimeSpan.FromHours(48))
		.UnlessInterruptedBy<OrderEvent.Cancelled>()
		.Named(nameof(AuthorisedOrderFulfils));

	public static readonly ReactiveConstraint<OrderEvent> NoDuplicates = Constraints<OrderEvent>.NeverTwice<OrderEvent.Placed>(p => p.OrderId).Named(
		nameof(NoDuplicates)
	);

	public static readonly ReactiveConstraintSet<OrderEvent> All = ReactiveConstraintSet.From(
		(nameof(PlacedOrderResolves), PlacedOrderResolves),
		(nameof(AuthorisedOrderFulfils), AuthorisedOrderFulfils),
		(nameof(NoDuplicates), NoDuplicates)
	);
}

// A service that exposes the live constraint state:
internal sealed class OrderService3
{
	private MonitoredConstraintSet<OrderEvent>? _running;

	public void Attach(IObservable<OrderEvent> events) => _running = OrderAggregate.All.Attach(events);

	// Access individual constraints by typed key — compile-time safe.
	public MonitoredConstraint<OrderEvent>? PlacedOrderResolves => _running?[OrderAggregate.PlacedOrderResolves];

	// All constraints as a change stream — useful for dashboards.
	public IObservable<ConstraintSetState>? State => _running?.State;
}

// ═══════════════════════════════════════════════════════════════════════════
// PATTERN 4: Transition table for finite state machines
// ═══════════════════════════════════════════════════════════════════════════
//
// For the common "finite set of named states" case, expressing forbidden arcs
// one by one with Never<T1,T2> is tedious. A Transitions operator takes an
// explicit allowlist — violations are anything not in it.

internal static class TrafficLightSpec
{
	// Structural: only legal arcs.
	// Any consecutive pair not in this table is a violation.
	public static readonly ReactiveConstraint<TrafficLightState> LegalTransitions = Constraints<TrafficLightState>.Transitions(
		(From: typeof(TrafficLightState.Red), To: typeof(TrafficLightState.Green)),
		(From: typeof(TrafficLightState.Green), To: typeof(TrafficLightState.Amber)),
		(From: typeof(TrafficLightState.Amber), To: typeof(TrafficLightState.Red))
	).Named(nameof(LegalTransitions));

	// Temporal: minimum durations are separate from the structural spec.
	public static readonly ReactiveConstraint<TrafficLightState> GreenLongEnough = Constraints<TrafficLightState>.Always<TrafficLightState.Green>(
		g => g.Elapsed >= TimeSpan.FromSeconds(15)
	).Named(nameof(GreenLongEnough));

	public static readonly ReactiveConstraint<TrafficLightState> AmberIsShort = Constraints<TrafficLightState>.Always<TrafficLightState.Amber>(
		a => a.Elapsed <= TimeSpan.FromSeconds(5)
	).Named(nameof(AmberIsShort));

	public static readonly ReactiveConstraintSet<TrafficLightState> All = ReactiveConstraintSet.From(
		(nameof(LegalTransitions), LegalTransitions),
		(nameof(GreenLongEnough), GreenLongEnough),
		(nameof(AmberIsShort), AmberIsShort)
	);
}

// ═══════════════════════════════════════════════════════════════════════════
// TEST: using the spec without CsCheck (TestScheduler only)
// ═══════════════════════════════════════════════════════════════════════════
//
// No Script<T>, no SampleWith — just a TestScheduler and manual feed.
// Useful for unit-testing individual constraints in isolation.

internal sealed class TrafficLightSpecTests
{
	[Test]
	public void IllegalTransitionViolatesSpec()
	{
		var scheduler = new TestScheduler();
		var subject = new System.Reactive.Subjects.Subject<TrafficLightState>();
		var spec = TrafficLightSpec.All.Attach(subject, scheduler);

		subject.OnNext(new TrafficLightState.Green(TimeSpan.FromSeconds(30)));

		// Red → Amber (skipping Green→Amber→Red cycle) is illegal.
		subject.OnNext(new TrafficLightState.Red(TimeSpan.FromSeconds(40)));

		// spec[TrafficLightSpec.LegalTransitions].Current is now Violated.
		_ = spec[TrafficLightSpec.LegalTransitions].Current;
	}
}
