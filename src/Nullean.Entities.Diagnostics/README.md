# Nullean.Entities.Diagnostics

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.Diagnostics.svg)](https://www.nuget.org/packages/Nullean.Entities.Diagnostics)

**Every rejected change and every missed deadline shows up in your logs, metrics and traces, under names OpenTelemetry subscribes to, without an OpenTelemetry dependency.**

Without this package, a violation your service sees at runtime is a `Result.Fail` someone has to remember to log, and a deadline a `TemporalMonitor` reaches on its timer is seen by nobody. This package reports both through the platform's own abstractions: structured `ILogger` records with stable event ids, counters on a `System.Diagnostics.Metrics` meter named `Nullean.Entities`, and an event on the current `Activity`.

It builds on [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities) and `Microsoft.Extensions.Logging.Abstractions`, nothing else. It is optional, it uses no reflection, and it is trim and NativeAOT clean.

## Install

Targets `net10.0` and `net11.0`.

```bash
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.Diagnostics
```

## Get going

Register it once, and pass outcomes through `ReportTo`. It returns what it was given, so it chains.

```csharp
using Nullean.Entities;
using Nullean.Entities.Diagnostics;

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

A withdrawal below zero logs event 4100 (`ConstraintViolated`: `EntityType`, `Rule`, `Severity`, `Member`, `ViolationMessage`) and counts one `entities.violations` and one `entities.rejections`. A confirmed order that does not ship in time logs event 4110 (`TemporalRuleViolated`) at the deadline's own instant, from the monitor's timer, with no further event needed.

Then subscribe in OpenTelemetry by the meter's name:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(ConstraintDiagnostics.MeterName))   // "Nullean.Entities"
    .WithLogging()
    .UseOtlpExporter();
```

## What you get

- `ConstraintDiagnostics(ILogger logger, IMeterFactory? meterFactory = null, ConstraintDiagnosticsOptions? options = null)`, and `services.AddConstraintDiagnostics(configure)` for a singleton.
- `Result<T>.ReportTo(diagnostics)` and `ApplyOutcome<T>.ReportTo(diagnostics)`: a failure counts one rejection and reports its violations; an `Applied` reports the warnings and hints it was accepted with.
- `diagnostics.Report<T>(violations)`: for `Validate`, `Check` or a caught `ConstraintViolationException`.
- `TemporalMonitor<TEvent>.ReportTo(diagnostics)` and `diagnostics.Watch(monitor)` (an `IDisposable`): every violation the monitor reaches, deadlines included.
- Counters `entities.violations` (tags `entities.rule`, `entities.severity`, `entities.type`) and `entities.rejections` (tag `entities.type`) on the meter `Nullean.Entities`.
- Log event ids in `ConstraintDiagnosticsEvents`: 4100, 4101 (with evidence), 4110 (temporal), 4111 (temporal, with evidence).
- An `entities.violation` event on `Activity.Current`, when there is one that records.

## Pitfalls and limits

- `Violation.Evidence` can hold personal data, so it is not logged unless `IncludeEvidence = true`, and it is never a metric tag or a trace event.
- Log levels are fixed: Warning for an error and for a temporal violation, Information for a warning, Debug for a hint. Filter with the usual logging rules.
- `ReportTo` on a monitor reports until the monitor is disposed; use `Watch` to stop earlier.
- The monitor reports on the thread that caused the change, the `TimeProvider`'s timer for a deadline.
- `MonitoredConstraintSet<TEvent>` in `Nullean.Entities.Reactive` is not hooked.

## Next

- This integration on the docs site: https://entities.nullean.net/integration/diagnostics/, and every integration: https://entities.nullean.net/integration/
- Time and temporal rules: https://entities.nullean.net/guides/time/
- Options validation and registration: https://www.nuget.org/packages/Nullean.Entities.Extensions.DependencyInjection
- Tests for the package: https://github.com/nullean/entities/tree/main/tests/Nullean.Entities.Diagnostics.Tests
