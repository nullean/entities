# DataAudit

The database administrator's use of the library: audit rows that already exist. `dotnet run --project examples/Apps/DataAudit` (exits non-zero if anything stops behaving as narrated).

| File | What to copy |
|---|---|
| `Rules.cs` | Row rules (`RowRules`) and table rules (`TableRules`), each commented with the schema constraint it corresponds to; severities (`Error`, `Warning`, `Hint`) as part of the rule; `SchemaEquivalent`, the hand-written table of which rules the schema should also enforce. |
| `AuditService.cs` | `ValidateAsync` over a stream (one row at a time), table rules over what was kept, a report ranked by severity with examples. `TryInsert` is the write path for the same rules: errors refuse a row, warnings and hints come back as notices. |
| `FakeTable.cs`, `Program.cs` | A cursor-like table with known, seeded dirt, and the check that the audit found exactly that: no row missed, none wrongly flagged. |

Tests: `tests/Nullean.Entities.Apps.Tests/DataAudit` run the audit over several seeds, over a clean table, and under cancellation, and check that every rule has a line in the schema table.

Things worth noticing:

- Rules that read the clock (`NotInFuture`) take the audit's `TimeProvider`, so a re-run of last year's audit gives last year's answer.
- Row rules stream; table rules (`Unique`, `References`, `Acyclic`) need the rows together. A table too large to hold can be audited by giving the table rules a value that holds only what they read (an email hash, an id, a parent id): the rules take whatever type you declare them over.
- `Unique` and `References` over rows in memory are **not** a substitute for the database's own index and foreign key. They tell you what is already wrong and what the schema should enforce; `SchemaEquivalent` is where that is written down. Rules with no declarative form (the parent cycle, anything reading a clock) say so.
- The library does not generate DDL. Mapping a rule to a `CHECK` or an index stays a decision.
