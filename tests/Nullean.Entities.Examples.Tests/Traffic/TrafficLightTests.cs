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
		var channel = ConstraintChannel.For(Red()).Enforce(Constraints);
		var result = channel.Apply(t => t.Advance());
		result.Should().BeOfType<ChannelResult<TrafficLight>.Applied>();
	}

	[Test]
	public void Channel_Failure_on_illegal_Red_to_Yellow()
	{
		var channel = ConstraintChannel.For(Red()).Enforce(Constraints);
		var result = channel.Apply(t => t with { Signal = Signal.Yellow });
		result.Should().BeOfType<ChannelResult<TrafficLight>.Failure>();
	}

	[Test]
	public void Full_cycle_Red_Green_Yellow_Red_all_Applied()
	{
		var channel = ConstraintChannel.For(Red()).Enforce(Constraints);

		channel.Apply(t => t.Advance()).Should().BeOfType<ChannelResult<TrafficLight>.Applied>(); // Green
		channel.Apply(t => t.Advance()).Should().BeOfType<ChannelResult<TrafficLight>.Applied>(); // Yellow
		channel.Apply(t => t.Advance()).Should().BeOfType<ChannelResult<TrafficLight>.Applied>(); // Red again
	}
}
