# Traffic — Traffic light state machine

A traffic light cycling through `Red → Green → Yellow → Red`. This is the canonical minimal state-machine example: small graph, no optional fields, fully deterministic transitions.

**Rules:**

- Signals must follow the standard cycle — no skipping (`Transitions`).
- Each signal phase must have a positive duration.
- Intersection ID must not be empty.

**Reactive extension:**

Emit a `SignalChanged` event on every transition for traffic-flow analytics. The full cycle (Red → Green → Yellow → Red) must complete within 90 seconds — modelled as `After<ColorChanged where From=Red>().Then<ColorChanged where To=Red>().Within(90s).Required()`.

This scenario is a stepping stone toward the planned `StateMachine<TState>` API, which will derive the transition constraint, the reactive monitor, and a Mermaid diagram from a single declaration.
