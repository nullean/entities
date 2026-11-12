---
navigation_title: GraphQL
description: Validate HotChocolate input objects against their constraints and report each violation as a GraphQL error with member and code.
---

# GraphQL input objects (HotChocolate)

No package: this is a recipe. It is compiled in
[`examples/Nullean.Entities.Recipes/GraphQL`](https://github.com/nullean/entities/tree/main/examples/Nullean.Entities.Recipes/GraphQL) and tested in
`tests/Nullean.Entities.Recipes.Tests/GraphQLMiddlewareTests.cs` through HotChocolate's request executor.

Bind the input object to a record that declares its constraints, and register it:

```csharp
public sealed partial record AddBookInput(string Title, int Pages)
{
    [Constraint]
    public static readonly Constraint<AddBookInput> HasPages = Constraints<AddBookInput>.Check(
        b => b.Pages > 0 ? [] : [Violation.Error(nameof(Pages), "must be at least 1")]
    );
}
```

A field middleware validates every argument whose type is registered. Each violation becomes one GraphQL error, with the member and the constraint id as extensions, and
the resolver does not run:

```csharp
public static FieldMiddleware Create(IConstraintRegistry registry) => next => async context =>
{
    var failed = false;
    foreach (var argument in context.Selection.Field.Arguments)
    {
        if (context.ArgumentValue<object?>(argument.Name) is not { } value || !registry.TryValidate(value, out var violations))
        {
            continue;
        }
        foreach (var v in violations.Where(v => v.Severity == Severity.Error))
        {
            failed = true;
            context.ReportError(
                ErrorBuilder.New()
                    .SetMessage(v.Message)
                    .SetPath(context.Path)
                    .SetExtension("member", v.Member)
                    .SetExtension("code", v.ConstraintId)
                    .Build()
            );
        }
    }
    if (!failed)
    {
        await next(context).ConfigureAwait(false);
    }
};

services
    .AddConstraints<AddBookInput>()
    .AddGraphQL()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .UseField(next => context => ConstraintFieldMiddleware.Create(context.Services.GetRequiredService<IConstraintRegistry>())(next)(context));
```

`mutation { addBook(input: { title: "Dune", pages: 0 }) { title } }` then answers:

```json
{
  "errors": [
    { "message": "must be at least 1", "path": ["addBook"], "extensions": { "member": "Pages", "code": "has-pages" } }
  ],
  "data": null
}
```

`member` is the C# property name (`Pages`); GraphQL clients see the camel-cased field (`pages`), so map it if they key on that. The middleware is global (`UseField`), so it
runs for every field; fields without arguments cost one empty loop. A field with a nullable result keeps the rest of `data`; here `addBook` is non-null, so `data` is null.

Why a field middleware and not an error filter: an error filter maps one error to one error, so it cannot turn one `ConstraintViolationException` into an error per
violation.

Tested with HotChocolate.AspNetCore 16.7.5.
