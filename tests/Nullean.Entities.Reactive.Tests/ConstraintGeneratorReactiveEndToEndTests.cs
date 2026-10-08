// Real end-to-end coverage for [Constraint] on ReactiveConstraint<TEvent> members: compiled by the
// actual generator (wired in as an analyzer on this test project too — see
// Nullean.Entities.Reactive.Tests.csproj), proving the companion-class + ReactiveConstraintSet
// shape works at runtime, not just that the generator emits the expected source text.

using System.Reactive.Subjects;
using AwesomeAssertions;

namespace Nullean.Entities.Reactive.Tests;

internal abstract record GeneratedOrderEvent
{
	public sealed record Placed(Guid OrderId) : GeneratedOrderEvent;

	public sealed record Confirmed(Guid OrderId) : GeneratedOrderEvent;

	public sealed record Shipped(Guid OrderId) : GeneratedOrderEvent;
}

internal static partial class GeneratedOrderEventSpec
{
	// Transitions, not Never<Placed,Shipped>: this domain has a real Confirmed case sitting between
	// them, and Never<T1,T2> filters every other event type out before checking adjacency — see the
	// caveat on Constraints<TEvent>.Never for the false positive that produces against the ordinary
	// Placed -> Confirmed -> Shipped path.
	[Constraint]
	public static readonly ReactiveConstraint<GeneratedOrderEvent> NeverShippedBeforeConfirmed = Constraints<GeneratedOrderEvent>.Transitions(
		(prev, next) => !(prev is GeneratedOrderEvent.Placed p && next is GeneratedOrderEvent.Shipped s && p.OrderId == s.OrderId)
	);

	[Constraint]
	public static readonly ReactiveConstraint<GeneratedOrderEvent> TotalAlwaysPositive = Constraints<GeneratedOrderEvent>.Always<GeneratedOrderEvent.Confirmed>(
		_ => true
	);
}

public class ReactiveCompanionClassConstraintsTests
{
	[Test]
	public void Generated_Constraints_is_a_ReactiveConstraintSet()
	{
		GeneratedOrderEventSpec.All.Should().BeOfType<ReactiveConstraintSet<GeneratedOrderEvent>>();
	}

	[Test]
	public void Attaching_the_generated_set_names_each_constraint_from_the_field()
	{
		var subject = new Subject<GeneratedOrderEvent>();
		var monitored = GeneratedOrderEventSpec.All.Attach(subject);

		monitored["never-shipped-before-confirmed"].Current.IsPending.Should().BeTrue();
		monitored["total-always-positive"].Current.IsPending.Should().BeTrue();
	}

	[Test]
	public void Attaching_the_generated_set_detects_a_real_violation()
	{
		var orderId = Guid.NewGuid();
		var subject = new Subject<GeneratedOrderEvent>();
		var monitored = GeneratedOrderEventSpec.All.Attach(subject);

		subject.OnNext(new GeneratedOrderEvent.Placed(orderId));
		subject.OnNext(new GeneratedOrderEvent.Shipped(orderId));

		monitored["never-shipped-before-confirmed"].Current.IsViolated.Should().BeTrue();
	}

	[Test]
	public void IReactiveConstraintsFor_interface_is_actually_implemented_via_the_nested_TypeConstraints()
	{
		// Same reasoning as the sync IConstraintsFor<T> end-to-end test: a plain instance interface,
		// so a caller passes the value and TEvent is inferred from it.
		static ReactiveConstraintSet<T> ConstraintsFor<T>(IReactiveConstraintsFor<T> spec) => spec.All;

		ConstraintsFor(GeneratedOrderEventSpec.TypeConstraints.Instance).Should().BeSameAs(GeneratedOrderEventSpec.All);
	}

	[Test]
	public void Instance_is_a_forwarding_shortcut_for_TypeConstraints_Instance()
	{
		// GeneratedOrderEventSpec.Instance is the generated top-level shortcut — callers never have
		// to spell out "TypeConstraints" to get the IReactiveConstraintsFor<OrderEvent> value.
		GeneratedOrderEventSpec.Instance.Should().BeEquivalentTo(GeneratedOrderEventSpec.TypeConstraints.Instance);
		GeneratedOrderEventSpec.Instance.All.Should().BeSameAs(GeneratedOrderEventSpec.All);
	}
}
