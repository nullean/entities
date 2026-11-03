---
navigation_title: Blazor forms
description: Show constraint violations in a Blazor EditForm, under each field and in the summary, with one component.
---

# Blazor forms

```bash
dotnet add package Nullean.Entities.Blazor
```

Put `<ConstraintValidator>` inside an `EditForm` and give it the model's constraint set. Violations then show where Blazor shows any validation message: under the field in
`<ValidationMessage For="...">` and in `<ValidationSummary>`, and `OnValidSubmit` only runs when there are none.

```html
<EditForm Model="Model" OnValidSubmit="Save">
	<ConstraintValidator Constraints="SignUp.Constraints" />

	<InputText @bind-Value="Model.Email" />
	<ValidationMessage For="() => Model.Email" />

	<InputText type="password" @bind-Value="Model.Password" />
	<InputText type="password" @bind-Value="Model.Confirm" />
	<ValidationMessage For="() => Model.Confirm" />

	<ValidationSummary />
	<button type="submit">Sign up</button>
</EditForm>
```

`SignUp` is an ordinary mutable form model with `[Constraint]` fields, so `SignUp.Constraints` is generated. For a model whose rules live in a companion, pass the companion's set:
`<ConstraintValidator Constraints="ContactRules.All" />`. The model type is inferred from the set.

## What it does

- **On submit** the whole model is checked, and every violation is shown.
- **When a field changes** the set runs again (a constraint checks the whole value, not one property) and that field gets its current messages. On the other fields a message that no longer
  holds is removed, but nothing new is added to a field the user has not reached yet. So fixing `Password` clears "must match the password" on `Confirm`, and typing an email does not flag an empty name.
- **Where a message goes**: a violation is shown on the field named by `Violation.Member` (`new FieldIdentifier(model, member)`). A violation without a member is about the whole model, and only `<ValidationSummary>` shows it.
- **Errors only**, by default. `MinimumSeverity="Severity.Warning"` also shows warnings, and `Severity.Hint` shows everything. Blazor counts every message as invalid, so a shown warning also blocks `OnValidSubmit`.
- `Time="..."` passes a `TimeProvider` to rules that read the clock (`NotInFuture`, `Always((x, now) => ...)`).

## This component or `DataAnnotationsValidator`

Blazor's own `<DataAnnotationsValidator />` runs a model's `IValidatableObject.Validate`. When a constraint type implements `IValidatableObject` (see [DataAnnotations](data-annotations.md)),
that validator already reports your constraints, and you need no extra package. Use `ConstraintValidator` instead when:

- the rules live in a **companion** (`ContactRules.All`) or a set you build at runtime, not on the model type;
- you do not want the model to implement `IValidatableObject`;
- you want **per-field updates**: `DataAnnotationsValidator` re-runs only property attributes when a field changes, so model-level rules (`IValidatableObject`) appear and clear only on submit,
  while this component re-checks the set on every change and updates the changed field;
- you want warnings or hints shown, or a `TimeProvider`.

Both can sit in one form: `DataAnnotationsValidator` for attributes the generator does not turn into constraints, `ConstraintValidator` for the set. Do not use both for the same rules, or each message is shown twice.

## Limits

- A member is matched against the form model's own properties. A violation with a dotted member (`Address.City`) or one about a collection (`Items`) still appears in `<ValidationSummary>`,
  but `<ValidationMessage For="() => Model.Address.City">` will not show it, because Blazor keys that field by the `Address` object.
- Only point-in-time rules run (`ConstraintSet<T>.Validate`); sequential rules (`Transitions`, `Monotonic`) need a previous value, which a form does not have.
- Constraints are synchronous. For a check that needs a server (is this email taken), validate in the submit handler and add the message yourself.

The package depends on `Nullean.Entities` and `Microsoft.AspNetCore.Components.Forms` (10.0 or later), and works with any render mode, including WebAssembly. It uses no reflection and builds with
the trim and AOT analyzers on; it is not yet published in this repository's NativeAOT smoke test or a trimmed WebAssembly app.

Every snippet on this page is backed by `tests/Nullean.Entities.Blazor.Tests` (`SignUpForm.razor`, `ContactForm.razor`, `ConstraintValidatorTests.cs`), which renders the forms with bUnit on net10.0 and net11.0.
