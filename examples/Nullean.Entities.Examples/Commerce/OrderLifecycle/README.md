# Order Lifecycle

**What is modelled:** An order moves through Pending → Confirmed → Shipped, or is Cancelled. Orders have lines with quantities and prices.

**Rules, in plain English**
- The order total always equals the sum of its lines' subtotals.
- A Confirmed or Shipped order must have at least one line.
- You can only Ship from Confirmed, and Confirm from Pending with lines — `Transitions`.
- Cancelling twice is the same as cancelling once — `Idempotent`.
- Adding a line can only grow the total (step-scoped `Monotonic`).
- Transferring a line between two orders must not create or destroy quantity (`Conserves` over the pair).

**Reactive extension:** A confirmed order ships or is cancelled within 48 hours; a cancelled order is never shipped afterward; every confirmed order eventually closes.

**Tested axes:** invariant, idempotency, monotonicity, conservation, transitions, channel Applied/Noop/Failure, CsCheck Script.SampleWith with reactive spec, confluence (Metamorphic), vacancy.
