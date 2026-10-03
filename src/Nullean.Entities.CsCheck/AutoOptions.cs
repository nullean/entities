// Nullean.Entities.CsCheck — configuration for GenAuto.Of<T>().

using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>How hostile the generated data is.</summary>
public enum AutoMode
{
	/// <summary>
	/// The default: mostly plausible values, with about one leaf in four replaced by an edge value
	/// (empty and whitespace strings, boundary numbers, null, empty collections, ...). Excludes the
	/// <see cref="AutoMode.Adversarial"/>-only "hostile" values that break serialisers on purpose.
	/// </summary>
	Mixed,

	/// <summary>Only plausible values: words, names, small positive numbers. What a demo would show.</summary>
	Plausible,

	/// <summary>Only edge values, including the hostile group (NUL, lone surrogates), and relational cases (Start == End, duplicate ids) at a high rate.</summary>
	Adversarial,
}

/// <summary>Options for <see cref="GenAuto.Of{T}(AutoMode, Action{AutoOptions})"/>.</summary>
public sealed class AutoOptions
{
	internal readonly Dictionary<Type, Gen<object?>> Custom = [];

	/// <summary>The mode, set by <c>GenAuto.Of&lt;T&gt;(mode)</c>.</summary>
	public AutoMode Mode { get; internal set; } = AutoMode.Mixed;

	/// <summary>Percentage (0-100) of leaves replaced by an edge value in <see cref="AutoMode.Mixed"/>. Default 25.</summary>
	public int EdgePercent { get; set; } = 25;

	/// <summary>How deep nested objects are built before a reference is left null. Default 3.</summary>
	public int MaxDepth { get; set; } = 3;

	/// <summary>The largest generated collection in <see cref="AutoMode.Plausible"/> and <see cref="AutoMode.Mixed"/>. Default 4.</summary>
	public int MaxCollection { get; set; } = 4;

	/// <summary>
	/// Generate relations between fields: equal, inverted and adjacent values for pairs such as <c>Start/End</c> and
	/// <c>Min/Max</c>, and duplicate identities across the items of a collection. Default on (a no-op in
	/// <see cref="AutoMode.Plausible"/>).
	/// </summary>
	public bool Relational { get; set; } = true;

	/// <summary>Use <paramref name="gen"/> for every <typeparamref name="T"/> instead of the built-in generator (a custom type, or one AutoGen cannot build).</summary>
	public AutoOptions Use<T>(Gen<T> gen)
	{
		Custom[typeof(T)] = gen.Select(x => (object?)x);
		return this;
	}

	/// <summary>Adds edge values for strings (placed after the built-in ones, so shrinking still prefers the simplest).</summary>
	public AutoOptions AddStringEdges(params string[] values)
	{
		ExtraStringEdges.AddRange(values);
		return this;
	}

	internal List<string> ExtraStringEdges { get; } = [];

	internal int NullPercent => Mode switch
	{
		AutoMode.Plausible => 0,
		AutoMode.Adversarial => 35,
		_ => 15,
	};
}
