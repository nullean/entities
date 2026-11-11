// Nullean.Entities.ComponentModel — constraint violations through INotifyDataErrorInfo.
//
// A view model is mutable; a constraint set is over an immutable domain value. ConstraintErrors<T> bridges the two: it builds the domain value from the
// view model's current state (a projection), runs the set, and reports the violations per property the way Avalonia, WPF, WinUI and MAUI bindings read
// them. BCL only (System.ComponentModel), no reflection: trim and NativeAOT clean.

using System.Collections;
using System.ComponentModel;

namespace Nullean.Entities.ComponentModel;

/// <summary>How <see cref="ConstraintErrors{T}"/> turns violations into binding errors.</summary>
public sealed record ConstraintErrorsOptions
{
	/// <summary>
	/// The least severe violation that is reported. <see cref="Severity.Error"/> (the default) reports errors only; <see cref="Severity.Warning"/> adds warnings,
	/// <see cref="Severity.Hint"/> reports everything. A reported warning shows like an error: INotifyDataErrorInfo has no severity.
	/// </summary>
	public Severity MinimumSeverity { get; init; } = Severity.Error;

	/// <summary>
	/// Domain member name (<see cref="Violation.Member"/>) to view-model property name, for the members whose names differ. A member not in the map is reported
	/// under its own name.
	/// </summary>
	public IReadOnlyDictionary<string, string>? Members { get; init; }

	/// <summary>The clock the time-aware rules read; <see cref="TimeProvider.System"/> when null.</summary>
	public TimeProvider? Time { get; init; }
}

/// <summary>
/// An <see cref="INotifyDataErrorInfo"/> a view model owns and delegates to. <see cref="Validate()"/> builds the domain value with the projection, runs the
/// constraint set, and raises <see cref="ErrorsChanged"/> only for the properties whose errors changed. Violations with no <see cref="Violation.Member"/> are
/// entity-level errors: <c>GetErrors(null)</c> or <c>GetErrors("")</c>.
/// </summary>
/// <typeparam name="T">The domain value the constraint set is over.</typeparam>
public sealed class ConstraintErrors<T> : INotifyDataErrorInfo
{
	private static readonly IReadOnlyList<Violation> None = [];

	private readonly ConstraintSet<T> _constraints;
	private readonly Func<T> _current;
	private readonly object _sender;
	private readonly ConstraintErrorsOptions _options;
	private Dictionary<string, IReadOnlyList<Violation>> _byProperty = [];

	/// <param name="constraints">The rules over the domain value.</param>
	/// <param name="current">Builds the domain value from the view model's current state; called on every <see cref="Validate()"/>.</param>
	/// <param name="sender">The <c>sender</c> of <see cref="ErrorsChanged"/>: pass the view model that delegates to this instance. This instance when null.</param>
	/// <param name="options">Severity and member mapping; the defaults report errors only, under their own member names.</param>
	public ConstraintErrors(ConstraintSet<T> constraints, Func<T> current, object? sender = null, ConstraintErrorsOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(constraints);
		ArgumentNullException.ThrowIfNull(current);
		_constraints = constraints;
		_current = current;
		_sender = sender ?? this;
		_options = options ?? new ConstraintErrorsOptions();
	}

	/// <summary>Raised for each property whose reported errors changed in the last <see cref="Validate()"/>; a null property name for the entity-level errors.</summary>
	public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

	/// <summary>True when the last validation reported anything (at <see cref="ConstraintErrorsOptions.MinimumSeverity"/> or worse), entity-level errors included.</summary>
	public bool HasErrors => _byProperty.Count > 0;

	/// <summary>Every reported violation from the last validation, in the order the set produced them.</summary>
	public IReadOnlyList<Violation> Violations { get; private set; } = None;

	/// <summary>
	/// Runs the point-in-time rules over the current value (<see cref="ConstraintSet{T}.Validate"/>) and raises <see cref="ErrorsChanged"/> for each property
	/// whose errors changed. Returns the reported violations.
	/// </summary>
	public IReadOnlyList<Violation> Validate() => Update(_constraints.Validate(_current(), _options.Time));

	/// <summary>
	/// For an edit of an existing entity: runs every rule, the rules about a change included, over <paramref name="previous"/> and the current value
	/// (<see cref="ConstraintSet{T}.Check"/>). Keep the value the edit started from and pass it on every call.
	/// </summary>
	public IReadOnlyList<Violation> Validate(T previous) => Update(_constraints.Check(previous, _current(), _options.Time));

	/// <summary>Forgets every reported error (a form reset) and raises <see cref="ErrorsChanged"/> for each property that had one.</summary>
	public void Clear() => Update(None);

	/// <summary>
	/// The messages for <paramref name="propertyName"/> (a view-model property name, after <see cref="ConstraintErrorsOptions.Members"/>), or the entity-level
	/// messages for null or empty. Strings, so every binding engine can show them as they are.
	/// </summary>
	public IEnumerable GetErrors(string? propertyName) => GetViolations(propertyName).Select(v => v.Message);

	/// <summary>The violations behind <see cref="GetErrors"/>, for a view that wants the severity, rule name or evidence.</summary>
	public IReadOnlyList<Violation> GetViolations(string? propertyName) => _byProperty.TryGetValue(propertyName ?? "", out var violations) ? violations : None;

	private List<Violation> Update(IReadOnlyList<Violation> all)
	{
		var reported = all.Where(v => v.Severity <= _options.MinimumSeverity).ToList();
		Dictionary<string, IReadOnlyList<Violation>> next = [];
		foreach (var group in reported.GroupBy(PropertyOf, StringComparer.Ordinal))
		{
			next[group.Key] = [.. group];
		}

		var previous = _byProperty;
		_byProperty = next;
		Violations = reported;

		foreach (var key in previous.Keys.Union(next.Keys, StringComparer.Ordinal).ToList())
		{
			var before = previous.TryGetValue(key, out var b) ? b : None;
			var after = next.TryGetValue(key, out var a) ? a : None;
			if (!before.Select(v => v.Message).SequenceEqual(after.Select(v => v.Message), StringComparer.Ordinal))
			{
				ErrorsChanged?.Invoke(_sender, new DataErrorsChangedEventArgs(key.Length == 0 ? null : key));
			}
		}
		return reported;
	}

	private string PropertyOf(Violation violation) => violation.Member switch
	{
		null or "" => "",
		var member when _options.Members is { } map && map.TryGetValue(member, out var property) => property,
		var member => member,
	};
}
