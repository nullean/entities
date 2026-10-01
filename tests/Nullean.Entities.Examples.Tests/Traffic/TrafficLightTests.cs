using AwesomeAssertions;
using Nullean.Entities.Examples.Traffic.TrafficLight;

namespace Nullean.Entities.Examples.Tests.Traffic;

public class TrafficLightTests
{
	private static readonly ConstraintSet<TrafficLight> Constraints = TrafficLightConstraints.All;

	private static TrafficLight Red() => new("Main/1st", Signal.Red, TimeSpan.FromSeconds(30));

	[Test]
	public void Validate_valid_red_light_passes()
	{
		Constraints.Validate(Red()).Should().BeEmpty();
	}

	[Test]
	public void Validate_rejects_zero_duration()
	{
		var bad = Red() with { Duration = TimeSpan.Zero };
		Constraints.Validate(bad).Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Applied_on_legal_Red_to_Green()
	{
		var channel = ConstrainedValue.For(Red()).Enforce(Constraints);
		var result = channel.ApplyDetailed(t => t.Advance());
		result.Should().BeOfType<ApplyOutcome<TrafficLight>.Applied>();
	}

	[Test]
	public void Channel_Failure_on_illegal_Red_to_Yellow()
	{
		var channel = ConstrainedValue.For(Red()).Enforce(Constraints);
		var result = channel.ApplyDetailed(t => t with { Signal = Signal.Yellow });
		result.Should().BeOfType<ApplyOutcome<TrafficLight>.Failure>();
	}

	[Test]
	public void Full_cycle_Red_Green_Yellow_Red_all_Applied()
	{
		var channel = ConstrainedValue.For(Red()).Enforce(Constraints);

		channel.ApplyDetailed(t => t.Advance()).Should().BeOfType<ApplyOutcome<TrafficLight>.Applied>(); // Green
		channel.ApplyDetailed(t => t.Advance()).Should().BeOfType<ApplyOutcome<TrafficLight>.Applied>(); // Yellow
		channel.ApplyDetailed(t => t.Advance()).Should().BeOfType<ApplyOutcome<TrafficLight>.Applied>(); // Red again
	}
}
