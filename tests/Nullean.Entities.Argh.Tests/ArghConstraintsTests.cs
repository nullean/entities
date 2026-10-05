using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Nullean.Argh;
using Nullean.Argh.Builder;
using Nullean.Argh.Runtime;

namespace Nullean.Entities.Argh.Tests;

public sealed partial record ServeOptions(string Host, int Port)
{
	[Constraint]
	public static readonly Constraint<ServeOptions> PortInRange = Constraints<ServeOptions>.Check(
		o => o.Port is > 0 and <= 65535 ? [] : [Violation.Error(nameof(Port), "must be between 1 and 65535")]
	);
}

internal static class Handlers
{
	public static int Ran;

	public static void Serve([AsParameters("serve")] ServeOptions options) => Ran += options is null ? 0 : 1;
}

internal static class Registration
{
	[ModuleInitializer]
	internal static void Register()
	{
		ArghConstraints.Register<ServeOptions>();
		var app = new ArghBuilder();
		app.Map("serve", Handlers.Serve);
	}
}

// Handlers.Ran and Console.Error are process-wide, so these tests cannot overlap.
[NotInParallel]
public class ArghConstraintsTests
{
	// TUnit owns Console.Error and records what is written to it per test, so read that back rather than swapping the writer.
	private static async Task<(int Code, string Err)> Run(params string[] args)
	{
		var code = await ArghRuntime.RunAsync(args);
		return (code, TestContext.Current!.GetErrorOutput());
	}

	[Test]
	public async Task A_violation_is_reported_against_the_flag_and_exits_2_without_running_the_handler()
	{
		Handlers.Ran = 0;

		var (code, err) = await Run("serve", "--serve-host", "localhost", "--serve-port", "70000");

		code.Should().Be(2);
		err.Should().Contain("Error: --serve-port: must be between 1 and 65535");
		err.Should().Contain("for usage.");
		Handlers.Ran.Should().Be(0);
	}

	[Test]
	public async Task A_valid_command_runs()
	{
		Handlers.Ran = 0;

		var (code, _) = await Run("serve", "--serve-host", "localhost", "--serve-port", "8080");

		code.Should().Be(0);
		Handlers.Ran.Should().Be(1);
	}
}
