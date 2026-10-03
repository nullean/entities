// Nullean.Entities.CsCheck — what EntitySpec<T> and the property helpers fall back to when you give
// them nothing: generated starting states and argument values (drawn from fixed seeds, so
// a run is reproducible), and a structural notion of "the same state".

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using CsCheck;

namespace Nullean.Entities.CsCheck;

internal static class AutoDefaults
{
	public const int DomainSize = 3;
	public const int StartCount = 3;
	private const int MaxModeStarts = 12;
	private const int Attempts = 400;

	/// <summary>
	/// A small, finite set of values to try for an argument of <paramref name="type"/>: a couple of plausible ones from fixed
	/// seeds, plus (unless <paramref name="data"/> is <see cref="AutoMode.Plausible"/>) the edge values that matter most for
	/// the type (<c>""</c> and <c>" "</c>, <c>0</c> and <c>-1</c>, <c>NaN</c>, ...). Small on purpose: the reachable state
	/// space grows with it.
	/// </summary>
	public static object[] Domain(Type type, AutoMode? data = null, int count = DomainSize)
	{
		// An enum or a bool has a handful of values: try them all instead of a few random ones, which
		// could easily miss the one a guard or a transition is about.
		if (type.IsEnum)
		{
			return [.. Enum.GetValues(type).Cast<object>()];
		}
		if (type == typeof(bool))
		{
			return [false, true];
		}
		var withEdges = data != AutoMode.Plausible;
		try
		{
			var plausible = new AutoBuilder(new AutoOptions { Mode = AutoMode.Plausible }).ForType(type);
			var values = new List<object>();
			DrawDistinct(plausible, withEdges ? 2 : count, values, 1u);
			if (withEdges)
			{
				var edgeBuilder = new AutoBuilder(new AutoOptions { Mode = AutoMode.Adversarial }).ForType(type);
				if (edgeBuilder is LeafGen leaf)
				{
					foreach (var edge in CoreEdges(type, leaf.Edges))
					{
						if (edge is not null && !values.Contains(edge))
						{
							values.Add(edge);
						}
					}
				}
				else
				{
					// An object: a plausible one is already there; add one with edge-case leaves.
					DrawDistinct(new AutoBuilder(new AutoOptions { Mode = AutoMode.Mixed }).ForType(type), 1, values, 2u);
				}
			}
			return [.. values];
		}
		catch (NotSupportedException e)
		{
			throw new InvalidOperationException($"AutoGen could not generate a {type.Name}: {e.Message} Declare the values with .Domain<{type.Name}>(...).", e);
		}
	}

	private static void DrawDistinct(Gen<object?> gen, int count, List<object> into, uint stream)
	{
		var wanted = into.Count + count;
		for (var i = 0; i < count * 10 && into.Count < wanted; i++)
		{
			var value = gen.Generate(new PCG(stream, (ulong)i + 1), null, out _);
			if (value is not null && !into.Contains(value))
			{
				into.Add(value);
			}
		}
	}

	private static bool IsMax(object? value) => value switch
	{
		DateTime dt => dt == DateTime.MaxValue,
		DateTimeOffset dto => dto == DateTimeOffset.MaxValue,
		DateOnly d => d == DateOnly.MaxValue,
		TimeOnly t => t == TimeOnly.MaxValue,
		TimeSpan ts => ts == TimeSpan.MaxValue,
		_ => false,
	};

