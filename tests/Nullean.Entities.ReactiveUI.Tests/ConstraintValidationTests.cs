using AwesomeAssertions;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.States;

namespace Nullean.Entities.ReactiveUI.Tests;

public class ConstraintValidationTests
{
	private static SignUpViewModel Valid() => new() { Email = "a@b.c", Age = 30, Password = "correct horse", Confirm = "correct horse" };

	// What BindValidation(ViewModel, vm => vm.Email, ...) reads: the states of the components that validate exactly that property.
	// Generic on purpose: an Expression<Func<_, object>> boxes an int property in a Convert node, which ReactiveUI.Validation's property path lookup reads as "".
	private static IList<IValidationState> StatesFor<TProperty>(
		SignUpViewModel vm,
		System.Linq.Expressions.Expression<Func<SignUpViewModel, TProperty>> property
	)
	{
		IList<IValidationState>? states = null;
		using var _ = vm.ValidationContext.ObserveFor(property).Subscribe(s => states = s);
		return states!;
	}

	private static string MessageFor<TProperty>(SignUpViewModel vm, System.Linq.Expressions.Expression<Func<SignUpViewModel, TProperty>> property) =>
		string.Join(" | ", StatesFor(vm, property).Where(s => !s.IsValid).Select(s => s.Text.ToSingleLine()));

	[Test]
	public void Invalid_input_makes_the_context_invalid_with_the_message_on_its_property()
	{
		var vm = Valid();
		using var rules = vm.ValidateConstraints(SignUp.Constraints, v => v.ToSignUp());
		vm.ValidationContext.IsValid.Should().BeTrue();

		vm.Email = "not-an-email";

		vm.ValidationContext.IsValid.Should().BeFalse();
		MessageFor(vm, v => v.Email).Should().Be("must contain @");
		MessageFor(vm, v => v.Age).Should().BeEmpty();
		vm.ValidationContext.Text.ToSingleLine().Should().Be("must contain @");
	}

	[Test]
	public void Fixing_the_input_clears_the_message()
	{
		var vm = Valid();
		using var rules = vm.ValidateConstraints(SignUp.Constraints, v => v.ToSignUp());
		vm.Age = 12;
		MessageFor(vm, v => v.Age).Should().Be("must be 18 or older");

		vm.Age = 18;

		vm.ValidationContext.IsValid.Should().BeTrue();
		MessageFor(vm, v => v.Age).Should().BeEmpty();
		vm.ValidationContext.Text.ToSingleLine().Should().BeEmpty();
	}

	[Test]
	public void The_view_model_is_validated_as_soon_as_the_rules_are_attached()
	{
		var vm = new SignUpViewModel { Email = "x", Age = 30 };

		using var rules = vm.ValidateConstraints(SignUp.Constraints, v => v.ToSignUp());

		vm.ValidationContext.IsValid.Should().BeFalse();
		MessageFor(vm, v => v.Email).Should().Be("must contain @");
	}

	[Test]
	public void A_rule_without_a_member_shows_on_the_context_and_on_no_property()
	{
		var vm = Valid();
		using var rules = vm.ValidateConstraints(SignUp.Constraints, v => v.ToSignUp());

		vm.Confirm = "something else";

		vm.ValidationContext.IsValid.Should().BeFalse();
		vm.ValidationContext.Text.ToSingleLine().Should().Be("passwords must match");
		MessageFor(vm, v => v.Password).Should().BeEmpty();
		MessageFor(vm, v => v.Confirm).Should().BeEmpty();
	}

	[Test]
	public void Two_violations_show_together_in_the_context_text()
	{
		var vm = Valid();
		using var rules = vm.ValidateConstraints(SignUp.Constraints, v => v.ToSignUp());

		vm.Email = "x";
		vm.Confirm = "y";

		vm.ValidationContext.Text.Should().BeEquivalentTo(["passwords must match", "must contain @"]);
	}

	[Test]
	public void Disposing_stops_validation_and_removes_the_rules()
	{
		var vm = Valid();
		var rules = vm.ValidateConstraints(SignUp.Constraints, v => v.ToSignUp());
		vm.Email = "x";
		vm.ValidationContext.IsValid.Should().BeFalse();

		rules.Dispose();

		vm.ValidationContext.IsValid.Should().BeTrue();
		vm.ValidationContext.Validations.Count.Should().Be(0);
		vm.Age = 1;
		vm.ValidationContext.IsValid.Should().BeTrue();
		vm.ValidationContext.Validations.Count.Should().Be(0);
	}

	[Test]
	public void Warnings_do_not_invalidate_by_default_and_do_with_a_lower_minimum_severity()
	{
		var vm = new SignUpViewModel { Email = "a@b.c", Age = 30, Password = "short", Confirm = "short" };

		using (vm.ValidateConstraints(SignUp.Constraints, v => v.ToSignUp()))
		{
			vm.ValidationContext.IsValid.Should().BeTrue();
		}

		using var rules = vm.ValidateConstraints(SignUp.Constraints, v => v.ToSignUp(), new ConstraintValidationOptions { MinimumSeverity = Severity.Warning });
		vm.ValidationContext.IsValid.Should().BeFalse();
		MessageFor(vm, v => v.Password).Should().Be("a longer password is safer");
	}

	[Test]
	public void One_validation_pass_serves_every_property_per_change()
	{
		var vm = Valid();
		var passes = 0;
		using var rules = vm.ValidateConstraints(SignUp.Constraints, v =>
		{
			passes++;
			return v.ToSignUp();
		});
		vm.Email = "x";
		vm.Age = 1;
		passes = 0;

		vm.Confirm = "different";

		passes.Should().Be(1);
	}
}
