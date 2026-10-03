// Nullean.Entities.CsCheck — leaf types beyond the core scalars: IPAddress, Version, Int128/UInt128, BigInteger, Half, Rune.
// Same contract as the others: plausible values first, then an edge catalog ordered simplest first.

using System.Net;
using System.Numerics;
using System.Text;
using CsCheck;

namespace Nullean.Entities.CsCheck;

internal static class ExtraLeaves
{
	private static readonly Type[] Handled =
	[
		typeof(IPAddress),
		typeof(Version),
		typeof(Int128),
		typeof(UInt128),
		typeof(BigInteger),
		typeof(Half),
		typeof(Rune)
	];

	public static bool Handles(Type type) => Array.IndexOf(Handled, type) >= 0;

	public static Gen<object?>? For(Type type, AutoOptions options)
	{
		if (type == typeof(IPAddress))
		{
			return Leaf(options, pcg => (new IPAddress([10, (byte)pcg.Next(256), (byte)pcg.Next(256), (byte)pcg.Next(256)]), 0UL), IpEdges());
		}
		if (type == typeof(Version))
		{
			return Leaf(options, pcg => (new Version((int)pcg.Next(10), (int)pcg.Next(20), (int)pcg.Next(100)), 0UL), VersionEdges());
		}
		if (type == typeof(Int128))
		{
			return Leaf(
				options,
				pcg => ((Int128)pcg.Next(100_001), 0UL),
				[(Int128)0, (Int128)1, (Int128)(-1), Int128.MinValue, Int128.MaxValue, (Int128)long.MaxValue + 1, (Int128)ulong.MaxValue + 1]
			);
		}
		if (type == typeof(UInt128))
		{
			return Leaf(
				options,
				pcg => ((UInt128)pcg.Next(100_001), 0UL),
				[(UInt128)0, (UInt128)1, UInt128.MaxValue, (UInt128)ulong.MaxValue + 1, (UInt128)long.MaxValue + 1]
			);
		}
		if (type == typeof(BigInteger))
		{
			return Leaf(options, pcg => (new BigInteger(pcg.Next(100_001)), 0UL), BigEdges());
		}
		if (type == typeof(Half))
		{
			return Leaf(
				options,
				pcg => ((Half)(pcg.Next(100_001) / 100f), 0UL),
				[
					Half.Zero,
					Half.One,
					Half.NegativeOne,
					Half.NaN,
					Half.PositiveInfinity,
					Half.NegativeInfinity,
					Half.NegativeZero,
					Half.Epsilon,
					Half.MaxValue,
					Half.MinValue
				]
			);
		}
		if (type == typeof(Rune))
		{
			return Leaf(
				options,
				pcg => (new Rune('a' + (int)pcg.Next(26)), 0UL),
				[new Rune('a'), new Rune(0), new Rune(0x20), new Rune(0xA0), new Rune(0xFFFD), new Rune(0xFFFF), new Rune(0x1F600), new Rune(0x10FFFF)]
			);
		}
		return null;
	}

	private static Gen<object?> Leaf(AutoOptions options, Func<PCG, (object?, ulong)> plausible, object?[] edges) =>
		new LeafGen(new FuncGen(plausible), edges, options);

	private static object?[] IpEdges() =>
		[
			IPAddress.Any,
			IPAddress.Loopback,
			IPAddress.None,
			IPAddress.IPv6Any,
			IPAddress.IPv6Loopback,
			IPAddress.Parse("::ffff:1.2.3.4"),
			IPAddress.Parse("169.254.0.1"),
			IPAddress.Parse("192.0.2.1"),
			IPAddress.Parse("fe80::1"),
			IPAddress.Parse("2001:db8::ff00:42:8329"),
		];

	private static object?[] VersionEdges() =>
		[
			new Version(0, 0),
			new Version(0, 0, 0, 0),
			new Version(1, 0),
			new Version(1, 0, 0),
			new Version(0, 0, 1),
			new Version(65535, 65535, 65535, 65535),
			new Version(int.MaxValue, int.MaxValue),
			new Version(1, 2, 3, 4),
		];

	private static object?[] BigEdges() =>
		[
			BigInteger.Zero,
			BigInteger.One,
			BigInteger.MinusOne,
			new BigInteger(long.MaxValue) + 1,
			new BigInteger(long.MinValue) - 1,
			BigInteger.Pow(2, 64),
			BigInteger.Pow(2, 127),
			BigInteger.Pow(10, 40),
			-BigInteger.Pow(10, 40),
			BigInteger.Pow(2, 1024),
		];
}