	// The two or three edge values worth trying for an argument: the rest of the catalog is for random testing.
	private static IEnumerable<object?> CoreEdges(Type type, object?[] edges)
	{
		if (type == typeof(string) || type == typeof(Guid) || type == typeof(Uri))
		{
			return edges.Take(2);
		}
		// An "End" argument has to be able to move forward as well as back, so dates and times get both extremes.
		if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan))
		{
			return [edges[0], .. edges.Where(IsMax)];
		}
		if (type == typeof(double) || type == typeof(float))
		{
			return [edges[0], edges[2], .. edges.Where(e => e is double.NaN or float.NaN)];
		}
		return ((edges.Length >= 3 && type.IsPrimitive) || type == typeof(decimal)) ? [edges[0], edges[2]] : edges.Take(2);
	}

	/// <summary>Half plausible-and-edge-mixed, half edge-only: for sampling where both holding and violated cases are wanted.</summary>
	public static Gen<T> Hostile<T>() => Gen.OneOf(GenAuto.Of<T>(), GenAuto.Of<T>(AutoMode.Adversarial));

	/// <summary>A generator of argument values (boxed), or a constant null for an action without an argument.</summary>
	public static Gen<object?> ArgumentGen(Type? type, AutoMode data = AutoMode.Mixed) =>
		type is null
			? Gen.Const(() => (object?)null)
			: (Gen<object?>)typeof(AutoDefaults).GetMethod(nameof(ArgumentGenOf), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(
				null,
				[data]
			)!;

	private static Gen<object?> ArgumentGenOf<T>(AutoMode data) => GenAuto.Of<T>(data).Select(x => (object?)x);

	/// <summary>
	/// AutoBogus values of <typeparamref name="T"/> that already satisfy <paramref name="constraints"/>.
	/// Three random starts could all land in the same status, so when the type has enum or bool
	/// properties (its modes) one start is kept per distinct combination, up to eight; otherwise
	/// the first three distinct values.
	/// </summary>
	public static T[] ValidStarts<T>(ConstraintSet<T> constraints, TimeProvider? time, AutoMode mode = AutoMode.Plausible, int count = StartCount)
	{
		var gen = GenAuto.Of<T>(mode);
		try
		{
			var candidates = new List<T>();
			var keys = new HashSet<object?>();
			var rejections = new Dictionary<string, int>();
			for (var i = 0; i < Attempts; i++)
			{
				var value = gen.Generate(new PCG(2u, (ulong)i + 1), null, out _);
				var violations = ConstraintProperties.SafeValidate(constraints, value, time);
				if (violations.Count == 0)
				{
					if (keys.Add(StructuralKey.Of(value)))
					{
						candidates.Add(value);
					}
					continue;
				}
				foreach (var v in violations)
				{
					rejections[v.ConstraintId ?? "?"] = rejections.GetValueOrDefault(v.ConstraintId ?? "?") + 1;
				}
			}
			if (candidates.Count == 0)
			{
				throw NoValidValues<T>(Attempts, rejections);
			}
			// Keep a candidate when it brings an enum/bool value no earlier start had: with a status enum
			// that gives one start per status, so no lifecycle edge is out of reach for want of a start.
			var seen = new HashSet<string>();
			var covering = new List<T>();
			foreach (var candidate in candidates)
			{
				var fresh = false;
				foreach (var pair in StructuralKey.Modes(candidate))
				{
					fresh |= seen.Add(pair);
				}
				if (fresh && covering.Count < MaxModeStarts)
				{
					covering.Add(candidate);
				}
			}
			return covering.Count > 1 ? [.. covering] : [.. candidates.Take(count)];
		}
		finally
		{
			(gen as IDisposable)?.Dispose();
		}
	}

	public static InvalidOperationException NoValidValues<T>(int attempts, Dictionary<string, int> rejections)
	{
		var sb = new StringBuilder($"AutoBogus produced no valid {typeof(T).Name} in {attempts} attempts. Most rejecting constraints: ");
		sb.Append(string.Join(", ", rejections.OrderByDescending(r => r.Value).Take(5).Select(r => $"{r.Key} ({r.Value})")));
		sb.Append(". Pass a starting value (Spec(initial)) or a generator that builds valid ones.");
		return new InvalidOperationException(sb.ToString());
	}
}

/// <summary>
/// "The same state", structurally: two values are equal when their public properties are, with any
/// collection compared element by element. Records holding a list compare that list by reference, so
/// by default every walk would look like a new state and the space would never close.
/// </summary>
internal static class StructuralKey
{
	private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

	public static object? Of(object? value)
	{
		if (value is null || IsLeaf(value))
		{
			return value;
		}
		if (value is IEnumerable items)
		{
			return new Seq([.. items.Cast<object?>().Select(Of)]);
		}
		var props = PropertiesOf(value.GetType());
		return props.Length == 0 ? value : new Seq([value.GetType(), .. props.Select(p => Of(p.GetValue(value)))]);
	}

	/// <summary>The largest collection anywhere inside <paramref name="value"/>.</summary>
	public static int LargestCollection(object? value)
	{
		if (value is null || IsLeaf(value))
		{
			return 0;
		}
		if (value is IEnumerable items)
		{
			var count = 0;
			var nested = 0;
			foreach (var item in items)
			{
				count++;
				nested = Math.Max(nested, LargestCollection(item));
			}
			return Math.Max(count, nested);
		}
		return PropertiesOf(value.GetType()).Select(p => LargestCollection(p.GetValue(value))).DefaultIfEmpty(0).Max();
	}

	/// <summary>The enum and bool properties of <paramref name="value"/> as "Name=Value" pairs: which modes it is in.</summary>
	public static IEnumerable<string> Modes(object? value) =>
		value is null
			? []
			: PropertiesOf(value.GetType()).Where(p => p.PropertyType.IsEnum || p.PropertyType == typeof(bool)).Select(p => $"{p.Name}={p.GetValue(value)}");

	private static bool IsLeaf(object value) =>
		value is string or decimal or Guid or DateTime or DateTimeOffset or TimeSpan || value.GetType().IsPrimitive || value.GetType().IsEnum;

	private static PropertyInfo[] PropertiesOf(Type type) =>
		Properties.GetOrAdd(
			type,
			t => [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0)]
		);

	private sealed class Seq(object?[] items) : IEquatable<Seq>
	{
		public bool Equals(Seq? other) => other is not null && items.AsSpan().SequenceEqual(other.Items);

		private object?[] Items => items;

		public override bool Equals(object? obj) => obj is Seq other && Equals(other);

		public override int GetHashCode()
		{
			var hash = new HashCode();
			foreach (var item in items)
			{
				hash.Add(item);
			}
			return hash.ToHashCode();
		}
	}
}
