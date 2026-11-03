using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

namespace Nullean.Entities.Diagnostics.Tests;

// The snippets on docs/integration/diagnostics.md and in the package README, as they are written there.

public sealed class Accounts(ConstraintDiagnostics diagnostics)
{
	public Result<Account> Withdraw(Account account, decimal amount) =>
		Account.Constraints.Apply(account, a => a with { Balance = a.Balance - amount }).ReportTo(diagnostics);
}

public sealed class Shipments(ConstraintDiagnostics diagnostics, TimeProvider time)
{
	private readonly TemporalMonitor<OrderEvent> _monitor = new TemporalMonitor<OrderEvent>(OrderRules.All, time).ReportTo(diagnostics);

	public void Confirmed(int id) => _monitor.Observe(new OrderEvent.Confirmed(id));

	public void Shipped(int id) => _monitor.Observe(new OrderEvent.Shipped(id));
}

public class DocsSnippetTests
{
	[Test]
	public void The_service_snippet_reports_a_rejected_withdrawal_and_a_missed_deadline()
	{
		var time = new ManualTimeProvider();
		var services = new ServiceCollection()
			.AddFakeLogging()
			.AddMetrics()
			.AddConstraintDiagnostics()
			.AddSingleton<TimeProvider>(time)
			.AddSingleton<Accounts>()
			.AddSingleton<Shipments>();
		using var provider = services.BuildServiceProvider();
		var logs = provider.GetFakeLogCollector();

		var result = provider.GetRequiredService<Accounts>().Withdraw(new Account("ada@example.com", 10), 25);
		var shipments = provider.GetRequiredService<Shipments>();
		shipments.Confirmed(7);
		time.Advance(TimeSpan.FromMinutes(2));

		result.IsFail.Should().BeTrue();
		logs.GetSnapshot().Select(r => r.Id.Id).Should().Equal(
			ConstraintDiagnosticsEvents.ConstraintViolated,
			ConstraintDiagnosticsEvents.TemporalRuleViolated
		);
	}

	[Test]
	public void IncludeEvidence_is_configured_on_registration()
	{
		var services = new ServiceCollection().AddFakeLogging().AddConstraintDiagnostics(o => o.IncludeEvidence = true);
		using var provider = services.BuildServiceProvider();

		new Accounts(provider.GetRequiredService<ConstraintDiagnostics>()).Withdraw(new Account("ada@example.com", 10), 25);

		provider.GetFakeLogCollector().LatestRecord.GetStructuredStateValue("Evidence").Should().Be("ada@example.com");
	}
}
