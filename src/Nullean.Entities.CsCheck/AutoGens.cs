// Nullean.Entities.CsCheck — the small generators AutoBuilder composes. Each draws straight from CsCheck's PCG
// (no locks, no allocation per combinator) and reports a Size so CsCheck can shrink: smaller index, smaller
// magnitude and fewer items are all "simpler".

using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>
/// How generated values report their <see cref="Size"/>, which CsCheck minimises while shrinking. Structure comes first: every collection item counts one in the high bits, and the
/// magnitude of the leaves (a number, a string's length) is added below, clamped so a few large numbers cannot spill into the structure. Fewer items beats smaller numbers, which is
/// what shrinks a failure to its fewest items; the sum this replaced let a bigger structure with small values win and stopped at 5 or 6 items.
/// </summary>
internal static class AutoSizes
{
	public const int StructureShift = 40;

	/// <summary>The most one leaf adds, so about 256 of the largest leaves fit below the structure bits.</summary>
	public const ulong LeafCap = (1UL << 32) - 1;

	/// <summary>
	/// Early exit. CsCheck passes the smallest failing size so far as <paramref name="min"/> and only runs the property on a candidate below it; the rest are generated in full and
	/// skipped. Once a collection's running <paramref name="size"/> is not below <paramref name="min"/>, no further item can make it so, and the candidate will be skipped anyway.
	/// A value is "big" when a collection plans at least <see cref="AutoOptions.EarlyExitFrom"/> items (known before any is drawn), so a small value is generated exactly as before
	/// and only a big one is cut short, from its first item.
	/// </summary>
	public static bool CannotBeSmaller(AutoOptions options, Size? min, int planned, Size size) =>
		min is { } floor && planned >= options.EarlyExitFrom && !Size.IsLessThan(size, floor);
}

internal sealed class ConstGen(object? value) : Gen<object?>
{
	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		size = new Size(0);
		return value;
	}
}

/// <summary>A value computed from the PCG, with a size derived from it.</summary>
internal sealed class FuncGen(Func<PCG, (object? Value, ulong Size)> draw) : Gen<object?>
{
	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		var (value, s) = draw(pcg);
		size = new Size(Math.Min(s, AutoSizes.LeafCap));
		return value;
	}
}

/// <summary>An index into an ordered list, simplest first, so a smaller index shrinks better.</summary>
internal sealed class IndexGen(object?[] values) : Gen<object?>
{
	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		// Half the picks come from the first dozen (the simplest and most common edges: "", " ", 0, 1, -1), the rest from
		// the whole catalog, so the everyday edges are not diluted by a long tail of exotic ones.
		var core = (uint)Math.Min(values.Length, 12);
		var index = pcg.Next(2) == 0 ? pcg.Next(core) : pcg.Next((uint)values.Length);
		size = new Size(index);
		return values[index];
	}
}

internal sealed class NullGen(Gen<object?> inner, int percent) : Gen<object?>
{
	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		if (percent > 0 && pcg.Next(100) < (uint)percent)
		{
			size = new Size(0);
			return null;
		}
		return inner.Generate(pcg, min, out size);
	}
}

/// <summary>Plausible values, edge values, or a mix, per <see cref="AutoMode"/>.</summary>
internal sealed class LeafGen(Gen<object?> plausible, object?[] edges, AutoOptions options) : Gen<object?>
{
	private readonly IndexGen _edge = new(edges);

	/// <summary>The edge catalog, simplest first.</summary>
	public object?[] Edges => edges;

	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		var useEdge = options.Mode switch
		{
			AutoMode.Plausible => false,
			AutoMode.Adversarial => true,
			_ => pcg.Next(100) < (uint)options.EdgePercent,
		};
		return useEdge ? _edge.Generate(pcg, min, out size) : plausible.Generate(pcg, min, out size);
	}
}

internal sealed class CastGen<T>(Gen<object?> inner) : Gen<T>
{
	public override T Generate(PCG pcg, Size? min, out Size size) => (T)inner.Generate(pcg, min, out size)!;
}

/// <summary>Picks one case of a union and wraps a value of that case type in the union's matching constructor.</summary>
internal sealed class UnionGen(Type type, (System.Reflection.ConstructorInfo Constructor, Gen<object?> Value)[] cases) : Gen<object?>
{
	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		var start = (int)pcg.Next((uint)cases.Length);
		for (var i = 0; i < cases.Length; i++)
		{
			var (constructor, value) = cases[(start + i) % cases.Length];
			var inner = value.Generate(pcg, min, out size);
			if (inner is null)
			{
				continue;
			}
			try
			{
				return constructor.Invoke([inner]);
			}
			catch (System.Reflection.TargetInvocationException)
			{
				// the case's own guard refused this value: try the next case
			}
		}
		size = new Size(0);
		return Activator.CreateInstance(type);
	}
}

/// <summary>One of several generators per draw (the concrete subtypes of an abstract type), skipping a case that yields null.</summary>
internal sealed class ChoiceGen(Gen<object?>[] cases) : Gen<object?>
{
	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		var start = (int)pcg.Next((uint)cases.Length);
		for (var i = 0; i < cases.Length; i++)
		{
			var value = cases[(start + i) % cases.Length].Generate(pcg, min, out size);
			if (value is not null)
			{
				return value;
			}
		}
		size = new Size(0);
		return null;
	}
}

/// <summary>Passes the inner value through the type's normalizer: always in plausible data, half the time in mixed, never in adversarial.</summary>
internal sealed class NormalizedGen(Gen<object?> inner, Func<object, object> fix, AutoOptions options) : Gen<object?>
{
	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		var value = inner.Generate(pcg, min, out size);
		if (value is null)
		{
			return null;
		}
		return Applies(pcg, options) ? fix(value) : value;
	}

	internal static bool Applies(PCG pcg, AutoOptions options) => options.Mode switch
	{
		AutoMode.Plausible => true,
		AutoMode.Adversarial => false,
		_ => pcg.Next(2) == 0,
	};
}
