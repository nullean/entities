// Copyright Nullean and contributors
// Licensed under the MIT License.

using CsCheck;
using Soenneker.Utils.AutoBogus;

namespace Nullean.Entities.CsCheck;

/// <summary>
/// Creates CsCheck generators for any type from its shape. <see cref="Of{T}(AutoMode, Action{AutoOptions})"/> builds
/// values itself (plausible and adversarial, lock-free); <see cref="Bogus{T}"/> uses <c>Soenneker.Utils.AutoBogus</c>.
/// </summary>
public static class GenAuto
{
	/// <summary>
	/// Creates a <see cref="Gen{T}"/> that automatically populates all members of
	/// <typeparamref name="T"/>, using Bogus locale-specific data for well-known types.
	/// </summary>
	/// <typeparam name="T">The type to generate.</typeparam>
	/// <param name="mode">How hostile the data is; see <see cref="AutoMode"/>.</param>
	/// <param name="configure">Optional tweaks: edge percentage, depth, collection size, custom generators for a type, extra string edges.</param>
	public static Gen<T> Of<T>(AutoMode mode = AutoMode.Mixed, Action<AutoOptions>? configure = null)
	{
		var options = new AutoOptions { Mode = mode };
		configure?.Invoke(options);
		return new AutoBuilder(options).For<T>();
	}

	/// <summary>
	/// The AutoBogus-backed generator (<c>Soenneker.Utils.AutoBogus</c>): plausible Bogus data only, drawn under
	/// Bogus's process-wide lock. Kept for callers that configure <see cref="AutoFaker"/> themselves; prefer
	/// <see cref="Of{T}(AutoMode, Action{AutoOptions})"/>, which is lock-free, shrinks better and includes edge cases.
	/// </summary>
	public static Gen<T> Bogus<T>(Action<AutoFaker>? configure = null) => new AutoFakerGen<T>(configure);
}

/// <summary>
/// A <see cref="Gen{T}"/> backed by <see cref="AutoFaker"/>.
/// </summary>
/// <remarks>
/// See <see cref="Nullean.Entities.CsCheck.FakerGen{T}"/>'s own remarks for why
/// <c>Faker.IndexFaker</c>/<c>IndexVariable</c> are reset to zero before every draw here too —
/// AutoFaker generates through the same <see cref="Bogus.Faker"/> hub, so it inherits the same gap
/// without the same reset.
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
