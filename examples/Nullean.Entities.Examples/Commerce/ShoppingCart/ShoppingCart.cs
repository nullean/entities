namespace Nullean.Entities.Examples.Commerce.ShoppingCart;

public sealed record CartLine(string Sku, int Quantity, decimal UnitPrice);

public sealed record ShoppingCart(IReadOnlyList<CartLine> Lines)
{
	public decimal Total => Lines.Sum(l => l.Quantity * l.UnitPrice);

	public ShoppingCart AddLine(CartLine line) => this with { Lines = [.. Lines, line] };

	public ShoppingCart Clear() => this with { Lines = [] };
}

[Constraints]
public static partial class ShoppingCartConstraints
{
	public static readonly Constraint<ShoppingCart> AllQuantitiesPositive = Constraints<ShoppingCart>.Always(
		c => c.Lines.All(l => l.Quantity > 0),
		"Every line must have a positive quantity."
	);

	public static readonly Constraint<ShoppingCart> AllPricesPositive = Constraints<ShoppingCart>.Always(
		c => c.Lines.All(l => l.UnitPrice > 0),
		"Every line must have a positive unit price."
	);

	// Clearing an already-empty cart leaves it empty — idempotent.
	public static readonly Constraint<ShoppingCart> ClearIsIdempotent = Constraints<ShoppingCart>.Idempotent(c => c.Clear(), "Clear must be idempotent.");

	// Adding a line can only make the total grow — step-scoped, never attached alongside RemoveLine.
	public static readonly Constraint<ShoppingCart> AddLineGrowsTotal = Constraints<ShoppingCart>.Monotonic(
		c => c.Total,
		MonotonicDirection.NonDecreasing,
		"AddLine must never shrink the running total."
	);
}
