namespace Nullean.Entities.AotSmoke;

public sealed class AccountOptions
{
	public decimal Balance { get; set; }

	public static readonly ConstraintSet<AccountOptions> Rules = ConstraintSet.From(
		("NonNegative", Constraints<AccountOptions>.Check(o => o.Balance >= 0 ? [] : [Violation.Error(nameof(Balance), "must not be negative")]))
	);
}
