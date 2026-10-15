using AwesomeAssertions;
using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

internal sealed record AutoAccount(int Value, int Version, int Credits, int Debits);

internal sealed record AutoMeter(int Reading, string Name)
{
	public AutoMeter Tick() => this with { Reading = Reading + 1 };
}

[Constraints]
internal static partial class MeterConstraints
{
	public static readonly Constraint<AutoMeter> NameRequired = Constraints<AutoMeter>.Always(m => !string.IsNullOrEmpty(m.Name), "A meter has a name.");

	public static readonly Constraint<AutoMeter> ReadingNeverDrops = Constraints<AutoMeter>.Monotonic(m => m.Reading, MonotonicDirection.NonDecreasing);
}

internal sealed record AutoOutcome<T>(T? Value, IReadOnlyList<string> Notes);

// The operation returns a wrapper (like a domain Result<T> that carries its diagnostics), so the generator has to read the new value out of it.
internal sealed record AutoCounter(int Count, string Name)
{
	public AutoOutcome<AutoCounter> Add(int amount) => new(this with { Count = Count + amount }, []);
}

[Constraints]
internal static partial class AutoCounterConstraints
{
	public static readonly Constraint<AutoCounter> NameRequired = Constraints<AutoCounter>.Always(c => !string.IsNullOrEmpty(c.Name), "A counter has a name.");

	public static readonly Constraint<AutoCounter> CountNeverDrops = Constraints<AutoCounter>.Monotonic(c => c.Count, MonotonicDirection.NonDecreasing);
}

public class AutoTestTests
{
	private static readonly Gen<AutoAccount> Accounts = Gen.Select(
		Gen.Int[0, 10],
		Gen.Int[1, 10],
		Gen.Int[0, 20],
		Gen.Int[0, 20],
		(v, ver, c, d) => new AutoAccount(v, ver, c, d)
	);
	private static readonly Gen<AutoMeter> Meters = Gen.Select(Gen.Int[5, 10], Gen.Const("meter"), (reading, name) => new AutoMeter(reading, name));

	// One rule for each state axis except confluence, which this type does not declare.
	private static ConstraintSet<AutoAccount> Rules() =>
		ConstraintSet.From(
			("value-not-negative", Constraints<AutoAccount>.Always(a => a.Value >= 0, "A value is never negative.")),
			("value-settles", Constraints<AutoAccount>.Idempotent(a => a with { Value = Math.Min(a.Value, 8) })),
			("version-grows", Constraints<AutoAccount>.Monotonic(a => a.Version, MonotonicDirection.NonDecreasing)),
			("total-kept", Constraints<AutoAccount>.Conserves(a => a.Credits + a.Debits))
		);

	private static IReadOnlyDictionary<string, Func<AutoAccount, AutoAccount>> CounterExamples { get; } = new Dictionary<string, Func<AutoAccount, AutoAccount>>
	{
		["value-not-negative"] = a => a with { Value = -1 },
		["version-grows"] = a => a with { Version = a.Version - 1 },
		["total-kept"] = a => a with { Credits = a.Credits + 1 }
	};

	private static AutoTest<AutoAccount> Sound() =>
		AutoTest
			.For(Rules(), Accounts)
			.CounterExamples(CounterExamples)
			.Action("Bump", a => a with { Version = a.Version + 1 })
			.Action<int>("Transfer", (a, n) => a with { Credits = a.Credits - n, Debits = a.Debits + n });

	[Test]
	public async Task A_sound_type_passes_every_axis_it_declares_and_reports_the_one_it_does_not()
	{
		var report = await Sound().RunAsync();

		report.Results.Select(r => (r.Axis, r.GetType().Name)).Should().Equal(
			(Axis.Invariant, nameof(AxisResult.Passed)),
			(Axis.Idempotency, nameof(AxisResult.Passed)),
			(Axis.Monotonicity, nameof(AxisResult.Passed)),
			(Axis.Conservation, nameof(AxisResult.Passed)),
			(Axis.Confluence, nameof(AxisResult.NotApplicable)),
			(Axis.Vacancy, nameof(AxisResult.Passed))
		);
		report.ToString().Should().Contain("Confluence: not applicable");
	}

