using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Nullean.Entities.Diagnostics.Tests;

public class ConstraintDiagnosticsTests
{
	private static readonly Account Opened = new("ada@example.com", 100);

	[Test]
	public void The_meter_name_is_the_one_the_docs_tell_OpenTelemetry_to_add()
	{
		ConstraintDiagnostics.MeterName.Should().Be("Nullean.Entities");
		ConstraintDiagnostics.ViolationsCounter.Should().Be("entities.violations");
		ConstraintDiagnostics.RejectionsCounter.Should().Be("entities.rejections");
	}

	[Test]
	public void A_rejected_change_logs_each_violation_and_counts_one_rejection()
	{
		using var meters = new TestMeterFactory();
		using var measurements = new Measurements(meters);
		var logger = new FakeLogger();
		var diagnostics = new ConstraintDiagnostics(logger, meters);

		var result = Account.Constraints.Apply(Opened, a => a with { Balance = -5 }).ReportTo(diagnostics);

		result.IsFail.Should().BeTrue();
		var violations = measurements.Of(ConstraintDiagnostics.ViolationsCounter);
		violations.Should().ContainSingle();
		violations[0].Meter.Should().Be(ConstraintDiagnostics.MeterName);
		violations[0].Value.Should().Be(1);
		violations[0].Tags.Should().BeEquivalentTo(new Dictionary<string, object?>
		{
			[ConstraintDiagnostics.RuleTag] = "never-negative",
			[ConstraintDiagnostics.SeverityTag] = "error",
			[ConstraintDiagnostics.TypeTag] = "Account",
		});
		var rejections = measurements.Of(ConstraintDiagnostics.RejectionsCounter);
		rejections.Should().ContainSingle();
		rejections[0].Tags.Should().BeEquivalentTo(new Dictionary<string, object?> { [ConstraintDiagnostics.TypeTag] = "Account" });

		var record = logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
		record.Id.Id.Should().Be(ConstraintDiagnosticsEvents.ConstraintViolated);
		record.Id.Name.Should().Be("ConstraintViolated");
		record.Level.Should().Be(LogLevel.Warning);
		record.GetStructuredStateValue("EntityType").Should().Be("Account");
		record.GetStructuredStateValue("Rule").Should().Be("never-negative");
		record.GetStructuredStateValue("Member").Should().Be("Balance");
		record.GetStructuredStateValue("ViolationMessage").Should().Be("must not go below zero");
		record.Message.Should().Be("Account broke never-negative (error) on Balance: must not go below zero");
	}

	[Test]
	public void Evidence_is_left_out_of_the_log_unless_asked_for()
	{
		var hidden = new FakeLogger();
		var shown = new FakeLogger();

		Account.Constraints.ApplyDetailed(Opened, a => a with { Balance = -5 }).ReportTo(new ConstraintDiagnostics(hidden, new TestMeterFactory()));
		Account.Constraints.ApplyDetailed(Opened, a => a with { Balance = -5 })
			.ReportTo(new ConstraintDiagnostics(shown, new TestMeterFactory(), new ConstraintDiagnosticsOptions { IncludeEvidence = true }));

		var withoutEvidence = hidden.LatestRecord;
		withoutEvidence.Message.Should().NotContain("ada@example.com");
		withoutEvidence.StructuredState!.Select(kv => kv.Value).Should().NotContain("ada@example.com");
		var withEvidence = shown.LatestRecord;
		withEvidence.Id.Id.Should().Be(ConstraintDiagnosticsEvents.ConstraintViolatedWithEvidence);
		withEvidence.GetStructuredStateValue("Evidence").Should().Be("ada@example.com");
	}

	[Test]
	public void An_accepted_change_reports_its_warnings_but_no_rejection()
	{
		using var meters = new TestMeterFactory();
		using var measurements = new Measurements(meters);
		var logger = new FakeLogger();

		var outcome = Account.Constraints.ApplyDetailed(Opened, a => a with { Balance = 5 }).ReportTo(new ConstraintDiagnostics(logger, meters));

		outcome.IsApplied.Should().BeTrue();
		measurements.Of(ConstraintDiagnostics.RejectionsCounter).Should().BeEmpty();
		measurements.Of(ConstraintDiagnostics.ViolationsCounter).Should().ContainSingle()
			.Which.Tags[ConstraintDiagnostics.SeverityTag].Should().Be("warning");
		logger.LatestRecord.Level.Should().Be(LogLevel.Information);
	}

	[Test]
	public void A_change_that_holds_reports_nothing()
	{
		using var meters = new TestMeterFactory();
		using var measurements = new Measurements(meters);
		var logger = new FakeLogger();
		var diagnostics = new ConstraintDiagnostics(logger, meters);

		Account.Constraints.Apply(Opened, a => a with { Balance = 50 }).ReportTo(diagnostics);
		diagnostics.Report<Account>(Account.Constraints.Validate(Opened));

		measurements.Of(ConstraintDiagnostics.ViolationsCounter).Should().BeEmpty();
		measurements.Of(ConstraintDiagnostics.RejectionsCounter).Should().BeEmpty();
		logger.Collector.Count.Should().Be(0);
	}

