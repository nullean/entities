// Nullean.Entities — synchronous domain constraints.
//
// Constraint<T> is a named, synchronous rule, built only through Constraints<T> (Always,
// Transitions, When().Requires(), or the Check/CheckTransition escape hatches) — never via a
// public constructor, the same way Nullean.Entities.Reactive's ReactiveConstraint<TEvent> is only
// ever built through Constraints<TEvent>. ConstraintSet<T> bundles named constraints and runs them
// over a single instance (Validate) or over a proposed mutation (Apply), returning a Result<T>.
//
// No CsCheck, no System.Reactive dependency here — this is the plain, host-agnostic core.
// Nullean.Entities.Reactive lifts a ConstraintSet<T> onto IObservable<T> streams;
// Nullean.Entities.CsCheck drives it from property-test scripts.
//
// No doc comments yet (checkpoint-A exploration code) — CS1591 is suppressed project-wide
// in Directory.Build.props rather than per file.

using System.Diagnostics;

namespace Nullean.Entities;

// ── Severity ──────────────────────────────────────────────────────────────────

public enum Severity
{
	Error,
	Warning,
	Hint,
}

// ── Violation ─────────────────────────────────────────────────────────────────

public sealed record Violation(string Message, Severity Severity = Severity.Error)
{
	public string? ConstraintId { get; init; }

	/// <summary>Whatever value triggered the violation — attach it for logging/debugging.</summary>
	public object? Evidence { get; init; }

	public int? Line { get; init; }
	public int? Column { get; init; }
	public string? File { get; init; }

	public static Violation Error(string message) => new(message);
	public static Violation Warning(string message) => new(message, Severity.Warning);
	public static Violation Hint(string message) => new(message, Severity.Hint);
}

// ── Result<T> ─────────────────────────────────────────────────────────────────

public abstract record Result<T>
{
	// Closes the hierarchy to the two nested records below: a private protected constructor can
	// only be called from this assembly or a type derived from Result<T>, and every such type here
	// is sealed, so nothing outside this file can ever add a third case.
	private protected Result() { }

	public sealed record Ok(T Value) : Result<T>;

	// Keeps what was attempted, not just why it was rejected — a caller can log or correct it. Not
	// threaded through Match: the common case only wants the violations, and Attempted is still
	// reachable by matching on the Fail record directly when it's needed.
	public sealed record Fail(T Attempted, IReadOnlyList<Violation> Violations) : Result<T>;

	public bool IsOk => this is Ok;
	public bool IsFail => this is Fail;

	public TOut Match<TOut>(Func<T, TOut> ok, Func<IReadOnlyList<Violation>, TOut> fail) => this switch
	{
		Ok o => ok(o.Value),
		Fail f => fail(f.Violations),
		_ => throw new UnreachableException(),
	};

	/// <summary>Throw-on-failure for use in tests / enforcement boundaries.</summary>
	public T Unwrap() => this switch
	{
		Ok o => o.Value,
		Fail f => throw new InvalidOperationException($"Result is Fail: {string.Join(", ", f.Violations.Select(v => v.Message))}"),
		_ => throw new UnreachableException(),
	};
}

// ── ApplyOutcome<T> ───────────────────────────────────────────────────────────
// The three-way outcome of ApplyDetailed on ConstraintSet<T> / ConstrainedValue<T>. Carries T in every case so
// a caller can pattern-match uniformly without extracting the value first.

public abstract record ApplyOutcome<T>
{
	private protected ApplyOutcome() { }

	/// <summary>Mutation passed all constraints and <see cref="ConstrainedValue{T}.Current"/> was updated.</summary>
	public sealed record Applied(T Value) : ApplyOutcome<T>;

	/// <summary>
	/// Mutation produced the same value (by <see cref="EqualityComparer{T}.Default"/>), so no
	/// constraints were run and nothing was committed. Carries the unchanged value so the caller
	/// can pattern-match uniformly.
	/// </summary>
	public sealed record Noop(T Value) : ApplyOutcome<T>;

	/// <summary>
	/// One or more constraints were violated. <see cref="ConstrainedValue{T}.Current"/> is
	/// unchanged. Carries what it was at the time of the failed attempt.
	/// </summary>
	public sealed record Failure(T Current, T Attempted, IReadOnlyList<Violation> Violations) : ApplyOutcome<T>;

	public bool IsApplied => this is Applied;
	public bool IsNoop => this is Noop;
	public bool IsFailure => this is Failure;
}

