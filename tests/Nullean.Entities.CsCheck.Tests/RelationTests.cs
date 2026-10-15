using AwesomeAssertions;
using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

public class RelationTests
{
	private static readonly Relation<Rel.CatalogFirst, Rel.Docset, Rel.Product, string> ProductsOfDocsets = Relation.ReferenceList(
		(Rel.CatalogFirst pair) => new[] { pair.Docset },
		d => d.Products,
		pair => pair.Catalog.Products,
		p => p.Id
	);

	private static ConstraintSet<Rel.CatalogFirst> Rules { get; } = ConstraintSet.From(
		("docset-products-exist", ProductsOfDocsets.Rule("A docset names a product the catalog has."))
	);

	private static Rel.CatalogFirst Pair(string[] listed, params string[] inCatalog) =>
		new(new Rel.Catalog([.. inCatalog.Select(id => new Rel.Product(id, id))]), new Rel.Docset("docs", listed));

	/// <summary>
	/// Law: a relation's rule reports every key a holder names that no target has, and nothing for a holder that names none.
	/// Consequence if broken: a rule declared from a relation checks less than the same rule written by hand.
	/// </summary>
	[Test]
	public void The_rule_reports_each_dangling_key_and_only_those()
	{
		Rules.Validate(Pair(["a", "b"], "a", "b")).Should().BeEmpty();
		Rules.Validate(Pair([], "a")).Should().BeEmpty();

		Rules.Validate(Pair(["a", "x", "y"], "a")).Select(v => v.Evidence).Should().Equal("x", "y");
	}

	[Test]
	public void A_single_key_member_that_is_null_or_empty_refers_to_nothing()
	{
		var relation = Relation.Reference((Rel.CatalogFirst pair) => new[] { pair.Docset }, d => d.Name, pair => pair.Catalog.Products, p => p.Id);
		var rule = ConstraintSet.From(("name-is-a-product", relation.Rule()));

		rule.Validate(Pair([], "docs")).Should().BeEmpty();
		rule.Validate(new Rel.CatalogFirst(new Rel.Catalog([]), new Rel.Docset("", []))).Should().BeEmpty();
		rule.Validate(Pair([], "other")).Should().ContainSingle().Which.Evidence.Should().Be("docs");
	}

	/// <summary>
	/// Law: the rule carries the members it relates, read from the text of the lambdas the caller wrote; nothing is compiled.
	/// Consequence if broken: a generator cannot be told which members to keep consistent, and the rule has to be declared twice.
	/// </summary>
	[Test]
	public void The_relation_says_which_members_it_relates()
	{
		var info = ProductsOfDocsets.Info;

		info.Kind.Should().Be(RelationKind.ReferenceList);
		(info.FromType, info.FromMember, info.ToType, info.KeyMember).Should().Be((typeof(Rel.Docset), "Products", typeof(Rel.Product), "Id"));
		info.Problem.Should().BeNull();
		Rules.AsConstraints().Single().Shape.Relation.Should().Be(info);
	}

