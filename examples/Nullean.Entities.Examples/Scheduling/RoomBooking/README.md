# Room Booking

**What is modelled:** A meeting room with a list of bookings (time slots).

**Rules, in plain English**
- A booking's start time must be before its end time.
- No two bookings in the same calendar can overlap (`Check` over a collection).

**Tested axes:** invariant (Validate), channel Applied/Failure, CsCheck property.
