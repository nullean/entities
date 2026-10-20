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
using System.Runtime.CompilerServices;

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

	/// <summary>
	/// The member the violation is about (<c>"Email"</c>, <c>"Address.City"</c>), when the rule is about one: what a validation response (ProblemDetails
	/// <c>errors</c>, ModelState) is keyed by. Filled by the selector overloads of <c>Always</c>, <c>Unique</c>, <c>References</c>, <c>Acyclic</c> and <c>NoChains</c>.
	/// For the rules over rows it is the text of the collection selector (<c>"Orders"</c>), not the key: the key that broke the rule is <see cref="Evidence"/>.
	/// </summary>
	public string? Member { get; init; }

	public int? Line { get; init; }
	public int? Column { get; init; }
	public int? Length { get; init; }
	public string? File { get; init; }

	/// <summary>The same violation, pointing at <paramref name="location"/> (a null location changes nothing).</summary>
	public Violation At(SourceLocation? location) =>
		location is null ? this : this with { File = location.File, Line = location.Line, Column = location.Column, Length = location.Length };

	public static Violation Error(string message) => new(message);
	public static Violation Error(string member, string message) => new(message) { Member = member };
	public static Violation Warning(string message) => new(message, Severity.Warning);
	public static Violation Hint(string message) => new(message, Severity.Hint);
}

/// <summary>Where in a source file a rule's subject came from (a YAML line, a TOC entry), for findings that should point back at it.</summary>
public sealed record SourceLocation(string? File, int? Line = null, int? Column = null, int? Length = null);

// ── ConstraintViolationException ──────────────────────────────────────────────

/// <summary>
/// What <c>ApplyOrThrow</c> and <c>Result.Unwrap</c> throw: the violations themselves, not only their messages joined into one string, so an
/// application boundary can turn them into a validation response (see <see cref="Violation.Member"/>).
/// </summary>
public sealed class ConstraintViolationException : InvalidOperationException
{
	public ConstraintViolationException(IReadOnlyList<Violation> violations) : this(
			violations,
			$"Constraints violated: {string.Join(", ", violations.Select(v => v.Message))}"
		)
	{ }

	public ConstraintViolationException(IReadOnlyList<Violation> violations, string message) : base(message) => Violations = violations;

	public ConstraintViolationException() : this([]) { }

	public ConstraintViolationException(string message) : this([], message) { }

	public ConstraintViolationException(string message, Exception innerException) : base(message, innerException) => Violations = [];

	public IReadOnlyList<Violation> Violations { get; }
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
		Fail f => throw new ConstraintViolationException(f.Violations, $"Result is Fail: {string.Join(", ", f.Violations.Select(v => v.Message))}"),
		_ => throw new UnreachableException(),
	};

	// A failure carried across a change of type has nothing of the new type that was "attempted", so Attempted is default there;
	// the violations are what carry over.

	/// <summary>Transforms the value of an <see cref="Ok"/>; a <see cref="Fail"/> keeps its violations (its <c>Attempted</c> is default, there being no attempted <typeparamref name="TOut"/>).</summary>
	public Result<TOut> Map<TOut>(Func<T, TOut> map) => this switch
	{
		Ok o => new Result<TOut>.Ok(map(o.Value)),
		Fail f => new Result<TOut>.Fail(default!, f.Violations),
		_ => throw new UnreachableException(),
	};

	/// <summary>Continues with a step that can fail itself. Stops at the first <see cref="Fail"/>; to gather the violations of independent results use <see cref="Result.Combine{T1, T2, TOut}"/>.</summary>
	public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> next) => this switch
	{
		Ok o => next(o.Value),
		Fail f => new Result<TOut>.Fail(default!, f.Violations),
		_ => throw new UnreachableException(),
	};

	/// <summary>Query syntax: <c>from a in x select f(a)</c>. Same as <see cref="Map{TOut}"/>.</summary>
	public Result<TOut> Select<TOut>(Func<T, TOut> map) => Map(map);

	/// <summary>Query syntax: <c>from a in x from b in y(a) select f(a, b)</c>. Stops at the first failure, like <see cref="Bind{TOut}"/>.</summary>
	public Result<TOut> SelectMany<TNext, TOut>(Func<T, Result<TNext>> next, Func<T, TNext, TOut> project) =>
		Bind(a => next(a).Map(b => project(a, b)));

	/// <summary>The value, or <paramref name="fallback"/> when this is a <see cref="Fail"/>.</summary>
	public T GetValueOrDefault(T fallback) => this is Ok o ? o.Value : fallback;

	/// <summary>The violations of a <see cref="Fail"/>; empty for an <see cref="Ok"/>.</summary>
	public IReadOnlyList<Violation> ViolationsOrEmpty => this is Fail f ? f.Violations : [];
}

/// <summary>Builds and combines <see cref="Result{T}"/>s.</summary>
public static class Result
{
	public static Result<T> Ok<T>(T value) => new Result<T>.Ok(value);

