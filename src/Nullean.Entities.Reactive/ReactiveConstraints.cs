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

// Verdict (Pending / Holds / Violated) lives in Nullean.Entities: the core temporal monitor reports the same type.

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

	// A cheap synchronous snapshot — "has anything violated so far" — rather than subscribing to
	// State and tracking it yourself. This is what lets Nullean.Entities.CsCheck's SampleWith use a
	// MonitoredConstraintSet as CsCheck's own `equal` check: `equal: (sut, _) => !monitored.AnyViolated`.
	public bool AnyViolated => _byName.Values.Any(c => c.Current.IsViolated);

	/// <summary>
	/// Every constraint currently in a <see cref="Verdict.Violated"/> state, by name — the detail
	/// behind <see cref="AnyViolated"/>'s yes/no. This is what lets
	/// Nullean.Entities.CsCheck's <c>RunWith</c> report which named constraints fired for a
	/// shrunk failing case, not just that one did.
	/// </summary>
	public IReadOnlyList<(string Name, Verdict Verdict)> Violations =>
		[.. _byName.Where(kv => kv.Value.Current.IsViolated).Select(kv => (kv.Key, kv.Value.Current))];

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

// ── IReactiveConstraintsFor<TEvent> ───────────────────────────────────────────
// The reactive companion-class interface — mirrors Nullean.Entities.IConstraintsFor<T>: a plain
// instance interface (not static-abstract), so a value implementing it is ordinary data a caller can
// pass around and infer TEvent from, rather than only a generic type constraint. There is no reactive
// self-attach interface: a union that declares its own ReactiveConstraint<TEvent> members still gets
// this same interface, since reactive self-attach already flows through the sync core's
// IHaveConstraints<T> + ToReactive() (see ReactiveConstraintSetExtensions.AttachConstraints below)
// rather than a dedicated reactive-native self-attach interface.

