using System.Diagnostics;
using Nullean.Entities;

namespace SpikeApp;

public sealed record Node(string Path, IReadOnlyList<Node> Children);

public sealed record Tree1(IReadOnlyList<Node> Roots);

public sealed record File(string Path, IReadOnlyList<string> Includes);

public sealed record Files(IReadOnlyList<File> All);

public static class Perf
{
	private static Node Build(ref int counter, int depth, int width)
	{
		var path = $"folder/page-{counter++}.md";
		if (depth == 0)
		{
			return new Node(path, []);
		}
		var children = new List<Node>(width);
		for (var i = 0; i < width; i++)
		{
			children.Add(Build(ref counter, depth - 1, width));
		}
		return new Node(path, children);
	}

	private static void Measure(string name, int n, Action action)
	{
		action();
		GC.Collect();
		var before = GC.GetAllocatedBytesForCurrentThread();
		var watch = Stopwatch.StartNew();
		action();
		watch.Stop();
		Console.WriteLine(
			$"{name,-44} n={n,8}  {watch.Elapsed.TotalMilliseconds,9:0.0} ms  {(GC.GetAllocatedBytesForCurrentThread() - before) / 1024.0 / 1024.0,8:0.0} MB allocated"
		);
	}

	public static void Run()
	{
		Console.WriteLine("=== performance (Release) ===");
		foreach (var (depth, width) in new[] { (4, 10), (5, 8), (6, 7) })
		{
			var counter = 0;
			var root = Build(ref counter, depth, width);
			var tree = new Tree1([root]);
			var n = counter;

			var unique = Constraints<Tree1>.Unique(t => Tree.Flatten(t.Roots, x => x.Children), x => x.Path);
			Measure("Unique over Tree.Flatten (all distinct)", n, () => unique.Check(tree, tree).Count());
			var dup = Constraints<Tree1>.Unique(t => Tree.Flatten(t.Roots, x => x.Children), x => x.Path.Length % 997);
			Measure("Unique over Tree.Flatten (many duplicates)", n, () => dup.Check(tree, tree).Count());
			Measure("Tree.Flatten alone", n, () => Tree.Flatten(tree.Roots, x => x.Children).Count());

			var files = new Files([.. Enumerable.Range(0, n).Select(i => new File($"f{i}", i == 0 ? [] : [$"f{i - 1}", $"f{i / 2}"]))]);
			var acyclic = Constraints<Files>.Acyclic(f => f.All, x => x.Path, x => x.Includes);
			Measure("Acyclic (a chain of includes, deep)", n, () => acyclic.Check(files, files).Count());

			var refs = Constraints<Files>.References<File, string>(f => f.All, x => x.Includes, f => f.All.Select(x => x.Path));
			Measure("References (2 per item)", n, () => refs.Check(files, files).Count());
		}
	}
}
