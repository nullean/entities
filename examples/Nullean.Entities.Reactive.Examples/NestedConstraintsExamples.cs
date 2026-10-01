// Example 4: nested constraints across a richer domain, using [Constraint] end to end.
//
// An order is composed of lines. Each line owns its own rules (self-attach); the order's rules
// compose them (a companion spec, generated); and one rule needs TimeProvider, which [Constraint]
// can't express on its own — a [Constraint] field is static and dependency-free by construction.
// The reactive half repeats the same companion shape for the event union.

using System.Reactive.Subjects;
using AwesomeAssertions;
using Nullean.Entities.Reactive;

namespace Nullean.Entities.Reactive.Examples;

// ── Domain ───────────────────────────────────────────────────────────────────

internal enum NestedOrderStatus
{
	Pending,
	Confirmed,
	Shipped,
	Cancelled,
}

// Self-attach: NestedOrderLine owns its own rules and gets IHaveConstraints<NestedOrderLine> for free.
internal sealed partial record NestedOrderLine(string Sku, int Quantity, decimal Price)
{
	// Nullean.Entities.Reactive.Examples nests inside Nullean.Entities.Reactive, which declares its
	// OWN Constraints<TEvent> — that containing namespace wins over the sync Constraints<T> for an
	// unqualified reference, so the sync builder needs its namespace spelled out here (see the same
	// note in tests/Nullean.Entities.Reactive.Tests/ReactiveConstraintTests.cs).
	[Constraint]
	public static readonly Constraint<NestedOrderLine> PositiveQuantity = global::Nullean
		.Entities
		.Constraints<NestedOrderLine>
		.Always(l => l.Quantity > 0, "Quantity must be positive.");

	[Constraint]
	public static readonly Constraint<NestedOrderLine> NonNegativePrice = global::Nullean
		.Entities
		.Constraints<NestedOrderLine>
		.Always(l => l.Price >= 0, "Price must not be negative.");
}

internal sealed record Order(Guid Id, string CustomerEmail, IReadOnlyList<NestedOrderLine> Lines, NestedOrderStatus Status, DateTimeOffset? ConfirmedAt)
{
	public decimal Total => Lines.Sum(l => l.Quantity * l.Price);
}

// Companion spec: Order stays untouched. NestedOrderSpec gets the generated Constraints property, plus a
// nested TypeConstraints : IConstraintsFor<Order> — used generically below.
internal static partial class NestedOrderSpec
{
	[Constraint]
	public static readonly Constraint<Order> HasAtLeastOneLine = global::Nullean
		.Entities
		.Constraints<Order>
		.Always(o => o.Lines.Count > 0, "Order must have at least one line.");

	// Nested composition: every line's own (self-attach) violations roll up into the order's. The
	// position goes into Message/Evidence, not ConstraintId — whichever ConstraintSet.Validate runs
	// last always overwrites ConstraintId with its OWN field name (here, "EveryLineIsValid"), so a
	// re-tag on the inner violation's ConstraintId would just be discarded by the outer Validate.
	[Constraint]
	public static readonly Constraint<Order> EveryLineIsValid = global::Nullean
		.Entities
		.Constraints<Order>
		.Check(
			o =>
				o.Lines.SelectMany(
					(line, i) => NestedOrderLine.All.Validate(line).Select(v => v with { Message = $"Line {i}: {v.Message}", Evidence = i })
				)
		);

	// A rule that needs TimeProvider can't be a [Constraint] field — those are static and
	// dependency-free by construction, and TimeProvider is exactly the kind of thing that should be
	// injected, not hard-coded to TimeProvider.System. Build it by hand and fold it into the
	// generated set with a small factory instead. `All` (unqualified) below is this class's
	// own generated property — no namespace collision, since it takes no type argument.
	public static ConstraintSet<Order> WithDeadline(TimeProvider clock)
	{
		var shipsWithinDeadline = global::Nullean
			.Entities
			.Constraints<Order>
			.Always(
				o =>
					o.Status != NestedOrderStatus.Confirmed || o.ConfirmedAt is not { } confirmedAt || clock.GetUtcNow() - confirmedAt < TimeSpan.FromHours(
						48
					),
				"A confirmed order must ship within 48 hours of confirmation."
			);

		return ConstraintSet.From([.. All.AsConstraints().Select(c => (c.Name!, c)), (nameof(shipsWithinDeadline), shipsWithinDeadline)]);
	}
}

