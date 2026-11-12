---
navigation_title: Mediator pipelines
description: Validate every request in a Mediator or Wolverine pipeline against its constraints, and stop it before the handler runs.
---

# Mediator pipelines (Mediator, Wolverine)

No package: each of these is a recipe of a dozen lines. Both are compiled in
[`examples/Nullean.Entities.Recipes`](https://github.com/nullean/entities/tree/main/examples/Nullean.Entities.Recipes) and run against the real library in
`tests/Nullean.Entities.Recipes.Tests/MediatorPipelineTests.cs`: an invalid request throws `ConstraintViolationException` before its handler, a valid one is handled.

Two ways to find the rules for a request:

- **`IConstraintRegistry`** (from `Nullean.Entities.Extensions.DependencyInjection`): register each request type with `services.AddConstraints<T>()`, and a behaviour that
  only holds a `TRequest` validates it with `registry.TryValidate(request, out var violations)`. Unregistered types pass through. No reflection.
- **`T.Constraints`**: constrain the behaviour to `where T : IHaveConstraints<T>` and read the set statically. Nothing to register, but only types that declare their own
  constraints get the behaviour.

Only `Severity.Error` violations reject; warnings and hints are left to you. Turn the exception into a response at the edge, for example with
`AddConstraintProblemDetails()` from [ASP.NET Core](aspnetcore.md).

## Mediator (source-generated)

[martinothamar/Mediator](https://github.com/martinothamar/Mediator) generates its dispatch code at compile time. Here the behaviour is constrained to messages that declare
their own constraints and reads `TMessage.Constraints`:

```csharp
public sealed class ConstraintBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse> where TMessage : IMessage, IHaveConstraints<TMessage>
{
    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var violations = TMessage.Constraints.Validate(message);
        if (violations.Any(v => v.Severity == Severity.Error))
        {
            throw new ConstraintViolationException(violations);
        }
        return next(message, cancellationToken);
    }
}

public sealed partial record PlaceOrder(string Sku, int Quantity) : ICommand<string>, IHaveConstraints<PlaceOrder>
{
    [Constraint]
    public static readonly Constraint<PlaceOrder> PositiveQuantity = Constraints<PlaceOrder>.Check(
        o => o.Quantity > 0 ? [] : [Violation.Error(nameof(Quantity), "must be at least 1")]
    );
}

services.AddMediator(options =>
{
    options.Namespace = "Nullean.Entities.Recipes.Mediator.Generated";
    options.PipelineBehaviors = [typeof(ConstraintBehavior<,>)];
});
```

**Write `IHaveConstraints<PlaceOrder>` on the message yourself**, although the `[Constraint]` generator adds it too. Source generators do not see each other's output, so
without it Mediator's generator finds no message that satisfies the behaviour's constraint and leaves the behaviour out without a warning: the test for the invalid
command fails that way. To avoid the question, use the registry form from the Wolverine recipe instead.

Tested with Mediator.SourceGenerator and Mediator.Abstractions 3.0.2. Register the behaviour through `MediatorOptions.PipelineBehaviors`, as above, rather than as an open
generic in the container; that is also the form Mediator's README recommends for NativeAOT.

## Wolverine

[Wolverine](https://wolverinefx.net) middleware is a class with a `Before` method; its parameters are resolved like a handler's, and an exception stops the handler.
`InvokeAsync` rethrows it to the caller; a message from a queue goes through Wolverine's error policies instead.

```csharp
public static class ConstraintMiddleware
{
    public static void Before(Envelope envelope, IConstraintRegistry registry)
    {
        if (envelope.Message is { } message && registry.TryValidate(message, out var violations) && violations.Any(v => v.Severity == Severity.Error))
        {
            throw new ConstraintViolationException(violations);
        }
    }
}

builder.Services.AddConstraints<ShipParcel>();
builder.UseWolverine(opts =>
{
    // Only this recipe's handler: the other recipes in the example assembly have classes Wolverine's naming conventions would pick up too.
    opts.Discovery.DisableConventionalDiscovery().IncludeType(typeof(ShipParcelHandler));
    opts.Policies.AddMiddleware(typeof(ConstraintMiddleware));
});
```

Tested with WolverineFx 6.64.0. Wolverine 6 compiles handler and middleware code at runtime only with the `WolverineFx.RuntimeCompilation` package referenced (or with
pre-generated code and `TypeLoadMode.Static`); without it the host fails to start and says so.
