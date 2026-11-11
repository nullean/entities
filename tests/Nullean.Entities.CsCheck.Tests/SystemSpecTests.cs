using System.Collections.Immutable;
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// Orders publishes OrderPlaced through an outbox; billing charges the order and answers Charged. The broker delivers at least once, so billing must
// deduplicate: an order is charged at most once however many times OrderPlaced arrives.

internal sealed record OrderPlaced(int OrderId);

internal sealed record Charged(int OrderId);

internal sealed record OrdersState(ImmutableSortedSet<int> Placed, ImmutableSortedSet<int> Paid);

internal sealed record BillingState(ImmutableSortedSet<int> Seen, ImmutableList<int> Charges);

internal sealed class OutboxShop
{
	public Node<OrdersState> Orders { get; } = SystemSpec.Node("orders", new OrdersState([], []));

	public Node<BillingState> Billing { get; } = SystemSpec.Node("billing", new BillingState([], []));

	public OutboxShop(bool deduplicate = true, int[]? orderIds = null)
	{
		Orders.Client("Place", orderIds ?? [1], (o, id, send) =>
		{
			if (o.Placed.Contains(id))
			{
				return o;
			}
			send(Billing, new OrderPlaced(id));
			return o with { Placed = o.Placed.Add(id) };
		}).Handles<Charged>((o, m, _) => o with { Paid = o.Paid.Add(m.OrderId) });

		Billing.Handles<OrderPlaced>((b, m, send) =>
		{
			if (deduplicate && b.Seen.Contains(m.OrderId))
			{
				return b;
			}
			send(Orders, new Charged(m.OrderId));
			return b with { Seen = b.Seen.Add(m.OrderId), Charges = b.Charges.Add(m.OrderId) };
		});
	}

	public ConstraintSet<SystemState> Rules =>
		ConstraintSet.From(
			("charged-at-most-once", Constraints<SystemState>.Always(
				s => Billing.In(s).Charges.GroupBy(id => id).All(g => g.Count() == 1),
				"An order is charged at most once."
			)),
			("only-placed-orders-are-paid", Constraints<SystemState>.Always(
				s => Orders.In(s).Paid.IsSubsetOf(Orders.In(s).Placed),
				"Only a placed order is paid."
			))
		);

	public SystemSpec System() => SystemSpec.Of(Orders, Billing).Network(Network.AtLeastOnce);
}

public class SystemSpecTests
{
	[Test]
	public void A_deduplicating_consumer_is_charged_once_under_at_least_once_delivery()
	{
		var shop = new OutboxShop();

		var report = shop.System().Spec(shop.Rules).Exhaustive();

		report.Closed.Should().BeTrue();
		report.States.Should().BeGreaterThan(5);
	}

	[Test]
	public void Without_deduplication_a_redelivery_charges_twice()
	{
		var shop = new OutboxShop(deduplicate: false);

		var act = () => shop.System().Spec(shop.Rules).Exhaustive();

		var message = act.Should().Throw<CsCheckException>().Which.Message;
		message.Should().Contain("charged-at-most-once");
		message.Should().Contain("duplicate orders → billing: OrderPlaced");
	}

	[Test]
	public void A_crash_that_loses_the_dedupe_record_charges_twice()
	{
		// Billing remembers what it has seen only in memory: a restart forgets it, and the broker's redelivery is charged again.
		var shop = new OutboxShop();
		var system = shop.System().Crashes(shop.Billing, durable: b => b with { Seen = [] });

		var act = () => system.Spec(shop.Rules).Exhaustive();

		var message = act.Should().Throw<CsCheckException>().Which.Message;
		message.Should().Contain("charged-at-most-once");
		message.Should().Contain("crash billing");
	}

	[Test]
	public void A_crash_that_keeps_the_dedupe_record_is_safe()
	{
		var shop = new OutboxShop();

		var report = shop.System().Crashes(shop.Billing, durable: b => b).Spec(shop.Rules).Exhaustive();

		report.Closed.Should().BeTrue();
	}

	[Test]
	public void Every_placed_order_is_paid_when_the_broker_delivers_fairly()
	{
		var shop = new OutboxShop();

		var report = shop
			.System()
			.Spec(shop.Rules)
			.LeadsTo("a-placed-order-is-paid", s => shop.Orders.In(s).Placed.Contains(1), s => shop.Orders.In(s).Paid.Contains(1))
			.Liveness();

		report.FairActions.Should().Equal("Deliver");
	}

