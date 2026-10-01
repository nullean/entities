namespace Nullean.Entities.Examples.Finance.Transfer;

public sealed record Account(string Iban, decimal Balance);

public static class Transfers
{
	public static (Account From, Account To) Move((Account From, Account To) p, decimal amount) =>
		(p.From with { Balance = p.From.Balance - amount }, p.To with { Balance = p.To.Balance + amount });
}

[Constraints]
public static partial class TransferConstraints
{
	public static readonly Constraint<(Account From, Account To)> TotalIsConserved = Nullean
		.Entities
		.Constraints<(Account From, Account To)>
		.Conserves(p => p.From.Balance + p.To.Balance, "A transfer must not create or destroy money.");

	public static readonly Constraint<(Account From, Account To)> DistinctAccounts = Nullean
		.Entities
		.Constraints<(Account From, Account To)>
		.Always(p => p.From.Iban != p.To.Iban, "Cannot transfer to the same account.");
}
