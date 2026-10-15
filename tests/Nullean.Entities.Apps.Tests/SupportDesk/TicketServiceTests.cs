using AwesomeAssertions;
using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;
using SupportDesk;

namespace Nullean.Entities.Apps.Tests.SupportDesk;

// A desk the tests can drive: the service, an in-memory repository, a log that records every event with its time, and "the current ticket".
internal sealed class Desk
{
	private Guid? _current;

	public InMemoryTicketRepository Tickets { get; } = new();

	public EventLog Log { get; }

	public TicketService Service { get; }

	public TimeProvider Clock { get; }

	public Desk(TimeProvider clock)
	{
		Clock = clock;
		Log = new EventLog(clock);
		Service = new TicketService(Tickets, Log, clock);
	}

	public static Desk WithOpenTicket(TimeProvider clock, Priority priority = Priority.P2)
	{
		var desk = new Desk(clock);
		desk.Open(priority).GetAwaiter().GetResult();
		return desk;
	}

	public Ticket Ticket => Tickets.FindAsync(_current!.Value).GetAwaiter().GetResult()!;

	public TicketStatus Status => Ticket.Status;

	public async Task Open(Priority priority) => _current = (await Service.OpenAsync("Something is broken", priority)).Unwrap().Id;

	public Task Do(string trigger) =>
		_current is not { } id
			? Task.CompletedTask
			: trigger switch
			{
				"Acknowledge" => Service.AcknowledgeAsync(id),
				"Start" => Service.StartAsync(id),
				"Resolve" => Service.ResolveAsync(id),
				"Reopen" => Service.ReopenAsync(id),
				"Close" => Service.CloseAsync(id),
				_ => throw new InvalidOperationException($"No operation for '{trigger}'"),
			};
}

public class TicketServiceTests
{
	[Test]
	public async Task A_refused_transition_names_what_is_allowed()
	{
		var desk = Desk.WithOpenTicket(new ManualTimeProvider());

		var result = await desk.Service.ResolveAsync(desk.Ticket.Id);

		result.ViolationsOrEmpty.Should().ContainSingle().Which.Message.Should().Contain("allowed: Acknowledge");
		desk.Status.Should().Be(TicketStatus.New);
	}

	[Test]
	public async Task A_resolved_ticket_closes_only_once_three_days_have_passed()
	{
		var clock = new ManualTimeProvider();
		var desk = Desk.WithOpenTicket(clock);
		foreach (var trigger in new[] { "Acknowledge", "Start", "Resolve" })
		{
			await desk.Do(trigger);
		}

		clock.Advance(TimeSpan.FromDays(3) - TimeSpan.FromMinutes(1));
		(await desk.Service.SweepAsync()).Should().Be(0);
		desk.Status.Should().Be(TicketStatus.Resolved);

		clock.Advance(TimeSpan.FromMinutes(1));
		(await desk.Service.SweepAsync()).Should().Be(1);
		desk.Status.Should().Be(TicketStatus.Closed);
		desk.Log.Events.Last().Event.Should().BeOfType<TicketEvent.Closed>();
	}

	[Test]
	public async Task Reopening_restarts_the_three_days()
	{
		var clock = new ManualTimeProvider();
		var desk = Desk.WithOpenTicket(clock);
		foreach (var trigger in new[] { "Acknowledge", "Start", "Resolve" })
		{
			await desk.Do(trigger);
		}
		clock.Advance(TimeSpan.FromDays(2));
		await desk.Do("Reopen");
		await desk.Do("Resolve");

		clock.Advance(TimeSpan.FromDays(2));

		(await desk.Service.SweepAsync()).Should().Be(0);
	}

	[Test]
	public Task The_service_conforms_to_the_lifecycle_machine() =>
		TicketLifecycle
			.Machine
			.ConformToServiceAsync(
				() => Desk.WithOpenTicket(new ManualTimeProvider()),
				d => d.Status,
				iter: 200,
				bindings: [
					("Acknowledge", d => d.Do("Acknowledge")),
					("Start", d => d.Do("Start")),
					("Resolve", d => d.Do("Resolve")),
					("Reopen", d => d.Do("Reopen")),
					("Close", d => d.Do("Close")),
				]
			);

	[Test]
	public async Task A_declared_trigger_the_service_cannot_perform_is_found_before_any_run()
	{
		var act = async () =>
			await TicketLifecycle
				.Machine
				.ConformToServiceAsync(
					() => Desk.WithOpenTicket(new ManualTimeProvider()),
					d => d.Status,
					bindings: [
						("Acknowledge", d => d.Do("Acknowledge")),
						("Start", d => d.Do("Start")),
						("Resolve", d => d.Do("Resolve")),
						("Close", d => d.Do("Close"))
					]
				);

		(await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Reopen");
	}

	// ── time: the rules about sequences ──────────────────────────────────────────

	private static Script<Desk> DeskScript() =>
		Script
			.For<Desk>()
			.StepAsync("Open P1", d => d.Open(Priority.P1))
			.StepAsync("Open P3", d => d.Open(Priority.P3))
			.StepAsync("Acknowledge", d => d.Do("Acknowledge"))
			.StepAsync("Start", d => d.Do("Start"))
			.StepAsync("Resolve", d => d.Do("Resolve"))
			.StepAsync("Reopen", d => d.Do("Reopen"))
			.StepAsync("Close", d => d.Do("Close"))
			.StepAsync("Sweep", d => d.Service.SweepAsync())
			.Delays(Gen.Int[0, 100].Select(hours => TimeSpan.FromHours(hours)))
			.Length(1, 20);

	[Test]
	public Task However_the_desk_is_used_no_ticket_closes_twice() =>
		DeskScript()
			.SampleWithAsync(
				clock => new Desk(clock),
				d => d.Log.Events,
				TemporalRuleSet<TicketEvent>.From(("a-ticket-closes-once", SlaRules.All.Rules[1].Rule)),
				iter: 300
			);

	[Test]
	public async Task The_SLA_rule_is_a_wish_not_a_guarantee_and_the_search_finds_the_shortest_way_to_break_it()
	{
		// The service cannot make a human acknowledge a P1 in time, so this rule fails under generated delays. What the run gives back is the
		// shortest schedule that does it, with the rule and the time it was violated.
		var act = async () => await DeskScript().SampleWithAsync(clock => new Desk(clock), d => d.Log.Events, SlaRules.All, iter: 500);

		var message = (await act.Should().ThrowAsync<Exception>()).Which.ToString();
		message.Should().Contain("p1-acknowledged-within-1h");
		message.Should().Contain("Open P1");
	}

	[Test]
	public void An_acknowledgement_inside_the_hour_satisfies_the_rule_and_one_after_it_does_not()
	{
		var t0 = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
		var id = Guid.NewGuid();

		Timestamped<TicketEvent>[] Trace(TimeSpan ackAfter) =>
			[new(t0, new TicketEvent.Opened(id, Priority.P1)), new(t0 + ackAfter, new TicketEvent.Acknowledged(id))];

		global::Nullean.Entities.Trace.Check(Trace(TimeSpan.FromMinutes(59)), SlaRules.All, t0.AddHours(2)).Passed.Should().BeTrue();

		var late = global::Nullean.Entities.Trace.Check(Trace(TimeSpan.FromMinutes(61)), SlaRules.All, t0.AddHours(2));
		late.Violations.Should().ContainSingle().Which.Verdict.ViolationAt.Should().Be(t0.AddHours(1));
	}
}
