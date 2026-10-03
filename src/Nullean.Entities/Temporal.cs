// Nullean.Entities — temporal rules over a sequence of events, with no Rx and no dependencies.
//
// A TemporalRule<TEvent> is a recipe for a small pure state machine (IMonitor<TEvent>) that is fed
// timestamped events and reports a Verdict: Pending, Holds, or Violated(at, evidence). The same machine
// serves three callers:
//
//   Trace.Check(...)                  offline: a recorded session or audit log, timestamps in the events
//   TemporalMonitor<TEvent> / MonitorAsync   online: events stamped by a TimeProvider, deadlines on its timers,
//                                     so ManualTimeProvider.Advance fires a missed deadline deterministically
//   Nullean.Entities.CsCheck's Script  generated runs on virtual time
//
// Nullean.Entities.Reactive expresses the same operators over IObservable and keeps doing so; a differential
// test in the Reactive test project feeds both the same traces and requires identical verdicts.
//
// Two ordering rules that both implementations share:
//   - a deadline is reached BEFORE an event at the same instant is considered (a timer fires before the event
//     delivered at that time), so an event arriving exactly at the deadline is too late;
//   - an event never satisfies the obligation it opens: a Confirmed that is also a trigger does not answer itself.

namespace Nullean.Entities;

// ── Verdict ───────────────────────────────────────────────────────────────────

public abstract record Verdict
{
	public static readonly Verdict Pending = new PendingVerdict();
	public static readonly Verdict Holds = new HoldsVerdict();

	public static Verdict Violated(DateTimeOffset at, object evidence) => new ViolatedVerdict(at, evidence);

	private sealed record PendingVerdict : Verdict;

	private sealed record HoldsVerdict : Verdict;

	private sealed record ViolatedVerdict(DateTimeOffset At, object Evidence) : Verdict;

	public bool IsViolated => this is ViolatedVerdict;
	public bool IsHolds => this is HoldsVerdict;
	public bool IsPending => this is PendingVerdict;
	public object? ViolationEvidence => (this as ViolatedVerdict)?.Evidence;
	public DateTimeOffset? ViolationAt => (this as ViolatedVerdict)?.At;
}

/// <summary>An event with the instant it happened.</summary>
public readonly record struct Timestamped<TEvent>(DateTimeOffset At, TEvent Event);

// ── IMonitor<TEvent> ──────────────────────────────────────────────────────────

/// <summary>
/// The running state of one rule. Pure and single-threaded: feed it events in time order and read <see cref="Current"/>.
/// A violation is sticky; <see cref="Verdict.Holds"/> is only ever reported at <see cref="Finish"/>.
/// </summary>
public interface IMonitor<TEvent>
{
	Verdict Current { get; }

	/// <summary>The earliest instant a pending obligation expires, or null when none is pending. A driver sets a timer for it.</summary>
	DateTimeOffset? NextDeadline { get; }

	/// <summary>Reaches every deadline at or before <paramref name="at"/>, then considers <paramref name="value"/>.</summary>
	Verdict Observe(TEvent value, DateTimeOffset at);

	/// <summary>Reaches every deadline at or before <paramref name="now"/> with no event.</summary>
	Verdict Tick(DateTimeOffset now);

	/// <summary>The stream has ended: unresolved liveness obligations become violations; a satisfied one reports <see cref="Verdict.Holds"/>.</summary>
	Verdict Finish(DateTimeOffset at);
}

internal abstract class MonitorBase<TEvent> : IMonitor<TEvent>
{
	public Verdict Current { get; private set; } = Verdict.Pending;

	public virtual DateTimeOffset? NextDeadline => null;

	public Verdict Observe(TEvent value, DateTimeOffset at)
	{
		if (Current.IsViolated)
		{
			return Current;
		}
		Tick(at);
		if (!Current.IsViolated)
		{
			OnEvent(value, at);
		}
		return Current;
	}

	public virtual Verdict Tick(DateTimeOffset now) => Current;

	public virtual Verdict Finish(DateTimeOffset at) => Current;

