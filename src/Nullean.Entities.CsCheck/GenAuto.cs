// Copyright Nullean and contributors
// Licensed under the MIT License.

using CsCheck;
using Soenneker.Utils.AutoBogus;

namespace Nullean.Entities.CsCheck;

/// <summary>
/// Creates CsCheck generators backed by <c>Soenneker.Utils.AutoBogus</c>
/// (<see cref="AutoFaker"/>), which fills all public properties and fields automatically.
/// </summary>
public static class GenAuto
{
	/// <summary>
	/// Creates a <see cref="Gen{T}"/> that automatically populates all members of
	/// <typeparamref name="T"/>, using Bogus locale-specific data for well-known types.
	/// </summary>
	/// <typeparam name="T">The type to generate.</typeparam>
	/// <param name="configure">
	/// Optional configuration for the <see cref="AutoFaker"/>. Called once per thread on
	/// first use; use it to add overrides and skip rules.
	/// </param>
	public static Gen<T> Of<T>(Action<AutoFaker>? configure = null) => new AutoFakerGen<T>(configure);
}

/// <summary>
/// A <see cref="Gen{T}"/> backed by <see cref="AutoFaker"/>.
/// </summary>
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
		var value = autoFaker.Generate<T>();
		size = randomizer.PcgRandom.Size;
		return value;
	}

	public void Dispose() => _local.Dispose();
}
