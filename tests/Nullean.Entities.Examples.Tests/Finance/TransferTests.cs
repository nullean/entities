using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.Finance.Transfer;

namespace Nullean.Entities.Examples.Tests.Finance;

public class TransferTests
{
	private static readonly ConstraintSet<(Account From, Account To)> Constraints = TransferConstraints.All;

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

	// The pair-valued case: no generated Actions (Move is a static function), so the action is declared by hand.
	[Test]
	public void AutoTest_every_reachable_transfer_conserves_the_total_between_two_distinct_accounts() =>
		Constraints
			.Spec((From: new Account("DE01", 200m), To: new Account("DE02", 100m)))
			.Action("Move", [10m, 50m, 500m], Transfers.Move)
			.Boundary(pair => Math.Abs(pair.From.Balance) < 2_000m && Math.Abs(pair.To.Balance) < 2_000m)
			.Exhaustive();
}
