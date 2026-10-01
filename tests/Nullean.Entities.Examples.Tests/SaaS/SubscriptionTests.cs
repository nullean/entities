using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.SaaS.Subscription;

namespace Nullean.Entities.Examples.Tests.SaaS;

public class SubscriptionTests
{
	private static readonly ConstraintSet<Subscription> Constraints = SubscriptionConstraints.All;

	private static readonly BillingPeriod Period = new(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));

	private static Subscription Trial() => new(Guid.NewGuid(), "cust-1", Plan.Starter, SubscriptionStatus.Trial, new DateOnly(2024, 1, 15), null, 3);

	[Test]
	public void Validate_valid_trial_passes()
	{
		Constraints.Validate(Trial()).Should().BeEmpty();
	}

	[Test]
	public void Validate_free_plan_with_multiple_seats_fails()
	{
		var bad = Trial() with { Plan = Plan.Free, SeatCount = 5 };
		Constraints.Validate(bad).Should().NotBeEmpty();
	}

	[Test]
	public void Validate_active_without_billing_period_fails()
	{
		var bad = Trial() with { Status = SubscriptionStatus.Active, CurrentPeriod = null };
		Constraints.Validate(bad).Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Applied_on_activate()
	{
		var channel = ConstrainedValue.For(Trial()).Enforce(Constraints);
		var result = channel.ApplyDetailed(s => s.Activate(Period));
		result.Should().BeOfType<ApplyOutcome<Subscription>.Applied>();
	}

	[Test]
	public void Channel_Failure_on_illegal_transition_Active_to_Expired()
	{
		var active = Trial().Activate(Period);
		var channel = ConstrainedValue.For(active).Enforce(Constraints);
		var result = channel.ApplyDetailed(s => s.Expire());
		result.Should().BeOfType<ApplyOutcome<Subscription>.Failure>();
	}

	[Test]
	public void ShouldSatisfy_any_positive_seat_count_is_valid() =>
		Gen.Int[1, 100].Select(seats => Trial() with { SeatCount = seats }).ShouldSatisfy(Constraints);

	// AddSeats(n) takes any n; SeatCountPositive and FreePlanHasOneSeat reject a bad result at the Apply boundary.
	[Test]
	public void AutoTest_enforced_subscriptions_follow_the_lifecycle_and_keep_their_seat_rules() =>
		SubscriptionConstraints
			.Instance
			.Spec()
			.From(Trial(), Trial() with { Plan = Plan.Free, SeatCount = 1 })
			.Domain(1, 5)
			.Key(s => (s.Plan, s.Status, s.CurrentPeriod is null, Math.Min(s.SeatCount, 6)))
			.Enforced()
			.Exhaustive();
}
