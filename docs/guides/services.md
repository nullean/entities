---
navigation_title: Services and mutable classes
description: "Test a service, or any class that changes in place, with the same AutoTest: the rules and the axes are the same, only the way operations are registered differs."
---

# Services and mutable classes

A record's operation returns the next value, so the value before it is still there to compare with. A service or a mutable class changes the object it is called on, and the value before is gone. `AutoTest` handles both with one front door: you give it a factory instead of a generator, and register operations with `Mutate` and `MutateAsync` instead of `Action`.

<!-- file: tests/Nullean.Entities.Quickstart.Tests/ServicesTests.cs -->
```csharp
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.Quickstart.Tests;

// What a wallet service shows to the outside, and the rules about it.
internal sealed record WalletState(int Checking, int Savings);

// A service that changes in place and awaits. It is not a record: Move overwrites the object it is called on.
internal sealed class Wallet(bool leaksAUnit = false)
{
	public WalletState State => new(_checking, _savings);

	private int _checking = 30;
	private int _savings = 10;

	public async Task MoveAsync(int amount)
	{
		await Task.Yield();
		if (amount <= 0 || amount > _checking)
		{
			return;
		}
		_checking -= amount;
		_savings += leaksAUnit && amount > 5 ? amount - 1 : amount;
	}
}

// The rules are written over the state, then lifted onto the service with Map.
public class ServicesTests
{
	private static readonly ConstraintSet<Wallet> Rules = ConstraintSet
		.From(
			("money-is-conserved", Constraints<WalletState>.Conserves(s => s.Checking + s.Savings, "Money was created or destroyed.")),
			("checking-never-negative", Constraints<WalletState>.Always(s => s.Checking >= 0, "Checking cannot go below zero."))
		)
		.Map<Wallet>(wallet => wallet.State);

	private static AutoTest<Wallet> Auto(bool leaksAUnit = false) =>
		AutoTest
			.For(Rules, () => new Wallet(leaksAUnit))
			.MutateAsync<int>("Move", (wallet, amount) => wallet.MoveAsync(amount), _ => Gen.Int[1, 12])
			.Steps(1, 6);

	public static IEnumerable<Func<AxisCase>> Axes() => Auto().AsDataSource();

	[Test]
	[MethodDataSource(nameof(Axes))]
	public Task EveryDeclaredAxisHolds(AxisCase axis) => axis.RunAsync();

	[Test]
	public async Task A_move_that_loses_a_unit_is_caught_by_conservation_with_the_steps_that_led_to_it()
	{
		var message = await BugGalleryTests.FailAsync(
			"service-conservation",
			() => Auto(leaksAUnit: true).Cases.Single(c => c.Axis == Axis.Conservation).RunAsync()
		);

		message.Should().Contain("money-is-conserved").And.Contain("Move(");
	}
}
```

What to take from it:

- **The rules are the ones you would write for a record.** They are written over the state the service exposes (`WalletState`) and lifted onto the service with `Map`. Nothing about a rule knows it is checked on a service.
- **One `AutoTest`, the same axes.** Conservation, monotonicity, invariants and vacancy run on a service as they do on a record, and `AsDataSource()` gives one test per axis.
- **Operations are awaited.** `MutateAsync` takes a `Task`. Nothing blocks.
- **`Steps(1, 6)`** applies one to six operations to one instance and checks after each, so a bug that needs earlier calls to show is found. Without it a run is one operation on a fresh service.

## How the "before" survives

The test needs the state before an operation and after it, and an object changed in place has only one. So the "after" is built on a second, fresh instance: the factory, then the operations that led up to the one being tested, then that one. The "before" is never touched again.

That is a small rule for you to keep, not magic to trust:

- **The factory and the operations must be deterministic.** The same calls with the same arguments must reach the same state. A service that reads a random number, a GUID or the wall clock is not repeatable unless those are faked. The clock overload (`AutoTest.For(rules, clock => new Service(clock))`) gives every instance its own manual clock.
- **Replay costs the length of the run**, so keep `Steps` short.
- **Real I/O is outside this.** A step that awaits a real database or HTTP call is not repeatable, so a failure cannot replay. Use in-memory fakes. The data a rule needs from outside is fetched first and passed in.

## A failure says how it got there

:::{include} ../_snippets/service-conservation.txt
:language: text
:::

The operation line names the call and, when the failing call came after others, the calls before it.

## What it does not do

- **Rules without a counter-example.** A record's rule must show it can fire (axis 09 builds a value that breaks it). A service has no value to break, so a rule on a service is not required to have a counter-example; give one with `CounterExample(rule, ...)` if you can build a broken instance.
- **Idempotency of an in-place operation** is not checked yet. `Idempotent` rules compare a value with the value after the operation, and a service has no value to compare. State it as a rule over the state record instead, or test the operation twice in a hand-written test.
- **Custom `CheckTransition` rules** that need a whole before are not run against a service. Use `Monotonic`, `Conserves` and `Transitions`, which read one value.
- **Public methods nobody registered** are listed in the report ("Not run as operations: ...") so an operation you forgot is visible.
