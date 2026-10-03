using Nullean.Entities;

namespace Inventory;

public sealed record Product(string Sku, string Name, decimal Price);

/// <summary>What is sold. One aggregate.</summary>
public sealed record Catalog(IReadOnlyList<Product> Products)
{
	public Catalog Add(Product product) => this with { Products = [.. Products, product] };
}

public sealed record StockLevel(string Sku, int OnHand, int Reserved);

/// <summary>What is in the warehouse. A second aggregate, which refers to the first by SKU.</summary>
public sealed record Warehouse(IReadOnlyList<StockLevel> Levels)
{
	public Warehouse Receive(string sku, int qty) => Change(sku, level => level with { OnHand = level.OnHand + qty }, new StockLevel(sku, qty, 0));

	public Warehouse Reserve(string sku, int qty) => Change(sku, level => level with { Reserved = level.Reserved + qty }, new StockLevel(sku, 0, qty));

	public Warehouse Release(string sku, int qty) => Change(sku, level => level with { Reserved = level.Reserved - qty }, null);

	// A change to a SKU that has no level creates one (or does nothing): the rules, not this method, decide whether that is allowed.
	private Warehouse Change(string sku, Func<StockLevel, StockLevel> change, StockLevel? whenMissing) =>
		Levels.Any(l => l.Sku == sku)
			? this with { Levels = [.. Levels.Select(l => l.Sku == sku ? change(l) : l)] }
			: whenMissing is null ? this : this with { Levels = [.. Levels, whenMissing] };
}

/// <summary>Both aggregates together: what a rule about the two needs, and what a service loads and saves.</summary>
public sealed record StockState(Catalog Catalog, Warehouse Warehouse);

[Constraints]
public static partial class CatalogRules
{
	public static readonly Constraint<Catalog> SkusAreUnique = Constraints<Catalog>.Unique(c => c.Products, p => p.Sku, message: "Two products share a SKU");

	public static readonly Constraint<Catalog> PricesArePositive = Constraints<Catalog>.Always(
		c => c.Products.All(p => p.Price > 0),
		"A price must be greater than zero"
	);
}

[Constraints]
public static partial class WarehouseRules
{
	public static readonly Constraint<Warehouse> SkusAreUnique = Constraints<Warehouse>.Unique(
		w => w.Levels,
		l => l.Sku,
		message: "Two stock levels share a SKU"
	);

	public static readonly Constraint<Warehouse> QuantitiesAreNotNegative = Constraints<Warehouse>.Always(
		w => w.Levels.All(l => l.OnHand >= 0 && l.Reserved >= 0),
		"Quantities cannot be negative"
	);

	public static readonly Constraint<Warehouse> ReservedFitsOnHand = Constraints<Warehouse>.Always(
		w => w.Levels.All(l => l.Reserved <= l.OnHand),
		"More is reserved than is on hand"
	);
}

/// <summary>
/// The rules of the whole: each aggregate's own, plus the one that exists only between them. <c>Map</c> lifts a rule on <see cref="Catalog"/> or
/// <see cref="Warehouse"/> onto <see cref="StockState"/> without copying it; <c>References</c> is the foreign key that no single aggregate can state.
/// </summary>
public static class StockRules
{
	public static readonly ConstraintSet<StockState> All = ConstraintSet.Merge(
		CatalogRules.All.Map<StockState>(s => s.Catalog, prefix: "Catalog."),
		WarehouseRules.All.Map<StockState>(s => s.Warehouse, prefix: "Warehouse."),
		ConstraintSet.From(
			("stocked-skus-are-in-the-catalogue",
			Constraints<StockState>.References<StockLevel, string>(
				s => s.Warehouse.Levels,
				l => [l.Sku],
				s => s.Catalog.Products.Select(p => p.Sku),
				message: "Stock exists for a SKU that is not in the catalogue"
			))
		)
	);
}
