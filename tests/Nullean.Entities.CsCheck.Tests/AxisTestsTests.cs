using AwesomeAssertions;
using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

internal sealed record AxisTally(int Value, int Credits, int Debits);

internal sealed record AxisBag(IReadOnlyList<int> Items);

internal sealed record AxisHolder(AxisTally Tally, string Label);

public class AxisTestsTests
{
	private static readonly Gen<AxisTally> Tallies = Gen.Select(Gen.Int[-5, 10], Gen.Int[0, 20], Gen.Int[0, 20], (v, c, d) => new AxisTally(v, c, d));
	private static readonly Gen<AxisTally> NonNegative = Gen.Select(Gen.Int[0, 10], Gen.Int[0, 20], Gen.Int[0, 20], (v, c, d) => new AxisTally(v, c, d));
	// Valid for "a value above 5 has no debits": the generator itself must respect the rules, or Laws reports it.
	private static readonly Gen<AxisTally> NoDebitsWhenBig = Gen.Select(
		Gen.Int[0, 10],
		Gen.Int[0, 20],
		Gen.Int[0, 20],
		(v, c, d) => new AxisTally(v, c, v > 5 ? 0 : d)
	);
	private static readonly Gen<AxisBag> Bags = Gen.Int[0, 9].List[2, 5].Select(items => new AxisBag(items));
	private static readonly Gen<AxisBag> SingleItemBags = Gen.Int[0, 9].Select(i => new AxisBag([i]));
	private static readonly AxisRun Plausible = new(Samples: 100, Data: AutoMode.Plausible);

	private static EntityAction<AxisTally> Action(string name, Func<AxisTally, AxisTally> apply) => new(name, null, (t, _) => apply(t));

	private static ConstraintSet<AxisTally> Set(params (string, Constraint<AxisTally>)[] rules) => ConstraintSet.From(rules);

	// ── 01 Invariant ─────────────────────────────────────────────────────────────

	[Test]
	public void Invariant_passes_when_the_rules_hold_and_a_given_counter_example_breaks_only_its_rule()
	{
		var set = Set(("non-negative", Constraints<AxisTally>.Always(t => t.Value >= 0, "A value is never negative.")));

		var result = set.Invariant(NonNegative, new Dictionary<string, Func<AxisTally, AxisTally>> { ["non-negative"] = t => t with { Value = -1 } });

		result.Should().BeOfType<AxisResult.Passed>().Which.Rules.Should().Equal("non-negative");
	}

	[Test]
	public void Invariant_is_not_applicable_when_every_rule_is_on_another_axis()
	{
		var set = Set(("idempotent", Constraints<AxisTally>.Idempotent(t => t)));

		set.Invariant(Tallies).Should().BeOfType<AxisResult.NotApplicable>();
	}

	// ── 02 Idempotency ───────────────────────────────────────────────────────────

	[Test]
	public void Idempotency_passes_for_an_action_that_settles_and_reports_that_it_changed_values()
	{
		var set = Set(("abs-is-idempotent", Constraints<AxisTally>.Idempotent(t => t with { Value = Math.Abs(t.Value) })));

		var result = set.Idempotency(Tallies, Plausible);

		var passed = result.Should().BeOfType<AxisResult.Passed>().Subject;
		passed.Rules.Should().Equal("abs-is-idempotent");
		passed.Note.Should().Contain("changed a value in");
	}

	[Test]
	public void Idempotency_catches_an_action_that_keeps_changing_the_value()
	{
		var set = Set(("increments", Constraints<AxisTally>.Idempotent(t => t with { Value = t.Value + 1 })));

		var act = () => set.Idempotency(Tallies, Plausible);

		act.Should().Throw<Exception>().Which.ToString().Should().Contain("increments").And.Contain("not the same as doing it once");
	}

	[Test]
	public void Idempotency_is_vacuous_when_the_action_never_changes_anything()
	{
		var set = Set(("does-nothing", Constraints<AxisTally>.Idempotent(t => t)));

		var act = () => set.Idempotency(Tallies, Plausible);

		act.Should().Throw<VacuousPropertyException>().Which.Message.Should().Contain("never exercised");
	}

