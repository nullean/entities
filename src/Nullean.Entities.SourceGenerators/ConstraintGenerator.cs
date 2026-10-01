// Nullean.Entities.SourceGenerators — the [Constraint] generator.
//
// Finds every static field/property tagged [Constraint], groups them by containing type, and
// bundles each group into one generated `All` property:
//
//   - Constraint<T> members where the containing type IS T get IHaveConstraints<T> added to the
//     same partial declaration, so `MyType.Constraints` (not `All` — that name is for companion
//     classes, where `OrderConstraints.All` reads right and `OrderConstraints.Constraints` would
//     stutter) and `events.AttachConstraints()` both
//     just work — the "domain type declares its own rules" shape. The property is built from
//     `ConstraintSet.From((nameof(field), field), ...)`, so a constraint's name can never drift
//     from the field that declares it.
//   - Constraint<T> members on any other partial type (a companion "spec" class, T untouched) get
//     the same plain `ConstraintSet<T> All` property, plus a nested
//     `TypeConstraints : IConstraintsFor<T>` struct with a singleton `Instance` — never on the spec
//     class's own base list, even when it isn't `static`, because IConstraintsFor<T> is a plain
//     instance interface: `SpecClass.TypeConstraints.Instance` is a value a caller can pass around
//     or infer T from, which putting the interface directly on the (usually static) spec class
//     could never give you. A forwarding `public static readonly TypeConstraints Instance` on the
//     spec class itself means a caller never has to spell out `TypeConstraints` at all —
//     `SpecClass.Instance` is the same value as `SpecClass.TypeConstraints.Instance`.
//   - ReactiveConstraint<T> members get the same treatment with
//     `ReactiveConstraintSet<T> All` / a nested `TypeConstraints : IReactiveConstraintsFor<T>`
//     — there is no reactive-native self-attach interface (self-attach for reactive flows through
//     the sync core's IHaveConstraints<T> + ToReactive() instead), so every declarer goes through
//     this one path.
//
// A type with ANY invalid [Constraint] member (not static, wrong type, mixed kinds, not
// partial, nested, generic, or an inconsistent type argument) gets no generated property at all —
// see Diagnostics.cs for why a partial, silently-incomplete set is treated as worse than a
// compile error.

