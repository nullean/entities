// Nullean.Entities.CsCheck — Script<TSut> entry points that need nothing beyond CsCheck. The TimeProvider terminals are in ScriptTemporal.cs; the
// Rx scheduler ones are in the separate Nullean.Entities.CsCheck.Reactive package.

using CsCheck;

namespace Nullean.Entities.CsCheck;

public static partial class ScriptExtensions
{
	/// <summary>
	/// Confluence: two generated values <c>a</c>, <c>b</c>, applied to two fresh instances via
	/// <paramref name="path1"/> and <paramref name="path2"/> respectively, must leave the two
	/// instances <paramref name="equal"/> — the standard metamorphic-testing shape (e.g. "adding
	/// two lines in either order gives the same order"), riding CsCheck's own generate/shrink/replay
	/// rather than a hand-rolled two-instance comparison. <paramref name="script"/> itself
	/// contributes nothing here (no steps, no delays) — it only carries <typeparamref name="TSut"/>
	/// so the call reads <c>Script.For&lt;TSut&gt;().Metamorphic(...)</c> the same as every other
	/// entry point in this file.
	/// </summary>
#pragma warning disable IDE0060 // script: the receiver only carries TSut so the call reads Script.For<TSut>().Metamorphic(...); see the doc comment above.
	public static void Metamorphic<TSut, TArg>(
		this Script<TSut> script,
		Func<TSut> factory,
		Gen<TArg> gen,
		Action<TSut, TArg, TArg> path1,
		Action<TSut, TArg, TArg> path2,
		Func<TSut, TSut, bool> equal,
		long iter = -1,
		string? seed = null
	) =>
		Gen
			.Select(gen, gen, (a, b) => (a, b))
			.Select(pair => (pair.a, pair.b, A: factory(), B: factory()))
			.Sample(t => RunMetamorphic(t.A, t.B, t.a, t.b, path1, path2, equal), seed: seed, iter: iter);
#pragma warning restore IDE0060

	private static bool RunMetamorphic<TSut, TArg>(
		TSut a,
		TSut b,
		TArg x,
		TArg y,
		Action<TSut, TArg, TArg> path1,
		Action<TSut, TArg, TArg> path2,
		Func<TSut, TSut, bool> equal
	)
	{
		path1(a, x, y);
		path2(b, x, y);
		return equal(a, b);
	}
}
