// Nullean.Entities.Reactive — temporal constraints over IObservable<TEvent> streams.
//
// Core idea: ReactiveConstraint<TEvent> is a value — a descriptor you can place anywhere,
// including as a static member of the union type it constrains. The union HOLDS
// constraints; it does not inherit from anything.
//
// C# 11 specifics used here:
//   - static abstract interface members → IHaveConstraints<T> (Nullean.Entities) /
//                                          F-bounded self-type
//   - partial records                   → a future source gen adds IHaveConstraints<T>
//
// No doc comments yet (checkpoint-A exploration code) — CS1591 is suppressed project-wide
// in Directory.Build.props rather than per file.

using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

// No `using Nullean.Entities;` needed: Nullean.Entities.Reactive is nested inside it, and C#
// name lookup walks up enclosing namespaces, so IHaveConstraints<T>/ConstraintSet<T>/Constraint<T>
// below resolve unqualified.
namespace Nullean.Entities.Reactive;

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

// ── MonitoredConstraint<TEvent> ───────────────────────────────────────────────
// The live, attached instance of a ReactiveConstraint<TEvent>.

public sealed class MonitoredConstraint<TEvent> : IDisposable
{
	// BehaviorSubject, not Subject: a late subscriber — e.g. MonitoredConstraintSet.State,
	// which merges every constraint's Verdicts stream but only subscribes once someone
	// subscribes to State itself — must see the constraint's current verdict immediately,
	// not just verdicts that happen to change after that subscription starts. A plain Subject
	// would silently drop every constraint that had already settled (even into its initial
	// Pending) before State was subscribed to.
	private readonly BehaviorSubject<Verdict> _subject = new(Verdict.Pending);
	private readonly IDisposable _subscription;

	public string Name { get; }
	public Verdict Current { get; private set; } = Verdict.Pending;
	public IObservable<Verdict> Verdicts { get; }

	internal MonitoredConstraint(string name, IObservable<Verdict> verdicts)
	{
		Name = name;
		Verdicts = _subject.AsObservable();
		_subscription = verdicts.Subscribe(
			v =>
			{
				Current = v;
				_subject.OnNext(v);
			},
			_subject.OnError,
			_subject.OnCompleted
		);
	}

	public void Dispose() => _subscription.Dispose();
}

// ── ReactiveConstraint<TEvent> ────────────────────────────────────────────────
// An immutable descriptor. Lives anywhere — static field on a record, local
// variable, constructor parameter. Attach it to an IObservable<TEvent> to get
// a MonitoredConstraint<TEvent> back.

public sealed class ReactiveConstraint<TEvent>
{
	internal string? Name
	{
		get;
		set;
	} // set by .Named() only — [Constraint] source gen names via the (nameof(field), field) tuple it passes to ReactiveConstraintSet.From instead, see Attach(events, name, scheduler) below
	internal Func<IObservable<TEvent>, IScheduler, IObservable<Verdict>> Factory { get; }

	internal ReactiveConstraint(Func<IObservable<TEvent>, IScheduler, IObservable<Verdict>> factory) => Factory = factory;

	public ReactiveConstraint<TEvent> Named(string name)
	{
		Name = name;
		return this;
	}

	public MonitoredConstraint<TEvent> Attach(IObservable<TEvent> events, IScheduler? scheduler = null) => Attach(events, Name ?? "unnamed", scheduler);

	// Used by ReactiveConstraintSet<TEvent>.Attach: the set's own (string Name, ReactiveConstraint)
	// tuple is authoritative for MonitoredConstraintSet's by-name lookup, which must agree with
	// whatever name the set was built with — not with whether this constraint happened to have
	// .Named() called on it (ToReactive() never does; it names via the tuple only).
	internal MonitoredConstraint<TEvent> Attach(IObservable<TEvent> events, string name, IScheduler? scheduler = null)
	{
		scheduler ??= DefaultScheduler.Instance;
		var verdicts = Factory(events, scheduler);
		return new MonitoredConstraint<TEvent>(name, verdicts);
	}
}

// ── ReactiveConstraintSet<TEvent> ─────────────────────────────────────────────
// A named, ordered collection of ReactiveConstraint<TEvent>s.

public sealed class ReactiveConstraintSet<TEvent>
{
	private readonly IReadOnlyList<(string Name, ReactiveConstraint<TEvent> Constraint)> _constraints;

	internal ReactiveConstraintSet(IReadOnlyList<(string, ReactiveConstraint<TEvent>)> constraints) => _constraints = constraints;

