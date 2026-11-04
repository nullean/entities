// Nullean.Entities.CsCheck — SystemSpec: several entities that talk by message, over a network that may reorder, duplicate or drop.
//
// A distributed system is still a state machine: the state of every node plus the messages in flight. Each thing the network or the outside
// world can do is an action on that state:
//
//   Deliver(slot)     hand a deliverable message to its node's handler; what the handler sends goes into flight
//   Duplicate(slot)   the broker redelivers: one more copy in flight            (AtLeastOnce, Lossy; bounded)
//   Drop(slot)        the network loses a message                              (Lossy)
//   Crash(node)       the node restarts from what it had made durable          (bounded)
//   node.Client(...)  a request from outside
//
// SystemSpec.Spec(rules) compiles all of that into an EntitySpec<SystemState>, so Exhaustive, Sample, Mermaid, Faults and the liveness rules
// (Eventually, LeadsTo) apply unchanged. Delivery is fair (a message that stays deliverable is eventually delivered); nothing else is.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Nullean.Entities.CsCheck;

/// <summary>A node of a <see cref="SystemSpec"/>, as a message is addressed to it.</summary>
public interface INode
{
	/// <summary>The node's name, unique in its system.</summary>
	string Name { get; }
}

/// <summary>Sends <paramref name="message"/> to <paramref name="to"/>: it goes into flight, for the network to deliver.</summary>
public delegate void Send(INode to, object message);

/// <summary>A message in flight: who sent it, to whom, and what it says.</summary>
public sealed record Envelope(string From, string To, object Message)
{
	/// <inheritdoc />
	public override string ToString() => $"{From} → {To}: {ValuePrinter.Print(Message)}";
}

/// <summary>What the network may do to a message in flight.</summary>
/// <param name="Ordered">Messages between two nodes arrive in the order they were sent (FIFO per channel).</param>
/// <param name="Duplicates">A message may be delivered more than once (bounded by <see cref="SystemSpec.Bound"/>).</param>
/// <param name="Drops">A message may be lost.</param>
public sealed record Network(bool Ordered, bool Duplicates, bool Drops)
{
	/// <summary>Exactly once, in order per channel: TCP between two processes.</summary>
	public static Network Reliable { get; } = new(true, false, false);

	/// <summary>Exactly once, in any order.</summary>
	public static Network Unordered { get; } = new(false, false, false);

	/// <summary>At least once, in any order: what most message brokers promise.</summary>
	public static Network AtLeastOnce { get; } = new(false, true, false);

	/// <summary>Any order, any number of times including none: UDP, or a broker with no retry.</summary>
	public static Network Lossy { get; } = new(false, true, true);
}

internal interface INodeDefinition : INode
{
	object Initial { get; }

	IReadOnlyList<ClientDefinition> Clients { get; }

	object Handle(object state, object message, Send send);
}

internal sealed record ClientDefinition(string Name, object[]? Domain, Func<object, object?, Send, object> Act);

/// <summary>
/// One node: an immutable state, what it does with each message it receives (<see cref="Handles{TMessage}"/>) and the requests it takes from outside
/// (<see cref="Client(string, Func{TState, Send, TState})"/>). Declare every node first, then their handlers, so they can address each other.
/// </summary>
public sealed class Node<TState> : INodeDefinition where TState : notnull
{
	private readonly Dictionary<Type, Func<TState, object, Send, TState>> _handlers = [];
	private readonly List<ClientDefinition> _clients = [];

	internal Node(string name, TState initial)
	{
		Name = name;
		Initial = initial;
	}

	/// <inheritdoc />
	public string Name { get; }

	/// <summary>The state the node starts in.</summary>
	public TState Initial { get; }

	object INodeDefinition.Initial => Initial;

	IReadOnlyList<ClientDefinition> INodeDefinition.Clients => _clients;

	/// <summary>What the node does with a <typeparamref name="TMessage"/>: the new state, and what it sends through <see cref="Send"/>.</summary>
	public Node<TState> Handles<TMessage>(Func<TState, TMessage, Send, TState> handle)
	{
		_handlers[typeof(TMessage)] = (state, message, send) => handle(state, (TMessage)message, send);
		return this;
	}

