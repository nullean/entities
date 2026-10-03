using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.Basics.Money;

namespace Nullean.Entities.Examples.Tests.Basics;

public class MoneyTests
{
	private static readonly ConstraintSet<Money> MoneySet = MoneyConstraints.All;
	private static readonly ConstraintSet<(Money Left, Money Right)> PairSet = MoneyPairConstraints.All;

	[Test]
	public void Validate_positive_amount_passes()
	{
		MoneySet.Validate(new Money(10m, "USD")).Should().BeEmpty();
	}

	[Test]
	public void Validate_negative_amount_fails()
	{
		MoneySet.Validate(new Money(-1m, "USD")).Should().NotBeEmpty();
	}

	[Test]
	public void Split_conserves_total()
	{
		var original = new Money(100m, "USD");
		var (left, right) = MoneyOps.Split(original, 0.3m);
		var violations = PairSet.Validate((left, right));
		violations.Should().BeEmpty();
	}

	[Test]
	public void Bogus_generated_amounts_in_a_currency_satisfy_the_constraints() =>
		GenFakerExtensions
			.Faker<Money>(faker => faker.CustomInstantiator(f => new Money(f.Finance.Amount(0.01m, 1_000_000m), f.Finance.Currency().Code)))
			.ShouldSatisfy(MoneySet, iter: 200);

	// No hand-made generator: the default one mixes in negative amounts and blank currencies on its own.
	[Test]
	public void RejectsInvalid_negative_amounts_and_blank_currencies_are_named_and_rejected() => MoneySet.RejectsInvalid().Unfalsifiable.Should().BeEmpty();
}
