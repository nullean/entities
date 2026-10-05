using CsCheck;
using Elastic.Documentation.Catalog;
using Elastic.Documentation.Codex;
using Elastic.Documentation.Constraints;
using Elastic.Documentation.DocumentationSets;
using Elastic.Documentation.Publishing;
using Elastic.Documentation.ReleaseNotes;
using Nullean.Entities.CsCheck;
using Nullean.Entities.CsCheck.FileSystem;

namespace SpikeApp;

// Every constraint docs-builder's domain declares, run over generated data in all three modes.
//   violated  how often the generated data broke it (a rule that is never broken by hostile data is a tautology, a derived value, or something the generator cannot reach)
//   threw     a constraint should report a problem, not throw on it
//   plausible a violation by PLAUSIBLE data means the generator cannot build valid data for that rule (an undeclared relation, a derived value), not that the rule is wrong

public static class Audit
{
	private const int Samples = 400;

	private static int Depth = 3;

	public static void Run()
	{
		Console.WriteLine();
		Console.WriteLine($"=== every domain constraint over {Samples} generated values per mode ===");
		Of("VersioningSystemConstraints", VersioningSystemConstraints.All, mode => GenAuto.Of<VersioningSystemCatalog>(mode, o => o.UseFileSystem()));
		Of("DocumentationSetConstraints", DocumentationSetConstraints.All, mode => GenAuto.Of<DocumentationSetInCatalog>(mode, o => o.UseFileSystem()));
		Of("CodexConfigurationConstraints", CodexConfigurationConstraints.All, mode => GenAuto.Of<CodexConfiguration>(mode, o => o.UseFileSystem()));
		Of("RelatedLearningConstraints", RelatedLearningConstraints.All, mode => GenAuto.Of<RelatedLearningCatalog>(mode, o => o.UseFileSystem()));
		Of("DocsetDiscoveryConstraints", DocsetDiscoveryConstraints.All, mode => GenAuto.Of<DocsetDiscovery>(mode, o => o.UseFileSystem()));
		Of("SiteNavigationConstraints", SiteNavigationConstraints.All, mode => GenAuto.Of<SiteNavigationDefinition>(mode, o => o.UseFileSystem()));
		Of(
			"ChangelogConfigurationConstraints",
			ChangelogConfigurationConstraints.All,
			mode => GenAuto.Of<ChangelogConfigurationInCatalog>(mode, o => o.UseFileSystem())
		);
		ChangelogReach();
		Depth = 8;
		ChangelogReach();
		Of("ChangelogConfigurationConstraints, MaxDepth 8", ChangelogConfigurationConstraints.All, mode => GenAuto.Of<ChangelogConfigurationInCatalog>(mode, o =>
		{
			o.UseFileSystem();
			o.MaxDepth = 8;
		}));
		Of("AssemblyConstraints", AssemblyConstraints.All, mode => GenAuto.Of<AssemblyConfiguration>(mode, o => o.UseFileSystem()));
	}

	// Why do 21 of the 23 changelog rules never fail? Look at what the generated configurations contain.
	private static void ChangelogReach()
	{
		Console.WriteLine("--- what generated changelog configurations contain (400 per mode)");
		foreach (var mode in new[] { AutoMode.Plausible, AutoMode.Mixed, AutoMode.Adversarial })
		{
			var gen = GenAuto.Of<ChangelogConfigurationInCatalog>(mode, o => { o.UseFileSystem(); o.MaxDepth = Depth; });
			var values = Enumerable.Range(0, Samples).Select(i => gen.Generate(new PCG(11u, (ulong)i + 1), null, out _)).ToList();
			var pivot = values.Count(v => v.Configuration.Pivot is not null);
			var types = values.Count(v => v.Configuration.Pivot?.Types is { Count: > 0 });
			var products = values.Count(v => v.Products.Products.Count > 0);
			var bundle = values.Count(v => v.Configuration.Bundle is not null);
			Console.WriteLine(
				$"    {mode,-11} pivot {pivot,3}/{Samples}   pivot.types non-empty {types,3}   catalog has products {products,3}   bundle {bundle,3}"
			);
		}
	}

	private static void Of<T>(string name, IReadOnlyList<Constraint<T>> constraints, Func<AutoMode, Gen<T>> gen)
	{
		var modes = new[] { AutoMode.Plausible, AutoMode.Mixed, AutoMode.Adversarial };
		var values = modes.ToDictionary(
			m => m,
			m => Enumerable.Range(0, Samples).Select(i => gen(m).Generate(new PCG(11u, (ulong)i + 1), null, out _)).ToList()
		);
		Console.WriteLine($"--- {name} ({constraints.Count} constraints)");
		Console.WriteLine($"    {"constraint",-44} {"plausible",9} {"mixed",7} {"adversarial",12}   threw");
		foreach (var constraint in constraints)
		{
			var violated = new Dictionary<AutoMode, int>();
			var threw = 0;
			foreach (var mode in modes)
			{
				var count = 0;
				foreach (var value in values[mode])
				{
					try
					{
						if (constraint.Check(value).Any())
						{
							count++;
						}
					}
					catch (Exception)
					{
						threw++;
					}
				}
				violated[mode] = count;
			}
			var note = (violated[AutoMode.Mixed] + violated[AutoMode.Adversarial]) == 0
				? "  <- never violated"
				: violated[AutoMode.Plausible] > 0 ? "  <- plausible data breaks it" : "";
			Console.WriteLine(
				$"    {constraint.Id,-44} {violated[AutoMode.Plausible],9} {violated[AutoMode.Mixed],7} {violated[AutoMode.Adversarial],12}   {(threw > 0 ? threw.ToString() : "")}{note}"
			);
		}
	}
}
