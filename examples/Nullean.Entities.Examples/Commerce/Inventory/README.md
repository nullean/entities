# Inventory

**What is modelled:** A warehouse with stock items tracking on-hand and reserved quantities.

**Rules, in plain English**
- On-hand stock cannot go negative.
- Reserved quantity cannot exceed on-hand.
- Reserved quantity cannot be negative.
- A reserve/release operation must not change on-hand stock (`Conserves`).

**Reactive extension:** A reserve that is not followed by a release or restock within 24 hours triggers an alert.

**Tested axes:** invariant (Validate), channel Applied/Noop, conservation, CsCheck property.
