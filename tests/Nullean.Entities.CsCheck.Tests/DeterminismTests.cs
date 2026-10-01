using AwesomeAssertions;
using CsCheck;
using global::Bogus;
using Nullean.Entities.CsCheck;
using TUnit;

namespace Nullean.Entities.CsCheck.Tests;

/// <summary>Domain type used by all determinism and shrink tests.</summary>
internal sealed class Order
{
	public Order(Guid id) => Id = id;
	public Guid Id { get; }
	public string Email { get; set; } = "";
	public List<OrderLine> Lines { get; set; } = [];
	public decimal Total => Lines.Sum(l => l.Quantity * l.Price);
}

internal sealed class OrderLine
{
	public string Sku { get; set; } = "";
	public int Quantity { get; set; }
	public decimal Price { get; set; }
}

public class DeterminismTests
{
	// The generator shared between tests. Every draw goes through CsCheck's PCG.
	private static readonly Gen<Order> GenOrder = GenFakerExtensions.Faker<Order>(
		faker =>
			faker
				.CustomInstantiator(f => new Order(f.Random.Guid()))
				.RuleFor(o => o.Email, f => f.Internet.Email())
				.RuleFor(
					o => o.Lines,
					f =>
						f.Make(
							f.Random.Int(0, 5),
							() => new OrderLine { Sku = f.Commerce.Ean13(), Quantity = f.Random.Int(1, 20), Price = f.Finance.Amount(), }
						)
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
	public void TotalEqualsLineSum() => GenOrder.Sample(order => order.Total.Should().Be(order.Lines.Sum(l => l.Quantity * l.Price)));

	/// <summary>
	/// A property that always fails should give a seed in the exception message.
	/// </summary>
	[Test]
	public void FailingPropertyThrowsWithSeed()
	{
		var threw = false;
		try
		{
			GenOrder.Sample(order => order.Lines.Count.Should().Be(-1, "always fails"));
		}
		catch (Exception ex) when (ex.Message.Contains("CsCheck_Seed"))
		{
			threw = true;
		}

		threw.Should().BeTrue("CsCheck must include CsCheck_Seed in the failure message");
	}

	/// <summary>
	/// <c>f.IndexFaker</c>/<c>f.IndexVariable</c> are public Bogus counters, independent of
	/// <see cref="Randomizer"/>, that increment on every <c>Generate()</c> call against the same
	/// <see cref="global::Bogus.Faker{T}"/> instance — which is exactly what
	/// <see cref="GenFakerExtensions.Faker{T}"/> reuses across every draw on a thread. Left alone,
	/// a rule reading either one would drift across repeated draws from the identical PCG state,
	/// silently breaking the "same seed in, same value out" guarantee for any config that happens
	/// to use them — a well-known Bogus idiom for "the Nth record" that has nothing to do with the
	/// generator's own seed. <see cref="FakerGen{T}"/> resets both to zero before every draw so
	/// they can never do this; this test is what would fail if that reset were ever removed.
	/// </summary>
	[Test]
	public void IndexFakerAndIndexVariableDoNotDriftAcrossRepeatedDrawsFromTheSameThread()
	{
		var genWithIndex = GenFakerExtensions.Faker<IndexedRecord>(
			faker => faker.RuleFor(r => r.IndexFaker, f => f.IndexFaker).RuleFor(r => r.IndexVariable, f => f.IndexVariable)
		);

		// Same PCG state, generated twice with unrelated draws from different PCG states (the same
		// thread's Faker<T> instance, reused, exactly as it is while CsCheck is shrinking) run in
		// between — the two calls at the *original* state must still agree, which is precisely
		// what would fail if the reset were removed and IndexFaker/IndexVariable were left to drift
		// with every intervening call regardless of PCG state. (Bogus increments both at the start
		// of Generate(), so the stable value isn't literally zero — it's whatever a single
		// reset-then-increment always lands on, the same every time.)
		var a = genWithIndex.Generate(new PCG(1u, 1UL), null, out _);
		genWithIndex.Generate(new PCG(7u, 99UL), null, out _);
		genWithIndex.Generate(new PCG(3u, 50UL), null, out _);
		var b = genWithIndex.Generate(new PCG(1u, 1UL), null, out _);

		a.IndexFaker.Should().Be(b.IndexFaker);
		a.IndexVariable.Should().Be(b.IndexVariable);
	}

	private sealed class IndexedRecord
	{
		public int IndexFaker { get; set; }
		public int IndexVariable { get; set; }
	}
}