	public MonitoredConstraintSet<TEvent> Attach(IObservable<TEvent> events, IScheduler? scheduler = null)
	{
		var shared = events.Publish().RefCount();
		var pairs = _constraints.Select(c =>
		{
			var running = c.Constraint.Attach(shared, c.Name, scheduler);
			return (c.Constraint, running);
		}).ToList();
		return new MonitoredConstraintSet<TEvent>(pairs);
	}
}

public static class ReactiveConstraintSet
{
	public static ReactiveConstraintSet<TEvent> From<TEvent>(params (string Name, ReactiveConstraint<TEvent> Constraint)[] constraints) => new(constraints);
}

// ── MonitoredConstraintSet<TEvent> ────────────────────────────────────────────
// The live attachment of a ReactiveConstraintSet. Keyed by ReactiveConstraint<TEvent>
// instance (compile-time safety) or by name string (runtime / dashboard use).

public sealed class MonitoredConstraintSet<TEvent>
{
	private readonly Dictionary<ReactiveConstraint<TEvent>, MonitoredConstraint<TEvent>> _byConstraint;
	private readonly Dictionary<string, MonitoredConstraint<TEvent>> _byName;

	internal MonitoredConstraintSet(IEnumerable<(ReactiveConstraint<TEvent> Constraint, MonitoredConstraint<TEvent> Running)> constraints)
	{
		var list = constraints.ToList();
		_byConstraint = list.ToDictionary(p => p.Constraint, p => p.Running);
		_byName = list.ToDictionary(p => p.Running.Name, p => p.Running);
	}

	// Access by constraint descriptor — typed, compile-time safe.
	// The ReactiveConstraint<TEvent> instance IS the key.
	public MonitoredConstraint<TEvent> this[ReactiveConstraint<TEvent> constraint] => _byConstraint[constraint];

	// Access by name — for logging, dashboards, property tests output.
	public MonitoredConstraint<TEvent> this[string name] => _byName[name];

	// All constraints as a change stream.
	public IObservable<ConstraintSetState> State =>
		_byName
			.Values
			.Select(c => c.Verdicts.Select(v => (c.Name, v)))
			.Merge()
			.Scan(
				(IReadOnlyDictionary<string, Verdict>)new Dictionary<string, Verdict>(),
				(acc, pair) => new Dictionary<string, Verdict>(acc) { [pair.Name] = pair.v }
			)
			.Select(d => new ConstraintSetState(d));
}

// ── ConstraintSetState ────────────────────────────────────────────────────────

public sealed record ConstraintSetState(IReadOnlyDictionary<string, Verdict> Verdicts);

// ── Constraints<TEvent> — static builder ──────────────────────────────────────
// No base class. All fluent builder methods live here.
// Use `using static Nullean.Entities.Reactive.Constraints<MyEvent>;` in the file that
// defines the event union — then After<T>(), Never<T>() etc. read like prose.

#pragma warning disable CA1000
public static class Constraints<TEvent>
{
	// Helper: select events of a subtype without triggering OfType ambiguity in .NET 10.
	private static IObservable<T> WhereType<T>(IObservable<TEvent> source) where T : TEvent => source.Where(e => e is T).Select(e => (T)(object)e!);

	// ── Structural operators ──────────────────────────────────────────────

	/// <summary>
	/// Violated when two consecutive events of types T1 and T2 satisfy the predicate.
	/// Pair receives (T1, T2) — timestamps come from the attached scheduler, not from
	/// the event payload.
	/// </summary>
	public static ReactiveConstraint<TEvent> Never<T1, T2>(Func<T1, T2, bool> pair) where T1 : TEvent where T2 : TEvent =>
		new(
			(events, scheduler) => events
				.Where(e => e is T1 or T2)
				.Buffer(2, 1)
				.Where(buf => buf.Count == 2 && buf[0] is T1 first && buf[1] is T2 second && pair(first, second))
				.Select(buf => Verdict.Violated(scheduler.Now, (buf[0], buf[1])))
				.Take(1)
				.StartWith(Verdict.Pending)
		);

	/// <summary>
	/// Violated when two non-consecutive events of type T share the same key.
	/// Unlike Never&lt;T,T&gt;, catches non-adjacent duplicates.
	/// </summary>
	public static ReactiveConstraint<TEvent> NeverTwice<T>(Func<T, object> key) where T : TEvent =>
		new((events, scheduler) => Observable.Defer(() =>
		{
			var seen = new HashSet<object>();
			return WhereType<T>(events).Where(e => !seen.Add(key(e))).Select(e => Verdict.Violated(scheduler.Now, e!)).Take(1).StartWith(Verdict.Pending);
		}));