	/// <summary>A request from outside the system, with no argument, named <c>node.name</c> in traces.</summary>
	public Node<TState> Client(string name, Func<TState, Send, TState> act)
	{
		_clients.Add(new ClientDefinition(name, null, (state, _, send) => act((TState)state, send)));
		return this;
	}

	/// <summary>A request from outside the system, tried with every value in <paramref name="domain"/> from every state.</summary>
	public Node<TState> Client<TArg>(string name, TArg[] domain, Func<TState, TArg, Send, TState> act)
	{
		_clients.Add(new ClientDefinition(name, [.. domain.Cast<object>()], (state, arg, send) => act((TState)state, (TArg)arg!, send)));
		return this;
	}

	/// <summary>This node's state in <paramref name="state"/>.</summary>
	public TState In(SystemState state) => (TState)state.StateOf(this);

	object INodeDefinition.Handle(object state, object message, Send send)
	{
		for (var type = message.GetType(); type is not null; type = type.BaseType)
		{
			if (_handlers.TryGetValue(type, out var handle))
			{
				return handle((TState)state, message, send);
			}
		}
		throw new InvalidOperationException(
			$"Node '{Name}' received a {message.GetType().Name} and has no handler for it: add .Handles<{message.GetType().Name}>(...)."
		);
	}

	/// <inheritdoc />
	public override string ToString() => Name;
}

/// <summary>
/// One state of a <see cref="SystemSpec"/>: every node's state and the messages in flight, plus how many duplicates and crashes have been spent.
/// Immutable; <see cref="Last"/> says what the step that reached it did, for the trace, and is not part of what makes two states the same.
/// </summary>
public sealed class SystemState
{
	private readonly SystemModel _model;
	private readonly ImmutableArray<object> _nodes;
	private readonly ImmutableArray<int> _crashes;
	private object? _key;

	internal SystemState(
		SystemModel model,
		ImmutableArray<object> nodes,
		ImmutableList<Envelope> inFlight,
		int duplicates,
		ImmutableArray<int> crashes,
		string? last
	)
	{
		_model = model;
		_nodes = nodes;
		InFlight = model.Network.Ordered ? inFlight : [.. inFlight.OrderBy(StructuralKey.Of, KeyOrder.Instance)];
		Duplicates = duplicates;
		_crashes = crashes;
		Last = last;
	}

	/// <summary>The messages sent and not yet delivered or lost, duplicates included.</summary>
	public IReadOnlyList<Envelope> InFlight { get; }

	/// <summary>How many duplicate deliveries the network has made so far.</summary>
	public int Duplicates { get; }

	/// <summary>What the step that reached this state did. Not part of the state.</summary>
	public string? Last { get; }

	/// <summary>How many times <paramref name="node"/> has crashed so far.</summary>
	public int CrashesOf(INode node) => _crashes[_model.IndexOf(node)];

	/// <summary>This state with <paramref name="node"/> in <paramref name="state"/>, for a <see cref="SystemSpec.Symmetric{TId}"/> rename.</summary>
	public SystemState With<TState>(Node<TState> node, TState state) where TState : notnull =>
		new(_model, _nodes.SetItem(_model.IndexOf(node), state), [.. InFlight], Duplicates, _crashes, Last);

	/// <summary>This state with <paramref name="node"/>'s state replaced by <paramref name="change"/> of it.</summary>
	public SystemState With<TState>(Node<TState> node, Func<TState, TState> change) where TState : notnull => With(node, change(node.In(this)));

	/// <summary>This state with every message in flight replaced by <paramref name="map"/> of it, for a <see cref="SystemSpec.Symmetric{TId}"/> rename.</summary>
	public SystemState WithMessages(Func<object, object> map) =>
		new(_model, _nodes, [.. InFlight.Select(e => e with { Message = map(e.Message) })], Duplicates, _crashes, Last);

	internal object StateOf(INode node) => _nodes[_model.IndexOf(node)];

	internal ImmutableArray<object> Nodes => _nodes;

	internal ImmutableArray<int> Crashes => _crashes;

	internal SystemModel Model => _model;

	/// <summary>What makes two states the same: nodes, messages in flight, duplicates and crashes spent; under symmetry, the smallest renaming.</summary>
	internal object Key => _key ??= _model.KeyOf(this);

	internal object RawKey => StructuralKey.Of(new object?[] { _nodes.ToArray(), InFlight.ToArray(), Duplicates, _crashes.ToArray() })!;

