using CsCheck;
using Elastic.Documentation.DocumentationSets;
using Nullean.Entities.CsCheck;
using Nullean.Entities.CsCheck.FileSystem;

namespace SpikeApp;

public static class Stats
{
	public static void Run()
	{
		foreach (var mode in new[] { AutoMode.Plausible, AutoMode.Mixed, AutoMode.Adversarial })
		{
			var gen = GenAuto.Of<ResolvedDocumentationSet>(mode, o => o.UseFileSystem());
			var sets = Enumerable.Range(0, 300).Select(i => gen.Generate(new PCG(2u, (ulong)i + 1), null, out _)).ToList();

			var sizes = sets.Select(s => s.TableOfContents.Descendants().Count()).ToList();
			var defaults = sets.Sum(s => s.TableOfContents.Descendants().Count(i => i.Value is null));
			var cases = sets
				.SelectMany(s => s.TableOfContents.Descendants())
				.Where(i => i.Value is not null)
				.GroupBy(i => i.Value!.GetType().Name)
				.OrderByDescending(g => g.Count())
				.Select(g => $"{g.Key}:{g.Count()}");
			var exists = sets.Count(s => s.SourceFile.Exists);
			var sameWorld = sets.Count(s => ReferenceEquals(s.SourceFile.FileSystem, s.ScopeDirectory.FileSystem));
			var withProducts = sets.Count(s => s.Products.Count > 0);
			var paths = sets.SelectMany(
				s => s.TableOfContents.Descendants().Where(i => i.Value is not null).Select(i => (string)i.Item().PathRelativeToDocumentationSet)
			).ToList();
			var repeated = sets.Count(
				s =>
					s
						.TableOfContents
						.Descendants()
						.Where(i => i.Value is not null)
						.GroupBy(i => (string)i.Item().PathRelativeToDocumentationSet)
						.Any(g => g.Count() > 1)
			);

			Console.WriteLine(
				$"--- ResolvedDocumentationSet {mode}: tree nodes min/avg/max {sizes.Min()}/{sizes.Average():0.0}/{sizes.Max()}; default unions {defaults}; cases {string.Join(" ", cases)}"
			);
			Console.WriteLine(
				$"    SourceFile exists {exists}/300; ScopeDirectory shares the file system {sameWorld}/300; sets with products {withProducts}/300; sets repeating a toc path {repeated}/300; sample paths: {string.Join(", ", paths.Distinct().Take(5))}"
			);
		}
	}
}
