using CsCheck;
using Nullean.Entities.Quickstart;

namespace Nullean.Entities.Quickstart.Tests;

// The same Order, tested with plain CsCheck: a library worth using on its own. Every property is written by hand.
public class PlainCsCheckTests
{
	private static readonly Gen<OrderLine> Lines = Gen.Select(
		Gen.OneOfConst("apple", "pear", "fig"),
		Gen.Int[1, 5],
		Gen.Int[1, 20],
		(sku, quantity, price) => new OrderLine(sku, quantity, price)
	);

	private static readonly Gen<Order> Pending = Lines.Array[0, 3].Select(lines => lines.Aggregate(Order.New(), (order, line) => order.AddLine(line)));

	private static readonly Gen<Order> Orders = Gen.Select(Pending, Gen.Int[0, 3], (order, step) =>
			step switch
			{
				1 => order.Confirm(),
				2 => order.Confirm().Ship(),
				3 => order.Cancel(),
				_ => order
			});

	private static readonly Func<Order, Order>[] Operations = [o => o.Confirm(), o => o.Ship(), o => o.Cancel()];

	[Test]
	public void Cancelling_twice_is_cancelling_once() => Orders.Sample(o => o.Cancel().Cancel() == o.Cancel());

	[Test]
	public void No_operation_lowers_the_version() =>
		Gen.Select(Orders, Gen.OneOfConst(Operations)).Sample((order, operation) => operation(order).Version >= order.Version);

	[Test]
	public void The_total_is_never_negative() => Orders.Sample(o => o.Total >= 0);
}
