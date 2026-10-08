using System.Diagnostics;
using CsCheck;

namespace Spike;

// The same random draws, three ways of reporting their Size. CsCheck prefers the smallest Size while shrinking, so how sizes combine decides what "simpler" means:
//
//   single-node  every part's size is ADDED into one number (what GenAuto does per constructor argument and per item)
//   chained      each line's size is APPENDED to a chain, compared lexicographically: the count first, then line 1, then line 2... (Size's own Next)
//   packed       one number with the count of draws in the high bits and their magnitude below (what PcgRandom does for the Bogus path)
//
// Only the combination differs; the values drawn are identical for a given PCG state.

public enum SizeStyle
{
	SingleNode,
	Chained,
	Packed,
}

public sealed class OrderGen(SizeStyle style, uint maxLines, bool earlyExit = false) : Gen<Shape>
{
	public override Shape Generate(PCG pcg, Size? min, out Size size)
	{
		var count = pcg.Next(maxLines + 1);
		var maxQty = 0;
		ulong draws = 1;
		ulong magnitude = count;
		var chain = new Size(count);

		for (var i = 0; i < count; i++)
		{
			var skuLength = 1 + pcg.Next(8);
			ulong lineMagnitude = skuLength;
			ulong lineDraws = 1;
			for (var c = 0; c < skuLength; c++)
			{
				_ = pcg.Next(62);
				lineDraws++;
			}
			var qty = (int)pcg.Next(101);
			lineDraws++;
			lineMagnitude += (ulong)qty;
			maxQty = Math.Max(maxQty, qty);

			switch (style)
			{
				case SizeStyle.SingleNode:
					chain.Add(new Size(lineMagnitude));
					break;
				case SizeStyle.Chained:
					chain.Append(new Size(lineMagnitude));
					break;
				default:
					draws += lineDraws;
					magnitude += lineMagnitude;
					break;
			}

			// Early exit: CsCheck only looks at a candidate whose size is below `min`. Once the running size is not below it, the rest of the value cannot make it
			// smaller (sizes only grow as parts are added), so stop drawing and hand back what there is; it is discarded either way.
			if (earlyExit && min is { } floor && style != SizeStyle.Packed && !Size.IsLessThan(chain, floor))
			{
				size = chain;
				return new Shape((int)count, maxQty);
			}
		}

		size = style == SizeStyle.Packed ? new Size((draws << 40) | Math.Min(magnitude, (1UL << 40) - 1)) : chain;
		return new Shape((int)count, maxQty);
	}
}

public static class SizeStyles
{
	public static void Run(int runs)
	{
		Console.WriteLine($"Same draws, three ways of combining Size ({runs} runs each; failing = more than 3 lines and a quantity over 50)");
		Console.WriteLine();
		foreach (var style in Enum.GetValues<SizeStyle>())
		{
			Quality.Report($"{style}", Quality.Shrink(new OrderGen(style, 12), runs));
		}
	}
}

public static class Quality
{
	public sealed record Result(Shape Final, int Calls, long Microseconds, bool Failed);

	public static List<Result> Shrink(Gen<Shape> gen, int runs)
	{
		var results = new List<Result>();
		for (var i = 0; i < runs; i++)
		{
			Shape last = default;
			var calls = 0;
			var sw = Stopwatch.StartNew();
			var failed = false;
			try
			{
				gen.Sample(
					shape =>
					{
						calls++;
						if (shape.Fails)
						{
							last = shape;
							throw new InvalidOperationException("property failed");
						}
					},
					iter: 3_000,
					threads: 1
				);
			}
			catch (CsCheckException)
			{
				failed = true;
			}
			results.Add(new Result(last, calls, (long)sw.Elapsed.TotalMicroseconds, failed));
		}
		return results;
	}

	public static void Report(string name, List<Result> results)
	{
		var failing = results.Where(r => r.Failed).ToList();
		Console.WriteLine(name);
		Console.WriteLine($"  runs that found the failure: {failing.Count}/{results.Count}");
		if (failing.Count == 0)
		{
			Console.WriteLine();
			return;
		}
		Console.WriteLine($"  shrunk to exactly 4 lines:   {failing.Count(r => r.Final.Lines == 4)}/{failing.Count}");
		Console.WriteLine($"  shrunk lines    median {Median(failing.Select(r => r.Final.Lines))}, worst {failing.Max(r => r.Final.Lines)}");
		Console.WriteLine(
			$"  shrunk max qty  median {Median(failing.Select(r => r.Final.MaxQty))}, worst {failing.Max(r => r.Final.MaxQty)}  (best possible 51)"
		);
		Console.WriteLine($"  property calls  median {Median(failing.Select(r => r.Calls))}");
		Console.WriteLine($"  time (us)       median {Median(failing.Select(r => (int)r.Microseconds))}");
		Console.WriteLine();
	}

	private static int Median(IEnumerable<int> values)
	{
		var sorted = values.Order().ToList();
		return sorted[sorted.Count / 2];
	}
}
