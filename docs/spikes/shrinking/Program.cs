using System.Diagnostics;
using Bogus;
using CsCheck;
using Nullean.Entities.CsCheck;

// Spike question 1: shrinking quality.
//
// The property fails only for an order with more than three lines of which one has a quantity over 50. The smallest failing order is therefore
// four lines, one of them with a quantity of 51 (the others 0). A generator "shrinks well" when CsCheck lands near that, quickly.
//
// Three ways of building the same order are compared:
//   native   a hand-chained CsCheck generator: every field has its own Gen, sizes combine as CsCheck's own combinators do. The reference.
//   auto     GenAuto.Of<T>(): a Gen per member, composed by AutoBuilder, sizes summed per constructor argument and per item.
//   faker    GenFakerExtensions.Faker<T>(): Bogus rules draw from one PCG, and the whole object reports ONE size (the draws' count and magnitude).
//
// "single-node Size" is the faker path (one size for the node); "chained" is what the other two do (a size per member, added up).

namespace Spike;

public sealed record LineR(string Sku, int Qty);

public sealed record OrderR(IReadOnlyList<LineR> Lines);

public sealed class LineC
{
	public string Sku { get; set; } = "";

	public int Qty { get; set; }
}

public sealed class OrderC
{
	public List<LineC> Lines { get; set; } = [];
}

public readonly record struct Shape(int Lines, int MaxQty)
{
	public bool Fails => Lines > 3 && MaxQty > 50;
}

public static class Program
{
	public static void Main(string[] args)
	{
		var runs = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 40;
		Console.WriteLine(
			$"Shrinking quality: {runs} independent runs per generator; failing = more than 3 lines and a quantity over 50 (smallest: 4 lines, max qty 51)"
		);
		Console.WriteLine();

		var native = Gen
			.Select(Gen.Char.AlphaNumeric.Array[1, 8].Select(c => new string(c)), Gen.Int[0, 100])
			.List[0, 12]
			.Select(l => new Shape(l.Count, l.Count == 0 ? 0 : l.Max(x => x.Item2)));

		static Gen<Shape> Auto(int maxCollection) =>
			GenAuto
				.Of<OrderR>(AutoMode.Plausible, o => o.MaxCollection = maxCollection)
				.Select(o => new Shape(o.Lines.Count, o.Lines.Count == 0 ? 0 : o.Lines.Max(l => l.Qty)));

		var faker = GenFakerExtensions.Faker<OrderC>(f =>
				f.RuleFor(
					o => o.Lines,
					g =>
						Enumerable
							.Range(0, g.Random.Int(0, 12))
							.Select(_ => new LineC { Sku = g.Random.AlphaNumeric(g.Random.Int(1, 8)), Qty = g.Random.Int(0, 100) })
							.ToList()
				)).Select(o => new Shape(o.Lines.Count, o.Lines.Count == 0 ? 0 : o.Lines.Max(l => l.Qty)));

		Quality.Report("native (chained CsCheck gens)", Quality.Shrink(native, runs));
		Quality.Report("auto   (GenAuto, MaxCollection 4: counts 0..4)", Quality.Shrink(Auto(4), runs));
		Quality.Report("auto   (GenAuto, MaxCollection 12: counts are only 0, 3, 6, 9, 12)", Quality.Shrink(Auto(12), runs));
		Quality.Report("faker  (Bogus, one size per object)", Quality.Shrink(faker, runs));

		SizeStyles.Run(runs);
		EarlyExit.Run(runs);
	}
}
