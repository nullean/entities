namespace Nullean.Entities.Blazor.Tests;

// A form model: mutable, so InputText can bind to it. It declares its own rules, so SignUp.Constraints is generated.
public sealed partial class SignUp
{
	public string Name { get; set; } = "";
	public string Email { get; set; } = "";
	public string Password { get; set; } = "";
	public string Confirm { get; set; } = "";
	public bool AcceptTerms { get; set; }

	[Constraint]
	public static readonly Constraint<SignUp> NameGiven = Constraints<SignUp>.Check(
		s => string.IsNullOrWhiteSpace(s.Name) ? [Violation.Error(nameof(Name), "Enter your name")] : []
	);

	[Constraint]
	public static readonly Constraint<SignUp> EmailLooksValid = Constraints<SignUp>.Check(
		s => s.Email.Contains('@') ? [] : [Violation.Error(nameof(Email), "Enter an email address")]
	);

	// A warning: not shown unless MinimumSeverity is Warning or Hint.
	[Constraint]
	public static readonly Constraint<SignUp> EmailNotExample = Constraints<SignUp>.Check(
		s => s.Email.EndsWith("@example.com", StringComparison.Ordinal)
			? [new Violation("example.com does not receive mail", Severity.Warning) { Member = nameof(Email) }]
			: []
	);

	// A rule over two fields, reported on Confirm.
	[Constraint]
	public static readonly Constraint<SignUp> ConfirmMatches = Constraints<SignUp>.Check(
		s => s.Confirm == s.Password ? [] : [Violation.Error(nameof(Confirm), "Must match the password")]
	);

	// No member: a model-level message, shown by ValidationSummary only.
	[Constraint]
	public static readonly Constraint<SignUp> TermsAccepted = Constraints<SignUp>.Check(
		s => s.AcceptTerms ? [] : [Violation.Error("Accept the terms to continue")]
	);
}

// A model with no rules of its own; a companion holds them.
public sealed class Contact
{
	public string Phone { get; set; } = "";
}

[Constraints<Contact>]
public static partial class ContactRules
{
	[Constraint]
	public static readonly Constraint<Contact> PhoneDigits = Constraints<Contact>.Check(
		c => c.Phone.Length > 0 && c.Phone.All(char.IsAsciiDigit) ? [] : [Violation.Error(nameof(Contact.Phone), "Digits only")]
	);
}
