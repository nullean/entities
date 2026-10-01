using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Examples.Basics.EmailAddress;

namespace Nullean.Entities.Examples.Tests.Basics;

public class EmailAddressTests
{
	private static readonly ConstraintSet<EmailAddress> Constraints = EmailConstraints.Constraints;

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
	public void CsCheck_strings_without_at_always_fail()
	{
		Gen
			.String
			.Where(s => !s.Contains('@'))
			.Sample(s =>
			{
				if (string.IsNullOrWhiteSpace(s))
				{
					return;
				}
				Constraints.Validate(new EmailAddress(s)).Should().NotBeEmpty();
			});
	}
}
