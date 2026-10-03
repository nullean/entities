// Nullean.Entities.SourceGenerators — diagnostics for the [Constraint] generator.
//
// Every one of these is an Error, not a Warning: the whole point of [Constraint] is that a set
// can never silently drift from the fields actually declared (see ConstraintGenerator.cs). A
// misuse that degraded to "warn and generate a partial set anyway" would be exactly the kind of
// silent gap the attribute exists to prevent, so a type with any invalid [Constraint] member gets
// no generated Constraints property at all until every member on it is fixed.

using Microsoft.CodeAnalysis;

namespace Nullean.Entities.SourceGenerators;

internal static class Diagnostics
{
	private const string Category = "Nullean.Entities.SourceGenerators";

	public static readonly DiagnosticDescriptor TypeMustBePartial = new(
		"NECG001",
		"Type with [Constraint] members must be partial",
		"'{0}' has [Constraint] members but is not declared partial; the generator cannot add a Constraints property to it",
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MemberMustBeStatic = new(
		"NECG002",
		"[Constraint] member must be static",
		"'{0}' is marked [Constraint] but is not static; only static fields and properties can be bundled into a Constraints set",
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor UnsupportedMemberType = new(
		"NECG003",
		"[Constraint] member has an unsupported type",
		"'{0}' is marked [Constraint] but its type is neither Constraint<T> nor ReactiveConstraint<T>",
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor GenericTypeNotSupported = new(
		"NECG004",
		"Generic types are not supported",
		"'{0}' has [Constraint] members but is a generic type, which this generator does not yet support",
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor NestedTypeNotSupported = new(
		"NECG005",
		"Nested types are not supported",
		"'{0}' has [Constraint] members but is a nested type, which this generator does not yet support",
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor MixedConstraintKinds = new(
		"NECG006",
		"[Constraint] members must be all Constraint<T> or all ReactiveConstraint<T>",
		"'{0}' mixes Constraint<T> and ReactiveConstraint<T> [Constraint] members; a single type can only bundle one kind",
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor InconsistentTypeArgument = new(
		"NECG007",
		"[Constraint] members must share one type argument",
		"'{0}'s [Constraint] members don't all target the same type; every Constraint<T>/ReactiveConstraint<T> on this type must share one T",
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	// Info, not a warning: a consumer that treats warnings as errors must not fail to build because a model carries [Phone] or a custom validation attribute.
	public static readonly DiagnosticDescriptor UnsupportedAnnotation = new(
		"NECG010",
		"Validation attribute is not turned into a constraint",
		"'{0}.{1}' has [{2}], which the generator does not turn into a constraint; write it as a Constraint<T> if the rule should be checked, or mark the property [ConstraintIgnore]",
		Category,
		DiagnosticSeverity.Info,
		isEnabledByDefault: true
	);
}
