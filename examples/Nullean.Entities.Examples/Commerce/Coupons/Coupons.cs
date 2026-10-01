namespace Nullean.Entities.Examples.Commerce.Coupons;

public enum CouponKind
{
	Standard,
	Exclusive
}

public sealed record Coupon(string Code, decimal MinimumSpend, CouponKind Kind);

public sealed record Cart(decimal Subtotal, IReadOnlyList<Coupon> AppliedCoupons);

[Constraints]
public static partial class CouponConstraints
{
	public static readonly Constraint<Cart> MinimumSpendMet = Constraints<Cart>.Check(
		cart =>
			[
				.. cart
					.AppliedCoupons
					.Where(c => cart.Subtotal < c.MinimumSpend)
					.Select(c => Violation.Error($"Coupon '{c.Code}' requires a minimum spend of {c.MinimumSpend:C}."))
			]
	);

	public static readonly Constraint<Cart> NoTwoExclusiveCoupons = Constraints<Cart>.Always(
		cart => cart.AppliedCoupons.Count(c => c.Kind == CouponKind.Exclusive) <= 1,
		"Only one exclusive coupon may be applied at a time."
	);
}