	[Test]
	public void A_lossy_network_can_leave_an_order_unpaid_for_ever()
	{
		var shop = new OutboxShop();

		var act =
			() => shop
				.System()
				.Network(Network.Lossy)
				.Spec(shop.Rules)
				.LeadsTo("a-placed-order-is-paid", s => shop.Orders.In(s).Placed.Contains(1), s => shop.Orders.In(s).Paid.Contains(1))
				.Liveness();

		var message = act.Should().Throw<CsCheckException>().Which.Message;
		message.Should().Contain("Rule broken   a-placed-order-is-paid");
		message.Should().Contain("drop");
		message.Should().Contain("Then stops");
	}

	[Test]
	public void Symmetry_explores_interchangeable_orders_once_and_still_finds_the_bug()
	{
		static SystemState Rename(OutboxShop shop, SystemState s, Func<int, int> map) =>
			s
				.With(shop.Orders, o => new OrdersState([.. o.Placed.Select(map)], [.. o.Paid.Select(map)]))
				.With(shop.Billing, b => new BillingState([.. b.Seen.Select(map)], [.. b.Charges.Select(map)]))
				.WithMessages(
					m =>
						m switch
						{
							OrderPlaced p => new OrderPlaced(map(p.OrderId)),
							Charged c => new Charged(map(c.OrderId)),
							_ => m
						}
				);

		var plain = new OutboxShop(orderIds: [1, 2]);
		var full = plain.System().Bound(inFlight: 3).Spec(plain.Rules).Exhaustive();

		var reduced = new OutboxShop(orderIds: [1, 2]);
		var symmetric = reduced.System().Bound(inFlight: 3).Symmetric([1, 2], (s, map) => Rename(reduced, s, map)).Spec(reduced.Rules).Exhaustive();

		symmetric.States.Should().BeLessThan(full.States);

		var broken = new OutboxShop(deduplicate: false, orderIds: [1, 2]);
		var act = () => broken.System().Bound(inFlight: 3).Symmetric([1, 2], (s, map) => Rename(broken, s, map)).Spec(broken.Rules).Exhaustive();
		act.Should().Throw<CsCheckException>().Which.Message.Should().Contain("charged-at-most-once");
	}

	[Test]
	public void Liveness_refuses_to_run_under_symmetry()
	{
		var shop = new OutboxShop(orderIds: [1, 2]);

		var act =
			() => shop
				.System()
				.Symmetric([1, 2], (s, _) => s)
				.Spec(shop.Rules)
				.Eventually("all-paid", s => shop.Orders.In(s).Paid.SetEquals(shop.Orders.In(s).Placed))
				.Liveness();

		act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain(".Symmetric");
	}

	[Test]
	public void A_reliable_network_keeps_each_channel_in_order_and_an_unordered_one_does_not()
	{
		// The sender sends First then Second; the receiver records what arrives. In order, Second never arrives before First.
		static (SystemSpec System, ConstraintSet<SystemState> Rules) Pipe(Network network)
		{
			var sender = SystemSpec.Node("sender", false);
			var receiver = SystemSpec.Node("receiver", ImmutableList<string>.Empty);
			sender.Client("Send", (sent, send) =>
			{
				if (sent)
				{
					return sent;
				}
				send(receiver, "First");
				send(receiver, "Second");
				return true;
			});
			receiver.Handles<string>((got, m, _) => got.Add(m));
			var rules = ConstraintSet.From(
				("first-arrives-first", Constraints<SystemState>.Always(s => receiver.In(s) is [] or ["First", ..], "First arrives before Second."))
			);
			return (SystemSpec.Of(sender, receiver).Network(network), rules);
		}

		var (reliable, rules) = Pipe(Network.Reliable);
		reliable.Spec(rules).Exhaustive().Closed.Should().BeTrue();

		var (unordered, unorderedRules) = Pipe(Network.Unordered);
		var act = () => unordered.Spec(unorderedRules).Exhaustive();
		act.Should().Throw<CsCheckException>().Which.Message.Should().Contain("first-arrives-first");
	}

	[Test]
	public void A_message_with_no_handler_names_the_node_and_the_message()
	{
		var a = SystemSpec.Node("a", 0);
		var b = SystemSpec.Node("b", 0);
		a.Client("Ping", (n, send) =>
		{
			send(b, new Charged(1));
			return n + (n < 1 ? 1 : 0);
		});

		var act = () => SystemSpec.Of(a, b).Spec().Exhaustive(requireCoverage: false);

		act.Should().Throw<Exception>().Which.ToString().Should().Contain("Node 'b' received a Charged and has no handler for it");
	}
}