	/// <summary>Combines independent results: when several fail, <b>every</b> violation is kept (a <c>from ... from ...</c> query would stop at the first), so a form or a constructor reports all that is wrong at once.</summary>
	public static Result<TOut> Combine<T1, T2, TOut>(Result<T1> first, Result<T2> second, Func<T1, T2, TOut> combine) => (first, second) switch
	{
		(Result<T1>.Ok a, Result<T2>.Ok b) => new Result<TOut>.Ok(combine(a.Value, b.Value)),
		_ => new Result<TOut>.Fail(default!, [.. first.ViolationsOrEmpty, .. second.ViolationsOrEmpty]),
	};

	/// <inheritdoc cref="Combine{T1, T2, TOut}"/>
	public static Result<TOut> Combine<T1, T2, T3, TOut>(
		Result<T1> first,
		Result<T2> second,
		Result<T3> third,
		Func<T1, T2, T3, TOut> combine
	) => (first, second, third) switch
	{
		(Result<T1>.Ok a, Result<T2>.Ok b, Result<T3>.Ok c) => new Result<TOut>.Ok(combine(a.Value, b.Value, c.Value)),
		_ => new Result<TOut>.Fail(default!, [.. first.ViolationsOrEmpty, .. second.ViolationsOrEmpty, .. third.ViolationsOrEmpty]),
	};
}

// ── ApplyOutcome<T> ───────────────────────────────────────────────────────────
// The three-way outcome of ApplyDetailed on ConstraintSet<T> / ConstrainedValue<T>. Carries T in every case so
// a caller can pattern-match uniformly without extracting the value first.

public abstract record ApplyOutcome<T>
{
	private protected ApplyOutcome()
	{ }

	/// <summary>Mutation passed all constraints and <see cref="ConstrainedValue{T}.Current"/> was updated.</summary>
	public sealed record Applied(T Value) : ApplyOutcome<T>
	{
		/// <summary>The warnings and hints the change raised. Only errors reject a change, so these are what was accepted with a remark.</summary>
		public IReadOnlyList<Violation> Notices { get; init; } = [];
	}

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
public partial class Constraint<T>
{
	/// <summary>
	/// Set by <see cref="Named"/>, or by the (nameof(field), field) tuple <see cref="ConstraintSet"/>'s
	/// From threads through — see [Constraint] source gen, which builds that tuple automatically.
	/// </summary>
	public string? Name { get; internal set; }

	public ConstraintKind Kind { get; }

	/// <summary>
	/// Runs the rule. For a <see cref="ConstraintKind.PointInTime"/> constraint the first argument
	/// equals the second (there is no real "previous"); for <see cref="ConstraintKind.Sequential"/>
	/// it is the value before the mutation being checked.
	/// </summary>
	public Func<T, T, IEnumerable<Violation>> Check => (previous, next) => _checkAt(previous, next, TimeProvider.System);

	private Func<T, T, TimeProvider, IEnumerable<Violation>> _checkAt;

	internal Constraint(ConstraintKind kind, Func<T, T, IEnumerable<Violation>> check, [CallerMemberName] string? factory = null)
	{
		Kind = kind;
		_checkAt = (prev, next, _) => check(prev, next);
		Shape = new OpaqueShape<T>(kind) { Factory = factory };
	}