// ── ConstraintKind ────────────────────────────────────────────────────────────

public enum ConstraintKind
{
	/// <summary>Checked against the value alone — Validate and Apply both run these.</summary>
	PointInTime,

	/// <summary>
	/// Checked against a (previous, next) pair — only Apply can run these; a standalone instance
	/// passed to Validate has no "previous" to compare against, so Validate skips them.
	/// </summary>
	Sequential,
}

// ── Constraint<T> ─────────────────────────────────────────────────────────────

/// <summary>
/// A named, synchronous rule over <typeparamref name="T"/>. Lives anywhere — static field on a
/// record, local variable, constructor parameter. Built only through <see cref="Constraints{T}"/>.
/// </summary>
public sealed class Constraint<T>
{
	/// <summary>
	/// Set by <see cref="Named"/>, or by the (nameof(field), field) tuple <see cref="ConstraintSet"/>'s
	/// From threads through — see [Constraint] source gen, which builds that tuple automatically.
	/// </summary>
	public string? Name { get; private set; }

	public ConstraintKind Kind { get; }

	/// <summary>
	/// Runs the rule. For a <see cref="ConstraintKind.PointInTime"/> constraint the first argument
	/// equals the second (there is no real "previous"); for <see cref="ConstraintKind.Sequential"/>
	/// it is the value before the mutation being checked.
	/// </summary>
	public Func<T, T, IEnumerable<Violation>> Check { get; }

	private readonly Func<T, T, TimeProvider, IEnumerable<Violation>> _checkAt;

	internal Constraint(ConstraintKind kind, Func<T, T, IEnumerable<Violation>> check)
	{
		Kind = kind;
		Check = check;
		_checkAt = (prev, next, _) => check(prev, next);
	}

	// A time-aware constraint reads "now" from whatever TimeProvider the caller checks it with —
	// never from a captured one, since constraints are static fields shared by every caller (and by
	// tests that need to control the clock). Check, which has no provider to offer, uses TimeProvider.System.
	internal Constraint(ConstraintKind kind, Func<T, T, TimeProvider, IEnumerable<Violation>> checkAt)
	{
		Kind = kind;
		_checkAt = checkAt;
		Check = (prev, next) => checkAt(prev, next, TimeProvider.System);
	}

	/// <summary>
	/// Like <see cref="Check"/>, but time-aware constraints (see <c>Constraints&lt;T&gt;.NotInFuture</c>,
	/// <c>NotOlderThan</c>, and the <c>Func&lt;T, DateTimeOffset, bool&gt;</c> overload of
	/// <c>Always</c>) read "now" from <paramref name="time"/>. Other constraints ignore it.
	/// </summary>
	public IEnumerable<Violation> CheckAt(T previous, T next, TimeProvider time) => _checkAt(previous, next, time);

	/// <summary>
	/// Set by <c>When(guard).Requires(...)</c>: the condition under which this constraint has anything
	/// to say. A test driver can ask "was the guard ever true?" to tell a constraint that held from one
	/// that was never exercised.
	/// </summary>
	public Func<T, bool>? Guard { get; private set; }

	/// <summary>
	/// Set by <c>Transitions(selector).Allow(...)</c>: names the state a value is in (<see cref="StateOf"/>)
	/// and every legal edge (<see cref="Edges"/>), so a test driver can check each edge is reachable.
	/// </summary>
	public Func<T, string>? StateOf { get; private set; }

	public IReadOnlyList<(string From, string To)>? Edges { get; private set; }

	internal Constraint<T> WithGuard(Func<T, bool> guard)
	{
		Guard = guard;
		return this;
	}

	internal Constraint<T> WithEdges(Func<T, string> stateOf, IReadOnlyList<(string From, string To)> edges)
	{
		StateOf = stateOf;
		Edges = edges;
		return this;
	}

	public Constraint<T> Named(string name)
	{
		Name = name;
		return this;
	}
}

// ── MonotonicDirection ────────────────────────────────────────────────────────

/// <summary>The one direction <see cref="Constraints{T}.Monotonic{TValue}"/> allows a selected value to move.</summary>
public enum MonotonicDirection
{
	NonDecreasing,
	NonIncreasing,
}

// ── Constraints<T> — static builder ────────────────────────────────────────────
// No base class. All fluent builder methods live here. Mirrors Nullean.Entities.Reactive's
// Constraints<TEvent> so the same vocabulary reads the same way on both sides of ToReactive().

