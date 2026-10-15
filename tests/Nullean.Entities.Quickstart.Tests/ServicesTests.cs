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
