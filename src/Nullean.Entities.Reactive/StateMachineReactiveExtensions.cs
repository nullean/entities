// Nullean.Entities.Reactive — lifts a StateMachine<TState> onto an event stream.
//
// AsReactive doesn't reimplement transition-checking: it derives TState from each TEvent via
// stateOf and hands the (previous, next) pair straight to Constraints<TEvent>.Transitions(Func<TEvent,
// TEvent, bool>) — the exact same predicate-based overload ReactiveConstraints.cs already carries
// for a case type that cannot satisfy `where T : TEvent`, reused here rather than duplicated,
// since StateMachine<TState>.Allows has the identical shape (a predicate over a raw (prev, next)
// pair, not a generic constraint).
//
// No doc comments yet (checkpoint-A exploration code) - CS1591 is suppressed project-wide
// in Directory.Build.props rather than per file.

namespace Nullean.Entities.Reactive;

public static class StateMachineReactiveExtensions
{
	/// <summary>
	/// Violated whenever two consecutive events' derived states (via <paramref name="stateOf"/>)
	/// aren't a transition <paramref name="machine"/> allows — see
	/// <see cref="Entities.StateMachine{TState}.Allows"/>, including its guard. <paramref name="message"/>
	/// is optional, same as every other <c>Constraints&lt;TEvent&gt;</c> builder.
	/// </summary>
	public static ReactiveConstraint<TEvent> AsReactive<TState, TEvent>(
		this Entities.StateMachine<TState> machine,
		Func<TEvent, TState> stateOf,
		string? message = null
	) =>
		Constraints<TEvent>.Transitions((prev, next) => machine.Allows(stateOf(prev), stateOf(next))).Named(
			message ?? $"StateMachine<{typeof(TState).Name}>.AsReactive"
		);
}
