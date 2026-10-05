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

	// The default generator mixes plausible words with edge strings ("a@b..c", "@", ...), so it reaches both valid and invalid addresses
	// on its own, with no Bogus rule written. It does not reach a *realistic* address, which the Bogus rule below does.
	[Test]
	public void Probe_shows_the_default_generator_reaches_both_valid_and_invalid_addresses()
	{
		var entry = Constraints.Probe(throwOnContradiction: false).Entries.Single(e => e.Constraint == "valid-format");

		entry.Held.Should().BeGreaterThan(0);
		entry.Violated.Should().BeGreaterThan(0);
	}

	// ...but a Bogus rule can, and the same constraints are the oracle for it.
	[Test]
	public void Bogus_generated_addresses_satisfy_the_constraints() =>
		GenFakerExtensions.Faker<EmailAddress>(faker => faker.CustomInstantiator(f => new EmailAddress(f.Internet.Email()))).ShouldSatisfy(
			Constraints,
			iter: 200
		);
}
