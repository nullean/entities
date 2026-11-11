---
name: nullean-entities-blazor
description: >-
  Use when a Blazor EditForm should show the messages of a Nullean.Entities constraint set under its fields.
  It provides the `<ConstraintValidator>` component, which writes violations into the form's validation state.
---

# Nullean.Entities.Blazor

`ConstraintValidator<TModel>` validates an `EditForm` model against a `ConstraintSet<TModel>`, so `<ValidationMessage>` and `<ValidationSummary>` show the domain's own rules. It replaces `DataAnnotationsValidator` for those rules; for ReactiveUI or `INotifyDataErrorInfo` view models use `Nullean.Entities.ReactiveUI` or `Nullean.Entities.ComponentModel`.

## The calls you need

The form model is mutable (so `InputText` can bind) and declares its rules:

```csharp
public sealed partial class SignUp
{
	public string Email { get; set; } = "";
	public string Password { get; set; } = "";
	public string Confirm { get; set; } = "";

	[Constraint]
	public static readonly Constraint<SignUp> ConfirmMatches = Constraints<SignUp>.Check(
		s => s.Confirm == s.Password ? [] : [Violation.Error(nameof(Confirm), "Must match the password")]
	);
}
```

Put the validator inside the form (`@using Nullean.Entities` and `@using Nullean.Entities.Blazor` in `_Imports.razor`):

```razor
<EditForm Model="Model" OnValidSubmit="Save">
	<ConstraintValidator Constraints="SignUp.Constraints" />
	<InputText @bind-Value="Model.Confirm" />
	<ValidationMessage For="() => Model.Confirm" />
	<ValidationSummary />
</EditForm>
```

A companion's set works the same way (`Constraints="ContactRules.All"`). Show warnings too, with a test clock:

```razor
<ConstraintValidator Constraints="SignUp.Constraints" MinimumSeverity="Severity.Warning" Time="Clock" />
```

## Gotchas

- It must sit inside an `EditForm`; otherwise it throws `InvalidOperationException`. It also throws when the form model is not a `TModel`, or when the `EditContext` changes.
- Name the member: `Violation.Error(nameof(Confirm), "...")`. A violation with no member, or a dotted one like `Address.City`, shows only in `<ValidationSummary>`.
- A field change re-runs the set but only adds messages to the changed field; other fields only lose messages that no longer apply. Submit checks everything.
- Every shown message blocks `OnValidSubmit`, including a warning you opted into with `MinimumSeverity`.
- Point-in-time rules only. Call `Apply` in the submit handler for `Transitions` or `Monotonic` rules.
- Do not combine with `<DataAnnotationsValidator />` over a model whose `IValidatableObject` runs the same constraints, or every message shows twice.

Docs: https://entities.nullean.net
