using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// An account balance as the model, and two shared implementations: one that locks around its read-modify-write, one that does not.

internal sealed record LedgerAccount(int Balance)
{
	public LedgerAccount Deposit(int amount) => this with { Balance = Balance + amount };

	public LedgerAccount Withdraw(int amount) => amount <= Balance ? this with { Balance = Balance - amount } : this;
}

internal interface ISharedAccount
{
	int Balance { get; }

	void Deposit(int amount);

	void Withdraw(int amount);
}

internal sealed class LockedAccount : ISharedAccount
{
	private readonly Lock _gate = new();
	private int _balance;

	public int Balance
	{
		get
		{
			lock (_gate)
			{
				return _balance;
			}
		}
	}

	public void Deposit(int amount)
	{
		lock (_gate)
		{
			_balance += amount;
		}
	}

	public void Withdraw(int amount)
	{
		lock (_gate)
		{
			if (amount <= _balance)
			{
				_balance -= amount;
			}
		}
	}
}

// Reads, waits, writes: two deposits at once can both read the old balance, and one of them is lost.
internal sealed class RacyAccount : ISharedAccount
{
	private int _balance;

	public int Balance => Volatile.Read(ref _balance);

	public void Deposit(int amount)
	{
		var read = Volatile.Read(ref _balance);
		Thread.Sleep(1);
		Volatile.Write(ref _balance, read + amount);
	}

	public void Withdraw(int amount)
	{
		var read = Volatile.Read(ref _balance);
		Thread.Sleep(1);
		if (amount <= read)
		{
			Volatile.Write(ref _balance, read - amount);
		}
	}
}

public class LinearizableTests
{
	private static EntitySpec<LedgerAccount> Accounts() =>
		ConstraintSet
			.From(("never-negative", Constraints<LedgerAccount>.Always(a => a.Balance >= 0, "A balance is never negative.")))
			.Spec(new LedgerAccount(0))
			.Action("Deposit", [1, 5], (a, amount) => a.Deposit(amount))
			.Action("Withdraw", [1, 5], (a, amount) => a.Withdraw(amount));

	private static void Apply(ISharedAccount account, string action, object? amount)
	{
		switch (action)
		{
			case "Deposit":
				account.Deposit((int)amount!);
				break;
			case "Withdraw":
				account.Withdraw((int)amount!);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(action), action, null);
		}
	}

	[Test]
	public void An_account_that_locks_is_linearizable() =>
		Accounts().Linearizable<ISharedAccount>(() => new LockedAccount(), Apply, (account, model) => account.Balance == model.Balance, iter: 100);

	[Test]
	public void An_account_that_reads_then_writes_loses_updates_under_concurrent_calls()
	{
		var act =
			() => Accounts().Linearizable<ISharedAccount>(
				() => new RacyAccount(),
				Apply,
				(account, model) => account.Balance == model.Balance,
				maxSequential: 2,
				iter: 200
			);

		var message = act.Should().Throw<CsCheckException>().Which.Message;
		message.Should().Contain("Set seed");
		message.Should().Contain("Deposit");
	}

	[Test]
	public void A_spec_with_no_actions_has_nothing_to_run()
	{
		var act =
			() => ConstraintSet.From<LedgerAccount>().Spec(new LedgerAccount(0)).Linearizable<ISharedAccount>(() => new LockedAccount(), Apply, (_, _) => true);

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("No actions");
	}
}
