// Nullean.Entities.CsCheck — a class that is changed in place (a service, a mutable entity) as the subject of an AutoTest.
//
//   AutoTest.For(rules, clock => new AccountService(clock))
//       .Mutate<int>("Deposit", (s, n) => s.Deposit(n))
//       .MutateAsync("Settle", s => s.SettleAsync())
//       .Steps(1, 6)
//
// The axes compare a value before an operation with the value after it. A record gives both for free; an object changed in place does not, because the
// operation overwrites the before. So the "after" is built on a fresh instance: the factory, then the operations that led to the before, then the one being
// tested. The "before" is never touched again and stays what it was. That needs the subject to be deterministic (the same operations with the same
// arguments reach the same state), which a test of a service that reads a clock, a random number or the network is not, unless those are faked.

using System.Runtime.CompilerServices;
using CsCheck;

namespace Nullean.Entities.CsCheck;

internal interface IDescribeCall
{
	string Describe(string operation);
}

/// <summary>One mutating operation as registered with <c>Mutate</c>: how to draw its argument, and how to run it on an instance.</summary>
internal sealed record SubjectOp<T>(string Name, Func<AutoMode, Func<Gen<T>, Gen<(T Start, object? Argument)>>> Draw, Func<T, object?, Task> Run);

internal sealed class Subject<T>(Func<T> create)
{
#pragma warning disable IDE0028 // a ConditionalWeakTable has an Add, but it is not a collection of values
	private readonly ConditionalWeakTable<object, List<Applied>> _history = new();
#pragma warning restore IDE0028

	public Gen<T> Starts => Gen.Const(0).Select(_ => Fresh());

	private T Fresh()
	{
		var instance = create();
		_history.AddOrUpdate(instance!, []);
		return instance;
	}

	private async Task RunAsync(T instance, Applied step)
	{
		await step.Run(instance, step.Argument).ConfigureAwait(false);
		_history.GetOrCreateValue(instance!).Add(step);
	}

	public IReadOnlyList<AutoAction<T>> Actions(IReadOnlyList<SubjectOp<T>> ops, AutoMode data, (int Min, int Max) steps)
	{
		var prefix = PrefixGen(ops, data, steps);
		return [.. ops.Select(op => Action(op, op.Draw(data), prefix))];
	}

	// The operations that lead up to the one tested, drawn from the same operations, so the operation under test also runs from states other than a fresh one.
	private Gen<Applied[]> PrefixGen(IReadOnlyList<SubjectOp<T>> ops, AutoMode data, (int Min, int Max) steps)
	{
		var before = Math.Max(steps.Min - 1, 0);
		var most = Math.Max(steps.Max - 1, before);
		if (ops.Count == 0 || most == 0)
		{
			return Gen.Const<Applied[]>([]);
		}
		var one = Gen.OneOf([.. ops.Select(op => op.Draw(data)(Starts).Select(drawn => new Applied(op.Name, drawn.Argument, op.Run)))]);
		return one.Array[before, most];
	}

	private AutoAction<T> Action(SubjectOp<T> op, Func<Gen<T>, Gen<(T Start, object? Argument)>> draw, Gen<Applied[]> prefix) =>
		new(op.Name, starts =>
			draw(starts).SelectMany(drawn => prefix.Select(steps => (drawn.Start, (object?)new Planned(op.Name, drawn.Argument, steps)))), async (
			start,
			planned
		) =>
		{
			var plan = (Planned)planned!;
			foreach (var step in plan.Prefix)
			{
				await RunAsync(start, step).ConfigureAwait(false);
			}
			var after = Fresh();
			foreach (var step in _history.GetOrCreateValue(start!).ToArray())
			{
				await RunAsync(after, step).ConfigureAwait(false);
			}
			await RunAsync(after, new Applied(op.Name, plan.Argument, op.Run)).ConfigureAwait(false);
			return after;
		});

	private sealed record Applied(string Name, object? Argument, Func<T, object?, Task> Run)
	{
		public string Describe() => Argument is null ? $"{Name}()" : $"{Name}({ConstraintProperties.SafePrint(Argument)})";
	}

	private sealed record Planned(string Name, object? Argument, Applied[] Prefix) : IDescribeCall
	{
		public string Describe(string operation)
		{
			var call = Argument is null ? $"{Name}()" : $"{Name}({ConstraintProperties.SafePrint(Argument)})";
			return Prefix.Length == 0 ? call : $"{call}, after {string.Join(", ", Prefix.Select(p => p.Describe()))}";
		}
	}
}

