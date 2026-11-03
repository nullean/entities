---
navigation_title: MVVM (Avalonia, WPF, WinUI, MAUI)
description: Show constraint violations next to the field in an MVVM app, through INotifyDataErrorInfo, with or without an MVVM toolkit.
---

# MVVM: Avalonia, WPF, WinUI, MAUI

```bash
dotnet add package Nullean.Entities.ComponentModel
```

A desktop or mobile form binds to a view model. `Nullean.Entities.ComponentModel` runs your constraint set from that view model and reports the violations through
`System.ComponentModel.INotifyDataErrorInfo`, the interface Avalonia's and WPF's bindings read to put an error next to a field (WinUI and MAUI: see [Binding](#binding)). The
same rules that guard your domain and your API now drive the red outline on the text box.

The package depends on `Nullean.Entities` and the BCL only: no UI framework, no reflection. It is trim and NativeAOT clean, and is published NativeAOT in this repository's CI.

## The shape: a mutable view model over an immutable value

A constraint set is over an immutable domain value; a view model is mutable. `ConstraintErrors<T>` bridges them: you give it the set and a function that builds the domain value
from the view model's current state, and it does the rest.

```csharp
public sealed partial record Signup(string Name, string Email, int Age)
{
	[Constraint]
	public static readonly Constraint<Signup> EmailHasAt = Constraints<Signup>.Check(
		s => s.Email.Contains('@') ? [] : [Violation.Error(nameof(Email), "Email needs an @")]
	);
	// NameGiven, Adult, ... the same way
}
```

`ConstraintErrors<T>.Validate()` builds the value, runs the set and raises `ErrorsChanged` only for the properties whose messages changed. A view model owns one and delegates
`INotifyDataErrorInfo` to it.

## Without an MVVM toolkit: `ConstraintViewModel<T>`

The package has a small base class that implements `INotifyPropertyChanged` and `INotifyDataErrorInfo` and revalidates after every property change:

```csharp
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

Nothing is reported until the first change, so an empty form is not red on open. Call `Validate()` (before a save, say) to show every error at once. `HasErrors` raises
`PropertyChanged` when it flips, so a Save button can bind to it.

## With CommunityToolkit.Mvvm (or any base class of your own)

A toolkit view model already has a base class (`ObservableObject`), so own a `ConstraintErrors<T>` instead and delegate to it. Revalidate in `OnPropertyChanged`, which every
generated `[ObservableProperty]` setter calls:

```csharp
public sealed partial class ToolkitSignupViewModel : ObservableObject, INotifyDataErrorInfo
{
	private readonly ConstraintErrors<Signup> _errors;

	public ToolkitSignupViewModel() =>
		_errors = new ConstraintErrors<Signup>(
			Signup.Constraints,
			() => new Signup(Name, EmailText, Age),
			this,                                         // the sender of ErrorsChanged: the view model, not the helper
			new ConstraintErrorsOptions { Members = new Dictionary<string, string> { [nameof(Signup.Email)] = nameof(EmailText) } }
		);

	[ObservableProperty] public partial string Name { get; set; } = "";
	[ObservableProperty] public partial string EmailText { get; set; } = "";
	[ObservableProperty] public partial int Age { get; set; }

	protected override void OnPropertyChanged(PropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);
		if (e.PropertyName == nameof(HasErrors))
			return;
		var had = _errors.HasErrors;
		_errors.Validate();
		if (had != _errors.HasErrors)
			OnPropertyChanged(nameof(HasErrors));
	}

	public bool HasErrors => _errors.HasErrors;
	public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged { add => _errors.ErrorsChanged += value; remove => _errors.ErrorsChanged -= value; }
	public IEnumerable GetErrors(string? propertyName) => _errors.GetErrors(propertyName);
}
```

This is tested against CommunityToolkit.Mvvm 8.4.2. The `Members` map is for a domain member whose view-model property has another name (`Signup.Email` is edited as
`EmailText` here); members not in the map keep their own name. CommunityToolkit's own `ObservableValidator` runs DataAnnotations attributes; you do not need it for this.

## Binding

Nothing to configure in the view: bind as usual and the framework reads the errors.

```xml
<!-- Avalonia -->
<TextBox Text="{Binding Email}" />
```

- **Avalonia 12**: the built-in `INotifyDataErrorInfo` plugin is on by default, and the error shows through `DataValidationErrors` (the Fluent theme draws it under the box).
  Avalonia 12 turned its DataAnnotations plugin off by default; this package does not need it.
- **WPF**: bindings read `INotifyDataErrorInfo` by default since .NET 4.5 (`ValidatesOnNotifyDataErrors` defaults to true); the error is in `Validation.Errors`.
- **WinUI 3**: has no complete built-in validation display. Microsoft's guidance (the "build a validated form" article on Microsoft Learn) is to implement
  `INotifyDataErrorInfo` on the view model and show the errors yourself (call `GetErrors`, or bind to a property you raise from `ErrorsChanged`); the stock `TextBox` does not
  draw them.
- **MAUI**: the stock controls (`Entry` and the rest) do not read `INotifyDataErrorInfo`. Call `GetErrors` from your own label or behaviour; third-party control suites
  (Syncfusion's and Telerik's data forms and grids) read the interface.

Only the Avalonia binding is tested here, with `Avalonia.Headless` 12.1.5: a `TextBox` bound to a view model property, its text changed, and
`DataValidationErrors.GetErrors(textBox)` holding the constraint's message. The binding is made in code (`new Binding("Email")`), not compiled XAML. WPF, WinUI and MAUI need
their own platforms to run and are not tested in this repository; they read the same interface.

## What gets reported

| Violation | Where it shows |
|---|---|
| `Member` set (`Violation.Error(nameof(Email), "...")`) | `GetErrors("Email")`, or the mapped property name |
| No `Member` (a rule about the whole value) | Entity-level: `GetErrors(null)` and `GetErrors("")`; `ErrorsChanged` is raised with a null property name |
| `Severity.Warning` or `Severity.Hint` | Left out, unless `ConstraintErrorsOptions.MinimumSeverity` is `Warning` (or `Hint`); then it shows like an error |

`GetErrors` returns the messages as strings, which every binding engine displays as they are. `GetViolations(property)` returns the `Violation`s themselves (severity, rule name,
evidence) for a view that wants more; `Violations` holds every reported one.

## Editing an existing entity

Rules about a change (`CheckTransition`, `Transitions`, `Monotonic`) need the value before the edit. Keep it and call `Validate(previous)`, which runs every rule through
`ConstraintSet<T>.Check(before, after)`:

```csharp
public sealed class EditSignupViewModel : ConstraintViewModel<Signup>
{
	private readonly Signup _original;
	private int _age;

	public EditSignupViewModel(Signup original) : base(Signup.Constraints)
	{
		_original = original;
		_age = original.Age;
	}

	public int Age { get => _age; set => SetProperty(ref _age, value); }

	protected override Signup ToValue() => _original with { Age = Age };

	protected override IReadOnlyList<Violation> RunConstraints() => Errors.Validate(_original);
}
```

## Pitfalls and limits

- **Validation runs on the UI thread, on every change.** The projection and the constraint set should be cheap. Constraints are synchronous and pure; a check that needs a
  database or a web call (is this user name taken?) does not belong in them. There is no async validation and no debounce here.
- **Every property is revalidated on every change,** so a field the user has not touched yet can turn red when another one changes (a required name, after the age is typed).
  There is no "touched" tracking; if you want it, filter in your own `GetErrors`.
- **The projection must not throw.** If a half-typed form cannot become a domain value (the record's constructor rejects it), build a looser value for the form, or catch and report
  it yourself. A converter that cannot parse the text (letters in an `int` box) is reported by the binding engine, not by this package.
- **Nested members.** A violation on `Address.City` is reported under `"Address.City"`. Bindings ask by the leaf property name on the object that owns it, so map it
  (`Members = new Dictionary<string, string> { ["Address.City"] = "City" }`) or put the errors on the nested view model.
- **One reported severity.** `INotifyDataErrorInfo` has no severity; a warning you opt into looks like an error.

The package's tests are in [`tests/Nullean.Entities.ComponentModel.Tests`](https://github.com/nullean/entities/tree/main/tests/Nullean.Entities.ComponentModel.Tests): every
snippet on this page is in `Signup.cs` and `ViewModels.cs` there, the behaviour is in `ConstraintErrorsTests.cs` and `ViewModelTests.cs`, and the Avalonia binding in
`AvaloniaBindingTests.cs`.
