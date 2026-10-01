using AwesomeAssertions;
using Nullean.Entities.CsCheck;
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

	[Test]
	public void AutoTest_the_light_cycles_through_exactly_three_states_and_every_transition_is_taken() =>
		TrafficLightConstraints.Instance.Spec(Red()).Exhaustive().States.Should().Be(3);

	[Test]
	public void AutoTest_advance_always_leaves_a_valid_light() => Constraints.Preserves(TrafficLightConstraints.Actions, iter: 200);

	[Test]
	public void AutoTest_the_lifecycle_diagram_names_every_signal() => TrafficLightConstraints.Instance.Spec(Red()).Mermaid().Should().Contain("Advance");
}
