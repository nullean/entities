// Nullean.Entities.CsCheck — liveness over the closed state space of an EntitySpec, under weak fairness.
//
// CsCheck's Spec<S> proves safety and bounded response (a deadline counted in steps). "Eventually", with no bound, needs the whole graph:
// a run that never reaches the goal is either one that stops (a state no action changes) or one that loops forever. Under weak fairness a
// loop only counts when it is fair: every fair action that stays enabled all the way round is taken somewhere on it. So the check is:
//
//   1. build the reachable graph, with the same actions, domains, key, boundary and limit as Exhaustive;
//   2. keep the states where the goal does not hold and split them into strongly connected components (Tarjan);
//   3. a component is a fair loop when every fair action enabled in all of its states labels one of its own edges
//      (a smaller loop inside it keeps every such action enabled and takes fewer edges, so if the whole component is not fair, nothing in it is);
//   4. a state is bad when, without reaching the goal, it can get to a fair loop or to a state that stops.
//
// LeadsTo(from, to) fails when a reachable state where `from` holds is bad for `to`. Eventually(goal) is LeadsTo(any state, goal).

using System.Globalization;
using System.Text;
using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>What <see cref="EntitySpec{T}.Liveness"/> checked: the size of the graph, the rules, and the actions assumed fair.</summary>
public sealed record LivenessReport(int States, int Transitions, IReadOnlyList<string> Rules, IReadOnlyList<string> FairActions)
{
	/// <inheritdoc />
	public override string ToString() =>
		$"{Rules.Count} liveness rule(s) held over {States} states and {Transitions} transitions"
		+ (FairActions.Count > 0 ? $", assuming {string.Join(", ", FairActions)} fair" : ", with no fairness assumed");
}

public sealed partial class EntitySpec<T>
{
	private sealed record LivenessRule(string Id, Func<T, bool>? From, Func<T, bool> To);

	private readonly List<LivenessRule> _liveness = [];
	private readonly Dictionary<string, Func<T, object?, object?>?> _fair = [];
	private bool _allFair;

	/// <summary>
	/// From every reachable state, every run reaches a state where <paramref name="goal"/> holds: it cannot stop short of it, or loop forever
	/// without it. Loops are only counted when they are fair (<see cref="Fair(string[])"/>): with no fairness declared, any loop that avoids the goal fails.
	/// Checked by <see cref="Liveness"/>, and by <see cref="Exhaustive"/> once its safety proof passes.
	/// </summary>
	public EntitySpec<T> Eventually(string id, Func<T, bool> goal)
	{
		_liveness.Add(new LivenessRule(id, null, goal));
		return this;
	}

	/// <summary>
	/// Whenever <paramref name="from"/> holds, every run from there reaches a state where <paramref name="to"/> holds ("a placed order is eventually
	/// paid"). <paramref name="from"/> must hold somewhere, or the rule proved nothing and fails as vacuous.
	/// </summary>
	public EntitySpec<T> LeadsTo(string id, Func<T, bool> from, Func<T, bool> to)
	{
		_liveness.Add(new LivenessRule(id, from, to));
		return this;
	}

	/// <summary>
	/// Weak fairness for the named actions (every action when none are named): a run that keeps one of them enabled forever must take it. Each argument
	/// of an action is its own fair choice, so <c>Deliver(m1)</c> being taken does not excuse <c>Deliver(m2)</c> never being taken.
	/// </summary>
	/// <remarks>
	/// Declare only what the environment guarantees: a broker that delivers every message eventually is fair, a client that may stop sending is not. A
	/// fairness assumption the system does not have hides the loops it gets stuck in.
	/// </remarks>
	public EntitySpec<T> Fair(params string[] actions)
	{
		if (actions.Length == 0)
		{
			_allFair = true;
		}
		foreach (var action in actions)
		{
			_fair[action] = null;
		}
		return this;
	}

