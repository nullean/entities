using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Examples.Basics.Money;

namespace Nullean.Entities.Examples.Tests.Basics;

public class MoneyTests
{
	private static readonly ConstraintSet<Money> MoneySet = MoneyConstraints.Constraints;
	private static readonly ConstraintSet<(Money Left, Money Right)> PairSet = MoneyPairConstraints.Constraints;

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
	public void CsCheck_positive_amounts_always_pass()
	{
		Gen.Decimal[0.01m, 1_000_000m].Sample(amount =>
		{
			MoneySet.Validate(new Money(amount, "EUR")).Should().BeEmpty();
		});
	}
}
