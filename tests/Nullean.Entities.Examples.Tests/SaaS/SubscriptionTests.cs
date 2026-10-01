using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Examples.SaaS.Subscription;

namespace Nullean.Entities.Examples.Tests.SaaS;

public class SubscriptionTests
{
	private static readonly ConstraintSet<Subscription> Constraints = SubscriptionConstraints.Constraints;

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
		var channel = ConstraintChannel.For(Trial()).Enforce(Constraints);
		var result = channel.Apply(s => s.Activate(Period));
		result.Should().BeOfType<ChannelResult<Subscription>.Applied>();
	}

	[Test]
	public void Channel_Failure_on_illegal_transition_Active_to_Expired()
	{
		var active = Trial().Activate(Period);
		var channel = ConstraintChannel.For(active).Enforce(Constraints);
		var result = channel.Apply(s => s.Expire());
		result.Should().BeOfType<ChannelResult<Subscription>.Failure>();
	}

	[Test]
	public void CsCheck_seat_count_is_always_positive()
	{
		Gen.Int[1, 100].Sample(seats =>
		{
			var sub = Trial() with { SeatCount = seats };
			Constraints.Validate(sub).Should().NotContain(v => v.Message.Contains("Seat count"));
		});
	}
}
