namespace Nullean.Entities.Quickstart;

// Two accounts. Money moves between them; none is created or destroyed.
public sealed record Ledger(int Checking, int Savings)
{
	public int Total => Checking + Savings;

	public Ledger MoveToSavings(int amount) => amount > 0 && amount <= Checking ? this with { Checking = Checking - amount, Savings = Savings + amount } : this;

	public Ledger MoveToChecking(int amount) => amount > 0 && amount <= Savings ? this with { Checking = Checking + amount, Savings = Savings - amount } : this;
}

[Constraints]
public static partial class LedgerConstraints
{
	public static readonly Constraint<Ledger> NoOverdraft = Constraints<Ledger>.Always(l => l.Checking >= 0 && l.Savings >= 0, "Neither account is overdrawn.");

	// across a step: the total is the same before and after
	public static readonly Constraint<Ledger> MoneyIsConserved = Constraints<Ledger>.Conserves(l => l.Total);
}
