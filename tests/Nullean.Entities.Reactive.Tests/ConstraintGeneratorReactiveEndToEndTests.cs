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
	[Constraint]
	public static readonly ReactiveConstraint<GeneratedOrderEvent> NeverShippedBeforeConfirmed = Constraints<GeneratedOrderEvent>.Never<GeneratedOrderEvent.Placed, GeneratedOrderEvent.Shipped>(
		(p, s) => p.OrderId == s.OrderId
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
		GeneratedOrderEventSpec.Constraints.Should().BeOfType<ReactiveConstraintSet<GeneratedOrderEvent>>();
	}

	[Test]
	public void Attaching_the_generated_set_names_each_constraint_from_the_field()
	{
		var subject = new Subject<GeneratedOrderEvent>();
		var monitored = GeneratedOrderEventSpec.Constraints.Attach(subject);

		monitored["NeverShippedBeforeConfirmed"].Current.IsPending.Should().BeTrue();
		monitored["TotalAlwaysPositive"].Current.IsPending.Should().BeTrue();
	}

	[Test]
	public void Attaching_the_generated_set_detects_a_real_violation()
	{
		var orderId = Guid.NewGuid();
		var subject = new Subject<GeneratedOrderEvent>();
		var monitored = GeneratedOrderEventSpec.Constraints.Attach(subject);

		subject.OnNext(new GeneratedOrderEvent.Placed(orderId));
		subject.OnNext(new GeneratedOrderEvent.Shipped(orderId));

		monitored["NeverShippedBeforeConfirmed"].Current.IsViolated.Should().BeTrue();
	}
}
