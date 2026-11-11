using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;

namespace Nullean.Entities.Tests;

// [Constraints(ValidatableObject = true)]: the type implements IValidatableObject, so DataAnnotations validation runs its constraints too.
[Constraints(ValidatableObject = true)]
public sealed partial class Registration
{
	[Required]
	public string? Name { get; set; }

	[Range(18, 120)]
	public int Age { get; set; }

	// No attribute: `required` gives it the implicit email-required rule, which only the constraints report.
	public required string Email { get; init; }

	public DateOnly From { get; set; }
	public DateOnly To { get; set; }
	public DateTimeOffset? Deadline { get; set; }

	public static readonly Constraint<Registration> EndsAfterItStarts = Constraints<Registration>.Always(
		r => r,
		r => r.To > r.From,
		"To must be after From",
		member: "To"
	);

	public static readonly Constraint<Registration> UnusualAge = Constraints<Registration>.Always(r => r.Age <= 100, "Check the age").WithSeverity(
		Severity.Warning
	);

	public static readonly Constraint<Registration> DeadlineNotPassed = Constraints<Registration>.Always(
		(r, now) => r.Deadline is null || r.Deadline > now,
		"The deadline has passed"
	);
}

public class ValidatableObjectTests
{
	private static Registration Valid() =>
		new() { Name = "Ada", Age = 36, Email = "ada@example.com", From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 1, 2), };

	private static (bool Valid, List<ValidationResult> Results) TryValidate(object instance, IServiceProvider? services = null)
	{
		var results = new List<ValidationResult>();
		var valid = Validator.TryValidateObject(instance, new ValidationContext(instance, services, null), results, validateAllProperties: true);
		return (valid, results);
	}

	private static string Describe(ValidationResult r) => $"{string.Join(",", r.MemberNames)}: {r.ErrorMessage}";

	[Test]
	public void A_valid_instance_passes()
	{
		TryValidate(Valid()).Valid.Should().BeTrue();
	}

	[Test]
	public void A_broken_constraint_fails_TryValidateObject_keyed_by_its_member()
	{
		var registration = Valid();
		registration.To = registration.From;

		var (valid, results) = TryValidate(registration);

		valid.Should().BeFalse();
		results.Select(Describe).Should().Equal("To: To must be after From");
	}

	[Test]
	public void An_implicit_required_rule_is_reported_because_no_attribute_reports_it()
	{
		var registration = new Registration { Name = "Ada", Age = 36, Email = null!, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 1, 2) };

		TryValidate(registration).Results.Select(Describe).Should().Equal("Email: The Email field is required.");
	}

	// Validator runs IValidatableObject only once every attribute passes (verified on .NET 10 and 11; minimal API AddValidation and MVC do the same,
	// see tests/Nullean.Entities.AspNetCore.Tests/ValidatableObjectEndpointTests.cs). Either way each rule is reported exactly once.
	[Test]
	public void With_an_attribute_broken_too_each_error_is_reported_once()
	{
		var registration = Valid();
		registration.Name = null;
		registration.To = registration.From;

		TryValidate(registration).Results.Select(Describe).Should().Equal("Name: The Name field is required.");

		registration.Name = "Ada";
		TryValidate(registration).Results.Select(Describe).Should().Equal("To: To must be after From");
	}

	[Test]
	public void Validate_leaves_out_the_rules_the_attributes_already_report()
	{
		var registration = Valid();
		registration.Name = null;
		registration.Age = 12;
		registration.To = registration.From;

		// The set itself has all three...
		Registration
			.Constraints
			.Validate(registration)
			.Select(v => v.ConstraintId)
			.Should()
			.BeEquivalentTo("name-required", "age-range", "ends-after-it-starts");
		// ...IValidatableObject reports only the one no attribute reports.
		((IValidatableObject)registration).Validate(new ValidationContext(registration)).Select(Describe).Should().Equal("To: To must be after From");
	}

	[Test]
	public void Warnings_are_not_validation_errors()
	{
		var registration = Valid();
		registration.Age = 110;

		Registration.Constraints.Validate(registration).Should().ContainSingle(v => v.Severity == Severity.Warning);
		TryValidate(registration).Valid.Should().BeTrue();
	}

	[Test]
	public void The_clock_comes_from_the_validation_context_services_and_a_rule_without_a_member_has_no_member_names()
	{
		var registration = Valid();
		registration.Deadline = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

		var before = TryValidate(registration, new Services(new ManualTimeProvider(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero))));
		before.Valid.Should().BeTrue();

		var after = TryValidate(registration, new Services(new ManualTimeProvider(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero))));
		after.Results.Should().ContainSingle().Which.MemberNames.Should().BeEmpty();
		after.Results[0].ErrorMessage.Should().Be("The deadline has passed");
	}

	private sealed class Services(TimeProvider time) : IServiceProvider
	{
		public object? GetService(Type serviceType) => serviceType == typeof(TimeProvider) ? time : null;
	}
}