// ── Events ───────────────────────────────────────────────────────────────────

internal abstract record NestedOrderEvent
{
	public sealed record Placed(Guid OrderId) : NestedOrderEvent;

	public sealed record Confirmed(Guid OrderId) : NestedOrderEvent;

	public sealed record Shipped(Guid OrderId) : NestedOrderEvent;

	public sealed record Cancelled(Guid OrderId) : NestedOrderEvent;
}

// Same companion shape as NestedOrderSpec, on the reactive side. No self-attach interface to reach for
// here — see IReactiveConstraintsFor<TEvent>'s own doc comment for why.
internal static partial class NestedOrderEventSpec
{
	[Constraint]
	public static readonly ReactiveConstraint<NestedOrderEvent> NeverShippedBeforeConfirmed = Constraints<NestedOrderEvent>.Never<NestedOrderEvent.Placed, NestedOrderEvent.Shipped>(
		(p, s) => p.OrderId == s.OrderId
	);

	[Constraint]
	public static readonly ReactiveConstraint<NestedOrderEvent> NeverShippedAfterCancellation = Constraints<NestedOrderEvent>.Never<NestedOrderEvent.Cancelled, NestedOrderEvent.Shipped>(
		(c, s) => c.OrderId == s.OrderId
	);
}

// ── Generic dispatch over IConstraintsFor<T> / IReactiveConstraintsFor<TEvent> ──────────────────
//
// Neither helper below needs to know NestedOrderSpec or NestedOrderEventSpec exist. T/TEvent are inferred from
// the spec argument, because the interfaces are plain instance interfaces, not static-abstract —
// see IConstraintsFor<T>'s own doc comment in Nullean.Entities/Constraints.cs.

internal static class GenericDispatch
{
	public static IReadOnlyList<Violation> Validate<T>(T value, IConstraintsFor<T> spec) => spec.All.Validate(value);

	public static MonitoredConstraintSet<TEvent> Attach<TEvent>(IObservable<TEvent> events, IReactiveConstraintsFor<TEvent> spec) =>
		spec.All.Attach(events);
}

// ── Examples ─────────────────────────────────────────────────────────────────

public class NestedConstraintsExamples
{
	[Test]
	public void InvalidLineReportsAViolationPositionedByIndex()
	{
		var order = new Order(Guid.NewGuid(), "a@b.com", [new NestedOrderLine("SKU-1", 0, 10m)], NestedOrderStatus.Pending, null);

		var violations = GenericDispatch.Validate(order, NestedOrderSpec.Instance);

		violations.Should().ContainSingle(v => v.ConstraintId == "EveryLineIsValid" && v.Message == "Line 0: Quantity must be positive.");
	}

	[Test]
	public void ConfirmedOrderPastDeadlineViolatesTheHandAssembledRule()
	{
		var clock = TimeProvider.System;
		var confirmedAt = clock.GetUtcNow() - TimeSpan.FromHours(49);
		var order = new Order(Guid.NewGuid(), "a@b.com", [new NestedOrderLine("SKU-1", 1, 10m)], NestedOrderStatus.Confirmed, confirmedAt);

		var violations = NestedOrderSpec.WithDeadline(clock).Validate(order);

		violations.Should().ContainSingle(v => v.ConstraintId == "shipsWithinDeadline");
	}

	[Test]
	public void ShippingAfterCancellationViolatesTheReactiveSpec()
	{
		var orderId = Guid.NewGuid();
		var subject = new Subject<NestedOrderEvent>();
		var monitored = GenericDispatch.Attach(subject, NestedOrderEventSpec.Instance);

		subject.OnNext(new NestedOrderEvent.Cancelled(orderId));
		subject.OnNext(new NestedOrderEvent.Shipped(orderId));

		monitored["NeverShippedAfterCancellation"].Current.IsViolated.Should().BeTrue();
	}
}
