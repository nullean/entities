# Inventory

Two aggregates and the rules between them. `dotnet run --project examples/Apps/Inventory` (exits non-zero if anything stops behaving as narrated).

| File | What to copy |
|---|---|
| `Model.cs` | A `Catalog` and a `Warehouse` with their own rules (`[Constraints]`), the `StockState` pair record, and `StockRules`: each aggregate's rules lifted onto the pair with `Map(..., prefix:)`, plus `References`, the foreign key that no single aggregate can state. |
| `StockService.cs` | Every operation is a mutation of the pair, applied against the pair's rules. The service never looks a SKU up or compares a quantity. |
| `Program.cs` | The scenarios, narrated, including auditing data that already exists with `Validate`. |

Tests: `tests/Nullean.Entities.Apps.Tests/Inventory`. The service by example and as a property (any sequence of receive, reserve, release and add keeps the stock valid), then the rules against **generated data**: `Probe` with the relation declared (`Reference<StockLevel, Product, string>(l => l.Sku, p => p.Sku)`) proves every rule can be broken by some generated state, so none is a tautology, and `Laws` pairs each rule with a counter-example that breaks that rule alone.

Things worth noticing:

- Rule names must be unique within a set (a violation, `Probe` and `Laws` all identify rules by name). Both aggregates have a `SkusAreUnique`, so each is lifted with a prefix (`Catalog.SkusAreUnique`, `Warehouse.SkusAreUnique`), which also says which part a violation is about. Without the prefix `Probe` tells you, rather than crashing.
- The pair record is the answer to "where do I put a rule about two aggregates". It is a value you can validate, generate and probe like any other.
- A reservation for a SKU with no stock is not special-cased: `Reserve` creates a level with nothing on hand, and `ReservedFitsOnHand` refuses it. The rule decides, so there is one place to read the fact.
