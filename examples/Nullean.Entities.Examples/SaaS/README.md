# SaaS — Subscription lifecycle

A SaaS subscription moving through `Trial → Active → PastDue / Cancelled / Expired`.

**Rules:**

- Status transitions follow a defined allow-list — Trial can activate or cancel; PastDue can recover (back to Active) or expire.
- Seat count must be positive.
- Free plan subscriptions are limited to exactly 1 seat (`When/Requires`).
- An Active subscription must have a current billing period set.

**Reactive extension:**

Notify when a subscription transitions to `PastDue`. Tests use `Script.SampleWith` with a scheduler to verify the dunning window: every failed payment must be resolved or the subscription cancelled within 14 days.