	[Test]
	public void Idempotency_is_skipped_when_the_rule_was_mapped_without_a_way_back()
	{
		var rule = Constraints<AxisTally>.Idempotent(t => t with { Value = Math.Abs(t.Value) }).Map<AxisHolder>(h => h.Tally);
		var set = ConstraintSet.From(("abs", rule));

		var result = set.Idempotency(Gen.Select(Tallies, Gen.String, (t, l) => new AxisHolder(t, l)), Plausible);

		result.Should().BeOfType<AxisResult.Skipped>().Which.Reason.Should().Contain("Map(project, set)");
	}

	// ── 03 Monotonicity ──────────────────────────────────────────────────────────

	[Test]
	public void Monotonicity_passes_when_every_action_keeps_the_value_from_going_backwards_and_one_moves_it()
	{
		var set = Set(("value-grows", Constraints<AxisTally>.Monotonic(t => t.Value, MonotonicDirection.NonDecreasing)));
		var actions = new EntityActions<AxisTally>(Action("Increment", t => t with { Value = t.Value + 1 }), Action("Stay", t => t));

		var result = set.Monotonicity(actions, NonNegative, Plausible);

		result.Should().BeOfType<AxisResult.Passed>().Which.Rules.Should().Equal("value-grows");
	}

	[Test]
	public void Monotonicity_catches_an_action_that_moves_the_value_backwards()
	{
		var set = Set(("value-grows", Constraints<AxisTally>.Monotonic(t => t.Value, MonotonicDirection.NonDecreasing)));
		var actions = new EntityActions<AxisTally>(
			Action("Increment", t => t with { Value = t.Value + 1 }),
			Action("Decrement", t => t with { Value = t.Value - 1 })
		);

		var act = () => set.Monotonicity(actions, NonNegative, Plausible);

		act.Should().Throw<Exception>().Which.ToString().Should().Contain("Decrement").And.Contain("value-grows");
	}

	[Test]
	public void Monotonicity_is_vacuous_when_no_action_ever_moves_the_value()
	{
		var set = Set(("value-grows", Constraints<AxisTally>.Monotonic(t => t.Value, MonotonicDirection.NonDecreasing)));
		var actions = new EntityActions<AxisTally>(Action("Stay", t => t), Action("Touch other field", t => t with { Credits = t.Credits + 1 }));

		var act = () => set.Monotonicity(actions, NonNegative, Plausible);

		act.Should().Throw<VacuousPropertyException>().Which.Message.Should().Contain("no action changed the value");
	}

	[Test]
	public void Monotonicity_is_skipped_without_actions_and_not_applicable_without_a_rule()
	{
		var monotonic = Set(("value-grows", Constraints<AxisTally>.Monotonic(t => t.Value, MonotonicDirection.NonDecreasing)));
		var other = Set(("idempotent", Constraints<AxisTally>.Idempotent(t => t)));

		monotonic.Monotonicity(new EntityActions<AxisTally>(), NonNegative).Should().BeOfType<AxisResult.Skipped>().Which.Reason.Should().Contain("no actions");
		other.Monotonicity(new EntityActions<AxisTally>(Action("Stay", t => t)), NonNegative).Should().BeOfType<AxisResult.NotApplicable>();
	}

	// ── 04 Conservation ──────────────────────────────────────────────────────────

	[Test]
	public void Conservation_passes_when_actions_move_value_between_halves_without_creating_any()
	{
		var set = Set(("total-kept", Constraints<AxisTally>.Conserves(t => t.Credits + t.Debits)));
		var actions = new EntityActions<AxisTally>(
			new EntityAction<AxisTally>("Transfer", typeof(int), (t, a) => t with { Credits = t.Credits - (int)a!, Debits = t.Debits + (int)a! })
		);

		var result = set.Conservation(actions, NonNegative, Plausible);

		result.Should().BeOfType<AxisResult.Passed>().Which.Rules.Should().Equal("total-kept");
	}

	[Test]
	public void Conservation_catches_an_action_that_destroys_value()
	{
		var set = Set(("total-kept", Constraints<AxisTally>.Conserves(t => t.Credits + t.Debits)));
		var actions = new EntityActions<AxisTally>(
			new EntityAction<AxisTally>("Burn", typeof(int), (t, a) => t with { Credits = t.Credits - Math.Max(1, (int)a!) })
		);

		var act = () => set.Conservation(actions, NonNegative, Plausible);

		act.Should().Throw<Exception>().Which.ToString().Should().Contain("Burn").And.Contain("total-kept");
	}

