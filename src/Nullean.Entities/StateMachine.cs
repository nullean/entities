// Nullean.Entities — a state machine declared as data: states, triggers, and which trigger moves which state to which.
//
// It is a specification, not an engine. It does three things:
//   - answers "what happens if this trigger fires in this state" (Fire / TryFire), purely;
//   - becomes constraints and a diagram: AsConstraint(selector) is the Transitions allow-list for an entity, ToMermaid() is the picture,
//     and StateMachine.FromMermaid(text) reads a diagram back, so a diagram in the docs can be the specification;
//   - is what Nullean.Entities.CsCheck explores and conformance-tests an entity against (machine.Spec(), machine.ConformTo(...)).

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nullean.Entities;

public sealed record MachineTransition<TState, TTrigger>(TState From, TTrigger Trigger, TState To);

/// <summary>A move that happens by itself once <see cref="After"/> has passed in <see cref="From"/>.</summary>
public sealed record MachineTimeout<TState>(TState From, TimeSpan After, TState To);

public sealed class StateMachine<TState, TTrigger> where TState : notnull where TTrigger : notnull
{
	internal StateMachine(
		TState initial,
		IReadOnlyList<TState> states,
		IReadOnlyList<MachineTransition<TState, TTrigger>> transitions,
		IReadOnlyList<MachineTimeout<TState>> timeouts
	)
	{
		Initial = initial;
		States = states;
		Transitions = transitions;
		Timeouts = timeouts;
		Triggers = [.. transitions.Select(t => t.Trigger).Distinct()];
	}

	public TState Initial { get; }

	public IReadOnlyList<TState> States { get; }

	public IReadOnlyList<TTrigger> Triggers { get; }

	public IReadOnlyList<MachineTransition<TState, TTrigger>> Transitions { get; }

	public IReadOnlyList<MachineTimeout<TState>> Timeouts { get; }

	/// <summary>Where <paramref name="trigger"/> takes <paramref name="state"/>; false when the machine does not permit it there.</summary>
	public bool TryFire(TState state, TTrigger trigger, out TState next)
	{
		foreach (var t in Transitions)
		{
			if (EqualityComparer<TState>.Default.Equals(t.From, state) && EqualityComparer<TTrigger>.Default.Equals(t.Trigger, trigger))
			{
				next = t.To;
				return true;
			}
		}
		next = state;
		return false;
	}

	public TState Fire(TState state, TTrigger trigger) =>
		TryFire(state, trigger, out var next) ? next : throw new InvalidOperationException($"{trigger} is not permitted in {state}.");

	/// <summary>The triggers the machine permits in <paramref name="state"/>.</summary>
	public IReadOnlyList<TTrigger> Permitted(TState state) =>
		[.. Transitions.Where(t => EqualityComparer<TState>.Default.Equals(t.From, state)).Select(t => t.Trigger)];

	/// <summary>
	/// The machine as a rule on an entity: whenever <paramref name="selector"/>'s state changes, the move must be one the machine declares
	/// (by a trigger or a timeout). Replaces a hand-written <c>Transitions(...).Allow(a, b)</c> list that can drift from the diagram.
	/// </summary>
	public Constraint<T> AsConstraint<T>(Func<T, TState> selector)
	{
		var builder = Constraints<T>.Transitions(selector);
		foreach (var (from, to) in Transitions.Select(t => (t.From, t.To)).Concat(Timeouts.Select(t => (t.From, t.To))).Distinct())
		{
			builder.Allow(from, to);
		}
		return builder;
	}

	/// <summary>The machine as a Mermaid <c>stateDiagram-v2</c>.</summary>
	public string ToMermaid()
	{
		var sb = new StringBuilder("stateDiagram-v2\n");
		sb.Append(CultureInfo.InvariantCulture, $"    [*] --> {Id(Initial)}\n");
		foreach (var t in Transitions)
		{
			sb.Append(CultureInfo.InvariantCulture, $"    {Id(t.From)} --> {Id(t.To)} : {t.Trigger}\n");
		}
		foreach (var t in Timeouts)
		{
			sb.Append(CultureInfo.InvariantCulture, $"    {Id(t.From)} --> {Id(t.To)} : after {Duration(t.After)}\n");
		}
		foreach (var state in States.Where(s => !Transitions.Any(t => Equals(t.From, s) || Equals(t.To, s)) && !Equals(s, Initial)))
		{
			sb.Append(CultureInfo.InvariantCulture, $"    {Id(state)}\n");
		}
		return sb.ToString();
	}

	internal static string Id(TState state) => MermaidPatterns.NonWord().Replace(Convert.ToString(state, CultureInfo.InvariantCulture) ?? "", "_").Trim('_');

	internal static string Duration(TimeSpan d) =>
		d.TotalHours >= 1 && d.TotalHours % 1 == 0
			? $"{(long)d.TotalHours}h"
			: d.TotalMinutes >= 1 && d.TotalMinutes % 1 == 0 ? $"{(long)d.TotalMinutes}m" : $"{d.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s";
}

public static class StateMachine
{
	/// <summary>Starts declaring a machine whose first state is <paramref name="initial"/>.</summary>
	public static StateMachineBuilder<TState, TTrigger> Define<TState, TTrigger>(TState initial) where TState : notnull where TTrigger : notnull =>
		new(initial);

