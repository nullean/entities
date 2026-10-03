using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;

namespace Inventory;

public interface IStockRepository
{
	Task<StockState> LoadAsync(CancellationToken cancellationToken = default);

	Task SaveAsync(StockState state, CancellationToken cancellationToken = default);
}

public sealed class InMemoryStockRepository : IStockRepository
{
	private StockState _state = new(new Catalog([]), new Warehouse([]));

	public Task<StockState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Volatile.Read(ref _state));

	public Task SaveAsync(StockState state, CancellationToken cancellationToken = default)
	{
		Volatile.Write(ref _state, state);
		return Task.CompletedTask;
	}
}

/// <summary>
/// Every operation is a mutation of the pair, checked against the rules of the pair. The service never looks a SKU up, never compares a quantity:
/// the rules are the only place those facts are written down, so there is no second copy to drift.
/// </summary>
public sealed class StockService(IStockRepository repository)
{
	public Task<Result<StockState>> AddProductAsync(Product product, CancellationToken cancellationToken = default) =>
		ApplyAsync(s => s with { Catalog = s.Catalog.Add(product) }, cancellationToken);

	public Task<Result<StockState>> ReceiveAsync(string sku, int qty, CancellationToken cancellationToken = default) =>
		ApplyAsync(s => s with { Warehouse = s.Warehouse.Receive(sku, qty) }, cancellationToken);

	public Task<Result<StockState>> ReserveAsync(string sku, int qty, CancellationToken cancellationToken = default) =>
		ApplyAsync(s => s with { Warehouse = s.Warehouse.Reserve(sku, qty) }, cancellationToken);

	public Task<Result<StockState>> ReleaseAsync(string sku, int qty, CancellationToken cancellationToken = default) =>
		ApplyAsync(s => s with { Warehouse = s.Warehouse.Release(sku, qty) }, cancellationToken);

	private async Task<Result<StockState>> ApplyAsync(Func<StockState, StockState> change, CancellationToken cancellationToken)
	{
		var state = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
		var result = StockRules.All.Apply(state, change);
		if (result is Result<StockState>.Ok ok)
		{
			await repository.SaveAsync(ok.Value, cancellationToken).ConfigureAwait(false);
		}
		return result;
	}
}

public static class InventoryServices
{
	public static IServiceCollection AddInventory(this IServiceCollection services) =>
		services
			.AddSingleton<IStockRepository, InMemoryStockRepository>()
			.AddSingleton<StockService>();
}
