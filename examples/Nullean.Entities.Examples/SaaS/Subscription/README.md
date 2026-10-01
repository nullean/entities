# Subscription (SaaS lifecycle)

**What is modelled:** A SaaS subscription moving through Trial → Active → PastDue/Cancelled/Expired.

**Rules, in plain English**
- Status transitions follow a defined allow-list (Trial can activate or cancel, PastDue can recover or expire).
- Seat count must be positive.
- Free plan is limited to exactly 1 seat.
- Active subscriptions must have a current billing period.

**Reactive extension:** Notify when a subscription transitions to PastDue.

**Tested axes:** invariant (Validate), channel Applied/Noop/Failure, transitions, CsCheck property.