	// ── 08 Confluence ────────────────────────────────────────────────────────────

	[Test]
	public void Confluence_passes_when_the_result_ignores_the_order_and_orderings_were_tried()
	{
		var set = ConstraintSet.From(("sum", Constraints<AxisBag>.OrderIndependent(b => b.Items, (_, items) => new AxisBag(items), b => b.Items.Sum())));

		var result = set.Confluence(Bags, Plausible);

		result.Should().BeOfType<AxisResult.Passed>().Which.Rules.Should().Equal("sum");
	}

	[Test]
	public void Confluence_catches_a_result_that_depends_on_which_item_comes_first()
	{
		var set = ConstraintSet.From(("first", Constraints<AxisBag>.OrderIndependent(b => b.Items, (_, items) => new AxisBag(items), b => b.Items[0])));

		var act = () => set.Confluence(Bags, Plausible);

		act.Should().Throw<Exception>().Which.ToString().Should().Contain("first").And.Contain("depends on the order");
	}

	[Test]
	public void Confluence_is_vacuous_when_no_value_has_two_items_to_reorder()
	{
		var set = ConstraintSet.From(("sum", Constraints<AxisBag>.OrderIndependent(b => b.Items, (_, items) => new AxisBag(items), b => b.Items.Sum())));

		var act = () => set.Confluence(SingleItemBags, Plausible);

		act.Should().Throw<VacuousPropertyException>().Which.Message.Should().Contain("two items to reorder");
	}

	// ── 09 Vacancy ───────────────────────────────────────────────────────────────

	[Test]
	public void Vacancy_passes_when_every_rule_has_a_counter_example_and_every_guard_is_reached()
	{
		var set = Set(
			("non-negative", Constraints<AxisTally>.Always(t => t.Value >= 0, "A value is never negative.")),
			("big-needs-no-debits", Constraints<AxisTally>.When(t => t.Value > 5).Requires(t => t.Debits == 0, "A big value has no debits."))
		);
		var counterExamples = new Dictionary<string, Func<AxisTally, AxisTally>>
		{
			["non-negative"] = t => t with { Value = -1 },
			["big-needs-no-debits"] = t => t with { Value = 9, Debits = 1 }
		};

		var result = set.Vacancy(NoDebitsWhenBig, counterExamples, Plausible);

		result.Should().BeOfType<AxisResult.Passed>().Which.Rules.Should().Equal("non-negative", "big-needs-no-debits");
	}

	[Test]
	public void Vacancy_fails_for_a_rule_nothing_shows_can_fire()
	{
		// only the author knows 12345 is the forbidden value: no single step from a valid value reaches it
		var set = Set(("not-twelve-thousand", Constraints<AxisTally>.Always(t => t.Value != 12_345, "A value is not 12345.")));

		var act = () => set.Vacancy(NonNegative, run: Plausible);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("not-twelve-thousand: no counter-example");
	}

	[Test]
	public void Vacancy_fails_for_a_guard_no_generated_value_reaches()
	{
		var set = Set(("huge-needs-no-debits", Constraints<AxisTally>.When(t => t.Value > 100).Requires(t => t.Debits == 0, "A huge value has no debits.")));
		var counterExamples = new Dictionary<string, Func<AxisTally, AxisTally>> { ["huge-needs-no-debits"] = t => t with { Value = 101, Debits = 1 } };

		var act = () => set.Vacancy(NonNegative, counterExamples, Plausible);

		act.Should().Throw<VacuousPropertyException>().Which.Message.Should().Contain("guard was never true");
	}

	// ── results ──────────────────────────────────────────────────────────────────

	[Test]
	public void A_failed_result_throws_when_asked_and_a_pass_does_not()
	{
		var failed = new AxisResult.Failed(Axis.Idempotency, "boom", "seed-1");
		var passed = new AxisResult.Passed(Axis.Invariant, ["a"], 10);

		var act = () => failed.ThrowIfFailed();

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("boom").And.Contain("seed-1");
		passed.ThrowIfFailed().Should().BeSameAs(passed);
	}
}
