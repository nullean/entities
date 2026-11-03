using FluentValidation;
using FluentValidation.Results;
using FvSeverity = FluentValidation.Severity;

namespace Nullean.Entities.FluentValidation;

/// <summary>
/// A FluentValidation validator for a constraint set: register it as <c>IValidator&lt;T&gt;</c>, pass it to <c>Include</c> or <c>SetValidator</c>, or derive from it to add
/// FluentValidation rules next to the constraints. Each violation becomes one <see cref="ValidationFailure"/> (see <see cref="ConstraintValidation.ToFailure"/>).
/// </summary>
public class ConstraintValidator<T> : AbstractValidator<T>
{
	public ConstraintValidator(ConstraintSet<T> constraints, TimeProvider? time = null)
	{
		ArgumentNullException.ThrowIfNull(constraints);
		Constraints = constraints;
		_ = RuleFor(x => x).SatisfiesConstraints(constraints, time);
	}

	/// <summary>The constraint set this validator checks.</summary>
	public ConstraintSet<T> Constraints { get; }
}

/// <summary>Runs constraint sets inside FluentValidation.</summary>
public static class ConstraintValidation
{
	/// <summary>
	/// Checks the property (or, with <c>RuleFor(x =&gt; x)</c>, the whole value) against <paramref name="constraints"/> and adds one failure per violation. Only the
	/// point-in-time rules run, as in <see cref="ConstraintSet{T}.Validate"/>: a validator sees one value, not a change. A null value is skipped, as FluentValidation
	/// skips a child validator for a null property; add <c>NotNull()</c> when null is wrong.
	/// </summary>
	public static IRuleBuilderOptionsConditions<T, TProperty?> SatisfiesConstraints<T, TProperty>(
		this IRuleBuilder<T, TProperty?> rule,
		ConstraintSet<TProperty> constraints,
		TimeProvider? time = null
	)
	{
		ArgumentNullException.ThrowIfNull(rule);
		ArgumentNullException.ThrowIfNull(constraints);
		return rule.Custom((value, context) =>
		{
			if (value is null)
			{
				return;
			}
			foreach (var violation in constraints.Validate(value, time))
			{
				context.AddFailure(ToFailure(violation, context.PropertyPath));
			}
		});
	}

	/// <summary>The constraint set as a FluentValidation validator: <c>validator.Include(set.ToValidator())</c>, or <c>services.AddSingleton&lt;IValidator&lt;T&gt;&gt;(set.ToValidator())</c>.</summary>
	public static ConstraintValidator<T> ToValidator<T>(this ConstraintSet<T> constraints, TimeProvider? time = null) => new(constraints, time);

	/// <summary>
	/// One violation as a FluentValidation failure. <c>PropertyName</c> is <paramref name="propertyPath"/> joined with <see cref="Violation.Member"/>
	/// (<c>Address.City</c>); a violation with no member gets the path alone, or, at the root, the rule's id. <c>ErrorCode</c> is the rule's id
	/// (<see cref="Violation.ConstraintId"/>), <c>Severity</c> maps Error, Warning and Hint to Error, Warning and Info, <c>AttemptedValue</c> is
	/// <see cref="Violation.Evidence"/>, and <c>CustomState</c> is the <see cref="Violation"/> itself.
	/// </summary>
	public static ValidationFailure ToFailure(Violation violation, string? propertyPath = null)
	{
		ArgumentNullException.ThrowIfNull(violation);
		return new ValidationFailure(PropertyName(propertyPath, violation), violation.Message, violation.Evidence)
		{
			ErrorCode = violation.ConstraintId,
			Severity = ToFluent(violation.Severity),
			CustomState = violation,
		};
	}

	/// <summary>Error, Warning and Hint as FluentValidation's Error, Warning and Info.</summary>
	public static FvSeverity ToFluent(Severity severity) => severity switch
	{
		Severity.Warning => FvSeverity.Warning,
		Severity.Hint => FvSeverity.Info,
		_ => FvSeverity.Error,
	};

	/// <summary>FluentValidation's Error, Warning and Info as Error, Warning and Hint.</summary>
	public static Severity FromFluent(FvSeverity severity) => severity switch
	{
		FvSeverity.Warning => Severity.Warning,
		FvSeverity.Info => Severity.Hint,
		_ => Severity.Error,
	};

	private static string PropertyName(string? path, Violation violation) => (string.IsNullOrEmpty(path), violation.Member) switch
	{
		(true, null) => violation.ConstraintId ?? string.Empty,
		(true, { } member) => member,
		(false, null) => path!,
		(false, { } member) => member.StartsWith('[') ? path + member : $"{path}.{member}",
	};
}