	protected void Violate(DateTimeOffset at, object evidence)
	{
		if (!Current.IsViolated)
		{
			Current = Verdict.Violated(at, evidence);
		}
	}

	protected void Hold()
	{
		if (Current.IsPending)
		{
			Current = Verdict.Holds;
		}
	}

	protected abstract void OnEvent(TEvent @event, DateTimeOffset at);
}

// ── TemporalRule / TemporalRuleSet ───────────────────────────────────────────

/// <summary>An immutable descriptor of a temporal rule. Lives anywhere: a static field, a local, a parameter.</summary>
public sealed class TemporalRule<TEvent>
{
	private readonly Func<IMonitor<TEvent>> _create;

	internal TemporalRule(Func<IMonitor<TEvent>> create) => _create = create;

	public string? Name { get; private set; }

	public TemporalRule<TEvent> Named(string name)
	{
		Name = name;
		return this;
	}

	/// <summary>A fresh monitor with no history.</summary>
	public IMonitor<TEvent> CreateMonitor() => _create();
}

/// <summary>A named, ordered collection of temporal rules.</summary>
public sealed class TemporalRuleSet<TEvent>
{
	internal TemporalRuleSet(IReadOnlyList<(string Name, TemporalRule<TEvent> Rule)> rules) => Rules = rules;

	public IReadOnlyList<(string Name, TemporalRule<TEvent> Rule)> Rules { get; }

#pragma warning disable CA1000
	public static TemporalRuleSet<TEvent> From(params (string Name, TemporalRule<TEvent> Rule)[] rules) => new(rules);
#pragma warning restore CA1000
}

// ── Temporal<TEvent>: the builder ────────────────────────────────────────────

#pragma warning disable CA1000
public static class Temporal<TEvent>
{
	// Every operator mirrors Nullean.Entities.Reactive's Constraints<TEvent> of the same name, with the same semantics.

	/// <summary>
	/// Violated when a T1 immediately followed by a T2 (consecutive among T1/T2 events; every other event is ignored)
	/// satisfies <paramref name="pair"/>. See Reactive's <c>Never</c> for why <see cref="Transitions(Func{TEvent, TEvent, bool})"/> is
	/// the right choice when an intermediate event can legitimately sit between the two.
	/// </summary>
	public static TemporalRule<TEvent> Never<T1, T2>(Func<T1, T2, bool> pair) where T1 : TEvent where T2 : TEvent =>
		Never(e => e is T1, e => e is T2, (a, b) => pair((T1)(object)a!, (T2)(object)b!));

	/// <summary>Predicate-based counterpart to <see cref="Never{T1, T2}"/>, for event shapes that cannot satisfy <c>where T : TEvent</c> (C# unions).</summary>
	public static TemporalRule<TEvent> Never(Func<TEvent, bool> isT1, Func<TEvent, bool> isT2, Func<TEvent, TEvent, bool> pair) =>
		new(() => new NeverMonitor(isT1, isT2, pair));

	/// <summary>Violated when a second event of type T carries the same <paramref name="key"/> as an earlier one, adjacent or not.</summary>
	public static TemporalRule<TEvent> NeverTwice<T>(Func<T, object> key) where T : TEvent => NeverTwice(e => e is T, e => key((T)(object)e!));

	public static TemporalRule<TEvent> NeverTwice(Func<TEvent, bool> isRelevant, Func<TEvent, object> key) => new(() => new NeverTwiceMonitor(isRelevant, key));

	/// <summary>Violated if <paramref name="predicate"/> is false for any event of type T.</summary>
	public static TemporalRule<TEvent> Always<T>(Func<T, bool> predicate) where T : TEvent => Always(e => e is T, e => predicate((T)(object)e!));

	public static TemporalRule<TEvent> Always(Func<TEvent, bool> isRelevant, Func<TEvent, bool> predicate) =>
		new(() => new AlwaysMonitor(isRelevant, predicate));

	/// <summary>Violated by the first pair of consecutive events (in the raw stream) for which <paramref name="isValid"/> is false.</summary>
	public static TemporalRule<TEvent> Transitions(Func<TEvent, TEvent, bool> isValid) => new(() => new TransitionsMonitor(isValid));

