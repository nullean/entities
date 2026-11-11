---
name: nullean-entities-componentmodel
description: >-
  Use when a WPF, Avalonia, WinUI or MAUI view model should report Nullean.Entities constraint violations to its bindings.
  It implements INotifyDataErrorInfo over a constraint set, either as a helper to delegate to or as a small view-model base class.
---

# Nullean.Entities.ComponentModel

Bridges a mutable view model and an immutable domain value: a projection builds the value from the current properties, the set runs, and the violations are reported per property through `INotifyDataErrorInfo`. BCL only and NativeAOT clean; for ReactiveUI.Validation use `Nullean.Entities.ReactiveUI`, for Blazor forms `Nullean.Entities.Blazor`.

## The calls you need

Without an MVVM toolkit, derive from `ConstraintViewModel<T>`; every `SetProperty` that changes a value revalidates:

```csharp
public sealed class SignupViewModel() : ConstraintViewModel<Signup>(Signup.Constraints)
{
	private string _email = "";
	private int _age;

	public string Email { get => _email; set => SetProperty(ref _email, value); }
	public int Age { get => _age; set => SetProperty(ref _age, value); }

	protected override Signup ToValue() => new(Email, Age);
}
```

With a toolkit base class, own a `ConstraintErrors<T>`, delegate `HasErrors`, `GetErrors` and `ErrorsChanged` to it, and call `Validate()` after each change:

```csharp
var errors = new ConstraintErrors<Signup>(
	Signup.Constraints,
	() => new Signup(EmailText, Age),
	this,   // the sender of ErrorsChanged
	new ConstraintErrorsOptions { Members = new Dictionary<string, string> { [nameof(Signup.Email)] = nameof(EmailText) } }
);
errors.Validate();
var messages = errors.GetErrors(nameof(EmailText));   // strings; GetErrors(null) for entity-level ones
```

Editing an existing entity, so the rules about a change run too:

```csharp
protected override IReadOnlyList<Violation> RunConstraints() => Errors.Validate(_original);
```

## Gotchas

- Nothing is validated on construction; an empty form shows no errors until the first change or an explicit `Validate()`.
- The projection runs on every change, on the UI thread. It must not throw on half-typed input and must stay cheap.
- `Members` maps domain member names to view-model property names. Without it a violation on `Email` is reported under `Email`, which shows on nothing if the bound property is `EmailText`.
- Errors only by default. `MinimumSeverity = Severity.Warning` includes warnings, which then show exactly like errors (`INotifyDataErrorInfo` has no severity).
- `ErrorsChanged` fires only for properties whose messages actually changed. `ConstraintViewModel<T>` also raises `PropertyChanged(nameof(HasErrors))` when it flips; with `ConstraintErrors<T>` that is yours to do.
- WinUI 3 and MAUI stock controls do not draw these errors; read `GetErrors` in your own view.

Docs: https://entities.nullean.net