	/// <summary>
	/// Weak fairness for <paramref name="action"/>, where what makes two of its steps the same fair choice is <paramref name="identity"/> of the state
	/// and argument rather than the argument alone: for an argument that is a position (deliver the message in slot 0), the identity is the message.
	/// </summary>
	public EntitySpec<T> Fair(string action, Func<T, object?, object?> identity)
	{
		_fair[action] = identity;
		return this;
	}

	/// <summary>
	/// Proves the <see cref="Eventually"/> and <see cref="LeadsTo"/> rules over every reachable state. Fails with a path to the state the rule gives up
	/// in and then the loop (or the stop) that never reaches the goal. Needs the state space to close within <paramref name="maxStates"/>; states
	/// outside the <see cref="Boundary"/> or <see cref="Limit"/> are not expanded, so a run that leaves the region is not counted against a rule.
	/// </summary>
	public LivenessReport Liveness(int maxStates = 100_000, bool requireCoverage = true)
	{
		if (_liveness.Count == 0)
		{
			throw new InvalidOperationException("No liveness rules: declare them with .Eventually(...) or .LeadsTo(...).");
		}
		LivenessReport? largest = null;
		foreach (var start in Starts())
		{
			CheckStart(start);
			var report = CheckLiveness(start, maxStates, requireCoverage);
			if (largest is null || report.States > largest.States)
			{
				largest = report;
			}
		}
		return largest!;
	}

	private bool HasLiveness => _liveness.Count > 0;

	private LivenessReport CheckLiveness(T start, int maxStates, bool requireCoverage)
	{
		var graph = ExploreGraph(start, maxStates);
		foreach (var rule in _liveness)
		{
			CheckRule(graph, rule, requireCoverage);
		}
		var fair = _allFair ? _actions.Select(a => a.Name).ToList() : [.. _fair.Keys];
		return new LivenessReport(graph.States.Count, graph.Edges.Sum(e => e.Count), [.. _liveness.Select(r => r.Id)], fair);
	}

	// ── The graph ─────────────────────────────────────────────────────────────

	private sealed record Edge(int To, string Label, (string Action, object? Identity)? FairChoice);

	private sealed class Graph
	{
		public List<T> States { get; } = [];

		public List<List<Edge>> Edges { get; } = [];

		public List<int> Parent { get; } = [];

		public List<string?> ParentLabel { get; } = [];

		/// <summary>Not expanded: outside the boundary or the collection limit. Nothing is known about where it goes.</summary>
		public HashSet<int> Frontier { get; } = [];

		/// <summary>The fair choices enabled in each state (an action that changes it).</summary>
		public List<HashSet<(string Action, object? Identity)>> Enabled { get; } = [];
	}

	private Graph ExploreGraph(T start, int maxStates)
	{
		var instances = new List<(ActionDef Def, object? Argument, int ArgIndex)>();
		foreach (var def in _actions)
		{
			if (def.ArgumentType is null)
			{
				instances.Add((def, null, -1));
				continue;
			}
			var domain = def.OwnDomain ?? DomainOf(def.ArgumentType);
			for (var i = 0; i < domain.Length; i++)
			{
				instances.Add((def, domain[i], i));
			}
		}

		var allowed = Math.Max(_collectionLimit, StructuralKey.LargestCollection(start));
		var graph = new Graph();
		var index = new Dictionary<Keyed<T>, int>();

		int Add(T state, int parent, string? label)
		{
			var keyed = Wrap(state);
			if (index.TryGetValue(keyed, out var existing))
			{
				return existing;
			}
			if (graph.States.Count >= maxStates)
			{
				throw new InvalidOperationException(
					$"The state space did not close within {maxStates} states from {_print(start)}, so liveness cannot be proved: a run that never reaches the goal may lie past the cut.{Environment.NewLine}"
					+ "Lower .Limit(...), add a .Boundary(...), pass a .Key(...) that identifies equal states, or raise maxStates."
				);
			}
			var id = graph.States.Count;
			index[keyed] = id;
			graph.States.Add(state);
			graph.Edges.Add([]);
			graph.Enabled.Add([]);
			graph.Parent.Add(parent);
			graph.ParentLabel.Add(label);
			return id;
		}

		Add(start, -1, null);
		for (var current = 0; current < graph.States.Count; current++)
		{
			var state = graph.States[current];
			if (StructuralKey.LargestCollection(state) > allowed || !(_boundary?.Invoke(state) ?? true))
			{
				graph.Frontier.Add(current);
				continue;
			}
			foreach (var (def, argument, argIndex) in instances)
			{
				var next = Run(state, def, argument);
				if (Same(next, state))
				{
					continue;
				}
				var label = argIndex < 0 ? def.Name : $"{def.Name}({Convert.ToString(argument, CultureInfo.InvariantCulture)})";
				(string, object?)? choice = null;
				if (_allFair || _fair.ContainsKey(def.Name))
				{
					var identity = _fair.TryGetValue(def.Name, out var by) && by is not null ? StructuralKey.Of(by(state, argument)) : argIndex;
					choice = (def.Name, identity);
					graph.Enabled[current].Add(choice.Value);
				}
				var to = Add(next, current, label);
				graph.Edges[current].Add(new Edge(to, label, choice));
			}
		}
		return graph;
	}

