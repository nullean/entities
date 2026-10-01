// Nullean.Entities.CsCheck — what EntitySpec<T> and the property helpers fall back to when you give
// them nothing: AutoBogus-generated starting states and argument values (drawn from fixed seeds, so
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
	private const int MaxModeStarts = 8;
	private const int Attempts = 400;

	/// <summary>Up to <paramref name="count"/> distinct AutoBogus values of <paramref name="type"/>, from fixed seeds.</summary>
	public static object[] Domain(Type type, int count = DomainSize)
	{
		try
		{
			return (object[])typeof(AutoDefaults).GetMethod(nameof(Draw), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(
				null,
				[count]
			)!;
		}
		catch (TargetInvocationException e) when (e.InnerException is not null)
		{
			throw new InvalidOperationException(
				$"AutoBogus could not generate a {type.Name}: {e.InnerException.Message}. Declare the values with .Domain<{type.Name}>(...).",
				e.InnerException
			);
		}
	}

	/// <summary>A generator of AutoBogus argument values (boxed), or a constant null for an action without an argument.</summary>
	public static Gen<object?> ArgumentGen(Type? type) =>
		type is null
			? Gen.Const(() => (object?)null)
			: (Gen<object?>)typeof(AutoDefaults).GetMethod(nameof(ArgumentGenOf), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(
				null,
				null
			)!;

	private static Gen<object?> ArgumentGenOf<T>() => GenAuto.Of<T>().Select(x => (object?)x);

	private static object[] Draw<T>(int count)
	{
		var gen = GenAuto.Of<T>();
		try
		{
			var seen = new List<object>();
			for (var i = 0; i < count * 10 && seen.Count < count; i++)
			{
				var value = gen.Generate(new PCG(1u, (ulong)i + 1), null, out _);
				if (value is not null && !seen.Contains(value))
				{
					seen.Add(value);
				}
			}
			return [.. seen];
		}
		finally
		{
			(gen as IDisposable)?.Dispose();
		}
	}

	/// <summary>
	/// AutoBogus values of <typeparamref name="T"/> that already satisfy <paramref name="constraints"/>.
	/// Three random starts could all land in the same status, so when the type has enum or bool
	/// properties (its modes) one start is kept per distinct combination, up to eight; otherwise
	/// the first three distinct values.
	/// </summary>
	public static T[] ValidStarts<T>(ConstraintSet<T> constraints, TimeProvider? time, int count = StartCount)
	{
		var gen = GenAuto.Of<T>();
		try
		{
			var candidates = new List<T>();
			var keys = new HashSet<object?>();
			var rejections = new Dictionary<string, int>();
			for (var i = 0; i < Attempts; i++)
			{
				var value = gen.Generate(new PCG(2u, (ulong)i + 1), null, out _);
				var violations = constraints.Validate(value, time);
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
			var byMode = candidates.GroupBy(c => StructuralKey.Mode(c)).Select(g => g.First()).Take(MaxModeStarts).ToList();
			return byMode.Count > 1 ? [.. byMode] : [.. candidates.Take(count)];
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

	/// <summary>The enum and bool property values of <paramref name="value"/>: which "mode" it is in.</summary>
	public static string Mode(object? value) =>
		value is null
			? ""
			: string.Join(
				",",
				PropertiesOf(value.GetType()).Where(p => p.PropertyType.IsEnum || p.PropertyType == typeof(bool)).Select(p => $"{p.GetValue(value)}")
			);

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
