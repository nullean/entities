// Nullean.Entities.CsCheck — generates random walks over a StateMachine<TState>'s declared graph.
//
// Scope, deliberately: this walks the GRAPH (declared states and edges, by Type), not real
// TState values — a transition's guard needs an actual value to evaluate, and there is no generic
// way to generate one for an arbitrary case type here. So Walks() ignores guards exactly the way
// the four structural checks in StateMachine.cs do, for the same reason: this is a structural
// tool, not a replacement for driving a real Script<TSut> run through SampleWith and checking
// AsConstraint/AsReactive against the actual values it produces. Pairing the two — generate a walk
// here for its shape, drive the real steps through Script<TSut> for its data — is still on the
// caller; there is no automatic bridge between them yet.
//
// The generator itself is a plain Gen<int[]>.Select: CsCheck already owns generation, shrinking
// and seed replay for an int array (Script.cs's own BuildGen leans on the same primitives), so the
// walk only needs to interpret a generated array of choices against the graph — no bespoke
// generator machinery (Gen.Recursive, a custom IGen) is needed for this.
//
// No doc comments yet on most members (checkpoint-A exploration code) - CS1591 is suppressed
// project-wide in Directory.Build.props rather than per file.

using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>One step of a generated <see cref="StateMachineWalkExtensions.Walks{TState}"/> run.</summary>
public readonly record struct StateMachineStep<TState>(string Name, Type From, Type To);

public static class StateMachineWalkExtensions
{
	/// <summary>
	/// A shrinkable generator of walks through <paramref name="machine"/>'s declared graph,
	/// starting at <see cref="StateMachine{TState}.Initial"/> and following a randomly chosen
	/// outgoing edge at each step. A walk ends early (shorter than <paramref name="maxLength"/>)
	/// the moment it reaches a state with no outgoing edge — every <c>Terminal&lt;T&gt;()</c> state
	/// qualifies, so a walk reaching one simply stops rather than looping back in. Guards are
	/// ignored — see this file's header comment.
	/// </summary>
	public static Gen<IReadOnlyList<StateMachineStep<TState>>> Walks<TState>(this StateMachine<TState> machine, int minLength = 0, int maxLength = 20) =>
		Gen.Int[0, int.MaxValue].Array[minLength, maxLength].Select(choices => (IReadOnlyList<StateMachineStep<TState>>)Interpret(machine, choices));

	private static List<StateMachineStep<TState>> Interpret<TState>(StateMachine<TState> machine, int[] choices)
	{
		var steps = new List<StateMachineStep<TState>>(choices.Length);
		var current = machine.Initial;
		foreach (var choice in choices)
		{
			var outgoing = machine.Edges.Where(e => e.From == current).ToArray();
			if (outgoing.Length == 0)
			{
				break;
			}
			var edge = outgoing[(int)((uint)choice % (uint)outgoing.Length)];
			steps.Add(new StateMachineStep<TState>(edge.Name, edge.From, edge.To));
			current = edge.To;
		}
		return steps;
	}
}
