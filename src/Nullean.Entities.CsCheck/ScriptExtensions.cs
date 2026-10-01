// Nullean.Entities.CsCheck — drives a Script<TSut> under CsCheck and checks a
// ReactiveConstraintSet<TEvent> against the resulting event stream on every iteration.
//
// The trick: build a real CsCheck Gen<ScriptRun<TSut>> — a fresh Sut (built by the caller's
// factory against a fresh virtual-time scheduler) plus a generated, shrinkable array of
// (delay, step) pairs — and hand it to Gen<T>.Sample. CsCheck owns generation, shrinking, seed
// replay and multithreading; this file only supplies "what a run is" and "did anything violate".
// There is no hand-rolled model here (Model would be Unit, ignored): the "model" a model-based
// test would compare against is exactly the monitored constraint set's own current state, so
// the predicate is just `!monitored.AnyViolated` — nothing else needs comparing.
//
// No doc comments yet (checkpoint-A exploration code) — CS1591 is suppressed project-wide
// in Directory.Build.props rather than per file.

using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using CsCheck;
using Nullean.Entities.Reactive;

// No `using Nullean.Entities;` needed: Nullean.Entities.CsCheck is nested inside it, and C#
// name lookup walks up enclosing namespaces, so IHaveConstraints<T> below resolves unqualified.
namespace Nullean.Entities.CsCheck;

/// <summary>
/// One generated iteration: a Sut built against its own fresh virtual-time scheduler, plus the
/// generated (and — on a failure — shrunk) sequence of delayed steps to run against it. Building
/// the Sut happens inside the Gen's own Select, not before it, so every shrink attempt gets an
/// untouched instance — reusing one across attempts would let an earlier attempt's mutations leak
/// into the next.
/// </summary>
internal sealed class ScriptRun<TSut>
{
	public TSut Sut { get; }
	public HistoricalScheduler Scheduler { get; }
	public (TimeSpan Delay, ScriptStep<TSut> Step)[] Ops { get; }

	public ScriptRun(TSut sut, HistoricalScheduler scheduler, (TimeSpan Delay, ScriptStep<TSut> Step)[] ops)
	{
		Sut = sut;
		Scheduler = scheduler;
		Ops = ops;
	}

	/// <summary>Advances virtual time by each step's delay, then runs the step — in generation order.</summary>
	public void Execute()
	{
		foreach (var (delay, step) in Ops)
		{
			if (delay > TimeSpan.Zero)
			{
				Scheduler.AdvanceBy(delay);
			}
			step.Act(Sut);
		}
	}

	/// <summary>Human-readable trace for CsCheck's own failure report and <see cref="EntityEvent.SmallerFailure"/>.</summary>
	public string Print() =>
		Ops.Length == 0 ? "(no steps)" : string.Join(" -> ", Ops.Select(o => o.Delay > TimeSpan.Zero ? $"[+{o.Delay}] {o.Step.Name}" : o.Step.Name));
}

public static partial class ScriptExtensions
{
	// Matches the quoted seed in CsCheck's own "Set seed: \"...\" or -e CsCheck_Seed=... to
	// reproduce (...)." exception message — the documented, user-facing reproduction string, not
	// an internal format this is guessing at. Source-generated (not `new Regex(...)`) so this stays
	// native-AOT compatible — see AGENTS.md's "no reflection, no dynamic codegen" rule.
	[GeneratedRegex("\"([^\"]+)\"")]
	private static partial Regex SeedPattern();

	private static Gen<ScriptRun<TSut>> BuildGen<TSut>(Script<TSut> script, Func<IScheduler, TSut> factory)
	{
		if (script.Steps.Count == 0)
		{
			throw new InvalidOperationException("Script<TSut> has no steps — call Step(...) at least once before SampleWith/RunWith.");
		}

		var stepGen = Gen.OneOf(script.Steps.ToArray());
		var opGen = Gen.Select(script.Delay, stepGen, (delay, step) => (delay, step));
		var opsGen = opGen.Array[script.MinLength, script.MaxLength];

		return opsGen.Select(ops =>
		{
			var scheduler = new HistoricalScheduler();
			var sut = factory(scheduler);
			return new ScriptRun<TSut>(sut, scheduler, ops);
		});
	}

	private static bool RunAndCheck<TSut, TEvent>(ScriptRun<TSut> run, Func<TSut, IObservable<TEvent>> events, ReactiveConstraintSet<TEvent> constraints)
	{
		var monitored = constraints.Attach(events(run.Sut), run.Scheduler);
		run.Execute();
		return !monitored.AnyViolated;
	}

	private static string? ExtractSeed(string exceptionMessage)
	{
		var match = SeedPattern().Match(exceptionMessage);
		return match.Success ? match.Groups[1].Value : null;
	}

	// Primary overload: explicit constraint set, any event type.
	public static void SampleWith<TSut, TEvent>(
		this Script<TSut> script,
		Func<IScheduler, TSut> factory,
		Func<TSut, IObservable<TEvent>> events,
		ReactiveConstraintSet<TEvent> constraints,
		long iter = -1,
		string? seed = null
	) => BuildGen(script, factory).Sample(run => RunAndCheck(run, events, constraints), seed: seed, iter: iter, print: run => run.Print());

