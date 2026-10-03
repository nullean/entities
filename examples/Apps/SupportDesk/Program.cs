using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;
using SupportDesk;
using static Apps.Narrator;

// A support desk on virtual time: the clock is a ManualTimeProvider, so "an hour later" and "three days later" are one line each.

var clock = new ManualTimeProvider(new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero));
await using var provider = new ServiceCollection().AddSupportDesk(clock).BuildServiceProvider();
var desk = provider.GetRequiredService<TicketService>();
var sla = provider.GetRequiredService<TemporalMonitor<TicketEvent>>();
var log = provider.GetRequiredService<EventLog>();

static string Why(Result<Ticket> result) => string.Join("; ", result.ViolationsOrEmpty.Select(v => v.Message));

Section("A ticket follows the lifecycle, and is told what it may do next");
var ticket = (await desk.OpenAsync("Cannot log in", Priority.P2)).Unwrap();
var early = await desk.ResolveAsync(ticket.Id);
Say($"Resolve a new ticket -> {Why(early)}");
Expect(early.IsFail, "a new ticket cannot be resolved before it is worked on");

Expect((await desk.AcknowledgeAsync(ticket.Id)).IsOk, "Acknowledge");
Expect((await desk.StartAsync(ticket.Id)).IsOk, "Start");
Expect((await desk.ResolveAsync(ticket.Id)).IsOk, "Resolve");

Expect((await desk.OpenAsync("  ", Priority.P3)).IsFail, "a ticket without a subject is refused");

Section("The SLA rule is watched live: a P1 not acknowledged within the hour");
var urgent = (await desk.OpenAsync("Production is down", Priority.P1)).Unwrap();
clock.Advance(TimeSpan.FromMinutes(59));
Expect(!sla.AnyViolated, "59 minutes in, nothing is wrong yet");
clock.Advance(TimeSpan.FromMinutes(2));
var (breachName, breachVerdict) = sla.Current.Single(c => c.Verdict.IsViolated);
Say($"{breachName} violated at {breachVerdict.ViolationAt:HH:mm}: opened 09:00, and the hour ran out at 10:00, not when it was noticed");
Expect(sla.AnyViolated, "61 minutes in, the monitor has raised the breach");
Expect(
	breachName == "p1-acknowledged-within-1h" && breachVerdict.ViolationAt == new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero),
	"it names the rule and the instant the deadline passed"
);
await desk.AcknowledgeAsync(urgent.Id); // too late, but the desk still works

Section("A resolved ticket closes by itself after three days");
Expect((await desk.SweepAsync()).Equals(0), "nothing has sat resolved long enough yet");
clock.Advance(TimeSpan.FromDays(3));
var moved = await desk.SweepAsync();
var closed = (await provider.GetRequiredService<ITicketRepository>().FindAsync(ticket.Id))!;
Say($"{moved} ticket(s) closed by the sweeper; the resolved one is now {closed.Status}");
Expect(closed.Status == TicketStatus.Closed, "the resolved ticket is closed");
Expect((await desk.ReopenAsync(ticket.Id)).IsFail, "a closed ticket cannot be reopened");

Section("Afterwards: the same rules audit the recorded log");
var report = Trace.Check(log.Events, SlaRules.All, clock.GetUtcNow());
foreach (var (name, verdict) in report.Violations)
{
	Say($"violated: {name} at {verdict.ViolationAt:yyyy-MM-dd HH:mm}");
}
Expect(report.Violations.Count == 1 && report.Violations[0].Name == "p1-acknowledged-within-1h", "the audit finds the same breach, and only that one");

Section("The lifecycle, as a diagram");
Say(TicketLifecycle.Machine.ToMermaid().ReplaceLineEndings("\n   "));

return Finish();
