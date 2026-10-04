# Nullean.Entities.AspNetCore

Answer constraint violations with a standard 400 `ValidationProblem`, keyed by member.

```csharp
services.AddConstraints<SignUp>().AddConstraintProblemDetails();

app.MapPost("/signup", (SignUp body) => ...).ValidateWith<RouteHandlerBuilder, SignUp>();   // one endpoint
app.MapGroup("/api").ValidateConstraints();                                                  // every registered argument type
SignUp.Constraints.Apply(current, change).ToHttpResult();                                    // Result<T> / ApplyOutcome<T> to 200 / 400
```

`ConstraintViolationExceptionHandler` turns a thrown `ConstraintViolationException` into the same 400, and `AddConstraintValidation()` on `IMvcBuilder` feeds
violations into `ModelState` for controllers. See `docs/integration.md` for the whole picture.
