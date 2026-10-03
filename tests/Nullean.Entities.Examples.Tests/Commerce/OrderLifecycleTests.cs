using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
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
	public void ForAll_total_matches_sum_of_line_subtotals_for_any_order_the_actions_can_build() =>
		Constraints.ForAll(
			order => order.Total == order.Lines.Sum(l => l.Subtotal),
			gen: OrderConstraints.Instance.Spec().Data(AutoMode.Plausible).States(),
			iter: 200
		);

	[Test]
	public void AutoTest_every_reachable_order_is_valid_and_every_status_transition_is_reachable() => OrderConstraints.Instance.Spec().Exhaustive();

	[Test]
	// Plausible only: Order.Total multiplies quantity by price, which overflows decimal on extreme lines (see AUTOTEST_NOTES.md).
	public void AutoTest_every_action_keeps_a_valid_order_valid() => Constraints.Preserves(OrderConstraints.Actions, iter: 300, data: AutoMode.Plausible);
}
