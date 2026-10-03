using Inventory;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;
using static Apps.Narrator;

// A catalogue and a warehouse, and the rules between them. Run it with `dotnet run`.

await using var provider = new ServiceCollection().AddInventory().BuildServiceProvider();
var stock = provider.GetRequiredService<StockService>();
var repository = provider.GetRequiredService<IStockRepository>();

static string Why(Result<StockState> result) => string.Join("; ", result.ViolationsOrEmpty.Select(v => v.Message));

async Task<StockLevel> Level(string sku) => (await repository.LoadAsync()).Warehouse.Levels.Single(l => l.Sku == sku);

Section("The catalogue's own rules");
Expect((await stock.AddProductAsync(new Product("APL-1", "Apple", 0.50m))).IsOk, "a product is added");
var duplicate = await stock.AddProductAsync(new Product("APL-1", "Another apple", 0.60m));
Say($"same SKU again -> {Why(duplicate)}");
Expect(duplicate.IsFail, "two products cannot share a SKU");
Expect((await stock.AddProductAsync(new Product("PER-1", "Pear", 0m))).IsFail, "a product with no price is refused");
await stock.AddProductAsync(new Product("PER-1", "Pear", 0.75m));

Section("A rule across the two aggregates: stock needs a product");
var ghost = await stock.ReceiveAsync("GHO-9", 10);
Say($"receiving stock of GHO-9 -> {Why(ghost)}");
Expect(ghost.IsFail, "no stock for a product nobody sells");
Expect((await repository.LoadAsync()).Warehouse.Levels.Count == 0, "and nothing was saved");

Section("The warehouse's own rules");
await stock.ReceiveAsync("APL-1", 10);
Expect((await stock.ReserveAsync("APL-1", 4)).IsOk, "4 of 10 reserved");
var tooMany = await stock.ReserveAsync("APL-1", 7);
Say($"reserving 7 more -> {Why(tooMany)}");
Expect(tooMany.IsFail, "6 are left, so 7 cannot be reserved");
Expect(await Level("APL-1") is { OnHand: 10, Reserved: 4 }, "a refused reservation changes nothing");

Expect((await stock.ReserveAsync("PER-1", 1)).IsFail, "nothing can be reserved of a product with no stock");
Expect((await stock.ReleaseAsync("APL-1", 5)).IsFail, "more cannot be released than was reserved");
Expect((await stock.ReleaseAsync("APL-1", 4)).IsOk && await Level("APL-1") is { Reserved: 0 }, "releasing what was reserved works");

Section("The same rules audit data that already exists");
var dirty = new StockState(
	new Catalog([new Product("A", "a", 1m), new Product("A", "a again", 1m)]),
	new Warehouse([new StockLevel("A", 1, 3), new StockLevel("Z", 2, 0)])
);
foreach (var v in StockRules.All.Validate(dirty))
{
	Say($"{v.ConstraintId}: {v.Message}");
}
Expect(StockRules.All.Validate(dirty).Count >= 3, "a duplicate SKU, an over-reservation and an orphaned stock level are all found");

return Finish();