	// A time-aware constraint reads "now" from whatever TimeProvider the caller checks it with —
	// never from a captured one, since constraints are static fields shared by every caller (and by
	// tests that need to control the clock). Check, which has no provider to offer, uses TimeProvider.System.
	internal Constraint(ConstraintKind kind, Func<T, T, TimeProvider, IEnumerable<Violation>> checkAt, [CallerMemberName] string? factory = null)
	{
		Kind = kind;
		_checkAt = checkAt;
		Shape = new OpaqueShape<T>(kind) { Factory = factory };
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
	/// What the rule declares about itself, readable without running it: its <see cref="Axis"/> and the typed payload a test needs to attack it
	/// (an <see cref="IdempotentShape{T}"/>'s action, a <see cref="MonotonicShape{T}"/>'s direction). Never null: a rule built from a closure
	/// with nothing declared is an <see cref="OpaqueShape{T}"/>, an invariant when it is point-in-time and unclassified when it is sequential.
	/// </summary>
	public ConstraintShape<T> Shape { get; private set; }

	/// <summary>The axis a test attacks this rule on: shorthand for <c>Shape.Axis</c>.</summary>
	public Axis Axis => Shape.Axis;

	/// <summary>
	/// Set by <c>Transitions(selector).Allow(...)</c>: names the state a value is in (<see cref="StateOf"/>)
	/// and every legal edge (<see cref="Edges"/>), so a test driver can check each edge is reachable. Read from <see cref="Shape"/>.
	/// </summary>
	public Func<T, string>? StateOf => (Shape as TransitionsShape<T>)?.StateOf;

	public IReadOnlyList<(string From, string To)>? Edges => (Shape as TransitionsShape<T>)?.Edges;

	internal Constraint<T> WithGuard(Func<T, bool> guard)
	{
		Guard = guard;
		return this;
	}

	internal Constraint<T> WithShape(ConstraintShape<T> shape)
	{
		Shape = shape;
		return this;
	}

	internal Constraint<T> WithEdges(Func<T, string> stateOf, IReadOnlyList<(string From, string To)> edges) =>
		WithShape(new TransitionsShape<T>(stateOf, edges) { Factory = nameof(Constraints<>.Transitions) });

	/// <summary>Prose for people and tooling: what the rule says, beyond its name. Optional.</summary>
	public string? Description { get; private set; }

	/// <summary>
	/// True for a rule the other rules rely on: "this member is there". When one is violated, the set reports only the violated prerequisites and skips the
	/// rest, because those were written for a value that holds them (they read the member without checking it). Set with <see cref="AsPrerequisite"/>;
	/// the generated <c>*-required</c> rules are prerequisites.
	/// </summary>
	public bool IsPrerequisite { get; private set; }

	/// <summary>Marks this rule as one the others rely on (see <see cref="IsPrerequisite"/>).</summary>
	public Constraint<T> AsPrerequisite()
	{
		IsPrerequisite = true;
		return this;
	}

	/// <summary>Names the rule in place. Do this where the rule is declared; <see cref="ConstraintSet.From"/> copies a rule that is named differently elsewhere.</summary>
	public Constraint<T> Named(string name)
	{
		Name = name;
		return this;
	}

	/// <summary>The severity this rule's violations were set to with <see cref="WithSeverity"/>, or null when each violation keeps its own.</summary>
	public Severity? SeverityOverride { get; private set; }

	/// <summary>
	/// Reports every violation of this rule at <paramref name="severity"/>: a rule that is only a hint today (<c>Unique(...).WithSeverity(Severity.Hint)</c>) or a warning.
	/// Works on every shape, including <see cref="Constraints{T}.Check"/>. Only errors reject a change in <c>Apply</c>.
	/// </summary>
	public Constraint<T> WithSeverity(Severity severity)
	{
		var inner = _checkAt;
		_checkAt = (previous, next, time) => inner(previous, next, time).Select(v => v with { Severity = severity });
		SeverityOverride = severity;
		return this;
	}

	public Constraint<T> Describe(string description)
	{
		Description = description;
		return this;
	}

	// The same rule as an independent object, so two sets can name one rule differently without sharing the name.
	internal Constraint<T> Copy()
	{
		var copy = new Constraint<T>(Kind, _checkAt)
		{
			Name = Name,
			Description = Description,
			SeverityOverride = SeverityOverride,
			IsPrerequisite = IsPrerequisite,
			Shape = Shape
		};
		if (Guard is { } guard)
		{
			copy.WithGuard(guard);
		}
		return copy;
	}

	/// <summary>
	/// The same rule over a different type, reached through <paramref name="project"/>: a constraint on an <c>Order</c> becomes a constraint on
	/// an <c>(Order, Audit)</c> pair, a wrapper, or an aggregate that holds one. Kind, name, <see cref="Guard"/> and the <see cref="Shape"/>'s readable parts
	/// (transition edges, a monotonic probe, a conserved selector) carry over, so tooling that reads them (coverage, diagrams, axis tests) sees through the
	/// projection. A shape that has to <i>build</i> a value (an idempotent action, a permutation) cannot be carried by a read-only projection: use the
	/// overload with a <c>set</c> to keep it.
	/// </summary>
	public Constraint<TOther> Map<TOther>(Func<TOther, T> project) => Map(project, set: null);

	/// <summary>
	/// <see cref="Map{TOther}(Func{TOther, T})"/> with a way to put a changed <typeparamref name="T"/> back into the <typeparamref name="TOther"/>, so
	/// a shape that builds values (<see cref="IdempotentShape{T}.Action"/>, <see cref="OrderIndependentShape{T}.Permute"/>) survives the projection.
	/// </summary>
	public Constraint<TOther> Map<TOther>(Func<TOther, T> project, Func<TOther, T, TOther>? set)
	{
		var mapped = new Constraint<TOther>(Kind, (previous, next, time) => CheckAt(project(previous), project(next), time))
		{
			Name = Name,
			Description = Description,
			SeverityOverride = SeverityOverride,
			IsPrerequisite = IsPrerequisite,
			Shape = Shape.Map(project, set)
		};
		if (Guard is { } guard)
		{
			mapped.WithGuard(other => guard(project(other)));
		}
		return mapped;
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
public static partial class Constraints<T>
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
	/// Violated when <paramref name="predicate"/> is false for the value <paramref name="select"/> picks out. The violation carries that value as
	/// evidence and the member's name as <see cref="Violation.Member"/> (taken from the text of <paramref name="select"/>, so
	/// <c>Always(o =&gt; o.Email, ...)</c> reports <c>"Email"</c>; no expression tree, so it is trim and AOT safe).
	/// </summary>
	public static Constraint<T> Always<TValue>(
		Func<T, TValue> select,
		Func<TValue, bool> predicate,
		string? message = null,
		Func<T, SourceLocation?>? at = null,
		string? member = null,
		Func<TValue, string>? describe = null,
		[CallerArgumentExpression(nameof(select))] string? selectExpression = null
	)
	{
		Constraint<T> self = null!;
		member ??= MemberOf(selectExpression);
		self = new(ConstraintKind.PointInTime, (_, next) =>
		{
			var value = select(next);
			return predicate(value)
				? []
				: [
					new Violation(describe?.Invoke(value) ?? message ?? $"{self.Name ?? "Constraint"} was violated.") { Member = member, Evidence = value }.At(
						at?.Invoke(next)
					)
				];
		});
		return self;
	}

	/// <summary>"o =&gt; o.Address.City" becomes "Address.City"; anything that is not a plain member path becomes null.</summary>
	public static string? MemberOf(string? expression)
	{
		if (expression is null)
		{
			return null;
		}
		var arrow = expression.IndexOf("=>", StringComparison.Ordinal);
		var body = (arrow >= 0 ? expression[(arrow + 2)..] : expression).Trim();
		var dot = body.IndexOf('.', StringComparison.Ordinal);
		if (dot < 0)
		{
			return null;
		}
		var path = body[(dot + 1)..];
		return path.Length > 0 && path.All(c => char.IsLetterOrDigit(c) || c is '_' or '.') ? path : null;
	}

	/// <summary>
	/// Violated by every key that more than one item has: a silent overwrite waiting to happen (two pages at one URL, two entries with one id).
	/// Each duplicated key is one violation with the key as evidence.
	/// </summary>
	public static Constraint<T, TKey> Unique<TItem, TKey>(
		Func<T, IEnumerable<TItem>> items,
		Func<TItem, TKey> key,
		IEqualityComparer<TKey>? comparer = null,
		string? message = null,
		Func<TItem, SourceLocation?>? at = null,
		Func<TKey, int, string>? describe = null,
		Func<TKey, IReadOnlyList<TItem>, string>? describeGroup = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	)
	{
		Constraint<T, TKey> self = null!;
		var member = MemberOf(itemsExpression);
		self = new(
			ConstraintKind.PointInTime,
			(_, next) =>
				[
					.. items(next)
						.GroupBy(key, comparer)
						.Where(g => g.Count() > 1)
						.Select(
							g =>
								new Violation(
									describeGroup?.Invoke(g.Key, [.. g]) ?? describe?.Invoke(
										g.Key,
										g.Count()
									) ?? message ?? $"{self.Name ?? "Constraint"}: '{g.Key}' appears {g.Count()} times."
								)

								{
									Member = member,
									Evidence = g.Key
								}.At(at?.Invoke(g.Skip(1).First()))
						),
				]
		);
		return self;
	}

	/// <summary>
	/// Referential integrity within a value: every key an item refers to (<paramref name="references"/>, none or many per item) must be one of
	/// <paramref name="targets"/>. Each dangling reference is one violation, with the key as evidence.
	/// </summary>
	public static Constraint<T, TKey> References<TItem, TKey>(
		Func<T, IEnumerable<TItem>> items,
		Func<TItem, IEnumerable<TKey>> references,
		Func<T, IEnumerable<TKey>> targets,
		IEqualityComparer<TKey>? comparer = null,
		string? message = null,
		Func<TItem, SourceLocation?>? at = null,
		Func<TItem, TKey, string>? describe = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	)
	{
		Constraint<T, TKey> self = null!;
		var member = MemberOf(itemsExpression);
		self = new(ConstraintKind.PointInTime, (_, next) =>
		{
			var known = new HashSet<TKey>(targets(next), comparer);
			return [
				.. items(next)
					.SelectMany(item => references(item).Where(r => !known.Contains(r)).Select(r => (Item: item, Reference: r)))
					.Select(
						d =>
							new Violation(
								describe?.Invoke(d.Item, d.Reference) ?? message ?? $"{self.Name ?? "Constraint"}: '{d.Reference}' refers to nothing."
							)

							{
								Member = member,
								Evidence = d.Reference
							}.At(at?.Invoke(d.Item))
					),
			];
		});
		return self;
	}

	/// <summary>
	/// Violated by a cycle in a graph given as nodes with an id and the ids they point at (a table of contents that includes itself, an include
	/// loop, a redirect loop). Each cycle found is one violation whose evidence is the path (<c>a -&gt; b -&gt; a</c>). Edges to ids that are not
	/// nodes are ignored, and duplicate ids do not throw: the first node with an id is the one followed.
	/// </summary>
	public static Constraint<T> Acyclic<TNode, TKey>(
		Func<T, IEnumerable<TNode>> nodes,
		Func<TNode, TKey> id,
		Func<TNode, IEnumerable<TKey>> edges,
		IEqualityComparer<TKey>? comparer = null,
		string? message = null,
		Func<TNode, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(nodes))] string? nodesExpression = null
	) where TKey : notnull
	{
		Constraint<T> self = null!;
		var member = MemberOf(nodesExpression);
		self = new(ConstraintKind.PointInTime, (_, next) =>
		{
			var byId = new Dictionary<TKey, TNode>(comparer);
			foreach (var node in nodes(next))
			{
				byId.TryAdd(id(node), node);
			}
			var state = new Dictionary<TKey, int>(comparer);
			var path = new List<TKey>();
			var found = new List<Violation>();
			var equal = comparer ?? EqualityComparer<TKey>.Default;

			// Iterative, so an include chain a million deep cannot overflow the stack: each frame is a node and the edges still to follow.
			foreach (var start in byId.Keys.ToList())
			{
				if (state.ContainsKey(start))
				{
					continue;
				}
				var stack = new Stack<(TKey Key, IEnumerator<TKey> Edges)>();
				state[start] = 1;
				path.Add(start);
				stack.Push((start, edges(byId[start]).GetEnumerator()));
				while (stack.Count > 0)
				{
					var (key, following) = stack.Peek();
					if (!following.MoveNext())
					{
						following.Dispose();
						stack.Pop();
						path.RemoveAt(path.Count - 1);
						state[key] = 2;
						continue;
					}
					var target = following.Current;
					if (!byId.ContainsKey(target))
					{
						continue;
					}
					if (!state.TryGetValue(target, out var seen))
					{
						state[target] = 1;
						path.Add(target);
						stack.Push((target, edges(byId[target]).GetEnumerator()));
					}
					else if (seen == 1)
					{
						var cycle = path.SkipWhile(k => !equal.Equals(k, target)).Append(target).ToList();
						var text = string.Join(" -> ", cycle);
						found.Add(
							new Violation(message ?? $"{self.Name ?? "Constraint"}: cycle {text}.") { Member = member, Evidence = text }.At(
								at?.Invoke(byId[target])
							)
						);
					}
				}
			}
			return found;
		});
		return self;
	}

