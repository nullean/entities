using AwesomeAssertions;

namespace Nullean.Entities.Tests;

internal sealed record Mount(string Path);

internal sealed record Release(
	string? From,
	string? To,
	IReadOnlyList<string> Versions,
	string Env,
	IReadOnlyList<string> Flags,
	string? One,
	string? Other,
	string? User,
	string? Password
);

public class ConstraintVocabularyTests
{
	private static Release Make(
		string? from = null,
		string? to = null,
		string[]? versions = null,
		string env = "prod",
		string[]? flags = null,
		string? one = null,
		string? other = null,
		string? user = null,
		string? password = null
	) => new(from, to, versions ?? [], env, flags ?? [], one, other, user, password);

	[Test]
	public void Each_names_the_offending_item_in_its_message()
	{
		var rule = Constraints<Release>.Each(r => r.Versions, v => v.StartsWith('v'), v => $"version '{v}' has no v prefix");

		var found = rule.Check(Make(versions: ["v1", "2", "v3", "4"]), Make(versions: ["v1", "2", "v3", "4"])).ToList();

		found.Select(v => v.Message).Should().Equal("version '2' has no v prefix", "version '4' has no v prefix");
		found[0].Member.Should().Be("Versions");
		found[0].Evidence.Should().Be("2");
	}

	[Test]
	public void Each_with_a_problem_function_reports_why_each_item_is_bad()
	{
		var rule = Constraints<Release>.Each(r => r.Versions, v => v.Length > 3 ? $"'{v}' is too long" : null);

		rule.Check(Make(versions: ["a", "abcd"]), Make(versions: ["a", "abcd"])).Single().Message.Should().Be("'abcd' is too long");
	}

	[Test]
	public void OneOf_lists_the_allowed_values()
	{
		var rule = Constraints<Release>.OneOf(r => r.Env, ["dev", "edge", "staging", "prod"]);
		var set = ConstraintSet.From(("env", rule));

		set.Validate(Make(env: "prod")).Should().BeEmpty();
		var violation = set.Validate(Make(env: "local")).Single();
		violation.Message.Should().Be("'local' is not one of dev, edge, staging, prod.");
		violation.Member.Should().Be("Env");
		violation.Evidence.Should().Be("local");
	}

	[Test]
	public void OneOf_can_take_its_allowed_values_from_the_value_itself_and_compare_case_insensitively()
	{
		var rule = Constraints<Release>.OneOf(r => r.Env, r => r.Versions, StringComparer.OrdinalIgnoreCase);
		var set = ConstraintSet.From(("env", rule));

		set.Validate(Make(env: "PROD", versions: ["prod"])).Should().BeEmpty();
		set.Validate(Make(env: "dev", versions: ["prod"])).Should().HaveCount(1);
	}

	[Test]
	public void EachOneOf_reports_every_stray_value()
	{
		var set = ConstraintSet.From(("flags", Constraints<Release>.EachOneOf(r => r.Flags, ["a", "b"])));

		set.Validate(Make(flags: ["a", "x", "y"])).Select(v => v.Evidence).Should().Equal("x", "y");
	}

	[Test]
	[Arguments(0, 1, null, true)]
	[Arguments(2, 1, 3, true)]
	[Arguments(4, 1, 3, false)]
	[Arguments(0, 1, null, true)]
	public void CountBetween_bounds_a_collection(int count, int min, int? max, bool holds)
	{
		var set = ConstraintSet.From(("n", Constraints<Release>.CountBetween(r => r.Versions, min, max)));
		var versions = Enumerable.Range(0, count).Select(i => $"v{i}").ToArray();

		set.Validate(Make(versions: versions)).Count.Should().Be(count == 0 && min > 0 ? 1 : holds ? 0 : 1);
	}

	[Test]
	public void CountBetween_message_says_what_was_expected()
	{
		var set = ConstraintSet.From(("n", Constraints<Release>.CountBetween(r => r.Versions, 2, 3)));

		set.Validate(Make(versions: ["v1"])).Single().Message.Should().Be("Versions has 1 item, expected between 2 and 3.");
	}

	[Test]
	public void AtMostOne_names_the_members_that_are_set()
	{
		var set = ConstraintSet.From(("x", Constraints<Release>.AtMostOne(("one", r => r.One is not null), ("other", r => r.Other is not null))));

		set.Validate(Make(one: "a")).Should().BeEmpty();
		set.Validate(Make()).Should().BeEmpty();
		set.Validate(Make(one: "a", other: "b")).Single().Message.Should().Be("Set at most one of one, other; set: one, other.");
	}

