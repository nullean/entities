using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nullean.Entities.Diagnostics;

public static class ConstraintDiagnosticsExtensions
{
	/// <summary>Reports a <see cref="Result{T}.Fail"/> (one rejection, and its violations) and returns the result unchanged, so it chains: <c>Rules.Apply(order, o =&gt; o.Ship()).ReportTo(diagnostics)</c>.</summary>
	public static Result<T> ReportTo<T>(this Result<T> result, ConstraintDiagnostics diagnostics)
	{
		ArgumentNullException.ThrowIfNull(result);
		ArgumentNullException.ThrowIfNull(diagnostics);
		if (result is Result<T>.Fail fail)
		{
			diagnostics.Rejected<T>(fail.Violations);
		}
		return result;
	}

	/// <summary>
	/// Reports an <see cref="ApplyOutcome{T}.Failure"/> (one rejection, and its violations) and the warnings and hints an <see cref="ApplyOutcome{T}.Applied"/>
	/// was accepted with, and returns the outcome unchanged.
	/// </summary>
	public static ApplyOutcome<T> ReportTo<T>(this ApplyOutcome<T> outcome, ConstraintDiagnostics diagnostics)
	{
		ArgumentNullException.ThrowIfNull(outcome);
		ArgumentNullException.ThrowIfNull(diagnostics);
		switch (outcome)
		{
			case ApplyOutcome<T>.Failure failure:
				diagnostics.Rejected<T>(failure.Violations);
				break;
			case ApplyOutcome<T>.Applied { Notices.Count: > 0 } applied:
				diagnostics.Report<T>(applied.Notices);
				break;
		}
		return outcome;
	}

	/// <summary>Reports every violation the monitor reaches from now on (see <see cref="ConstraintDiagnostics.Watch{TEvent}"/>) and returns the monitor. Reporting stops when the monitor is disposed.</summary>
	public static TemporalMonitor<TEvent> ReportTo<TEvent>(this TemporalMonitor<TEvent> monitor, ConstraintDiagnostics diagnostics)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);
		_ = diagnostics.Watch(monitor);
		return monitor;
	}

	/// <summary>
	/// Registers a singleton <see cref="ConstraintDiagnostics"/> that logs to the <c>Nullean.Entities</c> category of the registered <see cref="ILoggerFactory"/>
	/// and creates its meter from the registered <see cref="IMeterFactory"/> (either may be missing: no logs, or the process-wide meter).
	/// </summary>
	public static IServiceCollection AddConstraintDiagnostics(this IServiceCollection services, Action<ConstraintDiagnosticsOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		var options = new ConstraintDiagnosticsOptions();
		configure?.Invoke(options);
		services.TryAddSingleton(sp =>
				new ConstraintDiagnostics(
					sp.GetService<ILoggerFactory>()?.CreateLogger(ConstraintDiagnostics.LoggerCategory) ?? NullLogger.Instance,
					sp.GetService<IMeterFactory>(),
					options
				));
		return services;
	}
}
