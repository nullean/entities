using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

internal sealed record SearchOwner(string Label);

internal sealed record SearchShop(string Name, IReadOnlyList<string> Tags, string Key, string Path, SearchOwner Owner);

public class CounterExampleSearchTests
{
	private static readonly Gen<SearchShop> SearchShops = Gen.Select(
		Gen.OneOfConst("kibana", "beats", "apm"),
		Gen.OneOfConst("a", "b", "c", "d").Array[2, 4].Select(tags => (IReadOnlyList<string>)tags.Distinct().ToArray()),
		Gen.OneOfConst("k1", "k2", "k3"),
		Gen.OneOfConst("o1", "o2"),
		(name, tags, key, owner) => new SearchShop(name, tags, key, $"/{key}", new SearchOwner(owner))
	);

	private static ConstraintSet<SearchShop> Rules() =>
		ConstraintSet.From(
			("name-required", Constraints<SearchShop>.Always(s => !string.IsNullOrWhiteSpace(s.Name), "A shop has a name.")),
			("tags-unique", Constraints<SearchShop>.Unique(s => s.Tags, t => t)),
			("path-matches-key", Constraints<SearchShop>.Always(s => s.Path == $"/{s.Key}", "The path is the key.")),
			("owner-label-required", Constraints<SearchShop>.Always(s => s.Owner.Label.Length > 0, "An owner has a label."))
		);

	/// <summary>
	/// Law: a rule with no counter-example given gets one from the search (an edge value, an item copied, a member taken from another value, a nested member), and it breaks only that rule.
	/// Consequence if broken: every rule needs a hand-written counter-example before its test can pass.
	/// </summary>
	[Test]
	public void Rules_with_no_counter_example_are_found_by_search()
	{
		var report = Rules().Laws(SearchShops);

		report.Results.Select(r => r.Constraint).Should().BeEquivalentTo("name-required", "tags-unique", "path-matches-key", "owner-label-required");
	}

	[Test]
	public void A_found_counter_example_is_said_as_pasteable_csharp()
	{
		var report = Rules().Laws(SearchShops);

		var text = string.Join("\n", report.Results.Select(r => r.CounterExample));
		text.Should().Contain("e => e with { Tags = [.. e.Tags, e.Tags.First()] }");
		text.Should().Contain("e => e with { Owner = e.Owner with { Label =");
	}

	internal sealed record SearchDoc(bool IsDraft, string Slug, string Title, IReadOnlyDictionary<string, SearchOwner> Owners);

	private static readonly Gen<SearchDoc> Docs = Gen.Select(
		Gen.OneOfConst("a", "b", "c"),
		Gen.OneOfConst("x", "y"),
		(slug, owner) => new SearchDoc(false, slug, "title", new Dictionary<string, SearchOwner> { [owner] = new SearchOwner(owner) })
	);

	private static ConstraintSet<SearchDoc> DocRules() =>
		ConstraintSet.From(
			("draft-has-slug", Constraints<SearchDoc>.When(d => d.IsDraft).Requires(d => d.Slug.Length > 0, "A draft has a slug.")),
			("title-required", Constraints<SearchDoc>.Always(d => d.Title is not null, "A doc has a title.")),
			("owner-label-required", Constraints<SearchDoc>.Always(d => d.Owners.Values.All(o => o.Label.Length > 0), "An owner has a label."))
		);

	/// <summary>
	/// Law: a rule that only applies when its guard is true gets a two-step counter-example (open the guard, then break the requirement); a "required" rule is broken by null even on a non-nullable string; a member inside a dictionary value is reached.
	/// Consequence if broken: the author hand-writes the counter-examples that are the most mechanical to find.
	/// </summary>
	[Test]
	public void Guards_nulls_and_dictionary_values_are_reached()
	{
		var report = AutoTest.For(DocRules(), Docs).CounterExampleReport();

		report.Missing.Should().BeEmpty();
		var text = report.ToString();
		text.Should().Contain("e => { e = e with { IsDraft = true }; return e with { Slug = \"\" }; }");
		text.Should().Contain("Title = null!");
	}

	[Test]
	public void A_counter_example_given_by_the_author_is_reported_as_given()
	{
		var report = AutoTest
			.For(DocRules(), Docs)
			.CounterExamples(new Dictionary<string, Func<SearchDoc, SearchDoc>> { ["title-required"] = d => d with { Title = null! } })
			.CounterExampleReport();

		report.Lines.Single(l => l.Rule == "title-required").Kind.Should().Be(CounterExampleKind.Given);
	}

	[Test]
	public void A_counter_example_named_by_its_rule_object_is_the_one_given()
	{
		var rules = Rules();
		var nameRequired = rules.AsConstraints().Single(c => c.Name == "name-required");

		var report = AutoTest.For(rules, SearchShops).CounterExample(nameRequired, s => s with { Name = " " }).CounterExampleReport();

		report.Lines.Single(l => l.Rule == "name-required").Kind.Should().Be(CounterExampleKind.Given);
	}

	[Test]
	public void A_counter_example_for_a_rule_outside_the_set_is_refused_at_once()
	{
		var stranger = Constraints<SearchShop>.Always(s => s.Name.Length > 0, "A shop has a name.");

		var act = () => AutoTest.For(Rules(), SearchShops).CounterExample(stranger, s => s);

		act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("not in this set");
	}
}
