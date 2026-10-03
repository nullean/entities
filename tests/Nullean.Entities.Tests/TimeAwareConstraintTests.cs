using AwesomeAssertions;

namespace Nullean.Entities.Tests;

internal sealed record Reading(DateTimeOffset At, int Value);

public class TimeAwareConstraintTests
{
	private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	private static readonly Constraint<Reading> NotFromTheFuture = Constraints<Reading>.NotInFuture(r => r.At);
	private static readonly Constraint<Reading> Fresh = Constraints<Reading>.NotOlderThan(r => r.At, TimeSpan.FromMinutes(5));
	private static readonly Constraint<Reading> BusinessHours = Constraints<Reading>.Always((_, now) => now.Hour is >= 9 and < 17, "Outside business hours.");

	private static readonly ConstraintSet<Reading> All = ConstraintSet.From(
		(nameof(NotFromTheFuture), NotFromTheFuture),
		(nameof(Fresh), Fresh),
		(nameof(BusinessHours), BusinessHours)
	);

	[Test]
	public void NotInFuture_is_judged_against_the_supplied_clock()
	{
		var time = new ManualTimeProvider(Start);
		var reading = new Reading(Start.AddMinutes(1), 1);

		All.Validate(reading, time).Should().Contain(v => v.ConstraintId == nameof(NotFromTheFuture));

		time.Advance(TimeSpan.FromMinutes(2));
		All.Validate(reading, time).Should().NotContain(v => v.ConstraintId == nameof(NotFromTheFuture));
	}

	[Test]
	public void NotOlderThan_flips_as_the_clock_advances_without_touching_the_value()
	{
		var time = new ManualTimeProvider(Start);
		var reading = new Reading(Start, 1);

		All.Validate(reading, time).Should().NotContain(v => v.ConstraintId == nameof(Fresh));

		time.Advance(TimeSpan.FromMinutes(6));
		All.Validate(reading, time).Should().Contain(v => v.ConstraintId == nameof(Fresh));
	}

	[Test]
	public void Always_with_now_receives_the_provider_time()
	{
		var evening = new ManualTimeProvider(Start.AddHours(8));
		All.Validate(new Reading(Start, 1), evening).Should().Contain(v => v.ConstraintId == nameof(BusinessHours));
		All.Validate(new Reading(Start, 1), new ManualTimeProvider(Start)).Should().NotContain(v => v.ConstraintId == nameof(BusinessHours));
	}

	[Test]
	public void ConstrainedValue_uses_the_provider_it_was_enforced_with()
	{
		var time = new ManualTimeProvider(Start);
		var value = ConstrainedValue.For(new Reading(Start, 1)).Enforce(All, time);

		value.Apply(r => r with { Value = 2 }).IsOk.Should().BeTrue();

		time.Advance(TimeSpan.FromMinutes(10));
		value.Apply(r => r with { Value = 3 }).IsFail.Should().BeTrue();
	}

	[Test]
	public void Check_without_a_provider_uses_the_system_clock()
	{
		var future = new Reading(DateTimeOffset.UtcNow.AddDays(1), 1);
		NotFromTheFuture.Check(future, future).Should().NotBeEmpty();
	}
}
