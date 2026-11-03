using System.Collections;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nullean.Entities.ComponentModel.Tests;

// The base class: for an app without an MVVM toolkit. docs/integration/mvvm.md shows this one.
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

// Editing an existing entity: the rules about a change run against the value the edit started from.
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

// CommunityToolkit.Mvvm: the view model keeps ObservableObject as its base and delegates INotifyDataErrorInfo to a ConstraintErrors<T>.
public sealed partial class ToolkitSignupViewModel : ObservableObject, INotifyDataErrorInfo
{
	private readonly ConstraintErrors<Signup> _errors;

	public ToolkitSignupViewModel() =>
		_errors = new ConstraintErrors<Signup>(
			Signup.Constraints,
			() => new Signup(Name, EmailText, Age),
			this,
			new ConstraintErrorsOptions { Members = new Dictionary<string, string> { [nameof(Signup.Email)] = nameof(EmailText) } }
		);

	[ObservableProperty]
	public partial string Name { get; set; } = "";

	[ObservableProperty]
	public partial string EmailText { get; set; } = "";

	[ObservableProperty]
	public partial int Age { get; set; }

	// Every generated setter ends in OnPropertyChanged: revalidate there, and tell the view when HasErrors flips.
	protected override void OnPropertyChanged(PropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);
		if (e.PropertyName == nameof(HasErrors))
		{
			return;
		}
		var had = _errors.HasErrors;
		_errors.Validate();
		if (had != _errors.HasErrors)
		{
			OnPropertyChanged(nameof(HasErrors));
		}
	}

	public bool HasErrors => _errors.HasErrors;

	public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged { add => _errors.ErrorsChanged += value; remove => _errors.ErrorsChanged -= value; }

	public IEnumerable GetErrors(string? propertyName) => _errors.GetErrors(propertyName);
}
