using System.Collections.Frozen;
using System.Text.RegularExpressions;
using CsCheck;
using Elastic.Documentation.Catalog;
using Elastic.Documentation.Constraints;
using Elastic.Documentation.DocumentationSets;
using Elastic.Documentation.Publishing;
using Nullean.Entities;
using Nullean.Entities.CsCheck;
using Nullean.Entities.CsCheck.FileSystem;

namespace SpikeApp;

// Our shapes beside docs-builder's own constraints, on THEIR real types.

public sealed record NavigationInAssembly(SiteNavigationDefinition Navigation, AssemblyConfiguration Assembly);

public static partial class Port
{
	private static string Scheme(string name)
	{
		var clean = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]", "");
		return clean.Length == 0 || !char.IsLetter(clean[0]) ? "x" + clean : clean;
	}

	// ── 1. ours, written against their real types ────────────────────────────────────────

	public static readonly ConstraintSet<AssemblyConfiguration> AssemblyRules = ConstraintSet.From(
		("ReferenceKeysMatchNames", Constraints<AssemblyConfiguration>.KeysMatch(a => a.ReferenceRepositories, r => r.Name)),
		("AvailableKeysMatchNames", Constraints<AssemblyConfiguration>.KeysMatch(a => a.AvailableRepositories, r => r.Name)),
		("PrivateKeysMatchNames", Constraints<AssemblyConfiguration>.KeysMatch(a => a.PrivateRepositories, r => r.Name)),
		("PrivateIsAvailable",
		Constraints<AssemblyConfiguration>.References<KeyValuePair<string, Repository>, string>(
			a => a.PrivateRepositories,
			kv => [kv.Key],
			a => a.AvailableRepositories.Keys
		)),
		("AvailableRepositoriesPublish",
		Constraints<AssemblyConfiguration>.Always(
			a => a.AvailableRepositories.Values,
			repos => repos.All(r => !r.Skip),
			"An available repository does not skip publishing."
		))
	);

	public static readonly ConstraintSet<NavigationInAssembly> MountRules = ConstraintSet.From(
		("MountSchemeIsARepository",
		Constraints<NavigationInAssembly>.References<SiteTableOfContentsRef, string>(
			p => SiteNavigationConstraints.AllMounts(p.Navigation),
			m => [m.Source.Scheme],
			p => p.Assembly.AvailableRepositories.Keys.Select(Scheme)
		)),
		("PathPrefixesAreUnique",
		Constraints<NavigationInAssembly>.Unique(
			p => SiteNavigationConstraints.AllMounts(p.Navigation).Where(m => !string.IsNullOrEmpty(m.PathPrefix)),
			m => m.PathPrefix.Trim('/')
		))
	);

	public static readonly ConstraintSet<DocumentationSetInCatalog> DocsetRules = ConstraintSet.From(
		("ProductsExist",
		Constraints<DocumentationSetInCatalog>.References<string, string>(p => p.Docset.Products, id => [id], p => p.Products.Products.Keys))
	);

	// The derivation the loader performs, written once and used to make generated data valid: available = the references that publish, private = the available private ones.
	private static AssemblyConfiguration Derive(AssemblyConfiguration a)
	{
		var references = a.ReferenceRepositories.Values.Select(r => r with { Skip = false }).GroupBy(r => r.Name).Select(g => g.First()).ToList();
		var available = references.ToFrozenDictionary(r => r.Name);
		return a with
		{
			ReferenceRepositories = available,
			AvailableRepositories = available,
			PrivateRepositories = references.Where(r => r.Private).ToFrozenDictionary(r => r.Name),
			AllPrivateRepositoryNames = references.Where(r => r.Private).Select(r => r.Name).ToHashSet(),
		};
	}

	// Mounts refer to repositories the assembly has: rewrite every mount's scheme to one of them.
	private static NavigationInAssembly RepairMounts(NavigationInAssembly pair)
	{
		var names = pair.Assembly.AvailableRepositories.Keys.Select(Scheme).Order().ToList();
		if (names.Count == 0)
		{
			return pair;
		}
		var n = 0;

		SiteTableOfContentsRef Fix(SiteTableOfContentsRef t) =>
			t with { Source = new Uri($"{names[n++ % names.Count]}://x"), Children = [.. t.Children.Select(Fix)] };

		var entries = pair.Navigation.TableOfContents
			.Select(e =>
					e.Value switch
					{
						SiteTableOfContentsRef toc => new SiteNavigationEntry(Fix(toc)),
						SiteSectionRef section => new SiteNavigationEntry(section with { Children = [.. section.Children.Select(Fix)] }),
						_ => e,
					})
			.ToList();
		return pair with { Navigation = pair.Navigation with { TableOfContents = entries } };
	}

	private static int SafeCount<T>(IEnumerable<T> items, Func<T, bool> predicate, out int threw)
	{
		var count = 0;
		var thrown = 0;
		foreach (var item in items)
		{
			try
			{
				if (predicate(item))
				{
					count++;
				}
			}
			catch (Exception)
			{
				thrown++;
			}
		}
		threw = thrown;
		return count;
	}

	private static readonly string[] TheirPrefixRules = ["path-prefixes-unique", "repository-path-prefixes-unique"];

	public static void Run()
	{
		// the relations and the derivations their aggregates need declared
		static void Relations(AutoOptions o) =>
			o
				.UseFileSystem()
				.KeyedBy<AssemblyConfiguration, string, Repository>(a => a.ReferenceRepositories, r => r.Name)
				.KeyedBy<ProductCatalog, string, Product>(c => c.Products, p => p.Id)
				.Reference<SiteTableOfContentsRef, Repository, string, Uri>(t => t.Source, r => r.Name, name => new Uri($"{Scheme(name)}://x"))
				.References<DocumentationSetDefinition, Product, string>(d => d.Products, p => p.Id)
				.Identity<SiteTableOfContentsRef, string>(t => t.PathPrefix)
				.Normalize<AssemblyConfiguration>(Derive)
				.Normalize<NavigationInAssembly>(RepairMounts);

		foreach (var mode in new[] { AutoMode.Plausible, AutoMode.Mixed, AutoMode.Adversarial })
		{
			Console.WriteLine($"=== {mode} ===");

			var assemblies = Draw<AssemblyConfiguration>(mode, Relations, 300);
			var asm = AssemblyRules.AsConstraints().ToDictionary(c => c.Name!, c => assemblies.Count(a => Violated(c, a)));
			Console.WriteLine($"AssemblyConfiguration   violated of {assemblies.Count}: {string.Join(", ", asm.Select(kv => $"{kv.Key}={kv.Value}"))}");

			var pairs = Draw<NavigationInAssembly>(mode, Relations, 300);
			var ours = SafeCount(pairs, p => MountRules.Validate(p).Any(v => v.ConstraintId == "PathPrefixesAreUnique"), out _);
			var theirs = SafeCount(
				pairs,
				p => SiteNavigationConstraints.All.CheckAll(p.Navigation).Any(v => TheirPrefixRules.Contains(v.ConstraintId)),
				out var theirsThrew
			);
			var both = SafeCount(
				pairs,
				p =>
					MountRules.Validate(p).Any(v => v.ConstraintId == "PathPrefixesAreUnique") && SiteNavigationConstraints
						.All
						.CheckAll(p.Navigation)
						.Any(v => TheirPrefixRules.Contains(v.ConstraintId)),
				out _
			);
			var oursOnly = SafeCount(
				pairs,
				p =>
					MountRules.Validate(p).Any(v => v.ConstraintId == "PathPrefixesAreUnique") && !SiteNavigationConstraints
						.All
						.CheckAll(p.Navigation)
						.Any(v => TheirPrefixRules.Contains(v.ConstraintId)),
				out _
			);
			var theirsOnly = SafeCount(
				pairs,
				p =>
					!MountRules.Validate(p).Any(v => v.ConstraintId == "PathPrefixesAreUnique") && SiteNavigationConstraints
						.All
						.CheckAll(p.Navigation)
						.Any(v => TheirPrefixRules.Contains(v.ConstraintId)),
				out _
			);
			var mountRepo = SafeCount(pairs, p => MountRules.Validate(p).Any(v => v.ConstraintId == "MountSchemeIsARepository"), out _);
			var anyMounts = pairs.Count(p => SiteNavigationConstraints.AllMounts(p.Navigation).Any());
			Console.WriteLine(
				$"Navigation prefixes     of {pairs.Count} ({anyMounts} with mounts): ours flags {ours}, theirs flags {theirs}, both {both}, ours-only {oursOnly}, theirs-only {theirsOnly}; THEIR RULE THREW {theirsThrew} times"
			);
			if (mode == AutoMode.Plausible && pairs.FirstOrDefault(p => MountRules.Validate(p).Any(v => v.ConstraintId == "PathPrefixesAreUnique")) is { } dup)
			{
				Console.WriteLine(
					"   DUP EXAMPLE prefixes: " + string.Join(
						" | ",
						SiteNavigationConstraints.AllMounts(dup.Navigation).Select(m => $"{m.Source}={m.PathPrefix}")
					)
				);
			}
			Console.WriteLine($"Mount of unknown repo   ours flags {mountRepo}/{pairs.Count}; theirs checks nothing about it");

			var docs = Draw<DocumentationSetInCatalog>(mode, Relations, 300);
			var unknownProducts = SafeCount(docs, d => DocsetRules.Validate(d).Count > 0, out _);
			var theirProductRule = SafeCount(
				docs,
				d =>
					DocumentationSetConstraints
						.All
						.CheckAll(d)
						.Any(v => v.Message.Contains("product", StringComparison.OrdinalIgnoreCase) && v.ConstraintId != "release-notes-product-known"),
				out var docThrew
			);
			Console.WriteLine(
				$"Docset products         of {docs.Count}: ours flags an unknown product id in {unknownProducts}; their rules mention a docset product in {theirProductRule}; their rules threw {docThrew} times"
			);
		}
	}

	private static bool Violated<T>(Nullean.Entities.Constraint<T> c, T value) => c.Check(value, value).Any();

	private static List<T> Draw<T>(AutoMode mode, Action<AutoOptions> configure, int count)
	{
		var list = new List<T>();
		for (var i = 0; i < count; i++)
		{
			try
			{
				list.Add(GenAuto.Of<T>(mode, configure).Generate(new PCG(3u, (ulong)i + 1), null, out _));
			}
			catch (Exception e)
			{
				Console.WriteLine($"   (draw {i} of {typeof(T).Name} failed: {e.GetType().Name}: {e.Message.Split('\n')[0]})");
				break;
			}
		}
		return list;
	}
}
