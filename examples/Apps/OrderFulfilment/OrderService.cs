using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;

namespace OrderFulfilment;

/// <summary>What the rest of the application calls. Every operation answers with a <see cref="Result{T}"/>: a refused one is data, not an exception.</summary>
public interface IOrderService
{
	Task<Result<Order>> CreateAsync(string? customerEmail, CancellationToken cancellationToken = default);

	Task<Result<Order>> PlaceAsync(Guid id, CancellationToken cancellationToken = default);

	Task<Result<Order>> AddLineAsync(Guid id, CancellationToken cancellationToken = default);

	Task<Result<Order>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default);

	Task<Result<Order>> ShipAsync(Guid id, string paymentRef, CancellationToken cancellationToken = default);

	Task<Result<Order>> CancelAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// The service has no rules of its own. It loads the aggregate, asks the machine to fire a trigger on it (the machine, the guard and every constraint decide),
/// saves what came back and publishes what happened. There is no <c>if (order.State is ...)</c> here to forget or to get out of step with the lifecycle.
/// </summary>
public sealed class OrderService(IOrderRepository orders, IEventPublisher events, TimeProvider clock) : IOrderService
{
	// The lifecycle binding that also checks every constraint on the order a transition produces.
	private static readonly MachineBinding<Order, OrderState> Lifecycle = OrderLifecycle.Binding.Enforce(OrderConstraints.All);

	public async Task<Result<Order>> CreateAsync(string? customerEmail, CancellationToken cancellationToken = default)
	{
		var created = Email.Create(customerEmail).Map(Order.Draft);
		if (created is Result<Order>.Ok ok)
		{
			await orders.SaveAsync(ok.Value, cancellationToken).ConfigureAwait(false);
		}
		return created;
	}

	public Task<Result<Order>> PlaceAsync(Guid id, CancellationToken cancellationToken = default) => FireAsync(id, "Place", o => o, cancellationToken);

	public Task<Result<Order>> AddLineAsync(Guid id, CancellationToken cancellationToken = default) => FireAsync(id, "AddLine", o => o, cancellationToken);

	public Task<Result<Order>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default) => FireAsync(id, "Confirm", o => o, cancellationToken);

	public Task<Result<Order>> ShipAsync(Guid id, string paymentRef, CancellationToken cancellationToken = default) =>
		FireAsync(id, "Ship", o => o with { PaymentRef = paymentRef }, cancellationToken);

	public Task<Result<Order>> CancelAsync(Guid id, CancellationToken cancellationToken = default) => FireAsync(id, "Cancel", o => o, cancellationToken);

	private async Task<Result<Order>> FireAsync(Guid id, string trigger, Func<Order, Order> prepare, CancellationToken cancellationToken)
	{
		var order = await orders.FindAsync(id, cancellationToken).ConfigureAwait(false);
		if (order is null)
		{
			return new Result<Order>.Fail(default!, [Violation.Error($"Order {id} does not exist")]);
		}

		var fired = Lifecycle.FireWithEvent(prepare(order), trigger, clock);
		if (fired is not Result<(Order Entity, Transitioned<OrderState> Event)>.Ok ok)
		{
			return new Result<Order>.Fail(order, fired.ViolationsOrEmpty);
		}

		await orders.SaveAsync(ok.Value.Entity, cancellationToken).ConfigureAwait(false);
		await events.PublishAsync(new OrderTransitioned(id, ok.Value.Event, clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
		return new Result<Order>.Ok(ok.Value.Entity);
	}
}

public static class OrderFulfilmentServices
{
	/// <summary>Registers the service and its in-memory infrastructure. A real application registers its own repository and publisher instead.</summary>
	public static IServiceCollection AddOrderFulfilment(this IServiceCollection services) =>
		services
			.AddSingleton(TimeProvider.System)
			.AddSingleton<IOrderRepository, InMemoryOrderRepository>()
			.AddSingleton<InMemoryEventPublisher>()
			.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InMemoryEventPublisher>())
			.AddSingleton<IOrderService, OrderService>();
}
