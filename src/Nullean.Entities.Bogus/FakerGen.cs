// Copyright Nullean and contributors
// Licensed under the MIT License.

using Bogus;
using CsCheck;

namespace Nullean.Entities.Bogus;

// Expose FakerHub (protected internal on Faker<T>) through a public property so
// FakerGen<T> can set its Random without reflection.
internal sealed class HubFaker<T> : Faker<T> where T : class
{
	/// <summary>The internal <see cref="Faker"/> that holds the <see cref="Randomizer"/>.</summary>
	internal Faker Hub => FakerHub;
}

/// <summary>
/// A CsCheck <see cref="Gen{T}"/> backed by a Bogus <see cref="Faker{T}"/>.
/// Every draw is routed through CsCheck's <see cref="PCG"/>, so seeds are
/// reproducible and failing cases shrink through CsCheck's size mechanism.
/// </summary>
/// <typeparam name="T">The type to generate.</typeparam>
internal sealed class FakerGen<T>(Func<Faker<T>, Faker<T>> configure) : Gen<T>, IDisposable where T : class
{
	private readonly ThreadLocal<(Faker<T> Faker, PcgRandomizer Randomizer)> _local = new(() =>
	{
		var random = new PcgRandom();
		var randomizer = new PcgRandomizer(random);
		var hub = new HubFaker<T>();
		hub.Hub.Random = randomizer;
		var faker = configure(hub);
		return (faker, randomizer);
	});

	/// <inheritdoc/>
	public override T Generate(PCG pcg, Size? min, out Size size)
	{
		var (faker, randomizer) = _local.Value;
		randomizer.PcgRandom.Bind(pcg);
		var value = faker.Generate();
		size = randomizer.PcgRandom.Size;
		return value;
	}

	public void Dispose() => throw new NotImplementedException();
}

/// <summary>
/// Extension methods for creating Bogus-backed <see cref="Gen{T}"/> instances.
/// </summary>
public static class GenFakerExtensions
{
	/// <summary>
	/// Creates a <see cref="Gen{T}"/> that generates instances of <typeparamref name="T"/>
	/// using Bogus <see cref="Faker{T}"/> rules.
	/// </summary>
	/// <remarks>
	/// Seeds are reproducible and failing cases shrink because every Bogus draw goes through
	/// CsCheck's PCG. Fewer draws (smaller collections, shorter strings) produce a smaller
	/// <see cref="Size"/>, which CsCheck prefers when looking for minimal counterexamples.
	///
	/// One <see cref="Faker{T}"/> per thread is created on first use and reused for all
	/// subsequent samples on that thread. The <paramref name="configure"/> delegate must be
	/// pure: do not capture mutable state that you modify on each call.
	/// </remarks>
	/// <typeparam name="T">The type to generate.</typeparam>
	/// <param name="configure">
	/// Configures the <see cref="Faker{T}"/>. Receives a fresh faker with a PCG-backed
	/// <see cref="Randomizer"/> already wired up; return the configured faker.
	/// </param>
	public static Gen<T> Faker<T>(Func<Faker<T>, Faker<T>> configure) where T : class => new FakerGen<T>(configure);
}
