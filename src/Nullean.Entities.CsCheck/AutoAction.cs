// Nullean.Entities.CsCheck — an operation an axis test can run, with its arguments drawn from the value it runs on.
//
// An EntityAction names a mutation and the type of its argument, so a test draws that argument from the type alone. That is not enough when the
// argument only makes sense for the start value: adding a registry entry that collides with a key already there, removing an item that exists.
// An AutoAction draws the (start, argument) pair together, so the argument can be built from the start.

using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>
/// One operation on <typeparamref name="T"/> as an axis test runs it: <paramref name="Draw"/> turns a generator of start values into a generator of
/// (start, argument) pairs, and <paramref name="Apply"/> runs it. Built with <see cref="AutoAction.Of{T}"/>, <see cref="AutoAction.Of{T, TArg}"/> or
/// <see cref="AutoAction.From{T}"/>; an operation that returns a wrapper (a <c>Result&lt;T&gt;</c>) is adapted by reading the new value out in <c>apply</c>.
/// </summary>
public sealed record AutoAction<T>(string Name, Func<Gen<T>, Gen<(T Start, object? Argument)>> Draw, Func<T, object?, T> Apply)
{
}

/// <summary>Builds <see cref="AutoAction{T}"/>s.</summary>
public static class AutoAction
{
	/// <summary>The same operation as an <see cref="EntityAction{TEntity}"/>, with its argument drawn from the argument type alone.</summary>
	public static AutoAction<T> From<T>(EntityAction<T> action, AutoMode data = AutoMode.Mixed) =>
		new(
			action.Name,
			starts => Gen.Select(starts, AutoDefaults.ArgumentGen(action.ArgumentType, data), (start, argument) => (start, argument)),
			action.Apply
		);

	// Without a generator the argument is drawn from its type alone, which for a key or an entry rarely names anything the start holds. So half the draws harvest a value of that type
	// from inside the start (an entry the registry has), and half of those are changed a little (an entry that is older, newer, or someone else's): the cases an operation decides on.
	private static Gen<object?> Argument<T>(T start, Type argumentType, AutoMode data)
	{
		var generated = AutoDefaults.ArgumentGen(argumentType, data);
		var harvested = CounterExampleSearch.Harvest(start, argumentType);
		if (harvested.Count == 0)
		{
			return generated;
		}
		var own = Gen.OneOfConst([.. harvested.Select(h => (object?)h)]);
		var changed = harvested
			.Take(8)
			.SelectMany(h => CounterExampleSearch.Perturbations(argumentType, h).Select(member => (Original: h, Changes: member)))
			.ToArray();
		var options = new List<(int, IGen<object?>)> { (2, generated), (3, own) };
		if (changed.Length > 0)
		{
			// a member first, then a change to it: every member is as likely to be the one changed, however many edge values it has
			options.Add((3, Gen.OneOfConst(changed).SelectMany(c => Gen.OneOfConst([.. c.Changes]).Select(change => change(c.Original)))));
		}
		return Gen.Frequency([.. options]);
	}

	/// <summary>An operation with no argument.</summary>
	public static AutoAction<T> Of<T>(string name, Func<T, T> apply) =>
		new(name, starts => starts.Select(start => (start, (object?)null)), (value, _) => apply(value));

	/// <summary>
	/// An operation with one argument. Without <paramref name="arguments"/> the argument is drawn from its type; with it, from the start value
	/// (so it can name something that is really there).
	/// </summary>
	public static AutoAction<T> Of<T, TArg>(string name, Func<T, TArg, T> apply, Func<T, Gen<TArg>>? arguments = null, AutoMode data = AutoMode.Mixed)
	{
		var draw = arguments is null
			? (Func<Gen<T>, Gen<(T Start, object? Argument)>>)(starts =>
				starts.SelectMany(start => Argument(start, typeof(TArg), data).Select(argument => (start, argument))))
			: starts => starts.SelectMany(start => arguments(start).Select(argument => (start, (object?)argument)));
		return new AutoAction<T>(name, draw, (value, argument) => apply(value, (TArg)argument!));
	}
}