#pragma warning disable CA1000
public static class Constraints<T>
{
	/// <summary>
	/// Violated whenever <paramref name="predicate"/> is false for the value being checked.
	/// <paramref name="message"/> is optional: omitted, the violation names the constraint itself
	/// (whatever <see cref="Constraint{T}.Named"/>/the generator's <c>(nameof(field), field)</c>
	/// gave it) and attaches the offending value as <see cref="Violation.Evidence"/> — pass
	/// <paramref name="message"/> for hand-written, user-facing prose instead.
	/// </summary>
	public static Constraint<T> Always(Func<T, bool> predicate, string? message = null)
	{
		Constraint<T> self = null!;
		self = new(
			ConstraintKind.PointInTime,
			(_, next) => predicate(next) ? [] : [Violation.Error(message ?? $"{self.Name ?? "Constraint"} was violated.") with { Evidence = next }]
		);
		return self;
	}

	/// <summary>
	/// Like <see cref="Always(Func{T, bool}, string)"/>, but the predicate also receives "now" from the
	/// <see cref="TimeProvider"/> the constraint is checked with (<see cref="ConstraintSet{T}.Validate"/>/
	/// <see cref="ConstraintSet{T}.Apply"/> take one; the default is <see cref="TimeProvider.System"/>).
	/// A test that passes a <c>FakeTimeProvider</c> controls the clock without touching the rule.
	/// </summary>
	public static Constraint<T> Always(Func<T, DateTimeOffset, bool> predicate, string? message = null)
	{
		Constraint<T> self = null!;
		self = new(ConstraintKind.PointInTime, (_, next, time) =>
		{
			var now = time.GetUtcNow();
			return predicate(next, now)
				? []
				: [Violation.Error(message ?? $"{self.Name ?? "Constraint"} was violated at {now:O}.") with { Evidence = (next, now) }];
		});
		return self;
	}

	/// <summary>
	/// Violated when <paramref name="selector"/>'s timestamp is later than "now" — an event cannot
	/// have happened in the future. <paramref name="tolerance"/> absorbs clock skew between machines.
	/// </summary>
	public static Constraint<T> NotInFuture(Func<T, DateTimeOffset> selector, TimeSpan tolerance = default, string? message = null)
	{
		Constraint<T> self = null!;
		self = new(ConstraintKind.PointInTime, (_, next, time) =>
		{
			var at = selector(next);
			var now = time.GetUtcNow();
			return at <= now + tolerance
				? []
				: [Violation.Error(message ?? $"{self.Name ?? "Constraint"}: {at:O} is in the future (now is {now:O}).") with { Evidence = (at, now) }];
		});
		return self;
	}

	/// <summary>
	/// Violated when <paramref name="selector"/>'s timestamp is more than <paramref name="maxAge"/>
	/// before "now" — freshness for a reading, token, quote or lock.
	/// </summary>
	public static Constraint<T> NotOlderThan(Func<T, DateTimeOffset> selector, TimeSpan maxAge, string? message = null)
	{
		Constraint<T> self = null!;
		self = new(ConstraintKind.PointInTime, (_, next, time) =>
		{
			var at = selector(next);
			var age = time.GetUtcNow() - at;
			return age <= maxAge
				? []
				: [
					Violation.Error(message ?? $"{self.Name ?? "Constraint"}: {at:O} is {age} old, older than the allowed {maxAge}.") with
					{
						Evidence = (at, age)
					}
				];
		});
		return self;
	}

	/// <summary>
	/// Full point-in-time check, for a rule that needs more than one fixed message — e.g. a
	/// message built from the value itself. <see cref="Always(Func{T, bool}, string)"/> is sugar over this for the common
	/// single-predicate, fixed-message case.
	/// </summary>
	public static Constraint<T> Check(Func<T, IEnumerable<Violation>> check) => new(ConstraintKind.PointInTime, (_, next) => check(next));

	/// <summary>
	/// Violated whenever <paramref name="isValid"/> is false for the (previous, next) pair produced
	/// by a mutation. Only <see cref="ConstraintSet{T}.Apply"/> runs these —
	/// <see cref="ConstraintSet{T}.Validate"/> has no previous value to offer a standalone instance.
	/// <paramref name="message"/> is optional — omitted, the default names both the constraint and
	/// the actual (previous, next) pair that broke it.
	/// </summary>
	public static Constraint<T> Transitions(Func<T, T, bool> isValid, string? message = null)
	{
		Constraint<T> self = null!;
		self = new(
			ConstraintKind.Sequential,
			(prev, next) =>
				isValid(prev, next)
					? []
					: [
						Violation.Error(message ?? $"{self.Name ?? "Constraint"} does not allow the transition from {prev} to {next}.") with
						{
							Evidence = (prev, next)
						}
					]
		);
		return self;
	}

