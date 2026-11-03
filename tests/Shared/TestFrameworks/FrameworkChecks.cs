using System.Reflection;
using AwesomeAssertions;

namespace Nullean.Entities.TestFrameworks;

// What every framework project asserts about itself, by running itself again: the framework lists one test per axis, and an auto-test broken on one
// axis fails that test and only that one.
public static class FrameworkChecks
{
	// `--list-tests` is Microsoft.Testing.Platform's own option, so it is the same for every framework; `filter` selects the test class with the framework's own syntax.
	public static async Task ListsOneTestPerAxisAsync(Assembly tests, params string[] filter)
	{
		var (exitCode, output) = await FrameworkRun.RunAsync(tests, broken: false, ["--list-tests", .. filter]);
		exitCode.Should().Be(0, output);
		var listed = output.Split('\n').Select(l => l.Trim()).Where(l => l.Contains("EveryDeclaredAxisHolds", StringComparison.Ordinal)).ToList();
		listed.Should().HaveCount(OrderAxes.CaseNames.Count, $"the framework should list one test per axis:\n{output}");
		foreach (var name in OrderAxes.CaseNames)
		{
			listed.Should().ContainSingle(l => l.Contains(Quoted(name), StringComparison.Ordinal), $"the framework should list a test for {name}:\n{output}");
		}
	}

	// `filter` selects the broken test class with the framework's own filter syntax.
	public static async Task ABrokenAxisFailsOnlyItsOwnTestAsync(Assembly tests, params string[] filter)
	{
		var (exitCode, output) = await FrameworkRun.RunAsync(tests, broken: true, filter);
		exitCode.Should().NotBe(0, output);
		var cases = OrderAxes.CaseNames.Count;
		output.Should().Contain($"total: {cases}").And.Contain("failed: 1").And.Contain($"succeeded: {cases - 1}");
		output
			.Split('\n')
			.Where(l => l.StartsWith("failed ", StringComparison.Ordinal))
			.Should()
			.ContainSingle()
			.Which
			.Should()
			.Contain(Quoted("Monotonicity"));
		output.Should().Contain("version-never-goes-back").And.Contain("Reset()");
	}

	private static string Quoted(string name) => $"\"{name}\"";
}
