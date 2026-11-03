using System.Diagnostics;
using System.Reflection;

namespace Nullean.Entities.TestFrameworks;

// Runs this test assembly again as a child process (every project here is a Microsoft.Testing.Platform executable), so a test can look at what the
// framework itself discovered and reported: the number of tests listed, and which ones failed.
public static class FrameworkRun
{
	public static async Task<(int ExitCode, string Output)> RunAsync(Assembly tests, bool broken, params string[] arguments)
	{
		var host = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
		var start = new ProcessStartInfo(host) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
		// `dotnet exec x.dll` and the app host both work; under `dotnet run` the process is the app host, under `dotnet test` it may be dotnet itself.
		if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
		{
			start.ArgumentList.Add("exec");
			start.ArgumentList.Add(tests.Location);
		}
		foreach (var argument in arguments)
		{
			start.ArgumentList.Add(argument);
		}
		start.Environment[OrderAxes.BrokenVariable] = broken ? "1" : "0";
		// a child that reports to GitHub Actions would annotate the PR with the failure this run causes on purpose
		start.Environment.Remove("GITHUB_ACTIONS");
		using var process = Process.Start(start) ?? throw new InvalidOperationException($"could not start {host}");
		var stdout = process.StandardOutput.ReadToEndAsync();
		var stderr = process.StandardError.ReadToEndAsync();
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
		try
		{
			await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			process.Kill(entireProcessTree: true);
			throw new TimeoutException("the child test run did not finish in five minutes");
		}
		return (process.ExitCode, await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
	}
}
