using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Examples.Finance.BankAccount;

namespace Nullean.Entities.Examples.Tests.Finance;

public class BankAccountTests
{
	private static readonly ConstraintSet<BankAccount> Constraints = BankAccountConstraints.Constraints;

	private static BankAccount Active(decimal balance = 100m) => new("DE89370400440532013000", balance, 500m, AccountStatus.Active);

	[Test]
	public void Validate_valid_active_account()
	{
		Constraints.Validate(Active()).Should().BeEmpty();
	}

	[Test]
	public void Validate_rejects_balance_below_overdraft_limit()
	{
		var bad = Active() with { Balance = -600m };
		Constraints.Validate(bad).Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Applied_on_deposit()
	{
		var channel = ConstraintChannel.For(Active()).Enforce(Constraints);
		var result = channel.Apply(a => a.Deposit(50m));
		result.Should().BeOfType<ChannelResult<BankAccount>.Applied>();
	}

	[Test]
	public void Channel_Failure_on_frozen_status_change()
	{
		var frozen = Active().Freeze();
		var channel = ConstraintChannel.For(frozen).Enforce(Constraints);
		var result = channel.Apply(a => a with { Status = AccountStatus.Closed });
		result.Should().BeOfType<ChannelResult<BankAccount>.Failure>();
	}

	[Test]
	public void Channel_Noop_on_freeze_already_frozen_account()
	{
		var frozen = Active().Freeze();
		var channel = ConstraintChannel.For(frozen).Enforce(Constraints);
		var result = channel.Apply(a => a.Freeze());
		result.Should().BeOfType<ChannelResult<BankAccount>.Noop>();
	}

	[Test]
	public void CsCheck_deposits_always_increase_balance()
	{
		Gen.Decimal[0.01m, 10_000m].Sample(amount =>
		{
			var before = Active(100m);
			var after = before.Deposit(amount);
			after.Balance.Should().BeGreaterThan(before.Balance);
		});
	}
}
