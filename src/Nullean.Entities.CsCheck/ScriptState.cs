// Nullean.Entities.CsCheck — testing a service by the state it is in, with no Rx and no event log.
//
//   script.SampleState(...)        run generated steps against a service; after every step read its state and check a ConstraintSet on the
//                                  (before, after) pair, so a sequential rule (a state machine's own transition rule) counts as well as an invariant
//   machine.ConformsTo(...)        generate sequences of a type-state machine's triggers, run them on the service, and check that the service ends
//                                  up where the machine says after each one (a refused trigger must leave it where it was)
//   machine.ConformToService(...)  ConformToMutable for a value machine, with async bindings
//
// Both run on CsCheck's own Sample, so a failure is shrunk to the shortest sequence and carries a seed.

using CsCheck;

namespace Nullean.Entities.CsCheck;

public static partial class ScriptExtensions
{
	/// <summary>
	/// Checks a service against the rules of the state it exposes. <paramref name="factory"/> builds a fresh service around the run's
	/// <see cref="TimeProvider"/> (a <see cref="ManualTimeProvider"/> that the script's delays advance); <paramref name="observe"/> reads the state
	/// the rules are about (return a snapshot, such as an immutable record: the previous state is kept to compare with); after every step
	/// <paramref name="rules"/> are checked on (previous, current), so <c>Transitions</c>, <c>Monotonic</c> and <c>Conserves</c> rules apply as well as
	/// point-in-time ones (a step that left the state unchanged, a refused operation, is not a transition and only the point-in-time ones are asked). An <see cref="Severity.Error"/> violation fails the run with the step, both states and a shrunk sequence.
	/// </summary>
	public static void SampleState<TSut, TState>(
		this Script<TSut> script,
		Func<TimeProvider, TSut> factory,
		Func<TSut, TState> observe,
		ConstraintSet<TState> rules,
		long iter = -1,
		string? seed = null
	) => BuildTimedGen(script, factory).Sample(run => CheckStates(run, observe, rules), seed: seed, iter: iter, print: run => run.Print());

	private static void CheckStates<TSut, TState>(TimedRun<TSut> run, Func<TSut, TState> observe, ConstraintSet<TState> rules)
	{
		var before = observe(run.Sut);
		var initial = Errors(rules.Validate(before, run.Time));
		if (initial.Count > 0)
		{
			throw new InvalidOperationException($"The service starts in a state that breaks a rule: {string.Join("; ", initial)}\n  state: {before}");
		}

		var done = new List<string>();
		foreach (var (delay, step) in run.Ops)
		{
			if (delay > TimeSpan.Zero)
			{
				run.Time.Advance(delay);
			}
			step.Act(run.Sut);
			done.Add(step.Name);

			var after = observe(run.Sut);
			// A step the service refused leaves the state as it was: that is a stay, not a transition (Apply treats an unchanged value the same way),
			// so a rule about changes is not asked about it. The invariants still are.
			var broken = Errors(
				EqualityComparer<TState>.Default.Equals(before, after) ? rules.Validate(after, run.Time) : rules.Check(before, after, run.Time)
			);
			if (broken.Count > 0)
			{
				throw new InvalidOperationException(
					$"After step {done.Count} ({step.Name}) a rule is broken: {string.Join("; ", broken)}\n  before: {before}\n  after:  {after}\n  steps:  {string.Join(" -> ", done)}"
				);
			}
			before = after;
		}
	}

	private static List<string> Errors(IReadOnlyList<Violation> violations) =>
		[.. violations.Where(v => v.Severity == Severity.Error).Select(v => v.ConstraintId is { } id ? $"{id}: {v.Message}" : v.Message)];
}

public static class StateMachineServiceExtensions
{
	/// <summary>
	/// Checks that a service behaves as a declared <see cref="StateMachine{TState}"/> says. Generates sequences of the machine's triggers, runs each on a
	/// fresh service with <paramref name="drive"/>, and after every one compares <paramref name="observe"/> with what the machine predicts:
	/// the next state when <c>Fire</c> accepts the trigger, the same state when it refuses (a guard, or a trigger not allowed from that case).
	/// So it finds a service that ships an unconfirmed order, and one that refuses a transition it should allow.
	/// <paramref name="drive"/> must not throw for a refused trigger (return the failure instead); any exception fails the check.
	/// Only triggers whose edge has a factory and takes no argument are explored; the others are listed by <see cref="StateMachine{TState}.Edges"/>
	/// but the machine cannot predict what they build.
	/// </summary>
	public static void ConformsTo<TSut, TState>(
		this StateMachine<TState> machine,
		Func<TimeProvider, TSut> factory,
		Func<TSut, TState> observe,
		Func<TSut, string, Task> drive,
		int minLength = 0,
		int maxLength = 12,
		long iter = -1,
		string? seed = null
	)
	{
		var triggers = machine.Edges.Where(e => e.Create is not null && e.ArgumentType is null).Select(e => e.Name).Distinct().ToArray();
		if (triggers.Length == 0)
		{
			throw new InvalidOperationException(
				"The machine has no edge with a factory that takes no argument, so there is nothing to generate: give its To<T>(...) a factory."
			);
		}

		Gen.OneOfConst(triggers)
			.Array[minLength, maxLength]
			.Sample(sequence => Conform(machine, factory, observe, drive, sequence), seed: seed, iter: iter, print: sequence => string.Join(" -> ", sequence));
	}

	private static void Conform<TSut, TState>(
		StateMachine<TState> machine,
		Func<TimeProvider, TSut> factory,
		Func<TSut, TState> observe,
		Func<TSut, string, Task> drive,
		string[] sequence
	)
	{
		var sut = factory(new ManualTimeProvider());
		var model = observe(sut);
		if (machine.CaseOf(model) != machine.Initial)
		{
			throw new InvalidOperationException($"The service starts in {machine.CaseOf(model).Name}, but the machine starts in {machine.Initial.Name}.");
		}

		for (var i = 0; i < sequence.Length; i++)
		{
			var trigger = sequence[i];
			var expected = machine.Fire(model, trigger) is Result<TState>.Ok ok ? ok.Value : model;

			drive(sut, trigger).GetAwaiter().GetResult();
			var actual = observe(sut);

			if (!EqualityComparer<TState>.Default.Equals(actual, expected))
			{
				var allowed = machine.Fire(model, trigger).IsOk;
				throw new InvalidOperationException(
					$"After step {i + 1} ({trigger}) the machine says {(allowed ? "the service moves to" : "the trigger is refused, so it stays in")} {expected}, but the service is in {actual}.\n  before: {model}\n  steps:  {string.Join(" -> ", sequence.Take(i + 1))}"
				);
			}
			model = expected;
		}
	}

	/// <summary><see cref="StateMachineSpecExtensions.ConformToMutable{TState, TTrigger, TEntity}"/> for a service whose operations are async: a fresh service per walk from <paramref name="create"/>.</summary>
	public static SpecReport ConformToService<TState, TTrigger, TSut>(
		this StateMachine<TState, TTrigger> machine,
		Func<TSut> create,
		Func<TSut, TState> stateOf,
		Action<string>? writeLine = null,
		long iter = -1,
		params (TTrigger Trigger, Func<TSut, Task> Apply)[] bindings
	) where TState : notnull where TTrigger : notnull =>
		machine.ConformToMutable(
			create,
			stateOf,
			writeLine,
			iter,
			[.. bindings.Select(b => (b.Trigger, (Action<TSut>)(sut => b.Apply(sut).GetAwaiter().GetResult())))]
		);
}
