// Nullean.Entities.SourceGenerators — DataAnnotations as constraints.
//
// Every public property of T (and every primary-constructor parameter of a record or class that has one) carrying [Required], [Range], [StringLength],
// [MaxLength], [MinLength] or [RegularExpression] becomes one Constraint<T> in the generated set, named <Property><Attribute> (NameRequired,
// AgeRange, ...) and reporting the property as Violation.Member. The code is plain C# with no reflection and no System.ComponentModel.DataAnnotations
// call at runtime, so it is trim and AOT safe.
//
// The meaning of each attribute is the one DataAnnotations gives it, including its leniency: a null passes every attribute except [Required] (null
// is the business of [Required] alone), an empty string passes [RegularExpression], and [RegularExpression] must match the whole string.
// A member that must be set also gets a null-only rule <Property>Required unless it has an explicit [Required]: a non-nullable reference property declared `required`,
// or a get-only auto property (only a constructor can set it). Value types, Nullable<T> and `T?` references get none; opt out with ImplicitRequired = false.
// [ConstraintIgnore] on a property leaves its annotations out. Any other ValidationAttribute is reported as an Info diagnostic (NECG010) rather
// than skipped silently; Info does not fail a build that treats warnings as errors.

using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nullean.Entities.SourceGenerators;

internal sealed record AnnotationEntry(string Name, string Expression);

internal sealed record UnsupportedAnnotation(string Property, string Attribute, Location Location);

internal sealed record AnnotationResult(
	System.Collections.Generic.List<AnnotationEntry> Entries,
	System.Collections.Generic.List<string> Fields,
	System.Collections.Generic.List<UnsupportedAnnotation> Unsupported
);

internal static class Annotations
{
	private const string Ns = "System.ComponentModel.DataAnnotations.";
	private const string ConstraintIgnore = "Nullean.Entities.ConstraintIgnoreAttribute";

	public static AnnotationResult For(
		INamedTypeSymbol type,
		string typeArgDisplay,
		System.Collections.Generic.ICollection<string> takenNames,
		bool implicitRequired = true
	)
	{
		var result = new AnnotationResult([], [], []);
		var names = new System.Collections.Generic.HashSet<string>(takenNames);
		var parameters = PrimaryConstructorParameters(type);

		foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
		{
			if (property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public || property.GetMethod is null)
			{
				continue;
			}
			var attributes = property.GetAttributes().ToList();
			if (parameters.TryGetValue(property.Name, out var parameter))
			{
				attributes.AddRange(parameter.GetAttributes());
			}
			if (attributes.Any(a => a.AttributeClass?.ToDisplayString() == ConstraintIgnore))
			{
				continue;
			}
			var hasExplicitRequired = attributes.Any(a => a.AttributeClass?.ToDisplayString() == Ns + "RequiredAttribute");
			if (implicitRequired && !hasExplicitRequired && MustBeSet(property))
			{
				result.Entries.Add(
					new AnnotationEntry(
						Unique(names, property.Name + "Required"),
						$"global::Nullean.Entities.Constraints<{typeArgDisplay}>.Always(x => x.{property.Name}, v => v is not null, {Quote($"The {property.Name} field is required.")})"
					)
				);
			}
			foreach (var attribute in attributes)
			{
				var cls = attribute.AttributeClass;
				if (cls is null)
				{
					continue;
				}
				var full = cls.ToDisplayString();
				var entry = full switch
				{
					Ns + "RequiredAttribute" => Required(property, attribute),
					Ns + "RangeAttribute" => Range(property, attribute),
					Ns + "StringLengthAttribute" => StringLength(property, attribute),
					Ns + "MaxLengthAttribute" => Length(property, attribute, max: true),
					Ns + "MinLengthAttribute" => Length(property, attribute, max: false),
					Ns + "RegularExpressionAttribute" => Pattern(property, attribute, result.Fields),
					_ => null,
				};
				if (entry is { } built)
				{
					var name = Unique(names, property.Name + built.Suffix);
					result.Entries.Add(
						new AnnotationEntry(
							name,
							$"global::Nullean.Entities.Constraints<{typeArgDisplay}>.Always(x => x.{property.Name}, {built.Predicate}, {Quote(built.Message)})"
						)
					);
				}
				else if (IsValidationAttribute(cls))
				{
					result.Unsupported.Add(new UnsupportedAnnotation(property.Name, cls.Name, property.Locations.FirstOrDefault() ?? Location.None));
				}
			}
		}
		return result;
	}

