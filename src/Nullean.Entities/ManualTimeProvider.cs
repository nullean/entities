// Nullean.Entities — a TimeProvider whose clock only moves when told to.
//
// Time-aware constraints, TemporalMonitor deadlines and anything else that takes a TimeProvider can be tested with it:
// Advance(TimeSpan) moves the clock and fires every timer that falls due, in order, each seeing the clock at its own due
// time. Small on purpose; Microsoft.Extensions.TimeProvider.Testing's FakeTimeProvider does the same and works too,
// this one just avoids the extra package.

namespace Nullean.Entities;

public sealed class ManualTimeProvider(DateTimeOffset? start = null) : TimeProvider
{
	private readonly Lock _gate = new();
	private readonly List<ManualTimer> _timers = [];
	private DateTimeOffset _now = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	public override DateTimeOffset GetUtcNow()
	{
		lock (_gate)
		{
			return _now;
		}
	}

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		var timer = new ManualTimer(this, callback, state);
		timer.Change(dueTime, period);
		return timer;
	}

	/// <summary>Moves the clock forward by <paramref name="by"/>, firing every timer that falls due on the way.</summary>
	public void Advance(TimeSpan by)
	{
		if (by < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(by), "Time only moves forward.");
		}
		DateTimeOffset target;
		lock (_gate)
		{
			target = _now + by;
		}
		while (true)
		{
			ManualTimer? next;
			lock (_gate)
			{
				next = _timers.Where(t => t.Due is not null && t.Due <= target).OrderBy(t => t.Due).ThenBy(t => t.Sequence).FirstOrDefault();
				if (next is null)
				{
					_now = target;
					return;
				}
				_now = next.Due!.Value > _now ? next.Due.Value : _now;
				next.Fired();
			}
			// callbacks run outside the lock: they may create timers, change them, or read the clock
			next.Invoke();
		}
	}

	private void Register(ManualTimer timer)
	{
		lock (_gate)
		{
			if (!_timers.Contains(timer))
			{
				_timers.Add(timer);
			}
		}
	}

	private void Unregister(ManualTimer timer)
	{
		lock (_gate)
		{
			_timers.Remove(timer);
		}
	}

	private DateTimeOffset NowLocked()
	{
		lock (_gate)
		{
			return _now;
		}
	}

	private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
	{
		private static long SequenceCounter;
		private TimeSpan _period = Timeout.InfiniteTimeSpan;

		public DateTimeOffset? Due { get; private set; }

		public long Sequence { get; private set; }

		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			_period = period;
			if (dueTime == Timeout.InfiniteTimeSpan)
			{
				Due = null;
				owner.Unregister(this);
				return true;
			}
			Due = owner.NowLocked() + dueTime;
			Sequence = Interlocked.Increment(ref SequenceCounter);
			owner.Register(this);
			return true;
		}

		// Called with the provider's lock held, just before the callback runs.
		public void Fired()
		{
			if (_period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero)
			{
				Due = null;
				owner.Unregister(this);
			}
			else
			{
				Due += _period;
				Sequence = Interlocked.Increment(ref SequenceCounter);
			}
		}

		public void Invoke() => callback(state);

		public void Dispose() => owner.Unregister(this);

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
