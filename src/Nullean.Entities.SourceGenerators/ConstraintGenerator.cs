// Nullean.Entities.SourceGenerators — the [Constraint] generator.
//
// Finds every static field/property tagged [Constraint], groups them by containing type, and
// bundles each group into one generated `Constraints` property:
//
//   - Constraint<T> members where the containing type IS T get IHaveConstraints<T> added to the
//     same partial declaration, so `MyType.Constraints` and `events.AttachConstraints()` both
//     just work — the "domain type declares its own rules" shape.
//   - Constraint<T> members on any other partial type (a companion "spec" class, T untouched)
//     get a plain `ConstraintSet<T> Constraints` property with no interface — useful when T isn't
//     owned by this repo, or the author simply prefers rules declared alongside the type instead
//     of on it.
//   - ReactiveConstraint<T> members get a `ReactiveConstraintSet<T> Constraints` property built
//     from `ReactiveConstraintSet.From((nameof(field), field), ...)`. There is no reactive
//     equivalent of IHaveConstraints<T> to attach to, so this case never adds an interface.
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

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var members = context.SyntaxProvider.ForAttributeWithMetadataName(
			ConstraintAttributeMetadataName,
			predicate: static (node, _) => node is VariableDeclaratorSyntax or PropertyDeclarationSyntax,
			transform: static (ctx, ct) => Analyze(ctx, ct)
		);

		context.RegisterSourceOutput(members.Collect(), static (spc, all) => Generate(spc, all));
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
		var baseList = selfAttach ? $" : global::Nullean.Entities.IHaveConstraints<{type.Name}>" : "";

		return Emit(
			type,
			baseList,
			$"global::Nullean.Entities.ConstraintSet<{typeArgDisplay}>",
			"global::Nullean.Entities.ConstraintSet.From(",
			members.Select(m => m.MemberName)
		);
	}

	private static string GenerateReactive(INamedTypeSymbol type, ITypeSymbol typeArgument, ImmutableArray<MemberInfo> members)
	{
		var typeArgDisplay = typeArgument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

		return Emit(
			type,
			baseList: "",
			$"global::Nullean.Entities.Reactive.ReactiveConstraintSet<{typeArgDisplay}>",
			"global::Nullean.Entities.Reactive.ReactiveConstraintSet.From(",
			members.Select(m => $"(nameof({m.MemberName}), {m.MemberName})")
		);
	}

	private static string Emit(INamedTypeSymbol type, string baseList, string propertyType, string factoryCall, IEnumerable<string> args)
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
		sb.AppendLine($"{indent}    public static {propertyType} Constraints {{ get; }} =");
		sb.AppendLine($"{indent}        {factoryCall}");
		sb.AppendLine($"{indent}            {argList}");
		sb.AppendLine($"{indent}        );");
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
