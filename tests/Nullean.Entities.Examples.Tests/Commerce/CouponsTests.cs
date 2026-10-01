using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Examples.Commerce.Coupons;

namespace Nullean.Entities.Examples.Tests.Commerce;

public class CouponsTests
{
	private static readonly ConstraintSet<Cart> Constraints = CouponConstraints.Constraints;

	private static Coupon PercentOff(string code, decimal min = 0m) => new(code, min, CouponKind.Standard);

	private static Coupon Exclusive(string code, decimal min = 0m) => new(code, min, CouponKind.Exclusive);

	[Test]
	public void Validate_single_coupon_above_minimum_spend_passes()
	{
		var cart = new Cart(100m, [Exclusive("EXCL", 50m)]);
		Constraints.Validate(cart).Should().BeEmpty();
	}

	[Test]
	public void Validate_coupon_below_minimum_spend_fails()
	{
		var cart = new Cart(10m, [Exclusive("EXCL", 50m)]);
		Constraints.Validate(cart).Should().NotBeEmpty();
	}

	[Test]
	public void Validate_two_exclusive_coupons_fails()
	{
		var cart = new Cart(200m, [Exclusive("E1"), Exclusive("E2")]);
		Constraints.Validate(cart).Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Applied_when_adding_a_valid_coupon()
	{
		var channel = ConstraintChannel.For(new Cart(100m, [])).Enforce(Constraints);
		var result = channel.Apply(c => c with { AppliedCoupons = [PercentOff("PCT10")] });
		result.Should().BeOfType<ChannelResult<Cart>.Applied>();
	}

	[Test]
	public void CsCheck_standard_coupons_with_no_minimum_always_pass()
	{
		Gen.Int[1, 10].Sample(n =>
		{
			var coupons = Enumerable.Range(0, n).Select(i => PercentOff($"CODE{i}")).ToList();
			var cart = new Cart(50m, coupons);
			Constraints.Validate(cart).Should().BeEmpty();
		});
	}
}
