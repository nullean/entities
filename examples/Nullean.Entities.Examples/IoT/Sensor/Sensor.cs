namespace Nullean.Entities.Examples.IoT.Sensor;

public enum SensorStatus
{
	Offline,
	Online,
	Degraded,
	Maintenance
}

public enum AlertSeverity
{
	Info,
	Warning,
	Critical
}

public sealed record SensorReading(double Value, string Unit, DateTimeOffset RecordedAt);

public sealed record SensorAlert(AlertSeverity Severity, string Message, DateTimeOffset RaisedAt);

public sealed record Sensor(
	string DeviceId,
	string Location,
	SensorStatus Status,
	IReadOnlyList<SensorReading> RecentReadings,
	IReadOnlyList<SensorAlert> ActiveAlerts,
	double? MinThreshold,
	double? MaxThreshold
)
{
	public Sensor AddReading(SensorReading r) => this with { RecentReadings = [.. RecentReadings, r] };

	public Sensor RaiseAlert(SensorAlert a) => this with { ActiveAlerts = [.. ActiveAlerts, a] };

	public Sensor ClearAlerts() => this with { ActiveAlerts = [] };

	public Sensor GoOnline() => this with { Status = SensorStatus.Online };

	public Sensor GoOffline() => this with { Status = SensorStatus.Offline };

	public Sensor Degrade() => this with { Status = SensorStatus.Degraded };
}

[Constraints]
public static partial class SensorConstraints
{
	public static readonly Constraint<Sensor> ThresholdsConsistent = Constraints<Sensor>.Check(sensor =>
	{
		if (sensor.MinThreshold is double min && sensor.MaxThreshold is double max && min >= max)
		{
			return [Violation.Error($"MinThreshold ({min}) must be less than MaxThreshold ({max}).")];
		}
		return [];
	});

	public static readonly Constraint<Sensor> ReadingsWithinThresholds = Constraints<Sensor>.Check(
		sensor =>
			[
				.. sensor
					.RecentReadings
					.Where(r => (sensor.MinThreshold is double min && r.Value < min) || (sensor.MaxThreshold is double max && r.Value > max))
					.Select(r => Violation.Warning($"Reading {r.Value} {r.Unit} is outside configured thresholds."))
			]
	);

	public static readonly Constraint<Sensor> DeviceIdNotEmpty = Constraints<Sensor>.Always(
		s => !string.IsNullOrWhiteSpace(s.DeviceId),
		"Device ID must not be empty."
	);
}
