namespace Nullean.Entities.Examples.Basics.Temperature;

public sealed record Temperature(double Celsius);

[Constraints]
public static partial class TemperatureConstraints
{
	public static readonly Constraint<Temperature> AboveAbsoluteZero = Constraints<Temperature>.Always(
		t => t.Celsius >= -273.15,
		"Temperature cannot be below absolute zero (−273.15 °C)."
	);

	public static readonly Constraint<Temperature> MustBeFinite = Constraints<Temperature>.Always(
		t => double.IsFinite(t.Celsius),
		"Temperature must be a finite number."
	);
}
