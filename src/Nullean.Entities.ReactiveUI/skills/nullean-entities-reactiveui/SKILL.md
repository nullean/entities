---
name: nullean-entities-reactiveui
description: >-
  Use when a ReactiveUI view model with a ReactiveUI.Validation ValidationContext should validate against Nullean.Entities constraints.
  It adds `ValidateConstraints`, which turns a constraint set into validation components that `BindValidation` and `ObserveFor` find.
---

# Nullean.Entities.ReactiveUI

Runs a `ConstraintSet<T>` over a value projected from the view model, now and on every `PropertyChanged`, and reports one component per member on its `ValidationContext`. For a view model without ReactiveUI.Validation use `Nullean.Entities.ComponentModel` (`INotifyDataErrorInfo`) instead.

## The calls you need

The view model is a `ReactiveObject` with a `ValidationContext` and a projection to the domain value:

```csharp
public sealed class SignUpViewModel : ReactiveObject, IValidatableViewModel
{
	private string _email = "";
	public string Email { get => _email; set => this.RaiseAndSetIfChanged(ref _email, value); }

	public IValidationContext ValidationContext { get; } = new ValidationContext();

	public SignUp ToSignUp() => new(Email);
}
```

Attach the rules, usually in the constructor, and keep the `IDisposable` as long as the view model lives:

```csharp
var rules = vm.ValidateConstraints(SignUp.Constraints, v => v.ToSignUp());
vm.Email = "nope";   // vm.ValidationContext.IsValid is now false
```

Bind in the view as with any ReactiveUI.Validation rule:

```csharp
view.BindValidation(view.ViewModel, x => x.Email, v => v.EmailError);   // messages for Violation.Member "Email"
view.BindValidation(view.ViewModel, v => v.FormErrors);                 // every message, member-less ones included
```

Options go in the third argument, for when the names differ or warnings should show:

```csharp
var options = new ConstraintValidationOptions
{
	MinimumSeverity = Severity.Warning,
	PropertyFor = member => member == "Address" ? nameof(SignUpViewModel.Email) : member,   // null shows it on the view model as a whole
};
```

## Gotchas

- Validation runs once immediately and then on every property change, including properties the rules never read. The projection runs inside the setter: keep it cheap and make it not throw.
- Point-in-time rules only; `Transitions` and `Monotonic` rules are skipped (`Validate`, not `Check`).
- A property's component is added the first time that property has a violation and then stays (valid). Disposing the result removes them all.
- `ObserveFor` with an `Expression<Func<TViewModel, object>>` boxes an `int` property and then finds nothing; let the lambda type be inferred.
- An included warning makes its property invalid, like an error.
- ReactiveUI 26 needs `RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp()` before `BindValidation` works.
- Not NativeAOT clean, because ReactiveUI.Validation is not.

Docs: https://entities.nullean.net
