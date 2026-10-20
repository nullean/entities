# Nullean.Entities.AspNetCore

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.AspNetCore.svg)](https://www.nuget.org/packages/Nullean.Entities.AspNetCore)

**Your domain rules answer bad requests with a standard 400 `ValidationProblem`, and you do not write the mapping.**

Without this package, a rule that lives on your domain type is checked in each handler, and each handler turns the failures into a response its own way. This package validates the request body before the handler runs, turns a thrown `ConstraintViolationException` into the same response, and maps `Result<T>` and `ApplyOutcome<T>` to 200 or 400. Errors are keyed by `Violation.Member`, so a client can attach each message to a form field.

It builds on [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities) and [Nullean.Entities.Extensions.DependencyInjection](https://www.nuget.org/packages/Nullean.Entities.Extensions.DependencyInjection) (installed with it). It is optional, it needs the `Microsoft.AspNetCore.App` shared framework, it uses no reflection, and it is trim and NativeAOT clean.

## Install

Targets `net10.0` and `net11.0`.

```bash
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.AspNetCore
```

## Get going

A complete minimal API (`Microsoft.NET.Sdk.Web`). `SignUp` declares its rules next to the type; `AddConstraints` registers them.

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nullean.Entities;
using Nullean.Entities.AspNetCore;
using Nullean.Entities.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddConstraints<SignUp>().AddConstraintProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();

app.MapPost("/signup", (SignUp s) => TypedResults.Ok(s.Email)).ValidateWith<RouteHandlerBuilder, SignUp>();

app.Run();

public sealed partial record SignUp(string Email, int Age)
{
	[Constraint]
	public static readonly Constraint<SignUp> HasEmail = Constraints<SignUp>.Check(
		s => s.Email.Contains('@') ? [] : [Violation.Error(nameof(Email), "must be an email address")]
	);

	[Constraint]
	public static readonly Constraint<SignUp> Adult = Constraints<SignUp>.Check(
		s => s.Age >= 18 ? [] : [Violation.Error(nameof(Age), "must be 18 or older")]
	);
}
```

```bash
curl -i -X POST localhost:5000/signup -H 'Content-Type: application/json' -d '{"email":"nope","age":3}'
```

```text
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{"title":"One or more validation errors occurred.","status":400,"errors":{"Email":["must be an email address"],"Age":["must be 18 or older"]}}
```

(ASP.NET Core also adds a `type` field.) Every broken rule is reported at once, and the handler never runs. A valid body reaches the handler and returns 200.

The second example is the main value: a handler that changes an existing entity and lets a violation throw. `Apply`-style checks see the before and after, so rules like `Transitions` and `Monotonic` work, which a body-only filter cannot do.

```csharp
// Registered with AddConstraintProblemDetails() + app.UseExceptionHandler()
app.MapPost("/signup/age", (SignUp current) =>
	SignUp.Constraints.ApplyOrThrow(current, s => s with { Age = 1 }));   // ConstraintViolationException becomes a 400

// Or return the outcome: Result<T> and ApplyOutcome<T> map to 200 / 400
app.MapPost("/signup/age2", (SignUp current) =>
	SignUp.Constraints.Apply(current, s => s with { Age = 1 }).ToHttpResult());
```

## What you get

Reject a bad request:

- `.ValidateWith<RouteHandlerBuilder, T>()` on one endpoint, for a `T` that declares its own constraints.
- `.ValidateWith(constraintSet)` on one endpoint, for a companion's set.
- `.ValidateConstraints()` on a `MapGroup` (or any endpoint builder): validates every handler argument whose type was registered with `AddConstraints`, and merges the errors of several arguments.

Turn exceptions and results into responses:

- `services.AddConstraintProblemDetails()` registers problem details and `ConstraintViolationExceptionHandler`; call `app.UseExceptionHandler()` too. A handler can call `ApplyOrThrow` or `Unwrap` with no try/catch.
- `exception.ToProblem()` returns the same 400 as an `IResult`, for code that catches the exception itself.
- `result.ToHttpResult()` on `Result<T>`: 200 with the value, or 400.
- `outcome.ToHttpResult()` on `ApplyOutcome<T>`: `Applied` and `Noop` answer 200, `Failure` answers 400. Warnings and hints of an applied change ride along in the 200 body as `{ "value": ..., "notices": [...] }`.

MVC controllers:

- `services.AddControllers().AddConstraintValidation()` validates registered action arguments into `ModelState`; an `[ApiController]` answers with its usual 400.
- `ModelState.AddViolations(violations, prefix)` is public for code that validates by hand. Keys are `prefix.Member`.

## Pitfalls and limits

- The filters validate the body only (`Validate`, point-in-time rules). For a change to an existing entity, call `Apply` or `ApplyOrThrow` in the handler so sequential rules see the before and after.
- `ValidateWith` takes the first handler argument of type `T`. `ValidateConstraints` looks up each argument by its exact runtime type, so a derived type needs its own registration.
- Only errors reject. Warnings and hints never produce a 400.
- `ConstraintFilter<T>` and the registry are synchronous and pure: a rule that needs a database call belongs in the handler.
- The key of an error is `Violation.Member`, or the rule's id when the rule is not about one member.
- Requires the `Microsoft.AspNetCore.App` framework reference, so it is not for class libraries without it.

## Next

- Integration recipes: https://entities.nullean.net/integration/
- Getting started with constraints: https://entities.nullean.net/getting-started/
- Registering sets and validating options: https://www.nuget.org/packages/Nullean.Entities.Extensions.DependencyInjection
- Command-line apps: https://www.nuget.org/packages/Nullean.Entities.Argh
- Runnable example: https://github.com/nullean/entities/tree/main/examples/Nullean.Entities.Integration.Examples/AspNet
