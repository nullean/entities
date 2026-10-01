using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Examples.IoT.Sensor;

namespace Nullean.Entities.Examples.Tests.IoT;

public class SensorTests
{
	private static readonly ConstraintSet<Sensor> Constraints = SensorConstraints.All;

	private static Sensor Online() => new("dev-001", "Warehouse A", SensorStatus.Online, [], [], 0.0, 100.0);

	[Test]
	public void Validate_online_sensor_with_no_readings_passes()
	{
		Constraints.Validate(Online()).Should().BeEmpty();
	}

	[Test]
	public void Validate_rejects_min_threshold_above_max()
	{
		var bad = Online() with { MinThreshold = 200.0, MaxThreshold = 100.0 };
		Constraints.Validate(bad).Should().NotBeEmpty();
	}

	[Test]
	public void Validate_rejects_empty_device_id()
	{
		var bad = Online() with { DeviceId = "" };
		Constraints.Validate(bad).Should().NotBeEmpty();
	}

	[Test]
	public void Validate_reading_out_of_range_produces_warning()
	{
		var sensor = Online().AddReading(new SensorReading(150.0, "°C", DateTimeOffset.UtcNow));
		var violations = Constraints.Validate(sensor);
		violations.Should().ContainSingle(v => v.Severity == Severity.Warning);
	}

	[Test]
	public void Channel_Applied_on_add_reading_within_range()
	{
		var channel = ConstrainedValue.For(Online()).Enforce(Constraints);
		var result = channel.ApplyDetailed(s => s.AddReading(new SensorReading(50.0, "°C", DateTimeOffset.UtcNow)));
		result.Should().BeOfType<ApplyOutcome<Sensor>.Applied>();
	}

	[Test]
	public void CsCheck_readings_within_thresholds_never_warn()
	{
		Gen.Double[0.0, 100.0].Sample(value =>
		{
			var sensor = Online().AddReading(new SensorReading(value, "°C", DateTimeOffset.UtcNow));
			var violations = Constraints.Validate(sensor);
			violations.Should().NotContain(v => v.Severity == Severity.Warning);
		});
	}
}
