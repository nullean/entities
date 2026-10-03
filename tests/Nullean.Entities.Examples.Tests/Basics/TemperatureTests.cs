using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.Basics.Temperature;

namespace Nullean.Entities.Examples.Tests.Basics;

public class TemperatureTests
{
	private static readonly ConstraintSet<Temperature> Constraints = TemperatureConstraints.All;

	[Test]
	public void Validate_valid_temperature()
	{
		Constraints.Validate(new Temperature(20)).Should().BeEmpty();
	}

	[Test]
	public void Validate_rejects_below_absolute_zero()
	{
		var violations = Constraints.Validate(new Temperature(-274));
		violations.Should().NotBeEmpty();
	}

	[Test]
	public void Validate_rejects_NaN()
	{
		var violations = Constraints.Validate(new Temperature(double.NaN));
		violations.Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Applied_on_valid_mutation()
	{
		var channel = ConstrainedValue.For(new Temperature(20)).Enforce(Constraints);
		var result = channel.ApplyDetailed(t => t with { Celsius = 100 });
		result.Should().BeOfType<ApplyOutcome<Temperature>.Applied>();
	}

	[Test]
	public void Channel_Noop_on_same_value()
	{
		var channel = ConstrainedValue.For(new Temperature(20)).Enforce(Constraints);
		var result = channel.ApplyDetailed(t => t);
		result.Should().BeOfType<ApplyOutcome<Temperature>.Noop>();
	}

	[Test]
	public void Channel_Failure_on_invalid_mutation()
	{
		var channel = ConstrainedValue.For(new Temperature(20)).Enforce(Constraints);
		var result = channel.ApplyDetailed(_ => new Temperature(-300));
		result.Should().BeOfType<ApplyOutcome<Temperature>.Failure>();
	}

	[Test]
	public void ShouldSatisfy_everyday_temperatures_are_valid() =>
		Gen.Double[0.0, 1000.0].Select(c => new Temperature(c)).ShouldSatisfy(Constraints, iter: 200);

	// No hand-made generator: the default one reaches NaN, infinity and sub-absolute-zero values on its own.
	[Test]
	public void RejectsInvalid_nan_infinity_and_below_absolute_zero_are_named_and_rejected() => Constraints.RejectsInvalid().Unfalsifiable.Should().BeEmpty();
}
