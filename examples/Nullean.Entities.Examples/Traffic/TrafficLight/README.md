# Traffic Light

**What is modelled:** A traffic light cycling through Red → Green → Yellow → Red.

**Rules, in plain English**
- Signals must follow the standard Red → Green → Yellow → Red cycle (no skipping).
- Each signal phase must have a positive duration.
- Intersection ID must not be empty.

This is the canonical minimal state-machine example — small graph, no optional fields, fully deterministic.

**Reactive extension:** Emit a `SignalChanged` event on every transition for traffic-flow analytics.

**Tested axes:** invariant (Validate), channel Applied/Noop/Failure, transitions, CsCheck full cycle.