	/// <summary>Full sequential check over the (previous, next) pair — see <see cref="Transitions"/>.</summary>
	public static Constraint<T> CheckTransition(Func<T, T, IEnumerable<Violation>> check) => new(ConstraintKind.Sequential, check);

	/// <summary>
	/// Starts a conditional constraint: <c>When(guard).Requires(requirement)</c> is violated when
	/// the next value satisfies <paramref name="guard"/> but fails <c>requirement</c>. Point-in-time
	/// — for a requirement that needs the previous value too, use <see cref="Transitions"/> instead.
	/// </summary>
	public static WhenBuilder<T> When(Func<T, bool> guard) => new(guard);

	/// <summary>
	/// Starts a declarative transition allow-list: <c>Transitions(selector).Allow(a, b).Allow(c, d)</c>
	/// is violated when <paramref name="selector"/>'s (previous, next) pair isn't one of the allowed
	/// edges — one edge per line, instead of hand-writing a tuple pattern-match over
	/// <see cref="Transitions(Func{T, T, bool}, string)"/> and risking a missed or backwards case.
	/// The right choice whenever the thing moving through states is a single comparable value (an
	/// enum, usually); reach for the predicate-based overload instead when the legality of a
	/// transition depends on more than just the two state values (e.g. a quantity carried on the
	/// event).
	/// </summary>
	public static TransitionBuilder<T, TState> Transitions<TState>(Func<T, TState> selector) => new(selector);

	/// <summary>
	/// Violated whenever applying <paramref name="action"/> twice from the value being checked
	/// gives a different result than applying it once — the standard <c>f(f(x)) == f(x)</c> law.
	/// Point-in-time: it only needs the value being checked, never a previous one, since
	/// <paramref name="action"/> itself supplies both "once" and "twice". Naming the quality
	/// instead of hand-rolling it as an <see cref="Always(Func{T, bool}, string)"/> predicate means a failure reads
	/// "not idempotent," not the name of a rule that looks unrelated to what actually broke.
	/// <paramref name="message"/> is optional, same as <see cref="Always(Func{T, bool}, string)"/>.
	/// </summary>
	public static Constraint<T> Idempotent(Func<T, T> action, string? message = null)
	{
		Constraint<T> self = null!;
		self = new(ConstraintKind.PointInTime, (_, next) =>
		{
			var once = action(next);
			var twice = action(once);
			return EqualityComparer<T>.Default.Equals(once, twice)
				? []
				: [Violation.Error(message ?? $"{self.Name ?? "Constraint"} is not idempotent.") with { Evidence = (once, twice) }];
		});
		return self;
	}

	/// <summary>
	/// Violated whenever <paramref name="selector"/>'s value moves the wrong way across a
	/// mutation, per <paramref name="direction"/>. Sequential, like <see cref="Transitions"/> — it
	/// only fires from <see cref="ConstraintSet{T}.Apply"/>, never <see cref="ConstraintSet{T}.Validate"/>,
	/// because "did it move the wrong way" is meaningless without a previous value. This is also
	/// why it is step-scoped rather than global by construction: attach it only to the
	/// <c>Constraint&lt;T&gt;</c> set backing the specific script steps it is supposed to hold for
	/// (e.g. <c>AddLine</c>), not the type's whole <c>Constraints</c> — a step that is allowed to
	/// shrink the value (<c>RemoveLine</c>) would otherwise wrongly fail it.
	/// </summary>
	public static Constraint<T> Monotonic<TValue>(Func<T, TValue> selector, MonotonicDirection direction, string? message = null) =>
		new(ConstraintKind.Sequential, (prev, next) =>
		{
			var before = selector(prev);
			var after = selector(next);
			var comparison = Comparer<TValue>.Default.Compare(after, before);
			var holds = direction == MonotonicDirection.NonDecreasing ? comparison >= 0 : comparison <= 0;
			return holds ? [] : [Violation.Error(message ?? $"Expected {direction} but moved from {before} to {after}.") with { Evidence = (before, after) }];
		});

