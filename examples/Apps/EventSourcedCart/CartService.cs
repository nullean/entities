using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;

namespace EventSourcedCart;

public interface IEventStore
{
	/// <summary>The events of a stream, in order, and the version they bring it to.</summary>
	Task<(IReadOnlyList<CartEvent> Events, int Version)> LoadAsync(Guid streamId, CancellationToken cancellationToken = default);

	/// <summary>Appends only if the stream is still at <paramref name="expectedVersion"/>; false means someone else appended first.</summary>
	Task<bool> AppendAsync(Guid streamId, int expectedVersion, IReadOnlyList<CartEvent> events, CancellationToken cancellationToken = default);
}

public sealed class InMemoryEventStore : IEventStore
{
	private readonly ConcurrentDictionary<Guid, List<CartEvent>> _streams = new();

	public Task<(IReadOnlyList<CartEvent> Events, int Version)> LoadAsync(Guid streamId, CancellationToken cancellationToken = default)
	{
		var stream = _streams.GetOrAdd(streamId, _ => []);
		lock (stream)
		{
			return Task.FromResult<(IReadOnlyList<CartEvent>, int)>(([.. stream], stream.Count));
		}
	}

	public Task<bool> AppendAsync(Guid streamId, int expectedVersion, IReadOnlyList<CartEvent> events, CancellationToken cancellationToken = default)
	{
		var stream = _streams.GetOrAdd(streamId, _ => []);
		lock (stream)
		{
			if (stream.Count != expectedVersion)
			{
				return Task.FromResult(false);
			}
			stream.AddRange(events);
			return Task.FromResult(true);
		}
	}
}

/// <summary>
/// Load the stream, fold it into the cart, decide, check the rules on the cart the new events would give, append. The decider is a parameter so a test can
/// hand in a wrong one and watch the rules stop it.
/// </summary>
public sealed class CartService(IEventStore store, Func<Cart, CartCommand, IEnumerable<CartEvent>>? decide = null)
{
	private readonly Func<Cart, CartCommand, IEnumerable<CartEvent>> _decide = decide ?? CartLogic.Decide;

	public async Task<Result<Cart>> HandleAsync(Guid cartId, CartCommand command, CancellationToken cancellationToken = default)
	{
		var (history, version) = await store.LoadAsync(cartId, cancellationToken).ConfigureAwait(false);
		var before = CartLogic.Rehydrate(history);

		var produced = _decide(before, command).ToList();
		var after = produced.Aggregate(before, CartLogic.Evolve);

		var broken = CartRules.All.Check(before, after).Where(v => v.Severity == Severity.Error).ToList();
		if (broken.Count > 0)
		{
			return new Result<Cart>.Fail(before, broken);
		}

		if (produced.Count > 0 && !await store.AppendAsync(cartId, version, produced, cancellationToken).ConfigureAwait(false))
		{
			return new Result<Cart>.Fail(before, [Violation.Error("The cart was changed by someone else; load it again and retry")]);
		}
		return new Result<Cart>.Ok(after);
	}

	public async Task<Cart> GetAsync(Guid cartId, CancellationToken cancellationToken = default) =>
		CartLogic.Rehydrate((await store.LoadAsync(cartId, cancellationToken).ConfigureAwait(false)).Events);
}

public static class CartServices
{
	public static IServiceCollection AddEventSourcedCart(this IServiceCollection services) =>
		services
			.AddSingleton<IEventStore, InMemoryEventStore>()
			.AddSingleton(sp => new CartService(sp.GetRequiredService<IEventStore>()));
}
