using System.Text.RegularExpressions;
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

internal sealed record Profile(string Name, int Age, double Score, ItemKind Kind);

public partial class BoundariesTests
{
	private static readonly ConstraintSet<Profile> Rules = ConstraintSet.From(
		("NameNotEmpty", Constraints<Profile>.Always(p => !string.IsNullOrEmpty(p.Name), "name")),
		("AgePositive", Constraints<Profile>.Always(p => p.Age > 0, "age")),
		("ScoreInRange", Constraints<Profile>.Always(p => p.Score is >= 0 and <= 1, "score"))
	);

	private static readonly Profile Baseline = new("Ada", 36, 0.5, ItemKind.Alpha);

	[Test]
	public void A_rule_that_rejects_a_boundary_is_named_against_the_value()
	{
		var report = Rules.Boundaries(Baseline);

		report.Results.Single(r => r.Property == "Age" && r.Value == "0").RejectedBy.Should().Contain("AgePositive");
		report.Results.Single(r => r.Property == "Age" && r.Value == "-1").RejectedBy.Should().Contain("AgePositive");
		report.RulesFor("Age").Should().Equal("AgePositive");
	}

	[Test]
	public void Hostile_values_nothing_rejects_are_listed_as_gaps()
	{
		var report = Rules.Boundaries(Baseline);

		report.Gaps.Should().Contain(g => g.Property == "Name" && g.Value == "\" \"");
		report.Gaps.Should().Contain(g => g.Property == "Name" && g.Value.Contains("\\u200B"));
		report.Gaps.Should().Contain(g => g.Property == "Age" && g.Value == int.MaxValue.ToString());
		report.Gaps.Should().Contain(g => g.Property == "Kind" && g.RejectedBy.Count == 0);
	}

	[Test]
	public void A_rule_that_does_reject_a_hostile_value_leaves_no_gap_for_it()
	{
		var report = Rules.Boundaries(Baseline);

		report.Gaps.Should().NotContain(g => g.Property == "Name" && g.Value == "\"\"");
		report.Gaps.Should().NotContain(g => g.Property == "Score" && g.Value == "NaN");
		report.Gaps.Should().NotContain(g => g.Property == "Score" && g.Value == "Infinity");
		report.Gaps.Should().NotContain(g => g.Property == "Age" && g.Value == "-1");
	}

	[Test]
	public void The_report_reads_as_documentation()
	{
		var text = Rules.Boundaries(Baseline).ToString();

		text.Should().Contain("rejected by AgePositive").And.Contain("GAP, nothing rejects");
	}

	[Test]
	public void Without_a_baseline_a_plausible_valid_one_is_generated()
	{
		var loose = ConstraintSet.From(("AgePositive", Constraints<Profile>.Always(p => p.Age > 0, "age")));

		loose.Boundaries().Results.Should().NotBeEmpty();
	}
}

public partial class AutoGenCatalogTests
{
	private static List<T> SeenList<T>(AutoMode mode, int draws = 6_000) =>
		[.. Enumerable.Range(0, draws).Select(i => GenAuto.Of<T>(mode).Generate(new PCG(1u, (ulong)i + 1), null, out _))];

	// A set would collapse -0.0 into 0.0 and 1.00m into 1m, which are exactly the edge cases being checked.
	private static HashSet<T> Seen<T>(AutoMode mode, int draws = 6_000) => [.. SeenList<T>(mode, draws)];

	[Test]
	public void Integers_reach_zero_negatives_extremes_and_powers_of_two()
	{
		var seen = Seen<int>(AutoMode.Adversarial);

		seen.Should().Contain([0, 1, -1, int.MinValue, int.MaxValue, 255, 256, 65535, 65536, 99, 100, 101]);
	}

	[Test]
	public void Narrow_integers_stay_in_range_and_reach_their_own_extremes()
	{
		Seen<byte>(AutoMode.Adversarial).Should().Contain([(byte)0, byte.MaxValue, (byte)255]);
		Seen<sbyte>(AutoMode.Adversarial).Should().Contain([sbyte.MinValue, sbyte.MaxValue, (sbyte)-1]);
		Seen<long>(AutoMode.Adversarial).Should().Contain([long.MinValue, long.MaxValue, 9_007_199_254_740_993L]);
	}

	[Test]
	public void Doubles_reach_nan_infinity_negative_zero_subnormals_and_the_precision_cliff()
	{
		var seen = SeenList<double>(AutoMode.Adversarial);

		seen.Should().Contain(d => double.IsNaN(d));
		seen.Should().Contain([double.PositiveInfinity, double.NegativeInfinity, double.Epsilon, double.MaxValue, 9007199254740993.0, 0.1 + 0.2]);
		seen.Should().Contain(d => d == 0 && double.IsNegative(d));
	}

	[Test]
	public void Decimals_reach_equal_numbers_with_different_scales_and_rounding_traps()
	{
		var seen = SeenList<decimal>(AutoMode.Adversarial);

		seen.Should().Contain([0.005m, 0.015m, decimal.MaxValue, decimal.MinValue]);
		seen.Where(d => d == 1m).Select(d => decimal.GetBits(d)[3]).Distinct().Count().Should().BeGreaterThan(2);
	}

	[Test]
	public void Dates_reach_the_epoch_leap_days_year_2038_and_the_extreme_offsets()
	{
		var offsets = Seen<DateTimeOffset>(AutoMode.Adversarial);

		offsets.Should().Contain([DateTimeOffset.UnixEpoch, DateTimeOffset.MaxValue, new DateTimeOffset(2038, 1, 19, 3, 14, 7, TimeSpan.Zero)]);
		offsets.Should().Contain(o => o.Offset == TimeSpan.FromHours(14));
		offsets.Should().Contain(o => o.Offset == TimeSpan.FromHours(-12));
		offsets.Should().Contain(o => o.Offset == new TimeSpan(5, 45, 0));
		Seen<DateOnly>(AutoMode.Adversarial).Should().Contain([new DateOnly(2024, 2, 29), new DateOnly(1900, 3, 1), DateOnly.MaxValue]);
		Seen<TimeSpan>(AutoMode.Adversarial).Should().Contain([TimeSpan.Zero, TimeSpan.MinValue, TimeSpan.MaxValue, TimeSpan.FromHours(24)]);
	}

	[Test]
	public void Strings_also_come_from_a_realistic_corpus_matched_to_the_member_name()
	{
		var emails = Enumerable.Range(0, 400).Select(i => GenAuto.Of<Account>(AutoMode.Plausible).Generate(new PCG(1u, (ulong)i + 1), null, out _).Email);

		emails.Should().OnlyContain(e => e.Contains('@') && e.Contains('.'));
	}

	[Test]
	public void A_failing_property_shrinks_toward_a_small_value()
	{
		var act = () => GenAuto.Of<Item>(AutoMode.Plausible).Sample(i => i.Name.Length <= 3, iter: 2_000);

		var message = act.Should().Throw<CsCheckException>().Which.Message;
		var name = NameInMessage().Match(message).Groups[1].Value;
		name.Length.Should().BeInRange(4, 20);
	}

	[GeneratedRegex("Name = ([^,]*),")]
	private static partial Regex NameInMessage();
}