public interface IReactiveConstraintsFor<TEvent>
{
	ReactiveConstraintSet<TEvent> All { get; }
}

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
	/// Violated when two T1/T2 events satisfy the predicate and nothing else of type T1 or T2 comes
	/// between them — every event of any OTHER type is filtered out first, so "consecutive" here
	/// means consecutive among T1/T2 occurrences, not consecutive in the raw stream. That makes this
	/// the wrong choice whenever a legitimate intermediate state can sit between T1 and T2: e.g.
	/// <c>Never&lt;Placed,Shipped&gt;</c> to forbid skipping Confirmed produces a false positive for
	/// the ordinary Placed → Confirmed → Shipped sequence, because Confirmed is neither T1 nor T2 and
	/// gets filtered out from between them. Use <see cref="Transitions(Func{TEvent, TEvent, bool})"/>
	/// instead when the intervening event matters — it checks raw-stream-adjacent pairs, not
	/// filtered ones. <c>Never&lt;T1,T2&gt;</c> is the right tool only when T1/T2 are the sole events
	/// of interest for the correlated key (e.g. a terminal state that, once reached, precludes any
	/// other T1/T2 event from legitimately following it — <c>Never&lt;Cancelled,Shipped&gt;</c> is
	/// safe because nothing can ever un-cancel an order back onto a path to Shipped).
	/// Pair receives (T1, T2) — timestamps come from the attached scheduler, not from
	/// the event payload.
	/// </summary>
	public static ReactiveConstraint<TEvent> Never<T1, T2>(Func<T1, T2, bool> pair) where T1 : TEvent where T2 : TEvent =>
		new(
			(events, scheduler) =>
				events
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
			(events, scheduler) =>
				WhereType<T>(events)
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
			(events, scheduler) =>
				events
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
			(events, scheduler) =>
				events
					.Buffer(2, 1)
					.Where(buf => buf.Count == 2 && !isValid(buf[0], buf[1]))
					.Select(buf => Verdict.Violated(scheduler.Now, (buf[0], buf[1])))
					.Take(1)
					.StartWith(Verdict.Pending)
		);

	// ── Predicate-based counterparts ────────────────────────────────────────
	// Every structural/temporal operator above is generic over a case type with `where T : TEvent`
	// — fine for a record hierarchy, where a case genuinely IS-A TEvent. It does not typecheck at
	// all for a C# union: `readonly union OrderEvent(Placed, Shipped);` makes Placed/Shipped
	// ordinary sibling top-level types with compiler-synthesized pattern-matching support, not real
	// subtypes of OrderEvent (`where T : OrderEvent` rejects a union type outright with CS0701 —
	// "not a valid constraint" — since the union itself compiles to a sealed type). These overloads
	// take a `Func<TEvent,bool>` classifier instead of a type parameter, so "is this a Placed" is
	// expressed as `e is OrderEvent.Placed` (pattern matching, not a type constraint) and works
	// identically whether TEvent is a record hierarchy or a union.

	/// <summary>
	/// Predicate-based counterpart to <see cref="Never{T1, T2}"/>, for event shapes where "is this
	/// a T1/T2" can only be a runtime check, not a generic constraint (C# unions — see the remark
	/// above). Same filter-then-adjacency semantics and the same caveat as the typed overload: not
	/// the right choice when a legitimate intervening event type can sit between the two classified
	/// events — use <see cref="Transitions(Func{TEvent, TEvent, bool})"/> instead.
	/// </summary>
	public static ReactiveConstraint<TEvent> Never(Func<TEvent, bool> isT1, Func<TEvent, bool> isT2, Func<TEvent, TEvent, bool> pair) =>
		new(
			(events, scheduler) =>
				events
					.Where(e => isT1(e) || isT2(e))
					.Buffer(2, 1)
					.Where(buf => buf.Count == 2 && isT1(buf[0]) && isT2(buf[1]) && pair(buf[0], buf[1]))
					.Select(buf => Verdict.Violated(scheduler.Now, (buf[0], buf[1])))
					.Take(1)
					.StartWith(Verdict.Pending)
		);

	/// <summary>Predicate-based counterpart to <see cref="NeverTwice{T}"/> — see the remark above.</summary>
	public static ReactiveConstraint<TEvent> NeverTwice(Func<TEvent, bool> isRelevant, Func<TEvent, object> key) =>
		new((events, scheduler) => Observable.Defer(() =>
		{
			var seen = new HashSet<object>();
			return events.Where(isRelevant).Where(e => !seen.Add(key(e))).Select(e => Verdict.Violated(scheduler.Now, e!)).Take(1).StartWith(Verdict.Pending);
		}));

	/// <summary>Predicate-based counterpart to <see cref="Always{T}"/> — see the remark above.</summary>
	public static ReactiveConstraint<TEvent> Always(Func<TEvent, bool> isRelevant, Func<TEvent, bool> predicate) =>
		new(
			(events, scheduler) =>
				events
					.Where(isRelevant)
					.Where(e => !predicate(e))
					.Select(e => Verdict.Violated(scheduler.Now, e!))
					.Take(1)
					.StartWith(Verdict.Pending)
		);

	/// <summary>
	/// Predicate-based counterpart to <see cref="Eventually{T}"/> — see the remark above.
	/// <paramref name="isTarget"/> replaces the type parameter: an event for which it returns true
	/// resolves the obligation opened by <paramref name="from"/> for the same <paramref name="scope"/> key.
	/// </summary>
	public static ReactiveConstraint<TEvent> Eventually(
		Func<TEvent, object> scope,
		Func<TEvent, bool> from,
		Func<TEvent, bool> isTarget,
		string? message = null
	) =>
		new(
			(events, scheduler) =>
				Observable.Create<Verdict>(observer =>
				{
					var pending = new HashSet<object>();
					return events.Subscribe(e =>
					{
						if (from(e))
						{
							pending.Add(scope(e));
						}
						if (isTarget(e))
						{
							pending.Remove(scope(e));
						}
					}, observer.OnError, () =>
					{
						observer.OnNext(
							pending.Count > 0
								? Verdict.Violated(
									scheduler.Now,
									message ?? $"{pending.Count} instance(s) never reached the target event: {string.Join(", ", pending)}"
								)
								: Verdict.Holds
						);
						observer.OnCompleted();
					});
				}).StartWith(Verdict.Pending)
		);

	/// <summary>
	/// Predicate-based counterpart to the whole <see cref="After{TTrigger}"/>/<c>.Then()</c>/
	/// <c>.Within()</c>/<c>.UnlessInterruptedBy()</c> builder chain, collapsed into one primitive
	/// since there is no type parameter to hang separate builder steps off — see the remark above.
	/// <paramref name="isNext"/> receives (trigger, candidate) and replaces both <c>.Then&lt;T&gt;()</c>
	/// and <c>.Then&lt;T1,T2&gt;()</c> (an <paramref name="isNext"/> that matches either case covers
	/// the two-case form). <paramref name="isInterrupt"/> replaces <c>.UnlessInterruptedBy&lt;T&gt;()</c>
	/// when supplied; omitted (null), a trigger with nothing satisfying <paramref name="isNext"/>
	/// within <paramref name="window"/> violates, matching <c>.Required()</c>.
	/// </summary>
	public static ReactiveConstraint<TEvent> AfterUntil(
		Func<TEvent, bool> isTrigger,
		Func<TEvent, TEvent, bool> isNext,
		TimeSpan window,
		Func<TEvent, bool>? isInterrupt = null
	) =>
		new(
			(events, scheduler) =>
				events
					.Where(isTrigger)
					.SelectMany(trigger =>
					{
						var nextObs = events.Where(e => isNext(trigger, e)).Select(_ => Verdict.Holds).Take(1);
						var timeoutObs = Observable.Timer(window, scheduler).Select(
							_ => Verdict.Violated(scheduler.Now, $"No matching event within {window} after {trigger}")
						);

						if (isInterrupt is null)
						{
							return nextObs.Amb(timeoutObs);
						}

						var interruptObs = events.Where(isInterrupt).Select(_ => Verdict.Holds).Take(1);
						return nextObs.Amb(interruptObs).Amb(timeoutObs);
					})
					.Where(v => v.IsViolated)
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

	/// <summary>
	/// Unbounded liveness: every instance opened by <paramref name="from"/> must eventually see a
	/// <typeparamref name="T"/> event sharing its <paramref name="scope"/> key, with no deadline.
	/// Unlike <see cref="After{TTrigger}"/>'s <c>.Then().Within()</c>, there is no window to expire
	/// mid-stream — an instance opened five events ago is still allowed to close on the next one.
	/// The only point at which "never" can be told apart from "not yet" is when the stream itself
	/// ends, so this resolves to <see cref="Verdict.Violated"/> only on <c>OnCompleted</c>, with any
	/// scope keys still open at that point reported as evidence. A stream that never completes
	/// (a live production feed) stays <see cref="Verdict.Pending"/> forever — this constraint is for
	/// a bounded run (a CsCheck-generated walk, a recorded session), not a live dashboard.
	/// </summary>
	public static ReactiveConstraint<TEvent> Eventually<T>(Func<TEvent, object> scope, Func<TEvent, bool> from, string? message = null) where T : TEvent =>
		new(
			(events, scheduler) =>
				Observable.Create<Verdict>(observer =>
				{
					var pending = new HashSet<object>();
					return events.Subscribe(e =>
					{
						if (from(e))
						{
							pending.Add(scope(e));
						}
						if (e is T)
						{
							pending.Remove(scope(e));
						}
					}, observer.OnError, () =>
					{
						observer.OnNext(
							pending.Count > 0
								? Verdict.Violated(
									scheduler.Now,
									message ?? $"{pending.Count} instance(s) never reached {typeof(T).Name}: {string.Join(", ", pending)}"
								)
								: Verdict.Holds
						);
						observer.OnCompleted();
					});
				}).StartWith(Verdict.Pending)
		);
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
			(events, scheduler) =>
				events
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
	internal AfterBuilder() { }

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
	public ReactiveConstraint<TEvent> UnlessInterruptedBy<TInterrupt>() where TInterrupt : TEvent =>
		BuildFactory<TInterrupt>(withInterrupt: true, required: false);

	/// <summary>
	/// If the trigger fires but the Then-event does not arrive within the window,
	/// the constraint is Violated. Without Required(), a trigger that never fires
	/// leaves the constraint Pending (vacuously Holds).
	/// With Required(), a trigger that never fires is also a Violation.
	/// </summary>
	public ReactiveConstraint<TEvent> Required() => BuildFactory<TEvent>(withInterrupt: false, required: true);

	// Implicit conversion so you can assign directly to ReactiveConstraint<TEvent>
	// without calling .Required() — vacuous-Holds semantics by default.
#pragma warning disable CA1000
	public static implicit operator ReactiveConstraint<TEvent>(WithinBuilder<TEvent, TTrigger, TNext> b) =>
		b.BuildFactory<TEvent>(withInterrupt: false, required: false);
#pragma warning restore CA1000

	private ReactiveConstraint<TEvent> BuildFactory<TInterrupt>(bool withInterrupt, bool required) where TInterrupt : TEvent
	{
		var window = _window;
		var matches = _matches;

		return new ReactiveConstraint<TEvent>((events, scheduler) =>
		{
			var anyTrigger = false;
			return events
				.Where(e => e is TTrigger)
				.Select(e => (TTrigger)(object)e!)
				.Do(_ => anyTrigger = true)
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
						return nextObs.Amb(timeoutObs);
					}

					var interruptObs = events.Where(e => e is TInterrupt).Select(e => (TInterrupt)(object)e!).Select(_ => Verdict.Holds).Take(1);

					return nextObs.Amb(interruptObs).Amb(timeoutObs);
				})
				.Where(v => v.IsViolated)
				.Take(1)
				.Concat(
					Observable.Defer(
						() =>
							required && !anyTrigger
								? Observable.Return(Verdict.Violated(scheduler.Now, $"Required {typeof(TTrigger).Name} trigger never fired"))
								: Observable.Empty<Verdict>()
					)
				)
				.StartWith(Verdict.Pending);
		});
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

	public ReactiveConstraint<TEvent> UnlessInterruptedBy<TInterrupt>() where TInterrupt : TEvent =>
		BuildFactory<TInterrupt>(withInterrupt: true, required: false);

	/// <inheritdoc cref="WithinBuilder{TEvent,TTrigger,TNext}.Required"/>
	public ReactiveConstraint<TEvent> Required() => BuildFactory<TEvent>(withInterrupt: false, required: true);