	/// <summary>
	/// Violated if the predicate is false for any event of type T.
	/// </summary>
	public static ReactiveConstraint<TEvent> Always<T>(Func<T, bool> predicate) where T : TEvent =>
		new(
			(events, scheduler) => WhereType<T>(events)
				.Where(e => !predicate(e))
				.Select(e => Verdict.Violated(scheduler.Now, e!))
				.Take(1)
				.StartWith(Verdict.Pending)
		);

	/// <summary>
	/// Transition allowlist. Any consecutive pair not in the table is a violation.
	/// </summary>
	public static ReactiveConstraint<TEvent> Transitions(params (Type From, Type To)[] allowed) =>
		new(
			(events, scheduler) => events
				.Buffer(2, 1)
				.Where(buf =>
				{
					if (buf.Count < 2)
					{
						return false;
					}
					var fromType = buf[0]!.GetType();
					var toType = buf[1]!.GetType();
					return !allowed.Any(a => a.From.IsAssignableFrom(fromType) && a.To.IsAssignableFrom(toType));
				})
				.Select(buf => Verdict.Violated(scheduler.Now, (buf[0], buf[1])))
				.Take(1)
				.StartWith(Verdict.Pending)
		);

	/// <summary>
	/// Transition guard. Return true if the transition is legal, false if not.
	/// Receives full state values so guards can depend on payload.
	/// </summary>
	public static ReactiveConstraint<TEvent> Transitions(Func<TEvent, TEvent, bool> isValid) =>
		new(
			(events, scheduler) => events
				.Buffer(2, 1)
				.Where(buf => buf.Count == 2 && !isValid(buf[0], buf[1]))
				.Select(buf => Verdict.Violated(scheduler.Now, (buf[0], buf[1])))
				.Take(1)
				.StartWith(Verdict.Pending)
		);

	/// <summary>
	/// Starts a conditional constraint: <c>When(guard).Requires(requirement)</c> is violated the
	/// first time an event satisfies <paramref name="guard"/> but fails <c>requirement</c>. Unlike
	/// <see cref="Transitions(Func{TEvent, TEvent, bool})"/>, this only ever looks at the current
	/// event — there is no "previous" here, so a requirement that needs one belongs in
	/// <see cref="Transitions(Func{TEvent, TEvent, bool})"/> instead.
	/// </summary>
	public static WhenBuilder<TEvent> When(Func<TEvent, bool> guard) => new(guard);

	// ── Temporal operators ────────────────────────────────────────────────

	public static AfterBuilder<TEvent, TTrigger> After<TTrigger>() where TTrigger : TEvent => new();
}
#pragma warning restore CA1000

// ── When / Requires ────────────────────────────────────────────────────────────

public sealed class WhenBuilder<TEvent>
{
	private readonly Func<TEvent, bool> _guard;

	internal WhenBuilder(Func<TEvent, bool> guard) => _guard = guard;

	/// <summary>
	/// Violated the first time an event satisfies the guard passed to <c>When</c> but fails
	/// <paramref name="requirement"/>. Pending until then, Holds forever once satisfied — this
	/// does not re-arm per event the way an unconditional constraint might read.
	/// </summary>
	public ReactiveConstraint<TEvent> Requires(Func<TEvent, bool> requirement) =>
		new(
			(events, scheduler) => events
				.Where(_guard)
				.Where(e => !requirement(e))
				.Select(e => Verdict.Violated(scheduler.Now, e!))
				.Take(1)
				.StartWith(Verdict.Pending)
		);
}

// ── After / Then / Within / UnlessInterruptedBy chain ────────────────────────
// Builder types. Only the terminal (.Required() / .Within()) returns ReactiveConstraint<TEvent>.

public sealed class AfterBuilder<TEvent, TTrigger> where TTrigger : TEvent
{
	internal AfterBuilder()
	{ }

	public ThenBuilder<TEvent, TTrigger, TNext> Then<TNext>(Func<TTrigger, TNext, bool>? matches = null) where TNext : TEvent => new(matches);

	public ThenBuilder2<TEvent, TTrigger, TNext1, TNext2> Then<TNext1, TNext2>(
		Func<TTrigger, TNext1, bool>? m1 = null,
		Func<TTrigger, TNext2, bool>? m2 = null
	) where TNext1 : TEvent where TNext2 : TEvent => new(m1, m2);
}

