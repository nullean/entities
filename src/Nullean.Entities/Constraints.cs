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
	private protected Result()
	{ }

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

	internal Constraint(ConstraintKind kind, Func<T, T, IEnumerable<Violation>> check)
	{
		Kind = kind;
		Check = check;
	}

	public Constraint<T> Named(string name)
	{
		Name = name;
		return this;
	}
}

// ── Constraints<T> — static builder ────────────────────────────────────────────
// No base class. All fluent builder methods live here. Mirrors Nullean.Entities.Reactive's
// Constraints<TEvent> so the same vocabulary reads the same way on both sides of ToReactive().

#pragma warning disable CA1000
public static class Constraints<T>
{
	/// <summary>Violated whenever <paramref name="predicate"/> is false for the value being checked.</summary>
	public static Constraint<T> Always(Func<T, bool> predicate, string message) =>
		new(ConstraintKind.PointInTime, (_, next) => predicate(next) ? [] : [Violation.Error(message)]);

	/// <summary>
	/// Full point-in-time check, for a rule that needs more than one fixed message — e.g. a
	/// message built from the value itself. <see cref="Always"/> is sugar over this for the common
	/// single-predicate, fixed-message case.
	/// </summary>
	public static Constraint<T> Check(Func<T, IEnumerable<Violation>> check) => new(ConstraintKind.PointInTime, (_, next) => check(next));

	/// <summary>
	/// Violated whenever <paramref name="isValid"/> is false for the (previous, next) pair produced
	/// by a mutation. Only <see cref="ConstraintSet{T}.Apply"/> runs these —
	/// <see cref="ConstraintSet{T}.Validate"/> has no previous value to offer a standalone instance.
	/// </summary>
	public static Constraint<T> Transitions(Func<T, T, bool> isValid, string message) =>
		new(ConstraintKind.Sequential, (prev, next) => isValid(prev, next) ? [] : [Violation.Error(message)]);

	/// <summary>Full sequential check over the (previous, next) pair — see <see cref="Transitions"/>.</summary>
	public static Constraint<T> CheckTransition(Func<T, T, IEnumerable<Violation>> check) => new(ConstraintKind.Sequential, check);

	/// <summary>
	/// Starts a conditional constraint: <c>When(guard).Requires(requirement)</c> is violated when
	/// the next value satisfies <paramref name="guard"/> but fails <c>requirement</c>. Point-in-time
	/// — for a requirement that needs the previous value too, use <see cref="Transitions"/> instead.
	/// </summary>
	public static WhenBuilder<T> When(Func<T, bool> guard) => new(guard);
}
#pragma warning restore CA1000

public sealed class WhenBuilder<T>
{
	private readonly Func<T, bool> _guard;

	internal WhenBuilder(Func<T, bool> guard) => _guard = guard;

	public Constraint<T> Requires(Func<T, bool> requirement, string message) =>
		new(ConstraintKind.PointInTime, (_, next) => !_guard(next) || requirement(next) ? [] : [Violation.Error(message)]);
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

// ── IHaveConstraints<TSelf> ───────────────────────────────────────────────────
// C# 11 static abstract interface member. Enables generic validation/attachment:
// where T : IHaveConstraints<T>

public interface IHaveConstraints<TSelf> where TSelf : IHaveConstraints<TSelf>
{
	static abstract ConstraintSet<TSelf> Constraints { get; }
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
	public IReadOnlyList<Violation> Validate(T instance) =>
		[
			.. _constraints.Where(c => c.Kind == ConstraintKind.PointInTime).SelectMany(
				c => c.Check(instance, instance).Select(v => v with { ConstraintId = c.Name })
			),
		];

	/// <summary>
	/// Runs a <paramref name="mutation"/> and checks every constraint over the result —
	/// <see cref="ConstraintKind.PointInTime"/> constraints against the result alone,
	/// <see cref="ConstraintKind.Sequential"/> ones against the (<paramref name="current"/>, result)
	/// pair. If all pass, returns <see cref="Result{T}.Ok"/> with the new value; otherwise
	/// <see cref="Result{T}.Fail"/> with every violation.
	/// </summary>
	public Result<T> Apply(T current, Func<T, T> mutation)
	{
		var next = mutation(current);
		var violations = _constraints.SelectMany(c => c.Check(current, next).Select(v => v with { ConstraintId = c.Name })).ToList();
		return violations.Count == 0 ? new Result<T>.Ok(next) : new Result<T>.Fail(next, violations);
	}

	/// <summary>Like <see cref="Apply"/>, but throws on the first violated constraint.</summary>
	public T ApplyOrThrow(T current, Func<T, T> mutation) => Apply(current, mutation).Unwrap();
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

// ── ConstraintChannel<T> ──────────────────────────────────────────────────────
// A channel that holds a current value and enforces a constraint set on each mutation.

public sealed class ConstraintChannel<T>
{
	private readonly ConstraintSet<T> _constraints;

	public T Current { get; private set; }

	internal ConstraintChannel(T initial, ConstraintSet<T> constraints)
	{
		Current = initial;
		_constraints = constraints;
	}

	/// <summary>
	/// Apply <paramref name="mutation"/> if every constraint passes; otherwise return the
	/// violations without updating <see cref="Current"/>.
	/// </summary>
	public Result<T> Apply(Func<T, T> mutation)
	{
		var result = _constraints.Apply(Current, mutation);
		if (result is Result<T>.Ok ok)
		{
			Current = ok.Value;
		}
		return result;
	}

	/// <summary>Like <see cref="Apply"/>, but throws on the first violated constraint.</summary>
	public T ApplyOrThrow(Func<T, T> mutation) => Apply(mutation).Unwrap();
}

public static class ConstraintChannel
{
	public static ConstraintChannelBuilder<T> For<T>(T initial) => new(initial);
}

/// <summary>Intermediate builder from <see cref="ConstraintChannel.For{T}"/> — call <see cref="Enforce"/> to finish.</summary>
public readonly struct ConstraintChannelBuilder<T>
{
	private readonly T _initial;

	internal ConstraintChannelBuilder(T initial) => _initial = initial;

	public ConstraintChannel<T> Enforce(ConstraintSet<T> constraints) => new(_initial, constraints);
}
