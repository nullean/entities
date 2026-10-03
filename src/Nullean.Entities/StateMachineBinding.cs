// Nullean.Entities — binds a StateMachine<TState> to the aggregate that holds the state, so a service fires a
// transition on the entity and gets the entity back. See StateMachine<TState>.For.

namespace Nullean.Entities;

/// <summary>
/// A <see cref="StateMachine{TState}"/> bound to an entity that has a <typeparamref name="TState"/> property. Firing a transition returns the entity with its
/// state replaced; given a constraint set (<see cref="Enforce"/>), the new entity is also checked against every rule, including the sequential ones (the machine's
/// own <see cref="AsConstraint"/> is one), through <see cref="ConstraintSet{T}.Apply"/>.
/// </summary>
[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Type-state machines find a union's cases and wrap and unwrap them by reflection. Use StateMachine<TState, TTrigger>, whose states are values, for trimmed or NativeAOT code.")]
public sealed class MachineBinding<TEntity, TState>
{
	private readonly StateMachine<TState> _machine;
	private readonly Func<TEntity, TState> _getState;
	private readonly Func<TEntity, TState, TEntity> _withState;
	private readonly ConstraintSet<TEntity>? _rules;

	internal MachineBinding(
		StateMachine<TState> machine,
		Func<TEntity, TState> getState,
		Func<TEntity, TState, TEntity> withState,
		ConstraintSet<TEntity>? rules = null
	)
	{
		_machine = machine;
		_getState = getState;
		_withState = withState;
		_rules = rules;
	}

	/// <summary>The machine's own transition rule as a constraint over the entity: add it to the entity's set so every <c>Apply</c> rejects a change of state the machine does not allow.</summary>
	public Constraint<TEntity> AsConstraint(string? message = null) => _machine.AsConstraint(message).Map<TEntity>(_getState);

	/// <summary>The same binding that also checks the result of every <c>Fire</c> against <paramref name="rules"/>. Include <see cref="AsConstraint"/> in them, or the machine's own rule is not part of the check.</summary>
	public MachineBinding<TEntity, TState> Enforce(ConstraintSet<TEntity> rules) => new(_machine, _getState, _withState, rules);

	/// <summary>The transitions that may run on <paramref name="entity"/> now.</summary>
	public IReadOnlyList<string> Permitted(TEntity entity) => _machine.Permitted(_getState(entity));

	/// <summary>Runs <paramref name="trigger"/> on the entity's state; the entity comes back with the new state, or a failure (the machine's reason, or the violated rules).</summary>
	public Result<TEntity> Fire(TEntity entity, string trigger, TimeProvider? time = null) => Then(entity, _machine.Fire(_getState(entity), trigger), time);

	/// <summary><see cref="Fire(TEntity, string, TimeProvider)"/> for a transition that takes an argument.</summary>
	public Result<TEntity> Fire<TArg>(TEntity entity, string trigger, TArg argument, TimeProvider? time = null) =>
		Then(entity, _machine.Fire(_getState(entity), trigger, argument), time);

	/// <summary><see cref="Fire(TEntity, string, TimeProvider)"/>, also returning the <see cref="Transitioned{TState}"/> a service would publish.</summary>
	public Result<(TEntity Entity, Transitioned<TState> Event)> FireWithEvent(TEntity entity, string trigger, TimeProvider? time = null)
	{
		var from = _getState(entity);
		return Fire(entity, trigger, time).Map(next => (next, new Transitioned<TState>(from, trigger, _getState(next))));
	}

	private Result<TEntity> Then(TEntity entity, Result<TState> fired, TimeProvider? time) => fired switch
	{
		Result<TState>.Ok ok when _rules is null => new Result<TEntity>.Ok(_withState(entity, ok.Value)),
		Result<TState>.Ok ok => _rules.Apply(entity, e => _withState(e, ok.Value), time),
		Result<TState>.Fail fail => new Result<TEntity>.Fail(entity, fail.Violations),
		_ => throw new System.Diagnostics.UnreachableException(),
	};
}
