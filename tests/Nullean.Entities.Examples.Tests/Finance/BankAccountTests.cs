using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.Finance.BankAccount;

namespace Nullean.Entities.Examples.Tests.Finance;

public class BankAccountTests
{
	private static readonly ConstraintSet<BankAccount> Constraints = BankAccountConstraints.All;

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
		var channel = ConstrainedValue.For(Active()).Enforce(Constraints);
		var result = channel.ApplyDetailed(a => a.Deposit(50m));
		result.Should().BeOfType<ApplyOutcome<BankAccount>.Applied>();
	}

	[Test]
	public void Channel_Failure_on_frozen_status_change()
	{
		var frozen = Active().Freeze();
		var channel = ConstrainedValue.For(frozen).Enforce(Constraints);
		var result = channel.ApplyDetailed(a => a with { Status = AccountStatus.Closed });
		result.Should().BeOfType<ApplyOutcome<BankAccount>.Failure>();
	}

	[Test]
	public void Channel_Noop_on_freeze_already_frozen_account()
	{
		var frozen = Active().Freeze();
		var channel = ConstrainedValue.For(frozen).Enforce(Constraints);
		var result = channel.ApplyDetailed(a => a.Freeze());
		result.Should().BeOfType<ApplyOutcome<BankAccount>.Noop>();
	}

	[Test]
	// Plausible only: at ~7.9e28 a decimal cannot represent balance + 0.01, so a deposit has no effect (see AUTOTEST_NOTES.md).
	public void ForAll_depositing_a_positive_amount_increases_the_balance() =>
		Constraints.ForAll(a => a.Deposit(0.01m).Balance > a.Balance, GenAuto.Of<BankAccount>(AutoMode.Plausible), iter: 200);

	// Withdraw() never checks the overdraft limit, and DepositIsMonotonic is in the general set (so Withdraw is always
	// rejected through Apply). Both are in AUTOTEST_NOTES.md. Enforced models the Apply boundary; Key keeps the space finite.
	[Test]
	public void AutoTest_enforced_accounts_never_breach_the_overdraft_limit() =>
		BankAccountConstraints
			.Instance
			.Spec(Active())
			.Domain(50m, 500m)
			.Key(a => (a.Status, a.Balance))
			.Boundary(a => Math.Abs(a.Balance) < 1_000m)
			.Enforced()
			.Exhaustive();

	[Test]
	public void AutoTest_raw_actions_are_found_to_rely_on_Apply_to_reject_bad_amounts()
	{
		var act = () => Constraints.Preserves(BankAccountConstraints.Actions, iter: 500);

		act.Should().Throw<Exception>().Which.Message.Should().MatchRegex("Withdraw|Deposit");
	}
}
