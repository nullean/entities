using AwesomeAssertions;
using ReactiveUI;
using ReactiveUI.Validation.Extensions;

namespace Nullean.Entities.ReactiveUI.Tests;

// A view without a UI framework: IViewFor plus the string properties a form would show.
public sealed class SignUpView : ReactiveObject, IViewFor<SignUpViewModel>
{
	private SignUpViewModel? _viewModel;
	private string _emailError = "";
	private string _formErrors = "";

	public SignUpViewModel? ViewModel { get => _viewModel; set => this.RaiseAndSetIfChanged(ref _viewModel, value); }

	object? IViewFor.ViewModel { get => ViewModel; set => ViewModel = (SignUpViewModel?)value; }

	public string EmailError { get => _emailError; set => this.RaiseAndSetIfChanged(ref _emailError, value); }

	public string FormErrors { get => _formErrors; set => this.RaiseAndSetIfChanged(ref _formErrors, value); }
}

public class BindValidationTests
{
	[Test]
	public void BindValidation_shows_the_violation_on_the_bound_view_property_and_clears_it()
	{
		var vm = new SignUpViewModel { Email = "a@b.c", Age = 30, Password = "correct horse", Confirm = "correct horse" };
		using var rules = vm.ValidateConstraints(SignUp.Constraints, v => v.ToSignUp());
		var view = new SignUpView { ViewModel = vm };
		using var emailBinding = view.BindValidation(view.ViewModel, x => x.Email, v => v.EmailError);
		using var formBinding = view.BindValidation(view.ViewModel, v => v.FormErrors);
		view.EmailError.Should().BeEmpty();

		vm.Email = "nope";
		vm.Confirm = "different";

		view.EmailError.Should().Be("must contain @");
		view.FormErrors.Should().Contain("passwords must match").And.Contain("must contain @");

		vm.Email = "a@b.c";
		vm.Confirm = vm.Password;

		view.EmailError.Should().BeEmpty();
		view.FormErrors.Should().BeEmpty();
	}
}
