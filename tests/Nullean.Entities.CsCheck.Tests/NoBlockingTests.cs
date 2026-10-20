using System.Text.RegularExpressions;

namespace Nullean.Entities.CsCheck.Tests;

public partial class NoBlockingTests
{
	/// <summary>
	/// Law: no library, test or example source blocks on a task (<c>.GetAwaiter().GetResult()</c>, <c>.Result</c>, <c>.Wait()</c>).
	/// Consequence if broken: a test run deadlocks or starves the thread pool under a synchronisation context, and an exception arrives wrapped in an AggregateException.
	/// </summary>
	// properties named Result that are not tasks: ActionExecutingContext.Result, ReachMeter.Result and a transfer record's Result
	private static readonly string[] NotATask = ["context.Result =", "reach.Result", "t.Result.IsOk"];

	[Test]
	public async Task Library_source_never_blocks_on_a_task()
	{
		var root = RepositoryRoot();
		var offenders = new[] { "src", "tests", "examples" }
			.SelectMany(dir => Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
			.Where(
				f =>
					!f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && Path.GetFileName(f) != nameof(
						NoBlockingTests
					) + ".cs"
			)
			.SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
			.Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && Blocking().IsMatch(l.Text) && !NotATask.Any(l.Text.Contains))
			.Select(l => $"{Path.GetRelativePath(root, l.File)}:{l.Line}: {l.Text.Trim()}")
			.ToList();

		await Assert.That(offenders).IsEmpty();
	}

	[GeneratedRegex(@"\.GetAwaiter\(\)\s*\.GetResult\(\)|\.Result\b(?!\w)|\.Wait\(\)")]
	private static partial Regex Blocking();

	// Found from where the test runs, not from the path the compiler recorded: a CI build maps source paths to "/_/", so [CallerFilePath] names a folder that does not exist there.
	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "nullean-entities.slnx")))
		{
			directory = directory.Parent;
		}
		return directory?.FullName ?? throw new InvalidOperationException("nullean-entities.slnx was not found above the test's folder");
	}
}