	// A non-nullable reference member that must be set: declared `required`, or a get-only auto property (only a constructor can set it).
	private static bool MustBeSet(IPropertySymbol property)
	{
		if (!property.Type.IsReferenceType || property.Type.NullableAnnotation == NullableAnnotation.Annotated)
		{
			return false;
		}
		if (property.IsRequired)
		{
			return true;
		}
		if (property.SetMethod is not null || property.IsAbstract)
		{
			return false;
		}
		var syntax = property.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<PropertyDeclarationSyntax>().FirstOrDefault();
		if (syntax is null || syntax.ExpressionBody is not null || syntax.Initializer is not null || syntax.AccessorList is null)
		{
			return false;
		}
		var accessors = syntax.AccessorList.Accessors;
		return accessors.Count == 1
		&& accessors[0].IsKind(SyntaxKind.GetAccessorDeclaration)
		&& accessors[0].Body is null
		&& accessors[0].ExpressionBody is null;
	}

	private sealed record Built(string Suffix, string Predicate, string Message);

	// The constructor whose parameters are the properties: a record's or class's primary constructor, or (from metadata) the one with the most parameters that all match.
	private static System.Collections.Generic.Dictionary<string, IParameterSymbol> PrimaryConstructorParameters(INamedTypeSymbol type)
	{
		var properties = new System.Collections.Generic.HashSet<string>(
			type.GetMembers().OfType<IPropertySymbol>().Select(p => p.Name),
			StringComparer.OrdinalIgnoreCase
		);
		var best = type
			.InstanceConstructors
			.Where(c => c.Parameters.Length > 0 && c.Parameters.All(p => properties.Contains(p.Name)))
			.OrderByDescending(c => c.Parameters.Length)
			.FirstOrDefault();
		var map = new System.Collections.Generic.Dictionary<string, IParameterSymbol>(StringComparer.OrdinalIgnoreCase);
		if (best is not null)
		{
			foreach (var p in best.Parameters)
			{
				map[p.Name] = p;
			}
		}
		return map;
	}

	private static bool IsValidationAttribute(INamedTypeSymbol? type)
	{
		for (var t = type; t is not null; t = t.BaseType)
		{
			if (t.ToDisplayString() == Ns + "ValidationAttribute")
			{
				return true;
			}
		}
		return false;
	}

	private static string Unique(System.Collections.Generic.HashSet<string> taken, string name)
	{
		var candidate = name;
		for (var i = 1; !taken.Add(candidate); i++)
		{
			candidate = i == 1 ? name + "Annotation" : name + "Annotation" + i.ToString(CultureInfo.InvariantCulture);
		}
		return candidate;
	}

	private static string? Named(AttributeData a, string name) =>
		a.NamedArguments.Where(n => n.Key == name).Select(n => n.Value.Value as string).FirstOrDefault();

	private static bool NamedBool(AttributeData a, string name) =>
		a.NamedArguments.Any(n => n.Key == name && n.Value.Value is true);

	private static string Message(AttributeData a, string property, string fallback)
	{
		var custom = Named(a, "ErrorMessage");
		return custom is null ? fallback : custom.Replace("{0}", property);
	}

	// ── [Required]

	private static Built? Required(IPropertySymbol p, AttributeData a)
	{
		var type = p.Type;
		var message = Message(a, p.Name, $"The {p.Name} field is required.");
		if (type.SpecialType == SpecialType.System_String)
		{
			var allowEmpty = NamedBool(a, "AllowEmptyStrings");
			return new Built("Required", allowEmpty ? "v => v is not null" : "v => !string.IsNullOrWhiteSpace(v)", message);
		}
		if (type.IsValueType)
		{
			return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
				? new Built("Required", "v => v.HasValue", message)
				: null;
		}
		return new Built("Required", "v => v is not null", message);
	}

	// ── [Range]