	/// <summary>
	/// Reads a Mermaid <c>stateDiagram-v2</c> (<c>[*] --&gt; A</c>, <c>A --&gt; B : trigger</c>, <c>A --&gt; B : after 30s</c>) into a machine of
	/// string states and string triggers, so a diagram in the documentation can be the specification an entity is tested against.
	/// </summary>
	public static StateMachine<string, string> FromMermaid(string diagram)
	{
		string? initial = null;
		var builderStates = new List<string>();
		var transitions = new List<(string From, string Trigger, string To)>();
		var timeouts = new List<(string From, TimeSpan After, string To)>();

		foreach (var raw in diagram.Split('\n'))
		{
			var line = raw.Trim();
			if (line.Length == 0 || line.StartsWith("stateDiagram", StringComparison.Ordinal) || line.StartsWith(
					"%%",
					StringComparison.Ordinal
				) || line == "}" || line.StartsWith("state ", StringComparison.Ordinal))
			{
				continue;
			}
			var m = MermaidPatterns.Edge().Match(line);
			if (!m.Success)
			{
				if (MermaidPatterns.BareState().IsMatch(line))
				{
					builderStates.Add(line);
					continue;
				}
				throw new FormatException(
					$"Cannot read this Mermaid state diagram line: \"{line}\". Expected \"A --> B : trigger\", \"[*] --> A\" or a bare state name."
				);
			}
			var from = m.Groups["from"].Value;
			var to = m.Groups["to"].Value;
			var label = m.Groups["label"].Success ? m.Groups["label"].Value.Trim() : null;
			if (from == "[*]")
			{
				initial = to;
				continue;
			}
			if (to == "[*]")
			{
				continue; // a final marker: the state simply has no way out
			}
			if (label is not null && label.StartsWith("after ", StringComparison.OrdinalIgnoreCase) && TryParseDuration(label[6..], out var after))
			{
				timeouts.Add((from, after, to));
			}
			else
			{
				transitions.Add((from, label ?? "next", to));
			}
		}

		var first = initial ?? transitions.Select(t => t.From).Concat(timeouts.Select(t => t.From)).FirstOrDefault() ?? builderStates.FirstOrDefault()
		?? throw new FormatException("The diagram declares no states.");
		var builder = Define<string, string>(first);
		foreach (var s in builderStates)
		{
			builder.State(s);
		}
		foreach (var (from, trigger, to) in transitions)
		{
			builder.Permit(trigger, from, to);
		}
		foreach (var (from, after, to) in timeouts)
		{
			builder.After(after, from, to);
		}
		return builder.Build();
	}

	private static bool TryParseDuration(string text, out TimeSpan value)
	{
		var m = MermaidPatterns.Duration().Match(text.Trim());
		if (!m.Success)
		{
			return TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out value);
		}
		var n = double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
		value = m.Groups["unit"].Value.ToLowerInvariant() switch
		{
			"ms" => TimeSpan.FromMilliseconds(n),
			"s" => TimeSpan.FromSeconds(n),
			"m" => TimeSpan.FromMinutes(n),
			"h" => TimeSpan.FromHours(n),
			_ => TimeSpan.FromDays(n),
		};
		return true;
	}
}

public sealed class StateMachineBuilder<TState, TTrigger> where TState : notnull where TTrigger : notnull
{
	private readonly TState _initial;
	private readonly List<TState> _states = [];
	private readonly List<MachineTransition<TState, TTrigger>> _transitions = [];
	private readonly List<MachineTimeout<TState>> _timeouts = [];

	internal StateMachineBuilder(TState initial)
	{
		_initial = initial;
		_states.Add(initial);
	}

	/// <summary>Declares a state with no transitions yet (so an isolated or terminal state still shows up).</summary>
	public StateMachineBuilder<TState, TTrigger> State(TState state)
	{
		Add(state);
		return this;
	}

	/// <summary><paramref name="trigger"/> takes <paramref name="from"/> to <paramref name="to"/>. In any other state the trigger does nothing.</summary>
	public StateMachineBuilder<TState, TTrigger> Permit(TTrigger trigger, TState from, TState to)
	{
		if (_transitions.Any(t => EqualityComparer<TState>.Default.Equals(t.From, from) && EqualityComparer<TTrigger>.Default.Equals(t.Trigger, trigger)))
		{
			throw new InvalidOperationException($"{trigger} is already permitted in {from}: a trigger leads to one state.");
		}
		Add(from);
		Add(to);
		_transitions.Add(new MachineTransition<TState, TTrigger>(from, trigger, to));
		return this;
	}

	/// <summary><paramref name="trigger"/> takes each of <paramref name="from"/> to <paramref name="to"/>.</summary>
	public StateMachineBuilder<TState, TTrigger> Permit(TTrigger trigger, IEnumerable<TState> from, TState to)
	{
		foreach (var f in from)
		{
			Permit(trigger, f, to);
		}
		return this;
	}

	/// <summary>After <paramref name="after"/> in <paramref name="from"/> the machine moves to <paramref name="to"/> by itself.</summary>
	public StateMachineBuilder<TState, TTrigger> After(TimeSpan after, TState from, TState to)
	{
		Add(from);
		Add(to);
		_timeouts.Add(new MachineTimeout<TState>(from, after, to));
		return this;
	}

	public StateMachine<TState, TTrigger> Build() => new(_initial, [.. _states], [.. _transitions], [.. _timeouts]);

	private void Add(TState state)
	{
		if (!_states.Contains(state))
		{
			_states.Add(state);
		}
	}
}

internal static partial class MermaidPatterns
{
	[GeneratedRegex(@"\W+")]
	public static partial Regex NonWord();

	[GeneratedRegex(@"^(?<from>\[\*\]|[\w.\-]+)\s*-->\s*(?<to>\[\*\]|[\w.\-]+)\s*(?::\s*(?<label>.+))?$")]
	public static partial Regex Edge();

	[GeneratedRegex(@"^[\w.\-]+$")]
	public static partial Regex BareState();

	[GeneratedRegex(@"^(?<n>\d+(\.\d+)?)\s*(?<unit>ms|s|m|h|d)$", RegexOptions.IgnoreCase)]
	public static partial Regex Duration();
}
