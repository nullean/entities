# SupportDesk

A support desk on virtual time. `dotnet run --project examples/Apps/SupportDesk` (exits non-zero if anything stops behaving as narrated).

| File | What to copy |
|---|---|
| `Domain.cs` | The ticket lifecycle as a **value** state machine (`StateMachine<TicketStatus, string>`, with an `After(3 days)` timeout), the rules about a ticket, and `SlaRules`: rules about *sequences in time* ("a P1 is acknowledged within the hour"). |
| `TicketService.cs` | A service on a `TimeProvider`: a transition is `machine.TryFire` plus `Apply`; `SweepAsync` applies the machine's timeouts; `RunSweeperAsync` is the loop a hosted service would run. The event log feeds a live `TemporalMonitor` and keeps the recording for an audit. |
| `Program.cs` | The scenarios, narrated: the clock is a `ManualTimeProvider`, so "an hour later" and "three days later" are one line each. |

Tests: `tests/Nullean.Entities.Apps.Tests/SupportDesk`. The service is checked against its machine with `ConformToService` (a declared trigger the service cannot perform is reported before any run), the closes-once rule is checked under generated schedules, and the SLA rule is shown *failing* under the same schedules, with the shortest way to break it.

Things worth noticing:

- Two kinds of time. A **timeout** (`After`) is part of the machine: it is what the sweeper applies. A **deadline on a sequence** (`AfterUntil`) is a rule over events: the service cannot cause it, only observe it. A human has to acknowledge a P1; the monitor tells you when they did not.
- A violation is reported at the instant of the deadline (10:00), not when it was noticed (10:01), so an alert can say how late it is.
- The same `TemporalRuleSet` runs live (`TemporalMonitor`, on the provider's timers) and afterwards (`Trace.Check` over the recorded log), and they agree.
- The service never sleeps. Every wait is the injected clock, which is why the tests that cover three days run in milliseconds.