#pragma warning disable CA1000
	public static implicit operator ReactiveConstraint<TEvent>(WithinBuilder2<TEvent, TTrigger, TNext1, TNext2> b) =>
		b.BuildFactory<TEvent>(withInterrupt: false, required: false);
#pragma warning restore CA1000

	private ReactiveConstraint<TEvent> BuildFactory<TInterrupt>(bool withInterrupt, bool required) where TInterrupt : TEvent
	{
		var window = _window;
		var m1 = _m1;
		var m2 = _m2;

		return new ReactiveConstraint<TEvent>((events, scheduler) =>
		{
			var anyTrigger = false;
			return events
				.Where(e => e is TTrigger)
				.Select(e => (TTrigger)(object)e!)
				.Do(_ => anyTrigger = true)
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
						return next1Obs.Amb(next2Obs).Amb(timeoutObs);
					}

					var interruptObs = events.Where(e => e is TInterrupt).Select(e => (TInterrupt)(object)e!).Select(_ => Verdict.Holds).Take(1);

					return next1Obs.Amb(next2Obs).Amb(interruptObs).Amb(timeoutObs);
				})
				.Where(v => v.IsViolated)
				.Take(1)
				.Concat(
					Observable.Defer(
						() =>
							required && !anyTrigger
								? Observable.Return(Verdict.Violated(scheduler.Now, $"Required {typeof(TTrigger).Name} trigger never fired"))
								: Observable.Empty<Verdict>()
					)
				)
				.StartWith(Verdict.Pending);
		});
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
	/// Lifts a synchronous <see cref="ConstraintSet{T}"/> onto a stream: each constraint becomes a
	/// <see cref="ReactiveConstraint{TEvent}"/> that re-evaluates on every event and reports the
	/// first violation. A <see cref="ConstraintKind.PointInTime"/> constraint checks every event on
	/// its own, exactly like <see cref="ConstraintSet{T}.Validate"/> would; a
	/// <see cref="ConstraintKind.Sequential"/> one checks each consecutive pair, exactly like
	/// <see cref="ConstraintSet{T}.Apply"/> would — there is no first "previous" here either, so
	/// the very first event alone never triggers one. This does not gain the temporal/structural
	/// operators declared directly against <see cref="Constraints{TEvent}"/> — those still need to
	/// be authored in Reactive terms.
	/// </summary>
	public static ReactiveConstraintSet<T> ToReactive<T>(this ConstraintSet<T> set)
	{
		var pairs = set.AsConstraints().Select(c => (c.Name!, ToReactiveConstraint(c))).ToArray();
		return ReactiveConstraintSet.From(pairs);
	}

	private static ReactiveConstraint<T> ToReactiveConstraint<T>(Constraint<T> constraint) =>
		new((events, scheduler) =>
		{
			// Time-aware constraints read "now" from the stream's own scheduler, so virtual time
			// (HistoricalScheduler under a Script) drives them the same way it drives After().Within().
			var time = new SchedulerTimeProvider(scheduler);
			var violationLists = constraint.Kind == ConstraintKind.PointInTime
				? events.Select(e => constraint.CheckAt(e, e, time).ToList())
				: events.Buffer(2, 1).Where(pair => pair.Count == 2).Select(pair => constraint.CheckAt(pair[0], pair[1], time).ToList());

			return violationLists
				.Where(violations => violations.Count > 0)
				.Select(violations => Verdict.Violated(scheduler.Now, violations))
				.Take(1)
				.StartWith(Verdict.Pending);
		});
}

/// <summary>A <see cref="TimeProvider"/> whose clock is an Rx <see cref="IScheduler"/>'s.</summary>
internal sealed class SchedulerTimeProvider(IScheduler scheduler) : TimeProvider
{
	public override DateTimeOffset GetUtcNow() => scheduler.Now;
}