	/// <summary>
	/// Violated whenever <paramref name="selector"/>'s value changes across a mutation — a
	/// quantity that a transition must neither create nor destroy. Sequential, the same as
	/// <see cref="Monotonic{TValue}"/>, but by equality instead of ordering. The first shape here
	/// that is naturally exercised with a pair-valued <typeparamref name="T"/> (e.g.
	/// <c>(Order From, Order To)</c>, checked via <c>Constraints&lt;(Order, Order)&gt;</c>) rather
	/// than a single entity, since "conserved" usually means "conserved across two halves of a
	/// transfer," not just across one type's own history — but nothing here requires that; any
	/// <typeparamref name="T"/> with a quantity that must round-trip a mutation unchanged applies.
	/// <paramref name="message"/> is optional, same as <see cref="Always(Func{T, bool}, string)"/>/<see cref="Monotonic{TValue}"/>.
	/// </summary>
	public static Constraint<T> Conserves<TValue>(Func<T, TValue> selector, string? message = null)
	{
		Constraint<T> self = null!;
		self = new(ConstraintKind.Sequential, (prev, next) =>
		{
			var before = selector(prev);
			var after = selector(next);
			return EqualityComparer<TValue>.Default.Equals(before, after)
				? []
				: [
					Violation.Error(message ?? $"{self.Name ?? "Constraint"} did not conserve its value (was {before}, became {after}).") with
					{
						Evidence = (before, after)
					}
				];
		});
		return self;
	}
}
#pragma warning restore CA1000

public sealed class WhenBuilder<T>
{
	private readonly Func<T, bool> _guard;

	internal WhenBuilder(Func<T, bool> guard) => _guard = guard;

	/// <summary><paramref name="message"/> is optional, same as <see cref="Constraints{T}.Always(Func{T, bool}, string)"/>.</summary>
	public Constraint<T> Requires(Func<T, bool> requirement, string? message = null)
	{
		Constraint<T> self = null!;
		self = new(
			ConstraintKind.PointInTime,
			(_, next) =>
				!_guard(next) || requirement(next) ? [] : [Violation.Error(message ?? $"{self.Name ?? "Constraint"} was violated.") with { Evidence = next }]
		);
		return self.WithGuard(_guard);
	}
}

// ── TransitionBuilder<T, TState> ───────────────────────────────────────────────
// Declarative counterpart to Constraints<T>.Transitions(Func<T, T, bool>) — see that overload's
// own doc comment for when to reach for which.

/// <summary>
/// Built only through <see cref="Constraints{T}.Transitions{TState}"/>. Implicitly converts to
/// <see cref="Constraint{T}"/>, so it never needs an explicit terminal call — assigning it (or
/// handing it to <see cref="ConstraintSet.From{T}"/>) is enough, the same as every other
/// <c>Constraints&lt;T&gt;</c> builder result.
/// </summary>
public sealed class TransitionBuilder<T, TState>
{
	private readonly Func<T, TState> _selector;
	private readonly List<(TState From, TState To)> _allowed = [];

	internal TransitionBuilder(Func<T, TState> selector) => _selector = selector;

	/// <summary>Adds one allowed edge. Call once per legal transition; order doesn't matter.</summary>
	public TransitionBuilder<T, TState> Allow(TState from, TState to)
	{
		_allowed.Add((from, to));
		return this;
	}

#pragma warning disable CA1000
	public static implicit operator Constraint<T>(TransitionBuilder<T, TState> builder) => builder.Build();
#pragma warning restore CA1000

	private Constraint<T> Build()
	{
		var selector = _selector;
		var allowed = _allowed.ToArray();
		Constraint<T> self = null!;
		self = new(ConstraintKind.Sequential, (prev, next) =>
		{
			var from = selector(prev);
			var to = selector(next);
			// Staying in the same state is not a transition: a mutation that changes some other field
			// (AddLine on a Pending order) must not be rejected for leaving the status alone.
			if (EqualityComparer<TState>.Default.Equals(from, to))
			{
				return [];
			}
			return allowed.Any(edge => EqualityComparer<TState>.Default.Equals(edge.From, from) && EqualityComparer<TState>.Default.Equals(edge.To, to))
				? []
				: [Violation.Error($"{self.Name ?? "Constraint"} does not allow the transition from {from} to {to}.") with { Evidence = (from, to) }];
		});
		return self.WithEdges(v => $"{selector(v)}", [.. allowed.Select(e => ($"{e.From}", $"{e.To}"))]);
	}
}

