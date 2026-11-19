// Drives ConstraintGenerator directly through CSharpGeneratorDriver against a hand-built
// Compilation — no MSBuild, no analyzer pipeline. References every currently-trusted platform
// assembly (the standard way to get a working BCL reference set for an in-memory Roslyn
// compilation in tests) plus the real Nullean.Entities/Nullean.Entities.Reactive assemblies, so a
// test snippet can use the real Constraint<T>/ReactiveConstraint<T>/IHaveConstraints<T> types.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Nullean.Entities.SourceGenerators;

namespace Nullean.Entities.SourceGenerators.Tests;

internal static class GeneratorTestHost
{
	private static readonly ImmutableArray<MetadataReference> PlatformReferences =
	[
		.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
			.Split(Path.PathSeparator)
			.Where(path => path.EndsWith(".dll", StringComparison.Ordinal))
			.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
	];

	// Compiles `librarySource` to an in-memory assembly and runs the generator over `source` with it referenced as metadata,
	// so members carry what a compiler emits into an assembly (IsImplicitlyDeclared is false for those).
	public static GeneratorRun RunWithLibrary(string librarySource, string source)
	{
		var lib = CSharpCompilation.Create(
			"GeneratorTestsLib",
			[CSharpSyntaxTree.ParseText(librarySource)],
			[.. PlatformReferences, MetadataReference.CreateFromFile(typeof(Constraint<object>).Assembly.Location)],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
		);
		using var stream = new MemoryStream();
		var emit = lib.Emit(stream);
		if (!emit.Success)
			throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
		return Run(source, MetadataReference.CreateFromImage(stream.ToArray()));
	}

	public static GeneratorRun Run(string source, params MetadataReference[] extra)
	{
		ImmutableArray<MetadataReference> references =
		[
			.. PlatformReferences,
			MetadataReference.CreateFromFile(typeof(Constraint<object>).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(Reactive.ReactiveConstraint<object>).Assembly.Location),
			.. extra,
		];

		var compilation = CSharpCompilation.Create(
			"GeneratorTests",
			[CSharpSyntaxTree.ParseText(source)],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
		);

		var driver = CSharpGeneratorDriver.Create(new ConstraintGenerator());
		driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

		// Diagnostics from the generator only — outputCompilation's own compile errors (e.g. a test
		// snippet with a typo) would otherwise drown out the one diagnostic a test cares about.
		ImmutableArray<(string HintName, string Source)> generated =
		[
			.. driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources).Select(s => (s.HintName, Source: s.SourceText.ToString())),
		];
		return new GeneratorRun(generatorDiagnostics, generated, outputCompilation);
	}
}

internal sealed record GeneratorRun(
	ImmutableArray<Diagnostic> Diagnostics,
	ImmutableArray<(string HintName, string Source)> Generated,
	Compilation OutputCompilation
)
{
	public bool HasDiagnostic(string id) => Diagnostics.Any(d => d.Id == id);

	public string SingleGeneratedSource() => Generated.Single().Source;
}
