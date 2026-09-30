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

	public static GeneratorRun Run(string source)
	{
		ImmutableArray<MetadataReference> references =
		[
			.. PlatformReferences,
			MetadataReference.CreateFromFile(typeof(Constraint<object>).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(Reactive.ReactiveConstraint<object>).Assembly.Location),
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
