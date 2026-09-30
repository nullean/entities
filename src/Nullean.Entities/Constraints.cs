// Nullean.Entities — synchronous domain constraints.
//
// Constraint<T> is a named, synchronous rule: given an instance, produce zero or more
// Violations. ConstraintSet<T> bundles named constraints and runs them over a single
// instance (Validate) or over a proposed mutation (Apply), returning a Result<T>.
//
// No CsCheck, no System.Reactive dependency here — this is the plain, host-agnostic
// core. Nullean.Entities.Reactive lifts a ConstraintSet<T> onto IObservable<T> streams;
// Nullean.Entities.CsCheck drives it from property-test scripts.
//
// No doc comments yet (checkpoint-A exploration code) — CS1591 is suppressed project-wide
// in Directory.Build.props rather than per file.

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
	public sealed record Ok(T Value) : Result<T>;

	public sealed record Fail(T Attempted, IReadOnlyList<Violation> Violations) : Result<T>;

	public TOut Match<TOut>(Func<T, TOut> ok, Func<T, IReadOnlyList<Violation>, TOut> fail) => this switch
	{
		Ok o => ok(o.Value),
		Fail f => fail(f.Attempted, f.Violations),
		_ => throw new InvalidOperationException("Unknown Result variant"),
	};

	public T Unwrap() => this switch
	{
		Ok o => o.Value,
		Fail f => throw new InvalidOperationException($"Result is Fail: {string.Join(", ", f.Violations.Select(v => v.Message))}"),
		_ => throw new InvalidOperationException("Unknown Result variant"),
	};
}

// ── Constraint<T> ─────────────────────────────────────────────────────────────

/// <summary>
/// A named, synchronous rule over <typeparamref name="T"/>. Lives anywhere — static
/// field on a record, local variable, constructor parameter.
/// </summary>
public sealed record Constraint<T>(string Id, string Description, Func<T, IEnumerable<Violation>> Check);

public static class ConstraintExtensions
{
	public static IReadOnlyList<Violation> CheckAll<T>(this IEnumerable<Constraint<T>> constraints, T model) =>
		[.. constraints.SelectMany(c => c.Check(model).Select(v => v with { ConstraintId = c.Id }))];
}

// ── [Constraint] attribute ────────────────────────────────────────────────────
// Mark a static Constraint<T> field on a partial record. A future source generator
// bundles all marked fields into a ConstraintSet<T> Constraints property and adds
// IHaveConstraints<T> to the partial record declaration, so the set is always
// exhaustive — it cannot drift from the fields actually declared.

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

	/// <summary>Run every constraint synchronously over the single <paramref name="instance"/>.</summary>
	public IReadOnlyList<Violation> Validate(T instance) => _constraints.CheckAll(instance);

	/// <summary>
	/// Apply a <paramref name="mutation"/> and run every constraint synchronously over the
	/// resulting value. If all pass, returns <see cref="Result{T}.Ok"/> with the new value;
	/// otherwise <see cref="Result{T}.Fail"/> with the list of violations. <paramref name="current"/>
	/// is not itself re-validated — only the proposed next value is.
	/// </summary>
	public Result<T> Apply(T current, Func<T, T> mutation)
	{
		var next = mutation(current);
		var violations = Validate(next);
		return violations.Count == 0 ? new Result<T>.Ok(next) : new Result<T>.Fail(next, violations);
	}

	/// <summary>Like <see cref="Apply"/>, but throws on the first violated constraint.</summary>
	public T ApplyOrThrow(T current, Func<T, T> mutation) => Apply(current, mutation) switch
	{
		Result<T>.Ok ok => ok.Value,
		Result<T>.Fail f => throw new InvalidOperationException($"Constraint violated: {string.Join(", ", f.Violations.Select(v => v.Message))}"),
		var r => throw new InvalidOperationException($"Unknown Result variant: {r}"),
	};
}

public static class ConstraintSet
{
	public static ConstraintSet<T> From<T>(params Constraint<T>[] constraints) => new(constraints);

	public static ConstraintSet<T> From<T>(params (string Id, string Description, Func<T, IEnumerable<Violation>> Check)[] constraints) =>
		new([.. constraints.Select(c => new Constraint<T>(c.Id, c.Description, c.Check))]);
}

// ── ConstraintChannel<T> ──────────────────────────────────────────────────────
// A channel that holds a current value and enforces a constraint set on each mutation.

public sealed class ConstraintChannel<T>
{
	private readonly ConstraintSet<T> _constraints;

	public T Current { get; private set; }

	public ConstraintChannel(T initial, ConstraintSet<T> constraints)
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
}

public static class ConstraintChannel
{
	public static ConstraintChannel<T> Enforce<T>(T initial, ConstraintSet<T> constraints) => new(initial, constraints);
}
