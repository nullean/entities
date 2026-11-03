---
navigation_title: Marten
description: Test a Marten-style event-sourced aggregate's own Decide and Apply methods, and check the rules before appending.
---

# Marten and event sourcing (Decide / Evolve)

No package: this is a recipe. The library side is compiled and tested in
[`examples/Nullean.Entities.Integration.Examples/Marten`](https://github.com/nullean/entities/tree/main/examples/Nullean.Entities.Integration.Examples/Marten); the `session`
calls are Marten's.

Marten's aggregate conventions (a static `Create(event)` and instance `Apply(event)` methods) are plain methods that return the next value, which is exactly what the library
wants. Write the decision as a function from state and command to events, fold with the aggregate's own `Apply`, and the aggregate's own code is what gets tested:

```csharp
var spec = EntitySpec.EventSourced<Invoice, InvoiceCommand, object>(
    initial, InvoiceDecider.Decide, (invoice, e) => invoice.Evolve(e),
    [new Pay(50m), new Pay(100m), new Void()], InvoiceConstraints.All);

spec.Spec.Exhaustive();                      // the state rules hold after every command sequence
spec.Exhaustive(NoPaymentAfterVoid);         // so does a temporal rule over the emitted events
```

In the command handler, run the same check before appending:

```csharp
var state = await session.Events.AggregateStreamAsync<Invoice>(id);
var events = InvoiceDecider.Decide(state, command).ToList();
var next = events.Aggregate(state, (s, e) => s.Evolve(e));
if (InvoiceConstraints.All.Check(state, next) is { Count: > 0 } violations) return Refuse(violations);
session.Events.Append(id, events);
```

The exhaustive search found, in `Integration/Marten/Invoice.cs`, a decide without the overpayment guard and one that pays a voided invoice, each with the shortest command sequence.

To check a recorded stream instead, `Trace.Check(events, rules)` takes the events as `Timestamped<TEvent>` (Marten stores a timestamp with each event) and returns every violation of a temporal rule with its time.
[`examples/Apps`](https://github.com/nullean/entities/tree/main/examples/Apps) has an event-sourced cart built on the same pattern, in memory.
