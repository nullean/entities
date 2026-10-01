using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Examples.Finance.Transfer;

namespace Nullean.Entities.Examples.Tests.Finance;

public class TransferTests
{
	private static readonly ConstraintSet<(Account From, Account To)> Constraints = TransferConstraints.Constraints;

	[Test]
	public void Validate_legal_transfer_conserves_total()
	{
		var from = new Account("DE01", 200m);
		var to = new Account("DE02", 50m);
		var result = Transfers.Move((from, to), 100m);
		Constraints.Validate(result).Should().BeEmpty();
	}

	[Test]
	public void Validate_rejects_same_account_transfer()
	{
		var acc = new Account("DE01", 200m);
		Constraints.Validate((acc, acc)).Should().NotBeEmpty();
	}

	[Test]
	public void CsCheck_move_always_conserves_total()
	{
		// Gen.Decimal[min,max] uses an internal Where filter that can exhaust CsCheck's skip budget.
		// Cast from int for exact, rejection-free generation.
		var genBal = Gen.Int[10, 500].Select(v => (decimal)v);
		var genAmount = Gen.Int[0, 50].Select(v => (decimal)v);
		Gen.Select(genBal, genAmount, (bal, amount) => (bal, amount)).Sample(t =>
		{
			var (bal, amount) = t;
			var from = new Account("DE01", bal);
			var to = new Account("DE02", 100m);
			var (newFrom, newTo) = Transfers.Move((from, to), amount);
			(newFrom.Balance + newTo.Balance).Should().Be(from.Balance + to.Balance);
		});
	}
}