	[Test]
	public void Report_counts_violations_of_Validate_without_a_rejection()
	{
		using var meters = new TestMeterFactory();
		using var measurements = new Measurements(meters);
		var diagnostics = new ConstraintDiagnostics(new FakeLogger(), meters);

		diagnostics.Report<Account>(Account.Constraints.Validate(Opened with { Balance = -1 }));

		measurements.Of(ConstraintDiagnostics.ViolationsCounter).Should().ContainSingle();
		measurements.Of(ConstraintDiagnostics.RejectionsCounter).Should().BeEmpty();
	}

	[Test]
	public void A_violation_is_an_event_on_the_current_activity()
	{
		using var source = new ActivitySource("Nullean.Entities.Diagnostics.Tests." + Guid.NewGuid());
		using var listener = new ActivityListener
		{
			ShouldListenTo = s => s == source,
			Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
		};
		ActivitySource.AddActivityListener(listener);
		var diagnostics = new ConstraintDiagnostics(new FakeLogger(), new TestMeterFactory());

		using var activity = source.StartActivity("transfer")!;
		Account.Constraints.Apply(Opened, a => a with { Balance = -5 }).ReportTo(diagnostics);

		var recorded = activity.Events.Should().ContainSingle().Subject;
		recorded.Name.Should().Be(ConstraintDiagnostics.ActivityEventName);
		recorded.Tags.Should().Contain([
			new(ConstraintDiagnostics.RuleTag, "never-negative"),
			new(ConstraintDiagnostics.SeverityTag, "error"),
			new(ConstraintDiagnostics.MemberTag, "Balance"),
		]);
	}

	[Test]
	public void A_missed_deadline_on_a_ManualTimeProvider_is_one_log_record_and_one_count()
	{
		using var meters = new TestMeterFactory();
		using var measurements = new Measurements(meters);
		var logger = new FakeLogger();
		var time = new ManualTimeProvider();
		using var monitor = new TemporalMonitor<OrderEvent>(OrderRules.All, time).ReportTo(new ConstraintDiagnostics(logger, meters));
		var confirmedAt = time.GetUtcNow();

		monitor.Observe(new OrderEvent.Confirmed(1));
		time.Advance(TimeSpan.FromSeconds(59));
		logger.Collector.Count.Should().Be(0);
		time.Advance(TimeSpan.FromSeconds(2));
		// Reported by the timer itself, with no event to carry it.
		logger.Collector.Count.Should().Be(1);
		measurements.Of(ConstraintDiagnostics.ViolationsCounter).Should().HaveCount(1);
		// A violation is sticky: more time and a late answer report nothing more.
		time.Advance(TimeSpan.FromMinutes(10));
		monitor.Observe(new OrderEvent.Shipped(1));

		var record = logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
		record.Id.Id.Should().Be(ConstraintDiagnosticsEvents.TemporalRuleViolated);
		record.Level.Should().Be(LogLevel.Warning);
		record.GetStructuredStateValue("Rule").Should().Be("ShipsWithinAMinute");
		record.GetStructuredStateValue("EntityType").Should().Be("OrderEvent");
		record.Message.Should().Contain(confirmedAt.AddMinutes(1).ToString("O"));
		var counted = measurements.Of(ConstraintDiagnostics.ViolationsCounter).Should().ContainSingle().Subject;
		counted.Tags[ConstraintDiagnostics.RuleTag].Should().Be("ShipsWithinAMinute");
		counted.Tags[ConstraintDiagnostics.TypeTag].Should().Be("OrderEvent");
	}

	[Test]
	public void Disposing_the_watch_stops_reporting()
	{
		var logger = new FakeLogger();
		var time = new ManualTimeProvider();
		using var monitor = new TemporalMonitor<OrderEvent>(OrderRules.All, time);
		var watch = new ConstraintDiagnostics(logger, new TestMeterFactory()).Watch(monitor);

		watch.Dispose();
		monitor.Observe(new OrderEvent.Confirmed(1));
		time.Advance(TimeSpan.FromMinutes(2));

		monitor.AnyViolated.Should().BeTrue();
		logger.Collector.Count.Should().Be(0);
	}

	[Test]
	public void AddConstraintDiagnostics_uses_the_hosts_logger_factory_and_meter_factory()
	{
		var services = new ServiceCollection().AddFakeLogging().AddMetrics().AddConstraintDiagnostics();
		using var provider = services.BuildServiceProvider();
		var factory = provider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
		using var measurements = new Measurements(factory);

		var diagnostics = provider.GetRequiredService<ConstraintDiagnostics>();
		Account.Constraints.Apply(Opened, a => a with { Balance = -5 }).ReportTo(diagnostics);

		provider.GetRequiredService<ConstraintDiagnostics>().Should().BeSameAs(diagnostics);
		measurements.Of(ConstraintDiagnostics.RejectionsCounter).Should().ContainSingle();
		var record = provider.GetFakeLogCollector().LatestRecord;
		record.Category.Should().Be(ConstraintDiagnostics.LoggerCategory);
		record.Id.Id.Should().Be(ConstraintDiagnosticsEvents.ConstraintViolated);
	}
}
