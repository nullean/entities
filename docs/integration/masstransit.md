---
navigation_title: MassTransit
description: Keep a temporal monitor per correlation id in a consumer, and check a saga's state changes against its state machine.
---

# MassTransit sagas and consumers

No package: this is a recipe. This repository does not reference MassTransit, so the snippet is **not compiled here**: treat it as the shape, and check the signatures against the
version you use.

A saga is a state machine plus a stream of messages, which is what the temporal monitor is for. Keep one `TemporalMonitor` per correlation id and feed it the messages:

```csharp
public sealed class OrderConsumer(MonitorStore monitors) : IConsumer<OrderShipped>
{
    public Task Consume(ConsumeContext<OrderShipped> context)
    {
        var monitor = monitors.For(context.Message.OrderId);        // TemporalMonitor<OrderEvent>, built from OrderRules.All
        monitor.Observe(new OrderEvent.Shipped(context.Message.OrderId));
        if (monitor.AnyViolated) /* log, alert, or fault the message */;
        return Task.CompletedTask;
    }
}
```

A deadline ("shipped within 5 minutes of confirmation") fires from the monitor's `TimeProvider`, so a test advances a `ManualTimeProvider` and the missed deadline appears
without sleeping. To check a recorded run instead, `Trace.Check(events, OrderRules.All)` takes the log (a MassTransit audit store, a Marten stream) and returns every violation with its time.

For the saga's own state, declare the allowed transitions once as a `StateMachine<string, string>`, use `machine.AsConstraint<Saga>(s => s.CurrentState)` as a sequential rule,
and call `constraints.Check(before, after)` around the state change.
