---
navigation_title: ReactiveUI
description: Show a constraint set's violations on a ReactiveUI view model through ReactiveUI.Validation, so BindValidation puts each message next to its field.
---

# ReactiveUI view models (ReactiveUI.Validation)

```bash
dotnet add package Nullean.Entities.ReactiveUI
```

A form's view model holds what the user is typing; the rules belong to the domain type that view model turns into. This package checks the domain type's
constraint set every time a property of the view model changes and reports the violations on the view model's ReactiveUI.Validation
`ValidationContext`, so the view binds them with the `BindValidation` calls it already uses. The rules are written once, on the domain type.

:::{note}
This is not `Nullean.Entities.Reactive`. That package checks rules over System.Reactive event streams (`IObservable<TEvent>`, temporal rules); see
[System.Reactive](reactive.md). This one is for ReactiveUI view models and forms.
:::

## The domain type and the view model

The rules are on the domain type. A violation names its member with `Violation.Error(nameof(Email), ...)`; a rule about the whole value has no member:

```csharp
public sealed partial record SignUp(string Email, int Age, string Password, string Confirm)
{
	[Constraint]
	public static readonly Constraint<SignUp> EmailHasAt = Constraints<SignUp>.Check(
		s => s.Email.Contains('@') ? [] : [Violation.Error(nameof(Email), "must contain @")]
	);

	[Constraint]
	public static readonly Constraint<SignUp> PasswordsMatch = Constraints<SignUp>.Check(
		s => s.Password == s.Confirm ? [] : [Violation.Error("passwords must match")]
	);
}
```

The view model is an ordinary `ReactiveObject` that implements `IValidatableViewModel`, plus one method that builds the domain value from what is on screen:

```csharp
public sealed class SignUpViewModel : ReactiveObject, IValidatableViewModel
{
	private string _email = "";
	public string Email { get => _email; set => this.RaiseAndSetIfChanged(ref _email, value); }
	// Age, Password, Confirm the same way

	public IValidationContext ValidationContext { get; } = new ValidationContext();

	public SignUp ToSignUp() => new(Email, Age, Password, Confirm);
}
```

## Attach the rules

```csharp
using var rules = vm.ValidateConstraints(SignUp.Constraints, v => v.ToSignUp());   // or a companion's set, e.g. SignUpRules.All

vm.Email = "not-an-email";
// vm.ValidationContext.IsValid          == false
// vm.ValidationContext.Text             == "must contain @"
// ValidationContext.ObserveFor(v => v.Email) has the state "must contain @"
```

`ValidateConstraints` validates right away and again on every `PropertyChanged` of the view model, and reports what it finds on `vm.ValidationContext`:

- **A violation with a member** goes to a property component for that member, the kind `BindValidation(ViewModel, vm => vm.Email, ...)` and
  `ValidationContext.ObserveFor(vm => vm.Email)` look for. A nested member (`"Address.City"`) matches `vm => vm.Address.City`.
- **A violation without a member** goes to one component for the whole view model. It makes `ValidationContext.IsValid` false and is in
  `ValidationContext.Text`, and no property shows it.
- **One validation pass per change** feeds every component; the set is not run once per property.
- **Fixing the input clears the message.** Dispose the returned `IDisposable` to stop validating and remove the components from the context.

A view model usually calls it in its constructor and keeps the `IDisposable` for as long as it lives (or disposes it in `WhenActivated`).

In the view, bind as usual:

```csharp
view.BindValidation(view.ViewModel, x => x.Email, v => v.EmailError);   // "must contain @", empty once fixed
view.BindValidation(view.ViewModel, v => v.FormErrors);                 // every message, including "passwords must match"
```

## Options

Pass a `ConstraintValidationOptions` as the third argument:

```csharp
new ConstraintValidationOptions { MinimumSeverity = Severity.Warning }   // default Error: warnings and hints are ignored

new ConstraintValidationOptions                                          // the domain says Email, the form says EmailAddress
{
	PropertyFor = member => member == nameof(SignUp.Email) ? nameof(ProfileViewModel.EmailAddress) : member
}

new ConstraintValidationOptions { Time = clock }                         // the clock for NotInFuture and Always((x, now) => ...); default TimeProvider.System
```

- `MinimumSeverity`: only errors make the view model invalid by default. ReactiveUI.Validation has no warning state, so a warning you include is shown,
  and blocks, like an error.
- `PropertyFor`: when the view model property is named differently from the domain member. Return `null` to show that member's violations on the view model
  as a whole instead.

## Pitfalls and limits

- **Every property change re-validates**, including properties the rules do not read (`IsBusy`). Keep the projection cheap, and make it not throw: it runs
  inside the property setter, so an exception surfaces there. A text field bound to an `int` is a parsing concern for the view model, not a constraint.
- **Point-in-time rules only.** `ConstraintSet<T>.Validate` skips sequential rules (`Monotonic`, transitions); a form has no "previous" value.
- **Property components appear on first violation.** A property's component is created the first time it has a violation and is kept, valid, from then on.
  `BindValidation` and `ObserveFor` pick it up when it appears.
- **Pass the property itself to `ObserveFor`.** An `Expression<Func<TViewModel, object>>` wraps an `int` property in a conversion, and ReactiveUI.Validation
  then reads its path as `""` and finds nothing. Let the property type be inferred, as `BindValidation` does.
- **ReactiveUI 26 needs its builder** (`RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp()`) before `BindValidation` works; your app already
  has this. `ValidateConstraints` itself does not need it.
- **Not trim or NativeAOT clean.** The package uses no reflection, but ReactiveUI.Validation's `ValidationContext` constructors and `BindValidation` are marked
  `RequiresUnreferencedCode` / `RequiresDynamicCode`.
- Built and tested against ReactiveUI 26.0.1 and ReactiveUI.Validation 8.1.0; other versions are untested.

Every snippet on this page is from the package's tests: `tests/Nullean.Entities.ReactiveUI.Tests/SignUp.cs` (the domain type and view model),
`ConstraintValidationTests.cs` (attach, fix, the member-less rule, dispose, severity, one pass per change), `BindValidationTests.cs` (a real `IViewFor` view)
`MappedMemberTests.cs` (`PropertyFor`) and `TimeTests.cs` (`Time`). The [package README](https://www.nuget.org/packages/Nullean.Entities.ReactiveUI) has the same in one place.
