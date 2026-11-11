using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.CsCheck.AutoBogus;

namespace Nullean.Entities.CsCheck.AutoBogus.Tests;

internal sealed record Address(string Street, string City, string PostCode);

internal sealed record BankDetails(string Iban, string Bic);

internal sealed record Customer(string Name, int LoyaltyPoints, Address Address, BankDetails Bank);

// The example on docs/integration/autobogus.md: GenAuto builds the shape and the edge cases, Bogus supplies the parts that must look real.
public class RicherGenerationTests
{
	private static Gen<Customer> Customers() =>
		GenAuto.Of<Customer>(
			configure: o => o.Use(
				GenFakerExtensions.Faker<Address>(f => f.CustomInstantiator(x => new Address(x.Address.StreetAddress(), x.Address.City(), x.Address.ZipCode())))
			).Use(GenAutoBogus.Of<BankDetails>())
		);

	private static List<Customer> Draw(Gen<Customer> gen, int count = 500) =>
		[.. Enumerable.Range(0, count).Select(i => gen.Generate(new PCG(7, (ulong)i + 1), null, out _))];

	[Test]
	public void Addresses_and_bank_details_come_from_Bogus()
	{
		var customers = Draw(Customers());

		customers.Should().OnlyContain(c => c.Address != null && !string.IsNullOrWhiteSpace(c.Address.Street) && !string.IsNullOrWhiteSpace(c.Address.City));
		customers.Should().OnlyContain(c => c.Bank != null && !string.IsNullOrWhiteSpace(c.Bank.Iban));
	}

	[Test]
	public void The_rest_of_the_value_keeps_its_edge_cases()
	{
		var customers = Draw(Customers());

		customers.Should().Contain(c => string.IsNullOrWhiteSpace(c.Name));
		customers.Should().Contain(c => c.LoyaltyPoints < 0);
	}

	[Test]
	public void A_seed_replays_the_same_customer() => Draw(Customers(), 100).Should().Equal(Draw(Customers(), 100));
}
