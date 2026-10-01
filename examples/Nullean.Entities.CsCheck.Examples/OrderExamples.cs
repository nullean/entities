// Usage examples for the Bogus-backed generators in Nullean.Entities.CsCheck. Run with: dotnet run
using AwesomeAssertions;
using CsCheck;
using global::Bogus;
using Nullean.Entities.CsCheck;
using Soenneker.Utils.AutoBogus;
using TUnit;

namespace Nullean.Entities.CsCheck.Examples;

// ── Domain ───────────────────────────────────────────────────────────────────
// Named BogusOrder/BogusOrderLine, not Order/OrderLine — OrderDomain.cs owns those names for the
// real nested Order/OrderLine/OrderEvent example these Bogus generators have nothing to do with.

internal sealed class BogusOrder
{
	public BogusOrder(Guid id) => Id = id;
	public Guid Id { get; }
	public string Email { get; set; } = "";
	public List<BogusOrderLine> Lines { get; set; } = [];
	public decimal Total => Lines.Sum(l => l.Quantity * l.Price);
}

internal sealed class BogusOrderLine
{
	public string Sku { get; set; } = "";
	public int Quantity { get; set; }
	public decimal Price { get; set; }
}

internal sealed class Customer
{
	public string FirstName { get; set; } = "";
	public string LastName { get; set; } = "";
	public string Email { get; set; } = "";
	public Address Address { get; set; } = new();
}

internal sealed class Address
{
	public string Street { get; set; } = "";
	public string City { get; set; } = "";
	public string PostalCode { get; set; } = "";
	public string Country { get; set; } = "";
}

// ── Generators ───────────────────────────────────────────────────────────────

internal static class Gens
{
	public static readonly Gen<BogusOrder> Order = GenFakerExtensions.Faker<BogusOrder>(
		faker => faker
			.CustomInstantiator(f => new BogusOrder(f.Random.Guid()))
			.RuleFor(o => o.Email, f => f.Internet.Email())
			.RuleFor(
				o => o.Lines,
				f => f.Make(
					f.Random.Int(0, 10),
					() => new BogusOrderLine { Sku = f.Commerce.Ean13(), Quantity = f.Random.Int(1, 100), Price = f.Finance.Amount(), }
				)
			)
	);

	// AutoBogus fills Address, FirstName, LastName, etc. automatically.
	// No overrides needed for this example; every field is populated by AutoBogus.
	public static readonly Gen<Customer> Customer = GenAuto.Of<Customer>();
}

// ── Examples ─────────────────────────────────────────────────────────────────

public class OrderExamples
{
	/// <summary>
	/// The total is the sum of its lines, regardless of what Bogus generates.
	/// Failing cases shrink to a minimal order (fewest lines, smallest values).
	/// </summary>
	[Test]
	public void TotalEqualsSumOfLines() => Gens.Order.Sample(order => order.Total.Should().Be(order.Lines.Sum(l => l.Quantity * l.Price)));

	/// <summary>
	/// An order with no lines has a total of zero.
	/// </summary>
	[Test]
	public void EmptyOrderHasZeroTotal() => Gens.Order.Where(o => o.Lines.Count == 0).Sample(order => order.Total.Should().Be(0));

	/// <summary>
	/// Model-based test: a repository must contain exactly the orders that were added.
	/// </summary>
	[Test]
	public void RepositoryTracksOrders()
	{
		// Initial state: empty repo + empty model.
		Gen.Const(() => (new OrderRepository(), new Dictionary<Guid, BogusOrder>())).SampleModelBased(
			// Add an order to both the real repo and the model.
			Gens.Order.Operation<OrderRepository, Dictionary<Guid, BogusOrder>>(
				o => $"Add({o.Email})",
				(repo, o) => repo.Add(o),
				(model, o) => model[o.Id] = o
			),
			// Remove by ID.
			Gen.Guid.Operation<OrderRepository, Dictionary<Guid, BogusOrder>>(
				id => $"Remove({id})",
				(repo, id) => repo.Remove(id),
				(model, id) => model.Remove(id)
			),
			equal: (repo, model) => repo.Count == model.Count && model.Keys.All(id => repo.Contains(id))
		);
	}
}

public class CustomerExamples
{
	/// <summary>AutoBogus fills every customer field.</summary>
	[Test]
	public void EmailIsNeverEmpty() => Gens.Customer.Sample(c => c.Email.Should().NotBeNullOrWhiteSpace());

	/// <summary>AutoBogus fills nested objects too.</summary>
	[Test]
	public void AddressIsNeverNull() => Gens.Customer.Sample(c => c.Address.Should().NotBeNull());
}

// ── Stub repository ───────────────────────────────────────────────────────────

internal sealed class OrderRepository
{
	private readonly Dictionary<Guid, BogusOrder> _store = [];
	public int Count => _store.Count;
	public void Add(BogusOrder o) => _store[o.Id] = o;
	public void Remove(Guid id) => _store.Remove(id);
	public bool Contains(Guid id) => _store.ContainsKey(id);
}
