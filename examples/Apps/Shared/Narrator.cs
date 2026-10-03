// Linked into every app (not a project of its own): prints what a scenario does and records whether it behaved as narrated,
// so a run of the app is also a check, and `./build.sh apps` fails when one stops behaving.

namespace Apps;

internal static class Narrator
{
	private static readonly List<string> Failures = [];

	public static void Section(string title)
	{
		Console.WriteLine();
		Console.WriteLine($"== {title}");
	}

	public static void Say(string text) => Console.WriteLine($"   {text}");

	public static void Expect(bool condition, string what)
	{
		Console.WriteLine($"   {(condition ? "ok  " : "FAIL")} {what}");
		if (!condition)
		{
			Failures.Add(what);
		}
	}

	/// <summary>The process exit code: 0 when everything behaved as narrated.</summary>
	public static int Finish()
	{
		Console.WriteLine();
		Console.WriteLine(Failures.Count == 0 ? "All scenarios behaved as narrated." : $"{Failures.Count} scenario(s) did not behave as narrated:");
		foreach (var failure in Failures)
		{
			Console.WriteLine($"  - {failure}");
		}
		return Failures.Count == 0 ? 0 : 1;
	}
}
