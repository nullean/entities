using ReactiveUI;
using ReactiveUI.Validation.Abstractions;
using ReactiveUI.Validation.Contexts;

namespace Nullean.Entities.ReactiveUI.Tests;

// The domain type: the rules live here, not in the view model.
public sealed partial record SignUp(string Email, int Age, string Password, string Confirm)
{
	[Constraint]
	public static readonly Constraint<SignUp> EmailHasAt = Constraints<SignUp>.Check(
		s => s.Email.Contains('@') ? [] : [Violation.Error(nameof(Email), "must contain @")]
	);

	[Constraint]
	public static readonly Constraint<SignUp> Adult = Constraints<SignUp>.Check(
		s => s.Age >= 18 ? [] : [Violation.Error(nameof(Age), "must be 18 or older")]
	);

	[Constraint]
	public static readonly Constraint<SignUp> PasswordsMatch = Constraints<SignUp>.Check(
		s => s.Password == s.Confirm ? [] : [Violation.Error("passwords must match")]
	);

	[Constraint]
	public static readonly Constraint<SignUp> LongPassword = Constraints<SignUp>.Check(
		s => s.Password.Length >= 12 ? [] : [Violation.Warning("a longer password is safer") with { Member = nameof(Password) }]
	);
}

// The view model: plain ReactiveUI properties, and the context ReactiveUI.Validation reads.
public sealed class SignUpViewModel : ReactiveObject, IValidatableViewModel
{
	private string _email = "";
	private int _age;
	private string _password = "";
	private string _confirm = "";

	public string Email { get => _email; set => this.RaiseAndSetIfChanged(ref _email, value); }
	public int Age { get => _age; set => this.RaiseAndSetIfChanged(ref _age, value); }
	public string Password { get => _password; set => this.RaiseAndSetIfChanged(ref _password, value); }
	public string Confirm { get => _confirm; set => this.RaiseAndSetIfChanged(ref _confirm, value); }

	public IValidationContext ValidationContext { get; } = new ValidationContext();

	public SignUp ToSignUp() => new(Email, Age, Password, Confirm);
}
