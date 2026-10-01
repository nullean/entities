using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.Commerce.Inventory;

namespace Nullean.Entities.Examples.Tests.Commerce;

public class InventoryTests
{
	private static readonly ConstraintSet<StockItem> Constraints = InventoryConstraints.All;

	[Test]
	public void Validate_valid_stock_item()
	{
		Constraints.Validate(new StockItem("A", 10, 5)).Should().BeEmpty();
	}

	[Test]
	public void Validate_rejects_reserved_exceeding_on_hand()
	{
		Constraints.Validate(new StockItem("A", 5, 10)).Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Applied_on_restock()
	{
		var channel = ConstrainedValue.For(new StockItem("A", 10, 0)).Enforce(Constraints);
		var result = channel.ApplyDetailed(s => s.Restock(5));
		result.Should().BeOfType<ApplyOutcome<StockItem>.Applied>();
	}

	[Test]
	public void Channel_Failure_when_reserving_more_than_available()
	{
		var channel = ConstrainedValue.For(new StockItem("A", 5, 0)).Enforce(Constraints);
		var result = channel.ApplyDetailed(s => s.Reserve(10));
		result.Should().BeOfType<ApplyOutcome<StockItem>.Failure>();
	}

	[Test]
	public void ForAll_reserving_what_is_available_then_releasing_it_restores_the_item() =>
		Constraints.ForAll(
			item =>
			{
				var quantity = item.Available;
				return item.Reserve(quantity).Release(quantity) == item;
			},
			iter: 200
		);

	// Reserve/Release/Restock take whatever you give them; the constraints reject a bad result at the Apply boundary.
	[Test]
	public void AutoTest_enforced_stock_never_goes_negative_or_over_reserved() => InventoryConstraints.Instance.Spec().Enforced().Exhaustive();
}
