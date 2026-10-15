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
public readonly record struct ScriptStep<TSut>(string Name, Action<TSut>? Act, Func<TSut, Task>? ActAsync = null)
{
	/// <summary>Runs the step: a synchronous one completes at once, an asynchronous one is awaited (never blocked on).</summary>
	public Task RunAsync(TSut sut)
	{
		if (Act is { } sync)
		{
			sync(sut);
			return Task.CompletedTask;
		}
		return ActAsync!(sut);
	}

	/// <summary>The synchronous action, for a terminal that cannot await (the Rx scheduler ones). An asynchronous step is a clear error there.</summary>
	public Action<TSut> RequireSync() =>
		Act ?? throw new NotSupportedException(
			$"Step '{Name}' is asynchronous. The Rx scheduler terminals run synchronous steps only: use the TimeProvider terminals (SampleWithAsync, SampleStateAsync, EventAutoTest)."
		);
}

public sealed class Script<TSut>
{
	private readonly List<Gen<ScriptStep<TSut>>> _steps = [];
	private Gen<TimeSpan>? _delays;
	private int _minLength;
	private int _maxLength = 10;

	internal Script() { }

	// What a terminal reads to build the actual Gen<T>: public so a terminal in another package (Nullean.Entities.CsCheck.Reactive, or your own) can be written.
	/// <summary>The steps declared so far, as generators of a named action.</summary>
	public IReadOnlyList<Gen<ScriptStep<TSut>>> Steps => _steps;

	internal bool HasDelays => _delays is not null;

	/// <summary>The delay generator, or a constant zero when none was set.</summary>
	public Gen<TimeSpan> Delay => _delays ?? Gen.Const(TimeSpan.Zero);

	/// <summary>The fewest steps a generated run may contain.</summary>
	public int MinLength => _minLength;

	/// <summary>The most steps a generated run may contain.</summary>
	public int MaxLength => _maxLength;

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
	/// <see cref="Step(string, Action{TSut})"/> for a service whose operation is async. The step is awaited, never blocked on, so the run stays deterministic as long as the awaits complete on their own: use it
	/// with services whose awaits complete on their own (in-memory fakes, a <see cref="ManualTimeProvider"/>), not with ones that need real I/O.
	/// </summary>
	public Script<TSut> StepAsync(string name, Func<TSut, Task> action)
	{
		_steps.Add(Gen.Const(new ScriptStep<TSut>(name, null, action)));
		return this;
	}

	/// <summary><see cref="Step{TArg}(string, Gen{TArg}, Action{TSut, TArg})"/> for an async operation.</summary>
	public Script<TSut> StepAsync<TArg>(string name, Gen<TArg> gen, Func<TSut, TArg, Task> action)
	{
		_steps.Add(gen.Select(arg => new ScriptStep<TSut>($"{name}({arg})", null, sut => action(sut, arg))));
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
