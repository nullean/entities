// Nullean.Entities.CsCheck — walks of an EntitySpec handed to code that runs them somewhere else: sampled traces (shrunk by CsCheck) and, over a
// closed state space, every transition down a shortest path. SystemSpec's conformance drives real hosts with these; they are async, and CsCheck's
// Spec.Conform is not.

using CsCheck;

namespace Nullean.Entities.CsCheck;

public sealed partial class EntitySpec<T>
{
	/// <summary>Random walks from the first starting state, as the steps taken; CsCheck shrinks a failing one to fewer, simpler steps.</summary>
	internal Gen<IReadOnlyList<EntityStep<T>>> Walks(int minSteps, int maxSteps)
	{
		var start = Starts()[0];
		CheckStart(start);
		var (spec, domains) = Build(start, autoFaults: false);
		return spec.GenTrace(minSteps, maxSteps).Select(trace => (IReadOnlyList<EntityStep<T>>)[.. trace.Steps.Select(t => ToStep(t, domains))]);
	}

	/// <summary>
	/// Every transition of the reachable state space from the first starting state, each as the shortest path that reaches its source followed by the
	/// transition itself, in breadth-first order (so the first one that fails is the shortest). Needs the space to close within <paramref name="maxStates"/>.
	/// </summary>
	internal IEnumerable<IReadOnlyList<EntityStep<T>>> TransitionPaths(int maxStates)
	{
		var start = Starts()[0];
		CheckStart(start);
		var graph = ExploreGraph(start, maxStates);
		for (var s = 0; s < graph.States.Count; s++)
		{
			var prefix = new List<EntityStep<T>>();
			for (var t = s; graph.ParentStep[t] is { } step; t = graph.Parent[t])
			{
				prefix.Add(step);
			}
			prefix.Reverse();
			foreach (var edge in graph.Edges[s])
			{
				yield return [.. prefix, edge.Step];
			}
		}
	}

	/// <summary>How a state is printed in traces, for reports built outside this class.</summary>
	internal string PrintState(T state) => _print(state);
}
