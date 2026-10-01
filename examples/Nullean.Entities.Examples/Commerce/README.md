# Commerce

Four scenarios covering the rules that appear in most e-commerce backends.

---

## ShoppingCart

A cart holds lines (sku, quantity, unit price). Constraints are point-in-time:

- Every line must have a positive quantity and unit price — caught at `Validate` time.
- `Clear` is idempotent — clearing an already-empty cart is a no-op (`Idempotent`).
- `AddLine` can only grow the total — the monotonicity constraint is step-scoped to this operation so it never fires on `RemoveLine` (`Monotonic`).

Tests use Bogus-backed generators (`GenFakerExtensions.Faker<CartLine>`) so shrinking and seed replay work identically to plain `Gen<T>`.

---

## OrderLifecycle

The flagship scenario. An order moves `Pending → Confirmed → Shipped`, or is `Cancelled`. Rules cover every constraint kind:

- **Always:** total ≥ 0.
- **When/Requires:** a Confirmed or Shipped order must have at least one line.
- **Transitions:** only legal state arcs — ship only from Confirmed, confirm only from Pending.
- **Idempotent:** cancelling twice equals cancelling once.
- **Monotonic (step-scoped):** `AddLine` can only grow the total.
- **Conserves:** transferring a line between two orders must not create or destroy quantity.

Reactive extension: a confirmed order ships or is cancelled within 48 h; a cancelled order is never shipped afterward; every confirmed order eventually closes.

Tests drive this with `Script.SampleWith` against the reactive spec, and `Script.Metamorphic` to verify that adding then removing a line commutes.

---

## Inventory

A warehouse with stock items tracking `OnHand` and `Reserved` quantities.

- `OnHand` and `Reserved` are each non-negative; `Reserved ≤ OnHand`.
- A reserve/release cycle must not change on-hand stock (`Conserves` over the before/after pair).

Reactive extension: a reserve not followed by a release or restock within 24 h triggers an alert.

---

## Coupons

A cart with applied discount coupons.

- Every applied coupon's minimum spend must be met by the cart subtotal (violation message embeds the coupon code and required amount — uses `Check` rather than `Always`).
- At most one exclusive coupon may be applied at a time.
