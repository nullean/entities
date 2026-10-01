namespace Nullean.Entities.Examples.Basics.EmailAddress;

public sealed record EmailAddress(string Value);

[Constraints]
public static partial class EmailConstraints
{
	public static readonly Constraint<EmailAddress> NotEmpty = Constraints<EmailAddress>.Always(
		e => !string.IsNullOrWhiteSpace(e.Value),
		"E-mail address cannot be empty."
	);

	// Check lets the violation message embed the offending value.
	public static readonly Constraint<EmailAddress> ValidFormat = Constraints<EmailAddress>.Check(
		e => e.Value.Contains('@') && e.Value.Contains('.') ? [] : [Violation.Error($"'{e.Value}' is not a valid e-mail address.")]
	);
}