	/// <summary>An allow-list of consecutive event types; any other adjacent pair is a violation.</summary>
	public static TemporalRule<TEvent> Transitions(params (Type From, Type To)[] allowed) =>
		Transitions((a, b) => allowed.Any(x => x.From.IsAssignableFrom(a!.GetType()) && x.To.IsAssignableFrom(b!.GetType())));

	/// <summary>Violated the first time an event satisfies <paramref name="guard"/> but fails the requirement.</summary>
	public static WhenRule<TEvent> When(Func<TEvent, bool> guard) => new(guard);

	/// <summary>
	/// Unbounded liveness: every instance opened by <paramref name="from"/> must eventually see a T event with the same
	/// <paramref name="scope"/> key. Only resolvable at <see cref="IMonitor{TEvent}.Finish"/>, so for a bounded run.
	/// </summary>
	public static TemporalRule<TEvent> Eventually<T>(Func<TEvent, object> scope, Func<TEvent, bool> from, string? message = null) where T : TEvent =>
		new(() => new EventuallyMonitor(scope, from, e => e is T, message ?? $"instance(s) never reached {typeof(T).Name}"));

	public static TemporalRule<TEvent> Eventually(Func<TEvent, object> scope, Func<TEvent, bool> from, Func<TEvent, bool> isTarget, string? message = null) =>
		new(() => new EventuallyMonitor(scope, from, isTarget, message ?? "instance(s) never reached the target event"));

	/// <summary>
	/// Bounded response: after a trigger, <paramref name="isNext"/> must be satisfied within <paramref name="window"/>, unless
	/// <paramref name="isInterrupt"/> fires first. Predicate-based form of <c>After().Then().Within()</c>.
	/// </summary>
	public static TemporalRule<TEvent> AfterUntil(
		Func<TEvent, bool> isTrigger,
		Func<TEvent, TEvent, bool> isNext,
		TimeSpan window,
		Func<TEvent, bool>? isInterrupt = null
	) =>
		new(() => new AfterMonitor(isTrigger, isNext, window, isInterrupt, required: false, "trigger"));

	/// <summary>Starts a bounded-response rule: <c>After&lt;Confirmed&gt;().Then&lt;Shipped&gt;(match).Within(60s)</c>.</summary>
	public static AfterTrigger<TEvent, TTrigger> After<TTrigger>() where TTrigger : TEvent => new();

	// ── Monitors ─────────────────────────────────────────────────────────

	private sealed class NeverMonitor(Func<TEvent, bool> isT1, Func<TEvent, bool> isT2, Func<TEvent, TEvent, bool> pair) : MonitorBase<TEvent>
	{
		private bool _hasPrevious;
		private TEvent _previous = default!;

		protected override void OnEvent(TEvent e, DateTimeOffset at)
		{
			if (!isT1(e) && !isT2(e))
			{
				return;
			}
			if (_hasPrevious && isT1(_previous) && isT2(e) && pair(_previous, e))
			{
				Violate(at, (_previous, e));
			}
			_previous = e;
			_hasPrevious = true;
		}
	}

	private sealed class NeverTwiceMonitor(Func<TEvent, bool> isRelevant, Func<TEvent, object> key) : MonitorBase<TEvent>
	{
		private readonly HashSet<object> _seen = [];

		protected override void OnEvent(TEvent e, DateTimeOffset at)
		{
			if (isRelevant(e) && !_seen.Add(key(e)))
			{
				Violate(at, e!);
			}
		}
	}

	private sealed class AlwaysMonitor(Func<TEvent, bool> isRelevant, Func<TEvent, bool> predicate) : MonitorBase<TEvent>
	{
		protected override void OnEvent(TEvent e, DateTimeOffset at)
		{
			if (isRelevant(e) && !predicate(e))
			{
				Violate(at, e!);
			}
		}
	}

	private sealed class TransitionsMonitor(Func<TEvent, TEvent, bool> isValid) : MonitorBase<TEvent>
	{
		private bool _hasPrevious;
		private TEvent _previous = default!;