	[Test]
	public void ExactlyOne_requires_a_choice()
	{
		var set = ConstraintSet.From(("x", Constraints<Release>.ExactlyOne(("one", r => r.One is not null), ("other", r => r.Other is not null))));

		set.Validate(Make(one: "a")).Should().BeEmpty();
		set.Validate(Make()).Single().Message.Should().Contain("none is set");
		set.Validate(Make(one: "a", other: "b")).Should().HaveCount(1);
	}

	[Test]
	public void AllOrNone_rejects_half_of_a_pair()
	{
		var set = ConstraintSet.From(("x", Constraints<Release>.AllOrNone(("user", r => r.User is not null), ("password", r => r.Password is not null))));

		set.Validate(Make()).Should().BeEmpty();
		set.Validate(Make(user: "u", password: "p")).Should().BeEmpty();
		set.Validate(Make(user: "u")).Single().Message.Should().Contain("set: user");
	}

	[Test]
	public void InOrder_compares_two_members_and_ignores_an_open_bound()
	{
		var set = ConstraintSet.From(("range", Constraints<Release>.InOrder(r => r.From, r => r.To)));

		set.Validate(Make(from: "a", to: "b")).Should().BeEmpty();
		set.Validate(Make(from: "a", to: "a")).Should().BeEmpty();
		set.Validate(Make(from: "a")).Should().BeEmpty();
		set.Validate(Make(from: "b", to: "a")).Single().Message.Should().Be("'b' must come before 'a'.");
	}

	[Test]
	public void InOrder_strict_rejects_equal_values_and_takes_a_version_comparer()
	{
		var strict = ConstraintSet.From(("range", Constraints<Release>.InOrder(r => r.From, r => r.To, strict: true)));
		var numeric = ConstraintSet.From(
			("range", Constraints<Release>.InOrder(
				r => r.From,
				r => r.To,
				comparer: Comparer<string?>.Create((a, b) => Version.Parse(a!).CompareTo(Version.Parse(b!)))
			))
		);

		strict.Validate(Make(from: "a", to: "a")).Should().HaveCount(1);
		numeric.Validate(Make(from: "9.0", to: "10.0")).Should().BeEmpty();
		numeric.Validate(Make(from: "10.0", to: "9.0")).Should().HaveCount(1);
	}

	[Test]
	public void Sorted_reports_each_item_out_of_place()
	{
		var set = ConstraintSet.From(("sorted", Constraints<Release>.Sorted(r => r.Versions, v => v)));

		set.Validate(Make(versions: ["a", "b", "c"])).Should().BeEmpty();
		set.Validate(Make(versions: ["a", "c", "b", "d"])).Select(v => v.Evidence).Should().Equal("b");
	}

	[Test]
	public void NoPrefixes_by_segment_allows_siblings_that_share_text()
	{
		var rule = Constraints<(IReadOnlyList<Mount> Mounts, int X)>.NoPrefixes(
			p => p.Mounts,
			m => m.Path
		);
		var set = ConstraintSet.From(("mounts", rule));
		IReadOnlyList<Mount> mounts = [new("reference/elasticsearch"), new("reference/elasticsearch-plugins"), new("reference/elasticsearch/query")];

		var violation = set.Validate((mounts, 0)).Single();

		violation.Evidence.Should().Be(("reference/elasticsearch", "reference/elasticsearch/query"));
	}

	[Test]
	public void NoPrefixes_by_text_reports_the_naive_prefix_collision()
	{
		var rule = Constraints<IReadOnlyList<Mount>>.NoPrefixes(m => m, m => m.Path, PrefixMatch.Text);
		var set = ConstraintSet.From(("mounts", rule));
		IReadOnlyList<Mount> mounts = [new("reference/apm"), new("reference/apm-agents"), new("other")];

		set.Validate(mounts).Single().Evidence.Should().Be(("reference/apm", "reference/apm-agents"));
	}

