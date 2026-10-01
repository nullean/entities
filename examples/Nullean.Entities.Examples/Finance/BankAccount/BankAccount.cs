namespace Nullean.Entities.Examples.Finance.BankAccount;

public enum AccountStatus
{
	Active,
	Frozen,
	Closed
}

public sealed record BankAccount(string Iban, decimal Balance, decimal OverdraftLimit, AccountStatus Status)
{
	public BankAccount Deposit(decimal amount) => this with { Balance = Balance + amount };

	public BankAccount Withdraw(decimal amount) => this with { Balance = Balance - amount };

	public BankAccount Freeze() => this with { Status = AccountStatus.Frozen };
}

[Constraints]
public static partial class BankAccountConstraints
{
	public static readonly Constraint<BankAccount> BalanceAboveLimit = Constraints<BankAccount>.Always(
		a => a.Balance >= -a.OverdraftLimit,
		"Balance cannot exceed the overdraft limit."
	);

	// Deposit only moves the balance upward — step-scoped to Deposit.
	public static readonly Constraint<BankAccount> DepositIsMonotonic = Constraints<BankAccount>.Monotonic(
		a => a.Balance,
		MonotonicDirection.NonDecreasing,
		"Deposit must not decrease the balance."
	);

	// Once frozen, an account cannot change status.
	public static readonly Constraint<BankAccount> FrozenAccountCannotTransition = Constraints<BankAccount>.Transitions(
		(prev, next) => prev.Status != AccountStatus.Frozen || next.Status == AccountStatus.Frozen,
		"A frozen account cannot change status."
	);
}
