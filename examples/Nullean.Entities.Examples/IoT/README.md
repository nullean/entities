# IoT — Sensor readings

An IoT sensor device with status, a recent-reading buffer, and configurable min/max thresholds.

**Rules:**

- When both thresholds are configured, `MinThreshold < MaxThreshold`.
- Readings outside the configured thresholds produce a warning-severity violation (not an error — out-of-range is alertable but not state-corrupting).
- Device ID must not be empty.

**Reactive extension:**

Raise a `Critical` alert when consecutive readings exceed the max threshold (`Always<ReadingReceived>` combined with an `Eventually` heartbeat rule — a device that stops sending readings at all is itself a violation).
