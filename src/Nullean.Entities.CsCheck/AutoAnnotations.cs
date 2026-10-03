// Nullean.Entities.CsCheck — DataAnnotations the generator respects: [Range], [StringLength], [MaxLength], [MinLength], [Required], [RegularExpression].
//
// Plausible data satisfies them (a value the attributes forbid is coerced into range). Mixed and Adversarial draw the boundary of each rule as an
// edge: the smallest and largest valid value, and the value just outside, which is where an off-by-one in the rule (or in the code beside it) lives.

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using CsCheck;

namespace Nullean.Entities.CsCheck;

internal sealed record AnnotationRule(Func<object, bool> Valid, Func<object, object> Coerce, object?[] Inside, object?[] Outside);

internal static class AutoAnnotations
{
	private static readonly string[] RegexPool =
	[
		"abc",
		"a1b2",
		"ABC-123",
		"123",
		"a-b_c",
		"user@example.com",
		"https://example.com",
		"2026-01-01",
		"10.0.0.1",
		"550e8400-e29b-41d4-a716-446655440000",
	];

	public static List<AnnotationRule> RulesFor(Type memberType, IEnumerable<Attribute> attributes)
	{
		var type = Nullable.GetUnderlyingType(memberType) ?? memberType;
		var rules = new List<AnnotationRule>();
		foreach (var attribute in attributes)
		{
			var rule = attribute switch
			{
				RangeAttribute range when IsNumeric(type) => Range(type, range),
				StringLengthAttribute length when type == typeof(string) => Length(length.MinimumLength, length.MaximumLength),
				MaxLengthAttribute max when type == typeof(string) && max.Length > 0 => Length(0, max.Length),
				MinLengthAttribute min when type == typeof(string) => Length(min.Length, int.MaxValue),
				RequiredAttribute when type == typeof(string) => Required(),
				RegularExpressionAttribute regex when type == typeof(string) => Pattern(regex.Pattern),
				_ => null,
			};
			if (rule is not null)
			{
				rules.Add(rule);
			}
		}
		return rules;
	}

	private static bool IsNumeric(Type t) =>
		t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(sbyte) || t == typeof(uint) || t == typeof(ulong)
		|| t == typeof(ushort) || t == typeof(double) || t == typeof(float) || t == typeof(decimal);

	private static AnnotationRule? Range(Type type, RangeAttribute range)
	{
		try
		{
			var min = (IComparable)Convert.ChangeType(range.Minimum, type, CultureInfo.InvariantCulture);
			var max = (IComparable)Convert.ChangeType(range.Maximum, type, CultureInfo.InvariantCulture);
			var outside = new List<object?>();
			if (Step(min, type, -1) is { } below)
			{
				outside.Add(below);
			}
			if (Step(max, type, +1) is { } above)
			{
				outside.Add(above);
			}
			return new AnnotationRule(
				v => ((IComparable)v).CompareTo(min) >= 0 && ((IComparable)v).CompareTo(max) <= 0,
				v => ((IComparable)v).CompareTo(min) < 0 ? min : ((IComparable)v).CompareTo(max) > 0 ? max : v,
				[min, max],
				[.. outside]
			);
		}
		catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
		{
			return null;
		}
	}

	// One representable step away from a bound, or null where there is none (int.MaxValue + 1).
	private static object? Step(IComparable bound, Type type, int direction)
	{
		try
		{
			return bound switch
			{
				double d => direction < 0 ? Math.BitDecrement(d) : Math.BitIncrement(d),
				float f => direction < 0 ? MathF.BitDecrement(f) : MathF.BitIncrement(f),
				_ => Convert.ChangeType(Convert.ToDecimal(bound, CultureInfo.InvariantCulture) + direction, type, CultureInfo.InvariantCulture),
			};
		}
		catch (OverflowException)
		{
			return null;
		}
	}

	private static AnnotationRule Length(int min, int max) =>
		new(
			v => ((string)v).Length >= min && ((string)v).Length <= max,
			v =>
			{
				var s = (string)v;
				if (s.Length > max)
				{
					s = s[..max];
				}
				return s.Length < min ? s + new string('a', min - s.Length) : s;
			},
			[.. new[] { min, max }.Where(n => n is >= 0 and <= 10_000).Distinct().Select(n => (object?)new string('a', n))],
			[.. new[] { min - 1, max + 1 }.Where(n => n is >= 0 and <= 10_001).Select(n => (object?)new string('a', n))]
		);

	private static AnnotationRule Required() =>
		new(v => !string.IsNullOrWhiteSpace((string)v), _ => "value", ["a"], ["", " ", "\t"]);

	private static AnnotationRule? Pattern(string pattern)
	{
		try
		{
			var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200));
			var matching = RegexPool.Where(candidate => SafeMatch(regex, candidate)).ToArray();
			var failing = RegexPool.Where(candidate => !SafeMatch(regex, candidate)).Take(3).ToArray();
			return new AnnotationRule(
				v => SafeMatch(regex, (string)v),
				v => matching.Length > 0 ? matching[Math.Abs(v.GetHashCode()) % matching.Length] : v,
				[.. matching.Take(3).Cast<object?>()],
				[.. failing.Cast<object?>()]
			);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	private static bool SafeMatch(Regex regex, string value)
	{
		try
		{
			return regex.IsMatch(value);
		}
		catch (RegexMatchTimeoutException)
		{
			return false;
		}
	}
}

/// <summary>Draws from the inner generator, then keeps the value inside the annotations, or, at the rate the mode allows, lets a violation through or draws a boundary.</summary>
internal sealed class AnnotatedGen(Gen<object?> inner, List<AnnotationRule> rules, AutoOptions options) : Gen<object?>
{
	private readonly object?[] _boundaries = [.. rules.SelectMany(r => r.Inside.Concat(r.Outside))];

	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		var edge = options.Mode switch
		{
			AutoMode.Plausible => false,
			AutoMode.Adversarial => pcg.Next(100) < 60,
			_ => pcg.Next(100) < (uint)options.EdgePercent,
		};
		if (edge && _boundaries.Length > 0)
		{
			var index = pcg.Next((uint)_boundaries.Length);
			size = new Size(index);
			return _boundaries[index];
		}
		var value = inner.Generate(pcg, min, out size);
		if (value is null || rules.All(r => r.Valid(value)))
		{
			return value;
		}
		if (options.Mode != AutoMode.Plausible && pcg.Next(100) < (uint)options.EdgePercent)
		{
			return value;
		}
		foreach (var rule in rules)
		{
			value = rule.Coerce(value);
		}
		return value;
	}
}
