# Nullean.Entities.Extensions.DependencyInjection

Register constraint sets once and let framework integrations find them, with no reflection (AOT-safe).

```csharp
services.AddConstraints<SignUp>();                       // T : IHaveConstraints<T>, uses SignUp.Constraints
services.AddConstraints(OrderRules.All);                 // a companion's set
services.AddOptions<ServerOptions>().ValidateConstraints(ServerRules).ValidateOnStart();
```

`IConstraintRegistry.TryValidate(object, out violations)` validates any registered instance by its exact type, which is what `Nullean.Entities.AspNetCore` and
`Nullean.Entities.Argh` use to validate arguments they only hold as `object`. `ViolationErrors.ToDictionary` groups errors by member for ProblemDetails.
