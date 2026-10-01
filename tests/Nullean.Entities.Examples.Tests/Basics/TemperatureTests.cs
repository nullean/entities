using AwesomeAssertions;
using CsCheck;
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
		var channel = ConstraintChannel.For(new Temperature(20)).Enforce(Constraints);
		var result = channel.Apply(t => t with { Celsius = 100 });
		result.Should().BeOfType<ChannelResult<Temperature>.Applied>();
	}

	[Test]
	public void Channel_Noop_on_same_value()
	{
		var channel = ConstraintChannel.For(new Temperature(20)).Enforce(Constraints);
		var result = channel.Apply(t => t);
		result.Should().BeOfType<ChannelResult<Temperature>.Noop>();
	}

	[Test]
	public void Channel_Failure_on_invalid_mutation()
	{
		var channel = ConstraintChannel.For(new Temperature(20)).Enforce(Constraints);
		var result = channel.Apply(_ => new Temperature(-300));
		result.Should().BeOfType<ChannelResult<Temperature>.Failure>();
	}

	[Test]
	public void CsCheck_all_finite_positive_temperatures_are_valid()
	{
		Gen.Double[0.0, 1000.0].Sample(celsius =>
		{
			var violations = Constraints.Validate(new Temperature(celsius));
			violations.Should().BeEmpty();
		});
	}
}
