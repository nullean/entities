# Using Nullean.Entities in a real service

The library has no opinion about your framework: a constraint set is a value you call at the edge of your domain. This page shows where to call it in four common stacks.

Which parts are tested here: the **library side** of every recipe is tested in `tests/Nullean.Entities.Examples.Tests/Integration`, and the ASP.NET Core and Marten-style
recipes compile and run as `examples/Nullean.Entities.Integration.Examples` (the shared framework needs no extra package). The **EF Core and MassTransit** snippets below
use those packages' APIs as I know them, but this repository does not reference those packages, so they are **not compiled here**; treat them as the shape, and check the
package signatures against the version you use.

## What to put where

| Concern | Where the rule goes |
|---|---|
| Invariants of one aggregate (a total is never negative, a state change is legal, nothing is created or destroyed) | A `ConstraintSet<T>` on the aggregate (`[Constraints]`), checked in the domain |
| Rules over a collection inside the aggregate (unique ids, dangling references, cycles) | `Unique`, `References`, `Acyclic`, `NoChains` in the same set |
| Rules that need a database (is this email taken, does this customer exist in another service) | **Not here.** Constraints are synchronous and pure on purpose; do those in the application service |
| What must happen over time (publish only after validation, reply within 10 minutes) | `Temporal<TEvent>` rules, checked over an event log or a live stream |
| "Does my implementation follow the lifecycle diagram?" | `StateMachine<,>` and `ConformTo` in a test |

## ASP.NET Core minimal APIs

Reject a bad request body with a standard `ValidationProblem`, keyed by member (`Violation.Member`) or, for rules that are not about one member, by the rule's name:

```csharp
app.MapPost("/signup", (SignUp body) => Results.Created("/signup/1", body))
   .ValidateWith(SignUpRules);                       // 400 + errors { "Email": ["Email must contain an @."] }

// in a handler that applies a change and lets a violation throw:
catch (ConstraintViolationException e) { return e.ToProblem(); }
```

`ConstraintFilter<T>`, `ValidateWith` and `ToProblem` are in `examples/Nullean.Entities.Integration.Examples/AspNet`. They are twenty lines; copy them. The filter validates the
body only (`Validate`, point-in-time rules). For a change to an existing entity, call `Apply`/`ApplyOrThrow` in the handler so sequential rules (`Transitions`, `Monotonic`) see the before and after.

## Marten and event sourcing (Decide / Evolve)

Marten's aggregate conventions (a static `Create(event)` and instance `Apply(event)` methods) are plain methods that return the next value, which is exactly what the library
wants. Write the decision as a function from state and command to events, fold with the aggregate's own `Apply`, and the aggregate's own code is what gets tested:

```csharp
var spec = EntitySpec.EventSourced<Invoice, InvoiceCommand, object>(
    initial, InvoiceDecider.Decide, (invoice, e) => invoice.Evolve(e),
    [new Pay(50m), new Pay(100m), new Void()], InvoiceConstraints.All);

spec.Spec.Exhaustive();                      // the state rules hold after every command sequence
spec.Exhaustive(NoPaymentAfterVoid);         // so does a temporal rule over the emitted events
```

In the command handler, run the same check before appending (shape only; the `session` calls are Marten's):

```csharp
var state = await session.Events.AggregateStreamAsync<Invoice>(id);
var events = InvoiceDecider.Decide(state, command).ToList();
var next = events.Aggregate(state, (s, e) => s.Evolve(e));
if (InvoiceConstraints.All.Check(state, next) is { Count: > 0 } violations) return Refuse(violations);
session.Events.Append(id, events);
```

The exhaustive search found, in `Integration/Marten/Invoice.cs`, a decide without the overpayment guard and one that pays a voided invoice, each with the shortest command sequence.

## EF Core

EF entities are mutated in place, so `Apply` cannot see a "before". Use `Check` with EF's original values from a `SaveChangesInterceptor` (not compiled here):

```csharp
public sealed class OrderConstraintsInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData data, InterceptionResult<int> result)
    {
        foreach (var entry in data.Context!.ChangeTracker.Entries<Order>())
        {
            IReadOnlyList<Violation> violations = entry.State switch
            {
                EntityState.Added => Order.Constraints.Validate(entry.Entity),
                EntityState.Modified => Order.Constraints.Check((Order)entry.OriginalValues.ToObject(), entry.Entity),
                _ => [],
            };
            if (violations.Count > 0) throw new ConstraintViolationException(violations);
        }
        return result;
    }
}
```

Override `SavingChangesAsync` the same way. `ToObject()` builds a new entity from the original values, which is the "before" the sequential rules need. Rules about a collection
navigation (`Unique` over `order.Lines`) work when the navigation is loaded; they see only what is in memory.

## MassTransit sagas and consumers

A saga is a state machine plus a stream of messages, which is what the temporal monitor is for. Keep one `TemporalMonitor` per correlation id and feed it the messages
(not compiled here):

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

## Trim and AOT

If your service is published NativeAOT, `Nullean.Entities` and `Nullean.Entities.Reactive` are analyzed and published NativeAOT in this repository's CI (`./build.sh aot`).
The `Nullean.Entities.CsCheck` packages are for tests only and are not AOT-compatible.