public sealed partial class AutoTest<T>
{
	/// <summary>
	/// An operation that changes the subject in place. Each run builds the "after" on a fresh instance (see <see cref="AutoTest.For{T}(ConstraintSet{T}, Func{T})"/>),
	/// so the "before" the axes compare it with is never overwritten.
	/// </summary>
	public AutoTest<T> Mutate(string name, Action<T> apply) =>
		AddOp(new SubjectOp<T>(name, data => AutoAction.Of<T>(name, t => t).Draw, (t, _) =>
		{
			apply(t);
			return Task.CompletedTask;
		}));

	/// <summary>A mutating operation with one argument. <paramref name="arguments"/> draws it from the start, so it can name something that is really there.</summary>
	public AutoTest<T> Mutate<TArg>(string name, Action<T, TArg> apply, Func<T, Gen<TArg>>? arguments = null) =>
		AddOp(new SubjectOp<T>(name, data => AutoAction.Of<T, TArg>(name, (t, _) => t, arguments, data).Draw, (t, a) =>
		{
			apply(t, (TArg)a!);
			return Task.CompletedTask;
		}));

	/// <summary>A mutating operation that is awaited. Use a subject whose awaits complete by themselves (in-memory fakes, a manual clock), so that a failure replays.</summary>
	public AutoTest<T> MutateAsync(string name, Func<T, Task> apply) =>
		AddOp(new SubjectOp<T>(name, data => AutoAction.Of<T>(name, t => t).Draw, (t, _) => apply(t)));

	/// <summary>A mutating operation with one argument that is awaited.</summary>
	public AutoTest<T> MutateAsync<TArg>(string name, Func<T, TArg, Task> apply, Func<T, Gen<TArg>>? arguments = null) =>
		AddOp(new SubjectOp<T>(name, data => AutoAction.Of<T, TArg>(name, (t, _) => t, arguments, data).Draw, (t, a) => apply(t, (TArg)a!)));

	/// <summary>How many operations one run applies to a subject, the last of them the one being tested. The default is 1 to 5.</summary>
	public AutoTest<T> Steps(int min, int max) =>
		_subject is null ? throw NeedsFactory(nameof(Steps)) : new(this, steps: (min, max));

	private AutoTest<T> AddOp(SubjectOp<T> op) =>
		_subject is null ? throw NeedsFactory("Mutate") : new(this, ops: [.. _ops, op]);

	// Public methods of the subject that no registered operation is named after: visible, never attacked. A name matches its method or the method without "Async".
	private IEnumerable<string> UnusedMethods()
	{
		var names = _ops.Select(o => o.Name).Concat(_makers.Select(m => m(_settings.Data).Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		return typeof(T)
			.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
			.Where(m => !m.IsSpecialName)
			.Select(m => m.Name)
			.Distinct(StringComparer.Ordinal)
			.Where(n => !names.Contains(n) && !names.Contains(n.EndsWith("Async", StringComparison.Ordinal) ? n[..^5] : n));
	}

	private static InvalidOperationException NeedsFactory(string method) =>
		new(
			$"{method} works on a subject built by a factory: AutoTest.For(rules, () => new Service()). A record-style AutoTest uses Action(...), which returns the next value."
		);
}

public static partial class AutoTest
{
	/// <summary>
	/// An auto-test over a class that is changed in place, built by <paramref name="create"/>. Register its operations with <c>Mutate</c> / <c>MutateAsync</c>.
	/// Each run applies a short sequence of them to fresh instances, so the rules are checked after every step and the "before" an operation is compared with is never overwritten.
	/// The factory and the operations must be deterministic: the same calls reach the same state.
	/// </summary>
	public static AutoTest<T> For<T>(ConstraintSet<T> constraints, Func<T> create) where T : class
	{
		var subject = new Subject<T>(create);
		return new AutoTest<T>(constraints, subject.Starts, null, subject);
	}

	/// <summary>The same for a subject that reads a clock: each instance gets its own <see cref="ManualTimeProvider"/>.</summary>
	public static AutoTest<T> For<T>(ConstraintSet<T> constraints, Func<TimeProvider, T> create) where T : class =>
		For(constraints, () => create(new ManualTimeProvider()));
}