	/// <inheritdoc />
	public override string ToString()
	{
		var text = new StringBuilder();
		if (Last is not null)
		{
			text.Append('[').Append(Last).Append("] ");
		}
		for (var i = 0; i < _nodes.Length; i++)
		{
			text.Append(_model.Nodes[i].Name).Append(": ").Append(ValuePrinter.Print(_nodes[i])).Append(" | ");
		}
		text.Append("in flight: [").AppendJoin(", ", InFlight).Append(']');
		return text.ToString();
	}
}

internal sealed class KeyOrder : IComparer<object?>
{
	public static KeyOrder Instance { get; } = new();

	public int Compare(object? x, object? y) => StructuralKey.Compare(x, y);
}

/// <summary>
/// A system of nodes that talk by message: <c>SystemSpec.Of(orders, billing).Network(Network.AtLeastOnce).Spec(rules).Exhaustive()</c>. See the
/// file header for the actions it explores.
/// </summary>
public sealed class SystemSpec
{
	private readonly List<INodeDefinition> _nodes;
	private readonly Dictionary<string, Func<object, object>> _durable = [];
	private Network _network = global::Nullean.Entities.CsCheck.Network.Reliable;
	private int _maxInFlight = 4;
	private int _maxDuplicates = 1;
	private int _maxCrashes = 1;
	private int _collections = 4;
	private Func<SystemState, bool>? _boundary;
	private Func<SystemState, object>? _symmetric;

	private SystemSpec(List<INodeDefinition> nodes) => _nodes = nodes;

	/// <summary>A node named <paramref name="name"/> starting in <paramref name="initial"/>. Add what it does with <c>Handles</c> and <c>Client</c>.</summary>
	public static Node<TState> Node<TState>(string name, TState initial) where TState : notnull => new(name, initial);

