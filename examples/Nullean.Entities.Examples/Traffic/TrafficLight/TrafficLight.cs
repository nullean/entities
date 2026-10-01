namespace Nullean.Entities.Examples.Traffic.TrafficLight;

public enum Signal
{
	Red,
	Green,
	Yellow
}

public sealed record TrafficLight(string IntersectionId, Signal Signal, TimeSpan Duration)
{
	// Standard cycle: Red → Green → Yellow → Red
	public TrafficLight Advance() =>
		this with
		{
			Signal = Signal switch
			{
				Signal.Red => Signal.Green,
				Signal.Green => Signal.Yellow,
				Signal.Yellow => Signal.Red,
				_ => throw new ArgumentOutOfRangeException()
			}
		};
}

[Constraints]
public static partial class TrafficLightConstraints
{
	public static readonly Constraint<TrafficLight> LegalTransitions = Constraints<TrafficLight>
		.Transitions(t => t.Signal)
		.Allow(Signal.Red, Signal.Green)
		.Allow(Signal.Green, Signal.Yellow)
		.Allow(Signal.Yellow, Signal.Red);

	public static readonly Constraint<TrafficLight> PositiveDuration = Constraints<TrafficLight>.Always(
		t => t.Duration > TimeSpan.Zero,
		"Signal duration must be positive."
	);

	public static readonly Constraint<TrafficLight> IntersectionIdNotEmpty = Constraints<TrafficLight>.Always(
		t => !string.IsNullOrWhiteSpace(t.IntersectionId),
		"Intersection ID must not be empty."
	);
}