	[Test]
	public void Unique_with_a_comparer_is_case_insensitive_and_describe_names_the_key()
	{
		var rule = Constraints<IReadOnlyList<Mount>>.Unique(
			m => m,
			m => m.Path,
			StringComparer.OrdinalIgnoreCase,
			describe: (key, count) => $"{count} mounts at '{key}'"
		);
		var set = ConstraintSet.From(("unique", rule));

		set.Validate([new("A"), new("a")]).Single().Message.Should().Be("2 mounts at 'A'");
	}

	[Test]
	public void References_describe_can_name_the_referring_item()
	{
		var rule = Constraints<(IReadOnlyList<Mount> Mounts, string[] Known)>.References(
			p => p.Mounts,
			m => [m.Path],
			p => p.Known,
			describe: (m, key) => $"mount '{m.Path}' refers to unknown '{key}'"
		);
		var set = ConstraintSet.From(("refs", rule));

		set.Validate(([new("x"), new("y")], ["x"])).Single().Message.Should().Be("mount 'y' refers to unknown 'y'");
	}

	[Test]
	public void Always_takes_an_explicit_member_for_a_computed_value()
	{
		var rule = Constraints<Release>.Always(r => r.Versions.Count, n => n < 2, "too many", member: "Versions");
		var set = ConstraintSet.From(("n", rule));

		set.Validate(Make(versions: ["a", "b"])).Single().Member.Should().Be("Versions");
	}

	[Test]
	public void A_set_can_be_re_leveled_without_touching_the_original()
	{
		var rule = Constraints<Release>.Always(r => r.Env == "prod", "not prod");
		var set = ConstraintSet.From(("prod-only", rule));
		var warned = set.WithSeverity("prod-only", Severity.Warning);

		warned.Validate(Make(env: "dev")).Single().Severity.Should().Be(Severity.Warning);
		set.Validate(Make(env: "dev")).Single().Severity.Should().Be(Severity.Error);
		warned.Strict().Validate(Make(env: "dev")).Single().Severity.Should().Be(Severity.Error);
	}

	[Test]
	public void Without_removes_a_rule_and_an_unknown_name_throws()
	{
		var set = ConstraintSet.From(
			("a", Constraints<Release>.Always(_ => false, "a")),
			("b", Constraints<Release>.Always(_ => false, "b"))
		);

		set.Without("a").Validate(Make()).Single().Message.Should().Be("b");
		var unknown = () => set.Without("zzz");
		unknown.Should().Throw<ArgumentException>();
	}

	[Test]
	public void Each_with_the_root_can_name_what_the_item_was_checked_against()
	{
		var set = ConstraintSet.From(
			("known", Constraints<Release>.Each(r => r.Versions, (r, v) => r.Flags.Contains(v) ? null : $"'{v}' is not among {string.Join(", ", r.Flags)}"))
		);

		set.Validate(Make(versions: ["a", "z"], flags: ["a", "b"])).Single().Message.Should().Be("'z' is not among a, b");
	}

	[Test]
	public void EachCheck_lets_one_item_report_several_violations_of_different_severity()
	{
		var set = ConstraintSet.From(
			("item", Constraints<Release>.EachCheck(r => r.Versions, (_, v) => v == "bad" ? [Violation.Error("bad one"), Violation.Warning("also odd")] : []))
		);

		var found = set.Validate(Make(versions: ["ok", "bad"]));

		found.Select(v => (v.Message, v.Severity)).Should().Equal(("bad one", Severity.Error), ("also odd", Severity.Warning));
		found.Should().OnlyContain(v => v.Member == "Versions" && (string?)v.Evidence == "bad");
	}

	[Test]
	public void OneOf_describe_sees_the_value_and_the_allowed_set()
	{
		var set = ConstraintSet.From(
			("env", Constraints<Release>.OneOf(r => r.Env, ["a", "b"], describe: (v, allowed) => $"Environment '{v}' (allowed: {string.Join("|", allowed)})"))
		);

		set.Validate(Make(env: "x")).Single().Message.Should().Be("Environment 'x' (allowed: a|b)");
	}

	[Test]
	public void Unique_describeGroup_sees_every_member_of_the_group()
	{
		var rule = Constraints<IReadOnlyList<Mount>>.Unique(
			m => m,
			m => m.Path.Length,
			describeGroup: (len, members) => $"{members.Count} paths of length {len}: {string.Join(", ", members.Select(m => m.Path))}"
		);

		ConstraintSet.From(("u", rule)).Validate([new("ab"), new("cd"), new("e")]).Single().Message.Should().Be("2 paths of length 2: ab, cd");
	}

