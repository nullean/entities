using System.Collections.Immutable;
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// The billing consumer for real: an async handler over a database, a payment gateway and a publisher, the way it is usually written. The model
// adds the gateway as a node of its own, because a call to it is an effect a crash cannot undo.

internal sealed record Charge(int OrderId, int? IdempotencyKey);

internal sealed record GatewayState(ImmutableList<int> Charges, ImmutableSortedSet<int> Keys);

internal sealed record LedgerState(ImmutableSortedSet<int> Seen);

internal sealed class BillingDatabase
{
	public HashSet<int> Charged { get; } = [];
}

/// <summary>A running billing service: real handler, in-memory database, fake gateway and publisher that record what goes out.</summary>
internal sealed class BillingHost(BillingDatabase db, BillingHost.Bug bug = BillingHost.Bug.None)
{
	public enum Bug
	{
		None,
		NoIdempotencyKey,
		CommitsBeforeCharging,
		NoCrashPoint
	}

	private readonly List<object> _sent = [];

	public CrashPoints Crashes { get; } = new();

	public BillingDatabase Db => db;

	public async Task Handle(object message)
	{
		if (message is not OrderPlaced placed)
		{
			return;
		}
		if (db.Charged.Contains(placed.OrderId))
		{
			return;
		}
		if (bug == Bug.CommitsBeforeCharging)
		{
			db.Charged.Add(placed.OrderId);
		}
		await ChargeAsync(placed.OrderId, bug == Bug.NoIdempotencyKey ? null : placed.OrderId);
		if (bug != Bug.CommitsBeforeCharging)
		{
			db.Charged.Add(placed.OrderId); // the commit
		}
		await PublishAsync(new Charged(placed.OrderId));
	}

	// The fake gateway: the charge leaves the process, then the process may die.
	private Task ChargeAsync(int orderId, int? key)
	{
		_sent.Add(new Charge(orderId, key));
		if (bug != Bug.NoCrashPoint)
		{
			Crashes.At("after-gateway");
		}
		return Task.CompletedTask;
	}

	private Task PublishAsync(object message)
	{
		_sent.Add(message);
		return Task.CompletedTask;
	}

	public IEnumerable<object> Drain()
	{
		var sent = _sent.ToList();
		_sent.Clear();
		return sent;
	}

	public LedgerState State() => new([.. db.Charged]);

	public BillingHost Restart() => new(db, bug);
}

internal sealed class PaymentSystem
{
	public Node<OrdersState> Orders { get; } = SystemSpec.Node("orders", new OrdersState([], []));

	public Node<LedgerState> Billing { get; } = SystemSpec.Node("billing", new LedgerState([]));

	public Node<GatewayState> Gateway { get; } = SystemSpec.Node("gateway", new GatewayState([], []));

	public PaymentSystem(bool gatewayDeduplicates = true)
	{
		Orders.Client("Place", [1], (o, id, send) =>
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
			if (b.Seen.Contains(m.OrderId))
			{
				return b;
			}
			send(Gateway, new Charge(m.OrderId, m.OrderId));
			send(Orders, new Charged(m.OrderId));
			return b with { Seen = b.Seen.Add(m.OrderId) };
		});

		Gateway.Handles<Charge>(
			(g, c, _) => gatewayDeduplicates && c.IdempotencyKey is { } key && g.Keys.Contains(key)
				? g
				: g with { Charges = g.Charges.Add(c.OrderId), Keys = c.IdempotencyKey is { } k ? g.Keys.Add(k) : g.Keys }
		);
	}

	public ConstraintSet<SystemState> Rules =>
		ConstraintSet.From(
			("charged-at-most-once", Constraints<SystemState>.Always(
				s => Gateway.In(s).Charges.GroupBy(id => id).All(g => g.Count() == 1),
				"An order is charged at most once."
			))
		);

	/// <summary>Billing may crash right after the gateway call: by then it has sent the charge, and has not committed.</summary>
	public CrashPoint AfterGateway =>
		CrashPoint.On<OrderPlaced, LedgerState>(
			"after-gateway",
			partial: (b, m, send) =>
			{
				send(Gateway, new Charge(m.OrderId, m.OrderId));
				return b;
			},
			reaches: (b, m) => !b.Seen.Contains(m.OrderId)
		);

	public SystemSpec System(params CrashPoint[] during) => System(Network.AtLeastOnce, during);

	public SystemSpec System(Network network, params CrashPoint[] during) =>
		SystemSpec.Of(Orders, Billing, Gateway).Network(network).Crashes(Billing, b => b, during);

	public HostBinding Host(BillingHost.Bug bug = BillingHost.Bug.None) =>
		Nullean
			.Entities
			.CsCheck
			.Host
			.For(
				Billing,
				create: () => new BillingHost(new BillingDatabase(), bug),
				deliver: (h, m) => h.Handle(m),
				stateOf: h => h.State(),
				sent: h => h.Drain(),
				crash: (h, point) => h.Crashes.Arm(point),
				restart: h => h.Restart()
			);
}

