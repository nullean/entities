// Nullean.Entities.CsCheck — Boundaries(): where do the rules start and stop saying no?
//
// Start from a valid value, swap in each edge value for ONE property at a time, and record which constraints
// flip. The report reads like documentation the constraints never wrote ("Quantity: 0 and -1 are rejected by
// PositiveQuantity; int.MaxValue is accepted"), and, more usefully, lists the hostile values nothing rejected at
// all: a name of " ", a NaN, an undefined enum, an empty Guid. Those are gaps in the rules, not in the data.

using System.Globalization;
using System.Text;

namespace Nullean.Entities.CsCheck;

public sealed record BoundaryResult(string Property, string Value, IReadOnlyList<string> RejectedBy, bool LooksHostile);

public sealed class BoundaryReport(string baseline, IReadOnlyList<BoundaryResult> results)
{
	public string Baseline { get; } = baseline;

	public IReadOnlyList<BoundaryResult> Results { get; } = results;

	/// <summary>Hostile-looking values that **no** constraint rejected: the gaps.</summary>
	public IReadOnlyList<BoundaryResult> Gaps => [.. Results.Where(r => r.RejectedBy.Count == 0 && r.LooksHostile)];

	/// <summary>The constraints that rejected at least one swapped-in value of <paramref name="property"/>.</summary>
	public IReadOnlyList<string> RulesFor(string property) => [.. Results.Where(r => r.Property == property).SelectMany(r => r.RejectedBy).Distinct()];

	public override string ToString()
	{
		var sb = new StringBuilder().AppendLine(CultureInfo.InvariantCulture, $"Boundaries from {Baseline}:");
		foreach (var group in Results.GroupBy(r => r.Property))
		{
			var rejected = group.Where(r => r.RejectedBy.Count > 0).ToList();
			var gaps = group.Where(r => r.RejectedBy.Count == 0 && r.LooksHostile).ToList();
			sb.AppendLine(
				CultureInfo.InvariantCulture,
				$"  {group.Key}: {rejected.Count} of {group.Count()} edge values rejected, {gaps.Count} hostile values accepted"
			);
			foreach (var rule in rejected.SelectMany(r => r.RejectedBy).Distinct())
			{
				sb.AppendLine(
					CultureInfo.InvariantCulture,
					$"      rejected by {rule}: {string.Join(", ", rejected.Where(r => r.RejectedBy.Contains(rule)).Take(4).Select(r => r.Value))}"
				);
			}
			if (gaps.Count > 0)
			{
				sb.AppendLine(
					CultureInfo.InvariantCulture,
					$"      GAP, nothing rejects: {string.Join(", ", gaps.Take(8).Select(r => r.Value))}{(gaps.Count > 8 ? $", and {gaps.Count - 8} more" : "")}"
				);
			}
		}
		return sb.ToString();
	}
}

public static class ConstraintBoundaries
{
	/// <summary>
	/// Swaps every edge value (the catalog behind <c>AutoMode.Adversarial</c>) into each property of a valid
	/// <paramref name="baseline"/> in turn and reports which constraints reject it. Without a baseline, a plausible valid one is generated.
	/// </summary>
	public static BoundaryReport Boundaries<T>(
		this ConstraintSet<T> constraints,
		T? baseline = default,
		TimeProvider? time = null,
		Action<string>? writeLine = null
	)
	{
		baseline ??= AutoDefaults.ValidStarts(constraints, time)[0];
		var results = Swaps(constraints, baseline, time).Select(w => new BoundaryResult(w.Property, w.Value, w.RejectedBy, w.Hostile)).ToList();
		var report = new BoundaryReport(ConstraintProperties.SafePrint(baseline), results);
		writeLine?.Invoke(report.ToString());
		return report;
	}

	/// <summary>One edge value swapped into one property of the baseline, with the constraints that rejected the result (the instance is null when the constructor refused).</summary>
	internal sealed record Swap<T>(string Property, string Value, T? Instance, IReadOnlyList<string> RejectedBy, bool Hostile);

