using System.Reactive.Subjects;
using Nullean.Entities;
using Nullean.Entities.AotSmoke;
using Nullean.Entities.Reactive;

// Exercises the AOT-relevant surface of the core and Reactive packages. Exit code 0 means every check held.

var failures = new List<string>();

void Expect(bool condition, string what)
{
	if (!condition)
	{
		failures.Add(what);
	}
}

// Generated constraints, Apply and its Result
var account = new Account("A-1", 100m);
Expect(Account.Constraints.Apply(account, a => a.Withdraw(30m)) is Result<Account>.Ok { Value.Balance: 70m }, "withdraw within balance is Ok");
Expect(Account.Constraints.Apply(account, a => a.Withdraw(500m)) is Result<Account>.Fail, "overdraw fails");
Expect(Account.Constraints.Validate(account with { Balance = -1m }).Count == 1, "negative balance is one violation");

// A declared state machine, with Mermaid out and in
var machine = StateMachine.Define<string, string>("Open").Permit("Close", "Open", "Closed").Build();
Expect(machine.TryFire("Open", "Close", out var next) && next == "Closed", "machine fires");
Expect(StateMachine.FromMermaid(machine.ToMermaid()).Permitted("Open").Contains("Close"), "mermaid round trip");

// Temporal rules over a trace
var rules = TemporalRuleSet<string>.From(("NoDoubleOpen", Temporal<string>.Transitions((a, b) => !(a == "open" && b == "open"))));
var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
Expect(Trace.Check([new(at, "open"), new(at.AddSeconds(1), "close")], rules).Passed, "good trace passes");
Expect(!Trace.Check([new(at, "open"), new(at.AddSeconds(1), "open")], rules).Passed, "bad trace fails");

// Online monitor on a manual clock
var time = new ManualTimeProvider(at);
using var monitor = new TemporalMonitor<string>(rules, time);
monitor.Observe("open");
monitor.Observe("open");
Expect(monitor.AnyViolated, "online monitor sees the double open");

// Reactive: a constraint set lifted onto a stream
using var subject = new Subject<Account>();
var attached = Account.Constraints.ToReactive().Attach(subject);
subject.OnNext(account with { Balance = -5m });
Expect(attached.Violations.Count > 0, "reactive constraint reports a violation");

if (failures.Count > 0)
{
	Console.Error.WriteLine("AOT smoke failed: " + string.Join("; ", failures));
	return 1;
}
Console.WriteLine("AOT smoke ok");
return 0;
