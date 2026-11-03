using AwesomeAssertions;
using ReactiveUI;
using ReactiveUI.Validation.Abstractions;
using ReactiveUI.Validation.Contexts;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.States;

namespace Nullean.Entities.ReactiveUI.Tests;

// The domain calls it Email; the form calls it EmailAddress.
public sealed class ProfileViewModel : ReactiveObject, IValidatableViewModel
{
	private string _emailAddress = "";

	public string EmailAddress { get => _emailAddress; set => this.RaiseAndSetIfChanged(ref _emailAddress, value); }

	public IValidationContext ValidationContext { get; } = new ValidationContext();
}

public class MappedMemberTests
{
	private static readonly ConstraintSet<SignUp> EmailOnly = ConstraintSet.From(("EmailHasAt", SignUp.EmailHasAt));

	private static string MessageFor<TProperty>(ProfileViewModel vm, System.Linq.Expressions.Expression<Func<ProfileViewModel, TProperty>> property)
	{
		IList<IValidationState>? states = null;
		using var _ = vm.ValidationContext.ObserveFor(property).Subscribe(s => states = s);
		return string.Join(" | ", states!.Where(s => !s.IsValid).Select(s => s.Text.ToSingleLine()));
	}

	[Test]
	public void A_domain_member_shows_on_the_view_model_property_it_maps_to()
	{
		var vm = new ProfileViewModel { EmailAddress = "a@b.c" };
		using var rules = vm.ValidateConstraints(
			EmailOnly,
			v => new SignUp(v.EmailAddress, 30, "", ""),
			new ConstraintValidationOptions { PropertyFor = member => member == nameof(SignUp.Email) ? nameof(ProfileViewModel.EmailAddress) : member }
		);

		vm.EmailAddress = "nope";

		MessageFor(vm, v => v.EmailAddress).Should().Be("must contain @");
	}

	[Test]
	public void A_member_mapped_to_null_shows_on_the_context_only()
	{
		var vm = new ProfileViewModel { EmailAddress = "nope" };
		using var rules = vm.ValidateConstraints(
			EmailOnly,
			v => new SignUp(v.EmailAddress, 30, "", ""),
			new ConstraintValidationOptions { PropertyFor = _ => null }
		);

		vm.ValidationContext.IsValid.Should().BeFalse();
		vm.ValidationContext.Text.ToSingleLine().Should().Be("must contain @");
		MessageFor(vm, v => v.EmailAddress).Should().BeEmpty();
	}
}
