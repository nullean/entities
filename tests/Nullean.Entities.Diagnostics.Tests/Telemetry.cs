using System.Diagnostics.Metrics;

namespace Nullean.Entities.Diagnostics.Tests;

/// <summary>A meter factory per test, so a listener only sees the measurements of the diagnostics under test, not of tests running alongside.</summary>
public sealed class TestMeterFactory : IMeterFactory
{
	private readonly List<Meter> _meters = [];

	public Meter Create(MeterOptions options)
	{
		options.Scope = this;
		var meter = new Meter(options);
		_meters.Add(meter);
		return meter;
	}

	public void Dispose()
	{
		foreach (var meter in _meters)
		{
			meter.Dispose();
		}
	}
}

public sealed record Measurement(string Meter, string Instrument, long Value, IReadOnlyDictionary<string, object?> Tags);

/// <summary>Records every long measurement of meters made by <paramref name="scope"/>.</summary>
public sealed class Measurements : IDisposable
{
	private readonly MeterListener _listener = new();
	private readonly List<Measurement> _recorded = [];

	public Measurements(object scope)
	{
		_listener.InstrumentPublished = (instrument, listener) =>
		{
			if (ReferenceEquals(instrument.Meter.Scope, scope))
			{
				listener.EnableMeasurementEvents(instrument);
			}
		};
		_listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
		{
			var dictionary = new Dictionary<string, object?>();
			foreach (var tag in tags)
			{
				dictionary[tag.Key] = tag.Value;
			}
			lock (_recorded)
			{
				_recorded.Add(new Measurement(instrument.Meter.Name, instrument.Name, value, dictionary));
			}
		});
		_listener.Start();
	}

	public IReadOnlyList<Measurement> Of(string instrument)
	{
		lock (_recorded)
		{
			return [.. _recorded.Where(m => m.Instrument == instrument)];
		}
	}

	public void Dispose() => _listener.Dispose();
}