// ── [Constraint] attribute ────────────────────────────────────────────────────
// Mark a static Constraint<T>/ReactiveConstraint<T> field or property on a partial type.
// Nullean.Entities.SourceGenerators bundles every marked member on that type into a
// ConstraintSet<T>/ReactiveConstraintSet<T> Constraints property, so the set is always
// exhaustive — it cannot drift from the members actually declared. If the type IS the T
// (a partial record/class/struct attaching its own constraints), the generator also adds
// IHaveConstraints<T> to the partial declaration; a separate "spec" companion class marking
// someone else's T gets the property without the interface. See
// src/Nullean.Entities.SourceGenerators/README.md for the full set of rules and diagnostics.

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ConstraintAttribute : Attribute
{
}

// ── [Constraints] attribute ───────────────────────────────────────────────────
// Mark a partial type itself, instead of tagging every member one at a time: the generator
// bundles every static Constraint<T>/ReactiveConstraint<TEvent> field or property declared
// directly on the type, with no [Constraint] needed on any of them. The two opt-in styles are
// mutually exclusive per type in effect, not merely in convention: a type carrying [Constraints]
// is read in auto-discovery mode, so any [Constraint] tags its own members also happen to carry
// become redundant rather than additive — they are not double-counted, since auto-discovery
// already finds every eligible member whether or not it is individually tagged. Reach for
// [Constraint] instead when a type has a static Constraint<T>/ReactiveConstraint<TEvent> member
// that must NOT be in the generated set (e.g. a private building block combined into a public
// rule rather than standing on its own) and tagging everything else individually is preferable to
// adding [ConstraintIgnore] to the one exception.

/// <summary>
/// Marks a partial type for automatic constraint discovery: every static
/// <see cref="Constraint{T}"/>/<c>ReactiveConstraint&lt;TEvent&gt;</c> field or property declared
/// directly on this type is bundled into the generated <c>All</c> set, with no need to tag
/// each one with <see cref="ConstraintAttribute"/> individually. Mark a member that should be
/// excluded despite matching with <see cref="ConstraintIgnoreAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ConstraintsAttribute : Attribute
{
}

/// <summary>
/// Excludes a static <see cref="Constraint{T}"/>/<c>ReactiveConstraint&lt;TEvent&gt;</c> member
/// from a <see cref="ConstraintsAttribute"/>-tagged type's automatic discovery. Has no effect on a
/// type using the per-member <see cref="ConstraintAttribute"/> opt-in instead — there, simply not
/// tagging the member already excludes it.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
public sealed class ConstraintIgnoreAttribute : Attribute
{
}

// ── IHaveConstraints<TSelf> ───────────────────────────────────────────────────
// C# 11 static abstract interface member. Enables generic validation/attachment:
// where T : IHaveConstraints<T>

public interface IHaveConstraints<TSelf> where TSelf : IHaveConstraints<TSelf>
{
	static abstract ConstraintSet<TSelf> Constraints { get; }

	/// <summary>The entity's mutations (generated; see <see cref="EntityActions{TEntity}"/>), or null when there are none.</summary>
	static virtual EntityActions<TSelf>? Actions => null;
}

// ── IConstraintsFor<T> ────────────────────────────────────────────────────────
// The companion-class counterpart to IHaveConstraints<TSelf> — but a plain instance interface, not
// F-bounded and not static-abstract. Two reasons:
//   - The type declaring the constraints (a "spec" class) is deliberately not the type they
//     constrain — T may not even be yours to add an interface to, so F-bounding on it makes no sense.
//   - An instance member (rather than static-abstract) means a value implementing this interface is
//     ordinary data: it can be a method parameter, a collection element, or resolved from DI, and a
//     generic caller can infer T from the value instead of spelling out two type parameters
//     (`Foo<TSpec, T>() where TSpec : ...` vs. just `Foo<T>(IConstraintsFor<T> spec)`).
// [Constraint] generates a singleton `TypeConstraints.Instance` nested inside the spec class to hand
// out — see the generator's own comment for why that's nested rather than on the spec class itself —
// plus a forwarding `SpecClass.Instance` shortcut on the outer class, so a caller never has to spell
// out `TypeConstraints`.

public interface IConstraintsFor<T>
{
	ConstraintSet<T> All { get; }

	/// <summary>The entity's mutations (generated; see <see cref="EntityActions{TEntity}"/>), or null when there are none.</summary>
	EntityActions<T>? Actions => null;
}

