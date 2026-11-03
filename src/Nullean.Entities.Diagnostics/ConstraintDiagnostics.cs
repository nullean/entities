// Nullean.Entities.Diagnostics — violations seen at runtime as logs, metrics and trace events.
//
// Everything here is the platform's own abstractions: ILogger (Microsoft.Extensions.Logging.Abstractions), a System.Diagnostics.Metrics
// Meter and Activity events. OpenTelemetry, dotnet-counters or any MeterListener subscribe by the meter's name; nothing here knows them.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nullean.Entities.Diagnostics;

/// <summary>What <see cref="ConstraintDiagnostics"/> writes.</summary>
public sealed class ConstraintDiagnosticsOptions
{
	/// <summary>
	/// Writes <see cref="Violation.Evidence"/> (and a temporal violation's evidence) into the log record. Off by default: evidence is the value that broke the
	/// rule, which can be personal data (an email, an account). Evidence never becomes a metric tag or a trace event.
	/// </summary>
	public bool IncludeEvidence { get; set; }
}

/// <summary>
/// Reports violations as structured log records (stable event ids, see <see cref="ConstraintDiagnosticsEvents"/>), as increments of the
/// <see cref="ViolationsCounter"/> and <see cref="RejectionsCounter"/> counters on the <see cref="MeterName"/> meter, and as an event on the current
/// <see cref="Activity"/>. Thread safe; make one and share it (<c>services.AddConstraintDiagnostics()</c> registers a singleton).
/// </summary>
public sealed partial class ConstraintDiagnostics
{
	/// <summary>The meter's name: <c>AddMeter(ConstraintDiagnostics.MeterName)</c> in OpenTelemetry, <c>--counters Nullean.Entities</c> in dotnet-counters.</summary>
	public const string MeterName = "Nullean.Entities";

	/// <summary>The default logger category, when this is built from an <see cref="ILoggerFactory"/>.</summary>
	public const string LoggerCategory = "Nullean.Entities";

	/// <summary>One per violation: tagged <see cref="RuleTag"/>, <see cref="SeverityTag"/> and <see cref="TypeTag"/>.</summary>
	public const string ViolationsCounter = "entities.violations";

	/// <summary>One per change that was rejected (an <see cref="ApplyOutcome{T}.Failure"/> or a <see cref="Result{T}.Fail"/>): tagged <see cref="TypeTag"/>.</summary>
	public const string RejectionsCounter = "entities.rejections";

	/// <summary>The name of the event added to <see cref="Activity.Current"/> for each violation.</summary>
	public const string ActivityEventName = "entities.violation";

	public const string RuleTag = "entities.rule";
	public const string SeverityTag = "entities.severity";
	public const string TypeTag = "entities.type";
	public const string MemberTag = "entities.member";

	// Shared by every instance built without an IMeterFactory, so making one per scope does not leak meters.
	private static readonly Meter SharedMeter = new(MeterName);

	private readonly ILogger _logger;
	private readonly bool _includeEvidence;
	private readonly Counter<long> _violations;
	private readonly Counter<long> _rejections;

	/// <param name="logger">Where log records go; <see cref="NullLogger.Instance"/> for metrics only.</param>
	/// <param name="meterFactory">The host's meter factory (<c>services.AddMetrics()</c>, registered by every ASP.NET Core and generic host). Without one a process-wide meter is used.</param>
	/// <param name="options">What to write; see <see cref="ConstraintDiagnosticsOptions"/>.</param>
	public ConstraintDiagnostics(ILogger logger, IMeterFactory? meterFactory = null, ConstraintDiagnosticsOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(logger);
		_logger = logger;
		_includeEvidence = options?.IncludeEvidence ?? false;
		var meter = meterFactory?.Create(new MeterOptions(MeterName)) ?? SharedMeter;
		_violations = meter.CreateCounter<long>(ViolationsCounter, "{violation}", "Constraint violations observed, one per violation.");
		_rejections = meter.CreateCounter<long>(RejectionsCounter, "{change}", "Changes rejected because an error-severity constraint was violated.");
	}

	/// <summary>Reports violations about a <typeparamref name="T"/> (from <c>Validate</c>, <c>Check</c>, or a caught <see cref="ConstraintViolationException"/>). Does not count a rejection.</summary>
	public void Report<T>(IReadOnlyList<Violation> violations) => Report(TypeName<T>(), violations);

	/// <summary>Reports violations about the type named <paramref name="type"/> (the <see cref="TypeTag"/>). Does not count a rejection.</summary>
	public void Report(string type, IReadOnlyList<Violation> violations)
	{
		ArgumentNullException.ThrowIfNull(type);
		ArgumentNullException.ThrowIfNull(violations);
		foreach (var violation in violations)
		{
			var rule = violation.ConstraintId ?? "unnamed";
			var severity = SeverityName(violation.Severity);
			_violations.Add(1, new(RuleTag, rule), new(SeverityTag, severity), new(TypeTag, type));
			AddActivityEvent(rule, severity, type, violation.Member);
			var level = violation.Severity switch
			{
				Severity.Error => LogLevel.Warning,
				Severity.Warning => LogLevel.Information,
				_ => LogLevel.Debug,
			};
			if (!_logger.IsEnabled(level))
			{
				continue;
			}
			if (_includeEvidence)
			{
				ConstraintViolatedWithEvidence(_logger, level, type, rule, severity, violation.Member, violation.Message, violation.Evidence);
			}
			else
			{
				ConstraintViolated(_logger, level, type, rule, severity, violation.Member, violation.Message);
			}
		}
	}

	/// <summary>Reports a rejected change of a <typeparamref name="T"/>: counts one rejection and reports its violations.</summary>
	public void Rejected<T>(IReadOnlyList<Violation> violations)
	{
		var type = TypeName<T>();
		_rejections.Add(1, new KeyValuePair<string, object?>(TypeTag, type));
		Report(type, violations);
	}

