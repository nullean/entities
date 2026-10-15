---
navigation_title: Time and events
description: Test a service whose rules are about order and deadlines, on a virtual clock, with no real waiting.
---

# Time and events

A deadline, an order of events, or "every one of these ends" cannot be checked on a single value. These rules are about a sequence, so the test drives the real service through generated calls and judges the events it recorded.

## The service takes a clock and records events

```csharp
internal sealed class Fulfilment(TimeProvider time, TimeSpan shipsAfter)
{
	public IReadOnlyList<Timestamped<FulfilmentEvent>> Log => _log;
	// ... Confirm() records Confirmed and Shipped, stamped from `time`
}
```

The service reads time only from the `TimeProvider` it is given. A service that calls `DateTime.UtcNow` or `Task.Delay` is invisible to a virtual clock, and a missed deadline can never happen in a test.

## The rules

```csharp
TemporalRuleSet<FulfilmentEvent>.From(
	("confirmed-ships-within-a-minute",
		Temporal<FulfilmentEvent>.AfterUntil(
			e => e is FulfilmentEvent.Confirmed,
			(_, next) => next is FulfilmentEvent.Shipped,
			TimeSpan.FromSeconds(60))));
```

## The test

```csharp
await EventAutoTest.For(rules, script, time => new Fulfilment(time, shipsAfter), f => f.Log).RunAsync();
```

`script` lists the operations (`Script.For<Fulfilment>().Step("Confirm", f => f.Confirm())`). The test builds a fresh service per run on a manual clock, advances it by generated delays, and checks the rules against the log.

- You write no delays. The test tries 0 and just either side of every rule's deadline, so a deadline is both met and missed within a few draws. A script that sets its own delays keeps them.
- A rule fails as *vacuous* (see [concepts](../concepts.md)) if no run contained its trigger, or none went past its deadline.
- If the service exposes its events as an `IAsyncEnumerable<Timestamped<T>>`, use `EventAutoTest.ForAsync(...)`.

Nothing here depends on Reactive Extensions. If your service is built on `IObservable<T>` and an Rx scheduler, the opt-in package `Nullean.Entities.CsCheck.Reactive` has the same script on a scheduler.

## What you read when it fails

:::{include} ../_snippets/temporal.txt
:language: text
:::
