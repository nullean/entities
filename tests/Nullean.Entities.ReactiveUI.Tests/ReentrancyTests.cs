using AwesomeAssertions;
using ReactiveUI;
using ReactiveUI.Validation.Abstractions;
using ReactiveUI.Validation.Contexts;

namespace Nullean.Entities.ReactiveUI.Tests;

// A view model that sets one of its own properties when validity changes, as a CanSubmit flag does. That property change re-enters validation.
public sealed class SubmitViewModel : ReactiveObject, IValidatableViewModel
{
	private string _email = "";
	private int _age = 30;
	private bool _canSubmit;

	public SubmitViewModel() => ValidationContext.Valid.Subscribe(valid => CanSubmit = valid);

	public string Email { get => _email; set => this.RaiseAndSetIfChanged(ref _email, value); }
	public int Age { get => _age; set => this.RaiseAndSetIfChanged(ref _age, value); }
	public bool CanSubmit { get => _canSubmit; set => this.RaiseAndSetIfChanged(ref _canSubmit, value); }

	public IValidationContext ValidationContext { get; } = new ValidationContext();
}

public class ReentrancyTests
{
	[Test]
	public void A_view_model_that_reacts_to_validity_is_validated_once_per_state_and_stays_consistent()
	{
		var vm = new SubmitViewModel { Email = "a@b.c" };
		using var rules = vm.ValidateConstraints(SignUp.Constraints, v => new SignUp(v.Email, v.Age, "correct horse", "correct horse"));
		vm.CanSubmit.Should().BeTrue();

		vm.Email = "x";
		vm.CanSubmit.Should().BeFalse();

		vm.Age = 1;
		vm.CanSubmit.Should().BeFalse();
		vm.ValidationContext.Text.Should().BeEquivalentTo(["must contain @", "must be 18 or older"]);

		vm.Email = "a@b.c";
		vm.Age = 30;
		vm.CanSubmit.Should().BeTrue();
		vm.ValidationContext.Validations.Count.Should().Be(3);
	}

	[Test]
	public void A_new_property_violation_found_while_validity_flips_does_not_break_the_pass()
	{
		// Age comes from the email here, so one change breaks Email (whose component already exists) and Age (which has none yet) together.
		var vm = new SubmitViewModel { Email = "abcd" };
		using var rules = vm.ValidateConstraints(SignUp.Constraints, v => new SignUp(v.Email, v.Email.Length > 3 ? 30 : 1, "correct horse", "correct horse"));
		vm.Email = "a@bcd";
		vm.CanSubmit.Should().BeTrue();

		vm.Email = "x";

		vm.CanSubmit.Should().BeFalse();
		vm.ValidationContext.Text.Should().BeEquivalentTo(["must contain @", "must be 18 or older"]);
		vm.ValidationContext.Validations.Count.Should().Be(3);
	}
}
