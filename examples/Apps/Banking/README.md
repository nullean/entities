# Banking

Money moving between accounts. `dotnet run --project examples/Apps/Banking` (exits non-zero if anything stops behaving as narrated).

| File | What to copy |
|---|---|
| `Values.cs` | `Money` and `Iban`: values that cannot be invalid (private constructor, `Create` returns a `Result`). The IBAN carries the real mod-97 checksum, so a mistyped digit is caught at the edge. |
| `Account.cs` | Rules about one account (`[Constraints] AccountConstraints`), and `TransferRules`: both accounts' rules applied to each half of a `Transfer`, plus the rules that exist only across them (`Conserves` the total, accounts differ, currencies match). |
| `TransferService.cs` | Parse everything at the edge with `Result.Combine` (every problem reported at once), then let the rules decide. Nothing in the service compares a balance with an amount. |
| `Program.cs` | The scenarios, narrated, including 200 concurrent transfers. |

Tests: `tests/Nullean.Entities.Apps.Tests/Banking`. The interesting two are properties: *any* sequence of transfers keeps the total and every overdraft (a CsCheck generator, shrunk on failure), and the same statement as a rule on the state the service exposes, checked after every generated step with `SampleState`.

Things worth noticing:

- `Result.Combine` keeps every violation; a `from ... from ...` query stops at the first. Use `Combine` for input, a query for steps that depend on each other.
- `ConstraintSet.Merge` plus `Map` builds the rules of a composite (`Transfer`) from the rules of its parts without copying any of them.
- The `SemaphoreSlim` stands in for a database transaction. The rules keep a *single* transfer correct; making two transfers not interleave is the infrastructure's job.
