using AwesomeAssertions;
using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// The reference styles docs-builder's domain uses, none of which the name heuristics can see.
internal static class Rel
{
	public sealed record Product(string Id, string Name);

	public sealed record Catalog(IReadOnlyList<Product> Products);

	public sealed record Docset(string Name, IReadOnlyList<string> Products);

	public sealed record CatalogFirst(Catalog Catalog, Docset Docset);

	public sealed record DocsetFirst(Docset Docset, Catalog Catalog);

	public sealed record Repo(string Name);

	public sealed record Mount(Uri Source, string PathPrefix);

	public sealed record Nav(IReadOnlyList<Mount> Mounts);

	public sealed record Assembly(IReadOnlyDictionary<string, Repo> Repositories);

	public sealed record Site(Nav Nav, Assembly Assembly);

	public sealed record Node(string Path, IReadOnlyList<Node> Children);

	public sealed record Toc(IReadOnlyList<Node> Nodes);

	public sealed record TocFile(string Path, IReadOnlyList<string> Includes);

	public sealed record IncludeGraph(IReadOnlyList<TocFile> Files);

	public static string RepoOf(Mount m) => Uri.UnescapeDataString(m.Source.AbsolutePath.TrimStart('/'));
}

public class AutoRelationsTests
{
	private static List<T> Draw<T>(AutoMode mode, Action<AutoOptions> configure, int count = 400) =>
		[.. Enumerable.Range(0, count).Select(i => GenAuto.Of<T>(mode, configure).Generate(new PCG(11u, (ulong)i + 1), null, out _))];

	private static void ProductsDeclared(AutoOptions o) => o.References<Rel.Docset, Rel.Product, string>(d => d.Products, p => p.Id);

	private static bool Dangling(Rel.Catalog c, Rel.Docset d) => d.Products.Any(id => c.Products.All(p => p.Id != id));

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public void A_list_of_ids_refers_to_a_collection_on_another_object_whichever_is_generated_first(bool catalogFirst)
	{
		var pairs = catalogFirst
			? Draw<Rel.CatalogFirst>(AutoMode.Plausible, ProductsDeclared, 600).Select(x => (x.Catalog, x.Docset)).ToList()
			: Draw<Rel.DocsetFirst>(AutoMode.Plausible, ProductsDeclared, 600).Select(x => (x.Catalog, x.Docset)).ToList();

		pairs.Any(p => p.Docset.Products.Count > 0).Should().BeTrue("some docsets list products");
		pairs.All(p => !Dangling(p.Catalog, p.Docset)).Should().BeTrue("every listed product exists in the catalog it was generated beside");
	}

	[Test]
	public void The_other_modes_sometimes_list_a_product_nothing_has()
	{
		var mixed = Draw<Rel.CatalogFirst>(AutoMode.Mixed, ProductsDeclared, 800);
		var adversarial = Draw<Rel.DocsetFirst>(AutoMode.Adversarial, ProductsDeclared, 800);

		mixed.Any(x => Dangling(x.Catalog, x.Docset)).Should().BeTrue();
		adversarial.Any(x => Dangling(x.Catalog, x.Docset)).Should().BeTrue();
	}

	private static void MountsDeclared(AutoOptions o) =>
		o.Reference<Rel.Mount, Rel.Repo, string, Uri>(m => m.Source, r => r.Name, name => new Uri($"https://repo.example/{Uri.EscapeDataString(name)}"))
			.KeyedBy<Rel.Assembly, string, Rel.Repo>(a => a.Repositories, r => r.Name);

