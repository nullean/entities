---
name: nullean-entities
description: >-
  Use when declaring the business rules of a C# domain type, validating values, or applying a change that must be refused when it breaks a rule. Gives named Constraint<T> rules, a generated ConstraintSet<T>, and Result<T> from checked mutations.
---

# Nullean.Entities

Declare each rule once as a static `Constraint<T>`; the bundled `[Constraints]`/`[Constraint]` generator collects them into a `ConstraintSet<T>` that validates values and guards mutations. Use Nullean.Entities.Reactive for event streams and Nullean.Entities.CsCheck for generated tests.

## The calls you need

Companion class: every static `Constraint<Order>` lands in the generated `All`.

```csharp
[Constraints<Order>]
public static partial class OrderConstraints
{
	public static readonly Constraint<Order> MustHaveLines =
		Constraints<Order>.Always(o => o.Lines.Count > 0, "An order must have at least one line.");

	public static readonly Constraint<Order> LegalStatus = Constraints<Order>
		.Transitions(o => o.Status)
		.Allow(OrderStatus.Pending, OrderStatus.Confirmed)
		.Allow(OrderStatus.Confirmed, OrderStatus.Shipped);
}
```

On the type itself, tag members `[Constraint]`; it gets `SignUp.Constraints` and `IHaveConstraints<SignUp>`.

```csharp
public sealed partial record SignUp(string Email, int Age)
{
	[Constraint]
	public static readonly Constraint<SignUp> HasEmail = Constraints<SignUp>.Check(
		s => s.Email.Contains('@') ? [] : [Violation.Error(nameof(Email), "must be an email address")]);
}
```

The id is the member name in kebab-case (`has-email`).

```csharp
foreach (var v in SignUp.Constraints.Validate(signUp))
	Console.WriteLine($"{v.ConstraintId} [{v.Member}]: {v.Message}");
```

`Apply` never modifies the original.

```csharp
Result<Order> result = OrderConstraints.All.Apply(order, o => o with { Status = OrderStatus.Shipped });
string text = result.Match(ok => "ok", violations => string.Join(" | ", violations.Select(v => v.Message)));
Order shipped = OrderConstraints.All.ApplyOrThrow(order, o => o with { Status = OrderStatus.Shipped });
```

## Gotchas

- `Validate` runs point-in-time rules only. `Transitions`, `Monotonic` and `Conserves` need a previous value, so only `Apply`, `ApplyDetailed`, `ApplyOrThrow` or `Check(before, after)`.
- A mutation that returns an equal value is a no-op: no rule runs and `Apply` is `Ok`. `ApplyDetailed` tells `Noop` from `Applied`.
- Only `Severity.Error` violations fail `Apply`; warnings and hints come back as `Applied.Notices`.
- `ApplyOrThrow`, `Ensure` and `Result<T>.Unwrap()` throw `ConstraintViolationException` (`.Violations`); `Apply` and `Validated` return `Fail`.
- The generated property is `Constraints` on the type itself and `All` on a companion class.
- The type and its containing types must be `partial`, members `static` and of one `T`, or it is a build error (NECG001-007) and no set is generated at all. NECG010 (Info) means a DataAnnotation such as `[Phone]` was not turned into a rule.

Docs: https://entities.nullean.net
