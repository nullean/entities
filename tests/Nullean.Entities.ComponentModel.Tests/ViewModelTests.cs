using System.ComponentModel;
using AwesomeAssertions;

namespace Nullean.Entities.ComponentModel.Tests;

public class ViewModelTests
{
	private static string[] Messages(INotifyDataErrorInfo info, string? property) => [.. info.GetErrors(property).Cast<string>()];

	[Test]
	public async Task BaseClassRevalidatesOnEveryPropertyChange()
	{
		var vm = new SignupViewModel();
		var errorsChanged = new List<string?>();
		var propertyChanged = new List<string?>();
		vm.ErrorsChanged += (s, e) =>
		{
			s.Should().BeSameAs(vm);
			errorsChanged.Add(e.PropertyName);
		};
		vm.PropertyChanged += (_, e) => propertyChanged.Add(e.PropertyName);

		vm.HasErrors.Should().BeFalse(); // not validated on construction

		vm.Age = 12;
		Messages(vm, nameof(vm.Age)).Should().Equal("Must be 18 or older");
		Messages(vm, nameof(vm.Name)).Should().Equal("Name is required");
		vm.HasErrors.Should().BeTrue();
		propertyChanged.Should().Equal(nameof(vm.Age), nameof(vm.HasErrors));

		propertyChanged.Clear();
		errorsChanged.Clear();
		vm.Name = "Ada";
		vm.Email = "ada@example.com";
		vm.Age = 36;
		vm.HasErrors.Should().BeFalse();
		errorsChanged.Should().Equal(nameof(vm.Name), nameof(vm.Email), nameof(vm.Age));
		propertyChanged.Should().Equal(nameof(vm.Name), nameof(vm.Email), nameof(vm.Age), nameof(vm.HasErrors));
		await Task.CompletedTask;
	}

	[Test]
	public async Task ValidateShowsErrorsUpFront()
	{
		var vm = new SignupViewModel();
		vm.Validate();
		Messages(vm, nameof(vm.Email)).Should().Equal("Email needs an @");
		await Task.CompletedTask;
	}

	[Test]
	public async Task EditRunsTheRulesAboutAChange()
	{
		var vm = new EditSignupViewModel(new Signup("Ada", "ada@example.com", 36));
		vm.HasErrors.Should().BeFalse();

		vm.Age = 35;
		Messages(vm, nameof(vm.Age)).Should().Equal("Age cannot go down");

		vm.Age = 37;
		vm.HasErrors.Should().BeFalse();
		await Task.CompletedTask;
	}

	[Test]
	public async Task ComposesWithCommunityToolkitObservableObject()
	{
		var vm = new ToolkitSignupViewModel();
		var errorsChanged = new List<string?>();
		var hasErrorsRaised = 0;
		vm.ErrorsChanged += (_, e) => errorsChanged.Add(e.PropertyName);
		vm.PropertyChanged += (_, e) => hasErrorsRaised += e.PropertyName == nameof(vm.HasErrors) ? 1 : 0;

		vm.EmailText = "nope";

		Messages(vm, nameof(vm.EmailText)).Should().Equal("Email needs an @"); // Signup.Email mapped to EmailText
		vm.HasErrors.Should().BeTrue();
		hasErrorsRaised.Should().Be(1);
		errorsChanged.Should().Contain(nameof(vm.EmailText));

		vm.Name = "Ada";
		vm.EmailText = "ada@example.com";
		vm.Age = 36;
		vm.HasErrors.Should().BeFalse();
		hasErrorsRaised.Should().Be(2);
		await Task.CompletedTask;
	}
}
