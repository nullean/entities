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

## ASP.NET Core (`Nullean.Entities.AspNetCore`)

Reject a bad request with a standard `ValidationProblem`, keyed by member (`Violation.Member`) or, for rules that are not about one member, by the rule's name. Register the
constraints once with `services.AddConstraints<T>()` (from `Nullean.Entities.Extensions.DependencyInjection`), then pick the hook that fits:

```csharp
// One endpoint, with the set named or taken from T.Constraints:
app.MapPost("/signup", (SignUp body) => Results.Created("/signup/1", body))
   .ValidateWith<RouteHandlerBuilder, SignUp>();      // 400 + errors { "Email": ["must be an email address"] }

// A whole group: every argument whose type was registered is validated, no reflection:
app.MapGroup("/api").ValidateConstraints();

// Handlers that apply a change and let a violation throw need no try/catch:
services.AddConstraintProblemDetails();               // + app.UseExceptionHandler()
SignUp.Constraints.ApplyOrThrow(current, next);       // ConstraintViolationException becomes a 400

// Or return the outcome: Result<T> and ApplyOutcome<T> map to 200 / 400.
return SignUp.Constraints.Apply(current, change).ToHttpResult();
```

For MVC controllers, `services.AddControllers().AddConstraintValidation()` validates registered action arguments into `ModelState`; an `[ApiController]` answers with its usual 400.
`ModelState.AddViolations(violations, prefix)` is public for code that validates by hand.

The filters validate the body only (`Validate`, point-in-time rules). For a change to an existing entity, call `Apply`/`ApplyOrThrow` in the handler so sequential rules
(`Transitions`, `Monotonic`) see the before and after. The example is in `examples/Nullean.Entities.Integration.Examples/AspNet`.

## Options (`Nullean.Entities.Extensions.DependencyInjection`)

```csharp
services.AddOptions<ServerOptions>().BindConfiguration("Server").ValidateConstraints(ServerRules).ValidateOnStart();
```

A violation fails startup with `OptionsValidationException`, with failures worded `Member: message`.

## Command-line apps (`Nullean.Entities.Argh`)

For a [Nullean.Argh](https://github.com/nullean/argh) command, register the constraints for its option type and a violation fails the way Argh's own checks do:

```csharp
ArghConstraints.Register<ServeOptions>();             // before RunAsync; or Register(ServeRules.All) for a companion's set

app.Map("serve", ([AsParameters("serve")] ServeOptions options) => ...);
// $ app serve --serve-port 70000
// Error: --serve-port: must be between 1 and 65535        (then the flag's help rows and the usage line; exit code 2, handler not run)
```

It works through Argh's `ArghValidation` hook, so it covers `[AsParameters]`, global and namespace options, and `TryParseArgh` in tests. Needs Nullean.Argh 0.21.

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

## Rules over two aggregates: the pair model

A rule that needs two aggregates (a documentation set and the product catalog its ids point into, a navigation and the assembly whose repositories it mounts) is a constraint over a
record that holds both. docs-builder's own `DocumentationSetInCatalog(Docset, ProductCatalog)` is exactly this, and the shapes work on it unchanged:

```csharp
public sealed record DocsetInCatalog(Docset Docset, Catalog Catalog);

public static readonly Constraint<DocsetInCatalog> ProductsExist = Constraints<DocsetInCatalog>
    .References<string, string>(p => p.Docset.Products, id => [id], p => p.Catalog.Products.Select(x => x.Id));
```

To reuse rules written for one half, `ConstraintSet.Pair(setForFirst, setForSecond)` makes one set over `(First, Second)` that keeps each rule's name, and `ConstraintSet.Merge(a, b)` joins sets over one type.
Give the pair only what its rules need: the existing pair models carry only the product catalog, which is why a codex group, a cross-link repository or a mount's repository was never checked.

How the references are written does not matter to the rules: a list of id strings, a key in a dictionary, the scheme or path of a `Uri`, a path inside a tree. `References` takes any projection,
`KeysMatch` covers a dictionary keyed by the entity's own name, and `Tree.Flatten(roots, n => n.Children)` lets `Unique`, `References` and `Acyclic` run over every level of a tree at once.
For generated data, declare those relations to AutoGen (`o.References<Docset, Product, string>(d => d.Products, p => p.Id)`, see the CsCheck README): the names alone will not give them away.

## Declared and resolved layers

Put a rule on the layer where the thing it forbids can exist. docs-builder expands `toc.yml` includes while loading, and guards against a loop while doing it, so the resolved tree **cannot** hold a
cycle; "no include cycle" is a rule about the include graph *before* expansion (a record of files and the paths they include). The resolved tree has its own rule (no file listed twice,
`Unique` over `Tree.Flatten`). Writing the cycle rule against the expanded tree would prove nothing, and generated data could never break it.

## Severity: errors reject, warnings and hints are noticed

`constraint.WithSeverity(Severity.Warning)` (or `Hint`) sets the severity of every violation of a rule, whatever shape it is. `Validate` returns all of them. `Apply` rejects a change only for
**errors**; warnings and hints ride along on `ApplyOutcome.Applied.Notices`, so a rule that is only a hint today can be written now without blocking anything. `Violation` carries `File`,
`Line`, `Column` and `Length`, and the item shapes take `at:` (a `Func<TItem, SourceLocation?>`) so a finding points at the line the offending item came from.

Mapping to your own findings type is one extension method on your side; for docs-builder's `Finding(Severity, Message, Line, Column, File, Length)`:

```csharp
public static Finding ToFinding(this Violation v) =>
    new(v.Severity switch { Nullean.Entities.Severity.Error => Severity.Error, Nullean.Entities.Severity.Warning => Severity.Warning, _ => Severity.Hint },
        v.Message, v.Line, v.Column, v.File, v.Length);
```

## Models that hold the file system

If the model holds `IFileInfo` / `IDirectoryInfo` / `IFileSystem` (docs-builder's `DocumentationSetDefinition` does), `Nullean.Entities.FileSystem` has the file rules (`Files<T>.Exist`, `StayInside`,
`NotSymlink`, `NoHiddenSegments`, `HaveExtension`), reading the file system each path came from. `Nullean.Entities.CsCheck.FileSystem` generates the model: `o.UseFileSystem()` builds one
`MockFileSystem` world per value with missing, symlinked, hidden and outside-the-root files, so `Probe` finds counter-examples without a hand-written file system.

## Trim and AOT

If your service is published NativeAOT, `Nullean.Entities`, `Nullean.Entities.Reactive` and `Nullean.Entities.FileSystem` are analyzed and published NativeAOT in this repository's CI (`./build.sh aot`).
The `Nullean.Entities.CsCheck` packages are for tests only and are not AOT-compatible.

## Complete, runnable versions

The recipes above are fragments. [`examples/Apps`](../examples/Apps) has eight console apps that run end to end and are tested like a real service would be: an order service on a type-state machine, a bank transfer, a support desk on virtual time, an event-sourced cart (the Marten pattern, in memory), inventory across two aggregates, an audit of existing rows, a hierarchy edited as a tree, and a docs build over a file system. See [`docs/first-use-review.md`](first-use-review.md) for what building them found.
