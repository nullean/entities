---
navigation_title: ASP.NET Core
description: Answer a request that breaks a domain rule with a standard 400 ValidationProblem, from minimal APIs and MVC, with no mapping code.
---

# ASP.NET Core

```bash
dotnet add package Nullean.Entities.AspNetCore
```

`Nullean.Entities.AspNetCore` validates the request body before the handler runs, turns a thrown `ConstraintViolationException` into the same response, and maps `Result<T>`
and `ApplyOutcome<T>` to 200 or 400. Errors are keyed by `Violation.Member`, or by the rule's name for a rule that is not about one member, so a client can attach each message to a form field.
It brings [`Nullean.Entities.Extensions.DependencyInjection`](options.md), needs the `Microsoft.AspNetCore.App` shared framework, uses no reflection, and is trim and NativeAOT clean.

## Reject a bad request

Register the constraints once with `services.AddConstraints<T>()`, then pick the hook that fits:

```csharp
// One endpoint, with the set named or taken from T.Constraints:
app.MapPost("/signup", (SignUp body) => Results.Created("/signup/1", body))
   .ValidateWith<RouteHandlerBuilder, SignUp>();      // 400 + errors { "Email": ["must be an email address"] }

// A whole group: every argument whose type was registered is validated, no reflection:
app.MapGroup("/api").ValidateConstraints();
```

```text
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{"title":"One or more validation errors occurred.","status":400,"errors":{"Email":["must be an email address"],"Age":["must be 18 or older"]}}
```

Every broken rule is reported at once, and the handler never runs. `.ValidateWith(constraintSet)` takes a companion's set instead of `T.Constraints`.

## Changes to an existing entity

The filters validate the body only (`Validate`, point-in-time rules). For a change, call `Apply` or `ApplyOrThrow` in the handler so sequential rules (`Transitions`, `Monotonic`)
see the before and after:

```csharp
// Handlers that apply a change and let a violation throw need no try/catch:
services.AddConstraintProblemDetails();               // + app.UseExceptionHandler()
SignUp.Constraints.ApplyOrThrow(current, next);       // ConstraintViolationException becomes a 400

// Or return the outcome: Result<T> and ApplyOutcome<T> map to 200 / 400.
return SignUp.Constraints.Apply(current, change).ToHttpResult();
```

`Applied` and `Noop` answer 200 and `Failure` answers 400. Warnings and hints of an applied change ride along in the 200 body as `{ "value": ..., "notices": [...] }`.
`exception.ToProblem()` returns the same 400 as an `IResult`, for code that catches the exception itself.

## MVC controllers

`services.AddControllers().AddConstraintValidation()` validates registered action arguments into `ModelState`; an `[ApiController]` answers with its usual 400.
`ModelState.AddViolations(violations, prefix)` is public for code that validates by hand; keys are `prefix.Member`.

## Limits

- `ValidateWith` takes the first handler argument of type `T`. `ValidateConstraints` looks up each argument by its exact runtime type, so a derived type needs its own registration.
- Only errors reject. Warnings and hints never produce a 400.
- Rules are synchronous and pure: a rule that needs a database call belongs in the handler.

The runnable example is [`examples/Nullean.Entities.Integration.Examples/AspNet`](https://github.com/nullean/entities/tree/main/examples/Nullean.Entities.Integration.Examples/AspNet),
and the package README on [nuget.org](https://www.nuget.org/packages/Nullean.Entities.AspNetCore) has a complete minimal API.
