using System.Collections.Immutable;
using Nullean.Entities;

namespace EventSourcedCart;

public sealed record CartLine(string Sku, int Qty);

/// <summary>The state: folded from the events, never stored on its own. Lines are kept sorted by SKU, so equal carts are equal values.</summary>
public sealed record Cart(ImmutableList<CartLine> Lines, bool CheckedOut)
{
	public static readonly Cart Empty = new([], false);

	public int QtyOf(string sku) => Lines.FirstOrDefault(l => l.Sku == sku)?.Qty ?? 0;
}

public abstract record CartCommand
{
	public sealed record AddItem(string Sku, int Qty) : CartCommand;

	public sealed record RemoveItem(string Sku) : CartCommand;

	public sealed record Checkout : CartCommand;
}

public abstract record CartEvent
{
	public sealed record ItemAdded(string Sku, int Qty) : CartEvent;

	public sealed record ItemRemoved(string Sku) : CartEvent;

	public sealed record CheckedOut(decimal Total) : CartEvent;
}

public static class Catalog
{
	public const int MaxPerSku = 3;

	private static readonly Dictionary<string, decimal> Prices = new() { ["apple"] = 0.50m, ["pear"] = 0.75m, ["fig"] = 2.00m };

	public static bool Sells(string sku) => Prices.ContainsKey(sku);

	public static decimal PriceOf(string sku) => Prices[sku];
}

/// <summary>
/// The two pure functions an event-sourced aggregate is made of. <see cref="Decide"/> turns a command into the events it produces (none, when it is refused);
/// <see cref="Evolve"/> folds one event into the state. Neither touches a clock, a store or a service, so each is trivial to test and to explore.
/// </summary>
public static class CartLogic
{
	public static IEnumerable<CartEvent> Decide(Cart cart, CartCommand command)
	{
		if (cart.CheckedOut)
		{
			return [];
		}

		return command switch
		{
			CartCommand.AddItem(var sku, var qty) when Catalog.Sells(sku) && qty > 0 && cart.QtyOf(sku) + qty <= Catalog.MaxPerSku =>
				[new CartEvent.ItemAdded(sku, qty)],
			CartCommand.RemoveItem(var sku) when cart.QtyOf(sku) > 0 => [new CartEvent.ItemRemoved(sku)],
			CartCommand.Checkout when cart.Lines.Count > 0 => [new CartEvent.CheckedOut(cart.Lines.Sum(l => l.Qty * Catalog.PriceOf(l.Sku)))],
			_ => [],
		};
	}

	public static Cart Evolve(Cart cart, CartEvent evt) => evt switch
	{
		CartEvent.ItemAdded(var sku, var qty) =>
			cart with
			{
				Lines = [.. cart.Lines.Where(l => l.Sku != sku).Append(new CartLine(sku, cart.QtyOf(sku) + qty)).OrderBy(l => l.Sku, StringComparer.Ordinal)],
			},
		CartEvent.ItemRemoved(var sku) => cart with { Lines = [.. cart.Lines.Where(l => l.Sku != sku)] },
		CartEvent.CheckedOut => cart with { CheckedOut = true },
		_ => cart,
	};

	public static Cart Rehydrate(IEnumerable<CartEvent> events) => events.Aggregate(Cart.Empty, Evolve);
}

/// <summary>
/// What must hold of every cart, however it got there. They are checked on the cart the events <em>would</em> produce, before anything is appended:
/// a second line of defence behind <see cref="CartLogic.Decide"/>, so a bug in the decider is stopped at the stream, not discovered in a report.
/// </summary>
public static class CartRules
{
	public static readonly ConstraintSet<Cart> All = ConstraintSet.From(
		("quantities-are-within-limits", Constraints<Cart>.Always(
			c => c.Lines.All(l => l.Qty is >= 1 and <= Catalog.MaxPerSku),
			$"A line holds between 1 and {Catalog.MaxPerSku}"
		)),
		("one-line-per-sku", Constraints<Cart>.Unique(c => c.Lines, l => l.Sku)),
		("only-catalogue-items", Constraints<Cart>.Always(c => c.Lines.All(l => Catalog.Sells(l.Sku)), "Only items from the catalogue can be in a cart")),
		("checkout-needs-items", Constraints<Cart>.Always(c => !c.CheckedOut || c.Lines.Count > 0, "An empty cart cannot be checked out")),
		("checked-out-is-final",
		Constraints<Cart>.Transitions(
			(before, after) => !before.CheckedOut || (after.CheckedOut && before.Lines.SequenceEqual(after.Lines)),
			"A checked-out cart cannot change"
		))
	);
}
