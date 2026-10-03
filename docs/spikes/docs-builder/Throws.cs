using CsCheck;
using Elastic.Documentation.Catalog;
using Elastic.Documentation.DocumentationSets;
using Elastic.Documentation.Publishing;
using Nullean.Entities.CsCheck;
using Nullean.Entities.CsCheck.FileSystem;

namespace SpikeApp;

public static class Throws
{
	public static void Run()
	{
		Console.WriteLine("=== which of their rules throw ===");
		foreach (var mode in new[] { AutoMode.Mixed, AutoMode.Adversarial })
		{
			var docs = Enumerable
				.Range(0, 300)
				.Select(i => GenAuto.Of<DocumentationSetInCatalog>(mode, o => o.UseFileSystem()).Generate(new PCG(4u, (ulong)i + 1), null, out _))
				.ToList();
			var thrown = new Dictionary<string, (int Count, string Message, string Frame)>();
			foreach (var constraint in DocumentationSetConstraints.All)
			{
				foreach (var doc in docs)
				{
					try
					{
						_ = constraint.Check(doc).ToList();
					}
					catch (Exception e)
					{
						var frame = e.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("Elastic.Documentation", StringComparison.Ordinal))?.Trim() ?? "";
						thrown[constraint.Id] = (thrown.GetValueOrDefault(constraint.Id).Count + 1, $"{e.GetType().Name}: {e.Message.Split('\n')[0]}", frame);
					}
				}
			}
			Console.WriteLine($"--- DocumentationSetConstraints, {mode}");
			foreach (var (id, t) in thrown)
			{
				Console.WriteLine($"   {id}: threw {t.Count}x  {t.Message}  @ {t.Frame[..Math.Min(150, t.Frame.Length)]}");
			}
			var navs = Enumerable
				.Range(0, 300)
				.Select(i => GenAuto.Of<SiteNavigationDefinition>(mode).Generate(new PCG(5u, (ulong)i + 1), null, out _))
				.ToList();
			foreach (var constraint in SiteNavigationConstraints.All)
			{
				var count = 0;
				foreach (var nav in navs)
				{
					try
					{
						_ = constraint.Check(nav).ToList();
					}
					catch (Exception)
					{
						count++;
					}
				}
				Console.WriteLine($"--- SiteNavigationConstraints {constraint.Id}: threw {count}/300 ({mode})");
			}
		}
	}
}