	/// <summary>A system of <paramref name="nodes"/>, declared with <see cref="Node{TState}(string, TState)"/>. The network starts <see cref="Network.Reliable"/>.</summary>
	public static SystemSpec Of(params INode[] nodes)
	{
		var definitions = nodes.Select(
			n => n as INodeDefinition ?? throw new ArgumentException($"'{n.Name}' was not declared with SystemSpec.Node.", nameof(nodes))
		).ToList();
		var duplicate = definitions.GroupBy(n => n.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
		return duplicate is null ? new SystemSpec(definitions) : throw new ArgumentException($"Two nodes are named '{duplicate.Key}'.", nameof(nodes));
	}

	/// <summary>What the network may do to messages in flight.</summary>
	public SystemSpec Network(Network network)
	{
		_network = network;
		return this;
	}

	/// <summary>
	/// <paramref name="node"/> may crash and restart, coming back as <paramref name="durable"/> of the state it crashed in (what it had committed;
	/// <c>s => s</c> when everything is). Messages in flight to it stay in flight: the broker still has them.
	/// </summary>
	public SystemSpec Crashes<TState>(Node<TState> node, Func<TState, TState> durable) where TState : notnull
	{
		_durable[node.Name] = state => durable((TState)state);
		return this;
	}

	/// <summary>
	/// What keeps the search finite: states with more than <paramref name="inFlight"/> messages in flight, or a node state holding a collection larger
	/// than <paramref name="collections"/>, are not expanded; the network duplicates at most <paramref name="duplicates"/> times and each node crashes at
	/// most <paramref name="crashes"/> times per run. A proof says "nothing breaks within these bounds".
	/// </summary>
	public SystemSpec Bound(int inFlight = 4, int duplicates = 1, int crashes = 1, int collections = 4)
	{
		_maxInFlight = inFlight;
		_maxDuplicates = duplicates;
		_maxCrashes = crashes;
		_collections = collections;
		return this;
	}

	/// <summary>Stop expanding states outside this region, in addition to <see cref="Bound"/>.</summary>
	public SystemSpec Boundary(Func<SystemState, bool> inside)
	{
		_boundary = inside;
		return this;
	}

	/// <summary>
	/// Symmetry reduction: the <paramref name="ids"/> are interchangeable, so a state and any renaming of them are explored once. <paramref name="rename"/>
	/// applies a renaming to a state (every id in every node's state and every message). Only declare a real symmetry: if the system treats one id
	/// differently, a renaming hides states. Sound for safety rules; <c>Liveness</c> refuses to run under it, since a loop in the reduced graph can be
	/// one that renames on every lap.
	/// </summary>
	public SystemSpec Symmetric<TId>(TId[] ids, Func<SystemState, Func<TId, TId>, SystemState> rename) where TId : notnull
	{
		var renamings = Permutations(ids)
			.Select(perm =>
			{
				var map = ids.Zip(perm).ToDictionary(p => p.First, p => p.Second);
				return (Func<TId, TId>)(id => map.TryGetValue(id, out var to) ? to : id);
			})
			.ToList();
		_symmetric = state => renamings.Select(map => rename(state, map).RawKey).Min(KeyOrder.Instance)!;
		return this;
	}

	/// <summary>
	/// The system as an <see cref="EntitySpec{T}"/> over <see cref="SystemState"/>, checking <paramref name="rules"/> in every reachable state and step.
	/// Add <c>Eventually</c> or <c>LeadsTo</c> to it for liveness; delivery is already declared fair.
	/// </summary>
	public EntitySpec<SystemState> Spec(ConstraintSet<SystemState>? rules = null)
	{
		var model = new SystemModel(_nodes, _network, _symmetric);
		var initial = new SystemState(model, [.. _nodes.Select(n => n.Initial)], [], 0, [.. _nodes.Select(_ => 0)], null);
		var spec = EntitySpec.For(rules ?? ConstraintSet.From<SystemState>(), null, initial);

		foreach (var node in _nodes)
		{
			foreach (var client in node.Clients)
			{
				var (n, c) = (node, client);
				var name = $"{n.Name}.{c.Name}";
				if (c.Domain is null)
				{
					spec.Action(name, s => model.Client(s, n, c, null));
				}
				else
				{
					spec.Action(name, c.Domain, (s, arg) => model.Client(s, n, c, arg));
				}
			}
		}

		var slots = Enumerable.Range(0, _maxInFlight).ToArray();
		spec.Action("Deliver", slots, model.Deliver);
		if (_network.Duplicates)
		{
			spec.Action("Duplicate", slots, (s, slot) => s.Duplicates >= _maxDuplicates ? s : model.Duplicate(s, slot));
		}
		if (_network.Drops)
		{
			spec.Action("Drop", slots, model.Drop);
		}
		foreach (var (nodeName, durable) in _durable)
		{
			var node = _nodes.Single(n => n.Name == nodeName);
			spec.Action($"Crash({nodeName})", s => s.CrashesOf(node) >= _maxCrashes ? s : model.Crash(s, node, durable));
		}

		// A message that stays deliverable is eventually delivered; a slot is a position, so the fair choice is the message in it.
		spec.Fair("Deliver", (s, slot) => (int)slot! < model.Deliverable(s).Count ? StructuralKey.Of(model.Deliverable(s)[(int)slot!]) : null);
		if (_symmetric is not null)
		{
			spec.WithoutLiveness(
				"symmetry reduction (.Symmetric) is on: a loop in the reduced graph can rename on every lap, so fairness cannot be judged on it. Check liveness on a spec without .Symmetric."
			);
		}

		var limit = _collections;
		var maxInFlight = _maxInFlight;
		var boundary = _boundary;
		return spec
			.Key(s => s.Key)
			.Print(s => s.ToString())
			.Limit(int.MaxValue)
			.Boundary(s => s.InFlight.Count <= maxInFlight && s.Nodes.All(n => StructuralKey.LargestCollection(n) <= limit) && (boundary?.Invoke(s) ?? true));
	}

	private static IEnumerable<TId[]> Permutations<TId>(TId[] items)
	{
		if (items.Length <= 1)
		{
			yield return items;
			yield break;
		}
		for (var i = 0; i < items.Length; i++)
		{
			var rest = items.Where((_, j) => j != i).ToArray();
			foreach (var tail in Permutations(rest))
			{
				yield return [items[i], .. tail];
			}
		}
	}
}

/// <summary>The rules of one system: how each action changes a <see cref="SystemState"/>.</summary>
internal sealed class SystemModel
{
	private readonly Dictionary<string, int> _index;
	private readonly Func<SystemState, object>? _symmetric;

