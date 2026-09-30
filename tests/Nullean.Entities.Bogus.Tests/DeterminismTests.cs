using AwesomeAssertions;
using CsCheck;
using global::Bogus;
using Nullean.Entities.Bogus;
using TUnit;

namespace Nullean.Entities.Bogus.Tests;

/// <summary>Domain type used by all determinism and shrink tests.</summary>
sealed class Order
{
	public Order(Guid id) => Id = id;
	public Guid Id { get; }
	public string Email { get; set; } = "";
	public List<OrderLine> Lines { get; set; } = [];
	public decimal Total => Lines.Sum(l => l.Quantity * l.Price);
}

sealed class OrderLine
{
	public string Sku { get; set; } = "";
	public int Quantity { get; set; }
	public decimal Price { get; set; }
}

public class DeterminismTests
{
	// The generator shared between tests. Every draw goes through CsCheck's PCG.
	private static readonly Gen<Order> GenOrder = GenFakerExtensions.Faker<Order>(
		faker => faker
			.CustomInstantiator(f => new Order(f.Random.Guid()))
			.RuleFor(o => o.Email, f => f.Internet.Email())
			.RuleFor(
				o => o.Lines,
				f => f.Make(f.Random.Int(0, 5), () => new OrderLine { Sku = f.Commerce.Ean13(), Quantity = f.Random.Int(1, 20), Price = f.Finance.Amount(), })
			)
	);

	/// <summary>
    /// The same seed must produce the identical object on every run.
    /// If this test fails, the reproducibility guarantee is broken.
    /// </summary>
	[Test]
	public void SameSeedProducesIdenticalObject()
	{
		// Construct two PCGs with the same stream and seed: identical state means identical output.
		var pcg1 = new PCG(42u, 12345UL);
		var pcg2 = new PCG(42u, 12345UL);

		var a = GenOrder.Generate(pcg1, null, out var sizeA);
		var b = GenOrder.Generate(pcg2, null, out var sizeB);

		a.Email.Should().Be(b.Email);
		a.Lines.Count.Should().Be(b.Lines.Count);
		for (var i = 0; i < a.Lines.Count; i++)
		{
			a.Lines[i].Sku.Should().Be(b.Lines[i].Sku);
			a.Lines[i].Quantity.Should().Be(b.Lines[i].Quantity);
			a.Lines[i].Price.Should().Be(b.Lines[i].Price);
		}
		sizeA.I.Should().Be(sizeB.I);
	}

	/// <summary>
    /// A property that holds for all orders must not fail when the generator is used.
    /// </summary>
	[Test]
	public void TotalEqualsLineSum()
	{
		GenOrder.Sample(order =>
		{
			order.Total.Should().Be(order.Lines.Sum(l => l.Quantity * l.Price));
		});
	}

	/// <summary>
    /// A property that always fails should give a seed in the exception message.
    /// </summary>
	[Test]
	public void FailingPropertyThrowsWithSeed()
	{
		var threw = false;
		try
		{
			GenOrder.Sample(order =>
			{
				order.Lines.Count.Should().Be(-1, "always fails");
			});
		}
		catch (Exception ex) when (ex.Message.Contains("CsCheck_Seed"))
		{
			threw = true;
		}

		threw.Should().BeTrue("CsCheck must include CsCheck_Seed in the failure message");
	}
}