	[Test]
	public void Disjoint_and_EachUsed_find_shared_keys_and_orphans()
	{
		var shared = ConstraintSet.From(("d", Constraints<Release>.Disjoint(r => r.Versions, r => r.Flags)));
		var orphans = ConstraintSet.From(("o", Constraints<Release>.EachUsed(r => r.Versions, v => v, r => r.Flags)));

		shared.Validate(Make(versions: ["a", "b"], flags: ["b", "c"])).Single().Evidence.Should().Be("b");
		orphans.Validate(Make(versions: ["a", "b"], flags: ["b"])).Single().Evidence.Should().Be("a");
	}

	[Test]
	public void NoPrefixes_TextNotSegment_reports_only_the_ambiguous_pair()
	{
		var rule = Constraints<IReadOnlyList<Mount>>.NoPrefixes(m => m, m => m.Path, PrefixMatch.TextNotSegment);

		ConstraintSet.From(("m", rule)).Validate([new("a/b"), new("a/b/c"), new("a/bc")]).Single().Evidence.Should().Be(("a/b", "a/bc"));
	}

	[Test]
	public void MonotonicPerKey_compares_each_entry_with_its_own_earlier_value()
	{
		var rule = Constraints<IReadOnlyList<(string Id, int At)>>.MonotonicPerKey(e => e, e => e.Id, e => e.At, MonotonicDirection.NonDecreasing);
		var set = ConstraintSet.From(("at", rule));

		set.Check([("a", 5), ("b", 5)], [("a", 6), ("b", 4), ("c", 1)]).Select(v => v.Evidence).Should().Equal((5, 4));
	}

	[Test]
	public void Consistent_reports_a_key_that_maps_to_several_values()
	{
		var rule = Constraints<IReadOnlyList<(string Bundle, string Product)>>.Consistent(e => e, e => e.Bundle, e => e.Product);

		var found = ConstraintSet.From(("c", rule)).Validate([("a", "x"), ("a", "y"), ("b", "x"), ("b", "x")]);

		found.Single().Evidence.Should().Be("a");
		found.Single().Message.Should().Be("'a' has 2 different values: x, y.");
	}

	[Test]
	public void NoneOf_names_each_member_that_is_set()
	{
		var set = ConstraintSet.From(("n", Constraints<Release>.NoneOf(("one", r => r.One is not null), ("other", r => r.Other is not null))));

		set.Validate(Make()).Should().BeEmpty();
		set.Validate(Make(one: "a", other: "b")).Select(v => v.Member).Should().Equal("one", "other");
	}

	[Test]
	public void Always_describe_builds_the_message_from_the_value_and_the_other_hooks_take_their_items()
	{
		var always = ConstraintSet.From(("a", Constraints<Release>.Always(r => r.Env, e => e == "prod", describe: e => $"env '{e}' is not prod")));
		var prefixes = ConstraintSet.From(
			("p", Constraints<IReadOnlyList<Mount>>.NoPrefixes(m => m, m => m.Path, describe: (outer, inner) => $"{outer.Path} swallows {inner.Path}"))
		);
		var monotonic = ConstraintSet.From(
			("m", Constraints<IReadOnlyList<(string Id, int At)>>.MonotonicPerKey(
				e => e,
				e => e.Id,
				e => e.At,
				MonotonicDirection.NonDecreasing,
				describe: (k, b, a) => $"{k}: {b} -> {a}"
			))
		);

		always.Validate(Make(env: "dev")).Single().Message.Should().Be("env 'dev' is not prod");
		prefixes.Validate([new("a"), new("a/b")]).Single().Message.Should().Be("a swallows a/b");
		monotonic.Check([("k", 3)], [("k", 1)]).Single().Message.Should().Be("k: 3 -> 1");
	}

	[Test]
	public void A_rule_can_be_a_warning_until_a_date_and_an_error_after()
	{
		var rule = Constraints<Release>.Always(_ => false, "late").WithSeverityUntil(
			Severity.Warning,
			new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
			Severity.Error
		);
		var set = ConstraintSet.From(("late", rule));
		var before = new ManualTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
		var after = new ManualTimeProvider(new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero));

		set.Validate(Make(), before).Single().Severity.Should().Be(Severity.Warning);
		set.Validate(Make(), after).Single().Severity.Should().Be(Severity.Error);
	}
}
