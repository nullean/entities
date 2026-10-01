# Support — Ticket lifecycle

A customer support ticket moving through `Open → InProgress ↔ AwaitingCustomer → Resolved → Closed`, with a re-open path from Resolved back to InProgress.

**Rules:**

- Status transitions follow a defined allow-list, including the re-open path (`Transitions`).
- In-progress tickets must have an assigned agent (`When/Requires`).
- Resolved and closed tickets must record a resolution timestamp.
- Title must not be empty.

**Reactive extension:**

Alert when a Critical ticket has been open without a response for more than the SLA window. Uses `After<Opened>().Then<Responded>().Within(1h).UnlessInterruptedBy<Closed>()` — the obligation is lifted if the ticket is closed before a response arrives.