public sealed class ThenBuilder<TEvent, TTrigger, TNext> where TTrigger : TEvent where TNext : TEvent
{
	private readonly Func<TTrigger, TNext, bool>? _matches;

	internal ThenBuilder(Func<TTrigger, TNext, bool>? matches) => _matches = matches;

	public WithinBuilder<TEvent, TTrigger, TNext> Within(TimeSpan window) => new(window, _matches);
}

public sealed class ThenBuilder2<TEvent, TTrigger, TNext1, TNext2> where TTrigger : TEvent where TNext1 : TEvent where TNext2 : TEvent
{
	private readonly Func<TTrigger, TNext1, bool>? _m1;
	private readonly Func<TTrigger, TNext2, bool>? _m2;

	internal ThenBuilder2(Func<TTrigger, TNext1, bool>? m1, Func<TTrigger, TNext2, bool>? m2)
	{
		_m1 = m1;
		_m2 = m2;
	}

	public WithinBuilder2<TEvent, TTrigger, TNext1, TNext2> Within(TimeSpan window) => new(window, _m1, _m2);
}

// Within returns a builder that can:
//   a) terminate directly → .Required() or implicit ReactiveConstraint<TEvent> conversion
//   b) add an interruption → .UnlessInterruptedBy<TInterrupt>() → terminates

public sealed class WithinBuilder<TEvent, TTrigger, TNext> where TTrigger : TEvent where TNext : TEvent
{
	private readonly TimeSpan _window;
	private readonly Func<TTrigger, TNext, bool>? _matches;

	internal WithinBuilder(TimeSpan window, Func<TTrigger, TNext, bool>? matches)
	{
		_window = window;
		_matches = matches;
	}

	/// <summary>
	/// Lifts the temporal obligation when TInterrupt fires before the Then-event.
	/// Result is Holds (obligation removed), not Pending.
	/// </summary>
	public ReactiveConstraint<TEvent> UnlessInterruptedBy<TInterrupt>() where TInterrupt : TEvent => BuildFactory<TInterrupt>(withInterrupt: true);

	/// <summary>
	/// If the trigger fires but the Then-event does not arrive within the window,
	/// the constraint is Violated. Without Required(), a trigger that never fires
	/// leaves the constraint Pending (vacuously Holds).
	/// </summary>
	public ReactiveConstraint<TEvent> Required() => BuildFactory<TEvent>(withInterrupt: false);

	// Implicit conversion so you can assign directly to ReactiveConstraint<TEvent>
	// without calling .Required() — vacuous-Holds semantics by default.
#pragma warning disable CA1000
	public static implicit operator ReactiveConstraint<TEvent>(WithinBuilder<TEvent, TTrigger, TNext> b) => b.BuildFactory<TEvent>(withInterrupt: false);
#pragma warning restore CA1000

	private ReactiveConstraint<TEvent> BuildFactory<TInterrupt>(bool withInterrupt) where TInterrupt : TEvent
	{
		var window = _window;
		var matches = _matches;

		return new ReactiveConstraint<TEvent>(
			(events, scheduler) => events
				.Where(e => e is TTrigger)
				.Select(e => (TTrigger)(object)e!)
				.SelectMany(trigger =>
				{
					var nextObs = events
						.Where(e => e is TNext)
						.Select(e => (TNext)(object)e!)
						.Where(n => matches == null || matches(trigger, n))
						.Select(_ => Verdict.Holds)
						.Take(1);

					var timeoutObs = Observable.Timer(window, scheduler).Select(
						_ => Verdict.Violated(scheduler.Now, $"No {typeof(TNext).Name} within {window} after {trigger}")
					);

					if (!withInterrupt)
					{
						return nextObs.Amb(timeoutObs).Take(1);
					}

					var interruptObs = events.Where(e => e is TInterrupt).Select(e => (TInterrupt)(object)e!).Select(_ => Verdict.Holds).Take(1);

					return nextObs.Amb(interruptObs).Amb(timeoutObs).Take(1);
				})
				.Take(1)
				.StartWith(Verdict.Pending)
		);
	}
}

public sealed class WithinBuilder2<TEvent, TTrigger, TNext1, TNext2> where TTrigger : TEvent where TNext1 : TEvent where TNext2 : TEvent
{
	private readonly TimeSpan _window;
	private readonly Func<TTrigger, TNext1, bool>? _m1;
	private readonly Func<TTrigger, TNext2, bool>? _m2;

