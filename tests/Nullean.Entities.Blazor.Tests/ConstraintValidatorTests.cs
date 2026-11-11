using AwesomeAssertions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Nullean.Entities.Blazor.Tests;

public class ConstraintValidatorTests
{
	// .NET 11 renders a hidden, empty .validation-message for a field without messages (a slot for client-side validation), so hidden ones are not counted.
	private static string[] Messages<T>(IRenderedComponent<T> cut, string container) where T : IComponent =>
		[.. cut.FindAll($"#{container} .validation-message:not([hidden])").Select(e => e.TextContent.Trim())];

	[Test]
	public async Task Submit_shows_each_violation_under_its_field_and_all_of_them_in_the_summary()
	{
		using var ctx = new BunitContext();
		var cut = ctx.Render<SignUpForm>(p => p.Add(f => f.Model, new SignUp { Password = "secret" }));

		await cut.Find("form").SubmitAsync();

		Messages(cut, "name-messages").Should().Equal("Enter your name");
		Messages(cut, "email-messages").Should().Equal("Enter an email address");
		Messages(cut, "confirm-messages").Should().Equal("Must match the password");
		Messages(cut, "summary")
			.Should()
			.BeEquivalentTo("Enter your name", "Enter an email address", "Must match the password", "Accept the terms to continue");
		cut.Instance.Submitted.Should().Be(0);
	}

	[Test]
	public async Task A_valid_model_submits()
	{
		using var ctx = new BunitContext();
		var model = new SignUp { Name = "Ada", Email = "ada@nullean.net", Password = "secret", Confirm = "secret", AcceptTerms = true };
		var cut = ctx.Render<SignUpForm>(p => p.Add(f => f.Model, model));

		await cut.Find("form").SubmitAsync();

		Messages(cut, "summary").Should().BeEmpty();
		cut.Instance.Submitted.Should().Be(1);
	}

	[Test]
	public async Task Changing_a_field_shows_only_that_fields_messages()
	{
		using var ctx = new BunitContext();
		var cut = ctx.Render<SignUpForm>();

		await cut.Find("#email").ChangeAsync(new() { Value = "not-an-email" });

		Messages(cut, "email-messages").Should().Equal("Enter an email address");
		Messages(cut, "name-messages").Should().BeEmpty("the user has not touched Name yet");
		Messages(cut, "summary").Should().Equal("Enter an email address");

		await cut.Find("#email").ChangeAsync(new() { Value = "ada@nullean.net" });

		Messages(cut, "email-messages").Should().BeEmpty();
	}

	[Test]
	public async Task Fixing_one_field_clears_a_message_it_caused_on_another()
	{
		using var ctx = new BunitContext();
		var cut = ctx.Render<SignUpForm>(p => p.Add(f => f.Model, new SignUp { Password = "secret", Confirm = "secrte" }));
		await cut.Find("form").SubmitAsync();
		Messages(cut, "confirm-messages").Should().Equal("Must match the password");

		await cut.Find("#password").ChangeAsync(new() { Value = "secrte" });

		Messages(cut, "confirm-messages").Should().BeEmpty();
		Messages(cut, "name-messages").Should().Equal("Enter your name");
	}

	[Test]
	public async Task Warnings_are_left_out_by_default_and_shown_from_MinimumSeverity_Warning()
	{
		using var ctx = new BunitContext();
		var model = new SignUp { Name = "Ada", Email = "ada@example.com", AcceptTerms = true };

		var errorsOnly = ctx.Render<SignUpForm>(p => p.Add(f => f.Model, model));
		await errorsOnly.Find("form").SubmitAsync();
		Messages(errorsOnly, "email-messages").Should().BeEmpty();
		errorsOnly.Instance.Submitted.Should().Be(1);

		var withWarnings = ctx.Render<SignUpForm>(p => p.Add(f => f.Model, model).Add(f => f.MinimumSeverity, Severity.Warning));
		await withWarnings.Find("form").SubmitAsync();
		Messages(withWarnings, "email-messages").Should().Equal("example.com does not receive mail");
		withWarnings.Instance.Submitted.Should().Be(0);
	}

	[Test]
	public async Task A_companion_set_works_and_its_model_type_is_inferred()
	{
		using var ctx = new BunitContext();
		var cut = ctx.Render<ContactForm>(p => p.Add(f => f.Model, new Contact { Phone = "06-123" }));

		await cut.Find("form").SubmitAsync();

		Messages(cut, "phone-messages").Should().Equal("Digits only");
	}

	[Test]
	public async Task Validate_on_an_EditContext_reports_messages_by_FieldIdentifier()
	{
		using var ctx = new BunitContext();
		var model = new SignUp { Name = "Ada", Email = "ada@nullean.net" };
		var editContext = new EditContext(model);
		ctx.Render<CascadingValue<EditContext>>(
			p => p.Add(c => c.Value, editContext).AddChildContent<ConstraintValidator<SignUp>>(v => v.Add(c => c.Constraints, SignUp.Constraints))
		);

		(await Validate(editContext)).Should().BeFalse();

		editContext.GetValidationMessages(new FieldIdentifier(model, nameof(SignUp.Name))).Should().BeEmpty();
		editContext.GetValidationMessages(new FieldIdentifier(model, string.Empty)).Should().Equal("Accept the terms to continue");
		model.AcceptTerms = true;
		(await Validate(editContext)).Should().BeTrue();
	}

	// .NET 11 obsoletes EditContext.Validate for ValidateAsync; both raise OnValidationRequested, which is what the validator listens to.
	private static async Task<bool> Validate(EditContext editContext)
	{
#if NET11_0_OR_GREATER
		return await editContext.ValidateAsync();
#else
		await Task.CompletedTask;
		return editContext.Validate();
#endif
	}

	[Test]
	public async Task Outside_an_EditForm_it_says_so()
	{
		using var ctx = new BunitContext();

		var render = () => ctx.Render<ConstraintValidator<SignUp>>(p => p.Add(c => c.Constraints, SignUp.Constraints));

		render.Should().Throw<InvalidOperationException>().WithMessage("*inside an EditForm*");
		await Task.CompletedTask;
	}
}
