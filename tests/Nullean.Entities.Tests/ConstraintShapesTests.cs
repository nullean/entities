using AwesomeAssertions;

namespace Nullean.Entities.Tests;

internal sealed record WebPage(string Url, string? RedirectTo = null);

internal sealed record Site(IReadOnlyList<WebPage> Pages, IReadOnlyList<string> Mounted);

internal sealed record Node(string Id, params string[] Includes);

internal sealed record Graph(IReadOnlyList<Node> Nodes);

internal sealed class MutableCounter
{
	public int Value { get; set; }
}

internal sealed record Customer(string Email);

internal sealed record Located(string Id, int Line, string? Ref = null);

internal sealed record Doc(IReadOnlyList<Located> Items);

public class ConstraintShapesTests
{
	private static readonly Constraint<Site> UniqueUrls = Constraints<Site>.Unique(s => s.Pages, p => p.Url);

	[Test]
	public void Unique_reports_each_duplicated_key_once_with_the_key_as_evidence()
	{
		var site = new Site([new("/a"), new("/b"), new("/a"), new("/a"), new("/b")], []);

		var violations = ConstraintSet.From(("UniqueUrls", UniqueUrls)).Validate(site);

		violations.Should().HaveCount(2);
		violations.Select(v => v.Evidence).Should().BeEquivalentTo(["/a", "/b"]);
		violations.Should().OnlyContain(v => v.Member == "Pages");
	}

	[Test]
	public void Unique_accepts_a_comparer_so_keys_differing_only_by_case_collide()
	{
		var set = ConstraintSet.From(("Urls", Constraints<Site>.Unique(s => s.Pages, p => p.Url, StringComparer.OrdinalIgnoreCase)));

		set.Validate(new Site([new("/Docs"), new("/docs")], [])).Should().ContainSingle();
		set.Validate(new Site([new("/docs"), new("/guide")], [])).Should().BeEmpty();
	}

	[Test]
	public void References_reports_every_dangling_reference()
	{
		var set = ConstraintSet.From(
			("MountsExist", Constraints<Site>.References<string, string>(s => s.Mounted, m => [m], s => s.Pages.Select(p => p.Url)))
		);

		var violations = set.Validate(new Site([new("/a")], ["/a", "/ghost", "/other"]));

		violations.Select(v => v.Evidence).Should().BeEquivalentTo(["/ghost", "/other"]);
		set.Validate(new Site([new("/a"), new("/b")], ["/a", "/b"])).Should().BeEmpty();
	}

	private static ConstraintSet<Graph> Acyclic() =>
		ConstraintSet.From(("NoIncludeCycles", Constraints<Graph>.Acyclic(g => g.Nodes, n => n.Id, n => n.Includes)));

	[Test]
	public void Acyclic_accepts_a_dag_and_edges_to_unknown_ids()
	{
		var graph = new Graph([new("a", "b", "c"), new("b", "c"), new("c", "missing")]);

		Acyclic().Validate(graph).Should().BeEmpty();
	}

	[Test]
	public void Acyclic_reports_self_two_and_three_node_cycles_with_the_path()
	{
		Acyclic().Validate(new Graph([new("a", "a")])).Single().Evidence.Should().Be("a -> a");
		Acyclic().Validate(new Graph([new("a", "b"), new("b", "a")])).Single().Evidence.Should().Be("a -> b -> a");
		Acyclic().Validate(new Graph([new("a", "b"), new("b", "c"), new("c", "a")])).Single().Evidence.Should().Be("a -> b -> c -> a");
	}

	[Test]
	public void Acyclic_does_not_throw_on_duplicate_ids()
	{
		var act = () => Acyclic().Validate(new Graph([new("a", "b"), new("a", "c"), new("b")]));

		act.Should().NotThrow();
	}

	[Test]
	public void NoChains_reports_a_redirect_that_points_at_another_redirect_but_not_a_self_mapping()
	{
		var set = ConstraintSet.From(("Flat", Constraints<Site>.NoChains(s => s.Pages, p => p.Url, p => p.RedirectTo ?? p.Url)));

		var violations = set.Validate(new Site([new("/a", "/b"), new("/b", "/c"), new("/c"), new("/d", "/c")], []));

		violations.Should().ContainSingle().Which.Evidence.Should().Be(("/a", "/b"));
	}

	[Test]
	public void The_selector_overload_of_Always_names_the_member_and_carries_the_value()
	{
		var set = ConstraintSet.From(("EmailHasAt", Constraints<Customer>.Always(c => c.Email, e => e.Contains('@'))));

		var violation = set.Validate(new Customer("nobody")).Single();

		violation.Member.Should().Be("Email");
		violation.Evidence.Should().Be("nobody");
		set.Validate(new Customer("a@b")).Should().BeEmpty();
	}

	[Test]
	public void A_rule_shared_by_two_sets_keeps_the_name_each_set_gave_it()
	{
		var shared = Constraints<Customer>.Always(c => c.Email.Length > 0);
		var first = ConstraintSet.From(("EmailPresent", shared));
		var second = ConstraintSet.From(("HasEmail", shared));

		first.AsConstraints().Single().Name.Should().Be("EmailPresent");
		second.AsConstraints().Single().Name.Should().Be("HasEmail");
		first.Validate(new Customer("")).Single().ConstraintId.Should().Be("EmailPresent");
	}

