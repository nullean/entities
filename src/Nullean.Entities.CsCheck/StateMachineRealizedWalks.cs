// Nullean.Entities.CsCheck — guard-aware walks over a StateMachine<TState>, carrying real values.
//
// StateMachineWalkExtensions.Walks walks the graph by Type alone and ignores guards — there is no
// generic way to generate a real value for an arbitrary case type there. This file closes that gap
// the same way GenAuto.cs already does for "fill every field of an arbitrary T": GenAuto.Of<T>() is
// backed by AutoFaker (Soenneker.Utils.AutoBogus), which works for any T via reflection, with no
// per-type setup. The only new piece needed is calling GenAuto.Of<T>() for a case Type known only
// at runtime (StateMachine<TState>.Edges carries Type, not a generic parameter) — done once via
// reflection per case type, then cached for the rest of the walk.
//
// With a real value in hand at each step, a declared edge's Guard (a plain public
// Func<TState, bool> on StateMachineEdge<TState> — no internals needed here) can be evaluated for
// real, so a generated walk only ever takes a transition whose guard actually holds for the value
// it was generated against — the thing the type-only Walks() explicitly could not do.
//
// No doc comments yet on most members (checkpoint-A exploration code) - CS1591 is suppressed
// project-wide in Directory.Build.props rather than per file.

using System.Reflection;
using CsCheck;
using Soenneker.Utils.AutoBogus;

namespace Nullean.Entities.CsCheck;

/// <summary>One step of a generated <see cref="StateMachineRealizedWalkExtensions.RealizedWalks{TState}"/> run, carrying the real values either side of the transition.</summary>
public readonly record struct RealizedStep<TState>(string Name, TState From, TState To);

public static class StateMachineRealizedWalkExtensions
{
	/// <summary>
	/// Like <see cref="StateMachineWalkExtensions.Walks{TState}"/>, but generates a real value for
	/// every state along the way (via <see cref="GenAuto.Of{T}"/>) instead of walking the graph by
	/// type alone — so a guarded edge is only ever taken when its guard actually holds for the
	/// generated source value, and a walk that reaches a state where every live edge's guard fails
	/// stops there, the same as reaching a true dead end. <paramref name="configure"/> is forwarded
	/// to every <see cref="GenAuto.Of{T}"/> call, same meaning as there.
	/// </summary>
	public static Gen<IReadOnlyList<RealizedStep<TState>>> RealizedWalks<TState>(
		this StateMachine<TState> machine,
		int minLength = 0,
		int maxLength = 20,
		Action<AutoFaker>? configure = null
	) => new RealizedWalkGen<TState>(machine, minLength, maxLength, configure);
}

internal sealed class RealizedWalkGen<TState> : Gen<IReadOnlyList<RealizedStep<TState>>>
{
	private readonly StateMachine<TState> _machine;
	private readonly int _minLength;
	private readonly int _maxLength;
	private readonly Dictionary<Type, Gen<object>> _caseGens = [];

	public RealizedWalkGen(StateMachine<TState> machine, int minLength, int maxLength, Action<AutoFaker>? configure)
	{
		_machine = machine;
		_minLength = minLength;
		_maxLength = maxLength;
		foreach (var caseType in machine.States)
		{
			_caseGens[caseType] = BuildObjectGen(caseType, configure);
		}
	}

	// GenAuto.Of<T>() is only callable with T known at compile time; a case type here is only
	// known as a runtime Type (StateMachine<TState>.States), so this reflects once per case type
	// (cached in _caseGens, not repeated per draw) rather than once per generated value.
	private static Gen<object> BuildObjectGen(Type caseType, Action<AutoFaker>? configure)
	{
		var openMethod = typeof(RealizedWalkGen<TState>).GetMethod(nameof(BuildObjectGenOf), BindingFlags.NonPublic | BindingFlags.Static)!;
		var closedMethod = openMethod.MakeGenericMethod(caseType);
		return (Gen<object>)closedMethod.Invoke(null, [configure])!;
	}

	private static Gen<object> BuildObjectGenOf<TCase>(Action<AutoFaker>? configure) => GenAuto.Of<TCase>(configure).Select(v => (object)v!);

	public override IReadOnlyList<RealizedStep<TState>> Generate(PCG pcg, Size? min, out Size size)
	{
		var lengthGen = Gen.Int[_minLength, _maxLength];
		var length = lengthGen.Generate(pcg, min, out size);

		var steps = new List<RealizedStep<TState>>(length);
		var currentType = _machine.Initial;
		var currentValue = GenerateCase(currentType, pcg, size);

		for (var i = 0; i < length; i++)
		{
			var live = _machine.Edges.Where(e => e.From == currentType && (e.Guard is null || e.Guard(currentValue))).ToArray();
			if (live.Length == 0)
			{
				break;
			}

			var edge = live[(int)pcg.Next((uint)live.Length)];
			var nextValue = GenerateCase(edge.To, pcg, size);
			steps.Add(new RealizedStep<TState>(edge.Name, currentValue, nextValue));
			currentValue = nextValue;
			currentType = edge.To;
		}

		return steps;
	}

	private TState GenerateCase(Type caseType, PCG pcg, Size size)
	{
		var instance = _caseGens[caseType].Generate(pcg, null, out var instanceSize);
		size.Append(instanceSize);
		return _machine.Wrap(caseType, instance);
	}
}
