# Shopping Cart

**What is modelled:** An immutable shopping cart with lines (sku, quantity, unit price).

**Rules, in plain English**
- Every line must have a positive quantity.
- Every line must have a positive unit price.
- Clearing an already-empty cart is the same as clearing it once (`Idempotent`).
- Adding a line can only grow the total — this rule is step-scoped to `AddLine` and never attached to `RemoveLine` (`Monotonic`).

**Tested axes:** invariant (Validate), channel Applied/Noop/Failure, channel Noop on Clear of empty cart, CsCheck with Bogus generators, idempotency and monotonicity properties.
