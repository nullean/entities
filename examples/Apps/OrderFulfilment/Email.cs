using Nullean.Entities;

namespace OrderFulfilment;

/// <summary>
/// A value that cannot be invalid: the constructor is private, so the only way to get an <see cref="Email"/> is <see cref="Create"/>, which checks the rules
/// and returns a <see cref="Result{T}"/>. The check lives in the factory, not the constructor (see "Building a value that cannot be invalid" in the core README).
/// </summary>
public sealed record Email
{
	private Email(string value) => Value = value;

	public string Value { get; }

	private static readonly ConstraintSet<Email> Rules = ConstraintSet.From(
		("email-is-required", Constraints<Email>.Always(e => e.Value.Length > 0, "An email address is required")),
		("email-has-one-at", Constraints<Email>.Always(e => e.Value.Count(c => c == '@') == 1, "An email address has exactly one '@'")),
		("email-has-a-domain", Constraints<Email>.Always(e => e.Value.Split('@')[^1].Contains('.'), "The part after '@' needs a dot, like example.com"))
	);

	public static Result<Email> Create(string? value) => Rules.Validated(new Email(value?.Trim() ?? ""));

	public override string ToString() => Value;
}
