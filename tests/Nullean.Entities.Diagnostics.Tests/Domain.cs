namespace Nullean.Entities.Diagnostics.Tests;

public sealed partial record Account(string Email, decimal Balance)
{
	[Constraint]
	public static readonly Constraint<Account> NeverNegative = Constraints<Account>.Check(
		a => a.Balance >= 0 ? [] : [Violation.Error(nameof(Balance), "must not go below zero") with { Evidence = a.Email }]
	);

	[Constraint]
	public static readonly Constraint<Account> LowBalance = Constraints<Account>.Check(
		a => a.Balance is >= 0 and < 10 ? [Violation.Warning("balance is low") with { Member = nameof(Balance) }] : []
	);
}

public abstract record OrderEvent
{
	public sealed record Confirmed(int Id) : OrderEvent;

	public sealed record Shipped(int Id) : OrderEvent;
}

public static class OrderRules
{
	public static readonly TemporalRuleSet<OrderEvent> All = TemporalRuleSet<OrderEvent>.From(
		("ShipsWithinAMinute",
		Temporal<OrderEvent>.After<OrderEvent.Confirmed>().Then<OrderEvent.Shipped>((c, s) => c.Id == s.Id).Within(TimeSpan.FromMinutes(1)))
	);
}
