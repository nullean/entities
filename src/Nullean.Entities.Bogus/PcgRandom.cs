// Copyright Nullean and contributors
// Licensed under the MIT License.

using Bogus;
using CsCheck;

namespace Nullean.Entities.Bogus;

/// <summary>
/// A <see cref="Random"/> that routes every draw through CsCheck's <see cref="PCG"/>
/// and records a <see cref="Size"/> as it goes.
/// </summary>
/// <remarks>
/// Fewer draws produce a smaller <see cref="Size.I"/> value, so CsCheck naturally
/// shrinks to smaller collections and shorter strings. Within the same draw count,
/// smaller values in normalised [0, 65535] space produce a smaller size.
///
/// Assign this to <see cref="Randomizer.localSeed"/> via <see cref="PcgRandomizer"/>.
/// </remarks>
internal sealed class PcgRandom : Random
{
	private PCG _pcg = null!;
	private ulong _draws;
	private ulong _magnitude;

	/// <summary>Bind this instance to a new <see cref="PCG"/> for the upcoming <c>Generate</c> call.</summary>
	internal void Bind(PCG pcg)
	{
		_pcg = pcg;
		_draws = 0;
		_magnitude = 0;
	}

	/// <summary>
	/// The size accumulated since the last <see cref="Bind"/>.
	/// Primary key is draw count (fewer = simpler), secondary key is total normalised magnitude.
	/// </summary>
	internal Size Size => new((_draws << 40) | Math.Min(_magnitude, (1UL << 40) - 1));

	public override int Next() => Next(0, int.MaxValue);

	public override int Next(int maxValue) => Next(0, maxValue);

	public override int Next(int minValue, int maxValue)
	{
		if (minValue == maxValue)
		{
			Record(0, 0);
			return minValue;
		}
		var range = (uint)(maxValue - minValue);
		var v = _pcg.Next(range);
		Record(v, range);
		return (int)(minValue + v);
	}

	public override long NextInt64() => NextInt64(0, long.MaxValue);

	public override long NextInt64(long maxValue) => NextInt64(0, maxValue);

	public override long NextInt64(long minValue, long maxValue)
	{
		if (minValue == maxValue)
		{
			Record(0, 0);
			return minValue;
		}
		var range = (ulong)(maxValue - minValue);
		var v = _pcg.Next64(range);
		Record(v, range);
		return minValue + (long)v;
	}

	public override float NextSingle()
	{
		var v = _pcg.Next() >> 8;
		Record(v, 1UL << 24);
		return v * (1f / (1 << 24));
	}

	public override double NextDouble()
	{
		var v = _pcg.Next64() >> 11;
		Record(v, 1UL << 53);
		return v * (1.0 / (1UL << 53));
	}

	public override void NextBytes(byte[] buffer)
	{
		for (var i = 0; i < buffer.Length; i++)
		{
			buffer[i] = (byte)Next(0, 256);
		}
	}

	public override void NextBytes(Span<byte> buffer)
	{
		for (var i = 0; i < buffer.Length; i++)
		{
			buffer[i] = (byte)Next(0, 256);
		}
	}

	protected override double Sample() =>
		// Sample() is called by some Random members as the inner source.
		// Defer to NextDouble so every draw is recorded.
		NextDouble();

	// ── Private helpers ──────────────────────────────────────────────────────

	private void Record(ulong value, ulong range)
	{
		_draws++;
		if (range != 0)
		{
			_magnitude += (ulong)((UInt128)value * 65_535 / range);
		}
	}
}

/// <summary>
/// A <see cref="Randomizer"/> whose <c>localSeed</c> is a <see cref="PcgRandom"/>,
/// routing every Bogus draw through CsCheck's PCG.
/// </summary>
/// <remarks>
/// <see cref="Randomizer"/> has no virtual members, but its <c>localSeed</c> field is
/// <c>protected</c>. Assigning it here is the only public-API-free path to a custom RNG.
/// </remarks>
internal sealed class PcgRandomizer : Randomizer
{
	internal readonly PcgRandom PcgRandom;

	internal PcgRandomizer(PcgRandom pcgRandom)
	{
		PcgRandom = pcgRandom;
		localSeed = pcgRandom;
	}
}
