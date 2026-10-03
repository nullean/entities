using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;

namespace Banking;

public interface IAccountRepository
{
	Task<Account?> FindAsync(Iban number, CancellationToken cancellationToken = default);

	Task SaveAsync(Account account, CancellationToken cancellationToken = default);

	Task<IReadOnlyList<Account>> AllAsync(CancellationToken cancellationToken = default);
}

public sealed class InMemoryAccountRepository : IAccountRepository
{
	private readonly ConcurrentDictionary<string, Account> _accounts = new();

	public Task<Account?> FindAsync(Iban number, CancellationToken cancellationToken = default) =>
		Task.FromResult(_accounts.TryGetValue(number.Value, out var account) ? account : null);

	public Task SaveAsync(Account account, CancellationToken cancellationToken = default)
	{
		_accounts[account.Number.Value] = account;
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<Account>> AllAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<Account>>([.. _accounts.Values]);
}

public interface ITransferService
{
	Task<Result<Transfer>> TransferAsync(string? fromIban, string? toIban, decimal amount, string? currency, CancellationToken cancellationToken = default);
}

/// <summary>
/// Everything that arrives from outside is parsed first, with every problem reported at once (<c>Result.Combine</c>), so a form shows all that is wrong in one go.
/// What gets past that is a <see cref="Transfer"/>, and the rules decide the rest: nothing here compares a balance with an amount.
/// </summary>
public sealed class TransferService(IAccountRepository accounts) : ITransferService, IDisposable
{
	// Stands in for the database transaction a real service would open: two accounts change together or not at all.
	private readonly SemaphoreSlim _transaction = new(1, 1);

	public async Task<Result<Transfer>> TransferAsync(
		string? fromIban,
		string? toIban,
		decimal amount,
		string? currency,
		CancellationToken cancellationToken = default
	)
	{
		var input = Result.Combine(Iban.Create(fromIban), Iban.Create(toIban), Money.Create(amount, currency), (f, t, m) => (From: f, To: t, Amount: m));
		if (input is not Result<(Iban From, Iban To, Money Amount)>.Ok parsed)
		{
			return new Result<Transfer>.Fail(default!, input.ViolationsOrEmpty);
		}

		await _transaction.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var source = await accounts.FindAsync(parsed.Value.From, cancellationToken).ConfigureAwait(false);
			var target = await accounts.FindAsync(parsed.Value.To, cancellationToken).ConfigureAwait(false);
			if (source is null || target is null)
			{
				return new Result<Transfer>.Fail(
					default!,
					[Violation.Error(source is null ? $"No account {parsed.Value.From}" : $"No account {parsed.Value.To}")]
				);
			}

			var result = TransferRules.All.Apply(new Transfer(source, target, parsed.Value.Amount), t => t.Execute());
			if (result is Result<Transfer>.Ok ok)
			{
				await accounts.SaveAsync(ok.Value.From, cancellationToken).ConfigureAwait(false);
				await accounts.SaveAsync(ok.Value.To, cancellationToken).ConfigureAwait(false);
			}
			return result;
		}
		finally
		{
			_transaction.Release();
		}
	}

	public void Dispose() => _transaction.Dispose();
}

public static class BankingServices
{
	public static IServiceCollection AddBanking(this IServiceCollection services) =>
		services
			.AddSingleton<IAccountRepository, InMemoryAccountRepository>()
			.AddSingleton<ITransferService, TransferService>();
}