	// Convenience overload: event type carries its own constraints (IHaveConstraints<TEvent>).
	// The C# 11 static-abstract constraint eliminates the separate constraints argument.
	public static void SampleWith<TSut, TEvent>(
		this Script<TSut> script,
		Func<IScheduler, TSut> factory,
		Func<TSut, IObservable<TEvent>> events,
		long iter = -1,
		string? seed = null
	) where TEvent : IHaveConstraints<TEvent> => script.SampleWith(factory, events, TEvent.All.ToReactive(), iter, seed);

	// RunWith: exposes the whole run as an event stream (strand 3). Scope, deliberately: this
	// drives one CsCheck.Sample call (so generation, shrinking and seed replay are all real) and
	// reports the outcome as EntityEvents rather than throwing. It does not emit a
	// ConstraintResolved per iteration while sampling runs — CsCheck's own Sample loop does not
	// expose a per-iteration hook to observe from outside — only for the final, already-shrunk
	// failing case (replayed once, deterministically, from the seed CsCheck's own exception
	// reports), which is where "which constraint" actually matters.
	public static IObservable<EntityEvent> RunWith<TSut, TEvent>(
		this Script<TSut> script,
		Func<IScheduler, TSut> factory,
		Func<TSut, IObservable<TEvent>> events,
		ReactiveConstraintSet<TEvent> constraints,
		long iter = 1_000
	) =>
		Observable.Create<EntityEvent>(observer =>
		{
			var gen = BuildGen(script, factory);
			var seed = PCG.ThreadPCG.ToString();
			observer.OnNext(new EntityEvent.IterationStarted(iter, seed));
			try
			{
				gen.Sample(run => RunAndCheck(run, events, constraints), seed: seed, iter: iter, print: run => run.Print());
				observer.OnNext(new EntityEvent.Completed(iter, Passed: true));
			}
			catch (CsCheckException ex)
			{
				// The seed in the exception is already the shrunk, minimal-failing-case seed —
				// CsCheck reports the reproduction seed for the smallest counterexample it found,
				// not the original large one. Replaying it deterministically regenerates that
				// exact case so the specific violated constraints can be read back off it.
				var shrunkSeed = ExtractSeed(ex.Message) ?? seed;
				var replay = gen.Generate(PCG.Parse(shrunkSeed), null, out _);
				var monitored = constraints.Attach(events(replay.Sut), replay.Scheduler);
				replay.Execute();

				foreach (var (name, verdict) in monitored.Violations)
				{
					observer.OnNext(new EntityEvent.ConstraintResolved(iter, name, verdict));
				}

				observer.OnNext(new EntityEvent.SmallerFailure(shrunkSeed, Trace: [replay.Print()], Violations: [.. monitored.Violations.Select(v => v.Name)]));
				observer.OnNext(new EntityEvent.Completed(iter, Passed: false));
			}
			observer.OnCompleted();
			return Disposable.Empty;
		});

	public static IObservable<EntityEvent> RunWith<TSut, TEvent>(
		this Script<TSut> script,
		Func<IScheduler, TSut> factory,
		Func<TSut, IObservable<TEvent>> events,
		long iter = 1_000
	) where TEvent : IHaveConstraints<TEvent> => script.RunWith(factory, events, TEvent.All.ToReactive(), iter);

	/// <summary>
	/// Confluence: two generated values <c>a</c>, <c>b</c>, applied to two fresh instances via
	/// <paramref name="path1"/> and <paramref name="path2"/> respectively, must leave the two
	/// instances <paramref name="equal"/> — the standard metamorphic-testing shape (e.g. "adding
	/// two lines in either order gives the same order"), riding CsCheck's own generate/shrink/replay
	/// rather than a hand-rolled two-instance comparison. <paramref name="script"/> itself
	/// contributes nothing here (no steps, no delays) — it only carries <typeparamref name="TSut"/>
	/// so the call reads <c>Script.For&lt;TSut&gt;().Metamorphic(...)</c> the same as every other
	/// entry point in this file.
	/// </summary>
#pragma warning disable IDE0060 // script: the receiver only carries TSut so the call reads Script.For<TSut>().Metamorphic(...); see the doc comment above.
	public static void Metamorphic<TSut, TArg>(
		this Script<TSut> script,
		Func<TSut> factory,
		Gen<TArg> gen,
		Action<TSut, TArg, TArg> path1,
		Action<TSut, TArg, TArg> path2,
		Func<TSut, TSut, bool> equal,
		long iter = -1,
		string? seed = null
	) =>
		Gen
			.Select(gen, gen, (a, b) => (a, b))
			.Select(pair => (pair.a, pair.b, A: factory(), B: factory()))
			.Sample(t => RunMetamorphic(t.A, t.B, t.a, t.b, path1, path2, equal), seed: seed, iter: iter);
#pragma warning restore IDE0060

	private static bool RunMetamorphic<TSut, TArg>(
		TSut a,
		TSut b,
		TArg x,
		TArg y,
		Action<TSut, TArg, TArg> path1,
		Action<TSut, TArg, TArg> path2,
		Func<TSut, TSut, bool> equal
	)
	{
		path1(a, x, y);
		path2(b, x, y);
		return equal(a, b);
	}
}
