---
navigation_title: Logging and metrics
description: Violations seen at runtime as structured logs, System.Diagnostics.Metrics counters and Activity events, which OpenTelemetry picks up by name. No OpenTelemetry dependency.
---

# Logging and metrics, OpenTelemetry

```bash
dotnet add package Nullean.Entities.Diagnostics
```

A rejected change and a missed deadline are things you want to see in production, not only in a test. `Nullean.Entities.Diagnostics` reports them through the platform's own
abstractions: an `ILogger` (`Microsoft.Extensions.Logging.Abstractions`, its only dependency), a `System.Diagnostics.Metrics` meter, and an event on the current `Activity`.
OpenTelemetry, `dotnet-counters`, Application Insights or a plain console logger subscribe to those by name; the package does not reference any of them. It uses no
reflection and is trim and NativeAOT clean (`examples/Nullean.Entities.AotSmoke` publishes it).

## Report from your services

Register one `ConstraintDiagnostics` and pass the outcome of `Apply` through `ReportTo`. It returns the same result, so it chains. A `TemporalMonitor` reports every violation it
reaches from then on, including a deadline that passes on its timer with no event to carry it:

```csharp
builder.Services.AddConstraintDiagnostics();     // logs to the "Nullean.Entities" category, meter from the host's IMeterFactory

public sealed class Accounts(ConstraintDiagnostics diagnostics)
{
    public Result<Account> Withdraw(Account account, decimal amount) =>
        Account.Constraints.Apply(account, a => a with { Balance = a.Balance - amount }).ReportTo(diagnostics);
}

public sealed class Shipments(ConstraintDiagnostics diagnostics, TimeProvider time)
{
    private readonly TemporalMonitor<OrderEvent> _monitor = new TemporalMonitor<OrderEvent>(OrderRules.All, time).ReportTo(diagnostics);

    public void Confirmed(int id) => _monitor.Observe(new OrderEvent.Confirmed(id));
}
```

Without a container, `new ConstraintDiagnostics(logger)` (or `new ConstraintDiagnostics(logger, meterFactory, options)`) does the same.

| You have | Call |
|---|---|
| `Result<T>` from `Apply`, `ApplyAsync`, `Validated` | `.ReportTo(diagnostics)`: a `Fail` counts one rejection and reports its violations |
| `ApplyOutcome<T>` from `ApplyDetailed` | `.ReportTo(diagnostics)`: a `Failure` as above; an `Applied` reports the warnings and hints it was accepted with |
| Violations from `Validate` or `Check`, or a caught `ConstraintViolationException` | `diagnostics.Report<T>(violations)`: reports them, counts no rejection |
| A `TemporalMonitor<TEvent>` | `.ReportTo(diagnostics)`, or `diagnostics.Watch(monitor)` for an `IDisposable` that stops it |

The monitor hook is the core's `TemporalMonitor.VerdictChanged` event, which sees the same changes as `Changes()` without competing with a reader of it.

## What is written

**Metrics**, on the meter `Nullean.Entities` (`ConstraintDiagnostics.MeterName`):

| Instrument | Unit | One per | Tags |
|---|---|---|---|
| `entities.violations` | `{violation}` | violation, of any severity | `entities.rule`, `entities.severity` (`error`, `warning`, `hint`), `entities.type` |
| `entities.rejections` | `{change}` | change rejected by an error | `entities.type` |

`entities.type` is the type's name (`Account`; the event type for a temporal rule). The member and the evidence are never tags: they would make the series unbounded.

**Logs**, with stable event ids (`ConstraintDiagnosticsEvents`); filter and alert on the id, not the text:

| Event id | Name | Level | Fields |
|---|---|---|---|
| 4100 | `ConstraintViolated` | Warning for an error, Information for a warning, Debug for a hint | `EntityType`, `Rule`, `Severity`, `Member`, `ViolationMessage` |
| 4101 | `ConstraintViolatedWithEvidence` | as 4100 | as 4100, plus `Evidence` |
| 4110 | `TemporalRuleViolated` | Warning | `EntityType`, `Rule`, `ViolatedAt` (the deadline's own instant for a missed deadline) |
| 4111 | `TemporalRuleViolatedWithEvidence` | Warning | as 4110, plus `Evidence` |

**Traces**: each violation adds an `entities.violation` event, with the same tags plus `entities.member`, to `Activity.Current` (the request's span in ASP.NET Core), when
there is one that records. No `ActivitySource` of its own, so there is nothing extra to add to tracing.

### Evidence stays out by default

`Violation.Evidence` is the value that broke the rule, which can be an email or an account number. It is not logged unless you ask, and it is never a metric tag or a trace
event:

```csharp
builder.Services.AddConstraintDiagnostics(o => o.IncludeEvidence = true);   // logs event 4101/4111 with {Evidence} instead of 4100/4110
```

## Wire it to OpenTelemetry

Subscribe to the meter by name; logs reach OpenTelemetry through the usual logging pipeline, and the trace events ride on the spans you already export:

```csharp
builder.Services.AddConstraintDiagnostics();
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(ConstraintDiagnostics.MeterName))   // "Nullean.Entities"
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
    .WithLogging()
    .UseOtlpExporter();
```

With `dotnet-counters`: `dotnet-counters monitor -n <process> --counters Nullean.Entities`.

## Pitfalls and limits

- Log levels are fixed (see the table). Change what reaches a sink with the usual logging filters on the `Nullean.Entities` category or the event id.
- `ReportTo` on a monitor subscribes until the monitor is disposed. Use `Watch` when the diagnostics should stop earlier.
- A `VerdictChanged` handler runs on the thread that caused the change: the caller of `Observe` or `Complete`, or the `TimeProvider`'s timer for a deadline. Logging and counting
  there is cheap; do not block in your own handler.
- `MonitoredConstraintSet<TEvent>` (`Nullean.Entities.Reactive`) is not hooked. Each of its rules has a `Verdicts` observable; feeding those to
  `diagnostics.Report<TEvent>(new VerdictChange(name, verdict, at))` reports them the same way (not tested here).

Tested in [`tests/Nullean.Entities.Diagnostics.Tests`](https://github.com/nullean/entities/tree/main/tests/Nullean.Entities.Diagnostics.Tests): the service snippet and
`IncludeEvidence` in `DocsSnippetTests.cs`; the counters, tags, log records, Activity events, and a deadline missed on a `ManualTimeProvider` (exactly one log record and one count)
in `ConstraintDiagnosticsTests.cs`, which also checks that `ConstraintDiagnostics.MeterName` is `Nullean.Entities`. The OpenTelemetry snippet is **not compiled here** (this
repository does not reference OpenTelemetry); its calls were checked against OpenTelemetry.Extensions.Hosting and OpenTelemetry.Exporter.OpenTelemetryProtocol 1.20.1 and
OpenTelemetry.Instrumentation.AspNetCore 1.19.0.