	/// <summary>Reports a temporal rule over <typeparamref name="TEvent"/> that changed verdict. Only a violation is reported; pending and holds are not.</summary>
	public void Report<TEvent>(VerdictChange change)
	{
		if (!change.Verdict.IsViolated)
		{
			return;
		}
		var type = TypeName<TEvent>();
		const string severity = "error";
		_violations.Add(1, new(RuleTag, change.Rule), new(SeverityTag, severity), new(TypeTag, type));
		AddActivityEvent(change.Rule, severity, type, member: null);
		if (!_logger.IsEnabled(LogLevel.Warning))
		{
			return;
		}
		if (_includeEvidence)
		{
			TemporalRuleViolatedWithEvidence(_logger, type, change.Rule, change.At, change.Verdict.ViolationEvidence);
		}
		else
		{
			TemporalRuleViolated(_logger, type, change.Rule, change.At);
		}
	}

	/// <summary>Reports every violation <paramref name="monitor"/> reaches from now on, including a deadline that passes on its timer. Dispose the result to stop.</summary>
	public IDisposable Watch<TEvent>(TemporalMonitor<TEvent> monitor)
	{
		ArgumentNullException.ThrowIfNull(monitor);
		return new Subscription<TEvent>(monitor, this);
	}

	private static string TypeName<T>() => typeof(T).Name;

	private static string SeverityName(Severity severity) => severity switch
	{
		Severity.Error => "error",
		Severity.Warning => "warning",
		Severity.Hint => "hint",
		_ => "unknown",
	};

	private static void AddActivityEvent(string rule, string severity, string type, string? member)
	{
		if (Activity.Current is not { IsAllDataRequested: true } activity)
		{
			return;
		}
		var tags = new ActivityTagsCollection { [RuleTag] = rule, [SeverityTag] = severity, [TypeTag] = type };
		if (member is not null)
		{
			tags[MemberTag] = member;
		}
		activity.AddEvent(new ActivityEvent(ActivityEventName, tags: tags));
	}

	[LoggerMessage(EventId = ConstraintDiagnosticsEvents.ConstraintViolated, EventName = nameof(
		ConstraintViolated
	), Message = "{EntityType} broke {Rule} ({Severity}) on {Member}: {ViolationMessage}")]
	private static partial void ConstraintViolated(
		ILogger logger,
		LogLevel level,
		string entityType,
		string rule,
		string severity,
		string? member,
		string violationMessage
	);

	[LoggerMessage(EventId = ConstraintDiagnosticsEvents.ConstraintViolatedWithEvidence, EventName = nameof(
		ConstraintViolatedWithEvidence
	), Message = "{EntityType} broke {Rule} ({Severity}) on {Member}: {ViolationMessage}; evidence: {Evidence}")]
	private static partial void ConstraintViolatedWithEvidence(
		ILogger logger,
		LogLevel level,
		string entityType,
		string rule,
		string severity,
		string? member,
		string violationMessage,
		object? evidence
	);

	[LoggerMessage(EventId = ConstraintDiagnosticsEvents.TemporalRuleViolated, EventName = nameof(
		TemporalRuleViolated
	), Level = LogLevel.Warning, Message = "Temporal rule {Rule} over {EntityType} violated at {ViolatedAt:O}")]
	private static partial void TemporalRuleViolated(ILogger logger, string entityType, string rule, DateTimeOffset violatedAt);

	[LoggerMessage(EventId = ConstraintDiagnosticsEvents.TemporalRuleViolatedWithEvidence, EventName = nameof(
		TemporalRuleViolatedWithEvidence
	), Level = LogLevel.Warning, Message = "Temporal rule {Rule} over {EntityType} violated at {ViolatedAt:O}; evidence: {Evidence}")]
	private static partial void TemporalRuleViolatedWithEvidence(ILogger logger, string entityType, string rule, DateTimeOffset violatedAt, object? evidence);

	private sealed class Subscription<TEvent> : IDisposable
	{
		private readonly TemporalMonitor<TEvent> _monitor;
		private readonly ConstraintDiagnostics _diagnostics;
		private int _disposed;

		public Subscription(TemporalMonitor<TEvent> monitor, ConstraintDiagnostics diagnostics)
		{
			_monitor = monitor;
			_diagnostics = diagnostics;
			_monitor.VerdictChanged += OnChanged;
		}

		private void OnChanged(VerdictChange change) => _diagnostics.Report<TEvent>(change);

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				_monitor.VerdictChanged -= OnChanged;
			}
		}
	}
}

/// <summary>The stable <see cref="EventId"/>s <see cref="ConstraintDiagnostics"/> logs with. Filter or alert on these, not on the message text.</summary>
public static class ConstraintDiagnosticsEvents
{
	/// <summary>A constraint was violated (from <c>Apply</c>, <c>Validate</c> or <c>Check</c>). Level: Warning for an error, Information for a warning, Debug for a hint.</summary>
	public const int ConstraintViolated = 4100;

	/// <summary><see cref="ConstraintViolated"/> with the evidence, when <see cref="ConstraintDiagnosticsOptions.IncludeEvidence"/> is on.</summary>
	public const int ConstraintViolatedWithEvidence = 4101;

	/// <summary>A temporal rule was violated: a forbidden event, or a deadline that passed. Level: Warning.</summary>
	public const int TemporalRuleViolated = 4110;

	/// <summary><see cref="TemporalRuleViolated"/> with the evidence, when <see cref="ConstraintDiagnosticsOptions.IncludeEvidence"/> is on.</summary>
	public const int TemporalRuleViolatedWithEvidence = 4111;
}