	[Test]
	[Arguments("d => d.Products", "Products")]
	[Arguments("(d) => d.Products", "Products")]
	[Arguments("(Rel.Docset d) => d.Products", "Products")]
	[Arguments("d => d.CodexGroup!", "CodexGroup")]
	public void Member_names_are_read_from_plain_member_access(string lambda, string member)
	{
		var relation = Relation.Reference((Rel.CatalogFirst pair) => new[] { pair.Docset }, d => d.Name, pair => pair.Catalog.Products, p => p.Id);
		var reader = typeof(Relation).GetMethod("MemberOf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
		var args = new object?[] { lambda, null };

		reader.Invoke(null, args).Should().Be(member);
		relation.Info.Problem.Should().BeNull();
	}

	[Test]
	public void A_projection_that_is_not_a_member_access_is_reported_not_guessed()
	{
		var relation = Relation.ReferenceList(
			(Rel.CatalogFirst pair) => new[] { pair.Docset },
			d => d.Products.Where(id => id.Length > 0),
			pair => pair.Catalog.Products,
			p => p.Id
		);

		relation.Info.Problem.Should().Contain("not a plain member access");
		var act = () => new AutoOptions().From(ConstraintSet.From(("r", relation.Rule())));
		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("cannot read");
	}

	/// <summary>
	/// Law: data generated from the rules' relations holds them in plausible mode and breaks them in the others.
	/// Consequence if broken: the generator needs the relation written again, and the two can disagree about what is related.
	/// </summary>
	[Test]
	public void Plausible_data_holds_the_relation_and_hostile_data_sometimes_breaks_it()
	{
		static List<Rel.CatalogFirst> Draw(AutoMode mode, ConstraintSet<Rel.CatalogFirst> rules) =>
			[.. Enumerable.Range(0, 400).Select(i => GenAuto.Of<Rel.CatalogFirst>(mode, o => o.From(rules)).Generate(new PCG(11u, (ulong)i + 1), null, out _))];

		Draw(AutoMode.Plausible, Rules).Where(p => Rules.Validate(p).Any()).Should().BeEmpty();
		Draw(AutoMode.Mixed, Rules).Count(p => Rules.Validate(p).Any()).Should().BeGreaterThan(0);
	}

	// Without a non-empty list the filter that keeps only valid values passes by luck (an empty list refers to nothing), so both tests ask for one.
	private static readonly Constraint<Rel.CatalogFirst> NamesAProduct = Constraints<Rel.CatalogFirst>.Always(
		p => p.Docset.Products.Count > 0,
		"A docset names a product."
	);

	[Test]
	public async Task An_auto_test_draws_data_that_holds_its_rules_relations_without_being_told()
	{
		var rules = ConstraintSet.From(("names-a-product", NamesAProduct), ("docset-products-exist", ProductsOfDocsets.Rule()));

		var result = await AutoTest.For(rules).InvariantAsync();

		result.Should().BeOfType<AxisResult.Passed>();
	}

	[Test]
	public async Task The_same_rule_written_by_hand_gives_the_generator_nothing_to_go_on()
	{
		var byHand = ConstraintSet.From(
			("names-a-product", NamesAProduct),
			("docset-products-exist",
			Constraints<Rel.CatalogFirst>.References(
				pair => new[] { pair.Docset },
				d => d.Products,
				pair => pair.Catalog.Products.Select(p => p.Id)
			))
		);

		var act = async () => await AutoTest.For(byHand).InvariantAsync();

		await act.Should().ThrowAsync<Exception>();
	}

	[Test]
	public void The_relation_survives_being_mapped_onto_a_larger_value()
	{
		var onSite = Rules.Map<(Rel.CatalogFirst Pair, int Other)>(x => x.Pair);

		onSite.AsConstraints().Single().Shape.Relation.Should().NotBeNull();
	}

	/// <summary>
	/// Law: a unique key declared as a relation is unique in everything the generator draws, at every level.
	/// Consequence if broken: a rule against duplicate paths is only ever checked on data that cannot have a duplicate, or on data that always does.
	/// </summary>
	[Test]
	public void A_unique_relation_keeps_keys_unique_across_a_tree()
	{
		static IEnumerable<Rel.Node> All(IEnumerable<Rel.Node> nodes) => nodes.SelectMany(n => new[] { n }.Concat(All(n.Children)));

		var unique = Relation.Unique((Rel.Toc toc) => All(toc.Nodes), n => n.Path);
		var rules = ConstraintSet.From(("paths-unique", unique));

		var tocs = Enumerable
			.Range(0, 300)
			.Select(i => GenAuto.Of<Rel.Toc>(AutoMode.Plausible, o => o.From(rules)).Generate(new PCG(5u, (ulong)i + 1), null, out _))
			.ToList();

		tocs.Where(t => rules.Validate(t).Any()).Should().BeEmpty();
		tocs.Sum(t => All(t.Nodes).Count()).Should().BeGreaterThan(300, "the trees have to have something in them to be unique about");
	}
}
