using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Quickstart;

namespace Nullean.Entities.Quickstart.Tests;

internal abstract record FulfilmentEvent
{
	public sealed record Confirmed : FulfilmentEvent;

	public sealed record Shipped : FulfilmentEvent;
}

// A service that confirms an order and ships it later. The clock is the one it is given, so a test controls time.
internal sealed class Fulfilment(TimeProvider time, TimeSpan shipsAfter)
{
	private readonly List<Timestamped<FulfilmentEvent>> _log = [];

	public IReadOnlyList<Timestamped<FulfilmentEvent>> Log => _log;

	public void Confirm()
	{
		_log.Add(new Timestamped<FulfilmentEvent>(time.GetUtcNow(), new FulfilmentEvent.Confirmed()));
		var shippedAt = time.GetUtcNow() + shipsAfter;
		_log.Add(new Timestamped<FulfilmentEvent>(shippedAt, new FulfilmentEvent.Shipped()));
	}
}

// Each test plants one bug and shows the axis that catches it. The failure text is what a reader of the docs sees: with NULLEAN_WRITE_OUTPUTS=1 it is written to docs/outputs.
public class BugGalleryTests
{
	private static readonly Gen<OrderLine> Lines = Gen.Select(
		Gen.OneOfConst("apple", "pear", "fig"),
		Gen.Int[1, 5],
		Gen.Int[1, 20],
		(sku, quantity, price) => new OrderLine(sku, quantity, price)
	);

	private static readonly Gen<Order> PendingOnly = Lines.Array[0, 3].Select(lines => lines.Aggregate(Order.New(), (order, line) => order.AddLine(line)));

	// Orders in every state, so every rule has something to attack.
	private static readonly Gen<Order> Orders = Gen.Select(PendingOnly, Gen.Int[0, 3], (order, step) =>
			step switch
			{
				1 => order.Confirm(),
				2 => order.Confirm().Ship(),
				3 => order.Cancel(),
				_ => order
			});

	private static void Record(string name, string text)
	{
		if (Environment.GetEnvironmentVariable("NULLEAN_WRITE_OUTPUTS") != "1")
		{
			return;
		}
		var root = new DirectoryInfo(AppContext.BaseDirectory);
		while (root is not null && !File.Exists(Path.Combine(root.FullName, "nullean-entities.slnx")))
		{
			root = root.Parent;
		}
		File.WriteAllText(Path.Combine(root!.FullName, "docs", "outputs", name + ".txt"), text.Trim() + "\n");
	}

	private static string Fail(string name, Action run)
	{
		var message = "";
		try
		{
			run();
		}
		catch (Exception e)
		{
			message = e.Message;
		}
		message.Should().NotBeEmpty($"the {name} bug should be caught");
		Record(name, message);
		return message;
	}

	[Test]
	public void The_good_order_passes_every_axis() => AutoTest.For(OrderConstraints.Instance, Orders).Run();

	[Test]
	public void A_generator_that_never_confirms_an_order_is_caught_as_vacuous() =>
		Fail("vacancy", () => AutoTest.For(OrderConstraints.Instance, PendingOnly).Cases.Single(c => c.Axis == Axis.Vacancy).Run())
			.Should()
			.Contain("confirmed-orders-have-lines");

	[Test]
	public void An_operation_that_lets_a_negative_price_in_is_caught_by_the_invariants() =>
		Fail(
			"invariant",
			() =>
				AutoTest
					.For(OrderConstraints.Instance, Orders)
					.Action("AddRawLine", o => o with { Lines = [.. o.Lines, new OrderLine("fig", 1, -5)] })
					.Invariant()
		)
			.Should().Contain("AddRawLine").And.Contain("total-never-negative");

	[Test]
	public void A_cancel_that_changes_the_order_every_time_is_caught_as_not_idempotent() =>
		Fail(
			"idempotency",
			() =>
				ConstraintSet.From(("cancel-is-repeatable", Constraints<Order>.Idempotent(o => o.Cancel() with { Version = o.Version + 1 }))).Idempotency(
					Orders
				)
		)
			.Should().Contain("cancel-is-repeatable");

	[Test]
	public void An_operation_that_resets_the_version_is_caught_as_going_backwards() =>
		Fail(
			"monotonicity",
			() =>
				AutoTest
					.For(OrderConstraints.Instance, Orders)
					.Action("Reset", o => o with { Version = 0 })
					.Cases
					.Single(c => c.Axis == Axis.Monotonicity)
					.Run()
		)
			.Should().Contain("Reset").And.Contain("version-never-goes-back");

	[Test]
	public void A_result_that_depends_on_the_order_lines_were_added_in_is_caught_as_order_dependent() =>
		Fail(
			"confluence",
			() =>
				ConstraintSet.From(
					("first-sku-is-the-label", Constraints<Order>.OrderIndependent(
						o => o.Lines,
						(o, lines) => o with { Lines = lines },
						o => o.Lines.Count == 0 ? "" : o.Lines[0].Sku
					))
				).Confluence(Orders)
		)
			.Should().Contain("first-sku-is-the-label");

	private static TemporalRuleSet<FulfilmentEvent> ShipsInAMinute() =>
		TemporalRuleSet<FulfilmentEvent>.From(
			("confirmed-ships-within-a-minute",
			Temporal<FulfilmentEvent>.AfterUntil(e => e is FulfilmentEvent.Confirmed, (_, next) => next is FulfilmentEvent.Shipped, TimeSpan.FromSeconds(60)))
		);

	private static EventAutoTest<Fulfilment, FulfilmentEvent> Service(TimeSpan shipsAfter) =>
		EventAutoTest.For(
			ShipsInAMinute(),
			Script.For<Fulfilment>().Step("Confirm", f => f.Confirm()).Length(1, 3),
			time => new Fulfilment(time, shipsAfter),
			f => f.Log
		);

	[Test]
	public void A_service_that_ships_too_late_is_caught_by_the_deadline_rule() =>
		Fail("temporal", () => Service(TimeSpan.FromSeconds(90)).Cases.Single(c => c.Axis == Axis.Temporal).Run())
			.Should()
			.Contain("confirmed-ships-within-a-minute");

	[Test]
	public void A_service_that_ships_in_time_passes() => Service(TimeSpan.FromSeconds(30)).Run();
}
