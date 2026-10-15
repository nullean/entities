#pragma warning disable CS0618 // Laws is obsolete: this file tests it or demonstrates it; the examples move to AutoTest with the docs rewrite
using AwesomeAssertions;
using CsCheck;
using Inventory;
using Nullean.Entities;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.Apps.Tests.Inventory;

// Inventory: the service by example, then the rules against generated data. The generator needs to be told which members refer to which (a
// stock level's SKU names a product in the other aggregate); once it is, it produces dangling references on purpose and every rule has something to catch.

public class StockServiceTests
{
	private static readonly string[] Skus = ["APL-1", "PER-1", "GHO-9"];

	private static async Task<StockService> Stocked()
	{
		var service = new StockService(new InMemoryStockRepository());
		await service.AddProductAsync(new Product("APL-1", "Apple", 0.50m));
		await service.AddProductAsync(new Product("PER-1", "Pear", 0.75m));
		return service;
	}

	[Test]
	public async Task Stock_for_an_unknown_product_is_refused_by_the_cross_aggregate_rule()
	{
		var service = await Stocked();

		var result = await service.ReceiveAsync("GHO-9", 5);

		result.ViolationsOrEmpty.Should().ContainSingle().Which.ConstraintId.Should().Be("stocked-skus-are-in-the-catalogue");
	}

	[Test]
	public async Task A_reservation_cannot_exceed_what_is_on_hand()
	{
		var service = await Stocked();
		await service.ReceiveAsync("APL-1", 5);

		(await service.ReserveAsync("APL-1", 5)).IsOk.Should().BeTrue();
		var more = await service.ReserveAsync("APL-1", 1);

		more.ViolationsOrEmpty.Should().ContainSingle().Which.ConstraintId.Should().Be("Warehouse.reserved-fits-on-hand");
	}

	[Test]
	public async Task A_negative_quantity_cannot_sneak_in_as_a_correction()
	{
		var service = await Stocked();

		(await service.ReceiveAsync("APL-1", -3)).IsFail.Should().BeTrue();
	}

	[Test]
	public async Task Whatever_operations_arrive_the_stock_stays_valid()
	{
		var operation = Gen.Select(Gen.Int[0, 3], Gen.OneOfConst(Skus), Gen.Int[-2, 12]);

		await operation
			.List[0, 40]
			.SampleAsync(
				async operations =>
				{
					var repository = new InMemoryStockRepository();
					var service = new StockService(repository);
					await service.AddProductAsync(new Product("APL-1", "Apple", 0.50m));
					await service.AddProductAsync(new Product("PER-1", "Pear", 0.75m));

					foreach (var (kind, sku, qty) in operations)
					{
						var task = kind switch
						{
							0 => service.ReceiveAsync(sku, qty),
							1 => service.ReserveAsync(sku, qty),
							2 => service.ReleaseAsync(sku, qty),
							_ => service.AddProductAsync(new Product(sku, sku, qty)),
						};
						await task;
					}

					StockRules.All.Validate(await repository.LoadAsync()).Where(v => v.Severity == Severity.Error).Should().BeEmpty();
				},
				iter: 300
			);
	}

	// ── the rules against generated data ─────────────────────────────────────────

	private static Gen<StockState> Generated() =>
		Gen.OneOf(
			GenAuto.Of<StockState>(AutoMode.Mixed, Declare),
			GenAuto.Of<StockState>(AutoMode.Adversarial, Declare)
		);

	private static void Declare(AutoOptions o) =>
		o
			.Reference<StockLevel, Product, string>(l => l.Sku, p => p.Sku)
			.Identity<Product, string>(p => p.Sku)
			.Identity<StockLevel, string>(l => l.Sku);

	[Test]
	public void With_the_reference_declared_generated_data_breaks_every_rule_so_none_is_a_tautology() =>
		StockRules.All.Probe(Generated(), samples: 3000).NeverViolated.Should().BeEmpty();

	[Test]
	public void Every_rule_has_a_counter_example_and_the_valid_state_passes_them_all() =>
		StockRules
			.All
			.Laws(
				new StockState(new Catalog([new Product("A", "a", 1m)]), new Warehouse([new StockLevel("A", 5, 2)])),
				new Dictionary<string, Func<StockState, StockState>>
				{
					["Catalog.skus-are-unique"] = s => s with { Catalog = s.Catalog.Add(new Product("A", "again", 1m)) },
					["Catalog.prices-are-positive"] = s => s with { Catalog = new Catalog([new Product("A", "a", 0m)]) },
					["Warehouse.skus-are-unique"] = s => s with { Warehouse = new Warehouse([new StockLevel("A", 1, 0), new StockLevel("A", 1, 0)]) },
					["Warehouse.quantities-are-not-negative"] = s => s with { Warehouse = new Warehouse([new StockLevel("A", -1, -1)]) },
					["Warehouse.reserved-fits-on-hand"] = s => s with { Warehouse = new Warehouse([new StockLevel("A", 1, 2)]) },
					["stocked-skus-are-in-the-catalogue"] = s => s with { Warehouse = s.Warehouse.Receive("Z", 1) },
				}
			);
}