	[Test]
	public void ApplyOrThrow_and_Unwrap_throw_an_exception_that_carries_the_violations()
	{
		var set = ConstraintSet.From(("EmailHasAt", Constraints<Customer>.Always(c => c.Email, e => e.Contains('@'))));

		var apply = () => set.ApplyOrThrow(new Customer("a@b"), _ => new Customer("nobody"));
		var unwrap = () => set.Apply(new Customer("a@b"), _ => new Customer("nobody")).Unwrap();

		apply.Should().Throw<ConstraintViolationException>().Which.Violations.Should().ContainSingle().Which.Member.Should().Be("Email");
		unwrap.Should().Throw<InvalidOperationException>().Which.Should().BeOfType<ConstraintViolationException>();
	}

	[Test]
	public void A_class_mutated_in_place_has_its_point_in_time_constraints_run_instead_of_being_a_noop()
	{
		var set = ConstraintSet.From(("NotNegative", Constraints<MutableCounter>.Always(c => c.Value >= 0)));
		var counter = new MutableCounter { Value = 1 };

		var result = set.Apply(counter, c =>
		{
			c.Value = -5;
			return c;
		});

		result.Should().BeOfType<Result<MutableCounter>.Fail>();
	}

	[Test]
	public void A_record_returned_unchanged_is_still_a_noop()
	{
		var set = ConstraintSet.From(("EmailHasAt", Constraints<Customer>.Always(c => c.Email, e => e.Contains('@'))));

		set.ApplyDetailed(new Customer("nobody"), c => c).Should().BeOfType<ApplyOutcome<Customer>.Noop>();
	}

	[Test]
	public void Check_takes_the_before_you_kept_so_sequential_rules_work_for_in_place_mutation()
	{
		var set = ConstraintSet.From(("NeverDecreases", Constraints<MutableCounter>.Monotonic(c => c.Value, MonotonicDirection.NonDecreasing)));
		var counter = new MutableCounter { Value = 5 };
		var before = new MutableCounter { Value = counter.Value };
		counter.Value = 3;

		set.Check(before, counter).Should().ContainSingle();
		set.Check(before, new MutableCounter { Value = 9 }).Should().BeEmpty();
	}

	private static SourceLocation Where(Located l) => new("docset.yml", l.Line, 3, l.Id.Length);

	[Test]
	public void A_warning_is_reported_but_does_not_reject_the_change_and_rides_on_the_Applied_outcome()
	{
		var set = ConstraintSet.From(
			("EmailHasAt", Constraints<Customer>.Always(c => c.Email, e => e.Contains('@')).WithSeverity(Severity.Warning)),
			("NotEmpty", Constraints<Customer>.Always(c => c.Email.Length > 0))
		);

		var outcome = set.ApplyDetailed(new Customer("a@b"), _ => new Customer("nobody"));

		outcome.Should().BeOfType<ApplyOutcome<Customer>.Applied>().Which.Notices.Should().ContainSingle().Which.Severity.Should().Be(Severity.Warning);
		set.Apply(new Customer("a@b"), _ => new Customer("nobody")).Should().BeOfType<Result<Customer>.Ok>();
		set.Validate(new Customer("nobody")).Should().ContainSingle().Which.Severity.Should().Be(Severity.Warning);
		set.Apply(new Customer("a@b"), _ => new Customer("")).Should().BeOfType<Result<Customer>.Fail>("the empty one is still an error");
	}

	[Test]
	public void A_hint_works_on_the_escape_hatch_Check_and_survives_a_copy_into_another_set()
	{
		var rule = Constraints<Customer>.Check(c => [Violation.Error("x")]).WithSeverity(Severity.Hint);
		var first = ConstraintSet.From(("Hinted", rule));
		var second = ConstraintSet.From(("AlsoHinted", rule));

		first.Validate(new Customer("a")).Single().Severity.Should().Be(Severity.Hint);
		second.AsConstraints().Single().SeverityOverride.Should().Be(Severity.Hint);
		second.Validate(new Customer("a")).Single().Severity.Should().Be(Severity.Hint);
	}

	[Test]
	public void The_item_shapes_point_a_violation_at_the_item_that_caused_it()
	{
		var unique = ConstraintSet.From(("U", Constraints<Doc>.Unique(d => d.Items, i => i.Id, at: Where)));
		var references = ConstraintSet.From(
			("R", Constraints<Doc>.References<Located, string>(d => d.Items, i => i.Ref is null ? [] : [i.Ref], d => d.Items.Select(i => i.Id), at: Where))
		);
		var cycles = ConstraintSet.From(("C", Constraints<Doc>.Acyclic(d => d.Items, i => i.Id, i => i.Ref is null ? [] : [i.Ref], at: Where)));
		var chains = ConstraintSet.From(("N", Constraints<Doc>.NoChains(d => d.Items, i => i.Id, i => i.Ref ?? i.Id, at: Where)));

		unique
			.Validate(new Doc([new("a", 1), new("b", 2), new("a", 7)]))
			.Single()
			.Should()
			.Match<Violation>(v => v.File == "docset.yml" && v.Line == 7 && v.Column == 3 && v.Length == 1);
		references.Validate(new Doc([new("a", 1), new("b", 9, "ghost")])).Single().Line.Should().Be(9);
		cycles.Validate(new Doc([new("a", 4, "b"), new("b", 5, "a")])).Single().Line.Should().BeOneOf(4, 5);
		chains.Validate(new Doc([new("a", 2, "b"), new("b", 3, "c"), new("c", 4)])).Single().Line.Should().Be(2);
	}

	[Test]
	public void The_selector_overload_of_Always_takes_a_location()
	{
		var set = ConstraintSet.From(("E", Constraints<Located>.Always(l => l.Id, id => id.Length > 1, at: l => new SourceLocation("x.yml", l.Line))));

		var violation = set.Validate(new Located("a", 12)).Single();

		(violation.File, violation.Line, violation.Member).Should().Be(("x.yml", 12, "Id"));
	}
}
