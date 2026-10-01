using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.Examples.Commerce.OrderLifecycle;

namespace Nullean.Entities.Examples.Tests.Commerce;

public class OrderLifecycleTests
{
	private static readonly ConstraintSet<Order> Constraints = OrderConstraints.All;

	private static readonly OrderLine Line = new("SKU-1", 1, 10m);

	private static Order PendingOrder() => Order.New(Guid.NewGuid());

	private static Order ConfirmedOrder() => Order.New(Guid.NewGuid()).AddLine(Line).Confirm();

	[Test]
	public void Validate_confirmed_order_with_lines_passes()
	{
		Constraints.Validate(ConfirmedOrder()).Should().BeEmpty();
	}

	[Test]
	public void Validate_confirmed_order_without_lines_fails()
	{
		var noLines = Order.New(Guid.NewGuid()) with { Status = OrderStatus.Confirmed };
		Constraints.Validate(noLines).Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Applied_on_legal_Confirm_transition()
	{
		var channel = ConstrainedValue.For(PendingOrder().AddLine(Line)).Enforce(Constraints);
		var result = channel.ApplyDetailed(o => o.Confirm());
		result.Should().BeOfType<ApplyOutcome<Order>.Applied>();
	}

	[Test]
	public void Channel_Noop_when_cancelling_already_cancelled_order()
	{
		var cancelled = Order.New(Guid.NewGuid()).Cancel();
		var channel = ConstrainedValue.For(cancelled).Enforce(Constraints);
		// Cancel on a Cancelled order returns same value → Noop
		var result = channel.ApplyDetailed(o => o.Cancel());
		result.Should().BeOfType<ApplyOutcome<Order>.Noop>();
	}

	[Test]
	public void Channel_Failure_on_illegal_Ship_from_Pending()
	{
		var channel = ConstrainedValue.For(PendingOrder().AddLine(Line)).Enforce(Constraints);
		// Pending → Shipped skips Confirmed — transition constraint should reject
		var shipped = PendingOrder().AddLine(Line) with { Status = OrderStatus.Shipped };
		var channel2 = ConstrainedValue.For(PendingOrder().AddLine(Line)).Enforce(Constraints);
		var result = channel2.ApplyDetailed(_ => shipped);
		result.Should().BeOfType<ApplyOutcome<Order>.Failure>();
	}

	[Test]
	public void CsCheck_total_matches_sum_of_line_subtotals()
	{
		var genLine = Gen.Select(Gen.String["SKU"], Gen.Int[1, 50], Gen.Decimal[0.01m, 100m], (sku, qty, price) => new OrderLine(sku, qty, price));

		genLine.List[1, 10].Sample(lines =>
		{
			var order = lines.Aggregate(Order.New(Guid.NewGuid()), (o, l) => o.AddLine(l));
			order.Total.Should().Be(lines.Sum(l => l.Subtotal));
		});
	}
}
