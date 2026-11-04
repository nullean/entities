// Nullean.Entities.CsCheck — linearizability: does a shared implementation behave, under concurrent calls, as if the calls happened one at a time?
//
// CsCheck's SampleParallel runs a few operations sequentially, then a few on separate threads at once, and passes when at least one order of the
// parallel ones, run one after another on the model, ends where the implementation ended. The entity is that model: every action of the spec is an
// operation, its argument drawn from the action's domain, run on the implementation by name and on the entity by the action itself.

using CsCheck;

namespace Nullean.Entities.CsCheck;

public sealed partial class EntitySpec<T>
{
	/// <summary>
	/// Checks that an implementation shared between threads is linearizable against this entity: CsCheck runs a random sequence of the spec's actions
	/// on it, then several at once on separate threads, and at least one order of those, applied one after another to the entity, must end in a state
	/// <paramref name="agrees"/> accepts. A failure (a lost update, a torn read-modify-write) is shrunk to the fewest operations and carries a seed.
	/// </summary>
	/// <param name="create">A fresh implementation per sample, in the state of the first starting entity.</param>
	/// <param name="apply">Performs one action on the implementation, by name, with its argument (null for an action that takes none). Called from several threads at once.</param>
	/// <param name="agrees">Whether the implementation's final state is the entity's.</param>
	/// <param name="maxSequential">The most operations run one after another before the parallel part.</param>
	/// <param name="maxParallel">The most operations run at once; every order of them is tried, so keep it small.</param>
	/// <param name="seed">Replays one sample: the seed a failure printed.</param>
	/// <param name="iter">How many samples to run (CsCheck's default when -1).</param>
	/// <param name="time">Run for this many seconds instead of a number of samples (-1: off).</param>
	/// <param name="threads">How many samples run at once (CsCheck's default when -1).</param>
	/// <param name="writeLine">Where CsCheck writes its progress, if anywhere.</param>
	/// <remarks>
	/// Only the final state is compared, not what each call returned. Races that need more threads than <paramref name="maxParallel"/>, or timing the
	/// sample never hits, can pass: a pass is evidence, not a proof.
	/// </remarks>
	public void Linearizable<TSut>(
		Func<TSut> create,
		Action<TSut, string, object?> apply,
		Func<TSut, T, bool> agrees,
		int maxSequential = 10,
		int maxParallel = 5,
		string? seed = null,
		long iter = -1,
		int time = -1,
		int threads = -1,
		Action<string>? writeLine = null
	)
	{
		if (_actions.Count == 0)
		{
			throw new InvalidOperationException("No actions to run: declare them with .Action(...) or generate them with [Constraints].");
		}
		var start = Starts()[0];
		CheckStart(start);

		var operations = new GenOperation<TSut, EntityHolder<T>>[_actions.Count];
		for (var i = 0; i < _actions.Count; i++)
		{
			var def = _actions[i];
			var arguments = def.ArgumentType is null ? ArgGen(null) : ArgGen(def.OwnDomain ?? DomainOf(def.ArgumentType));
			operations[i] = GenOperation.Create<TSut, EntityHolder<T>, object?>(
				arguments,
				arg => def.ArgumentType is null ? def.Name : $"{def.Name}({Convert.ToString(arg, System.Globalization.CultureInfo.InvariantCulture)})",
				(sut, arg) => apply(sut, def.Name, arg),
				(model, arg) => model.Entity = Run(model.Entity, def, arg)
			);
		}

		var initial = Gen.Const(() => (create(), new EntityHolder<T>(start)));
		SampleCall.Run(() =>
				initial.SampleParallel(
					operations,
					(sut, model) => agrees(sut, model.Entity),
					seed: seed,
					maxSequentialOperations: maxSequential,
					maxParallelOperations: maxParallel,
					iter: iter,
					time: time,
					threads: threads,
					printModel: model => _print(model.Entity),
					writeLine: writeLine
				));
	}
}
