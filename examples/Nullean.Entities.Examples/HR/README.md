# HR — OrgChart (hierarchical graph)

An organisational hierarchy of people with manager/direct-report relationships and employment status.

This is the most structurally complex scenario in the examples: a nested graph with cross-entity referential integrity and cycle detection.

**Rules:**

- Person status follows a defined transition allow-list: `Active ↔ OnLeave` and `Active/OnLeave → Terminated`.
- Terminated employees must have no direct reports and no manager.
- No cycles — a person cannot be their own ancestor (depth-first traversal with a visited set).
- Every manager reference must point to a person that exists in the org.
- Span of control: no manager may have more than 12 direct reports.

**How it is validated:**

`Validate` runs on a snapshot of the whole `OrgChart`. Tests inject random org graphs via `GenAuto.Of<OrgChart>` and separately construct graphs with deliberate cycle, dangling-reference, and span violations to prove each constraint fires independently.
