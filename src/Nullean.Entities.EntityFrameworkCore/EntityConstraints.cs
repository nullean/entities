using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Nullean.Entities.EntityFrameworkCore;

/// <summary>
/// The constraint sets <see cref="ConstraintSaveChangesInterceptor"/> checks, keyed by entity type. Configured once through
/// <see cref="ConstraintDbContextOptionsBuilderExtensions.UseConstraints(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, Action{EntityConstraints})"/>.
/// </summary>
public sealed class EntityConstraints
{
	private readonly Dictionary<Type, IEntityChecker> _checkers = [];
	// Resolved per CLR type the first time it is seen, including "nothing registered" (null), so a proxy or derived type walks its base types once.
	private readonly ConcurrentDictionary<Type, IEntityChecker?> _resolved = new();

	/// <summary>The clock handed to every rule (<c>NotInFuture</c>, <c>NotOlderThan</c>). <see cref="TimeProvider.System"/> when not set.</summary>
	public TimeProvider? Time { get; set; }

	/// <summary>
	/// Called after the checks pass for every entry whose rules reported only warnings or hints. These never block a save; this is where to log them.
	/// </summary>
	public Action<EntityEntry, IReadOnlyList<Violation>>? OnNotices { get; set; }

	/// <summary>Checks entities of type <typeparamref name="T"/> (and types derived from it) with <paramref name="constraints"/>, for example a companion's <c>OrderRules.All</c>.</summary>
	public EntityConstraints Add<T>(ConstraintSet<T> constraints) where T : class
	{
		ArgumentNullException.ThrowIfNull(constraints);
		_checkers[typeof(T)] = new EntityChecker<T>(constraints);
		_resolved.Clear();
		return this;
	}

	/// <summary>Checks entities of a type that declares its own constraints (<c>[Constraint]</c> fields or <c>[Constraints]</c> on the entity) with <c>T.Constraints</c>.</summary>
	public EntityConstraints Add<T>() where T : class, IHaveConstraints<T> => Add(T.Constraints);

	/// <summary>Whether an entity of <paramref name="type"/> is checked: a set is registered for it or for one of its base types.</summary>
	public bool IsRegistered(Type type) => Resolve(type) is not null;

	/// <summary>
	/// The violations of one tracked entry: <see cref="ConstraintSet{T}.Validate"/> when it is <see cref="Microsoft.EntityFrameworkCore.EntityState.Added"/>,
	/// <see cref="ConstraintSet{T}.Check"/> from its original values to its current state when it is <see cref="Microsoft.EntityFrameworkCore.EntityState.Modified"/>.
	/// Empty for any other state and for a type nothing is registered for.
	/// </summary>
	public IReadOnlyList<Violation> Check(EntityEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);
		if (Resolve(entry.Entity.GetType()) is not { } checker)
		{
			return [];
		}
		return entry.State switch
		{
			Microsoft.EntityFrameworkCore.EntityState.Added => checker.Validate(entry.Entity, Time),
			// ToObject() builds a new instance from the values EF read from the database (or last saved): the "before" a sequential rule needs.
			Microsoft.EntityFrameworkCore.EntityState.Modified => checker.Check(entry.OriginalValues.ToObject(), entry.Entity, Time),
			_ => [],
		};
	}

	private IEntityChecker? Resolve(Type type) =>
		_resolved.GetOrAdd(type, t =>
		{
			for (var current = t; current is not null; current = current.BaseType)
			{
				if (_checkers.TryGetValue(current, out var checker))
				{
					return checker;
				}
			}
			return null;
		});

	private interface IEntityChecker
	{
		IReadOnlyList<Violation> Validate(object entity, TimeProvider? time);
		IReadOnlyList<Violation> Check(object before, object after, TimeProvider? time);
	}

	private sealed class EntityChecker<T>(ConstraintSet<T> constraints) : IEntityChecker where T : class
	{
		public IReadOnlyList<Violation> Validate(object entity, TimeProvider? time) => constraints.Validate((T)entity, time);

		public IReadOnlyList<Violation> Check(object before, object after, TimeProvider? time) => constraints.Check((T)before, (T)after, time);
	}
}
