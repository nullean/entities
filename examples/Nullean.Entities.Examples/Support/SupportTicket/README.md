# Support Ticket

**What is modelled:** A customer support ticket moving through Open → InProgress ↔ AwaitingCustomer → Resolved → Closed, with a re-open path from Resolved back to InProgress.

**Rules, in plain English**
- Status transitions follow a defined allow-list (including re-open).
- In-progress tickets must have an assigned agent.
- Resolved/closed tickets must record a resolution timestamp.
- Title must not be empty.

**Reactive extension:** Alert when a Critical ticket goes unresolved for too long.

**Tested axes:** invariant (Validate), channel Applied/Noop/Failure, transitions, CsCheck property.
