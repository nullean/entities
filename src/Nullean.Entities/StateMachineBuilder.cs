// Nullean.Entities — the fluent builder behind StateMachine<TState>.Define(...). See
// StateMachine.cs's header for the design: every case is a type, never a value, and none of the
// generic type parameters below carry a `where T : TState` constraint, because a C# 11 union's
// case types cannot satisfy one.
//
// No doc comments yet on most members (checkpoint-A exploration code) - CS1591 is suppressed
// project-wide in Directory.Build.props rather than per file.

namespace Nullean.Entities;

// ── StateMachineBuilder<TState> ───────────────────────────────────────────────

/// <summary>
/// Built only through <see cref="StateMachine{TState}.Define"/>. <see cref="Initial{T}"/>,
/// <see cref="State{T}"/> and <see cref="Terminal{T}"/> each declare exactly one case and return
/// this same builder, so a <c>Define</c> call reads as one continuous chain — one line per state.
/// </summary>
[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Type-state machines find a union's cases and wrap and unwrap them by reflection. Use StateMachine<TState, TTrigger>, whose states are values, for trimmed or NativeAOT code.")]
public sealed class StateMachineBuilder<TState>
{
	private readonly Dictionary<Type, StateRole> _states = [];
	private readonly List<StateMachineEdge<TState>> _edges = [];
	private Type? _initial;

	internal StateMachineBuilder() { }

	internal IReadOnlyDictionary<Type, StateRole> States => _states;
	internal IReadOnlyList<StateMachineEdge<TState>> Edges => _edges;
	internal Type? InitialState => _initial;

	/// <summary>Declares <typeparamref name="T"/> as the machine's one starting state.</summary>
	public StateMachineBuilder<TState> Initial<T>(Action<StateBuilder<TState, T>>? configure = null)
	{
		Declare<T>(StateRole.Initial);
		_initial = typeof(T);
		configure?.Invoke(new StateBuilder<TState, T>(this));
		return this;
	}

	/// <summary>Declares <typeparamref name="T"/> as a non-terminal, non-initial state.</summary>
	public StateMachineBuilder<TState> State<T>(Action<StateBuilder<TState, T>>? configure = null)
	{
		Declare<T>(StateRole.Normal);
		configure?.Invoke(new StateBuilder<TState, T>(this));
		return this;
	}

	/// <summary>Declares <typeparamref name="T"/> as terminal — it may have no outgoing transition.</summary>
	public StateMachineBuilder<TState> Terminal<T>()
	{
		Declare<T>(StateRole.Terminal);
		return this;
	}

	private void Declare<T>(StateRole role)
	{
		var type = typeof(T);
		if (!_states.TryAdd(type, role))
		{
			throw new InvalidOperationException(
				$"{type.Name} is already declared on this StateMachine<{typeof(TState).Name}> — each case needs exactly one Initial/State/Terminal call."
			);
		}
	}

	internal void AddEdge(StateMachineEdge<TState> edge) => _edges.Add(edge);
}

// ── StateBuilder<TState, TFrom> ────────────────────────────────────────────────

/// <summary>
/// The per-state builder handed to the <c>configure</c> lambda of
/// <see cref="StateMachineBuilder{TState}.Initial{T}"/>/<see cref="StateMachineBuilder{TState}.State{T}"/>.
/// <see cref="To{TTo}"/> adds an unnamed transition, named after its own target type;
/// <see cref="On"/> names one explicitly first.
/// </summary>
[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Type-state machines find a union's cases and wrap and unwrap them by reflection. Use StateMachine<TState, TTrigger>, whose states are values, for trimmed or NativeAOT code.")]
public sealed class StateBuilder<TState, TFrom>
{
	private readonly StateMachineBuilder<TState> _owner;

	internal StateBuilder(StateMachineBuilder<TState> owner) => _owner = owner;

	/// <summary>Starts a named transition out of <typeparamref name="TFrom"/> — call <see cref="TransitionNameBuilder{TState, TFrom}.To{TTo}"/> to finish it.</summary>
	public TransitionNameBuilder<TState, TFrom> On(string name) => new(_owner, name);

	/// <summary>
	/// An unnamed transition out of <typeparamref name="TFrom"/>, named after
	/// <typeparamref name="TTo"/>. <paramref name="when"/>, if given, is checked against the
	/// <typeparamref name="TFrom"/> value the transition would leave — see
	/// <see cref="StateMachine{TState}.Allows"/>.
	/// </summary>
	public StateBuilder<TState, TFrom> To<TTo>(Func<TFrom, bool>? when = null)
	{
		_owner.AddEdge(new StateMachineEdge<TState>(typeof(TTo).Name, typeof(TFrom), typeof(TTo), StateMachineGuards.Wrap<TState, TFrom>(when)));
		return this;
	}
}

// ── TransitionNameBuilder<TState, TFrom> ──────────────────────────────────────

/// <summary>Returned by <see cref="StateBuilder{TState, TFrom}.On"/> — call <see cref="To{TTo}"/> to name the transition's target.</summary>
[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Type-state machines find a union's cases and wrap and unwrap them by reflection. Use StateMachine<TState, TTrigger>, whose states are values, for trimmed or NativeAOT code.")]
public sealed class TransitionNameBuilder<TState, TFrom>
{
	private readonly StateMachineBuilder<TState> _owner;
	private readonly string _name;

	internal TransitionNameBuilder(StateMachineBuilder<TState> owner, string name)
	{
		_owner = owner;
		_name = name;
	}

	/// <summary>Completes the named transition to <typeparamref name="TTo"/> — see <see cref="StateBuilder{TState, TFrom}.To{TTo}"/> for <paramref name="when"/>.</summary>
	public StateBuilder<TState, TFrom> To<TTo>(Func<TFrom, bool>? when = null)
	{
		_owner.AddEdge(new StateMachineEdge<TState>(_name, typeof(TFrom), typeof(TTo), StateMachineGuards.Wrap<TState, TFrom>(when)));
		return new StateBuilder<TState, TFrom>(_owner);
	}
}

// ── StateMachineGuards ─────────────────────────────────────────────────────────
// Erases a typed Func<TFrom, bool> guard to Func<TState, bool> for storage on
// StateMachineEdge<TState>. Unwraps through StateMachineRuntime first (not a bare `(TFrom)(object)`
// cast of the raw TState value) because a union's TState value is the compiler-synthesized wrapper
// struct, not the case instance itself — see StateMachineRuntime's own header comment. The
// resulting downcast is always safe to run, because StateMachine<TState>.Allows only ever invokes
// a stored guard after it has already matched that edge's From against the unwrapped value's type.

[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Type-state machines find a union's cases and wrap and unwrap them by reflection. Use StateMachine<TState, TTrigger>, whose states are values, for trimmed or NativeAOT code.")]
internal static class StateMachineGuards
{
	public static Func<TState, bool>? Wrap<TState, TFrom>(Func<TFrom, bool>? when) =>
		when is null ? null : state => when((TFrom)StateMachineRuntime.Unwrap(state!));
}

// ── [StateMachine] attribute ──────────────────────────────────────────────────

/// <summary>
/// Marks a static <see cref="StateMachine{TState}"/> field for future compile-time structural
/// verification. Inert today: <see cref="StateMachine{TState}.Define"/> already runs NESM001-004
/// eagerly at the field's own construction, so a misdeclared machine fails immediately rather than
/// silently — but as an exception at first use, not as a build error the way [Constraint]'s
/// NECG001-007 are. A Roslyn analyzer reporting the same four checks as true build diagnostics is
/// tracked in the root README's "coming next" section, not implemented here yet. Applying this
/// attribute now costs nothing and marks the intent for when it lands.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class StateMachineAttribute : Attribute
{
}
