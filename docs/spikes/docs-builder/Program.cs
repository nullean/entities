using CsCheck;
using Elastic.Documentation.Catalog;
using Elastic.Documentation.Codex;
using Elastic.Documentation.DocumentationSets;
using Elastic.Documentation.Links;
using Elastic.Documentation.Publishing;
using Elastic.Documentation.ReleaseNotes;
using Nullean.Entities.CsCheck;
using Nullean.Entities.CsCheck.FileSystem;

// What does GenAuto make of docs-builder's real aggregates?

var results = new List<string>();

void Try<T>(string name, Action<AutoOptions>? configure = null)
{
	foreach (var mode in new[] { AutoMode.Plausible, AutoMode.Mixed, AutoMode.Adversarial })
	{
		var ok = 0;
		var failures = new Dictionary<string, int>();
		for (var i = 0; i < 100; i++)
		{
			try
			{
				GenAuto.Of<T>(mode, o =>
				{
					o.UseFileSystem();
					configure?.Invoke(o);
				}).Generate(new PCG(1u, (ulong)i + 1), null, out _);
				ok++;
			}
			catch (Exception e)
			{
				var root = e;
				while (root.InnerException is not null)
				{
					root = root.InnerException;
				}
				var text = $"{root.GetType().Name}: {root.Message.Split('\n')[0]}";
				failures[text.Length > 170 ? text[..170] : text] = failures.GetValueOrDefault(text.Length > 170 ? text[..170] : text) + 1;
			}
		}
		results.Add(
			$"{name,-34} {mode,-11} ok {ok,3}/100" + (failures.Count == 0 ? "" : "   " + string.Join(" | ", failures.Select(f => $"{f.Value}x {f.Key}")))
		);
	}
}

Try<GlobalCatalog>("GlobalCatalog");
Try<ProductCatalog>("ProductCatalog");
Try<DocumentationSetDefinition>("DocumentationSetDefinition");
Try<ResolvedDocumentationSet>("ResolvedDocumentationSet");
Try<SiteNavigationDefinition>("SiteNavigationDefinition");
Try<AssemblyConfiguration>("AssemblyConfiguration");
Try<LinkRegistry>("LinkRegistry");
Try<LinkManifest>("LinkManifest");
Try<ChangelogConfiguration>("ChangelogConfiguration");
Try<CodexConfiguration>("CodexConfiguration");
Try<DocsetDiscovery>("DocsetDiscovery");

SpikeApp.Stats.Run();
SpikeApp.Port.Run();
SpikeApp.Perf.Run();

foreach (var line in results)
{
	Console.WriteLine(line);
}
