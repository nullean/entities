// Nullean.Entities.CsCheck — use a ConstraintSet<T> as the oracle for a generator.

using CsCheck;

namespace Nullean.Entities.CsCheck;

public static class GenConstraintExtensions
{
	/// <summary>
	/// Only values that satisfy every point-in-time constraint in <paramref name="constraints"/>. A
	/// filter, so it fails with CsCheck's usual "Where" limit if almost nothing generated is valid —
	/// build the generator to be mostly valid (and let this catch the rest) rather than relying on it alone.
	/// </summary>
	public static Gen<T> Valid<T>(this Gen<T> gen, ConstraintSet<T> constraints, TimeProvider? time = null) =>
		gen.Where(value => constraints.Validate(value, time).Count == 0);

	/// <summary>
	/// Asserts that every generated value satisfies <paramref name="constraints"/>. A failure lists the
	/// violated constraints by name and shrinks to the smallest value that still violates one — for
	/// testing a factory, parser or mapper against the rules the domain already declares.
	/// </summary>
	public static void ShouldSatisfy<T>(
		this Gen<T> gen,
		ConstraintSet<T> constraints,
		TimeProvider? time = null,
		string? seed = null,
		long iter = -1,
		Action<string>? writeLine = null
	) =>
		gen.Sample(
			value =>
			{
				var violations = constraints.Validate(value, time);
				if (violations.Count > 0)
				{
					throw new InvalidOperationException(
						$"{value} violates: {string.Join("; ", violations.Select(v => $"{v.ConstraintId}: {v.Message}"))}"
					);
				}
			},
			writeLine,
			seed,
			iter
		);
}
