using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.Basics.EmailAddress;

namespace Nullean.Entities.Examples.Tests.Basics;

public class EmailAddressTests
{
	private static readonly ConstraintSet<EmailAddress> Constraints = EmailConstraints.All;

	[Test]
	public void Validate_valid_email()
	{
		Constraints.Validate(new EmailAddress("user@example.com")).Should().BeEmpty();
	}

	[Test]
	public void Validate_rejects_empty()
	{
		Constraints.Validate(new EmailAddress("")).Should().NotBeEmpty();
	}

	[Test]
	public void Validate_rejects_no_at_sign()
	{
		Constraints.Validate(new EmailAddress("notanemail")).Should().NotBeEmpty();
	}

	[Test]
	public void RejectsInvalid_malformed_addresses_are_named_by_Validate() => Constraints.RejectsInvalid();

	// AutoBogus makes arbitrary words, never an address, so it cannot build a valid one on its own...
	[Test]
	public void AutoBogus_alone_never_builds_a_valid_address() => Constraints.Probe(throwOnContradiction: false).NeverHeld.Should().Contain("ValidFormat");

	// ...but a Bogus rule can, and the same constraints are the oracle for it.
	[Test]
	public void Bogus_generated_addresses_satisfy_the_constraints() =>
		GenFakerExtensions.Faker<EmailAddress>(faker => faker.CustomInstantiator(f => new EmailAddress(f.Internet.Email()))).ShouldSatisfy(
			Constraints,
			iter: 200
		);
}