// ── ConstraintSet<T> ──────────────────────────────────────────────────────────
// A named, ordered collection of Constraint<T>s.

public sealed class ConstraintSet<T>
{
	private readonly IReadOnlyList<Constraint<T>> _constraints;

	internal ConstraintSet(IReadOnlyList<Constraint<T>> constraints) => _constraints = constraints;

	/// <summary>
	/// The constraints in this set, in declaration order. Lets other layers (e.g.
	/// Nullean.Entities.Reactive's <c>ToReactive</c>) lift this set onto a different
	/// execution model without this package depending on that model.
	/// </summary>
	public IReadOnlyList<Constraint<T>> AsConstraints() => _constraints;

	/// <summary>
	/// Runs every <see cref="ConstraintKind.PointInTime"/> constraint over the single
	/// <paramref name="instance"/>. <see cref="ConstraintKind.Sequential"/> constraints are
	/// skipped — a standalone instance has no "previous" to compare against; see <see cref="Apply"/>.
	/// </summary>
	public IReadOnlyList<Violation> Validate(T instance, TimeProvider? time = null)
	{
		time ??= TimeProvider.System;
		return [
			.. _constraints.Where(c => c.Kind == ConstraintKind.PointInTime).SelectMany(
				c => c.CheckAt(instance, instance, time).Select(v => v with { ConstraintId = c.Name })
			),
		];
	}

	/// <summary>
	/// Runs a <paramref name="mutation"/> and checks every constraint over the result —
	/// <see cref="ConstraintKind.PointInTime"/> constraints against the result alone,
	/// <see cref="ConstraintKind.Sequential"/> ones against the (<paramref name="current"/>, result)
	/// pair. Returns <see cref="Result{T}.Ok"/> when all constraints pass (a mutation that changes
	/// nothing is also <c>Ok</c> — use <see cref="ApplyDetailed"/> to tell the two apart) or
	/// <see cref="Result{T}.Fail"/> when any are violated.
	/// </summary>
	public Result<T> Apply(T current, Func<T, T> mutation, TimeProvider? time = null) => ApplyDetailed(current, mutation, time) switch
	{
		ApplyOutcome<T>.Applied a => new Result<T>.Ok(a.Value),
		ApplyOutcome<T>.Noop n => new Result<T>.Ok(n.Value),
		ApplyOutcome<T>.Failure f => new Result<T>.Fail(f.Attempted, f.Violations),
		_ => throw new UnreachableException(),
	};

	/// <summary>
	/// Like <see cref="Apply"/>, but distinguishes a mutation that produced the same value
	/// (<see cref="ApplyOutcome{T}.Noop"/> — no constraints are run) from one that actually changed
	/// it (<see cref="ApplyOutcome{T}.Applied"/>), and carries the original value on
	/// <see cref="ApplyOutcome{T}.Failure"/>.
	/// </summary>
	public ApplyOutcome<T> ApplyDetailed(T current, Func<T, T> mutation, TimeProvider? time = null)
	{
		var next = mutation(current);
		if (EqualityComparer<T>.Default.Equals(current, next))
			return new ApplyOutcome<T>.Noop(current);
		var violations = RunAll(current, next, time);
		return violations.Count == 0 ? new ApplyOutcome<T>.Applied(next) : new ApplyOutcome<T>.Failure(current, next, violations);
	}

	/// <summary>Like <see cref="ApplyDetailed"/>, but throws on <see cref="ApplyOutcome{T}.Failure"/>. Returns the new value for both <see cref="ApplyOutcome{T}.Applied"/> and <see cref="ApplyOutcome{T}.Noop"/>.</summary>
	public T ApplyOrThrow(T current, Func<T, T> mutation, TimeProvider? time = null) => ApplyDetailed(current, mutation, time) switch
	{
		ApplyOutcome<T>.Applied a => a.Value,
		ApplyOutcome<T>.Noop n => n.Value,
		ApplyOutcome<T>.Failure f => throw new InvalidOperationException($"Apply failed: {string.Join(", ", f.Violations.Select(v => v.Message))}"),
		_ => throw new UnreachableException(),
	};

	// Called by ConstrainedValue<T>.Apply after it has already computed `next` to avoid calling
	// the mutation twice — the channel needs the value upfront to short-circuit on equality.
	internal IReadOnlyList<Violation> RunAll(T current, T next, TimeProvider? time)
	{
		time ??= TimeProvider.System;
		return [.. _constraints.SelectMany(c => c.CheckAt(current, next, time).Select(v => v with { ConstraintId = c.Name })),];
	}
}

