# Date Range

**What is modelled:** A date range with a start and an end.

**Rules, in plain English**
- The start date cannot be after the end date.
- Extending a range can only move the end date forward (`Monotonic`).

**Tested axes:** invariant (Validate), channel Applied/Noop/Failure, CsCheck monotonicity property.
