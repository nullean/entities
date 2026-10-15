// Nullean.Entities — what a rule says about itself.
//
// A Constraint<T> used to keep only a name, a Kind and a closure, so a test library could not tell an Idempotent rule from an Always
// one. A shape is the rule's declared quality and the typed payload a test needs to attack it: the action an Idempotent rule applies
// twice, the direction a Monotonic rule allows, the items a confluence rule says may be listed in any order. A test reads the shape
// and picks the technique; the rule's own Check is untouched.

namespace Nullean.Entities;

/// <summary>
/// The kind of bug a rule is for: the nine axes a domain can be attacked on. <see cref="Vacancy"/> is not a shape (no rule is "about" vacancy): it is the
/// check, across every rule, that a green test reached the rule it claims to test.
/// </summary>
public enum Axis
{
	/// <summary>A rule that must hold in every state: <c>Always</c>, <c>Each</c>, <c>Unique</c>, <c>References</c>, transitions, and any point-in-time <c>Check</c>.</summary>
	Invariant,

	/// <summary><c>Idempotent</c>: doing it twice gives what doing it once does.</summary>
	Idempotency,

	/// <summary><c>Monotonic</c> and <c>MonotonicPerKey</c>: a value only moves one way.</summary>
	Monotonicity,

	/// <summary><c>Conserves</c>: a quantity is neither created nor destroyed.</summary>
	Conservation,

	/// <summary>A temporal rule that forbids a sequence (<c>Never</c>, <c>NeverTwice</c>).</summary>
	Absence,

	/// <summary>A temporal rule with a deadline (<c>After().Then().Within()</c>, <c>AfterUntil</c>).</summary>
	Temporal,

	/// <summary>A temporal rule that requires something to happen eventually (<c>Eventually</c>).</summary>
	Liveness,

	/// <summary><c>OrderIndependent</c>: the order things arrive in does not change the result.</summary>
	Confluence,

	/// <summary>Whether the tests reached the rules at all. No rule has this axis; it is computed over all of them.</summary>
	Vacancy,

	/// <summary>A sequential rule written by hand (<c>CheckTransition</c>, <c>Transitions</c> over a predicate): checked, but no technique can be chosen from its shape.</summary>
	Unclassified
}

/// <summary>
/// What one pair of values says about whether a rule was exercised: how many things a rule compared across the change, and how many of them moved.
/// A test that never saw <see cref="Moved"/> above zero checked nothing.
/// </summary>
public readonly record struct ShapeProbe(int Compared, int Moved);

/// <summary>
/// A rule's declared quality, readable without running it. Closed: only this assembly derives from it. Typed payloads live on the
/// <see cref="ConstraintShape{T}"/> subclasses; <see cref="Axis"/> and <see cref="Factory"/> are all a caller that does not know <c>T</c> needs.
/// </summary>
public abstract record ConstraintShape
{
	/// <summary>The axis a test attacks this rule on.</summary>
	public abstract Axis Axis { get; }

	/// <summary>The <c>Constraints&lt;T&gt;</c> factory that built the rule (<c>Always</c>, <c>Idempotent</c>, ...), for a failure message that names what was declared.</summary>
	public string? Factory { get; init; }

	/// <summary>The relation between members this rule was declared from (see <see cref="Relation"/>), when it was; a generator reads it so the data it draws already holds the relation.</summary>
	public virtual RelationInfo? Relation => null;

	internal ConstraintShape()
	{
	}
}

/// <summary>A <see cref="ConstraintShape"/> over <typeparamref name="T"/>, with the payload typed.</summary>
public abstract record ConstraintShape<T> : ConstraintShape
{
	/// <summary>
	/// The same shape over <typeparamref name="TOther"/>, reached through <paramref name="project"/>. A payload that only reads
	/// (a selector, a state, a count) composes with the projection. A payload that has to build a <typeparamref name="TOther"/>
	/// (an action, a permutation) needs <paramref name="set"/>, which puts a changed <typeparamref name="T"/> back; without it that payload is dropped
	/// and the axis reports it cannot be attacked, instead of attacking the wrong thing.
	/// </summary>
	internal abstract ConstraintShape<TOther> Map<TOther>(Func<TOther, T> project, Func<TOther, T, TOther>? set);
}

