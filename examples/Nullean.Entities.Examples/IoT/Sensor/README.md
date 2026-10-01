# IoT Sensor

**What is modelled:** An IoT sensor with status, recent readings, active alerts, and threshold configuration.

**Rules, in plain English**
- Min threshold must be less than max threshold (when both are configured).
- Readings outside the configured thresholds produce a warning violation.
- Device ID must not be empty.

**Reactive extension:** Raise a Critical alert when consecutive readings exceed the max threshold.

**Tested axes:** invariant (Validate), channel Applied/Failure, CsCheck property (generated readings).
