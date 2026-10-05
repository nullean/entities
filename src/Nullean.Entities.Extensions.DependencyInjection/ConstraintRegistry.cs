using System.Collections.Concurrent;

namespace Nullean.Entities.Extensions.DependencyInjection;

/// <summary>A type-erased lookup from a type to the constraints that validate it, so a framework hook that only holds an <see cref="object"/> can validate it without reflection.</summary>
public interface IConstraintRegistry
{
	bool IsRegistered(Type type);

	/// <summary>Validates <paramref name="instance"/> when its exact type is registered. False means nothing is registered for it, not that it is valid.</summary>
	bool TryValidate(object instance, out IReadOnlyList<Violation> violations);
}

public sealed class ConstraintRegistry : IConstraintRegistry
{
	private readonly ConcurrentDictionary<Type, Func<object, IReadOnlyList<Violation>>> _validators = new();
	private readonly TimeProvider? _time;

	public ConstraintRegistry(TimeProvider? time = null) => _time = time;

	public ConstraintRegistry Add<T>(ConstraintSet<T> constraints)
	{
		_validators[typeof(T)] = o => constraints.Validate((T)o, _time);
		return this;
	}

	public bool IsRegistered(Type type) => _validators.ContainsKey(type);

	public bool TryValidate(object instance, out IReadOnlyList<Violation> violations)
	{
		if (_validators.TryGetValue(instance.GetType(), out var validate))
		{
			violations = validate(instance);
			return true;
		}
		violations = [];
		return false;
	}
}
