using AwesomeAssertions;
using CsCheck;
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
		var channel = ConstraintChannel.For(new StockItem("A", 10, 0)).Enforce(Constraints);
		var result = channel.Apply(s => s.Restock(5));
		result.Should().BeOfType<ChannelResult<StockItem>.Applied>();
	}

	[Test]
	public void Channel_Failure_when_reserving_more_than_available()
	{
		var channel = ConstraintChannel.For(new StockItem("A", 5, 0)).Enforce(Constraints);
		var result = channel.Apply(s => s.Reserve(10));
		result.Should().BeOfType<ChannelResult<StockItem>.Failure>();
	}

	[Test]
	public void CsCheck_reserve_then_release_returns_to_original_on_hand()
	{
		Gen.Select(Gen.Int[0, 100], Gen.Int[0, 50], (onHand, reserve) => (onHand, reserve: Math.Min(reserve, onHand))).Sample(tuple =>
		{
			var (onHand, reserve) = tuple;
			var item = new StockItem("SKU", onHand, 0);
			var reserved = item.Reserve(reserve);
			var released = reserved.Release(reserve);
			released.Reserved.Should().Be(0);
			released.OnHand.Should().Be(onHand);
		});
	}
}
