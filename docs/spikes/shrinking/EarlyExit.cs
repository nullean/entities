using System.Diagnostics;
using System.Text.RegularExpressions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Spike;

// Spike question 2: early exit cost.
//
// While shrinking, CsCheck keeps asking the generator for a candidate and only runs the property on one whose Size is below the smallest failing so far; the others
// are "skipped" after being generated in full. Early exit stops generating a candidate as soon as its running Size is no longer below that bound.
// Two measurements: (1) a value of controlled size, with and without early exit, and (2) a real GenAuto tree, to see how much of a shrink is skipped candidates.

public sealed record Node(string Name, IReadOnlyList<Node> Children);

public static class EarlyExit
{
	public static void Run(int runs)
	{
		Console.WriteLine($"Early exit on a value of controlled size ({runs} runs each, up to 400 lines of up to 8 characters)");
		Console.WriteLine();
		foreach (var style in new[] { SizeStyle.SingleNode, SizeStyle.Chained })
		{
			foreach (var early in new[] { false, true })
			{
				var results = Quality.Shrink(new OrderGen(style, 400, early), runs);
				var failing = results.Where(r => r.Failed).ToList();
				Console.WriteLine(
					$"  {style,-10} early exit {(early ? "on " : "off")}: shrunk to 4 lines {failing.Count(r => r.Final.Lines == 4),3}/{failing.Count}, median {Median(failing.Select(r => (int)r.Microseconds))} us, median {Median(failing.Select(r => r.Calls))} property calls"
				);
			}
		}
		Console.WriteLine();
		Realistic(Math.Max(10, runs / 5));
	}

	private static void Realistic(int runs)
	{
		Console.WriteLine($"A real GenAuto tree (Large(): MaxDepth 6, up to 8 per collection), property fails when it has more than 3 nodes ({runs} runs)");
		var gen = GenAuto.Of<Node>(AutoMode.Plausible, o => o.Large());
		var times = new List<int>();
		var skipped = new List<int>();
		var shrinks = new List<int>();
		var totals = new List<int>();
		var pattern = new Regex(@"\((\d+) shrinks, (\d+) skipped, (\d+) total\)");
		for (var i = 0; i < runs; i++)
		{
			var sw = Stopwatch.StartNew();
			try
			{
				gen.Sample(
					node =>
					{
						var count = Count(node);
						if (count > 3)
						{
							throw new InvalidOperationException($"{count} nodes");
						}
					},
					iter: 500,
					threads: 1
				);
			}
			catch (CsCheckException e)
			{
				var m = pattern.Match(e.Message);
				if (m.Success)
				{
					shrinks.Add(int.Parse(m.Groups[1].Value));
					skipped.Add(int.Parse(m.Groups[2].Value));
					totals.Add(int.Parse(m.Groups[3].Value));
				}
			}
			times.Add((int)sw.Elapsed.TotalMicroseconds);
		}
		Console.WriteLine(
			$"  median {Median(times)} us per run; median {Median(shrinks)} shrinks, {Median(skipped)} skipped candidates, {Median(totals)} candidates in all"
		);
		var fraction = skipped.Zip(totals, (s, t) => t == 0 ? 0 : 100.0 * s / t).Order().ToList();
		Console.WriteLine($"  skipped share of all candidates: median {fraction[fraction.Count / 2]:F0}%");
		Console.WriteLine();
	}

	private static int Count(Node node) => 1 + node.Children.Sum(Count);

	private static int Median(IEnumerable<int> values)
	{
		var sorted = values.Order().ToList();
		return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
	}
}