public class SystemConformanceTests
{
	[Test]
	public void A_gateway_without_idempotency_charges_twice_when_billing_crashes_after_calling_it()
	{
		// No duplicates on this network, so the only way to a second charge is the crash: the charge has left, the commit has not, and the
		// redelivered OrderPlaced is charged again.
		var naive = new PaymentSystem(gatewayDeduplicates: false);
		naive.System(Network.Unordered).Spec(naive.Rules).Exhaustive().Closed.Should().BeTrue();

		var act = () => naive.System(Network.Unordered, naive.AfterGateway).Spec(naive.Rules).Exhaustive();
		var message = act.Should().Throw<CsCheckException>().Which.Message;
		message.Should().Contain("charged-at-most-once");
		message.Should().Contain("crash billing at after-gateway");
	}

	[Test]
	public void At_least_once_delivery_charges_twice_even_without_a_crash_unless_the_gateway_deduplicates()
	{
		// The broker, or an HTTP retry, delivers the charge itself twice: the shortest double charge needs no crash at all.
		var naive = new PaymentSystem(gatewayDeduplicates: false);
		var act = () => naive.System().Spec(naive.Rules).Exhaustive();
		act.Should().Throw<CsCheckException>().Which.Message.Should().Contain("duplicate billing → gateway: Charge");

		var keyed = new PaymentSystem();
		keyed.System(keyed.AfterGateway).Spec(keyed.Rules).Exhaustive().Closed.Should().BeTrue();
	}

	[Test]
	public async Task A_handler_that_passes_the_idempotency_key_conforms_on_sampled_walks()
	{
		var payments = new PaymentSystem();

		var report = await payments.System(payments.AfterGateway).ConformAsync(payments.Host(), iter: 200);

		report.Checked.Should().BeGreaterThan(0);
	}

	[Test]
	public async Task A_handler_that_passes_the_idempotency_key_conforms_on_every_transition()
	{
		var payments = new PaymentSystem();

		var report = await payments.System(payments.AfterGateway).ConformExhaustiveAsync(payments.Host());

		report.Checked.Should().BeGreaterThan(10);
	}

	[Test]
	public async Task Forgetting_the_idempotency_key_is_a_divergence_in_what_billing_sends()
	{
		var payments = new PaymentSystem();

		var act = () => payments.System(payments.AfterGateway).ConformExhaustiveAsync(payments.Host(BillingHost.Bug.NoIdempotencyKey));

		var message = (await act.Should().ThrowAsync<CsCheckException>()).Which.Message;
		message.Should().Contain("Conformance failed.");
		message.Should().Contain("billing sent [Charge { OrderId = 1, IdempotencyKey = null }");
		message.Should().Contain("the model sends [Charge { OrderId = 1, IdempotencyKey = 1 }");
	}

	[Test]
	public async Task Committing_before_the_charge_survives_a_crash_the_model_says_it_does_not()
	{
		var payments = new PaymentSystem();

		var act = () => payments.System(payments.AfterGateway).ConformExhaustiveAsync(payments.Host(BillingHost.Bug.CommitsBeforeCharging));

		var message = (await act.Should().ThrowAsync<CsCheckException>()).Which.Message;
		message.Should().Contain("crash billing at after-gateway");
		message.Should().Contain("billing holds LedgerState { Seen = [1] }, the model says LedgerState { Seen = [] }");
	}

	[Test]
	public async Task A_model_that_calls_the_handler_atomic_is_contradicted_by_the_charge_that_left_before_the_crash()
	{
		var payments = new PaymentSystem();
		var atomic = CrashPoint.On<OrderPlaced, LedgerState>("after-gateway", reaches: (b, m) => !b.Seen.Contains(m.OrderId));

		var act = () => payments.System(atomic).ConformExhaustiveAsync(payments.Host());

		var message = (await act.Should().ThrowAsync<CsCheckException>()).Which.Message;
		message.Should().Contain("billing sent [Charge { OrderId = 1, IdempotencyKey = 1 }], the model sends []");
	}

	[Test]
	public async Task A_crash_point_the_code_never_reaches_is_reported()
	{
		var payments = new PaymentSystem();

		var act = () => payments.System(payments.AfterGateway).ConformExhaustiveAsync(payments.Host(BillingHost.Bug.NoCrashPoint));

		(await act.Should().ThrowAsync<CsCheckException>()).Which.Message.Should().Contain("the code never reached that point");
	}

	[Test]
	public async Task A_sampled_divergence_is_shrunk_and_carries_a_seed()
	{
		var payments = new PaymentSystem();

		var act = () => payments.System(payments.AfterGateway).ConformAsync(payments.Host(BillingHost.Bug.NoIdempotencyKey), iter: 500);

		var message = (await act.Should().ThrowAsync<CsCheckException>()).Which.Message;
		message.Should().Contain("Conformance failed.");
		message.Should().Contain("Seed");
	}

	[Test]
	public async Task A_host_for_a_node_that_crashes_must_say_how_it_restarts()
	{
		var payments = new PaymentSystem();
		var host = Nullean.Entities.CsCheck.Host.For(payments.Billing, () => new BillingHost(new BillingDatabase()), (h, m) => h.Handle(m), h => h.State());

		var act = () => payments.System().ConformAsync(host);

		(await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("no restart");
	}
}
