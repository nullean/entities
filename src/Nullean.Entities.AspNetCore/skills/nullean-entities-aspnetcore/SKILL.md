---
name: nullean-entities-aspnetcore
description: >-
  Use when an ASP.NET Core minimal API or MVC controller should reject a request body, or a failed change, that breaks Nullean.Entities constraints.
  It adds endpoint filters, an exception handler and result mappers that answer with a 400 ValidationProblem keyed by member.
---

# Nullean.Entities.AspNetCore

Turns constraint violations into the standard ASP.NET Core 400 `ValidationProblem` (`errors: { "Email": ["..."] }`), from a filter, a thrown `ConstraintViolationException`, or a `Result<T>` you return. For options validation or validating an `object` outside HTTP, use `Nullean.Entities.Extensions.DependencyInjection` directly.

## The calls you need

```csharp
builder.Services.AddConstraints<SignUp>().AddConstraintProblemDetails();
var app = builder.Build();
app.UseExceptionHandler();   // needed for AddConstraintProblemDetails to answer
```

Reject a bad body before the handler runs:

```csharp
app.MapPost("/signup", (SignUp s) => TypedResults.Ok(s.Email)).ValidateWith<RouteHandlerBuilder, SignUp>();
app.MapPost("/contact", (Contact c) => TypedResults.Ok()).ValidateWith(ContactRules.All);
app.MapGroup("/api").ValidateConstraints();   // every argument whose type was registered with AddConstraints
```

Apply a change to an existing entity, so transition rules see before and after:

```csharp
app.MapPost("/signup/age", (SignUp current) => SignUp.Constraints.ApplyOrThrow(current, s => s with { Age = 21 }));
app.MapPost("/signup/age2", (SignUp current) => SignUp.Constraints.Apply(current, s => s with { Age = 21 }).ToHttpResult());
```

MVC: `builder.Services.AddControllers().AddConstraintValidation();` writes registered action arguments into `ModelState`.

## Gotchas

- `ValidateWith<TBuilder, T>()` needs both type arguments spelled out (`RouteHandlerBuilder` for a `MapPost`); C# cannot infer `T`.
- The filters run point-in-time rules only. `Transitions` and `Monotonic` rules need `Apply`/`ApplyOrThrow` in the handler.
- `ValidateConstraints()` and `AddConstraintValidation()` find rules through `IConstraintRegistry`, by exact runtime type. Unregistered types pass, and a derived type needs its own `AddConstraints`.
- Without `app.UseExceptionHandler()` a thrown `ConstraintViolationException` is a 500, not a 400. Catching it yourself? Return `ex.ToProblem()`.
- Only errors produce a 400. On `ApplyOutcome<T>.ToHttpResult()` (from `ApplyDetailed`), warnings of an applied change come back in a 200 body as `{ value, notices }`.
- The MVC filter short-circuits only for `[ApiController]` controllers; others must check `ModelState.IsValid`.

Docs: https://entities.nullean.net
