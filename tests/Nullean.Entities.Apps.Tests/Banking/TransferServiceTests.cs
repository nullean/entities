using AwesomeAssertions;
using Banking;
using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.Apps.Tests.Banking;

// Banking: unit tests on the always-valid values, then properties of the whole service. The properties are the point:
// "any sequence of transfers keeps the total" is a stronger statement than any list of examples.

internal sealed class Bank
{
	public static readonly string[] Ibans = ["DE89370400440532013000", "GB82WEST12345698765432", "FR1420041010050500013M02606"];

	public InMemoryAccountRepository Accounts { get; } = new();

	public TransferService Service { get; }

	public Bank(decimal opening = 1_000m, decimal overdraft = 100m)
	{
		Service = new TransferService(Accounts);
		foreach (var iban in Ibans)
		{
			Accounts.SaveAsync(new Account(Iban.Create(iban).Unwrap(), "EUR", opening, overdraft)).GetAwaiter().GetResult();
		}
	}

	public decimal Total => Accounts.AllAsync().GetAwaiter().GetResult().Sum(a => a.Balance);

	public Task<Result<Transfer>> Transfer(int from, int to, decimal amount) => Service.TransferAsync(Ibans[from], Ibans[to], amount, "EUR");
}

public class TransferServiceTests
{
	[Test]
	public void An_iban_is_checked_for_shape_and_checksum()
	{
		Iban.Create("DE89 3704 0044 0532 0130 00").Unwrap().Value.Should().Be("DE89370400440532013000");

		Iban.Create("DE89 3704 0044 0532 0130 01").ViolationsOrEmpty.Should().ContainSingle().Which.Message.Should().Contain("check digits");
		Iban.Create("hello").ViolationsOrEmpty.Should().ContainSingle().Which.Message.Should().Contain("two letters");
		Iban.Create(null).IsFail.Should().BeTrue();
	}

	[Test]
	public void Money_is_positive_with_cents_and_an_iso_currency()
	{
		Money.Create(10.25m, "EUR").IsOk.Should().BeTrue();

		Money.Create(0m, "EUR").IsFail.Should().BeTrue();
		Money.Create(1.005m, "EUR").IsFail.Should().BeTrue();
		Money.Create(5m, "euro").IsFail.Should().BeTrue();
	}

	[Test]
	public async Task Every_input_problem_is_reported_in_one_answer()
	{
		var bank = new Bank();

		var result = await bank.Service.TransferAsync("nope", "also nope", -1m, "x");

		result.ViolationsOrEmpty.Select(v => v.ConstraintId).Should().Contain(["iban-shape", "amount-is-positive", "currency-is-iso"]);
		bank.Total.Should().Be(3_000m);
	}

	[Test]
	public async Task A_transfer_moves_money_between_exactly_two_accounts()
	{
		var bank = new Bank();

		var result = await bank.Transfer(0, 1, 250m);

		result.IsOk.Should().BeTrue();
		var all = await bank.Accounts.AllAsync();
		all.Select(a => a.Balance).Should().BeEquivalentTo([750m, 1_250m, 1_000m]);
	}

	[Test]
	public async Task A_refused_transfer_leaves_both_accounts_alone()
	{
		var bank = new Bank();

		var result = await bank.Transfer(0, 1, 1_101m); // 1000 + 100 overdraft is the most account 0 can send

		result.ViolationsOrEmpty.Should().ContainSingle().Which.Message.Should().Contain("overdraft");
		(await bank.Accounts.AllAsync()).Select(a => a.Balance).Should().AllBeEquivalentTo(1_000m);
	}

	[Test]
	public async Task Sending_exactly_the_available_amount_is_allowed()
	{
		var bank = new Bank();

		(await bank.Transfer(0, 1, 1_100m)).IsOk.Should().BeTrue();
	}

	[Test]
	public void Any_sequence_of_transfers_keeps_the_total_and_every_account_within_its_overdraft()
	{
		var transfer = Gen.Select(Gen.Int[0, 2], Gen.Int[0, 2], Gen.Int[1, 150_000].Select(cents => cents / 100m));

		transfer
			.List[0, 40]
			.Sample(
				transfers =>
				{
					var bank = new Bank();
					foreach (var (from, to, amount) in transfers)
					{
						bank.Transfer(from, to, amount).GetAwaiter().GetResult();
					}

					bank.Total.Should().Be(3_000m);
					bank.Accounts.AllAsync().GetAwaiter().GetResult().Should().OnlyContain(a => a.Balance >= -a.OverdraftLimit);
				},
				iter: 300
			);
	}

	[Test]
	public async Task The_total_is_conserved_after_every_single_step()
	{
		// The same property, stated as a rule on the state the service exposes and checked after each generated step.
		var rules = ConstraintSet.From(("money-is-conserved", Constraints<decimal>.Conserves(total => total, "Money was created or destroyed")));

		await Script
			.For<Bank>()
			.Step(
				"transfer",
				Gen.Select(Gen.Int[0, 2], Gen.Int[0, 2], Gen.Int[1, 150_000].Select(cents => cents / 100m)),
				(bank, t) => bank.Transfer(t.Item1, t.Item2, t.Item3).GetAwaiter().GetResult()
			)
			.Length(1, 30)
			.SampleStateAsync(_ => new Bank(), bank => bank.Total, rules, iter: 300);
	}
}
