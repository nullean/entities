---
name: nullean-entities-diagnostics
description: >-
  Use when constraint violations seen at runtime (rejected changes, missed temporal deadlines) need to show up in logs, metrics or traces, for example under OpenTelemetry. Nullean.Entities.Diagnostics reports them as ILogger records with stable event ids, counters on the "Nullean.Entities" meter and Activity events, with no OpenTelemetry dependency.
---

# Nullean.Entities.Diagnostics

`ConstraintDiagnostics` turns `Result<T>`, `ApplyOutcome<T>`, violation lists and `TemporalMonitor<TEvent>` verdicts into structured telemetry. It does not hook `MonitoredConstraintSet` from `Nullean.Entities.Reactive`; for an HTTP 400 response use `Nullean.Entities.AspNetCore` instead.

## The calls you need

```csharp
using Nullean.Entities;
using Nullean.Entities.Diagnostics;

services.AddConstraintDiagnostics(o => o.IncludeEvidence = false); // singleton, "Nullean.Entities" log category
```

```csharp
public sealed class Accounts(ConstraintDiagnostics diagnostics)
{
	public Result<Account> Withdraw(Account account, decimal amount) =>
		Account.Constraints.Apply(account, a => a with { Balance = a.Balance - amount }).ReportTo(diagnostics);
}
```

```csharp
diagnostics.Report<Account>(Account.Constraints.Validate(account)); // violations, no rejection counted
try { Account.Constraints.Ensure(account); }
catch (ConstraintViolationException e) { diagnostics.Rejected<Account>(e.Violations); }
```

```csharp
// every violation the monitor reaches, including a deadline that passes on its timer
var monitor = new TemporalMonitor<OrderEvent>(OrderRules.All, time).ReportTo(diagnostics);
using IDisposable watch = diagnostics.Watch(otherMonitor); // or stop reporting early
```

Subscribe by name: `metrics.AddMeter(ConstraintDiagnostics.MeterName)` in OpenTelemetry, `dotnet-counters monitor --counters Nullean.Entities`.

## Gotchas

- `Report<T>` never counts a rejection; `Rejected<T>` and `ReportTo` on a failed `Result` or `ApplyOutcome` count one. `ReportTo` on an `Ok` result reports nothing, so warnings are lost; use `ApplyDetailed(...).ReportTo(...)` to log the notices an `Applied` outcome carries.
- Evidence is off by default because it can be personal data, and it is never a metric tag or a trace event.
- Log levels are fixed: Error logs at Warning, Warning at Information, Hint at Debug, temporal violations at Warning. Filter on event ids (`ConstraintDiagnosticsEvents`: 4100, 4101, 4110, 4111), not on message text.
- The rule tag is `Violation.ConstraintId`, which a `ConstraintSet` fills from the rule's name. A violation you built by hand has none and is tagged `unnamed`.
- `AddConstraintDiagnostics` uses `TryAddSingleton`: a second call with other options is ignored. Register `AddMetrics()` (hosts do) or a process-wide meter is used.
- `ReportTo(monitor)` reports until the monitor is disposed; keep the `Watch` handle to stop sooner.

Docs: https://entities.nullean.net
