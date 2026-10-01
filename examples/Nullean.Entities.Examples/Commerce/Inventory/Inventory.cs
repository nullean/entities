namespace Nullean.Entities.Examples.Commerce.Inventory;

public sealed record StockItem(string Sku, int OnHand, int Reserved)
{
	public int Available => OnHand - Reserved;

	public StockItem Reserve(int qty) => this with { Reserved = Reserved + qty };

	public StockItem Release(int qty) => this with { Reserved = Math.Max(0, Reserved - qty) };

	public StockItem Restock(int qty) => this with { OnHand = OnHand + qty };
}

public sealed record Warehouse(IReadOnlyList<StockItem> Items);

[Constraints]
public static partial class InventoryConstraints
{
	public static readonly Constraint<StockItem> StockNeverNegative = Constraints<StockItem>.Always(s => s.OnHand >= 0, "On-hand stock cannot go negative.");

	public static readonly Constraint<StockItem> ReservedNeverExceedsOnHand = Constraints<StockItem>.Always(
		s => s.Reserved <= s.OnHand,
		"Reserved quantity cannot exceed on-hand stock."
	);

	public static readonly Constraint<StockItem> ReservedNeverNegative = Constraints<StockItem>.Always(
		s => s.Reserved >= 0,
		"Reserved quantity cannot be negative."
	);
}

// Reserve+Release on the same item conserves the total allocated units.
[Constraints]
public static partial class StockMovementConstraints
{
	public static readonly Constraint<(StockItem Before, StockItem After)> OnHandIsConserved = Nullean
		.Entities
		.Constraints<(StockItem Before, StockItem After)>
		.Conserves(p => p.Before.OnHand, "A reserve/release operation must not change on-hand stock.");
}
