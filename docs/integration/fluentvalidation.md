---
navigation_title: FluentValidation
description: Run constraint sets inside a FluentValidation pipeline, and turn an existing FluentValidation validator into a constraint that generated data can attack.
---

# FluentValidation

```bash
dotnet add package Nullean.Entities.FluentValidation
```

For a team that already validates with [FluentValidation](https://fluentvalidation.net/). The package works in both directions: your constraints run inside FluentValidation, and your
FluentValidation validators run as constraints. Every snippet on this page is taken from
[`tests/Nullean.Entities.FluentValidation.Tests`](https://github.com/nullean/entities/tree/main/tests/Nullean.Entities.FluentValidation.Tests).

## Constraints inside FluentValidation

A constraint set becomes an `IValidator<T>`. Register it where the pipeline (ASP.NET Core, a MediatR behaviour, your own) resolves validators, and it is called through `ValidateAsync` like any
other:

```csharp
services.AddSingleton<IValidator<Customer>>(CustomerRules.All.ToValidator());
```

Add the constraints to a validator you already have, or check one property or each item of a collection against its own set:

```csharp
public sealed class SignUpValidator : AbstractValidator<Customer>
{
	public SignUpValidator()
	{
		RuleFor(c => c.Name).NotEmpty();
		Include(CustomerRules.All.ToValidator());
	}
}

validator.RuleFor(c => c.Address).SatisfiesConstraints(AddressRules.All);   // a failure is keyed "Address.City"
validator.RuleForEach(c => c.Lines).SatisfiesConstraints(LineRules.All);    // "Lines[1].Quantity"
```

`ConstraintValidator<T>` is the validator class itself: derive from it to add FluentValidation rules next to the constraints (`: base(CustomerRules.All)`).

Each violation becomes one `ValidationFailure`:

| `ValidationFailure` | From the `Violation` |
|---|---|
| `PropertyName` | `Member` under the rule's property path (`Address.City`, `Lines[1].Quantity`). Without a member: the path, or at the root the rule's id |
| `ErrorMessage` | `Message`, as written (FluentValidation's `{placeholders}` are not expanded) |
| `ErrorCode` | `ConstraintId`, the rule's name |
| `Severity` | `Error`, `Warning` and `Hint` become `Error`, `Warning` and `Info` |
| `AttemptedValue` | `Evidence` |
| `CustomState` | The `Violation` itself |

Only the point-in-time rules run, as in `Validate`: a validator sees one value, not a change, so a rule about a transition is not checked here. A null property is skipped, as FluentValidation
skips a child validator; add `NotNull()` when null is wrong.

Tested in `ConstraintValidatorTests.cs`.

## FluentValidation inside constraints

An existing validator becomes a named constraint, and from there everything that takes a constraint set takes it, including the property tests:

```csharp
var rules = ConstraintSet.From(("customer-validator", Constraints<Customer>.FromValidator(new CustomerValidator())));
// or: new CustomerValidator().ToConstraintSet()   (named after the validator type)

rules.Validate(customer with { Age = 7 });
// customer-validator  Member "Age"  'Age' must be between 18 and 120. You entered 7.
```

Each failure keeps its `PropertyName` as `Member` (`Address.City`, `Lines[1].Quantity` from `RuleForEach`), its `AttemptedValue` as `Evidence`, and `Info` maps back to `Hint`. Only errors
reject a change in `Apply`. Tested in `ValidatorConstraintsTests.cs`.

### Attack a validator with generated data

This is the reason to do it. With `Nullean.Entities.CsCheck` in the test project, generated values are thrown at the validator:

```csharp
var rules = new CustomerValidator().ToConstraintSet();

rules.Probe(samples: 500);
//   CustomerValidator    held 62  violated 438      (one run; both must be above zero)

GenAuto.Of<Customer>().ShouldSatisfy(rules);
// Set seed: "..." to reproduce (3 shrinks, ...)
// Customer { Name = "Anna Douglas", Email = "-", Age = 1, ... } violates: CustomerValidator: 'Email' is not a valid email address.; CustomerValidator: 'Age' must be between 18 and 120. You entered 1.
```

`Probe` tells you the validator both holds and fails on generated data (a validator that never fails is accepting everything); `ShouldSatisfy` against the output of your own factory or
mapper finds a value it builds that the validator rejects, shrunk and with a seed to replay. Tested in `ProbeTests.cs`.

## Pitfalls and limits

- **`Severity` is in both namespaces.** A file with `using FluentValidation;` and `using Nullean.Entities;` must say which one it means: `using FvSeverity = FluentValidation.Severity;`.
- **The validator is run synchronously.** Constraints are synchronous and pure, so `FromValidator` calls `Validate`; a validator with `MustAsync` or `CustomAsync` rules throws FluentValidation's
  `AsyncValidatorInvokedSynchronouslyException` when the constraint is checked. Keep database lookups in the application service.
- **One validator is one constraint.** `Probe` counts the whole validator, not each `RuleFor`; the violation's `Member` says which property failed.
- FluentValidation's `ErrorCode` is not kept on the way in: a violation's `ConstraintId` is the constraint's name.
- Not trim or NativeAOT clean: FluentValidation compiles expressions and is not annotated for trimming.
- Built and tested against FluentValidation 12.2.0 (Apache-2.0).
