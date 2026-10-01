// Nullean.Entities — the mutations an entity offers, as plain data.
//
// [Constraints] source generation emits one EntityActions<T> next to the constraint set, listing
// every public instance method on T that returns T and takes zero or one argument. Plain data
// (no CsCheck, no reflection) so any test driver can walk an entity's reachable states.

using System.Collections;

namespace Nullean.Entities;

/// <summary>One mutation of <typeparamref name="TEntity"/>: a name, the type of its argument (if any), and how to run it.</summary>
public sealed record EntityAction<TEntity>(string Name, Type? ArgumentType, Func<TEntity, object?, TEntity> Apply);

/// <summary>The mutations an entity offers; see <see cref="EntityAction{TEntity}"/>.</summary>
public sealed class EntityActions<TEntity> : IReadOnlyList<EntityAction<TEntity>>
{
	private readonly EntityAction<TEntity>[] _actions;

	public EntityActions(params EntityAction<TEntity>[] actions) => _actions = actions;

	public int Count => _actions.Length;

	public EntityAction<TEntity> this[int index] => _actions[index];

	public IEnumerator<EntityAction<TEntity>> GetEnumerator() => ((IEnumerable<EntityAction<TEntity>>)_actions).GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
