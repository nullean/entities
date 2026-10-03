using Nullean.Entities;

namespace Banking;

public sealed record Account(Iban Number, string Currency, decimal Balance, decimal OverdraftLimit)
{
	public Account Deposit(Money money) => this with { Balance = Balance + money.Amount };

	public Account Withdraw(Money money) => this with { Balance = Balance - money.Amount };
}

/// <summary>Rules about one account. <c>[Constraints]</c> collects the static fields into <c>AccountConstraints.All</c>.</summary>
[Constraints]
public static partial class AccountConstraints
{
	public static readonly Constraint<Account> StaysWithinOverdraft = Constraints<Account>.Always(
		a => a.Balance >= -a.OverdraftLimit,
		"The balance would go below the account's overdraft limit"
	);

	public static readonly Constraint<Account> OverdraftIsNotNegative = Constraints<Account>.Always(
		a => a.OverdraftLimit >= 0,
		"An overdraft limit cannot be negative"
	);
}

/// <summary>A transfer in flight: both accounts and the money that moves between them. The aggregate a transfer's rules are about.</summary>
public sealed record Transfer(Account From, Account To, Money Amount)
{
	public Transfer Execute() => this with { From = From.Withdraw(Amount), To = To.Deposit(Amount) };
}

/// <summary>
/// The rules of a transfer: each account's own rules, applied to both halves, plus the ones that only exist across them.
/// <c>Conserves</c> is the one a bank cannot do without: whatever a transfer does, the two balances still add up to what they did.
/// </summary>
public static class TransferRules
{
	public static readonly ConstraintSet<Transfer> All = ConstraintSet.Merge(
		AccountConstraints.All.Map<Transfer>(t => t.From),
		AccountConstraints.All.Map<Transfer>(t => t.To),
		ConstraintSet.From(
			("money-is-conserved", Constraints<Transfer>.Conserves(t => t.From.Balance + t.To.Balance, "A transfer cannot create or destroy money")),
			("accounts-differ", Constraints<Transfer>.Always(t => t.From.Number != t.To.Number, "An account cannot transfer to itself")),
			("currencies-match",
			Constraints<Transfer>.Always(
				t => t.From.Currency == t.Amount.Currency && t.To.Currency == t.Amount.Currency,
				"Both accounts and the amount must be in the same currency"
			))
		)
	);
}