	// ── One rule ──────────────────────────────────────────────────────────────

	private void CheckRule(Graph graph, LivenessRule rule, bool requireCoverage)
	{
		var count = graph.States.Count;
		var avoids = new bool[count];
		var goalSeen = false;
		var fromSeen = false;
		for (var s = 0; s < count; s++)
		{
			avoids[s] = !rule.To(graph.States[s]);
			goalSeen |= !avoids[s];
			fromSeen |= rule.From?.Invoke(graph.States[s]) ?? true;
		}
		if (requireCoverage && rule.From is not null && !fromSeen)
		{
			throw new VacuousPropertyException(
				$"Liveness rule '{rule.Id}' held, but its starting condition never held in any of the {count} reachable states, so it proved nothing.{Environment.NewLine}Add actions or domain values that reach it, or pass requireCoverage: false."
			);
		}

		// The bad states: where the goal does not hold and the run can stop or loop fairly without it.
		var bad = new bool[count];
		var loopOf = new Dictionary<int, List<int>>();
		foreach (var component in Components(graph, avoids))
		{
			if (IsFairLoop(graph, component))
			{
				foreach (var s in component)
				{
					bad[s] = true;
					loopOf[s] = component;
				}
			}
		}
		for (var s = 0; s < count; s++)
		{
			if (avoids[s] && !graph.Frontier.Contains(s) && graph.Edges[s].Count == 0)
			{
				bad[s] = true;
			}
		}

		// Which states can get to a bad one without reaching the goal: backwards over the edges that stay away from it.
		var reverse = new List<int>[count];
		for (var s = 0; s < count; s++)
		{
			reverse[s] = [];
		}
		for (var s = 0; s < count; s++)
		{
			if (!avoids[s])
			{
				continue;
			}
			foreach (var e in graph.Edges[s])
			{
				if (avoids[e.To])
				{
					reverse[e.To].Add(s);
				}
			}
		}
		var doomed = (bool[])bad.Clone();
		var queue = new Queue<int>(Enumerable.Range(0, count).Where(s => bad[s]));
		while (queue.Count > 0)
		{
			foreach (var p in reverse[queue.Dequeue()])
			{
				if (!doomed[p])
				{
					doomed[p] = true;
					queue.Enqueue(p);
				}
			}
		}

		// States are numbered in breadth-first order, so the first offender is the one nearest the start.
		for (var s = 0; s < count; s++)
		{
			if (doomed[s] && (rule.From?.Invoke(graph.States[s]) ?? true))
			{
				throw new CsCheckException(Explain(graph, rule, s, avoids, bad, loopOf));
			}
		}

		if (requireCoverage && !goalSeen)
		{
			throw new VacuousPropertyException(
				$"Liveness rule '{rule.Id}' held, but its goal holds in none of the {count} reachable states: every run leaves the explored region before it could.{Environment.NewLine}Widen the .Boundary(...) or .Limit(...), or pass requireCoverage: false."
			);
		}
	}

