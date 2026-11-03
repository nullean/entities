using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.FluentValidation.Tests;

// The reason to turn a validator into a constraint: generated data attacks it. Nullean.Entities.CsCheck is a test-only reference.
public class ProbeTests
{
	[Test]
	public void Probe_breaks_and_satisfies_a_FluentValidation_validator()
	{
		var rules = new CustomerValidator().ToConstraintSet();

		var entry = rules.Probe(samples: 500).Entries.Should().ContainSingle().Which;

		entry.Constraint.Should().Be(nameof(CustomerValidator));
		entry.Violated.Should().BeGreaterThan(0);
		entry.Held.Should().BeGreaterThan(0);
	}

	[Test]
	public void A_generated_value_that_breaks_the_validator_is_found_shrunk_and_reported()
	{
		var rules = new CustomerValidator().ToConstraintSet();

		var act = () => GenAuto.Of<Customer>().ShouldSatisfy(rules, iter: 500);

		// For example: Customer { ..., Email = "-", Age = 1, ... } violates: CustomerValidator: 'Email' is not a valid email address.; CustomerValidator: 'Age' must be between 18 and 120. You entered 1.
		var thrown = act.Should().Throw<CsCheckException>().Which;
		thrown.Message.Should().Contain("CsCheck_Seed=");
		thrown.InnerException!.Message.Should().Contain($"violates: {nameof(CustomerValidator)}: '");
	}
}
