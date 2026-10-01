# Scheduling

Two scenarios with collection-level rules: one checking time-slot overlap, the other enforcing labour-law limits across a nested graph.

---

## RoomBooking

A meeting room with a list of bookings (time slots).

- A booking's start must be before its end.
- No two bookings in the same calendar can overlap — the overlap check iterates over all pairs, so the violation message names the conflicting slots (`Check` over a collection rather than a single-value `Always`).

---

## ShiftRoster (nested rules)

A staffing roster with multiple employees, each with a list of shifts. The interesting part is rule composition: constraints are defined at the `Employee` level and then delegated to from a `Roster`-level constraint.

- No employee may work more than 40 h in any calendar week (`Check` — groups shifts by ISO week number).
- Every employee must have at least 11 h of rest between consecutive shifts.
- Both rules are re-checked at the roster level by calling the employee constraints directly — showing how to build aggregate constraints from fine-grained ones without duplicating logic.

This is one of the larger object graphs in the examples: `Roster → [Employee] → [Shift]`.
