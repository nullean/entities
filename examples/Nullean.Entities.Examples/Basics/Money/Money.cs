namespace Nullean.Entities.Examples.Basics.Money;

public sealed record Money(decimal Amount, string Currency);

public static class MoneyOps
{
	public static Money Add(Money a, Money b) =>
		// Currency mismatch is a no-op — the constraint below catches and rejects it.
		a.Currency == b.Currency ? a with { Amount = a.Amount + b.Amount } : a;

	public static (Money Left, Money Right) Split(Money m, decimal leftAmount) => (m with { Amount = leftAmount }, m with { Amount = m.Amount - leftAmount });
}

[Constraints]
public static partial class MoneyConstraints
{
	public static readonly Constraint<Money> NonNegative = Constraints<Money>.Always(m => m.Amount >= 0, "Money amount cannot be negative.");

	public static readonly Constraint<Money> CurrencyNotEmpty = Constraints<Money>.Always(
		m => !string.IsNullOrWhiteSpace(m.Currency),
		"Currency code cannot be empty."
	);
}

// A split conserves the total — this is a pair-valued, sequential constraint.
[Constraints]
public static partial class MoneyPairConstraints
{
	public static readonly Constraint<(Money Left, Money Right)> SplitConservesTotal = Nullean
		.Entities
		.Constraints<(Money Left, Money Right)>
		.Conserves(p => p.Left.Amount + p.Right.Amount, "A money split must not create or destroy value.");
}