	private static bool IsFairLoop(Graph graph, List<int> component)
	{
		var members = component.ToHashSet();
		var taken = new HashSet<(string, object?)>();
		var internalEdge = false;
		foreach (var s in component)
		{
			foreach (var e in graph.Edges[s])
			{
				if (!members.Contains(e.To))
				{
					continue;
				}
				internalEdge = true;
				if (e.FairChoice is { } choice)
				{
					taken.Add(choice);
				}
			}
		}
		if (!internalEdge)
		{
			return false;
		}
		// Every fair choice enabled in all of the loop's states must be taken inside it.
		var always = new HashSet<(string, object?)>(graph.Enabled[component[0]]);
		foreach (var s in component.Skip(1))
		{
			always.IntersectWith(graph.Enabled[s]);
		}
		return always.IsSubsetOf(taken);
	}

	/// <summary>Tarjan's strongly connected components over the states that avoid the goal, iteratively (a deep graph would overflow the stack).</summary>
	private static List<List<int>> Components(Graph graph, bool[] avoids)
	{
		var count = graph.States.Count;
		var order = new int[count];
		var low = new int[count];
		var onStack = new bool[count];
		Array.Fill(order, -1);
		var stack = new Stack<int>();
		var next = 0;
		var result = new List<List<int>>();
		for (var root = 0; root < count; root++)
		{
			if (!avoids[root] || order[root] >= 0)
			{
				continue;
			}
			var work = new Stack<(int State, int Edge)>();
			work.Push((root, 0));
			order[root] = low[root] = next++;
			stack.Push(root);
			onStack[root] = true;
			while (work.Count > 0)
			{
				var (s, i) = work.Pop();
				var edges = graph.Edges[s];
				if (i < edges.Count)
				{
					work.Push((s, i + 1));
					var t = edges[i].To;
					if (!avoids[t])
					{
						continue;
					}
					if (order[t] < 0)
					{
						order[t] = low[t] = next++;
						stack.Push(t);
						onStack[t] = true;
						work.Push((t, 0));
					}
					else if (onStack[t])
					{
						low[s] = Math.Min(low[s], order[t]);
					}
					continue;
				}
				if (work.Count > 0)
				{
					var parent = work.Peek().State;
					low[parent] = Math.Min(low[parent], low[s]);
				}
				if (low[s] == order[s])
				{
					var component = new List<int>();
					int member;
					do
					{
						member = stack.Pop();
						onStack[member] = false;
						component.Add(member);
					}
					while (member != s);
					result.Add(component);
				}
			}
		}
		return result;
	}

	// ── The counterexample ────────────────────────────────────────────────────

	private string Explain(Graph graph, LivenessRule rule, int offender, bool[] avoids, bool[] bad, Dictionary<int, List<int>> loopOf)
	{
		var text = new StringBuilder();
		text.AppendLine("Liveness failed.");
		text.AppendLine();
		var claim = rule.From is null ? "every run reaches the goal, from every reachable state" : "once its condition holds, every run reaches the goal";
		text.AppendLine(CultureInfo.InvariantCulture, $"Rule broken   {rule.Id}: {claim}.");
		var fair = _allFair ? "every action" : _fair.Count > 0 ? string.Join(", ", _fair.Keys) : "none (any loop counts)";
		text.AppendLine(CultureInfo.InvariantCulture, $"Fair actions  {fair}");
		text.AppendLine();

		// To the offender from the start, then (staying away from the goal) to the nearest bad state.
		var prefix = new List<(string? Label, int State)>();
		for (var s = offender; s >= 0; s = graph.Parent[s])
		{
			prefix.Add((graph.ParentLabel[s], s));
		}
		prefix.Reverse();
		var toBad = PathWithin(graph, offender, t => bad[t], t => avoids[t]);
		var steps = prefix.Concat(toBad.Skip(1)).ToList();

		text.AppendLine(CultureInfo.InvariantCulture, $"Path          {_print(graph.States[steps[0].State])}");
		for (var i = 1; i < steps.Count; i++)
		{
			text.AppendLine(CultureInfo.InvariantCulture, $"         {i,4} {steps[i].Label} → {_print(graph.States[steps[i].State])}");
		}

		var end = steps[^1].State;
		if (!loopOf.TryGetValue(end, out var component))
		{
			text.AppendLine();
			text.AppendLine("Then stops    no action changes this state, and the goal does not hold in it.");
		}
		else
		{
			var loop = FairLoop(graph, end, component);
			text.AppendLine();
			text.AppendLine("Then loops    forever, never reaching the goal, taking every fair action that stays enabled:");
			for (var i = 0; i < loop.Count; i++)
			{
				text.AppendLine(CultureInfo.InvariantCulture, $"         {i + 1,4} {loop[i].Label} → {_print(graph.States[loop[i].State])}");
			}
		}
		text.AppendLine();
		text.Append(CultureInfo.InvariantCulture, $"Explored      {graph.States.Count} states, {graph.Edges.Sum(e => e.Count)} transitions.");
		return text.ToString();
	}

