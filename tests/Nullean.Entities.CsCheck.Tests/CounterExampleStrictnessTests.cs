using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

internal sealed record Tally(int Value, int Version);

public class CounterExampleStrictnessTests
{
	private static readonly Gen<AutoAccount> Accounts = Gen.Select(
		Gen.Int[0, 10],
		Gen.Int[1, 10],
		Gen.Int[0, 20],
		Gen.Int[0, 20],
		(v, ver, c, d) => new AutoAccount(v, ver, c, d)
	);

	// ── 39: a rule about an operation has no counter-example, and says so ────

	[Test]
	public void The_report_says_counter_examples_do_not_apply_to_an_idempotent_rule()
	{
		var set = ConstraintSet.From(
			("value-not-negative", Constraints<AutoAccount>.Always(a => a.Value >= 0, "A value is never negative.")),
			("value-settles", Constraints<AutoAccount>.Idempotent(a => a with { Value = Math.Min(a.Value, 8) }))
		);

		var report = AutoTest.For(set, Accounts).CounterExampleReport();

		var line = report.Lines.Single(l => l.Rule == "value-settles");
		line.Kind.Should().Be(CounterExampleKind.NotApplicable);
		report.ToString().Should().Contain("value-settles: not applicable").And.Contain("Faults()").And.NotContain("NONE FOUND, write one");
		report.Missing.Should().NotContain("value-settles");
	}

	[Test]
	public void A_rule_about_a_value_still_says_none_found_when_nothing_breaks_it()
	{
		var set = ConstraintSet.From(("never-true", Constraints<AutoAccount>.Always(_ => true, "nothing breaks this")));

		AutoTest.For(set, Accounts).CounterExampleReport().Missing.Should().Equal("never-true");
	}

	// ── 38: a counter-example that applies to only some bases ────────────────

	private static readonly Gen<Tally> Tallies = Gen.Select(Gen.Int[0, 10], Gen.Int[0, 2], (value, version) => new Tally(value, version));

	private static readonly Constraint<Tally> ValueNotNegative = Constraints<Tally>.Always(t => t.Value >= 0, "A value is never negative.");

	private static readonly Constraint<Tally> VersionGrows = Constraints<Tally>.Monotonic(t => t.Version, MonotonicDirection.NonDecreasing);

	private static readonly ConstraintSet<Tally> TallyRules = ConstraintSet.From(("value-not-negative", ValueNotNegative), ("version-grows", VersionGrows));

	// resets the version too, which the lifecycle-like rule refuses for any base that has one
	private static Tally Reset(Tally t) => t with { Value = -1, Version = 0 };

	[Test]
	public async Task A_counter_example_that_also_breaks_another_rule_for_some_bases_fails_vacancy()
	{
		var auto = AutoTest
			.For(TallyRules, Tallies)
			.CounterExample(ValueNotNegative, Reset)
			.CounterExample(VersionGrows, t => t with { Version = t.Version - 1 });

		var act = async () => await auto.VacancyAsync();

		(await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("also breaks version-grows");
	}

	[Test]
	public async Task AppliesTo_skips_the_bases_the_rule_cannot_be_broken_from_and_vacancy_passes()
	{
		var auto = AutoTest
			.For(TallyRules, Tallies)
			.CounterExample(ValueNotNegative, Reset, appliesTo: t => t.Version == 0)
			.CounterExample(VersionGrows, t => t with { Version = t.Version - 1 });

		(await auto.VacancyAsync()).Should().BeOfType<AxisResult.Passed>();
	}

	[Test]
	public async Task AppliesTo_that_accepts_no_generated_value_still_fails_so_a_typo_cannot_skip_every_base()
	{
		var auto = AutoTest
			.For(TallyRules, Tallies)
			.CounterExample(ValueNotNegative, Reset, appliesTo: t => t.Version == 99)
			.CounterExample(VersionGrows, t => t with { Version = t.Version - 1 });

		var act = async () => await auto.VacancyAsync();

		(await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("breaks nothing for any of");
	}

	// ── 37: hold found counter-examples to one cause ─────────────────────────

	private static ConstraintSet<AutoAccount> OverlappingRules() =>
		ConstraintSet.From(
			("value-not-negative", Constraints<AutoAccount>.Always(a => a.Value >= 0, "A value is never negative.")),
			("value-above-minus-five", Constraints<AutoAccount>.Always(a => a.Value > -5, "A value is above -5."))
		);

	[Test]
	public async Task A_found_counter_example_that_also_breaks_another_rule_is_accepted_by_default_and_refused_when_asked()
	{
		var plain = AutoTest.For(OverlappingRules(), Accounts);

		// the premise: for the second rule only a value that also breaks the first is found
		plain.CounterExampleReport().Lines.Single(l => l.Rule == "value-above-minus-five").Overlaps.Should().Contain("value-not-negative");
		(await plain.VacancyAsync()).Should().BeOfType<AxisResult.Passed>();

		var strict = async () => await plain.RequireSingleCause().VacancyAsync();

		(await strict.Should().ThrowAsync<Exception>())
			.Which
			.Message
			.Should()
			.Contain("value-above-minus-five")
			.And
			.Contain("also breaks value-not-negative")
			.And
			.Contain("RequireSingleCause");
	}

	// ── 40: a rule an axis barely exercises ──────────────────────────────────

	private static readonly Gen<AutoAccount> Sparse = Gen.Select(Gen.Int[0, 29], Gen.Int[1, 10], (v, ver) => new AutoAccount(v, ver, 0, 0));

	// only an account whose value is 0 is moved: about 1 run in 30
	private static AutoTest<AutoAccount> Barely() =>
		AutoTest.For(
			ConstraintSet.From(("version-grows", Constraints<AutoAccount>.Monotonic(a => a.Version, MonotonicDirection.NonDecreasing))),
			Sparse
		).Action("Bump", a => a.Value == 0 ? a with { Version = a.Version + 1 } : a);

	[Test]
	public async Task A_rule_exercised_in_few_runs_passes_with_a_warning_naming_it()
	{
		var result = await Barely().MonotonicityAsync();

		result
			.Should()
			.BeOfType<AxisResult.Passed>()
			.Which
			.Note
			.Should()
			.Contain("barely exercised")
			.And
			.Contain("version-grows")
			.And
			.Contain("MinimumExercise");
	}

	[Test]
	public async Task MinimumExercise_fails_an_axis_that_exercises_a_rule_in_a_smaller_share_of_runs()
	{
		var act = async () => await Barely().MinimumExercise(0.1).MonotonicityAsync();

		(await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("version-grows").And.Contain("MinimumExercise");
	}

	[Test]
	public async Task MinimumExercise_below_the_share_reached_passes_without_a_failure()
	{
		var result = await Barely().MinimumExercise(0.001).MonotonicityAsync();

		result.Should().BeOfType<AxisResult.Passed>();
	}

	[Test]
	public async Task A_rule_exercised_in_most_runs_has_no_warning()
	{
		var auto = AutoTest.For(
			ConstraintSet.From(("version-grows", Constraints<AutoAccount>.Monotonic(a => a.Version, MonotonicDirection.NonDecreasing))),
			Accounts
		).Action("Bump", a => a with { Version = a.Version + 1 });

		(await auto.MonotonicityAsync()).Should().BeOfType<AxisResult.Passed>().Which.Note.Should().NotContain("barely exercised");
	}

	[Test]
	public void MinimumExercise_is_a_share_between_zero_and_one()
	{
		var act = () => Barely().MinimumExercise(1.5);

		act.Should().Throw<ArgumentOutOfRangeException>();
	}
}