	internal WithinBuilder2(TimeSpan window, Func<TTrigger, TNext1, bool>? m1, Func<TTrigger, TNext2, bool>? m2)
	{
		_window = window;
		_m1 = m1;
		_m2 = m2;
	}

	public ReactiveConstraint<TEvent> UnlessInterruptedBy<TInterrupt>() where TInterrupt : TEvent => BuildFactory<TInterrupt>(withInterrupt: true);

	public ReactiveConstraint<TEvent> Required() => BuildFactory<TEvent>(withInterrupt: false);

#pragma warning disable CA1000
	public static implicit operator ReactiveConstraint<TEvent>(WithinBuilder2<TEvent, TTrigger, TNext1, TNext2> b) =>
		b.BuildFactory<TEvent>(withInterrupt: false);
#pragma warning restore CA1000

	private ReactiveConstraint<TEvent> BuildFactory<TInterrupt>(bool withInterrupt) where TInterrupt : TEvent
	{
		var window = _window;
		var m1 = _m1;
		var m2 = _m2;

		return new ReactiveConstraint<TEvent>(
			(events, scheduler) => events
				.Where(e => e is TTrigger)
				.Select(e => (TTrigger)(object)e!)
				.SelectMany(trigger =>
				{
					var next1Obs = events
						.Where(e => e is TNext1)
						.Select(e => (TNext1)(object)e!)
						.Where(n => m1 == null || m1(trigger, n))
						.Select(_ => Verdict.Holds)
						.Take(1);

					var next2Obs = events
						.Where(e => e is TNext2)
						.Select(e => (TNext2)(object)e!)
						.Where(n => m2 == null || m2(trigger, n))
						.Select(_ => Verdict.Holds)
						.Take(1);

					var timeoutObs = Observable.Timer(window, scheduler).Select(
						_ => Verdict.Violated(scheduler.Now, $"No {typeof(TNext1).Name} or {typeof(TNext2).Name} within {window} after {trigger}")
					);

					if (!withInterrupt)
					{
						return next1Obs.Amb(next2Obs).Amb(timeoutObs).Take(1);
					}

					var interruptObs = events.Where(e => e is TInterrupt).Select(e => (TInterrupt)(object)e!).Select(_ => Verdict.Holds).Take(1);

					return next1Obs.Amb(next2Obs).Amb(interruptObs).Amb(timeoutObs).Take(1);
				})
				.Take(1)
				.StartWith(Verdict.Pending)
		);
	}
}

// ── IObservable<TEvent> extensions ────────────────────────────────────────────

public static class ReactiveConstraintSetExtensions
{
	/// <summary>
	/// Attaches the type's own <see cref="IHaveConstraints{TSelf}.Constraints"/> to the
	/// event stream, converting the synchronous <see cref="ConstraintSet{T}"/> to a
	/// <see cref="ReactiveConstraintSet{TEvent}"/> first (see <see cref="ConstraintSetReactiveExtensions.ToReactive{T}"/>).
	/// Only available when TEvent implements IHaveConstraints&lt;TEvent&gt;.
	/// </summary>
	public static MonitoredConstraintSet<TEvent> AttachConstraints<TEvent>(
		this IObservable<TEvent> events,
		IScheduler? scheduler = null
	) where TEvent : IHaveConstraints<TEvent> => TEvent.Constraints.ToReactive().Attach(events, scheduler);
}

public static class ConstraintSetReactiveExtensions
{
	/// <summary>
	/// Lifts a synchronous <see cref="ConstraintSet{T}"/> onto a stream: each constraint
	/// becomes a point-in-time <see cref="ReactiveConstraint{TEvent}"/> that re-evaluates
	/// its <see cref="Constraint{T}.Check"/> on every event and reports the first violation.
	/// This does not gain the temporal/structural operators declared directly against
	/// <see cref="Constraints{TEvent}"/> — those still need to be authored in Reactive terms.
	/// </summary>
	public static ReactiveConstraintSet<T> ToReactive<T>(this ConstraintSet<T> set)
	{
		var pairs = set.AsConstraints().Select(c => (c.Id, ToReactiveConstraint(c))).ToArray();
		return ReactiveConstraintSet.From(pairs);
	}

	private static ReactiveConstraint<T> ToReactiveConstraint<T>(Constraint<T> constraint) =>
		new(
			(events, scheduler) => events
				.Select(e => constraint.Check(e).ToList())
				.Where(violations => violations.Count > 0)
				.Select(violations => Verdict.Violated(scheduler.Now, violations))
				.Take(1)
				.StartWith(Verdict.Pending)
		);
}