	[Test]
	public void A_uri_built_from_a_repository_name_and_a_dictionary_keyed_by_that_name_agree_in_plausible_data()
	{
		var sites = Draw<Rel.Site>(AutoMode.Plausible, MountsDeclared, 600).Where(s => s.Nav.Mounts.Count > 0).ToList();

		sites.Should().NotBeEmpty();
		sites
			.All(s => s.Nav.Mounts.All(m => s.Assembly.Repositories.Values.Any(r => r.Name == Rel.RepoOf(m))))
			.Should()
			.BeTrue("every mount names a repository that exists");
		sites.All(s => s.Assembly.Repositories.All(e => e.Key == e.Value.Name)).Should().BeTrue("a dictionary key is its entity's name");
	}

	[Test]
	public void The_other_modes_mount_a_repository_nothing_has_and_let_a_dictionary_key_drift()
	{
		var sites = Draw<Rel.Site>(AutoMode.Adversarial, MountsDeclared, 800);

		sites
			.Any(s => s.Nav.Mounts.Any(m => s.Assembly.Repositories.Values.All(r => r.Name != Rel.RepoOf(m))))
			.Should()
			.BeTrue("a mount of a repository that is not there");
		sites.Any(s => s.Assembly.Repositories.Any(e => e.Key != e.Value.Name)).Should().BeTrue("a key that is not the entity's name");
	}

	private static void PathsDeclared(AutoOptions o) => o.Identity<Rel.Node, string>(n => n.Path);

	private static IEnumerable<Rel.Node> Flat(IEnumerable<Rel.Node> nodes) => Tree.Flatten(nodes, n => n.Children);

	[Test]
	public void An_identity_declared_across_a_tree_is_repeated_at_different_levels_in_the_other_modes()
	{
		var tocs = Draw<Rel.Toc>(AutoMode.Adversarial, PathsDeclared, 800);

		tocs.Any(t => Flat(t.Nodes).GroupBy(n => n.Path).Any(g => g.Count() > 1)).Should().BeTrue("a path listed twice somewhere in the tree");
	}

	[Test]
	public void Probe_finds_every_docs_rule_with_the_relations_declared_and_no_hand_made_generator()
	{
		var rules = ConstraintSet.From(
			("ProductsExist", Constraints<Rel.CatalogFirst>.References<Rel.Docset, string>(
				p => [p.Docset],
				d => d.Products,
				p => p.Catalog.Products.Select(x => x.Id)
			)),
			("PathsAreUnique", Constraints<Rel.CatalogFirst>.Unique(p => p.Catalog.Products, x => x.Id))
		);

		var report = rules.Probe(GenAuto.Of<Rel.CatalogFirst>(AutoMode.Mixed, ProductsDeclared));

		report.NeverViolated.Should().BeEmpty();
	}

	private static void IncludesDeclared(AutoOptions o) => o.References<Rel.TocFile, Rel.TocFile, string>(f => f.Includes, f => f.Path);

	private static ConstraintSet<Rel.IncludeGraph> Acyclic() =>
		ConstraintSet.From(("Acyclic", Constraints<Rel.IncludeGraph>.Acyclic(g => g.Files, f => f.Path, f => f.Includes)));

	[Test]
	public void A_relation_from_a_type_to_itself_is_a_dag_in_plausible_data_and_closes_cycles_in_the_other_modes()
	{
		var plausible = Draw<Rel.IncludeGraph>(AutoMode.Plausible, IncludesDeclared, 800);

		plausible.Any(g => g.Files.Any(f => f.Includes.Count > 0)).Should().BeTrue("some files include others");
		plausible.All(g => Acyclic().Validate(g).Count == 0).Should().BeTrue("includes only point at earlier files");
		plausible.All(g => g.Files.All(f => f.Includes.All(i => g.Files.Any(x => x.Path == i)))).Should().BeTrue("and at files that exist");

		var adversarial = Draw<Rel.IncludeGraph>(AutoMode.Adversarial, IncludesDeclared, 1000);

		adversarial.Any(g => Acyclic().Validate(g).Count > 0).Should().BeTrue("a cycle");
		adversarial.Any(g => g.Files.Any(f => f.Includes.Contains(f.Path))).Should().BeTrue("a file that includes itself");
	}
}
