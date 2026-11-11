namespace Nullean.Entities.ComponentModel.Tests;

/// <summary>The domain value the view models in these tests edit: immutable, with its rules on the type.</summary>
public sealed partial record Signup(string Name, string Email, int Age)
{
	[Constraint]
	public static readonly Constraint<Signup> NameGiven = Constraints<Signup>.Check(
		s => string.IsNullOrWhiteSpace(s.Name) ? [Violation.Error(nameof(Name), "Name is required")] : []
	);

	[Constraint]
	public static readonly Constraint<Signup> EmailHasAt = Constraints<Signup>.Check(
		s => s.Email.Contains('@') ? [] : [Violation.Error(nameof(Email), "Email needs an @")]
	);

	[Constraint]
	public static readonly Constraint<Signup> Adult = Constraints<Signup>.Check(s => s.Age >= 18 ? [] : [Violation.Error(nameof(Age), "Must be 18 or older")]);

	// Not about one member: an entity-level error.
	[Constraint]
	public static readonly Constraint<Signup> NameIsNotEmail = Constraints<Signup>.Check(
		s => s.Name.Length > 0 && s.Name == s.Email ? [Violation.Error("Name and email must differ")] : []
	);

	// A warning: not reported unless ConstraintErrorsOptions.MinimumSeverity asks for it.
	[Constraint]
	public static readonly Constraint<Signup> LongName = Constraints<Signup>.Check(
		s => s.Name.Length > 20 ? [new Violation("Long names may be cut off", Severity.Warning) { Member = nameof(Name) }] : []
	);

	// A rule about a change: only Validate(previous) runs it.
	[Constraint]
	public static readonly Constraint<Signup> AgeNeverDecreases = Constraints<Signup>.CheckTransition(
		(before, after) => after.Age < before.Age ? [Violation.Error(nameof(Age), "Age cannot go down")] : []
	);
}
