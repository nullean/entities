# Nullean.Entities.Blazor

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

**A Blazor form shows your domain rules under the right field, as the user types, with one line in the `EditForm`.**

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.Blazor?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.Blazor)

Without this package, a Blazor form either repeats the domain's rules as DataAnnotations attributes or checks the model by hand in the submit handler and has no way to put the message under a field.
This package adds `<ConstraintValidator>`, which checks an `EditForm`'s model against a `Nullean.Entities` constraint set and writes the violations into the form's validation state, so
`<ValidationMessage For="...">` and `<ValidationSummary>` show them and `OnValidSubmit` waits until there are none.

It builds on [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities) and `Microsoft.AspNetCore.Components.Forms`; it does not need the other integration packages. It is optional,
works in every render mode including WebAssembly, uses no reflection, and builds with the trim and AOT analyzers on.

## Install

Targets `net10.0` and `net11.0`.

```bash
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.Blazor
```

## Quick start

A form model with its rules on it:

```csharp
using Nullean.Entities;

public sealed partial class SignUp
{
	public string Email { get; set; } = "";
	public string Password { get; set; } = "";
	public string Confirm { get; set; } = "";

	[Constraint]
	public static readonly Constraint<SignUp> EmailLooksValid = Constraints<SignUp>.Check(
		s => s.Email.Contains('@') ? [] : [Violation.Error(nameof(Email), "Enter an email address")]
	);

	[Constraint]
	public static readonly Constraint<SignUp> ConfirmMatches = Constraints<SignUp>.Check(
		s => s.Confirm == s.Password ? [] : [Violation.Error(nameof(Confirm), "Must match the password")]
	);
}
```

And the form (add `@using Nullean.Entities.Blazor` here or in `_Imports.razor`):

```razor
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

Type `not-an-email` and leave the field: "Enter an email address" appears under it, and nothing appears under the fields not reached yet. Submit with a mismatched confirmation:
"Must match the password" appears under Confirm. Change Password to match, and that message goes away. `Save` runs once the model breaks no rule.

The value: the rule is written once, on the type, and the same set guards your API, your service layer and your generated tests. Change `ConfirmMatches` and the form follows.

## What you get

- `ConstraintValidator<TModel>`, used as `<ConstraintValidator Constraints="..." />` (Razor infers `TModel` from the set):
  - `Constraints` (required): `T.Constraints` for a type that declares its own rules, or a companion's set such as `ContactRules.All`.
  - `MinimumSeverity`: `Severity.Error` (default) shows errors only; `Severity.Warning` adds warnings; `Severity.Hint` shows everything.
  - `Time`: a `TimeProvider` for rules that read the clock.
- On submit, the whole model is checked. When a field changes, the set runs again, that field gets its current messages, and messages on other fields that no longer hold are removed.
- A violation is shown on the field named by `Violation.Member`. One without a member is a model-level message, shown by `<ValidationSummary>`.

## Pitfalls and limits

- Give each violation a member (`Violation.Error(nameof(Email), "...")`), or it only shows in the summary.
- Members match the form model's own properties. A dotted member (`Address.City`) shows in the summary, not in `<ValidationMessage For="() => Model.Address.City">`.
- Blazor counts every shown message as invalid: a warning you choose to show also blocks `OnValidSubmit`.
- Only point-in-time rules run. Rules over a change (`Transitions`, `Monotonic`) need a previous value; call `Apply` in the submit handler for those.
- Constraints are synchronous and pure. Checks that need a server belong in the submit handler.
- If the model already implements `IValidatableObject` over its constraints, Blazor's own `<DataAnnotationsValidator />` reports them too; do not use both for the same rules, or each message shows twice.

## Learn more

- This integration on the docs site, including when to use `DataAnnotationsValidator` instead: https://entities.nullean.net/integration/blazor/, and every integration: https://entities.nullean.net/integration/
- Getting started with constraints: https://entities.nullean.net/getting-started/
- Web APIs: https://www.nuget.org/packages/Nullean.Entities.AspNetCore
- Tests for the package: https://github.com/nullean/entities/tree/main/tests/Nullean.Entities.Blazor.Tests
- Source and issues: https://github.com/nullean/entities