using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Nullean.Entities.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public sealed class ConstraintGenerator : IIncrementalGenerator
{
	private const string ConstraintAttributeMetadataName = "Nullean.Entities.ConstraintAttribute";
	private const string ConstraintsAttributeMetadataName = "Nullean.Entities.ConstraintsAttribute";
	private const string ConstraintIgnoreAttributeMetadataName = "Nullean.Entities.ConstraintIgnoreAttribute";

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var explicitMembers = context.SyntaxProvider.ForAttributeWithMetadataName(
			ConstraintAttributeMetadataName,
			predicate: static (node, _) => node is VariableDeclaratorSyntax or PropertyDeclarationSyntax,
			transform: static (ctx, ct) => Analyze(ctx, ct)
		);

		// [Constraints] on the type itself: every eligible static member is discovered by
		// reflecting over the type's own symbol rather than requiring each one to carry
		// [Constraint] — one MemberInfo array per tagged type, flattened below so it can be merged
		// with explicitMembers and fed through the exact same grouping/validation path.
		var autoDiscoveredMembers = context.SyntaxProvider.ForAttributeWithMetadataName(
			ConstraintsAttributeMetadataName,
			predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
			transform: static (ctx, ct) => AnalyzeAutoType(ctx, ct)
		);

		var merged = explicitMembers.Collect().Combine(autoDiscoveredMembers.Collect()).Select(static (pair, _) => Merge(pair.Left, pair.Right));

		context.RegisterSourceOutput(merged, static (spc, all) => Generate(spc, all));
	}

	private static MemberInfo Analyze(GeneratorAttributeSyntaxContext ctx, CancellationToken _)
	{
		var symbol = ctx.TargetSymbol;
		var (memberType, isStatic) = symbol switch
		{
			IFieldSymbol f => (f.Type, f.IsStatic),
			IPropertySymbol p => (p.Type, p.IsStatic),
			_ => (null!, false),
		};
		return new MemberInfo(symbol.ContainingType!, memberType, symbol.Name, isStatic, ctx.TargetNode.GetLocation());
	}

	/// <summary>
	/// For a [Constraints]-tagged type, every static field/property declared directly on it whose
	/// type is Constraint&lt;T&gt;/ReactiveConstraint&lt;TEvent&gt; is eligible — found by walking
	/// the type's own members rather than requiring [Constraint] on each one. [ConstraintIgnore]
	/// excludes an otherwise-eligible member; anything whose type doesn't match (TryClassify
	/// returns false) is silently not a candidate at all, the same as it simply never being tagged
	/// under the per-member opt-in — there is no "wrong type" diagnostic to raise here, since
	/// nothing asked for this specific member to be included.
	/// </summary>
	private static ImmutableArray<MemberInfo> AnalyzeAutoType(GeneratorAttributeSyntaxContext ctx, CancellationToken _)
	{
		if (ctx.TargetSymbol is not INamedTypeSymbol type)
		{
			return [];
		}

		return [
			.. type
				.GetMembers()
				.Where(m => m is IFieldSymbol or IPropertySymbol && m.IsStatic)
				.Where(m => !m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ConstraintIgnoreAttributeMetadataName))
				.Select(
					m =>
						(Member: m, MemberType: m switch
						{
							IFieldSymbol f => f.Type,
							IPropertySymbol p => p.Type,
							_ => null!
						})
				)
				.Where(t => TryClassify(t.MemberType, out var unusedKind, out var unusedTypeArgument))
				.Select(t => new MemberInfo(type, t.MemberType, t.Member.Name, true, t.Member.Locations.FirstOrDefault() ?? Location.None)),
		];
	}

	/// <summary>
	/// A type read via [Constraints] takes over entirely for that type: its explicit [Constraint]
	/// members (if any — redundant, not an error) are dropped in favour of the auto-discovered
	/// list, so the same member is never counted twice and only one source file is ever emitted per
	/// type. A type with no [Constraints] tag is unaffected, using explicitMembers exactly as before.
	/// One consequence: a stray [Constraint] on a member whose type doesn't qualify (a mistake, on
	/// a type that also carries [Constraints]) is dropped silently rather than diagnosed — it was
	/// never going to be auto-discovered either, and [Constraint] is simply inert once [Constraints]
	/// is present on the containing type.
	/// </summary>
	private static ImmutableArray<MemberInfo> Merge(ImmutableArray<MemberInfo> explicitMembers, ImmutableArray<ImmutableArray<MemberInfo>> autoDiscovered)
	{
		var autoTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
		foreach (var group in autoDiscovered)
		{
			if (group.Length > 0)
			{
				autoTypes.Add(group[0].ContainingType);
			}
		}

		return [.. explicitMembers.Where(m => !autoTypes.Contains(m.ContainingType)), .. autoDiscovered.SelectMany(g => g),];
	}

	private static void Generate(SourceProductionContext spc, ImmutableArray<MemberInfo> members)
	{
		// The explicit <MemberInfo, INamedTypeSymbol> is required: SymbolEqualityComparer.Default's
		// static type only implements IEqualityComparer<ISymbol>, and contravariance lets that
		// satisfy IEqualityComparer<TKey> for any TKey : ISymbol — so without pinning TKey here,
		// overload resolution infers TKey as ISymbol instead of INamedTypeSymbol.
		foreach (var group in members.GroupBy<MemberInfo, INamedTypeSymbol>(m => m.ContainingType, SymbolEqualityComparer.Default))
		{
			var containingType = group.Key;
			var groupMembers = group.ToImmutableArray();
			var anchor = groupMembers[0].Location;

			if (containingType.ContainingType is not null)
			{
				spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.NestedTypeNotSupported, anchor, containingType.Name));
				continue;
			}
			if (containingType.TypeParameters.Length > 0)
			{
				spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.GenericTypeNotSupported, anchor, containingType.Name));
				continue;
			}
			if (!IsDeclaredPartial(containingType))
			{
				spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.TypeMustBePartial, anchor, containingType.Name));
				continue;
			}

			var valid = new List<(MemberInfo Member, ConstraintKind Kind, ITypeSymbol TypeArgument)>();
			var hadMemberError = false;
			foreach (var member in groupMembers)
			{
				if (!member.IsStatic)
				{
					spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MemberMustBeStatic, member.Location, member.MemberName));
					hadMemberError = true;
					continue;
				}
				if (!TryClassify(member.MemberType, out var memberKind, out var memberTypeArgument))
				{
					spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedMemberType, member.Location, member.MemberName));
					hadMemberError = true;
					continue;
				}
				valid.Add((member, memberKind, memberTypeArgument));
			}
			if (valid.Count == 0)
			{
				continue;
			}

			var kinds = valid.Select(v => v.Kind).Distinct().ToArray();
			if (kinds.Length > 1)
			{
				spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.MixedConstraintKinds, anchor, containingType.Name));
				continue;
			}

			// Same reasoning as the GroupBy above: pin the type argument explicitly rather than
			// let overload resolution infer it from SymbolEqualityComparer.Default's own static type.
			var typeArgs = valid.Select(v => v.TypeArgument).Distinct<ITypeSymbol>(SymbolEqualityComparer.Default).ToArray();
			if (typeArgs.Length > 1)
			{
				spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.InconsistentTypeArgument, anchor, containingType.Name));
				continue;
			}

			if (hadMemberError)
			{
				// Some members on this type are invalid. Exhaustiveness is the entire point of
				// [Constraint] — generating a Constraints set from only the valid subset while an
				// invalid member silently isn't included would itself be exactly the kind of gap
				// the attribute exists to prevent. Refuse until every member on the type is fixed.
				continue;
			}

			var kind = kinds[0];
			var typeArgument = typeArgs[0];
			var orderedMembers = valid.Select(v => v.Member).ToImmutableArray();
			var source = kind == ConstraintKind.Sync
				? GenerateSync(containingType, typeArgument, orderedMembers)
				: GenerateReactive(containingType, typeArgument, orderedMembers);

			spc.AddSource(HintName(containingType), SourceText.From(source, Encoding.UTF8));
		}
	}

	private static bool IsDeclaredPartial(INamedTypeSymbol type) =>
		type.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>().All(t => t.Modifiers.Any(SyntaxKind.PartialKeyword));

	private static bool TryClassify(ITypeSymbol memberType, out ConstraintKind kind, out ITypeSymbol typeArgument)
	{
		kind = default;
		typeArgument = null!;
		if (memberType is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named)
		{
			return false;
		}

		var original = named.OriginalDefinition;
		var ns = original.ContainingNamespace?.ToDisplayString();
		if (original.MetadataName == "Constraint`1" && ns == "Nullean.Entities")
		{
			kind = ConstraintKind.Sync;
			typeArgument = named.TypeArguments[0];
			return true;
		}
		if (original.MetadataName == "ReactiveConstraint`1" && ns == "Nullean.Entities.Reactive")
		{
			kind = ConstraintKind.Reactive;
			typeArgument = named.TypeArguments[0];
			return true;
		}
		return false;
	}

	private static string GenerateSync(INamedTypeSymbol type, ITypeSymbol typeArgument, ImmutableArray<MemberInfo> members)
	{
		var selfAttach = SymbolEqualityComparer.Default.Equals(type, typeArgument);
		var typeArgDisplay = typeArgument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		var propertyType = $"global::Nullean.Entities.ConstraintSet<{typeArgDisplay}>";
		// Self-attach (the type IS T) gets the F-bounded, static-abstract IHaveConstraints<T>
		// directly on its own base list — a domain record is never static, so this is always a
		// plain base-list addition. A companion "spec" class (T untouched) gets a nested
		// TypeConstraints struct instead — see NestedTypeConstraints for why it's never on the spec
		// class's own base list, even when the class isn't `static`.
		var (baseList, nested) = selfAttach
			? ($" : global::Nullean.Entities.IHaveConstraints<{type.Name}>", null)
			: (string.Empty, NestedTypeConstraints(type, "global::Nullean.Entities.IConstraintsFor", typeArgDisplay, propertyType));

		return Emit(
			type,
			baseList,
			propertyType,
			// Self-attach is read as `Order.Constraints.Apply(...)`; a companion spec class is
			// `OrderConstraints.All` — "OrderConstraints.Constraints" would stutter.
			selfAttach ? "Constraints" : "All",
			"global::Nullean.Entities.ConstraintSet.From(",
			members.Select(m => $"(nameof({m.MemberName}), {m.MemberName})"),
			nested
		);
	}

	private static string GenerateReactive(INamedTypeSymbol type, ITypeSymbol typeArgument, ImmutableArray<MemberInfo> members)
	{
		var typeArgDisplay = typeArgument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		var propertyType = $"global::Nullean.Entities.Reactive.ReactiveConstraintSet<{typeArgDisplay}>";
		// No reactive-native self-attach interface (see ReactiveConstraints.cs), so every declarer
		// goes through the same nested TypeConstraints path as the sync companion case.
		var nested = NestedTypeConstraints(type, "global::Nullean.Entities.Reactive.IReactiveConstraintsFor", typeArgDisplay, propertyType);

		return Emit(
			type,
			baseList: "",
			propertyType,
			"All",
			"global::Nullean.Entities.Reactive.ReactiveConstraintSet.From(",
			members.Select(m => $"(nameof({m.MemberName}), {m.MemberName})"),
			nested
		);
	}

	/// <summary>
	/// A zero-size <c>TypeConstraints</c> struct nested in the spec class, implementing the given
	/// plain instance <c>I...ConstraintsFor&lt;T&gt;</c> interface and exposing a singleton
	/// <c>Instance</c>. Never placed on the spec class's own base list — the interface is an
	/// instance member, and the spec class's `All` is `static`, so the spec class itself
	/// cannot be made to implement the interface without either an unwanted instantiation or (for
	/// the usual `static class` shape) CS0714. Nesting means `SpecClass.TypeConstraints.Instance` is
	/// always available regardless of how the spec class itself is declared. <see cref="Emit"/>
	/// additionally forwards a plain `Instance` field onto the spec class itself, so a caller can
	/// write `SpecClass.Instance` without ever needing to know `TypeConstraints` exists.
	/// </summary>
	private static string NestedTypeConstraints(INamedTypeSymbol type, string interfaceName, string typeArgDisplay, string propertyType) =>
		$"public readonly struct TypeConstraints : {interfaceName}<{typeArgDisplay}>\n" + $"    {{\n" +
		$"        public static readonly TypeConstraints Instance = default;\n" + $"\n" +
		$"        public {propertyType} All => {type.Name}.All;\n" + $"    }}";

	private static string Emit(
		INamedTypeSymbol type,
		string baseList,
		string propertyType,
		string propertyName,
		string factoryCall,
		IEnumerable<string> args,
		string? nestedType = null
	)
	{
		var ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
		var typeKind = TypeKindKeyword(type);
		var indent = ns is null ? "" : "    ";
		// A literal "\n", not Environment.NewLine (RS1035): a generator's output has to be
		// deterministic across OSes, and Environment.NewLine is exactly the kind of
		// environment-dependent read that would break that.
		var argList = string.Join($",\n{indent}            ", args);

		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("// Generated by Nullean.Entities.SourceGenerators from every [Constraint] member declared");
		sb.AppendLine("// on this type. Adding one there updates this set automatically — there is no separate");
		sb.AppendLine("// list to keep in sync, and no drift to catch in review.");
		sb.AppendLine("#nullable enable");
		sb.AppendLine();
		if (ns is not null)
		{
			sb.AppendLine($"namespace {ns}");
			sb.AppendLine("{");
		}
		sb.AppendLine($"{indent}partial {typeKind} {type.Name}{baseList}");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{indent}    public static {propertyType} {propertyName} {{ get; }} =");
		sb.AppendLine($"{indent}        {factoryCall}");
		sb.AppendLine($"{indent}            {argList}");
		sb.AppendLine($"{indent}        );");
		if (nestedType is not null)
		{
			sb.AppendLine();
			sb.AppendLine($"{indent}    {nestedType}");
			sb.AppendLine();
			// Forwarding shortcut so a caller never has to spell out "TypeConstraints" — see
			// NestedTypeConstraints's own doc comment for why the nested struct exists at all.
			sb.AppendLine($"{indent}    public static readonly TypeConstraints Instance = TypeConstraints.Instance;");
		}
		sb.AppendLine($"{indent}}}");
		if (ns is not null)
		{
			sb.AppendLine("}");
		}
		return sb.ToString();
	}

	private static string TypeKindKeyword(INamedTypeSymbol type) => type switch
	{
		{ IsRecord: true, IsValueType: true } => "record struct",
		{ IsRecord: true } => "record",
		{ IsValueType: true } => "struct",
		_ => "class",
	};

	private static string HintName(INamedTypeSymbol type) =>
		$"{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "")}.Constraints.g.cs";

	private enum ConstraintKind
	{
		Sync,
		Reactive,
	}

	private sealed record MemberInfo(INamedTypeSymbol ContainingType, ITypeSymbol MemberType, string MemberName, bool IsStatic, Location Location);
}
