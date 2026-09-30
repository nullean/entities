// Nullean.Entities.CsCheck — drives a Script<TSut> under CsCheck and checks a
// ReactiveConstraintSet<TEvent> against the resulting event stream on every iteration.
//
// See the note in Script.cs: these overloads are declared against the final API shape
// but still throw NotImplementedException. Nothing in this repo's test/example suite
// calls them at runtime yet — the examples that reference SampleWith/RunWith compile,
// demonstrating the intended call shape, but are not executed by `./build.sh test`.
//
// No doc comments yet (checkpoint-A exploration code) — CS1591 is suppressed project-wide
// in Directory.Build.props rather than per file.

using System.Reactive.Concurrency;
using Nullean.Entities.Reactive;

// No `using Nullean.Entities;` needed: Nullean.Entities.CsCheck is nested inside it, and C#
// name lookup walks up enclosing namespaces, so IHaveConstraints<T> below resolves unqualified.
namespace Nullean.Entities.CsCheck;

public static class ScriptExtensions
{
	// Every parameter below is genuinely unused: each overload's body is a NotImplementedException
	// throw, declared against the final API shape but not yet implemented (see the file-level note
	// above).
#pragma warning disable IDE0060

	// Primary overload: explicit constraint set, any event type.
	public static void SampleWith<TSut, TEvent>(
		this Script<TSut> script,
		Func<IScheduler, TSut> factory,
		Func<TSut, IObservable<TEvent>> events,
		ReactiveConstraintSet<TEvent> constraints,
		long iter = -1,
		string? seed = null
	) => throw new NotImplementedException("SampleWith not yet implemented");

	// Convenience overload: event type carries its own constraints (IHaveConstraints<TEvent>).
	// The C# 11 static-abstract constraint eliminates the separate constraints argument.
	public static void SampleWith<TSut, TEvent>(
		this Script<TSut> script,
		Func<IScheduler, TSut> factory,
		Func<TSut, IObservable<TEvent>> events,
		long iter = -1,
		string? seed = null
	) where TEvent : IHaveConstraints<TEvent> => throw new NotImplementedException("SampleWith not yet implemented");

	// RunWith: exposes the whole run as an event stream (strand 3).
	public static IObservable<EntityEvent> RunWith<TSut, TEvent>(
		this Script<TSut> script,
		Func<IScheduler, TSut> factory,
		Func<TSut, IObservable<TEvent>> events,
		ReactiveConstraintSet<TEvent> constraints,
		long iter = 1_000
	) => throw new NotImplementedException("RunWith not yet implemented");

	public static IObservable<EntityEvent> RunWith<TSut, TEvent>(
		this Script<TSut> script,
		Func<IScheduler, TSut> factory,
		Func<TSut, IObservable<TEvent>> events,
		long iter = 1_000
	) where TEvent : IHaveConstraints<TEvent> => throw new NotImplementedException("RunWith not yet implemented");

#pragma warning restore IDE0060
}