	public SystemModel(List<INodeDefinition> nodes, Network network, Func<SystemState, object>? symmetric)
	{
		Nodes = nodes;
		Network = network;
		_symmetric = symmetric;
		_index = nodes.Select((n, i) => (n.Name, i)).ToDictionary(p => p.Name, p => p.i, StringComparer.Ordinal);
	}

	public List<INodeDefinition> Nodes { get; }

	public Network Network { get; }

	public int IndexOf(INode node) =>
		_index.TryGetValue(node.Name, out var i) ? i : throw new InvalidOperationException($"'{node.Name}' is not a node of this system.");

	public object KeyOf(SystemState state) => _symmetric?.Invoke(state) ?? state.RawKey;

	/// <summary>
	/// The messages that can be delivered next. In order: the first message of each channel (sender, receiver). Otherwise: each distinct message once
	/// (two copies of one message are one choice, delivered one at a time).
	/// </summary>
	public IReadOnlyList<Envelope> Deliverable(SystemState state)
	{
		var seen = new HashSet<object?>();
		var result = new List<Envelope>();
		foreach (var e in state.InFlight)
		{
			var key = Network.Ordered ? (e.From, e.To) : StructuralKey.Of(e);
			if (seen.Add(key))
			{
				result.Add(e);
			}
		}
		return result;
	}

	public SystemState Client(SystemState state, INodeDefinition node, ClientDefinition client, object? argument)
	{
		var i = IndexOf(node);
		var sent = new List<Envelope>();
		var next = client.Act(state.Nodes[i], argument, Sender(node, sent));
		var what = argument is null ? $"{node.Name}.{client.Name}" : $"{node.Name}.{client.Name}({Convert.ToString(argument, CultureInfo.InvariantCulture)})";
		return new SystemState(this, state.Nodes.SetItem(i, next), [.. state.InFlight, .. sent], state.Duplicates, state.Crashes, what);
	}

	public SystemState Deliver(SystemState state, int slot)
	{
		var deliverable = Deliverable(state);
		if (slot >= deliverable.Count)
		{
			return state;
		}
		var envelope = deliverable[slot];
		var to = Nodes[IndexOf(new Address(envelope.To))];
		var i = IndexOf(to);
		var sent = new List<Envelope>();
		var next = to.Handle(state.Nodes[i], envelope.Message, Sender(to, sent));
		return new SystemState(
			this,
			state.Nodes.SetItem(i, next),
			[.. Without(state.InFlight, envelope), .. sent],
			state.Duplicates,
			state.Crashes,
			$"deliver {envelope}"
		);
	}

	public SystemState Duplicate(SystemState state, int slot)
	{
		var deliverable = Deliverable(state);
		if (slot >= deliverable.Count)
		{
			return state;
		}
		var envelope = deliverable[slot];
		return new SystemState(this, state.Nodes, [.. state.InFlight, envelope], state.Duplicates + 1, state.Crashes, $"duplicate {envelope}");
	}

	public SystemState Drop(SystemState state, int slot)
	{
		var deliverable = Deliverable(state);
		if (slot >= deliverable.Count)
		{
			return state;
		}
		var envelope = deliverable[slot];
		return new SystemState(this, state.Nodes, [.. Without(state.InFlight, envelope)], state.Duplicates, state.Crashes, $"drop {envelope}");
	}

	public SystemState Crash(SystemState state, INodeDefinition node, Func<object, object> durable)
	{
		var i = IndexOf(node);
		return new SystemState(
			this,
			state.Nodes.SetItem(i, durable(state.Nodes[i])),
			[.. state.InFlight],
			state.Duplicates,
			state.Crashes.SetItem(i, state.Crashes[i] + 1),
			$"crash {node.Name}"
		);
	}

	private static IEnumerable<Envelope> Without(IReadOnlyList<Envelope> inFlight, Envelope envelope)
	{
		var removed = false;
		foreach (var e in inFlight)
		{
			if (!removed && ReferenceEquals(e, envelope))
			{
				removed = true;
				continue;
			}
			yield return e;
		}
	}

	private Send Sender(INode from, List<Envelope> sent) =>
		(to, message) =>
		{
			_ = IndexOf(to);
			sent.Add(new Envelope(from.Name, to.Name, message));
		};

	private sealed record Address(string Name) : INode;
}