	internal static IEnumerable<Swap<T>> Swaps<T>(ConstraintSet<T> constraints, T baseline, TimeProvider? time)
	{
		var builder = new AutoBuilder(new AutoOptions { Mode = AutoMode.Adversarial });
		var plan = builder.Plan(typeof(T), 0);
		var args = plan.ReadArgs(baseline!) ?? throw new InvalidOperationException(
			$"{typeof(T).Name} has constructor or settable members that are not readable properties, so single values cannot be swapped."
		);

		for (var i = 0; i < args.Length; i++)
		{
			foreach (var edge in Edges(builder, plan.Types[i]))
			{
				object? instance = null;
				string? refused = null;
				try
				{
					var swapped = (object?[])args.Clone();
					swapped[i] = edge;
					instance = plan.Construct(swapped);
				}
				catch (System.Reflection.TargetInvocationException e)
				{
					refused = $"constructor: {e.InnerException?.GetType().Name}";
				}
				if (refused is not null)
				{
					yield return new Swap<T>(plan.Names[i], Show(edge), default, [refused], LooksHostile(edge));
					continue;
				}
				var rejectedBy = ConstraintProperties.SafeValidate(constraints, (T)instance!, time).Select(v => v.ConstraintId ?? "?").Distinct().ToList();
				yield return new Swap<T>(plan.Names[i], Show(edge), (T)instance!, rejectedBy, LooksHostile(edge));
			}
		}
	}

	private static IEnumerable<object?> Edges(AutoBuilder builder, Type type)
	{
		var underlying = Nullable.GetUnderlyingType(type);
		if (underlying is not null || (!type.IsValueType && type != typeof(string)))
		{
			yield return null;
		}
		var leaf = builder.ForType(underlying ?? type) as LeafGen;
		foreach (var edge in leaf?.Edges ?? [])
		{
			yield return edge;
		}
	}

	private static string Show(object? value) => value switch
	{
		null => "null",
		string s when s.Length > 24 => $"\"{Visible(s[..20])}...\" ({s.Length} chars)",
		string s => $"\"{Visible(s)}\"",
		double d => d.ToString("R", CultureInfo.InvariantCulture),
		_ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?",
	};

	private static string Visible(string s)
	{
		var sb = new StringBuilder();
		foreach (var c in s)
		{
			sb.Append(char.IsControl(c) || (c != ' ' && char.GetUnicodeCategory(
					c
				) is UnicodeCategory.Format or UnicodeCategory.SpaceSeparator or UnicodeCategory.Surrogate)
					? string.Create(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}")
					: c.ToString());
		}
		return sb.ToString();
	}

	private static bool IsInvisibleOrControl(char c) =>
		char.IsControl(c) || char.IsSurrogate(c) || char.GetUnicodeCategory(
			c
		) is UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned;

	/// <summary>Values nobody means to store: blank or control-laden strings, NaN/infinity, negatives, extremes, undefined enums, empty ids.</summary>
	private static bool LooksHostile(object? value) => value switch
	{
		null => true,
		string s => string.IsNullOrWhiteSpace(s) || s.Length > 1000 || s.Trim() != s || s.Any(IsInvisibleOrControl),
		double d => !double.IsFinite(d) || d is < 0 or double.MaxValue,
		float f => !float.IsFinite(f) || f < 0,
		decimal m => m is < 0 or decimal.MaxValue,
		int i => i is < 0 or int.MaxValue,
		long l => l is < 0 or long.MaxValue,
		short sh => sh < 0,
		sbyte sb => sb < 0,
		Guid g => g == Guid.Empty,
		DateTime dt => dt == default || dt == DateTime.MaxValue,
		DateTimeOffset dto => dto == default || dto == DateTimeOffset.MaxValue,
		DateOnly d => d == default || d == DateOnly.MaxValue,
		TimeSpan ts => ts < TimeSpan.Zero || ts == TimeSpan.MaxValue,
		Enum e => !Enum.IsDefined(e.GetType(), e),
		_ => false,
	};
}
