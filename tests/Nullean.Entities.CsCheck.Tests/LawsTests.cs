using AwesomeAssertions;
using Nullean.Entities;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

[Constraints]
internal sealed partial record Annotated(
	[property: System.ComponentModel.DataAnnotations.Required] string Name,
	[property: System.ComponentModel.DataAnnotations.StringLength(10, MinimumLength = 3)] string Handle,
	[property: System.ComponentModel.DataAnnotations.Range(18, 120)] int Age
);

internal sealed record Stocked(string Sku, int Quantity, int Reserved);

public class LawsTests
{
	private static readonly Stocked Valid = new("SKU-1", 10, 2);

	private static ConstraintSet<Stocked> Good() =>
		ConstraintSet.From(
			("SkuPresent", Constraints<Stocked>.Always(s => !string.IsNullOrWhiteSpace(s.Sku))),
			("QuantityNotNegative", Constraints<Stocked>.Always(s => s.Quantity >= 0)),
			("ReservedNotNegative", Constraints<Stocked>.Always(s => s.Reserved >= 0))
		);

	// A negative quantity also breaks the second rule, so QuantityNotNegative has no counter-example of its own.
	private static ConstraintSet<Stocked> Overlapping() =>
		ConstraintSet.From(
			("QuantityNotNegative", Constraints<Stocked>.Always(s => s.Quantity >= 0)),
			("ReservedWithinQuantity", Constraints<Stocked>.Always(s => s.Reserved <= s.Quantity))
		);

	[Test]
	public void A_sound_set_passes_with_a_counter_example_generated_for_each_rule()
	{
		var report = Good().Laws(Valid);

		report.Results.Select(r => r.Constraint).Should().BeEquivalentTo(["SkuPresent", "QuantityNotNegative", "ReservedNotNegative"]);
		report.Results.Single(r => r.Constraint == "SkuPresent").CounterExample.Should().StartWith("generated: Sku");
		report.Results.Single(r => r.Constraint == "QuantityNotNegative").CounterExample.Should().StartWith("generated: Quantity");
	}

	[Test]
	public void A_rule_nothing_can_break_on_its_own_fails_unless_counter_examples_are_optional_or_given()
	{
		var act = () => Overlapping().Laws(Valid);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("QuantityNotNegative: no counter-example");
		Overlapping().Laws(Valid, requireCounterExamples: false).Results.Should().NotBeEmpty();
	}

	[Test]
	public void An_invalid_valid_value_is_reported()
	{
		var act = () => Good().Laws(Valid with { Quantity = -1 }, requireCounterExamples: false);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("The valid value is not valid").And.Contain("QuantityNotNegative");
	}

	[Test]
	public void Duplicate_names_are_reported()
	{
		var rule = Constraints<Stocked>.Always(s => s.Quantity >= 0);
		var other = Constraints<Stocked>.Always(s => s.Quantity < 1000);
		var set = ConstraintSet.From(("Quantity", rule), ("Quantity", other));

		var act = () => set.Laws(Valid, requireCounterExamples: false);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("'Quantity' is used by 2 constraints");
	}

	[Test]
	public void A_counter_example_that_breaks_a_second_rule_is_reported_as_ambiguous()
	{
		var examples = new Dictionary<string, Func<Stocked, Stocked>> { ["QuantityNotNegative"] = s => s with { Quantity = -1 }, };

		var act = () => Overlapping().Laws(Valid, examples, requireCounterExamples: false);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("QuantityNotNegative").And.Contain("also breaks ReservedWithinQuantity");
	}

	[Test]
	public void A_counter_example_that_does_not_break_its_rule_is_reported()
	{
		var examples = new Dictionary<string, Func<Stocked, Stocked>> { ["QuantityNotNegative"] = s => s with { Quantity = 5 }, };

		var act = () => Good().Laws(Valid, examples, requireCounterExamples: false);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("QuantityNotNegative").And.Contain("does not break it");
	}

	[Test]
	public void A_counter_example_for_an_unknown_constraint_is_reported()
	{
		var examples = new Dictionary<string, Func<Stocked, Stocked>> { ["Typo"] = s => s };

		var act = () => Good().Laws(Valid, examples, requireCounterExamples: false);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("'Typo', which is not a constraint");
	}

	[Test]
	public void Sequential_rules_take_a_counter_example_that_is_the_bad_change()
	{
		var set = ConstraintSet.From(("QuantityNeverShrinks", Constraints<Stocked>.Monotonic(s => s.Quantity, MonotonicDirection.NonDecreasing)));
		var examples = new Dictionary<string, Func<Stocked, Stocked>> { ["QuantityNeverShrinks"] = s => s with { Quantity = s.Quantity - 1 } };

		set.Laws(Valid, examples).Results.Should().ContainSingle();
	}

	[Test]
	public void DataAnnotations_become_rules_that_pass_the_laws_with_generated_counter_examples()
	{
		var report = Annotated.Constraints.Laws(new Annotated("Ada", "adal", 30));

		report.Results.Select(r => r.Constraint).Should().BeEquivalentTo(["NameRequired", "HandleStringLength", "AgeRange"]);
		report.Results.Should().OnlyContain(r => r.CounterExample.StartsWith("generated:", StringComparison.Ordinal));
	}

	[Test]
	public void Generated_data_breaks_every_annotation_without_a_hand_made_generator() => Annotated.Constraints.Probe().NeverViolated.Should().BeEmpty();
}