		protected override void OnEvent(TEvent e, DateTimeOffset at)
		{
			if (_hasPrevious && !isValid(_previous, e))
			{
				Violate(at, (_previous, e));
			}
			_previous = e;
			_hasPrevious = true;
		}
	}

	internal sealed class WhenMonitor(Func<TEvent, bool> guard, Func<TEvent, bool> requirement) : MonitorBase<TEvent>
	{
		protected override void OnEvent(TEvent e, DateTimeOffset at)
		{
			if (guard(e) && !requirement(e))
			{
				Violate(at, e!);
			}
		}
	}

	private sealed class EventuallyMonitor(
		Func<TEvent, object> scope,
		Func<TEvent, bool> from,
		Func<TEvent, bool> isTarget,
		string message
	) : MonitorBase<TEvent>
	{
		private readonly HashSet<object> _pending = [];
		private bool _finished;

		protected override void OnEvent(TEvent e, DateTimeOffset at)
		{
			if (from(e))
			{
				_pending.Add(scope(e));
			}
			if (isTarget(e))
			{
				_pending.Remove(scope(e));
			}
		}

		public override Verdict Finish(DateTimeOffset at)
		{
			if (!_finished)
			{
				_finished = true;
				if (_pending.Count > 0)
				{
					Violate(at, $"{_pending.Count} {message}: {string.Join(", ", _pending)}");
				}
				else
				{
					Hold();
				}
			}
			return Current;
		}
	}

	internal sealed class AfterMonitor(
		Func<TEvent, bool> isTrigger,
		Func<TEvent, TEvent, bool> isNext,
		TimeSpan window,
		Func<TEvent, bool>? isInterrupt,
		bool required,
		string describe
	) : MonitorBase<TEvent>
	{
		private readonly List<(TEvent Trigger, DateTimeOffset Deadline)> _open = [];
		private bool _anyTrigger;

		public override DateTimeOffset? NextDeadline => _open.Count == 0 ? null : _open.Min(o => o.Deadline);

		public override Verdict Tick(DateTimeOffset now)
		{
			if (Current.IsViolated)
			{
				return Current;
			}
			var due = _open.Where(o => o.Deadline <= now).OrderBy(o => o.Deadline).ToList();
			if (due.Count > 0)
			{
				Violate(due[0].Deadline, $"No matching event within {window} after {due[0].Trigger}");
			}
			return Current;
		}

		protected override void OnEvent(TEvent e, DateTimeOffset at)
		{
			// Existing obligations first: an event never answers the obligation it opens.
			_open.RemoveAll(o => isNext(o.Trigger, e) || (isInterrupt is not null && isInterrupt(e)));
			if (isTrigger(e))
			{
				_anyTrigger = true;
				_open.Add((e, at + window));
			}
		}

		public override Verdict Finish(DateTimeOffset at)
		{
			Tick(at);
			if (required && !_anyTrigger)
			{
				Violate(at, $"Required {describe} never fired");
			}
			return Current;
		}
	}
}
#pragma warning restore CA1000

// ── When / After builders ─────────────────────────────────────────────────────

public sealed class WhenRule<TEvent>
{
	private readonly Func<TEvent, bool> _guard;

	internal WhenRule(Func<TEvent, bool> guard) => _guard = guard;

	public TemporalRule<TEvent> Requires(Func<TEvent, bool> requirement) => new(() => new Temporal<TEvent>.WhenMonitor(_guard, requirement));
}

public sealed class AfterTrigger<TEvent, TTrigger> where TTrigger : TEvent
{
	internal AfterTrigger() { }

	public ThenNext<TEvent, TTrigger> Then<TNext>(Func<TTrigger, TNext, bool>? matches = null) where TNext : TEvent =>
		new((t, e) => e is TNext n && (matches is null || matches(t, n)));

	public ThenNext<TEvent, TTrigger> Then<TNext1, TNext2>(
		Func<TTrigger, TNext1, bool>? m1 = null,
		Func<TTrigger, TNext2, bool>? m2 = null
	) where TNext1 : TEvent where TNext2 : TEvent =>
		new((t, e) => (e is TNext1 a && (m1 is null || m1(t, a))) || (e is TNext2 b && (m2 is null || m2(t, b))));
}