	private static Built? Range(IPropertySymbol p, AttributeData a)
	{
		var (underlying, nullable) = Underlying(p.Type);
		var floating = underlying.SpecialType is SpecialType.System_Double or SpecialType.System_Single;
		var integral = underlying.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Int16 or SpecialType.System_Byte
		or SpecialType.System_SByte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64 or SpecialType.System_Decimal;
		if ((!floating && !integral) || a.ConstructorArguments.Length != 2 || a.ConstructorArguments[0].Value is null || a.ConstructorArguments[1].Value is null)
		{
			return null;
		}
		if (a.ConstructorArguments[0].Value is string || a.ConstructorArguments[0].Type?.ToDisplayString() == "System.Type")
		{
			return null;
		}
		string? min;
		string? max;
		try
		{
			min = Literal(a.ConstructorArguments[0].Value!, floating);
			max = Literal(a.ConstructorArguments[1].Value!, floating);
		}
		catch (OverflowException)
		{
			return null;
		}
		var cast = floating ? "double" : "decimal";
		var lower = NamedBool(a, "MinimumIsExclusive") ? ">" : ">=";
		var upper = NamedBool(a, "MaximumIsExclusive") ? "<" : "<=";
		var value = nullable ? "n" : "v";
		var test = $"({cast}){value} {lower} {min} && ({cast}){value} {upper} {max}";
		var predicate = nullable ? $"v => v is not {{ }} n || ({test})" : $"v => {test}";
		var shownMin = Convert.ToString(a.ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
		var shownMax = Convert.ToString(a.ConstructorArguments[1].Value, CultureInfo.InvariantCulture);
		return new Built("Range", predicate, Message(a, p.Name, $"The field {p.Name} must be between {shownMin} and {shownMax}."));
	}

	private static string Literal(object value, bool floating)
	{
		if (!floating)
		{
			return Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "m";
		}
		var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
		if (double.IsNaN(number) || double.IsInfinity(number))
		{
			throw new OverflowException();
		}
		var text = number.ToString("R", CultureInfo.InvariantCulture);
		return text.IndexOfAny(['.', 'E', 'e']) < 0 ? text + "d" : text;
	}

	private static (ITypeSymbol Type, bool Nullable) Underlying(ITypeSymbol type) =>
		type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named ? (named.TypeArguments[0], true) : (type, false);

	// ── [StringLength], [MaxLength], [MinLength]

	private static Built? StringLength(IPropertySymbol p, AttributeData a)
	{
		if (p.Type.SpecialType != SpecialType.System_String || a.ConstructorArguments.Length != 1 || a.ConstructorArguments[0].Value is not int max)
		{
			return null;
		}
		var min = a.NamedArguments.Where(n => n.Key == "MinimumLength").Select(n => n.Value.Value is int i ? i : 0).FirstOrDefault();
		var test = min > 0 ? $"v.Length >= {min} && v.Length <= {max}" : $"v.Length <= {max}";
		var fallback = min > 0
			? $"The field {p.Name} must be a string with a minimum length of {min} and a maximum length of {max}."
			: $"The field {p.Name} must be a string with a maximum length of {max}.";
		return new Built("StringLength", $"v => v is null || ({test})", Message(a, p.Name, fallback));
	}

	private static Built? Length(IPropertySymbol p, AttributeData a, bool max)
	{
		if (p.Type.SpecialType != SpecialType.System_String || a.ConstructorArguments.Length != 1 || a.ConstructorArguments[0].Value is not int n || n < 0)
		{
			return null;
		}
		return max
			? new Built(
				"MaxLength",
				$"v => v is null || v.Length <= {n}",
				Message(a, p.Name, $"The field {p.Name} must be a string or array type with a maximum length of '{n}'.")
			)
			: new Built(
				"MinLength",
				$"v => v is null || v.Length >= {n}",
				Message(a, p.Name, $"The field {p.Name} must be a string or array type with a minimum length of '{n}'.")
			);
	}

	// ── [RegularExpression]: the whole string must match, and an empty string passes (it is [Required]'s business)

	private static Built? Pattern(IPropertySymbol p, AttributeData a, System.Collections.Generic.List<string> fields)
	{
		if (p.Type.SpecialType != SpecialType.System_String || a.ConstructorArguments.Length != 1 || a.ConstructorArguments[0].Value is not string pattern)
		{
			return null;
		}
		var timeout = a.NamedArguments.Where(n => n.Key == "MatchTimeoutInMilliseconds").Select(n => n.Value.Value is int i ? i : 0).FirstOrDefault();
		var field = "__annotation_" + p.Name + "_Pattern";
		var args = timeout > 0
			? $"{Quote(pattern)}, global::System.Text.RegularExpressions.RegexOptions.None, global::System.TimeSpan.FromMilliseconds({timeout})"
			: Quote(pattern);
		fields.Add($"private static readonly global::System.Text.RegularExpressions.Regex {field} = new({args});");
		return new Built(
			"Pattern",
			$"v => v is null || v.Length == 0 || {field}.Match(v) is {{ Success: true, Index: 0 }} m && m.Length == v.Length",
			Message(a, p.Name, $"The field {p.Name} must match the regular expression '{pattern}'.")
		);
	}

	public static string Quote(string value)
	{
		var sb = new StringBuilder("\"");
		foreach (var c in value)
		{
			switch (c)
			{
				case '\\':
					sb.Append("\\\\");
					break;
				case '"':
					sb.Append("\\\"");
					break;
				case '\n':
					sb.Append("\\n");
					break;
				case '\r':
					sb.Append("\\r");
					break;
				case '\t':
					sb.Append("\\t");
					break;
				case '\0':
					sb.Append("\\0");
					break;
				default:
					if (char.IsControl(c) || (int)c is 0x2028 or 0x2029)
					{
						sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
					}
					else
					{
						sb.Append(c);
					}
					break;
			}
		}
		return sb.Append('"').ToString();
	}
}
