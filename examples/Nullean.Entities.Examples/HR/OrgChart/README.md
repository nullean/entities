# Org Chart (hierarchical graph)

**What is modelled:** An organisational hierarchy of people with manager/direct-report relationships, employment status, and cycle detection.

**Rules, in plain English**
- Person status follows Active → OnLeave ↔ Active and Active/OnLeave → Terminated.
- Terminated employees must have no direct reports and no manager.
- The org-level graph must have no cycles (a person cannot be their own ancestor).
- Every manager reference must point to a person that exists in the org.

This is the most structurally complex scenario: a nested graph with cross-entity referential integrity, demonstrating how to compose per-entity and aggregate-level constraints.

**Tested axes:** invariant (Validate), channel Applied/Failure, CsCheck property (random org graphs, cycle injection).
