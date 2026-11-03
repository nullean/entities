using Nullean.Entities.CsCheck;
using NUnit.Framework;

namespace Nullean.Entities.TestFrameworks.NUnit;

// The same test over an auto-test FrameworkRun breaks on one axis in a child run. Green in a normal run.
public class NUnitBrokenAxisTests
{
	public static IEnumerable<string> Axes() => OrderAxes.MaybeBroken().CaseNames;

	[TestCaseSource(nameof(Axes))]
	public Task EveryDeclaredAxisHolds(string axis) => OrderAxes.MaybeBroken().Case(axis).RunAsync();
}

public class NUnitDiscoveryTests
{
	[Test]
	public Task Lists_one_test_per_axis() =>
		FrameworkChecks.ListsOneTestPerAxisAsync(typeof(NUnitAxesTests).Assembly, "--filter", $"FullyQualifiedName~{nameof(NUnitAxesTests)}");

	[Test]
	public Task A_broken_axis_fails_only_its_own_test() =>
		FrameworkChecks.ABrokenAxisFailsOnlyItsOwnTestAsync(typeof(NUnitAxesTests).Assembly, "--filter", $"FullyQualifiedName~{nameof(NUnitBrokenAxisTests)}");
}
