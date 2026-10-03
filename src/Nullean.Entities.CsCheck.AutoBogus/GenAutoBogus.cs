// Copyright Nullean and contributors
// Licensed under the MIT License.

using CsCheck;
using Soenneker.Utils.AutoBogus;

namespace Nullean.Entities.CsCheck.AutoBogus;

/// <summary>
/// A CsCheck generator backed by <c>Soenneker.Utils.AutoBogus</c> (<see cref="AutoFaker"/>), which fills every public member of a type with
/// plausible Bogus data, drawn from CsCheck's PCG so it is reproducible by seed and shrinkable.
/// </summary>
/// <remarks>
/// The default <c>GenAuto.Of&lt;T&gt;()</c> in <c>Nullean.Entities.CsCheck</c> is lock-free, shrinks better and includes edge cases; reach for this one when you
/// already configure <see cref="AutoFaker"/> (binders, overrides, skip rules) and want to keep that. Every draw takes Bogus's process-wide lock
/// (<c>Randomizer.Locker</c>, an internal static), so a multi-threaded CsCheck run does not scale. It generates plausible values only: no NaN,
/// negatives, blank strings or undefined enums. See <c>docs/autobogus-decision.md</c> for the measurements.
/// </remarks>
public static class GenAutoBogus
{
	/// <summary>
	/// A <see cref="Gen{T}"/> that populates every member of <typeparamref name="T"/> with AutoBogus.
	/// </summary>
	/// <param name="configure">Configures the <see cref="AutoFaker"/>: overrides, binders, skip rules. Runs once per thread, on first use.</param>
	public static Gen<T> Of<T>(Action<AutoFaker>? configure = null) => new AutoFakerGen<T>(configure);
}

/// <summary>
/// A <see cref="Gen{T}"/> backed by <see cref="AutoFaker"/>.
/// </summary>
/// <remarks>
/// See <see cref="FakerGen{T}"/>'s own remarks for why <c>Faker.IndexFaker</c>/<c>IndexVariable</c> are reset to zero before every draw here too:
/// AutoFaker generates through the same <see cref="Bogus.Faker"/> hub, so it inherits the same gap without the same reset.
/// </remarks>
internal sealed class AutoFakerGen<T>(Action<AutoFaker>? configure) : Gen<T>, IDisposable
{
	private readonly ThreadLocal<(AutoFaker AutoFaker, PcgRandomizer Randomizer)> _local = new(() =>
	{
		var random = new PcgRandom();
		var randomizer = new PcgRandomizer(random);
		var autoFaker = new AutoFaker();
		autoFaker.Faker.Random = randomizer;
		configure?.Invoke(autoFaker);
		return (autoFaker, randomizer);
	});

	public override T Generate(PCG pcg, Size? min, out Size size)
	{
		var (autoFaker, randomizer) = _local.Value;
		randomizer.PcgRandom.Bind(pcg);
		autoFaker.Faker.IndexFaker = 0;
		autoFaker.Faker.IndexVariable = 0;
		var value = autoFaker.Generate<T>();
		size = randomizer.PcgRandom.Size;
		return value;
	}

	public void Dispose() => _local.Dispose();
}