	[Test]
	public async Task An_operation_that_breaks_a_rule_fails_only_the_axis_it_breaks_and_run_names_it()
	{
		var broken = Sound().Action("Rewind", a => a with { Version = a.Version - 1 });

		var report = await broken.ReportAsync();

		report.Failures.Select(f => f.Axis).Should().Equal(Axis.Monotonicity);
		report.Results.Single(r => r.Axis == Axis.Conservation).Should().BeOfType<AxisResult.Passed>();
		var act = async () => await broken.RunAsync();
		(await act.Should().ThrowAsync<InvalidOperationException>())
			.Which
			.Message
			.Should()
			.Contain("1 of 6 axes failed")
			.And
			.Contain("Rewind")
			.And
			.Contain("version-grows");
	}

	[Test]
	public async Task A_failure_carries_the_seed_that_replays_it()
	{
		var report = await Sound().Action("Rewind", a => a with { Version = a.Version - 1 }).ReportAsync();

		report.Failures.Single().Seed.Should().NotBeNullOrEmpty();
	}

	[Test]
	public async Task A_required_axis_the_type_does_not_declare_fails_instead_of_being_not_applicable()
	{
		var report = await Sound().RequireAxes(Axis.Confluence).ReportAsync();

		var failure = report.Failures.Should().ContainSingle().Subject;
		failure.Axis.Should().Be(Axis.Confluence);
		failure.Report.Should().Contain("RequireAxes");
	}

	[Test]
	public async Task Cases_list_the_declared_axes_and_a_failing_one_says_its_law_and_consequence()
	{
		var cases = Sound().Action("Rewind", a => a with { Version = a.Version - 1 }).Cases;

		cases.Select(c => c.Axis).Should().Equal(Axis.Invariant, Axis.Idempotency, Axis.Monotonicity, Axis.Conservation, Axis.Vacancy);
		(await cases.Single(c => c.Axis == Axis.Invariant).RunAsync()).Should().BeOfType<AxisResult.Passed>();
		var act = async () => await cases.Single(c => c.Axis == Axis.Monotonicity).RunAsync();
		(await act.Should().ThrowAsync<InvalidOperationException>())
			.Which
			.Message
			.Should()
			.Contain("Law")
			.And
			.Contain("Consequence")
			.And
			.Contain("Rewind")
			.And
			.Contain("Operation");
	}

	[Test]
	public void An_axis_the_type_requires_is_a_case_even_when_nothing_is_declared_for_it()
	{
		Sound().RequireAxes(Axis.Confluence).Cases.Select(c => c.Axis).Should().Contain(Axis.Confluence);
	}

	[Test]
	public async Task Configuring_returns_a_copy_and_leaves_the_original_alone()
	{
		var original = AutoTest.For(Rules(), Accounts).CounterExamples(CounterExamples);

		var withAction = original.Action("Bump", a => a with { Version = a.Version + 1 });

		(await original.MonotonicityAsync()).Should().BeOfType<AxisResult.Skipped>("the original has no operations");
		(await withAction.MonotonicityAsync()).Should().BeOfType<AxisResult.Passed>();
	}

	[Test]
	public async Task A_sequential_rule_written_by_hand_is_listed_as_unclassified()
	{
		var set = ConstraintSet.From(
			("value-not-negative", Constraints<AutoAccount>.Always(a => a.Value >= 0, "A value is never negative.")),
			("hand-written", Constraints<AutoAccount>.CheckTransition((_, _) => []))
		);

		var report = await AutoTest
			.For(set, Accounts)
			.CounterExamples(new Dictionary<string, Func<AutoAccount, AutoAccount>> { ["value-not-negative"] = a => a with { Value = -1 } })
			.ReportAsync();

		report.Unclassified.Should().Equal("hand-written");
		report.ToString().Should().Contain("Unclassified: hand-written");
	}

