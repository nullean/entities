---
navigation_title: DataAnnotations
description: The attributes already on your model become named constraints, and generated test data attacks the boundary of each one.
---

# DataAnnotations

No extra package. DataAnnotations are read in two places:

- **The source generator** in `Nullean.Entities` turns them into constraints, so an annotated model is already a set you can `Validate`, `Apply` and test.
- **`GenAuto`** in `Nullean.Entities.CsCheck` reads the same attributes, so generated data lands on each rule's boundary: the smallest and largest valid value, and the one just outside.

Together, an annotated type gets its rules and the test data that probes them with no hand-written generator.

## An annotated record is a constraint set

A type with `[Constraints]` (or `[Constraint]` members) gets one generated constraint for each `[Required]`, `[Range]`, `[StringLength]`, `[MaxLength]`, `[MinLength]` and
`[RegularExpression]` on its properties or primary-constructor parameters, with no opt-in and no declared constraint needed:

```csharp
using System.ComponentModel.DataAnnotations;
using Nullean.Entities;

[Constraints]
public sealed partial record SignUp([Required] string Name, [Range(18, 120)] int Age, [RegularExpression("^[A-Z]{3}-[0-9]{3}$")] string Reference);

foreach (var v in SignUp.Constraints.Validate(new SignUp("Ada", 12, "ABC-123")))
	Console.WriteLine($"{v.ConstraintId} [{v.Member}]: {v.Message}");
```

```text
age-range [Age]: The field Age must be between 18 and 120.
```

Rules are named the kebab-case of `<Property><Attribute>`: `name-required`, `age-range`, `reference-pattern`. If that collides with a constraint you declared, yours keeps the name and the
annotation's gets an `Annotation` suffix. The generated code is plain C#, with no `Validator` call and no reflection, so it is trim and AOT safe. Hand-written `Constraint<T>` fields on
the same type join the same set.

A companion class picks the annotations up too, even with no rule of its own: `[Constraints<Order>] public static partial class OrderRules;` generates `OrderRules.All` from the
annotations on `Order`, plus any `Constraint<Order>` field you add.

## What each attribute means

Each attribute means what DataAnnotations says it means:

- `null` passes every attribute except `[Required]`; null is `[Required]`'s business alone.
- An explicit `[Required]` also rejects an empty or blank string.
- An empty string passes `[RegularExpression]`, and a pattern must match the **whole** string.
- `[ConstraintIgnore]` on a property leaves its annotations out.
- Any other validation attribute (`[Phone]`, a custom `ValidationAttribute`, `IValidatableObject`) is reported as the Info diagnostic `NECG010`, not skipped silently. Write those as a `Constraint<T>`.

### Members that must be set

A non-nullable reference property that has to be set also gets a null-only rule `<property>-required`, when it is declared `required` or is a get-only auto property (only a constructor can
set it). A value type, a `Nullable<T>` and a `T?` reference get none. An explicit `[Required]` replaces the implicit rule.

These rules are **prerequisites**: when one is violated, the other rules are skipped, because they were written for a value that has the member and would throw reading it. The members that
must be set on a nested object, on the items of a list or on the values of a dictionary get a rule too, named after the path (`owner-address-city-required`), up to three levels deep.

Positional record parameters are init properties, not get-only, so they get no implicit rule: annotate them with `[Required]`. Turn the implicit rules off with
`[Constraints(ImplicitRequired = false)]` (or on `[Constraints<T>]`).

## Generated data respects them, and attacks them

`GenAuto.Of<T>()` reads the same six attributes on the members it builds:

| Mode | What an annotated member gets |
|---|---|
| `Plausible` | Values that satisfy the attribute: a value the attribute forbids is coerced into range. |
| `Mixed` (default) | Plausible values, with the boundary of each rule among the edge values. |
| `Adversarial` | The boundary values at a high rate. |

The boundary is the smallest and largest valid value and the one just outside: `[Range(18, 120)]` gives 17, 18, 120 and 121; `[StringLength(10, MinimumLength = 3)]` gives strings of
length 2, 3, 10 and 11. That is where an off-by-one lives, in the rule or in the code beside it.

On a record, an attribute counts whether it is written on the parameter (`[Range(18, 120)] int Age`) or on the property (`[property: Range(18, 120)] int Age`).

## Together: the annotations are tested with no generator

Because the rules and the data come from the same attributes, the test needs nothing else:

```csharp
SignUp.Constraints.Probe().NeverViolated.Should().BeEmpty();   // generated data breaks every annotation
```

`Probe` reports a rule the generated data never broke; for annotations that list is empty, because the boundary values break each one. [`AutoTest.For<SignUp>()`](../getting-started/index.md)
runs the nine axes over the same rules and data. The examples on this page are tested in `tests/Nullean.Entities.CsCheck.Tests/DataAnnotationsPageTests.cs`.

## Where DataAnnotations stop

Annotations cover one property at one moment. A cross-field rule (`End` after `Start`), a rule over a collection (unique ids), a rule about a change (a version never goes back) or about
time (a deadline) is a `Constraint<T>` or a temporal rule, declared next to the annotations in the same set. See [Why not FluentValidation and DataAnnotations](../why-not/index.md).
