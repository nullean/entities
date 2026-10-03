using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;

namespace SupportDesk;

public interface ITicketRepository
{
	Task<Ticket?> FindAsync(Guid id, CancellationToken cancellationToken = default);

	Task SaveAsync(Ticket ticket, CancellationToken cancellationToken = default);

	Task<IReadOnlyList<Ticket>> AllAsync(CancellationToken cancellationToken = default);
}

public sealed class InMemoryTicketRepository : ITicketRepository
{
	private readonly ConcurrentDictionary<Guid, Ticket> _tickets = new();

	public Task<Ticket?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
		Task.FromResult(_tickets.TryGetValue(id, out var ticket) ? ticket : null);

	public Task SaveAsync(Ticket ticket, CancellationToken cancellationToken = default)
	{
		_tickets[ticket.Id] = ticket;
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<Ticket>> AllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Ticket>>([.. _tickets.Values]);
}

public interface IEventLog
{
	void Append(TicketEvent evt);
}

/// <summary>
/// Records every event with the time it happened, and feeds the live SLA monitor if there is one. The recording is what an audit reads later
/// (<c>Trace.Check</c>); the monitor is what raises the alarm while the hour is still running.
/// </summary>
public sealed class EventLog(TimeProvider clock, TemporalMonitor<TicketEvent>? monitor = null) : IEventLog
{
	private readonly Lock _gate = new();
	private readonly List<Timestamped<TicketEvent>> _events = [];

	public IReadOnlyList<Timestamped<TicketEvent>> Events
	{
		get
		{
			lock (_gate)
			{
				return [.. _events];
			}
		}
	}

	public void Append(TicketEvent evt)
	{
		lock (_gate)
		{
			_events.Add(new Timestamped<TicketEvent>(clock.GetUtcNow(), evt));
		}
		monitor?.Observe(evt);
	}
}

/// <summary>
/// The service has no lifecycle rules of its own. A transition is a lookup in the machine (<c>TryFire</c>) and a change checked by the rules;
/// the clock is injected, so the three-day timeout is tested by moving it, not by waiting.
/// </summary>
public sealed class TicketService(ITicketRepository tickets, IEventLog log, TimeProvider clock)
{
	public async Task<Result<Ticket>> OpenAsync(string? subject, Priority priority, CancellationToken cancellationToken = default)
	{
		var opened = TicketRules.All.Validated(new Ticket(Guid.NewGuid(), subject?.Trim() ?? "", priority, TicketStatus.New, clock.GetUtcNow()), clock);
		if (opened is Result<Ticket>.Ok ok)
		{
			await tickets.SaveAsync(ok.Value, cancellationToken).ConfigureAwait(false);
			log.Append(new TicketEvent.Opened(ok.Value.Id, priority));
		}
		return opened;
	}

	public Task<Result<Ticket>> AcknowledgeAsync(Guid id, CancellationToken cancellationToken = default) =>
		ActAsync(id, "Acknowledge", i => new TicketEvent.Acknowledged(i), cancellationToken);

	public Task<Result<Ticket>> StartAsync(Guid id, CancellationToken cancellationToken = default) => ActAsync(id, "Start", null, cancellationToken);

	public Task<Result<Ticket>> ResolveAsync(Guid id, CancellationToken cancellationToken = default) =>
		ActAsync(id, "Resolve", i => new TicketEvent.Resolved(i), cancellationToken);

	public Task<Result<Ticket>> ReopenAsync(Guid id, CancellationToken cancellationToken = default) =>
		ActAsync(id, "Reopen", i => new TicketEvent.Reopened(i), cancellationToken);

	public Task<Result<Ticket>> CloseAsync(Guid id, CancellationToken cancellationToken = default) =>
		ActAsync(id, "Close", i => new TicketEvent.Closed(i), cancellationToken);

	/// <summary>Applies the machine's timeouts: whatever has sat in a state longer than its timeout moves on. Returns how many tickets moved.</summary>
	public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
	{
		var moved = 0;
		foreach (var ticket in await tickets.AllAsync(cancellationToken).ConfigureAwait(false))
		{
			var now = clock.GetUtcNow();
			var due = TicketLifecycle.Machine.Timeouts.FirstOrDefault(t => t.From == ticket.Status && now - ticket.StatusSince >= t.After);
			if (due is null)
			{
				continue;
			}

			var result = TicketRules.All.Apply(ticket, t => t with { Status = due.To, StatusSince = now }, clock);
			if (result is Result<Ticket>.Ok ok)
			{
				await tickets.SaveAsync(ok.Value, cancellationToken).ConfigureAwait(false);
				if (due.To == TicketStatus.Closed)
				{
					log.Append(new TicketEvent.Closed(ticket.Id));
				}
				moved++;
			}
		}
		return moved;
	}

	/// <summary>Runs <see cref="SweepAsync"/> every <paramref name="every"/> on the injected clock until cancelled: what a hosted service would do.</summary>
	public async Task RunSweeperAsync(TimeSpan every, CancellationToken cancellationToken)
	{
		using var timer = new PeriodicTimer(every, clock);
		while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
		{
			await SweepAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	private async Task<Result<Ticket>> ActAsync(Guid id, string trigger, Func<Guid, TicketEvent>? evt, CancellationToken cancellationToken)
	{
		var ticket = await tickets.FindAsync(id, cancellationToken).ConfigureAwait(false);
		if (ticket is null)
		{
			return new Result<Ticket>.Fail(default!, [Violation.Error($"Ticket {id} does not exist")]);
		}

		if (!TicketLifecycle.Machine.TryFire(ticket.Status, trigger, out var next))
		{
			var permitted = string.Join(", ", TicketLifecycle.Machine.Permitted(ticket.Status));
			return new Result<Ticket>.Fail(
				ticket,
				[
					Violation.Error(
						$"'{trigger}' is not allowed while a ticket is {ticket.Status}" + (permitted.Length > 0 ? $" (allowed: {permitted})" : " (it is final)")
					)
				]
			);
		}

		var result = TicketRules.All.Apply(ticket, t => t with { Status = next, StatusSince = clock.GetUtcNow() }, clock);
		if (result is Result<Ticket>.Ok ok)
		{
			await tickets.SaveAsync(ok.Value, cancellationToken).ConfigureAwait(false);
			if (evt is not null)
			{
				log.Append(evt(id));
			}
		}
		return result;
	}
}

public static class SupportDeskServices
{
	/// <summary>Registers the service on <paramref name="clock"/> with a live SLA monitor attached to the event log.</summary>
	public static IServiceCollection AddSupportDesk(this IServiceCollection services, TimeProvider clock) =>
		services
			.AddSingleton(clock)
			.AddSingleton(sp => new TemporalMonitor<TicketEvent>(SlaRules.All, sp.GetRequiredService<TimeProvider>()))
			.AddSingleton<ITicketRepository, InMemoryTicketRepository>()
			.AddSingleton<EventLog>()
			.AddSingleton<IEventLog>(sp => sp.GetRequiredService<EventLog>())
			.AddSingleton<TicketService>();
}
