---
navigation_title: Mediator pipelines
description: Validate every request in a MediatR, Mediator or Wolverine pipeline against its constraints, and stop it before the handler runs.
---

# Mediator pipelines (MediatR, Mediator, Wolverine)

No package: each of these is a recipe of a dozen lines. All three are compiled in
[`examples/Nullean.Entities.Recipes`](https://github.com/nullean/entities/tree/main/examples/Nullean.Entities.Recipes) and run against the real library in
`tests/Nullean.Entities.Recipes.Tests/MediatorPipelineTests.cs`: an invalid request throws `ConstraintViolationException` before its handler, a valid one is handled.

Two ways to find the rules for a request:

- **`IConstraintRegistry`** (from `Nullean.Entities.Extensions.DependencyInjection`): register each request type with `services.AddConstraints<T>()`, and a behaviour that
  only holds a `TRequest` validates it with `registry.TryValidate(request, out var violations)`. Unregistered types pass through. No reflection.
- **`T.Constraints`**: constrain the behaviour to `where T : IHaveConstraints<T>` and read the set statically. Nothing to register, but only types that declare their own
  constraints get the behaviour.

Only `Severity.Error` violations reject; warnings and hints are left to you. Turn the exception into a response at the edge, for example with
`AddConstraintProblemDetails()` from [ASP.NET Core](aspnetcore.md).

## MediatR

```csharp
public sealed class ConstraintBehavior<TRequest, TResponse>(IConstraintRegistry registry) : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (registry.TryValidate(request, out var violations) && violations.Any(v => v.Severity == Severity.Error))
        {
            throw new ConstraintViolationException(violations);
        }
        return next(cancellationToken);
    }
}

services
    .AddConstraints<RegisterUser>()
    .AddMediatR(cfg =>
    {
        cfg.LicenseKey = licenseKey; // required in production only; read it from configuration
        cfg.RegisterServicesFromAssemblyContaining<RegisterUserHandler>();
        cfg.AddOpenBehavior(typeof(ConstraintBehavior<,>));
    });
```

Tested with MediatR 14.2.0. The source has `typeof(ConstraintBehavior<, >)` inside a `#pragma warning disable IDE0055`: this repository's formatter (curb) writes an
open generic that way and the IDE0055 analyzer then rejects it. That is a quirk of this repository's tooling, not of MediatR.

:::{note}
MediatR is commercially licensed from version 13.0.0 by Lucky Penny Software. Their [licensing FAQ](https://luckypennysoftware.com/faq) says only production
deployments need a license key ("No license is required for development, CI/CD, staging, QA, integration testing, or any other non-production environment"), that a
missing key is reported through warning or error log messages rather than by limiting the library, and that a free Community license is available to organisations under
USD 5 million in annual revenue. See [pricing](https://luckypennysoftware.com/community) and the [license agreement](https://luckypennysoftware.com/license). Earlier
versions keep their original open-source license. The tests here run without a key.
:::

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
command fails that way. To avoid the question, use the registry form from the MediatR recipe instead.

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

Tested with WolverineFx 6.61.0. Wolverine 6 compiles handler and middleware code at runtime only with the `WolverineFx.RuntimeCompilation` package referenced (or with
pre-generated code and `TypeLoadMode.Static`); without it the host fails to start and says so.
