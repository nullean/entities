// Nullean.Entities.CsCheck — the small generators AutoBuilder composes. Each draws straight from CsCheck's PCG
// (no locks, no allocation per combinator) and reports a Size so CsCheck can shrink: smaller index, smaller
// magnitude and fewer items are all "simpler".

using CsCheck;

namespace Nullean.Entities.CsCheck;

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
		size = new Size(s);
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