	/// <summary>A loop from <paramref name="at"/> back to itself inside <paramref name="component"/> that takes every fair choice enabled in all of it.</summary>
	private static List<(string Label, int State)> FairLoop(Graph graph, int at, List<int> component)
	{
		var members = component.ToHashSet();
		var always = new HashSet<(string, object?)>(graph.Enabled[component[0]]);
		foreach (var s in component.Skip(1))
		{
			always.IntersectWith(graph.Enabled[s]);
		}

		// One edge inside the component per choice, taken in label order: string hashes differ between processes, so set order would change the report.
		var required = always
			.Select(
				choice =>
					component
						.Order()
						.SelectMany(s => graph.Edges[s].Where(e => members.Contains(e.To) && Equals(e.FairChoice, choice)).Select(e => (From: s, Edge: e)))
						.First()
			)
			.OrderBy(x => x.Edge.Label, StringComparer.Ordinal)
			.ThenBy(x => x.From)
			.ToList();

		var loop = new List<(string Label, int State)>();
		var here = at;
		foreach (var (from, edge) in required)
		{
			loop.AddRange(PathWithin(graph, here, t => t == from, members.Contains).Skip(1).Select(p => (p.Label!, p.State)));
			loop.Add((edge.Label, edge.To));
			here = edge.To;
		}
		if (loop.Count == 0 || here != at)
		{
			// close the loop; with nothing fair to take, any way round will do, so step once first to make it a loop and not a standstill
			if (loop.Count == 0)
			{
				var first = graph.Edges[at].First(e => members.Contains(e.To));
				loop.Add((first.Label, first.To));
				here = first.To;
			}
			loop.AddRange(PathWithin(graph, here, t => t == at, members.Contains).Skip(1).Select(p => (p.Label!, p.State)));
		}
		return loop;
	}

	/// <summary>A shortest path from <paramref name="from"/> to a state where <paramref name="target"/> holds, through states where <paramref name="through"/> holds.</summary>
	private static List<(string? Label, int State)> PathWithin(Graph graph, int from, Func<int, bool> target, Func<int, bool> through)
	{
		if (target(from))
		{
			return [(null, from)];
		}
		var parent = new Dictionary<int, (int From, string Label)> { [from] = (-1, "") };
		var queue = new Queue<int>([from]);
		while (queue.Count > 0)
		{
			var s = queue.Dequeue();
			foreach (var e in graph.Edges[s])
			{
				if (parent.ContainsKey(e.To) || !through(e.To))
				{
					continue;
				}
				parent[e.To] = (s, e.Label);
				if (target(e.To))
				{
					var path = new List<(string? Label, int State)>();
					for (var t = e.To; t != from; t = parent[t].From)
					{
						path.Add((parent[t].Label, t));
					}
					path.Add((null, from));
					path.Reverse();
					return path;
				}
				queue.Enqueue(e.To);
			}
		}
		throw new InvalidOperationException("No path inside the component: the liveness search is inconsistent.");
	}
}
