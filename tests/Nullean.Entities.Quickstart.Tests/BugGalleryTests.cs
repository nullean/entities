using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Quickstart;

namespace Nullean.Entities.Quickstart.Tests;

internal abstract record FulfilmentEvent
{
	public sealed record Confirmed : FulfilmentEvent;

	public sealed record Shipped : FulfilmentEvent;

	public sealed record Cancelled : FulfilmentEvent;
}

// A service that confirms an order and ships it later. The clock is the one it is given, so a test controls time.
internal sealed class Fulfilment(TimeProvider time, TimeSpan shipsAfter, bool shipsCancelledOrders = false, bool forgetsToShip = false)
{
	private readonly List<Timestamped<FulfilmentEvent>> _log = [];
	private DateTimeOffset? _shipAt;
	private bool _cancelled;
	private bool _confirmed;

	public IReadOnlyList<Timestamped<FulfilmentEvent>> Log => [.. _log.OrderBy(e => e.At)];

	public void Confirm()
	{
		// confirming a cancelled or an already confirmed order does nothing
		if (_cancelled || _confirmed)
		{
			return;
		}
		_confirmed = true;
		_log.Add(new Timestamped<FulfilmentEvent>(time.GetUtcNow(), new FulfilmentEvent.Confirmed()));
		if (forgetsToShip)
		{
			return;
		}
		_shipAt = time.GetUtcNow() + shipsAfter;
		_log.Add(new Timestamped<FulfilmentEvent>(_shipAt.Value, new FulfilmentEvent.Shipped()));
	}

	public void Cancel()
	{
		_cancelled = true;
		_log.Add(new Timestamped<FulfilmentEvent>(time.GetUtcNow(), new FulfilmentEvent.Cancelled()));
		if (!shipsCancelledOrders && _shipAt is { } planned && planned > time.GetUtcNow())
		{
			_log.RemoveAll(e => e.Event is FulfilmentEvent.Shipped && e.At == planned);
		}
	}
}

