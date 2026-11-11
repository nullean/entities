---
name: nullean-entities-fluentvalidation
description: >-
  Use when a codebase has FluentValidation validators and Nullean.Entities constraints and they need to work together, in a validation pipeline or in property tests. Nullean.Entities.FluentValidation turns a ConstraintSet into an IValidator<T> and an existing validator into a named constraint.
---

# Nullean.Entities.FluentValidation

It bridges both ways: constraints run inside FluentValidation's pipeline as `ValidationFailure`s, and validators run as constraints that `Nullean.Entities.CsCheck` can attack with generated data. If there is no FluentValidation in the project, use the constraint sets directly (or `Nullean.Entities.AspNetCore` for HTTP).

## The calls you need

```csharp
using FluentValidation;
using Nullean.Entities;
using Nullean.Entities.FluentValidation;

services.AddSingleton<IValidator<Customer>>(CustomerRules.All.ToValidator());
```

```csharp
public sealed class SignUpValidator : AbstractValidator<Customer>
{
	public SignUpValidator()
	{
		RuleFor(c => c.Name).NotEmpty();
		Include(CustomerRules.All.ToValidator());
		RuleFor(c => c.Address).SatisfiesConstraints(AddressRules.All);   // failures keyed "Address.City"
		RuleForEach(c => c.Lines).SatisfiesConstraints(LineRules.All);    // "Lines[1].Quantity"
	}
}
```

```csharp
ConstraintSet<Customer> fromValidator = new CustomerValidator().ToConstraintSet("customer-validator");
Constraint<Customer> single = Constraints<Customer>.FromValidator(new CustomerValidator());
IReadOnlyList<Violation> violations = ConstraintSet.Merge(fromValidator, CustomerRules.All).Validate(customer);
```

With `Nullean.Entities.CsCheck`, `fromValidator.Probe(samples: 500)` shows whether generated data both passes and fails the validator.

## Gotchas

- `Severity` exists in both namespaces, so with both usings it is ambiguous. Alias one: `using FvSeverity = FluentValidation.Severity;`.
- `Constraints<T>.FromValidator` is a C# 14 static extension member; it only resolves with `using Nullean.Entities.FluentValidation;`.
- Only point-in-time rules run inside a validator (it calls `Validate`), so transition and sequential rules are skipped there.
- `SatisfiesConstraints` skips a null property. Add `NotNull()` when null is wrong.
- A validator becomes one constraint, named after the validator type unless you pass a name, and runs synchronously. `MustAsync` or `CustomAsync` rules throw `AsyncValidatorInvokedSynchronouslyException` when the constraint is checked, not when it is built.
- On the way in, `PropertyName` becomes `Member`, `AttemptedValue` becomes `Evidence`, and `ErrorCode` is dropped; `ConstraintId` is the constraint's name. On the way out, `ErrorCode` is the rule id, `CustomState` is the `Violation`, and Hint maps to Info.
- A violation with no `Member` at the root gets the rule id as `PropertyName`.
- Not trim or NativeAOT safe, because FluentValidation is not.

Docs: https://entities.nullean.net
