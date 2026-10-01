// Nullean.Entities.CsCheck — Script<TSut> under CsCheck's linearizability check.
//
// SampleWith runs one thread at a time. SampleParallelWith runs a sequential prefix and then a batch
// of steps concurrently against one SUT, and requires the constraints to hold however the concurrent
// steps interleave. Delays are not used: virtual time is single-threaded.

using System.Reactive.Concurrency;
using CsCheck;
using Nullean.Entities.Reactive;

namespace Nullean.Entities.CsCheck;

internal sealed class ParallelRun<TSut, TEvent>(TSut sut, MonitoredConstraintSet<TEvent> monitored)
{
	public TSut Sut { get; } = sut;

	public bool Violated => monitored.AnyViolated;
}

public static partial class ScriptExtensions
{
	/// <summary>
	/// Runs the script's steps concurrently and fails if the constraints are violated by an
	/// interleaving that no sequential order of the same steps produces — a race. Use alongside
	/// <c>SampleWith</c>, which already covers violations that need no concurrency. The SUT must
	/// tolerate concurrent calls (and raise events in a thread-safe way); that is what this tests.
	/// </summary>
	public static void SampleParallelWith<TSut, TEvent>(
		this Script<TSut> script,
		Func<IScheduler, TSut> factory,
		Func<TSut, IObservable<TEvent>> events,
		ReactiveConstraintSet<TEvent> constraints,
		int maxSequentialOperations = 10,
		int maxParallelOperations = 5,
		long iter = -1,
		string? seed = null
	)
	{
		if (script.Steps.Count == 0)
		{
			throw new InvalidOperationException("Script<TSut> has no steps — call Step(...) at least once before SampleParallelWith.");
		}

		var initial = Gen.Const(() =>
		{
			var scheduler = new HistoricalScheduler();
			var sut = factory(scheduler);
			return new ParallelRun<TSut, TEvent>(sut, constraints.Attach(events(sut), scheduler));
		});
		var operation = Gen.OneOf([.. script.Steps]).Operation<ParallelRun<TSut, TEvent>>(step => step.Name, (run, step) => step.Act(run.Sut));

		initial.SampleParallel(
			[operation],
			equal: (a, b) => a.Violated == b.Violated,
			seed: seed,
			maxSequentialOperations: maxSequentialOperations,
			maxParallelOperations: maxParallelOperations,
			iter: iter,
			print: run => run.Violated ? "constraints violated" : "constraints held"
		);
	}
}