	/// <summary>
	/// A dictionary whose key must be the entity's own key (<c>Repositories["kibana"].Name == "kibana"</c>): the key is stored twice, and the two drift.
	/// Each entry whose key differs from its value's key is one violation, with the dictionary key as evidence.
	/// </summary>
	public static Constraint<T> KeysMatch<TKey, TValue>(
		Func<T, IEnumerable<KeyValuePair<TKey, TValue>>> entries,
		Func<TValue, TKey> key,
		IEqualityComparer<TKey>? comparer = null,
		string? message = null,
		Func<TValue, SourceLocation?>? at = null,
		Func<TKey, TValue, string>? describe = null,
		[CallerArgumentExpression(nameof(entries))] string? entriesExpression = null
	)
	{
		Constraint<T> self = null!;
		var member = MemberOf(entriesExpression);
		var equal = comparer ?? EqualityComparer<TKey>.Default;
		self = new(ConstraintKind.PointInTime, (_, next) =>
				[
					.. entries(next)
						.Where(e => !equal.Equals(e.Key, key(e.Value)))
						.Select(
							e =>
								new Violation(
									describe?.Invoke(
										e.Key,
										e.Value
									) ?? message ?? $"{self.Name ?? "Constraint"}: the entry '{e.Key}' holds a value whose own key is '{key(e.Value)}'."
								)

								{
									Member = member,
									Evidence = (e.Key, key(e.Value))
								}.At(at?.Invoke(e.Value))
						),
				]);
		return self;
	}

