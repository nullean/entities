using AwesomeAssertions;

namespace Nullean.Entities.Tests;

// The reference styles docs-builder's domain actually uses: ids into another aggregate, a dictionary keyed by the entity's own name, a URI whose scheme
// is a repository, and paths inside a tree.
internal sealed record Repo(string Name);

internal sealed record Assembly(IReadOnlyDictionary<string, Repo> Repositories);

internal sealed record SiteMount(Uri Source, string PathPrefix);

internal sealed record Navigation(IReadOnlyList<SiteMount> Mounts);

internal sealed record TocNode(string Path, IReadOnlyList<TocNode> Children);

internal sealed record DocSet(string Name, IReadOnlyList<string> Products, string? CodexGroup, IReadOnlyList<TocNode> Toc);

internal sealed record ProductCatalog(IReadOnlyList<string> Ids);

internal sealed record CodexConfig(IReadOnlyList<string> GroupIds);

public class DocsBuilderShapesTests
{
	private static readonly ConstraintSet<Assembly> AssemblyRules = ConstraintSet.From(
		("RepositoryKeysMatchNames", Constraints<Assembly>.KeysMatch(a => a.Repositories, r => r.Name, StringComparer.Ordinal))
	);

	private static readonly ConstraintSet<(Navigation, Assembly)> MountRules = ConstraintSet.From(
		("MountSchemeIsARepository",
		Constraints<(Navigation Navigation, Assembly Assembly)>.References<SiteMount, string>(
			p => p.Navigation.Mounts,
			m => [m.Source.Scheme],
			p => p.Assembly.Repositories.Keys
		))
	);

	[Test]
	public void A_dictionary_key_that_differs_from_the_entitys_own_name_is_reported_with_the_pair()
	{
		var assembly = new Assembly(new Dictionary<string, Repo> { ["kibana"] = new("kibana"), ["apm"] = new("apm-server") });

		var violation = AssemblyRules.Validate(assembly).Single();

		violation.Evidence.Should().Be(("apm", "apm-server"));
		violation.Member.Should().Be("Repositories");
	}

	[Test]
	public void A_mount_whose_uri_scheme_is_not_a_known_repository_is_reported_through_a_pair()
	{
		var assembly = new Assembly(new Dictionary<string, Repo> { ["kibana"] = new("kibana") });
		var navigation = new Navigation([new(new Uri("kibana://get-started"), "/a"), new(new Uri("ghost://x"), "/b")]);

		MountRules.Validate((navigation, assembly)).Single().Evidence.Should().Be("ghost");
	}

	[Test]
	public void Rules_for_two_aggregates_are_one_pair_set_that_keeps_each_rules_name()
	{
		var catalog = ConstraintSet.From(
			("ProductsAreNamed", Constraints<ProductCatalog>.Always(c => c.Ids, ids => ids.All(i => i.Length > 0)))
		);
		var pair = ConstraintSet.Pair(catalog, ConstraintSet.From(("RepoRule", Constraints<Repo>.Always(r => r.Name.Length > 0))));

		pair.AsConstraints().Select(c => c.Name).Should().BeEquivalentTo(["ProductsAreNamed", "RepoRule"]);
		pair.Validate((new ProductCatalog(["a", ""]), new Repo("x"))).Single().ConstraintId.Should().Be("ProductsAreNamed");
	}

	[Test]
	public void Merge_puts_several_sets_over_one_type_into_one()
	{
		var first = ConstraintSet.From(("A", Constraints<Repo>.Always(r => r.Name.Length > 0)));
		var second = ConstraintSet.From(("B", Constraints<Repo>.Always(r => r.Name.Length < 5)));

		ConstraintSet.Merge(first, second).Validate(new Repo("")).Single().ConstraintId.Should().Be("A");
		ConstraintSet.Merge(first, second).AsConstraints().Should().HaveCount(2);
	}

	private static readonly ConstraintSet<DocSet> TreeRules = ConstraintSet.From(
		("NoFileIsListedTwice", Constraints<DocSet>.Unique(d => Tree.Flatten(d.Toc, n => n.Children), n => n.Path))
	);

	[Test]
	public void A_path_repeated_at_different_depths_of_a_tree_is_found_by_flattening_it()
	{
		var toc = new TocNode[] { new("a.md", [new("b.md", [new("c.md", [])]), new("d.md", [new("c.md", [])])]) };

		TreeRules.Validate(new DocSet("d", [], null, toc)).Single().Evidence.Should().Be("c.md");
	}

	[Test]
	public void Flatten_is_document_order_and_survives_a_very_deep_tree()
	{
		var deep = new TocNode("leaf", []);
		for (var i = 0; i < 100_000; i++)
		{
			deep = new TocNode($"n{i}", [deep]);
		}

		Tree.Flatten([deep], n => n.Children).Count().Should().Be(100_001);
		Tree.Flatten<string>(["a"], x => x == "a" ? ["b", "c"] : []).Should().Equal("a", "b", "c");
		Tree.FlattenWithAncestors<string>(["a"], x => x == "a" ? ["b"] : []).Last().Ancestors.Should().Equal("a");
	}

	[Test]
	public void Acyclic_handles_an_include_chain_a_million_deep_without_overflowing_the_stack_and_still_finds_the_loop()
	{
		var rule = Constraints<Graph>.Acyclic(g => g.Nodes, n => n.Id, n => n.Includes);
		var chain = new List<Node> { new("0") };
		for (var i = 1; i <= 1_000_000; i++)
		{
			chain.Add(
				new Node(i.ToString(System.Globalization.CultureInfo.InvariantCulture), (i - 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
			);
		}

		rule.Check(new Graph(chain), new Graph(chain)).Should().BeEmpty();

		chain[0] = new Node("0", "1000000");
		rule.Check(new Graph(chain), new Graph(chain)).Should().ContainSingle();
	}

	[Test]
	public void Tree_Map_rebuilds_a_tree_bottom_up()
	{
		var toc = new[] { new TocNode("a.md", [new TocNode("b.md", []), new TocNode("c.md", [new TocNode("d.md", [])])]) };

		var upper = Tree.Map(toc, n => n.Children, (n, children) => n with { Path = n.Path.ToUpperInvariant(), Children = children });

		Tree.Flatten(upper, n => n.Children).Select(n => n.Path).Should().Equal("A.MD", "B.MD", "C.MD", "D.MD");
		toc[0].Path.Should().Be("a.md", "the original is not changed");
	}
}
