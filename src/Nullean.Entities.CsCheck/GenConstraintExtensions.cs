// Nullean.Entities.CsCheck — use a ConstraintSet<T> as the oracle for a generator.

using CsCheck;

namespace Nullean.Entities.CsCheck;

// Like Gen.Where, but when nothing valid turns up it says which constraints did the rejecting instead
// of reporting a bare "Where limit" — the first thing to know when a generator is too loose.
internal sealed class ValidGen<T>(Gen<T> inner, ConstraintSet<T> constraints, TimeProvider? time) : Gen<T>
{
	public override T Generate(PCG pcg, Size? min, out Size size)
	{
		var rejections = new Dictionary<string, int>();
		for (var attempt = 0; attempt < Check.WhereLimit; attempt++)
		{
			var value = inner.Generate(pcg, min, out size);
			var violations = ConstraintProperties.SafeValidate(constraints, value, time);
			if (violations.Count == 0)
			{
				return value;
			}
			foreach (var v in violations)
			{
				var id = v.ConstraintId ?? "?";
				rejections[id] = rejections.GetValueOrDefault(id) + 1;
			}
		}
		throw AutoDefaults.NoValidValues<T>(Check.WhereLimit, rejections);
	}
}

public static class GenConstraintExtensions
{
	/// <summary>
	/// Only values that satisfy every point-in-time constraint in <paramref name="constraints"/>. A
	/// filter, so it fails with CsCheck's usual "Where" limit if almost nothing generated is valid —
	/// build the generator to be mostly valid (and let this catch the rest) rather than relying on it alone.
	/// </summary>
	public static Gen<T> Valid<T>(this Gen<T> gen, ConstraintSet<T> constraints, TimeProvider? time = null) => new ValidGen<T>(gen, constraints, time);

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
				var violations = ConstraintProperties.SafeValidate(constraints, value, time);
				if (violations.Count > 0)
				{
					throw new InvalidOperationException(
						$"{ConstraintProperties.SafePrint(value)} violates: {string.Join("; ", violations.Select(v => $"{v.ConstraintId}: {v.Message}"))}"
					);
				}
			},
			writeLine,
			seed,
			iter,
			print: ConstraintProperties.SafePrint
		);
}
