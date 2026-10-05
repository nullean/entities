using Nullean.Argh.Validation;

namespace Nullean.Entities.Argh;

/// <summary>
/// Registers constraint sets as Nullean.Argh validators. The generated parser runs them right after its own checks (<c>[Range]</c>, <c>[Existing]</c>, ...) for the matching
/// <c>[AsParameters]</c> or global/namespace options type, so a violation prints as <c>Error: --flag: message</c>, then the flag's help rows and the usage line, and exits with code 2.
/// </summary>
public static class ArghConstraints
{
	/// <summary>Registers <c>T.Constraints</c> for a type that declares its own constraints. Call at startup, before <c>RunAsync</c>.</summary>
	public static void Register<T>() where T : class, IHaveConstraints<T> => Register(T.Constraints);

	/// <summary>Registers a constraint set for <typeparamref name="T"/>. Errors fail the command; warnings and hints are printed to stderr and do not.</summary>
	public static void Register<T>(ConstraintSet<T> constraints) where T : class =>
		ArghValidation.Register<T>(value => Validate(constraints, value));

	private static List<ArghValidationError> Validate<T>(ConstraintSet<T> constraints, T value)
	{
		var errors = new List<ArghValidationError>();
		foreach (var v in constraints.Validate(value))
		{
			if (v.Severity == Severity.Error)
			{
				errors.Add(new ArghValidationError(v.Message, v.Member));
			}
			else
			{
				Console.Error.WriteLine($"{v.Severity}: {v.Message}");
			}
		}
		return errors;
	}
}
