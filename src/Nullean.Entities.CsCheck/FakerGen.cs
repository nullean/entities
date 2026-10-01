// Copyright Nullean and contributors
// Licensed under the MIT License.

using Bogus;
using CsCheck;

namespace Nullean.Entities.CsCheck;

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
/// <remarks>
/// Routing <see cref="Randomizer"/> through the <see cref="PCG"/> is not the whole story:
/// <see cref="Faker.IndexFaker"/>/<see cref="Faker.IndexVariable"/> are separate public counters,
/// incremented on every <c>Generate()</c> call, and completely independent of the <see cref="Random"/>
/// source — a rule using <c>f.IndexFaker</c> would otherwise drift across calls regardless of the
/// PCG state, since the same <see cref="Faker{T}"/> instance is reused for every draw on a thread
/// (see <see cref="GenFakerExtensions.Faker{T}"/>'s own remarks). Resetting both to zero before
/// every <c>Generate()</c> makes them a pure function of nothing but "this is one top-level
/// generate call," restoring the "same PCG state in, same value out" guarantee — at the cost of
/// <c>f.IndexFaker</c> no longer being useful for the sequential/round-robin idiom it's sometimes
/// reached for elsewhere, which is inherently incompatible with shrink-and-replay in the first
/// place (shrinking retries different draws out of call order, not sequence position).
/// <see cref="Faker.IndexGlobal"/>/<see cref="Faker.UniqueIndex"/>/<see cref="Faker.GlobalUniqueIndex"/>
/// remain unaddressed: they are process-wide statics Bogus itself owns, shared with every other
/// <see cref="Faker"/> in the process, so resetting them here would corrupt unrelated code's state
/// instead of fixing this generator's — a configure that depends on them stays non-reproducible.
/// </remarks>
/// <typeparam name="T">The type to generate.</typeparam>
internal sealed class FakerGen<T>(Func<Faker<T>, Faker<T>> configure) : Gen<T>, IDisposable where T : class
{
	private readonly ThreadLocal<(Faker<T> Faker, Faker Hub, PcgRandomizer Randomizer)> _local = new(() =>
	{
		var random = new PcgRandom();
		var randomizer = new PcgRandomizer(random);
		var hub = new HubFaker<T>();
		hub.Hub.Random = randomizer;
		var faker = configure(hub);
		// Captured before `configure` runs so this is the hub Faker regardless of whether
		// `configure` happens to return the same `hub` instance back (Bogus's fluent API always
		// does, but nothing in the delegate's signature guarantees it) — IndexFaker/IndexVariable
		// live on the hub either way, since that's what `f` resolves to inside every RuleFor.
		return (faker, hub.Hub, randomizer);
	});

	/// <inheritdoc/>
	public override T Generate(PCG pcg, Size? min, out Size size)
	{
		var (faker, hub, randomizer) = _local.Value;
		randomizer.PcgRandom.Bind(pcg);
		hub.IndexFaker = 0;
		hub.IndexVariable = 0;
		var value = faker.Generate();
		size = randomizer.PcgRandom.Size;
		return value;
	}

	public void Dispose() => _local.Dispose();
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
	/// pure: do not capture mutable state that you modify on each call. <c>f.IndexFaker</c>/
	/// <c>f.IndexVariable</c> are reset to zero before every draw for the same reason — see
	/// <see cref="FakerGen{T}"/>'s own remarks — but <c>f.IndexGlobal</c>/<c>f.UniqueIndex</c> are
	/// process-wide state Bogus itself owns and are not: avoid them in <paramref name="configure"/>
	/// if you need this generator's seed-replay/shrink guarantees to hold.
	/// </remarks>
	/// <typeparam name="T">The type to generate.</typeparam>
	/// <param name="configure">
	/// Configures the <see cref="Faker{T}"/>. Receives a fresh faker with a PCG-backed
	/// <see cref="Randomizer"/> already wired up; return the configured faker.
	/// </param>
	public static Gen<T> Faker<T>(Func<Faker<T>, Faker<T>> configure) where T : class => new FakerGen<T>(configure);
}
