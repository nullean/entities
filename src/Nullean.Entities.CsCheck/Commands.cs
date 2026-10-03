// Nullean.Entities.CsCheck — commands with arguments, for EntitySpec.EventSourced and FromReducer.
//
// An event-sourced model takes a list of commands. A command that carries a value (Pay(50m)) is one command per value you want explored, which is the same
// argument domain an action gets on EntitySpec: a few plausible values plus the edge values that matter for the type.

namespace Nullean.Entities.CsCheck;

public static class Commands
{
	/// <summary>One command per listed value: <c>Commands.Over(a =&gt; new Pay(a), 50m, 100m)</c>.</summary>
	public static IReadOnlyList<TCommand> Over<TArg, TCommand>(Func<TArg, TCommand> make, params TArg[] values) => [.. values.Select(make)];

	/// <summary>
	/// One command per value of the argument type's default domain: two plausible values plus the edge values that matter for the type (<c>0</c> and <c>-1</c>
	/// for a number, <c>""</c> and <c>" "</c> for a string, <c>NaN</c> for a double, every member of an enum), exactly what an action argument gets.
	/// Pass <see cref="AutoMode.Plausible"/> as <paramref name="data"/> to leave the edge values out.
	/// </summary>
	public static IReadOnlyList<TCommand> FromDomain<TArg, TCommand>(Func<TArg, TCommand> make, AutoMode? data = null) =>
		[.. AutoDefaults.Domain(typeof(TArg), data).Cast<TArg>().Select(make)];

	/// <summary>Several kinds of command in one list: <c>Commands.Of(Commands.Over(...), new[] { new Void() })</c>.</summary>
	public static IReadOnlyList<TCommand> Of<TCommand>(params IEnumerable<TCommand>[] groups) => [.. groups.SelectMany(g => g)];
}