/// <summary>A rule built from a closure with nothing declared about it: an invariant when it is point-in-time, unclassified when it is sequential.</summary>
public sealed record OpaqueShape<T>(ConstraintKind Kind) : ConstraintShape<T>
{
	public override Axis Axis => Kind == ConstraintKind.PointInTime ? Axis.Invariant : Axis.Unclassified;

	internal override ConstraintShape<TOther> Map<TOther>(Func<TOther, T> project, Func<TOther, T, TOther>? set) =>
		new OpaqueShape<TOther>(Kind) { Factory = Factory };
}

/// <summary>An allow-list of state changes (<c>Transitions(selector).Allow(a, b)</c>): an invariant over the pair, with every edge a test can check is reachable.</summary>
public sealed record TransitionsShape<T>(Func<T, string> StateOf, IReadOnlyList<(string From, string To)> Edges) : ConstraintShape<T>
{
	public override Axis Axis => Axis.Invariant;

	internal override ConstraintShape<TOther> Map<TOther>(Func<TOther, T> project, Func<TOther, T, TOther>? set)
	{
		var stateOf = StateOf;
		return new TransitionsShape<TOther>(other => stateOf(project(other)), Edges) { Factory = Factory };
	}
}

/// <summary><c>Idempotent(action)</c>: <paramref name="Action"/> applied twice equals applied once. Null once the rule is mapped without a way to put a changed value back.</summary>
public sealed record IdempotentShape<T>(Func<T, T>? Action) : ConstraintShape<T>
{
	public override Axis Axis => Axis.Idempotency;

	internal override ConstraintShape<TOther> Map<TOther>(Func<TOther, T> project, Func<TOther, T, TOther>? set)
	{
		var action = Action;
		Func<TOther, TOther>? lifted = action is not null && set is not null ? other => set(other, action(project(other))) : null;
		return new IdempotentShape<TOther>(lifted) { Factory = Factory };
	}
}

/// <summary>
/// <c>Monotonic</c> (one selected value) or <c>MonotonicPerKey</c> (one value per key of a collection): <paramref name="Probe"/> says, for a (before, after) pair,
/// how many values were compared and how many moved, so a test can tell "never moved the wrong way" from "never moved".
/// </summary>
public sealed record MonotonicShape<T>(MonotonicDirection Direction, Func<T, T, ShapeProbe> Probe, bool PerKey = false) : ConstraintShape<T>
{
	public override Axis Axis => Axis.Monotonicity;

	internal override ConstraintShape<TOther> Map<TOther>(Func<TOther, T> project, Func<TOther, T, TOther>? set)
	{
		var probe = Probe;
		return new MonotonicShape<TOther>(Direction, (before, after) => probe(project(before), project(after)), PerKey) { Factory = Factory };
	}
}

/// <summary><c>Conserves(selector)</c>: <paramref name="Select"/> reads the quantity that must not change across a mutation.</summary>
public sealed record ConservesShape<T>(Func<T, object?> Select) : ConstraintShape<T>
{
	public override Axis Axis => Axis.Conservation;

	internal override ConstraintShape<TOther> Map<TOther>(Func<TOther, T> project, Func<TOther, T, TOther>? set)
	{
		var select = Select;
		return new ConservesShape<TOther>(other => select(project(other))) { Factory = Factory };
	}
}

/// <summary>
/// <c>OrderIndependent</c>: the value lists items whose order must not change the result. <paramref name="Count"/> is how many items there are,
/// <paramref name="Permute"/> rebuilds the value with its items in a given order (a permutation of <c>0..Count-1</c>; null once the rule is mapped without a way to put
/// a changed value back), and <paramref name="SameResult"/> compares what two orderings produced.
/// </summary>
public sealed record OrderIndependentShape<T>(Func<T, int> Count, Func<T, IReadOnlyList<int>, T>? Permute, Func<T, T, bool> SameResult) : ConstraintShape<T>
{
	public override Axis Axis => Axis.Confluence;

	internal override ConstraintShape<TOther> Map<TOther>(Func<TOther, T> project, Func<TOther, T, TOther>? set)
	{
		var count = Count;
		var permute = Permute;
		var same = SameResult;
		Func<TOther, IReadOnlyList<int>, TOther>? lifted = permute is not null && set is not null
			? (other, order) => set(other, permute(project(other), order))
			: null;
		return new OrderIndependentShape<TOther>(other => count(project(other)), lifted, (a, b) => same(project(a), project(b))) { Factory = Factory };
	}
}
