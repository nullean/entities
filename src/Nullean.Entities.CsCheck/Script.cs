// Nullean.Entities.CsCheck — Script<TSut>: a CsCheck-driven action sequence for a SUT
// whose reactions are observed as an event stream.
//
// A script is just a declaration: the set of steps that may occur, an optional delay generator
// between them, and a length range. It generates nothing and runs nothing by itself — see
// ScriptExtensions.SampleWith/RunWith, which turn a Script<TSut> into a real CsCheck Gen<T> (a
// fresh Sut + a generated, shrinkable sequence of steps) and drive it through Gen<T>.Sample. That
// is CsCheck's own generate/shrink/replay machinery, not a hand-rolled substitute for it.
//
// No doc comments yet (checkpoint-A exploration code) — CS1591 is suppressed project-wide
// in Directory.Build.props rather than per file.

using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>One occurrence of a step in a generated run — a display name plus the action to apply to the SUT.</summary>
public readonly record struct ScriptStep<TSut>(string Name, Action<TSut> Act);

public sealed class Script<TSut>
{
	private readonly List<Gen<ScriptStep<TSut>>> _steps = [];
	private Gen<TimeSpan>? _delays;
	private int _minLength;
	private int _maxLength = 10;

	internal Script() { }

	// Read by ScriptExtensions to build the actual Gen<T> — never by anything outside this
	// project, so plain `internal` rather than exposing builder state as public API.
	internal IReadOnlyList<Gen<ScriptStep<TSut>>> Steps => _steps;
	internal Gen<TimeSpan> Delay => _delays ?? Gen.Const(TimeSpan.Zero);
	internal int MinLength => _minLength;
	internal int MaxLength => _maxLength;

	/// <summary>A step with no generated argument — the same action every time it's picked.</summary>
	public Script<TSut> Step(string name, Action<TSut> action)
	{
		_steps.Add(Gen.Const(new ScriptStep<TSut>(name, action)));
		return this;
	}

	/// <summary>
	/// A step whose argument is generated (and shrunk) independently each time it's picked. The
	/// display name is derived from the argument's type and printed value — use the
	/// <see cref="Step{TArg}(string, Gen{TArg}, Action{TSut, TArg})"/> overload for an explicit name.
	/// </summary>
	public Script<TSut> Step<TArg>(Gen<TArg> gen, Action<TSut, TArg> action)
	{
		var typeName = typeof(TArg).Name;
		_steps.Add(gen.Select(arg => new ScriptStep<TSut>($"{typeName}({arg})", sut => action(sut, arg))));
		return this;
	}

	/// <summary>Like <see cref="Step{TArg}(Gen{TArg}, Action{TSut, TArg})"/>, with an explicit step name.</summary>
	public Script<TSut> Step<TArg>(string name, Gen<TArg> gen, Action<TSut, TArg> action)
	{
		_steps.Add(gen.Select(arg => new ScriptStep<TSut>($"{name}({arg})", sut => action(sut, arg))));
		return this;
	}

	/// <summary>
	/// A delay drawn before every step in a generated run — not a fixed pause. Generated delays
	/// eventually land past a real deadline (see <c>After().Then().Within()</c> in
	/// Nullean.Entities.Reactive) and shrink to the smallest gap that still violates it. Without
	/// this, every step runs at the scheduler's current virtual time with no advance between them.
	/// </summary>
	public Script<TSut> Delays(Gen<TimeSpan> delays)
	{
		_delays = delays;
		return this;
	}

	/// <summary>How many steps a generated run may contain (inclusive). Default 0–10.</summary>
	public Script<TSut> Length(int min, int max)
	{
		_minLength = min;
		_maxLength = max;
		return this;
	}
}

public static class Script
{
	public static Script<TSut> For<TSut>() => new();
}