public static class ConstraintSet
{
	/// <summary>
	/// Names every constraint from its tuple (mirroring Nullean.Entities.Reactive's
	/// <c>ReactiveConstraintSet.From</c> — [Constraint] source gen passes (nameof(field), field),
	/// so a set's names can never drift from the fields actually declared).
	/// </summary>
	public static ConstraintSet<T> From<T>(params (string Name, Constraint<T> Constraint)[] constraints)
	{
		foreach (var (name, constraint) in constraints)
		{
			constraint.Named(name);
		}
		return new([.. constraints.Select(c => c.Constraint)]);
	}
}

// ── ConstrainedValue<T> ──────────────────────────────────────────────────────
// A channel that holds a current value and enforces a constraint set on each mutation.

public sealed class ConstrainedValue<T>
{
	private readonly ConstraintSet<T> _constraints;
	private readonly TimeProvider? _time;

	public T Current { get; private set; }

	internal ConstrainedValue(T initial, ConstraintSet<T> constraints, TimeProvider? time)
	{
		Current = initial;
		_constraints = constraints;
		_time = time;
	}

	/// <summary>
	/// Applies <paramref name="mutation"/> if every constraint passes, updating <see cref="Current"/>;
	/// otherwise returns <see cref="Result{T}.Fail"/> and leaves it unchanged. A mutation that
	/// changes nothing is <c>Ok</c> — see <see cref="ApplyDetailed"/> to detect it.
	/// </summary>
	public Result<T> Apply(Func<T, T> mutation) => ApplyDetailed(mutation) switch
	{
		ApplyOutcome<T>.Applied a => new Result<T>.Ok(a.Value),
		ApplyOutcome<T>.Noop n => new Result<T>.Ok(n.Value),
		ApplyOutcome<T>.Failure f => new Result<T>.Fail(f.Attempted, f.Violations),
		_ => throw new UnreachableException(),
	};

	/// <summary>
	/// Like <see cref="Apply"/>, but reports whether the value actually
	/// changed; otherwise returns <see cref="ApplyOutcome{T}.Noop"/> or
	/// <see cref="ApplyOutcome{T}.Failure"/> without updating <see cref="Current"/>.
	/// </summary>
	public ApplyOutcome<T> ApplyDetailed(Func<T, T> mutation)
	{
		var next = mutation(Current);
		if (EqualityComparer<T>.Default.Equals(Current, next))
		{
			return new ApplyOutcome<T>.Noop(Current);
		}
		var violations = _constraints.RunAll(Current, next, _time);
		if (violations.Count == 0)
		{
			Current = next;
			return new ApplyOutcome<T>.Applied(next);
		}
		return new ApplyOutcome<T>.Failure(Current, next, violations);
	}

	/// <summary>
	/// Like <see cref="ApplyDetailed"/>, but throws on <see cref="ApplyOutcome{T}.Failure"/>.
	/// Returns the current value for both <see cref="ApplyOutcome{T}.Applied"/> and
	/// <see cref="ApplyOutcome{T}.Noop"/> — a no-op is not an error.
	/// </summary>
	public T ApplyOrThrow(Func<T, T> mutation) => ApplyDetailed(mutation) switch
	{
		ApplyOutcome<T>.Applied a => a.Value,
		ApplyOutcome<T>.Noop n => n.Value,
		ApplyOutcome<T>.Failure f => throw new InvalidOperationException($"Apply failed: {string.Join(", ", f.Violations.Select(v => v.Message))}"),
		_ => throw new UnreachableException(),
	};
}

public static class ConstrainedValue
{
	public static ConstrainedValueBuilder<T> For<T>(T initial) => new(initial);
}

/// <summary>Intermediate builder from <see cref="ConstrainedValue.For{T}"/> — call <see cref="Enforce"/> to finish.</summary>
public readonly struct ConstrainedValueBuilder<T>
{
	private readonly T _initial;

	internal ConstrainedValueBuilder(T initial) => _initial = initial;

	/// <param name="constraints">The set every mutation is checked against.</param>
	/// <param name="time">The clock time-aware constraints read; defaults to <see cref="TimeProvider.System"/>.</param>
	public ConstrainedValue<T> Enforce(ConstraintSet<T> constraints, TimeProvider? time = null) => new(_initial, constraints, time);
}