	[Test]
	public async Task A_generated_companion_supplies_its_rules_and_the_operations_it_found()
	{
		var report = await AutoTest
			.For(MeterConstraints.Instance, Meters)
			.CounterExamples(new Dictionary<string, Func<AutoMeter, AutoMeter>>
			{
				["name-required"] = m => m with { Name = "" },
				["reading-never-drops"] = m => m with { Reading = m.Reading - 1 }
			})
			.RunAsync();

		report.Results.Single(r => r.Axis == Axis.Monotonicity).Should().BeOfType<AxisResult.Passed>().Which.Rules.Should().Equal("reading-never-drops");
	}

	[Test]
	public async Task An_operation_the_generator_read_out_of_a_wrapper_is_run_by_the_monotonicity_axis()
	{
		AutoCounterConstraints.Actions.Select(a => a.Name).Should().Equal("Add");

		var report = await AutoTest
			.For(AutoCounterConstraints.Instance, Gen.Select(Gen.Int[0, 10], Gen.Const("counter"), (count, name) => new AutoCounter(count, name)))
			.CounterExamples(new Dictionary<string, Func<AutoCounter, AutoCounter>>
			{
				["name-required"] = c => c with { Name = "" },
				["count-never-drops"] = c => c with { Count = c.Count - 1 }
			})
			.RunAsync();

		report.Results.Single(r => r.Axis == Axis.Monotonicity).Should().BeOfType<AxisResult.Passed>();
	}

	[Test]
	public async Task The_idempotent_rule_needs_no_counter_example_because_its_own_axis_proves_it_was_exercised()
	{
		var set = ConstraintSet.From(("value-settles", Constraints<AutoAccount>.Idempotent(a => a with { Value = Math.Min(a.Value, 8) })));

		var result = await set.VacancyAsync(Accounts);

		result.Should().BeOfType<AxisResult.Passed>().Which.Rules.Should().Equal("value-settles");
	}

	[Test]
	public async Task An_axis_that_fails_in_a_run_does_not_hide_the_others()
	{
		var report = await AutoTest.For(Rules(), Accounts).CounterExamples(CounterExamples).ReportAsync();

		// no operations were given: monotonicity and conservation are skipped, the rest still ran
		report.Results.Where(r => r is AxisResult.Skipped).Select(r => r.Axis).Should().Equal(Axis.Monotonicity, Axis.Conservation);
		report.Passed.Should().BeTrue();
	}

	[Test]
	public async Task Data_set_after_an_action_still_decides_the_data_that_action_is_run_with()
	{
		var longest = 0;
		var auto = AutoTest
			.For(Rules(), Accounts)
			.Action<string>("Label", (a, text) =>
			{
				longest = Math.Max(longest, text?.Length ?? 0);
				return a with { Version = a.Version + 1 };
			})
			.Data(AutoMode.Adversarial);

		await auto.MonotonicityAsync();

		longest.Should().BeGreaterThan(40, "adversarial strings include very long ones, wherever Data(...) was called");
	}

	/// <summary>
	/// Law: an operation that takes a valid value to one that breaks an invariant fails the Invariant axis, naming the operation and the rule.
	/// Consequence if broken: rules hold for every generated value while a method of the type can still produce a value that breaks them.
	/// </summary>
	[Test]
	public async Task An_operation_that_breaks_an_invariant_fails_the_invariant_axis()
	{
		var broken = Sound().Action("Overdraw", a => a with { Value = -1 });

		var act = async () => await broken.InvariantAsync();

		(await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("Overdraw").And.Contain("value-not-negative");
	}

	[Test]
	public async Task Operations_that_keep_the_invariants_are_named_in_the_pass()
	{
		(await Sound().InvariantAsync()).Should().BeOfType<AxisResult.Passed>().Which.Note.Should().Contain("Bump").And.Contain("Transfer");
	}
}
