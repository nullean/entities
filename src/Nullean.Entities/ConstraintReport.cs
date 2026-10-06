// A rule that says what it reports as evidence, and the report a set produces over one value: every violation plus typed access to the evidence of a given rule.

namespace Nullean.Entities;

/// <summary>
/// A <see cref="Constraint{T}"/> that also says what its violations carry as <see cref="Violation.Evidence"/>: the offending item (<c>Each</c>), the duplicated key (<c>Unique</c>),
/// the dangling reference (<c>References</c>), or a record the rule builds (<c>EachCheck</c>). A loader that wants to keep what the rule did not reject asks the report for it
/// by this handle (<see cref="ConstraintReport{T}.Keep{TItem}"/>), so renaming the rule or changing its evidence is a compile error where the loader uses it.
/// </summary>
/// <typeparam name="T">The value the rule checks.</typeparam>
/// <typeparam name="TEvidence">What the rule's violations carry as evidence.</typeparam>
public sealed class Constraint<T, TEvidence> : Constraint<T>
{
	internal Constraint(ConstraintKind kind, Func<T, T, IEnumerable<Violation>> check) : base(kind, check)
	{
	}

	internal Constraint(ConstraintKind kind, Func<T, T, TimeProvider, IEnumerable<Violation>> checkAt) : base(kind, checkAt)
	{
	}

	/// <inheritdoc cref="Constraint{T}.Named"/>
	public new Constraint<T, TEvidence> Named(string name)
	{
		_ = base.Named(name);
		return this;
	}

	/// <inheritdoc cref="Constraint{T}.WithSeverity"/>
	public new Constraint<T, TEvidence> WithSeverity(Severity severity)
	{
		_ = base.WithSeverity(severity);
		return this;
	}

	/// <inheritdoc cref="Constraint{T}.WithSeverityUntil"/>
	public new Constraint<T, TEvidence> WithSeverityUntil(Severity before, DateTimeOffset until, Severity after)
	{
		_ = base.WithSeverityUntil(before, until, after);
		return this;
	}

	/// <inheritdoc cref="Constraint{T}.Describe"/>
	public new Constraint<T, TEvidence> Describe(string description)
	{
		_ = base.Describe(description);
		return this;
	}
}

/// <summary>
/// What a <see cref="ConstraintSet{T}"/> found in one value: every violation, and typed access to them by rule. The one object a loader keeps after checking:
/// it reports the violations (<see cref="Violations"/>), decides whether the value is usable (<see cref="HasErrors"/>), and keeps what the rules did not reject
/// (<see cref="Keep{TItem}"/>), all by rule handle rather than by id string.
/// </summary>
public sealed class ConstraintReport<T>
{
	internal ConstraintReport(T value, IReadOnlyList<Violation> violations)
	{
		Value = value;
		Violations = violations;
	}

	/// <summary>The value that was checked.</summary>
	public T Value { get; }

	/// <summary>Every violation, in set order, each carrying its rule's id.</summary>
	public IReadOnlyList<Violation> Violations { get; }

	/// <summary>True when a rule reported an <see cref="Severity.Error"/>: warnings and hints never make a value unusable.</summary>
	public bool HasErrors => Violations.Any(v => v.Severity == Severity.Error);

	/// <summary>The <see cref="Severity.Error"/> violations.</summary>
	public IEnumerable<Violation> Errors => Violations.Where(v => v.Severity == Severity.Error);

	/// <summary>The violations of one rule, matched by the rule itself rather than by its id string. A rule that is not in the set (or has no name yet) has none.</summary>
	public IReadOnlyList<Violation> Of(Constraint<T> rule) => [.. Violations.Where(v => rule.Name is not null && v.ConstraintId == rule.Name)];

	/// <summary>
	/// The evidence of the rule's <see cref="Severity.Error"/> violations: the set of things the rule rejected. A warning or a hint never rejects anything,
	/// and a violation whose evidence is not a <typeparamref name="TEvidence"/> is ignored.
	/// </summary>
	public IReadOnlySet<TEvidence> Rejected<TEvidence>(Constraint<T, TEvidence> rule) =>
		Of(rule).Where(v => v.Severity == Severity.Error).Select(v => v.Evidence).OfType<TEvidence>().ToHashSet();

	/// <summary><paramref name="items"/> without the ones <paramref name="rule"/> rejected, where the rule's evidence is the item itself. Order is kept; the items are not copied.</summary>
	public IEnumerable<TItem> Keep<TItem>(IEnumerable<TItem> items, Constraint<T, TItem> rule) => Keep(items, rule, item => item);

	/// <summary>
	/// <paramref name="items"/> without the ones <paramref name="rule"/> rejected, where the rule's evidence is derived from the item by <paramref name="evidenceOf"/>
	/// (the trimmed text the rule reported, a <c>RejectedApi(key)</c> record). Returns the original items, in order.
	/// </summary>
	public IEnumerable<TItem> Keep<TItem, TEvidence>(IEnumerable<TItem> items, Constraint<T, TEvidence> rule, Func<TItem, TEvidence> evidenceOf)
	{
		var rejected = Rejected(rule);
		return rejected.Count == 0 ? items : items.Where(item => !rejected.Contains(evidenceOf(item)));
	}
}

public sealed partial class ConstraintSet<T>
{
	/// <summary>
	/// Checks <paramref name="value"/> like <see cref="Validate"/> and returns a <see cref="ConstraintReport{T}"/>: the violations plus typed access to what each rule rejected.
	/// A rule about a change (<see cref="ConstraintKind.Sequential"/>) is not run: there is no "before" here.
	/// </summary>
	public ConstraintReport<T> Report(T value, TimeProvider? time = null) => new(value, Validate(value, time));
}
