using FluentValidation;
using FluentValidation.Results;

namespace Nullean.Entities.FluentValidation;

/// <summary>Turns FluentValidation validators into constraints, so <c>Probe</c>, <c>RejectsInvalid</c> and the other property tests can attack them with generated data.</summary>
public static class ValidatorConstraints
{
	// A static extension block on a static class has an unnamed receiver; curb writes it "(Constraints<T> )" and IDE0055 wants "(Constraints<T>)", so the
	// line is left to curb.
#pragma warning disable IDE0055
	extension<T>(Constraints<T>)
#pragma warning restore IDE0055
	{
		/// <summary>
		/// The validator as one point-in-time constraint named <paramref name="name"/> (the validator's type name by default). Each
		/// <see cref="ValidationFailure"/> becomes a <see cref="Violation"/> (see <see cref="ToViolation"/>). The validator is run synchronously, as every
		/// constraint is: a validator with async rules (<c>MustAsync</c>, <c>CustomAsync</c>) throws FluentValidation's
		/// <see cref="AsyncValidatorInvokedSynchronouslyException"/> when the constraint is checked.
		/// </summary>
		// CA1000 reads a static extension member as a static member declared on a generic type; it is called as Constraints<T>.FromValidator, like Check.
#pragma warning disable CA1000
		public static Constraint<T> FromValidator(IValidator<T> validator, string? name = null)
#pragma warning restore CA1000
		{
			ArgumentNullException.ThrowIfNull(validator);
			return Constraints<T>.Check(value => validator.Validate(value).Errors.Select(ToViolation)).Named(name ?? validator.GetType().Name);
		}
	}

	/// <summary>The validator as a constraint (see <c>Constraints&lt;T&gt;.FromValidator</c>).</summary>
	public static Constraint<T> ToConstraint<T>(this IValidator<T> validator, string? name = null) => Constraints<T>.FromValidator(validator, name);

	/// <summary>The validator as a set of one constraint, ready for <c>Probe</c>, <c>ForAll</c> or <c>ConstraintSet.Merge</c> with the rules you already have.</summary>
	public static ConstraintSet<T> ToConstraintSet<T>(this IValidator<T> validator, string? name = null)
	{
		var constraint = validator.ToConstraint(name);
		return ConstraintSet.From((constraint.Name!, constraint));
	}

	/// <summary>
	/// One FluentValidation failure as a violation: the message, <c>PropertyName</c> as <see cref="Violation.Member"/>, <c>AttemptedValue</c> as
	/// <see cref="Violation.Evidence"/>, and Error, Warning and Info as Error, Warning and Hint. The violation's <see cref="Violation.ConstraintId"/> is the
	/// constraint's name once a set reports it; FluentValidation's <c>ErrorCode</c> is not kept.
	/// </summary>
	public static Violation ToViolation(ValidationFailure failure)
	{
		ArgumentNullException.ThrowIfNull(failure);
		return new Violation(failure.ErrorMessage, ConstraintValidation.FromFluent(failure.Severity))
		{
			Member = string.IsNullOrEmpty(failure.PropertyName) ? null : failure.PropertyName,
			Evidence = failure.AttemptedValue,
		};
	}
}
