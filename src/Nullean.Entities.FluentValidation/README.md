# Nullean.Entities.FluentValidation

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

**Keep your FluentValidation pipeline: your constraints run inside it as validators, and your validators run as constraints that generated data can attack.**

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.FluentValidation?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.FluentValidation)

Without this package, a team with FluentValidation validators and `Nullean.Entities` constraints keeps two sets of rules apart: the HTTP or mediator pipeline only sees the validators, and the
property tests only see the constraints. This package bridges [FluentValidation](https://fluentvalidation.net/) both ways.

It builds on [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities) and FluentValidation (Apache-2.0); it does not need the other integration packages. It uses no reflection
of its own, but it is not trim or NativeAOT clean, because FluentValidation is not. Built and tested against FluentValidation 12.2.0.

## Install

Targets `net10.0` and `net11.0`.

```bash
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.FluentValidation
```

## Quick start

Constraints inside FluentValidation: register the set as a validator, or add it to one you have.

```csharp
using FluentValidation;
using Nullean.Entities;
using Nullean.Entities.FluentValidation;

services.AddSingleton<IValidator<Customer>>(CustomerRules.All.ToValidator());

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

FluentValidation inside constraints: turn an existing validator into a constraint, and attack it in a test (with `Nullean.Entities.CsCheck`).

```csharp
var rules = new CustomerValidator().ToConstraintSet();          // or Constraints<Customer>.FromValidator(validator, "customer-validator")

rules.Probe(samples: 500);                                      // held N, violated M: the validator both passes and fails on generated data
GenAuto.Of<Customer>().ShouldSatisfy(rules);                    // throws with the smallest generated value it rejects and a seed to replay
```

## What you get

- `ConstraintSet<T>.ToValidator()` and `ConstraintValidator<T>`: an `AbstractValidator<T>` over a set, for DI, `Include`, `SetValidator`, or as a base class.
- `RuleFor(...).SatisfiesConstraints(set)` and `RuleForEach(...).SatisfiesConstraints(set)`: a property or each item checked against its own set.
- Each violation as a `ValidationFailure`: `PropertyName` is `Violation.Member` under the rule's property path (no member: the path, or the rule id at the root), `ErrorCode` the rule id,
  `Severity` Error/Warning/Info from Error/Warning/Hint, `AttemptedValue` the `Evidence`, `CustomState` the `Violation`. `ConstraintValidation.ToFailure` is the mapping on its own.
- `Constraints<T>.FromValidator(validator, name)`, `validator.ToConstraint(name)` and `validator.ToConstraintSet(name)`: a validator as a point-in-time constraint, each failure a `Violation`
  with `Member` from `PropertyName` and `Evidence` from `AttemptedValue` (`ValidatorConstraints.ToViolation`).

## Pitfalls and limits

- `Severity` exists in both `FluentValidation` and `Nullean.Entities`: alias one (`using FvSeverity = FluentValidation.Severity;`).
- Only point-in-time rules run inside FluentValidation; a validator sees one value, not a change. A null property is skipped; add `NotNull()` when null is wrong.
- `FromValidator` runs the validator synchronously, as every constraint is checked: a validator with `MustAsync`/`CustomAsync` rules throws `AsyncValidatorInvokedSynchronouslyException`.
- One validator is one constraint: `Probe` counts the validator, not each `RuleFor`. FluentValidation's `ErrorCode` is not kept on the way in.
- Not trim or NativeAOT clean (FluentValidation compiles expressions). Other FluentValidation versions than 12.2.0 are untested.

## Learn more

- This integration on the docs site: https://entities.nullean.net/integration/fluentvalidation/, and every integration: https://entities.nullean.net/integration/
- Getting started with constraints: https://entities.nullean.net/getting-started/
- FluentValidation: https://fluentvalidation.net/
- Property tests over constraints: https://www.nuget.org/packages/Nullean.Entities.CsCheck
- Tests for the package: https://github.com/nullean/entities/tree/main/tests/Nullean.Entities.FluentValidation.Tests
- Source and issues: https://github.com/nullean/entities
