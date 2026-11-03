using AwesomeAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using FvSeverity = FluentValidation.Severity;

namespace Nullean.Entities.FluentValidation.Tests;

// Direction 1: constraints inside FluentValidation. The snippets on docs/integration/fluentvalidation.md come from here.
public class ConstraintValidatorTests
{
	private static ValidationFailure Single(ValidationResult result, string errorCode) =>
		result.Errors.Should().ContainSingle(f => f.ErrorCode == errorCode).Which;

	[Test]
	public void A_valid_value_has_no_failures() => CustomerRules.All.ToValidator().Validate(CustomerRules.Valid).IsValid.Should().BeTrue();

	[Test]
	public void A_violation_becomes_a_failure_keyed_by_its_member_with_the_rule_id_as_error_code()
	{
		var result = CustomerRules.All.ToValidator().Validate(CustomerRules.Valid with { Email = "nope" });

		var failure = Single(result, "email-has-at");
		failure.PropertyName.Should().Be("Email");
		failure.ErrorMessage.Should().Be("must contain @");
		failure.Severity.Should().Be(FvSeverity.Error);
		failure.AttemptedValue.Should().Be("nope");
		failure.CustomState.Should().BeOfType<Violation>().Which.ConstraintId.Should().Be("email-has-at");
		result.IsValid.Should().BeFalse();
	}

	[Test]
	public void Severities_map_to_Error_Warning_and_Info()
	{
		var result = CustomerRules.All.ToValidator().Validate(CustomerRules.Valid with { Name = "Al", Age = 12, Email = "x" });

		Single(result, "email-has-at").Severity.Should().Be(FvSeverity.Error);
		Single(result, "adult").Severity.Should().Be(FvSeverity.Warning);
		Single(result, "short-name").Severity.Should().Be(FvSeverity.Info);
	}

	[Test]
	public void A_violation_without_a_member_is_keyed_by_its_rule_id_at_the_root() =>
		Single(CustomerRules.All.ToValidator().Validate(CustomerRules.Valid with { Name = "Al" }), "short-name").PropertyName.Should().Be("short-name");

	[Test]
	public void A_nested_member_path_is_kept() =>
		Single(CustomerRules.All.ToValidator().Validate(CustomerRules.Valid with { Address = new Address("1 Main St", "") }), "city-known")
			.PropertyName.Should()
			.Be("Address.City");

	[Test]
	public void SatisfiesConstraints_on_a_property_prefixes_the_member_with_the_property_path()
	{
		var validator = new InlineValidator<Customer>();
		_ = validator.RuleFor(c => c.Address).SatisfiesConstraints(AddressRules.All);

		var result = validator.Validate(CustomerRules.Valid with { Address = new Address("1 Main St", " ") });

		Single(result, "city-required").PropertyName.Should().Be("Address.City");
	}

	[Test]
	public void A_constraint_validator_as_a_child_validator_gets_the_parent_path()
	{
		var validator = new InlineValidator<Customer>();
		_ = validator.RuleFor(c => c.Address!).SetValidator(AddressRules.All.ToValidator());

		var result = validator.Validate(CustomerRules.Valid with { Address = new Address("1 Main St", "") });

		Single(result, "city-required").PropertyName.Should().Be("Address.City");
	}

	[Test]
	public void A_null_property_is_skipped()
	{
		var validator = new InlineValidator<Customer>();
		_ = validator.RuleFor(c => c.Address).SatisfiesConstraints(AddressRules.All);

		validator.Validate(CustomerRules.Valid with { Address = null }).IsValid.Should().BeTrue();
	}

	[Test]
	public void RuleForEach_names_the_item_that_broke_the_rule()
	{
		var validator = new InlineValidator<Customer>();
		_ = validator.RuleForEach(c => c.Lines).SatisfiesConstraints(LineRules.All);

		var result = validator.Validate(CustomerRules.Valid with { Lines = [new OrderLine("A-1", 1), new OrderLine("B-2", 0)] });

		var failure = Single(result, "quantity-positive");
		failure.PropertyName.Should().Be("Lines[1].Quantity");
		failure.AttemptedValue.Should().Be(0);
	}

	[Test]
	public void Include_adds_the_constraints_to_an_existing_validator()
	{
		var result = new SignUpValidator().Validate(CustomerRules.Valid with { Name = "", Email = "nope" });

		result.Errors.Select(f => f.PropertyName).Should().Contain(["Name", "Email"]);
		Single(result, "email-has-at").ErrorMessage.Should().Be("must contain @");
	}

	[Test]
	public void A_subclass_adds_FluentValidation_rules_next_to_the_constraints()
	{
		var result = new CustomerConstraintValidator().Validate(CustomerRules.Valid with { Email = "nope", Lines = [] });

		result.Errors.Select(f => f.ErrorCode).Should().Contain(["email-has-at", "NotEmptyValidator"]);
	}

	[Test]
	public void A_message_with_braces_is_not_treated_as_a_placeholder()
	{
		var rules = ConstraintSet.From(("braces", Constraints<Address>.Check(_ => [Violation.Error("{PropertyName} stays as written")])));

		rules.ToValidator().Validate(new Address("a", "b")).Errors.Single().ErrorMessage.Should().Be("{PropertyName} stays as written");
	}

	[Test]
	public async Task ValidateAndThrowAsync_throws_FluentValidations_exception()
	{
		IValidator<Customer> validator = CustomerRules.All.ToValidator();

		var act = () => ((AbstractValidator<Customer>)validator).ValidateAndThrowAsync(CustomerRules.Valid with { Email = "nope" });

		var thrown = await act.Should().ThrowAsync<ValidationException>();
		thrown.Which.Errors.Should().ContainSingle(f => f.ErrorCode == "email-has-at");
	}

	[Test]
	public async Task A_MediatR_style_pipeline_that_calls_ValidateAsync_sees_the_violations()
	{
		var services = new ServiceCollection()
			.AddSingleton<IValidator<Customer>>(CustomerRules.All.ToValidator())
			.AddSingleton<IValidator<Customer>, CustomerValidator>()
			.BuildServiceProvider();

		var failures = await ValidationBehavior.Failures(services.GetServices<IValidator<Customer>>(), CustomerRules.Valid with { Email = "nope", Age = 12 });

		failures.Should().Contain(f => f.ErrorCode == "email-has-at" && f.PropertyName == "Email");
		failures.Should().Contain(f => f.ErrorCode == "adult" && f.Severity == FvSeverity.Warning);
		failures.Should().Contain(f => f.ErrorCode == "InclusiveBetweenValidator" && f.PropertyName == "Age");
	}
}

public sealed class SignUpValidator : AbstractValidator<Customer>
{
	public SignUpValidator()
	{
		_ = RuleFor(c => c.Name).NotEmpty();
		Include(CustomerRules.All.ToValidator());
	}
}

public sealed class CustomerConstraintValidator : ConstraintValidator<Customer>
{
	public CustomerConstraintValidator() : base(CustomerRules.All) => _ = RuleFor(c => c.Lines).NotEmpty();
}

// The shape of a MediatR pipeline behaviour (or any pipeline that resolves IEnumerable<IValidator<T>>): every validator, asynchronously.
internal static class ValidationBehavior
{
	public static async Task<IReadOnlyList<ValidationFailure>> Failures<TRequest>(
		IEnumerable<IValidator<TRequest>> validators,
		TRequest request,
		CancellationToken ct = default
	)
	{
		var context = new ValidationContext<TRequest>(request);
		var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct)));
		return [.. results.SelectMany(r => r.Errors)];
	}
}
