using AwesomeAssertions;
using FluentValidation;

namespace Nullean.Entities.FluentValidation.Tests;

// Direction 2: an existing FluentValidation validator as a constraint.
public class ValidatorConstraintsTests
{
	private static readonly ConstraintSet<Customer> FromValidator = ConstraintSet.From(
		("customer-validator", Constraints<Customer>.FromValidator(new CustomerValidator()))
	);

	[Test]
	public void A_valid_value_has_no_violations() => FromValidator.Validate(CustomerRules.Valid).Should().BeEmpty();

	[Test]
	public void A_failure_becomes_a_violation_named_after_the_constraint()
	{
		var violation = FromValidator.Validate(CustomerRules.Valid with { Age = 7 }).Should().ContainSingle().Which;

		violation.ConstraintId.Should().Be("customer-validator");
		violation.Member.Should().Be("Age");
		violation.Severity.Should().Be(Severity.Error);
		violation.Evidence.Should().Be(7);
		violation.Message.Should().Be("'Age' must be between 18 and 120. You entered 7.");
	}

	[Test]
	public void Severities_map_back_to_Error_Warning_and_Hint()
	{
		var violations = FromValidator.Validate(CustomerRules.Valid with { Name = "", Email = "nope", Lines = [new OrderLine("A-1", 0)] });

		violations.Should().Contain(v => v.Member == "Name" && v.Severity == Severity.Error);
		violations.Should().Contain(v => v.Member == "Email" && v.Severity == Severity.Warning);
		violations.Should().Contain(v => v.Member == "Lines[0].Quantity" && v.Severity == Severity.Hint);
	}

	[Test]
	public void A_nested_property_path_is_the_member() =>
		FromValidator
			.Validate(CustomerRules.Valid with { Address = new Address("1 Main St", "") })
			.Should()
			.ContainSingle()
			.Which
			.Member
			.Should()
			.Be("Address.City");

	[Test]
	public void RuleForEach_failures_name_the_item() =>
		FromValidator.Validate(CustomerRules.Valid with { Lines = [new OrderLine("A-1", 1), new OrderLine("B-2", -3)] })
			.Should()
			.ContainSingle()
			.Which.Member.Should()
			.Be("Lines[1].Quantity");

	[Test]
	public void The_name_defaults_to_the_validator_type() => new CustomerValidator().ToConstraint().Name.Should().Be(nameof(CustomerValidator));

	[Test]
	public void ToConstraintSet_merges_with_the_rules_you_already_have()
	{
		var merged = ConstraintSet.Merge(CustomerRules.All, new CustomerValidator().ToConstraintSet());

		merged
			.Validate(CustomerRules.Valid with { Email = "nope" })
			.Select(v => v.ConstraintId)
			.Should()
			.BeEquivalentTo(["email-has-at", nameof(CustomerValidator)]);
	}

	[Test]
	public void Only_errors_reject_a_change_in_Apply()
	{
		var warningsOnly = FromValidator.Apply(CustomerRules.Valid, c => c with { Email = "nope" });
		var error = FromValidator.Apply(CustomerRules.Valid, c => c with { Age = 7 });

		warningsOnly.IsOk.Should().BeTrue();
		error.IsFail.Should().BeTrue();
	}

	[Test]
	public void A_round_trip_keeps_member_message_and_severity()
	{
		var original = CustomerRules.All.Validate(CustomerRules.Valid with { Name = "Al", Age = 12, Email = "x" });
		var roundTripped = ConstraintSet.From(("round-trip", Constraints<Customer>.FromValidator(CustomerRules.All.ToValidator())))
			.Validate(CustomerRules.Valid with { Name = "Al", Age = 12, Email = "x" });

		roundTripped.Select(v => (v.Member, v.Message, v.Severity))
			.Should()
			.BeEquivalentTo(original.Select(v => (v.Member ?? v.ConstraintId, v.Message, v.Severity)));
	}

	[Test]
	public void A_validator_with_async_rules_throws_when_checked()
	{
		var validator = new InlineValidator<Customer>();
		_ = validator.RuleFor(c => c.Name).MustAsync((_, _) => Task.FromResult(true));
		var rules = validator.ToConstraintSet("async");

		var act = () => rules.Validate(CustomerRules.Valid);

		_ = act.Should().Throw<AsyncValidatorInvokedSynchronouslyException>();
	}
}
