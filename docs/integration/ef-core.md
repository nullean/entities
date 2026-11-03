---
navigation_title: EF Core
description: Check constraints in a SaveChangesInterceptor, with EF's original values as the before.
---

# EF Core

No package: this is a recipe. This repository does not reference EF Core, so the snippet is **not compiled here**: treat it as the shape, and check the signatures against the
version you use.

EF entities are mutated in place, so `Apply` cannot see a "before". Use `Check` with EF's original values from a `SaveChangesInterceptor`:

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

With [ASP.NET Core](aspnetcore.md)'s `AddConstraintProblemDetails()`, the `ConstraintViolationException` thrown here becomes a 400. To audit rows already in the database, read them and
call `Validate`: [`examples/Apps`](https://github.com/nullean/entities/tree/main/examples/Apps) has an audit of existing rows.
