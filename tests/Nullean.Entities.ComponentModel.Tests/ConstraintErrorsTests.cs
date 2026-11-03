using System.ComponentModel;
using AwesomeAssertions;

namespace Nullean.Entities.ComponentModel.Tests;

public class ConstraintErrorsTests
{
	// A mutable form the projection reads: what a view model's fields are.
	private sealed class Form
	{
		public string Name = "Ada";
		public string Email = "ada@example.com";
		public int Age = 36;
		public Signup ToValue() => new(Name, Email, Age);
	}

	private static (Form Form, ConstraintErrors<Signup> Errors, List<string?> Changed) Create(ConstraintErrorsOptions? options = null, object? sender = null)
	{
		var form = new Form();
		var errors = new ConstraintErrors<Signup>(Signup.Constraints, form.ToValue, sender, options);
		var changed = new List<string?>();
		errors.ErrorsChanged += (_, e) => changed.Add(e.PropertyName);
		return (form, errors, changed);
	}

	private static string[] Messages(ConstraintErrors<Signup> info, string? property) => [.. info.GetErrors(property).Cast<string>()];

	[Test]
	public async Task NothingIsReportedBeforeTheFirstValidate()
	{
		var (form, errors, changed) = Create();
		form.Name = "";

		errors.HasErrors.Should().BeFalse();
		Messages(errors, nameof(Signup.Name)).Should().BeEmpty();
		changed.Should().BeEmpty();
		await Task.CompletedTask;
	}

	[Test]
	public async Task ViolationsAreReportedUnderTheirMember()
	{
		var (form, errors, changed) = Create();
		form.Name = "";
		form.Age = 12;

		errors.Validate();

		errors.HasErrors.Should().BeTrue();
		Messages(errors, nameof(Signup.Name)).Should().Equal("Name is required");
		Messages(errors, nameof(Signup.Age)).Should().Equal("Must be 18 or older");
		Messages(errors, nameof(Signup.Email)).Should().BeEmpty();
		errors.GetViolations(nameof(Signup.Age)).Single().ConstraintId.Should().Be(Signup.Adult.Name);
		changed.Should().BeEquivalentTo([nameof(Signup.Name), nameof(Signup.Age)]);
		await Task.CompletedTask;
	}

	[Test]
	public async Task ErrorsChangedIsRaisedOnlyForMembersWhoseErrorsChanged()
	{
		var (form, errors, changed) = Create();
		form.Name = "";
		form.Age = 12;
		errors.Validate();
		changed.Clear();

		form.Age = 40; // Age fixed, Name still wrong with the same message
		errors.Validate();
		changed.Should().Equal(nameof(Signup.Age));

		changed.Clear();
		errors.Validate(); // nothing changed
		changed.Should().BeEmpty();

		form.Name = "Grace";
		errors.Validate();
		changed.Should().Equal(nameof(Signup.Name));
		errors.HasErrors.Should().BeFalse();
		await Task.CompletedTask;
	}

	[Test]
	public async Task AViolationWithoutAMemberIsAnEntityLevelError()
	{
		var (form, errors, changed) = Create();
		form.Name = "a@b";
		form.Email = "a@b";

		errors.Validate();

		Messages(errors, null).Should().Equal("Name and email must differ");
		Messages(errors, "").Should().Equal("Name and email must differ");
		errors.HasErrors.Should().BeTrue();
		changed.Should().Equal([null]);

		changed.Clear();
		form.Email = "c@d";
		errors.Validate();
		Messages(errors, null).Should().BeEmpty();
		changed.Should().Equal([null]);
		await Task.CompletedTask;
	}

	[Test]
	public async Task WarningsAreLeftOutUnlessAsked()
	{
		var (form, errors, _) = Create();
		form.Name = "A name that is far too long to fit";
		errors.Validate();
		errors.HasErrors.Should().BeFalse();

		var (form2, withWarnings, _) = Create(new ConstraintErrorsOptions { MinimumSeverity = Severity.Warning });
		form2.Name = form.Name;
		withWarnings.Validate();
		withWarnings.HasErrors.Should().BeTrue();
		Messages(withWarnings, nameof(Signup.Name)).Should().Equal("Long names may be cut off");
		await Task.CompletedTask;
	}

	[Test]
	public async Task MembersMapToViewModelPropertyNames()
	{
		var options = new ConstraintErrorsOptions { Members = new Dictionary<string, string> { [nameof(Signup.Email)] = "EmailText" } };
		var (form, errors, changed) = Create(options);
		form.Email = "nope";
		form.Age = 3;

		errors.Validate();

		Messages(errors, "EmailText").Should().Equal("Email needs an @");
		Messages(errors, nameof(Signup.Email)).Should().BeEmpty();
		Messages(errors, nameof(Signup.Age)).Should().Equal("Must be 18 or older"); // unmapped: its own name
		changed.Should().BeEquivalentTo(["EmailText", nameof(Signup.Age)]);
		await Task.CompletedTask;
	}

	[Test]
	public async Task ValidateWithPreviousAlsoRunsTheRulesAboutAChange()
	{
		var (form, errors, _) = Create();
		var original = form.ToValue();
		form.Age = 30; // was 36: still an adult, but it went down

		errors.Validate();
		errors.HasErrors.Should().BeFalse();

		errors.Validate(original);
		Messages(errors, nameof(Signup.Age)).Should().Equal("Age cannot go down");
		await Task.CompletedTask;
	}

	[Test]
	public async Task ClearForgetsEveryErrorAndSaysSo()
	{
		var (form, errors, changed) = Create();
		form.Name = "";
		errors.Validate();
		changed.Clear();

		errors.Clear();

		errors.HasErrors.Should().BeFalse();
		errors.Violations.Should().BeEmpty();
		changed.Should().Equal(nameof(Signup.Name));
		await Task.CompletedTask;
	}

	[Test]
	public async Task ErrorsChangedComesFromTheGivenSender()
	{
		var owner = new object();
		var (form, errors, _) = Create(sender: owner);
		object? seen = null;
		errors.ErrorsChanged += (s, _) => seen = s;
		form.Name = "";

		errors.Validate();

		seen.Should().BeSameAs(owner);
		await Task.CompletedTask;
	}
}
