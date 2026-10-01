using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Examples.Basics.DateRange;

namespace Nullean.Entities.Examples.Tests.Basics;

public class DateRangeTests
{
	private static readonly ConstraintSet<DateRange> Constraints = DateRangeConstraints.All;

	private static readonly DateRange Valid = new(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

	[Test]
	public void Validate_valid_range()
	{
		Constraints.Validate(Valid).Should().BeEmpty();
	}

	[Test]
	public void Validate_end_before_start_fails()
	{
		var bad = new DateRange(new DateOnly(2024, 6, 1), new DateOnly(2024, 1, 1));
		Constraints.Validate(bad).Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Failure_when_end_moves_backward()
	{
		var channel = ConstraintChannel.For(Valid).Enforce(Constraints);
		var result = channel.Apply(r => r with { End = new DateOnly(2023, 1, 1) });
		result.Should().BeOfType<ChannelResult<DateRange>.Failure>();
	}

	[Test]
	public void Channel_Applied_when_end_moves_forward()
	{
		var channel = ConstraintChannel.For(Valid).Enforce(Constraints);
		var result = channel.Apply(r => r with { End = new DateOnly(2025, 12, 31) });
		result.Should().BeOfType<ChannelResult<DateRange>.Applied>();
	}
}
