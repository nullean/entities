# Nullean.Entities.ComponentModel

![Nullean.Entities](https://raw.githubusercontent.com/nullean/entities/main/docs/images/png/entities-social-card.png)

**The rules your domain already has show up next to the field in your MVVM form, through `INotifyDataErrorInfo`.**

[![NuGet](https://img.shields.io/nuget/v/Nullean.Entities.ComponentModel?style=flat-square)](https://www.nuget.org/packages/Nullean.Entities.ComponentModel)

Without this package, a desktop or mobile form repeats its validation in the view model, or in attributes, and drifts from the rules the domain enforces. This package runs
your `Nullean.Entities` constraint set from the view model and reports each violation under the property it is about, through `System.ComponentModel.INotifyDataErrorInfo`:
the interface Avalonia's and WPF's bindings read to draw an error next to a field.

It builds on [Nullean.Entities](https://www.nuget.org/packages/Nullean.Entities) and the BCL only: no UI-framework dependency, no reflection, trim and NativeAOT clean. It is
optional and does not need the other integration packages.

## Install

Targets `net10.0` and `net11.0`.

```bash
dotnet add package Nullean.Entities
dotnet add package Nullean.Entities.ComponentModel
```

## Quick start

The domain value is immutable and declares its rules:

```csharp
using Nullean.Entities;

public sealed partial record Signup(string Name, string Email, int Age)
{
	[Constraint]
	public static readonly Constraint<Signup> EmailHasAt = Constraints<Signup>.Check(
		s => s.Email.Contains('@') ? [] : [Violation.Error(nameof(Email), "Email needs an @")]
	);
}
```

The view model is mutable. `ConstraintViewModel<T>` builds the domain value from it (`ToValue`) and revalidates after every change:

```csharp
using Nullean.Entities.ComponentModel;

public sealed class SignupViewModel() : ConstraintViewModel<Signup>(Signup.Constraints)
{
	private string _name = "";
	private string _email = "";
	private int _age;

	public string Name { get => _name; set => SetProperty(ref _name, value); }
	public string Email { get => _email; set => SetProperty(ref _email, value); }
	public int Age { get => _age; set => SetProperty(ref _age, value); }

	protected override Signup ToValue() => new(Name, Email, Age);
}
```

Bind as usual; nothing else to configure:

```xml
<TextBox Text="{Binding Email}" />
```

Type `not-an-email` and Avalonia 12 shows "Email needs an @" under the box (its `INotifyDataErrorInfo` plugin is on by default). Fix it and the error goes. WPF does the same
through `Validation.Errors`.

Already on CommunityToolkit.Mvvm's `ObservableObject`, or a base of your own? Own a `ConstraintErrors<T>` and delegate `INotifyDataErrorInfo` to it; the docs page has the
complete view model.

## What you get

- `ConstraintErrors<T>(ConstraintSet<T> constraints, Func<T> current, object? sender = null, ConstraintErrorsOptions? options = null)`: an `INotifyDataErrorInfo` to own and
  delegate to. `Validate()` runs the set over `current()`; `Validate(T previous)` also runs the rules about a change (`ConstraintSet<T>.Check`), for an edit of an existing
  entity; `Clear()` forgets everything. `ErrorsChanged` is raised only for the properties whose messages changed, with `sender` as its sender.
- `GetErrors(property)` returns the messages as strings; `GetErrors(null)` or `GetErrors("")` the entity-level ones (violations with no `Member`). `GetViolations(property)` and
  `Violations` return the `Violation`s themselves.
- `ConstraintErrorsOptions`: `MinimumSeverity` (errors only by default; `Warning` adds warnings), `Members` (domain member name to view-model property name, when they differ),
  `Time` (the `TimeProvider` for time-aware rules).
- `ConstraintViewModel<T>`: an optional base with `INotifyPropertyChanged`, `SetProperty`, `Validate()`, and `RunConstraints()` to override (call `Errors.Validate(original)` when
  editing). `HasErrors` raises `PropertyChanged` when it flips.

## Pitfalls and limits

- Nothing is reported until the first change or `Validate()`, so an empty form is not red on open. After that every property is revalidated on every change: no "touched" tracking.
- Validation is synchronous, on the UI thread, on every change. Keep the projection and the rules cheap; checks that need a database do not belong in constraints.
- The projection must not throw: a half-typed form must still build a value.
- A violation on a nested member (`Address.City`) is reported under that name; map it to the bound property name with `Members`.
- `INotifyDataErrorInfo` has no severity: a warning you opt into shows like an error.
- Tested against Avalonia 12.1.5 (headless, a real `TextBox` binding) and CommunityToolkit.Mvvm 8.4.2. WPF is not tested here. WinUI 3 and MAUI's stock controls do not draw
  `INotifyDataErrorInfo` errors themselves; call `GetErrors` from your own view.

## Learn more

- This integration on the docs site: https://entities.nullean.net/integration/mvvm/, and every integration: https://entities.nullean.net/integration/
- Getting started with constraints: https://entities.nullean.net/getting-started/
- Web APIs: https://www.nuget.org/packages/Nullean.Entities.AspNetCore
- Tests for the package: https://github.com/nullean/entities/tree/main/tests/Nullean.Entities.ComponentModel.Tests
- Source and issues: https://github.com/nullean/entities
