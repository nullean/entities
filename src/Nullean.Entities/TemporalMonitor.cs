// Nullean.Entities — running temporal rules against a live stream, on a TimeProvider.
//
// TemporalMonitor<TEvent> stamps each event with the provider's clock, feeds every rule's monitor, and keeps one timer
// set to the earliest open deadline. When that timer fires with no event, the deadline is reached and the violation
// is reported at the deadline's own instant. With a ManualTimeProvider, Advance(...) fires it deterministically.

using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Nullean.Entities;

/// <summary>A rule's verdict changed (to <see cref="Verdict.Holds"/> or <see cref="Verdict.Violated"/>).</summary>
public readonly record struct VerdictChange(string Rule, Verdict Verdict, DateTimeOffset At);

public sealed class TemporalMonitor<TEvent> : IDisposable
{
	private readonly Lock _gate = new();
	private readonly (string Name, IMonitor<TEvent> Monitor)[] _monitors;
	private readonly Verdict[] _published;
	private readonly TimeProvider _time;
	private readonly Channel<VerdictChange> _changes = Channel.CreateUnbounded<VerdictChange>();
	private ITimer? _timer;
	private bool _completed;

	public TemporalMonitor(TemporalRuleSet<TEvent> rules, TimeProvider? time = null)
	{
		_time = time ?? TimeProvider.System;
		_monitors = [.. rules.Rules.Select(r => (r.Name, r.Rule.CreateMonitor()))];
		_published = [.. _monitors.Select(_ => Verdict.Pending)];
	}

	/// <summary>Each rule and where it stands right now.</summary>
	public IReadOnlyList<(string Name, Verdict Verdict)> Current
	{
		get
		{
			lock (_gate)
			{
				return [.. _monitors.Select(m => (m.Name, m.Monitor.Current))];
			}
		}
	}

	public bool AnyViolated => Current.Any(c => c.Verdict.IsViolated);

	/// <summary>Verdict changes in order. Completes when <see cref="Complete"/> or <see cref="Fail"/> is called.</summary>
	public IAsyncEnumerable<VerdictChange> Changes(CancellationToken cancellationToken = default) => _changes.Reader.ReadAllAsync(cancellationToken);

	/// <summary>Feeds an event, stamped with the provider's current time.</summary>
	public void Observe(TEvent value)
	{
		lock (_gate)
		{
			if (_completed)
			{
				return;
			}
			var now = _time.GetUtcNow();
			foreach (var (_, monitor) in _monitors)
			{
				monitor.Observe(value, now);
			}
			PublishAndReschedule(now);
		}
	}

	/// <summary>The stream ended: reach every deadline, then decide liveness rules (<c>Eventually</c>, <c>Required</c>).</summary>
	public void Complete()
	{
		lock (_gate)
		{
			if (_completed)
			{
				return;
			}
			var now = _time.GetUtcNow();
			foreach (var (_, monitor) in _monitors)
			{
				monitor.Tick(now);
				monitor.Finish(now);
			}
			Publish(now);
			_completed = true;
			_timer?.Dispose();
			_timer = null;
			_changes.Writer.TryComplete();
		}
	}

	/// <summary>The source of events failed: completes <see cref="Changes"/> with that exception.</summary>
	public void Fail(Exception error)
	{
		lock (_gate)
		{
			_completed = true;
			_timer?.Dispose();
			_timer = null;
			_changes.Writer.TryComplete(error);
		}
	}

	private void OnTimer()
	{
		lock (_gate)
		{
			if (_completed)
			{
				return;
			}
			var now = _time.GetUtcNow();
			foreach (var (_, monitor) in _monitors)
			{
				monitor.Tick(now);
			}
			PublishAndReschedule(now);
		}
	}

	private void PublishAndReschedule(DateTimeOffset now)
	{
		Publish(now);
		var next = _monitors.Where(m => !m.Monitor.Current.IsViolated).Select(m => m.Monitor.NextDeadline).Where(d => d is not null).Min();
		_timer?.Dispose();
		_timer = null;
		if (next is { } deadline)
		{
			var due = deadline - now;
			_timer = _time.CreateTimer(_ => OnTimer(), null, due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
		}
	}

	private void Publish(DateTimeOffset now)
	{
		for (var i = 0; i < _monitors.Length; i++)
		{
			var verdict = _monitors[i].Monitor.Current;
			if (!verdict.Equals(_published[i]))
			{
				_published[i] = verdict;
				_changes.Writer.TryWrite(new VerdictChange(_monitors[i].Name, verdict, verdict.ViolationAt ?? now));
			}
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_completed = true;
			_timer?.Dispose();
			_timer = null;
			_changes.Writer.TryComplete();
		}
	}
}

public static class TemporalAsyncExtensions
{
	/// <summary>
	/// Monitors an asynchronous stream of events: each is stamped with <paramref name="time"/>'s clock when it arrives, deadlines run on
	/// its timers, and every change of verdict is yielded as it happens. The sequence ends when the events end, after liveness rules
	/// are decided. A fault in the source surfaces here.
	/// </summary>
	public static async IAsyncEnumerable<VerdictChange> MonitorAsync<TEvent>(
		this IAsyncEnumerable<TEvent> events,
		TemporalRuleSet<TEvent> rules,
		TimeProvider? time = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
	{
		using var monitor = new TemporalMonitor<TEvent>(rules, time);
		var pump = Task.Run(
			async () =>
			{
				try
				{
					await foreach (var e in events.WithCancellation(cancellationToken).ConfigureAwait(false))
					{
						monitor.Observe(e);
					}
					monitor.Complete();
				}
				catch (Exception error)
				{
					monitor.Fail(error);
				}
			},
			CancellationToken.None
		);

		await foreach (var change in monitor.Changes(cancellationToken).ConfigureAwait(false))
		{
			yield return change;
		}
		await pump.ConfigureAwait(false);
	}
}