public sealed class ThenNext<TEvent, TTrigger> where TTrigger : TEvent
{
	private readonly Func<TTrigger, TEvent, bool> _isNext;

	internal ThenNext(Func<TTrigger, TEvent, bool> isNext) => _isNext = isNext;

	public WithinWindow<TEvent, TTrigger> Within(TimeSpan window) => new(_isNext, window);
}

public sealed class WithinWindow<TEvent, TTrigger> where TTrigger : TEvent
{
	private readonly Func<TTrigger, TEvent, bool> _isNext;
	private readonly TimeSpan _window;

	internal WithinWindow(Func<TTrigger, TEvent, bool> isNext, TimeSpan window)
	{
		_isNext = isNext;
		_window = window;
	}

	/// <summary>Lifts the obligation, without violation, when a <typeparamref name="TInterrupt"/> fires before the answer.</summary>
	public TemporalRule<TEvent> UnlessInterruptedBy<TInterrupt>() where TInterrupt : TEvent => Build(e => e is TInterrupt, required: false);

	/// <summary>A trigger that never fires at all is also a violation (reported at finish).</summary>
	public TemporalRule<TEvent> Required() => Build(null, required: true);

#pragma warning disable CA1000
	public static implicit operator TemporalRule<TEvent>(WithinWindow<TEvent, TTrigger> b) => b.Build(null, required: false);
#pragma warning restore CA1000

	private TemporalRule<TEvent> Build(Func<TEvent, bool>? isInterrupt, bool required) =>
		new(
			() =>
				new Temporal<TEvent>.AfterMonitor(
					e => e is TTrigger,
					(t, e) => _isNext((TTrigger)(object)t!, e),
					_window,
					isInterrupt,
					required,
					typeof(TTrigger).Name
				)
		);
}

// ── Offline checking ─────────────────────────────────────────────────────────

public sealed class TraceReport(IReadOnlyList<(string Name, Verdict Verdict)> results)
{
	public IReadOnlyList<(string Name, Verdict Verdict)> Results { get; } = results;

	public IReadOnlyList<(string Name, Verdict Verdict)> Violations => [.. Results.Where(r => r.Verdict.IsViolated)];

	/// <summary>Rules still waiting on something (an open deadline, or nothing happened that decides them).</summary>
	public IReadOnlyList<(string Name, Verdict Verdict)> Pending => [.. Results.Where(r => r.Verdict.IsPending)];

	public bool Passed => Violations.Count == 0;

	public override string ToString() =>
		string.Join(
			Environment.NewLine,
			Results.Select(r =>
					r.Verdict switch
					{
						{ IsViolated: true } v => $"VIOLATED {r.Name} at {v.ViolationAt:O}: {v.ViolationEvidence}",
						{ IsHolds: true } => $"holds    {r.Name}",
						_ => $"pending  {r.Name}",
					}
			)
		);
}

public static class Trace
{
	/// <summary>
	/// Checks a recorded, timestamped sequence of events against <paramref name="rules"/>. A trace is a closed run: after
	/// the last event (or <paramref name="endAt"/>) every deadline is reached and unresolved liveness obligations are decided.
	/// </summary>
	public static TraceReport Check<TEvent>(IEnumerable<Timestamped<TEvent>> trace, TemporalRuleSet<TEvent> rules, DateTimeOffset? endAt = null)
	{
		var monitors = rules.Rules.Select(r => (r.Name, Monitor: r.Rule.CreateMonitor())).ToList();
		var last = DateTimeOffset.MinValue;
		foreach (var (at, e) in trace)
		{
			last = at;
			foreach (var (_, monitor) in monitors)
			{
				monitor.Observe(e, at);
			}
		}
		var end = endAt ?? last;
		foreach (var (_, monitor) in monitors)
		{
			monitor.Tick(end);
			monitor.Finish(end);
		}
		return new TraceReport([.. monitors.Select(m => (m.Name, m.Monitor.Current))]);
	}
}