	/// <summary>
	/// Violated when a mapping points at something that itself maps onwards (redirect A to B while B redirects to C): the chain should be flattened
	/// to A to C. Each chained item is one violation. A mapping onto itself is not a chain; <see cref="Acyclic{TNode, TKey}"/> reports loops.
	/// </summary>
	public static Constraint<T> NoChains<TItem, TKey>(
		Func<T, IEnumerable<TItem>> items,
		Func<TItem, TKey> from,
		Func<TItem, TKey> to,
		IEqualityComparer<TKey>? comparer = null,
		string? message = null,
		Func<TItem, SourceLocation?>? at = null,
		Func<TItem, string>? describe = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	)
	{
		Constraint<T> self = null!;
		var member = MemberOf(itemsExpression);
		var equal = comparer ?? EqualityComparer<TKey>.Default;
		self = new(ConstraintKind.PointInTime, (_, next) =>
		{
			var all = items(next).ToList();
			var sources = new HashSet<TKey>(all.Where(i => !equal.Equals(from(i), to(i))).Select(from), comparer);
			return [
				.. all
					.Where(i => !equal.Equals(from(i), to(i)) && sources.Contains(to(i)))
					.Select(
						i =>
							new Violation(describe?.Invoke(i) ?? message ?? $"{self.Name ?? "Constraint"}: '{from(i)}' maps to '{to(i)}', which maps onwards.")
							{
								Member = member,
								Evidence = (from(i), to(i))
							}.At(at?.Invoke(i))
					),
			];
		});
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
		return self.WithShape(new IdempotentShape<T>(action) { Factory = nameof(Idempotent) });
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
		new Constraint<T>(ConstraintKind.Sequential, (prev, next) =>
		{
			var before = selector(prev);
			var after = selector(next);
			var comparison = Comparer<TValue>.Default.Compare(after, before);
			var holds = direction == MonotonicDirection.NonDecreasing ? comparison >= 0 : comparison <= 0;
			return holds ? [] : [Violation.Error(message ?? $"Expected {direction} but moved from {before} to {after}.") with { Evidence = (before, after) }];
		}).WithShape(new MonotonicShape<T>(
			direction,
			(prev, next) => new ShapeProbe(1, Comparer<TValue>.Default.Compare(selector(next), selector(prev)) == 0 ? 0 : 1)
		)

		{
			Factory = nameof(Monotonic)
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
		return self.WithShape(new ConservesShape<T>(value => selector(value)) { Factory = nameof(Conserves) });
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
	/// <summary>
	/// Whether the generator also emits a null-only <c>&lt;Property&gt;Required</c> rule for a non-nullable reference member that must be set: one declared <c>required</c>, or a
	/// get-only auto property. Default true; a member with an explicit <c>[Required]</c> gets that attribute's rule instead, and <c>[ConstraintIgnore]</c> leaves a member out.
	/// </summary>
	public bool ImplicitRequired { get; set; } = true;
}

/// <summary>
/// <see cref="ConstraintsAttribute"/> for a companion class that names the type its constraints are about: <c>[Constraints&lt;Order&gt;] public static partial class OrderRules</c>.
/// The generated <c>All</c> set contains the class's static <see cref="Constraint{T}"/> members and one constraint for every DataAnnotation on <typeparamref name="T"/>, so a
/// companion with no hand-written rule at all still gets the rules <c>Order</c> already declares with <c>[Required]</c>, <c>[Range]</c> and the rest. A member of another type
/// argument is an error (NECG007).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
#pragma warning disable CA1813
public class ConstraintsAttribute<T> : Attribute
#pragma warning restore CA1813
{
	/// <summary>
	/// Whether the generator also emits a null-only <c>&lt;Property&gt;Required</c> rule for a non-nullable reference member that must be set: one declared <c>required</c>, or a
	/// get-only auto property. Default true; a member with an explicit <c>[Required]</c> gets that attribute's rule instead, and <c>[ConstraintIgnore]</c> leaves a member out.
	/// </summary>
	public bool ImplicitRequired { get; set; } = true;
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

/// <summary>
/// On a method of an entity that returns a wrapper of the entity (a <c>Result&lt;T&gt;</c>, a tuple): names the member to read the new value from, for when the generator's
/// inference would choose the wrong one or decline (several members of the entity's type, none named like a result). Without it the generator reads the one member
/// of the entity's type, or the one named <c>Value</c>, <c>Result</c>, <c>Next</c>, <c>Current</c>, <c>Entity</c> or <c>Attempted</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ConstraintActionAttribute(string member) : Attribute
{
	/// <summary>The name of the member that holds the entity the method produced.</summary>
	public string Member { get; } = member;
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

public sealed partial class ConstraintSet<T>
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
	/// Every constraint in the set lifted through <paramref name="project"/> (see <see cref="Constraint{T}.Map{TOther}(Func{TOther, T})"/>), names preserved, or prefixed with
	/// <paramref name="prefix"/>. Prefix when two sets are lifted into one that both have a rule of the same name (two aggregates that each have <c>SkusAreUnique</c>):
	/// names are how a violation, <c>Probe</c> and <c>Laws</c> tell rules apart, and the prefix also says which part a violation is about.
	/// </summary>
	public ConstraintSet<TOther> Map<TOther>(Func<TOther, T> project, string? prefix = null) =>
		new([.. _constraints.Select(c => prefix is null ? c.Map(project) : c.Map(project).Named($"{prefix}{c.Name}"))]);

	/// <summary>
	/// Runs every <see cref="ConstraintKind.PointInTime"/> constraint over the single
	/// <paramref name="instance"/>. <see cref="ConstraintKind.Sequential"/> constraints are
	/// skipped — a standalone instance has no "previous" to compare against; see <see cref="Apply"/>.
	/// </summary>
	public IReadOnlyList<Violation> Validate(T instance, TimeProvider? time = null)
	{
		time ??= TimeProvider.System;
		var rules = _constraints.Where(c => c.Kind == ConstraintKind.PointInTime).ToList();
		var missing = Violated(rules.Where(c => c.IsPrerequisite), instance, instance, time);
		if (missing.Count > 0)
		{
			return missing;
		}
		return [.. rules.SelectMany(c => c.CheckAt(instance, instance, time).Select(v => v with { ConstraintId = c.Name }))];
	}

	// The violations of the prerequisite rules. When there are any, nothing else is asked: the other rules were written for a value that holds them.
	private static IReadOnlyList<Violation> Violated(IEnumerable<Constraint<T>> prerequisites, T previous, T next, TimeProvider time) =>
		[.. prerequisites.SelectMany(c => c.CheckAt(previous, next, time).Select(v => v with { ConstraintId = c.Name }))];

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
		if (IsNoop(current, next))
			return new ApplyOutcome<T>.Noop(current);
		return Outcome(current, next, RunAll(current, next, time));
	}

	/// <summary>Like <see cref="ApplyDetailed"/>, but throws on <see cref="ApplyOutcome{T}.Failure"/>. Returns the new value for both <see cref="ApplyOutcome{T}.Applied"/> and <see cref="ApplyOutcome{T}.Noop"/>.</summary>
	public T ApplyOrThrow(T current, Func<T, T> mutation, TimeProvider? time = null) => ApplyDetailed(current, mutation, time) switch
	{
		ApplyOutcome<T>.Applied a => a.Value,
		ApplyOutcome<T>.Noop n => n.Value,
		ApplyOutcome<T>.Failure f =>
			throw new ConstraintViolationException(f.Violations, $"Apply failed: {string.Join(", ", f.Violations.Select(v => v.Message))}"),
		_ => throw new UnreachableException(),
	};

	/// <summary>
	/// <see cref="ApplyDetailed"/> for a mutation that awaits something (a service call, a database read): it receives
	/// <paramref name="cancellationToken"/>, and the constraints are checked once it returns.
	/// </summary>
	public async ValueTask<ApplyOutcome<T>> ApplyDetailedAsync(
		T current,
		Func<T, CancellationToken, ValueTask<T>> mutation,
		TimeProvider? time = null,
		CancellationToken cancellationToken = default
	)
	{
		var next = await mutation(current, cancellationToken).ConfigureAwait(false);
		if (IsNoop(current, next))
		{
			return new ApplyOutcome<T>.Noop(current);
		}
		return Outcome(current, next, RunAll(current, next, time));
	}

	/// <summary><see cref="Apply"/> for an awaited mutation.</summary>
	public async ValueTask<Result<T>> ApplyAsync(
		T current,
		Func<T, CancellationToken, ValueTask<T>> mutation,
		TimeProvider? time = null,
		CancellationToken cancellationToken = default
	) => await ApplyDetailedAsync(current, mutation, time, cancellationToken).ConfigureAwait(false) switch
	{
		ApplyOutcome<T>.Applied a => new Result<T>.Ok(a.Value),
		ApplyOutcome<T>.Noop n => new Result<T>.Ok(n.Value),
		ApplyOutcome<T>.Failure f => new Result<T>.Fail(f.Attempted, f.Violations),
		_ => throw new UnreachableException(),
	};

	/// <summary>
	/// Validates a stream of values as they arrive (a database cursor, an event-store read, a paged API) without materialising it:
	/// each value is yielded with the violations found, in order.
	/// </summary>
	public async IAsyncEnumerable<(T Value, IReadOnlyList<Violation> Violations)> ValidateAsync(
		IAsyncEnumerable<T> values,
		TimeProvider? time = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
	{
		await foreach (var value in values.WithCancellation(cancellationToken).ConfigureAwait(false))
		{
			yield return (value, Validate(value, time));
		}
	}

	// Called by ConstrainedValue<T>.Apply after it has already computed `next` to avoid calling
	// the mutation twice — the channel needs the value upfront to short-circuit on equality.
	// Equal values are a no-op, except a reference to a type without value equality that is the very same object: that is a class mutated in
	// place (an EF entity, a classic aggregate), where "equal" only says the reference did not change. Those run the point-in-time constraints.
	internal static bool IsNoop(T current, T next) =>
		EqualityComparer<T>.Default.Equals(current, next) && !(current is not null && !typeof(T).IsValueType && ReferenceEquals(
			current,
			next
		) && current is not IEquatable<T>);

	/// <summary>
	/// Checks a change you made yourself, with the "before" you kept: for an entity that is mutated in place, where <see cref="Apply"/> cannot see the
	/// previous state. Take the snapshot (a copy, or the ORM's original values) before mutating, then pass both.
	/// </summary>
	public IReadOnlyList<Violation> Check(T before, T after, TimeProvider? time = null) => RunAll(before, after, time);

	/// <summary>
	/// Checks a value you have just built, for a factory (<c>public static Result&lt;Money&gt; Create(...) =&gt; Constraints.Validated(new Money(...))</c>):
	/// <see cref="Result{T}.Ok"/> unless an <see cref="Severity.Error"/> rule is violated, with warnings and hints left out. The point-in-time rules only,
	/// as <see cref="Validate"/>; a rule about a change has no "before" here.
	/// </summary>
	public Result<T> Validated(T value, TimeProvider? time = null) =>
		Validate(value, time) is var violations && violations.Any(v => v.Severity == Severity.Error)
			? new Result<T>.Fail(value, violations)
			: new Result<T>.Ok(value);

	/// <summary><see cref="Validated"/> that throws a <see cref="ConstraintViolationException"/> instead of returning a failure, and returns the value otherwise. For a boundary where an invalid value is a bug, not an input.</summary>
	public T Ensure(T value, TimeProvider? time = null) => Validated(value, time).Unwrap();

	// Only errors reject a change; warnings and hints ride along on the Applied outcome.
	internal static ApplyOutcome<T> Outcome(T current, T next, IReadOnlyList<Violation> violations) =>
		violations.Any(v => v.Severity == Severity.Error)
			? new ApplyOutcome<T>.Failure(current, next, violations)
			: new ApplyOutcome<T>.Applied(next) { Notices = violations };

	internal IReadOnlyList<Violation> RunAll(T current, T next, TimeProvider? time)
	{
		time ??= TimeProvider.System;
		var prerequisites = _constraints.Where(c => c.IsPrerequisite).ToList();
		if (prerequisites.Count > 0)
		{
			var missing = Violated(prerequisites, current, next, time);
			if (missing.Count == 0 && !ReferenceEquals(current, next))
			{
				missing = Violated(prerequisites, current, current, time);
			}
			if (missing.Count > 0)
			{
				return missing;
			}
		}
		return [.. _constraints.SelectMany(c => c.CheckAt(current, next, time).Select(v => v with { ConstraintId = c.Name })),];
	}
}

public static class ConstraintSet
{
	/// <summary>
	/// One set over a pair, from a set for each half: the way a rule that needs two aggregates (a documentation set and the product catalog it refers
	/// to, a navigation and the assembly it mounts) is written without a new type. Each constraint keeps its name and is checked on its own half.
	/// </summary>
	public static ConstraintSet<(TFirst First, TSecond Second)> Pair<TFirst, TSecond>(ConstraintSet<TFirst> first, ConstraintSet<TSecond> second) =>
		new([
			.. first.Map<(TFirst First, TSecond Second)>(p => p.First).AsConstraints(),
			.. second.Map<(TFirst First, TSecond Second)>(p => p.Second).AsConstraints()
		]);

	/// <summary>The constraints of several sets over one type as a single set, in order (annotations, hand-written rules, a composite's rules).</summary>
	public static ConstraintSet<T> Merge<T>(params ConstraintSet<T>[] sets) => new([.. sets.SelectMany(s => s.AsConstraints())]);

	/// <summary>
	/// Names every constraint from its tuple (mirroring Nullean.Entities.Reactive's
	/// <c>ReactiveConstraintSet.From</c> — [Constraint] source gen passes (nameof(field), field),
	/// so a set's names can never drift from the fields actually declared).
	/// </summary>
	public static ConstraintSet<T> From<T>(params (string Name, Constraint<T> Constraint)[] constraints)
	{
		// A rule already named differently (it is shared with another set) is copied, so each set keeps its own name for it.
		return new([
			.. constraints.Select(c => c.Constraint.Name is { } existing && existing != c.Name ? c.Constraint.Copy().Named(c.Name) : c.Constraint.Named(c.Name))
		]);
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
		if (ConstraintSet<T>.IsNoop(Current, next))
		{
			return new ApplyOutcome<T>.Noop(Current);
		}
		var outcome = ConstraintSet<T>.Outcome(Current, next, _constraints.RunAll(Current, next, _time));
		if (outcome is ApplyOutcome<T>.Applied)
		{
			Current = next;
		}
		return outcome;
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
		ApplyOutcome<T>.Failure f =>
			throw new ConstraintViolationException(f.Violations, $"Apply failed: {string.Join(", ", f.Violations.Select(v => v.Message))}"),
		_ => throw new UnreachableException(),
	};

	/// <summary><see cref="ApplyDetailed"/> for a mutation that awaits something. <see cref="Current"/> is updated only if every constraint passes.</summary>
	public async ValueTask<ApplyOutcome<T>> ApplyDetailedAsync(Func<T, CancellationToken, ValueTask<T>> mutation, CancellationToken cancellationToken = default)
	{
		var outcome = await _constraints.ApplyDetailedAsync(Current, mutation, _time, cancellationToken).ConfigureAwait(false);
		if (outcome is ApplyOutcome<T>.Applied applied)
		{
			Current = applied.Value;
		}
		return outcome;
	}

	/// <summary><see cref="Apply"/> for a mutation that awaits something.</summary>
	public async ValueTask<Result<T>> ApplyAsync(
		Func<T, CancellationToken, ValueTask<T>> mutation,
		CancellationToken cancellationToken = default
	) => await ApplyDetailedAsync(mutation, cancellationToken).ConfigureAwait(false) switch
	{
		ApplyOutcome<T>.Applied a => new Result<T>.Ok(a.Value),
		ApplyOutcome<T>.Noop n => new Result<T>.Ok(n.Value),
		ApplyOutcome<T>.Failure f => new Result<T>.Fail(f.Attempted, f.Violations),
		_ => throw new UnreachableException(),
	};

	/// <summary>
	/// Applies each mutation of an asynchronous sequence in order, each to the result of the last that was accepted, yielding the
	/// outcome of every one. A rejected mutation leaves <see cref="Current"/> as it was and the sequence carries on.
	/// </summary>
	public async IAsyncEnumerable<ApplyOutcome<T>> ApplyAllAsync(
		IAsyncEnumerable<Func<T, T>> mutations,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
	{
		await foreach (var mutation in mutations.WithCancellation(cancellationToken).ConfigureAwait(false))
		{
			yield return ApplyDetailed(mutation);
		}
	}
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
	/// <remarks>
	/// The initial value is not checked: a channel can start over data that is already invalid (a legacy row) and be repaired through <c>Apply</c>.
	/// To refuse an invalid start, check it first: <c>ConstrainedValue.For(constraints.Ensure(initial)).Enforce(constraints)</c>.
	/// </remarks>
	public ConstrainedValue<T> Enforce(ConstraintSet<T> constraints, TimeProvider? time = null) => new(_initial, constraints, time);
}
