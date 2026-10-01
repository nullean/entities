# Shift Roster (big graph)

**What is modelled:** A staffing roster with multiple employees, each with a list of shifts.

**Rules, in plain English**
- No employee may work more than 40 hours in any calendar week.
- Every employee must have at least 11 hours of rest between consecutive shifts.
- Both rules are checked at the roster level by delegating to per-employee constraints — demonstrating nested rule composition.

**Tested axes:** invariant (Validate), CsCheck property.