// Each test plants one bug and shows the axis that catches it. The failure text is what a reader of the docs sees: with NULLEAN_WRITE_OUTPUTS=1 it is written to docs/_snippets.
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
		File.WriteAllText(Path.Combine(root!.FullName, "docs", "_snippets", name + ".txt"), text.Trim() + "\n");
	}

	private static async Task<string> FailAsync(string name, Func<Task> run)
	{
		var message = "";
		try
		{
			await run().ConfigureAwait(false);
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
	public Task The_good_order_passes_every_axis() => AutoTest.For(OrderConstraints.Instance, Orders).RunAsync();

	[Test]
	public async Task A_generator_that_never_confirms_an_order_is_caught_as_vacuous() =>
		(await FailAsync("vacancy", () => AutoTest.For(OrderConstraints.Instance, PendingOnly).Cases.Single(c => c.Axis == Axis.Vacancy).RunAsync()))
			.Should()
			.Contain("confirmed-orders-have-lines");

	[Test]
	public async Task An_operation_that_lets_a_negative_price_in_is_caught_by_the_invariants() =>
		(await FailAsync(
			"invariant",
			() =>
				AutoTest
					.For(OrderConstraints.Instance, Orders)
					.Action("AddRawLine", o => o with { Lines = [.. o.Lines, new OrderLine("fig", 1, -5)] })
					.InvariantAsync()
		))
			.Should().Contain("AddRawLine").And.Contain("total-never-negative");

	[Test]
	public async Task A_cancel_that_changes_the_order_every_time_is_caught_as_not_idempotent() =>
		(await FailAsync(
			"idempotency",
			() =>
				ConstraintSet.From(("cancel-is-repeatable", Constraints<Order>.Idempotent(o => o.Cancel() with { Version = o.Version + 1 }))).IdempotencyAsync(
					Orders
				)
		))
			.Should().Contain("cancel-is-repeatable");

	[Test]
	public async Task An_operation_that_resets_the_version_is_caught_as_going_backwards() =>
		(await FailAsync(
			"monotonicity",
			() =>
				AutoTest
					.For(OrderConstraints.Instance, Orders)
					.Action("Reset", o => o with { Version = 0 })
					.Cases
					.Single(c => c.Axis == Axis.Monotonicity)
					.RunAsync()
		))
			.Should().Contain("Reset").And.Contain("version-never-goes-back");

	[Test]
	public async Task A_result_that_depends_on_the_order_lines_were_added_in_is_caught_as_order_dependent() =>
		(await FailAsync(
			"confluence",
			() =>
				ConstraintSet.From(
					("first-sku-is-the-label", Constraints<Order>.OrderIndependent(
						o => o.Lines,
						(o, lines) => o with { Lines = lines },
						o => o.Lines.Count == 0 ? "" : o.Lines[0].Sku
					))
				).ConfluenceAsync(Orders)
		))
			.Should().Contain("first-sku-is-the-label");

	private static TemporalRuleSet<FulfilmentEvent> ShipsInAMinute() =>
		TemporalRuleSet<FulfilmentEvent>.From(
			("confirmed-ships-within-a-minute",
			Temporal<FulfilmentEvent>.AfterUntil(
				e => e is FulfilmentEvent.Confirmed,
				(_, next) => next is FulfilmentEvent.Shipped,
				TimeSpan.FromSeconds(60),
				isInterrupt: e => e is FulfilmentEvent.Cancelled
			))
		);

	private static TemporalRuleSet<FulfilmentEvent> NeverShipsAfterCancel() =>
		TemporalRuleSet<FulfilmentEvent>.From(
			("cancelled-orders-are-never-shipped", Temporal<FulfilmentEvent>.Never<FulfilmentEvent.Cancelled, FulfilmentEvent.Shipped>((_, _) => true))
		);

	private static TemporalRuleSet<FulfilmentEvent> EveryConfirmedOrderShips() =>
		TemporalRuleSet<FulfilmentEvent>.From(
			("every-confirmed-order-ships",
			Temporal<FulfilmentEvent>.Eventually(
				_ => "order",
				e => e is FulfilmentEvent.Confirmed,
				e => e is FulfilmentEvent.Shipped or FulfilmentEvent.Cancelled
			))
		);

	private static EventAutoTest<Fulfilment, FulfilmentEvent> Service(
		TemporalRuleSet<FulfilmentEvent> rules,
		TimeSpan shipsAfter,
		bool shipsCancelledOrders = false,
		bool forgetsToShip = false
	) =>
		EventAutoTest.For(
			rules,
			Script
				.For<Fulfilment>()
				.Step("Confirm", f => f.Confirm())
				.Step("Cancel", f => f.Cancel())
				.Length(1, 4)
				.Delays(Gen.Int[0, 120].Select(s => TimeSpan.FromSeconds(s))),
			time => new Fulfilment(time, shipsAfter, shipsCancelledOrders, forgetsToShip),
			f => f.Log
		);

	[Test]
	public async Task A_service_that_ships_too_late_is_caught_by_the_deadline_rule() =>
		(await FailAsync("temporal", () => Service(ShipsInAMinute(), TimeSpan.FromSeconds(90)).Cases.Single(c => c.Axis == Axis.Temporal).RunAsync()))
			.Should()
			.Contain("confirmed-ships-within-a-minute");

	[Test]
	public async Task A_service_that_ships_an_order_it_cancelled_is_caught_as_a_forbidden_sequence() =>
		(await FailAsync(
			"absence",
			() => Service(NeverShipsAfterCancel(), TimeSpan.FromSeconds(30), shipsCancelledOrders: true).Cases.Single(c => c.Axis == Axis.Absence).RunAsync()
		))
			.Should().Contain("cancelled-orders-are-never-shipped");

	[Test]
	public async Task A_service_that_forgets_to_ship_a_confirmed_order_is_caught_as_never_finishing() =>
		(await FailAsync(
			"liveness",
			() => Service(EveryConfirmedOrderShips(), TimeSpan.FromSeconds(30), forgetsToShip: true).Cases.Single(c => c.Axis == Axis.Liveness).RunAsync()
		))
			.Should().Contain("every-confirmed-order-ships");

	[Test]
	public async Task A_service_that_does_everything_right_passes_every_event_axis()
	{
		await Service(ShipsInAMinute(), TimeSpan.FromSeconds(30)).RunAsync();
		await Service(NeverShipsAfterCancel(), TimeSpan.FromSeconds(30)).RunAsync();
		await Service(EveryConfirmedOrderShips(), TimeSpan.FromSeconds(30)).RunAsync();
	}

	private static readonly Gen<Ledger> Ledgers = Gen.Select(Gen.Int[0, 50], Gen.Int[0, 50], (checking, savings) => new Ledger(checking, savings));

	[Test]
	public async Task A_transfer_that_takes_a_fee_is_caught_by_the_conservation_rule() =>
		(await FailAsync(
			"conservation",
			() =>
				AutoTest
					.For(LedgerConstraints.Instance, Ledgers)
					.Action<int>(
						"MoveWithFee",
						(l, amount) => amount > 0 && amount < l.Checking ? l with { Checking = l.Checking - amount - 1, Savings = l.Savings + amount } : l
					)
					.Cases.Single(c => c.Axis == Axis.Conservation).RunAsync()
		))
			.Should().Contain("money-is-conserved");

	[Test]
	public Task The_good_ledger_passes_every_axis() => AutoTest.For(LedgerConstraints.Instance, Ledgers).RunAsync();
}
