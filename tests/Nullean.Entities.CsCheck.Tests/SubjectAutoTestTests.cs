using AwesomeAssertions;
using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// A class changed in place: the operation overwrites the very object a "before" would be read from.
internal sealed class MutableCounter(bool resetsOnOverflow = false)
{
	public int Count { get; private set; }

	public void Add(int amount)
	{
		Count = resetsOnOverflow && Count + amount > 20 ? 0 : Count + amount;
	}
}

// An async service that moves money between two pots; the planted bug loses a unit on every transfer over 5.
internal sealed class Wallet(bool leaks = false)
{
	public int Checking { get; private set; } = 30;

	public int Savings { get; private set; } = 10;

	public void Reset()
	{
		Checking = 30;
		Savings = 10;
	}

	public async Task MoveAsync(int amount)
	{
		await Task.Yield();
		if (amount <= 0 || amount > Checking)
		{
			return;
		}
		Checking -= amount;
		Savings += leaks && amount > 5 ? amount - 1 : amount;
	}
}

public class SubjectAutoTestTests
{
	private static readonly ConstraintSet<MutableCounter> CounterRules = ConstraintSet.From(
		("count-never-drops", Constraints<MutableCounter>.Monotonic(c => c.Count, MonotonicDirection.NonDecreasing))
	);

	private static readonly ConstraintSet<Wallet> WalletRules = ConstraintSet.From(
		("money-is-conserved", Constraints<Wallet>.Conserves(w => w.Checking + w.Savings, "Money was created or destroyed."))
	);

	private static AutoTest<MutableCounter> Counter(bool resetsOnOverflow = false) =>
		AutoTest.For(CounterRules, () => new MutableCounter(resetsOnOverflow)).Mutate<int>("Add", (c, n) => c.Add(n), _ => Gen.Int[0, 15]);

	/// <summary>
	/// Law: an operation that changes an object in place is compared with the state it started from, not with itself.
	/// Consequence if broken: every monotonicity rule on a mutable class passes, because the before and the after are the same object.
	/// </summary>
	[Test]
	public async Task A_sound_mutable_counter_passes_monotonicity()
	{
		var result = await Counter().MonotonicityAsync();

		result.Should().BeOfType<AxisResult.Passed>();
	}

	/// <summary>
	/// Law: a bug that shows only after earlier operations is found, because a run applies a sequence to one instance.
	/// Consequence if broken: a service that loses data on its third call passes every single-call test.
	/// </summary>
	[Test]
	public async Task A_counter_that_resets_after_enough_adds_is_caught_and_the_failure_names_the_steps_before_it()
	{
		var act = async () => await Counter(resetsOnOverflow: true).Steps(1, 6).MonotonicityAsync();

		var message = (await act.Should().ThrowAsync<Exception>()).Which.Message;
		message.Should().Contain("count-never-drops").And.Contain("Add(").And.Contain("after Add(");
	}

	[Test]
	public async Task With_only_one_step_the_same_bug_hides_because_a_fresh_counter_never_overflows()
	{
		var result = await Counter(resetsOnOverflow: true).Steps(1, 1).MonotonicityAsync();

		result.Should().BeOfType<AxisResult.Passed>();
	}

	/// <summary>
	/// Law: an awaited operation is run in a sequence and checked after each, with nothing blocking.
	/// Consequence if broken: services whose operations are async cannot be tested by the axes at all.
	/// </summary>
	[Test]
	public async Task An_async_service_that_loses_a_unit_is_caught_by_conservation()
	{
		var wallet = AutoTest.For(WalletRules, () => new Wallet(leaks: true)).MutateAsync<int>("Move", (w, n) => w.MoveAsync(n), _ => Gen.Int[1, 12]);

		var act = async () => await wallet.ConservationAsync();

		(await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("money-is-conserved").And.Contain("Move(");
	}

	[Test]
	public async Task The_sound_async_service_conserves_money()
	{
		var wallet = AutoTest.For(WalletRules, () => new Wallet()).MutateAsync<int>("Move", (w, n) => w.MoveAsync(n), _ => Gen.Int[1, 12]);

		(await wallet.ConservationAsync()).Should().BeOfType<AxisResult.Passed>();
	}

	/// <summary>
	/// Law: rules written over the state a service exposes are lifted onto the service with <c>Map</c>, and nothing else changes.
	/// Consequence if broken: a service needs its rules written twice, once over its state and once over itself.
	/// </summary>
	[Test]
	public async Task Rules_over_the_state_a_service_exposes_are_lifted_with_map()
	{
		var onState = ConstraintSet.From(("money-is-conserved", Constraints<(int Checking, int Savings)>.Conserves(p => p.Checking + p.Savings)));
		var lifted = onState.Map<Wallet>(w => (w.Checking, w.Savings));

		var act = async () =>
			await AutoTest
				.For(lifted, () => new Wallet(leaks: true))
				.MutateAsync<int>("Move", (w, n) => w.MoveAsync(n), _ => Gen.Int[1, 12])
				.ConservationAsync();

		(await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("money-is-conserved");
	}

	[Test]
	public async Task The_report_lists_public_methods_no_operation_covers()
	{
		var report = await AutoTest.For(WalletRules, () => new Wallet()).MutateAsync<int>("Move", (w, n) => w.MoveAsync(n), _ => Gen.Int[1, 12]).ReportAsync();

		report.NotOperations.Should().Equal("Reset");
		report.ToString().Should().Contain("Not run as operations: Reset");
	}

	[Test]
	public void Mutate_on_a_record_style_test_says_it_needs_a_factory()
	{
		var act = () => AutoTest.For(
			ConstraintSet.From(("a", Constraints<AutoAccountLike>.Always(_ => true, "x"))),
			Gen.Const(new AutoAccountLike())
		).Mutate("Op", _ => { });

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("factory");
	}

	private sealed record AutoAccountLike;
}
