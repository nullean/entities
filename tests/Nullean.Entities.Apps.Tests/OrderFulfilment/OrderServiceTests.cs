using AwesomeAssertions;
using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;
using OrderFulfilment;

namespace Nullean.Entities.Apps.Tests.OrderFulfilment;

// The OrderFulfilment service, tested three ways: by example, against its declared machine, and by the rules on the state it exposes.
// The last two run the same checks on a SloppyOrderService, to show they notice a service that is subtly wrong.

// A service with a plausible bug: Ship forgets that an order has to be confirmed first, and does not ask for a payment reference.
internal sealed class SloppyOrderService(IOrderRepository orders, IEventPublisher events, TimeProvider clock) : IOrderService
{
	private readonly OrderService _inner = new(orders, events, clock);

	public Task<Result<Order>> CreateAsync(string? customerEmail, CancellationToken cancellationToken = default) =>
		_inner.CreateAsync(customerEmail, cancellationToken);

	public Task<Result<Order>> PlaceAsync(Guid id, CancellationToken cancellationToken = default) => _inner.PlaceAsync(id, cancellationToken);

	public Task<Result<Order>> AddLineAsync(Guid id, CancellationToken cancellationToken = default) => _inner.AddLineAsync(id, cancellationToken);

	public Task<Result<Order>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default) => _inner.ConfirmAsync(id, cancellationToken);

	public Task<Result<Order>> CancelAsync(Guid id, CancellationToken cancellationToken = default) => _inner.CancelAsync(id, cancellationToken);

	public async Task<Result<Order>> ShipAsync(Guid id, string paymentRef, CancellationToken cancellationToken = default)
	{
		var order = (await orders.FindAsync(id, cancellationToken))!;
		if (order.State is OrderState.Placed or OrderState.Confirmed)
		{
			order = order with { State = new OrderState.Shipped() };
			await orders.SaveAsync(order, cancellationToken);
		}
		return new Result<Order>.Ok(order);
	}
}

// One order in a fresh in-memory world, observable by state: what a conformance run needs from a service.
internal sealed class OrderHarness
{
	private readonly InMemoryOrderRepository _orders = new();
	private readonly IOrderService _service;
	private readonly Guid _id;

	public OrderHarness(Func<IOrderRepository, IEventPublisher, TimeProvider, IOrderService> make, TimeProvider clock)
	{
		_service = make(_orders, new InMemoryEventPublisher(), clock);
		var order = Order.Draft(Email.Create("ada@example.com").Unwrap());
		_orders.Seed(order);
		_id = order.Id;
	}

	public Order Order => _orders.Get(_id)!;

	public OrderState State => Order.State;

	public Task Drive(string trigger) => trigger switch
	{
		"Place" => _service.PlaceAsync(_id),
		"AddLine" => _service.AddLineAsync(_id),
		"Confirm" => _service.ConfirmAsync(_id),
		"Ship" => _service.ShipAsync(_id, "pay-1"),
		"Cancel" => _service.CancelAsync(_id),
		_ => throw new InvalidOperationException($"The harness has no operation for '{trigger}'"),
	};

	public static OrderHarness Correct(TimeProvider clock) => new((r, e, c) => new OrderService(r, e, c), clock);

	public static OrderHarness Sloppy(TimeProvider clock) => new((r, e, c) => new SloppyOrderService(r, e, c), clock);
}

public class OrderServiceTests
{
	// ── by example ───────────────────────────────────────────────────────────────

	[Test]
	public void An_email_is_valid_only_through_Create()
	{
		Email.Create("ada@example.com").IsOk.Should().BeTrue();
		Email.Create(" ada@example.com ").Unwrap().Value.Should().Be("ada@example.com");

		Email.Create(null).IsFail.Should().BeTrue();
		Email.Create("no-at-sign").IsFail.Should().BeTrue();
		Email.Create("ada@localhost").ViolationsOrEmpty.Should().ContainSingle().Which.Message.Should().Contain("needs a dot");
	}

	[Test]
	public async Task A_bad_email_creates_no_order()
	{
		var orders = new InMemoryOrderRepository();
		var service = new OrderService(orders, new InMemoryEventPublisher(), new ManualTimeProvider());

		var result = await service.CreateAsync("nope");

		result.IsFail.Should().BeTrue();
	}

	[Test]
	public async Task A_refused_transition_changes_nothing_and_publishes_nothing()
	{
		var orders = new InMemoryOrderRepository();
		var events = new InMemoryEventPublisher();
		var service = new OrderService(orders, events, new ManualTimeProvider());
		var order = (await service.CreateAsync("ada@example.com")).Unwrap();

		var refused = await service.ShipAsync(order.Id, "pay-1");

		refused.IsFail.Should().BeTrue();
		(await orders.FindAsync(order.Id))!.State.Should().Be(new OrderState.Draft());
		events.Published.Should().BeEmpty();
	}

	[Test]
	public async Task A_missing_order_is_a_failure_not_an_exception()
	{
		var service = new OrderService(new InMemoryOrderRepository(), new InMemoryEventPublisher(), new ManualTimeProvider());

		var result = await service.PlaceAsync(Guid.NewGuid());

		result.ViolationsOrEmpty.Should().ContainSingle().Which.Message.Should().Contain("does not exist");
	}

	[Test]
	public async Task Each_accepted_transition_is_published_with_the_time_it_happened()
	{
		var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
		var events = new InMemoryEventPublisher();
		var service = new OrderService(new InMemoryOrderRepository(), events, clock);
		var order = (await service.CreateAsync("ada@example.com")).Unwrap();

		await service.PlaceAsync(order.Id);
		clock.Advance(TimeSpan.FromMinutes(5));
		await service.AddLineAsync(order.Id);

		events.Published.Select(e => (e.Change.Trigger, e.At.Minute)).Should().Equal(("Place", 0), ("AddLine", 5));
	}

	// ── against the declared machine ─────────────────────────────────────────────

	[Test]
	public Task The_service_conforms_to_the_lifecycle_machine() =>
		OrderLifecycle.Machine.ConformsToAsync(OrderHarness.Correct, h => h.State, (h, trigger) => h.Drive(trigger), maxLength: 15, iter: 300);

	[Test]
	public async Task A_service_that_ships_an_unconfirmed_order_is_caught_by_the_machine()
	{
		var act = async () =>
			await OrderLifecycle.Machine.ConformsToAsync(OrderHarness.Sloppy, h => h.State, (h, trigger) => h.Drive(trigger), maxLength: 15, iter: 500);

		(await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("Ship").And.Contain("the trigger is refused");
	}

	// ── by the rules on the state it exposes ─────────────────────────────────────

	private static Script<OrderHarness> Steps() =>
		Script
			.For<OrderHarness>()
			.StepAsync("Place", h => h.Drive("Place"))
			.StepAsync("AddLine", h => h.Drive("AddLine"))
			.StepAsync("Confirm", h => h.Drive("Confirm"))
			.StepAsync("Ship", h => h.Drive("Ship"))
			.StepAsync("Cancel", h => h.Drive("Cancel"))
			.Length(1, 15);

	[Test]
	public Task Every_state_the_service_reaches_satisfies_every_order_rule() =>
		Steps().SampleStateAsync(OrderHarness.Correct, h => h.Order, OrderConstraints.All, iter: 300);

	[Test]
	public async Task A_service_that_ships_without_a_payment_reference_breaks_a_rule()
	{
		var act = async () => await Steps().SampleStateAsync(OrderHarness.Sloppy, h => h.Order, OrderConstraints.All, iter: 500);

		(await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("After step").And.Contain("Shipped");
	}
}
