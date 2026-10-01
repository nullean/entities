using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.Commerce.ShoppingCart;

namespace Nullean.Entities.Examples.Tests.Commerce;

public class ShoppingCartTests
{
	private static readonly ConstraintSet<ShoppingCart> Constraints = ShoppingCartConstraints.All;

	private static readonly CartLine LineA = new("SKU-A", 2, 9.99m);

	[Test]
	public void Validate_empty_cart_passes()
	{
		Constraints.Validate(new ShoppingCart([])).Should().BeEmpty();
	}

	[Test]
	public void Validate_cart_with_valid_lines_passes()
	{
		Constraints.Validate(new ShoppingCart([LineA])).Should().BeEmpty();
	}

	[Test]
	public void Validate_rejects_zero_quantity()
	{
		Constraints.Validate(new ShoppingCart([new CartLine("A", 0, 5m)])).Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Applied_when_adding_a_valid_line()
	{
		var channel = ConstrainedValue.For(new ShoppingCart([])).Enforce(Constraints);
		var result = channel.ApplyDetailed(c => c.AddLine(LineA));
		result.Should().BeOfType<ApplyOutcome<ShoppingCart>.Applied>();
	}

	[Test]
	public void Channel_Noop_when_clearing_an_empty_cart()
	{
		var channel = ConstrainedValue.For(new ShoppingCart([])).Enforce(Constraints);
		var result = channel.ApplyDetailed(c => c.Clear());
		result.Should().BeOfType<ApplyOutcome<ShoppingCart>.Noop>();
	}

	[Test]
	public void ShouldSatisfy_carts_built_from_valid_lines_pass_validation()
	{
		var genLine = Gen.Select(Gen.String["SKU-"], Gen.Int[1, 100], Gen.Decimal[0.01m, 999m], (sku, qty, price) => new CartLine(sku, qty, price));

		genLine.List[1, 10].Select(lines => new ShoppingCart(lines)).ShouldSatisfy(Constraints);
	}

	// AddLineGrowsTotal sits in the general set, so through Apply Clear() on a non-empty cart is rejected too (see AUTOTEST_NOTES.md).
	[Test]
	public void AutoTest_enforced_carts_never_hold_a_bad_line() => ShoppingCartConstraints.Instance.Spec().Enforced().Exhaustive();
}
