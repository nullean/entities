namespace Nullean.Entities.Examples.SaaS.Subscription;

public enum SubscriptionStatus
{
	Trial,
	Active,
	PastDue,
	Cancelled,
	Expired
}

public enum Plan
{
	Free,
	Starter,
	Pro,
	Enterprise
}

public sealed record BillingPeriod(DateOnly Start, DateOnly End);

public sealed record Subscription(
	Guid Id,
	string CustomerId,
	Plan Plan,
	SubscriptionStatus Status,
	DateOnly TrialEndsOn,
	BillingPeriod? CurrentPeriod,
	int SeatCount
)
{
	public Subscription Activate(BillingPeriod period) => this with { Status = SubscriptionStatus.Active, CurrentPeriod = period };

	public Subscription MarkPastDue() => this with { Status = SubscriptionStatus.PastDue };

	public Subscription Cancel() => this with { Status = SubscriptionStatus.Cancelled, CurrentPeriod = null };

	public Subscription Expire() => this with { Status = SubscriptionStatus.Expired };

	public Subscription ChangePlan(Plan plan) => this with { Plan = plan };

	public Subscription AddSeats(int n) => this with { SeatCount = SeatCount + n };
}

[Constraints]
public static partial class SubscriptionConstraints
{
	public static readonly Constraint<Subscription> LegalStatusTransitions = Constraints<Subscription>
		.Transitions(s => s.Status)
		.Allow(SubscriptionStatus.Trial, SubscriptionStatus.Active)
		.Allow(SubscriptionStatus.Trial, SubscriptionStatus.Cancelled)
		.Allow(SubscriptionStatus.Active, SubscriptionStatus.PastDue)
		.Allow(SubscriptionStatus.Active, SubscriptionStatus.Cancelled)
		.Allow(SubscriptionStatus.PastDue, SubscriptionStatus.Active)
		.Allow(SubscriptionStatus.PastDue, SubscriptionStatus.Cancelled)
		.Allow(SubscriptionStatus.PastDue, SubscriptionStatus.Expired);

	public static readonly Constraint<Subscription> SeatCountPositive = Constraints<Subscription>.Always(
		s => s.SeatCount > 0,
		"Seat count must be at least 1."
	);

	public static readonly Constraint<Subscription> FreePlanHasOneSeat = Constraints<Subscription>.When(s => s.Plan == Plan.Free).Requires(
		s => s.SeatCount == 1,
		"Free plan is limited to 1 seat."
	);

	public static readonly Constraint<Subscription> ActiveRequiresBillingPeriod = Constraints<Subscription>.When(
		s => s.Status == SubscriptionStatus.Active
	).Requires(s => s.CurrentPeriod is not null, "An active subscription must have a current billing period.");
}
