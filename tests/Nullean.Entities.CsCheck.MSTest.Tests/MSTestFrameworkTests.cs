using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nullean.Entities.CsCheck;

[assembly: Parallelize(Scope = ExecutionScope.ClassLevel)]

namespace Nullean.Entities.TestFrameworks.MSTest;

// The same test over an auto-test FrameworkRun breaks on one axis in a child run. Green in a normal run.
[TestClass]
public class MSTestBrokenAxisTests
{
	public static IEnumerable<string> Axes() => OrderAxes.MaybeBroken().CaseNames;

	[TestMethod]
	[DynamicData(nameof(Axes))]
	public Task EveryDeclaredAxisHolds(string axis) => OrderAxes.MaybeBroken().Case(axis).RunAsync();
}

[TestClass]
public class MSTestDiscoveryTests
{
	[TestMethod]
	public Task Lists_one_test_per_axis() =>
		FrameworkChecks.ListsOneTestPerAxisAsync(typeof(MSTestAxesTests).Assembly, "--filter", $"FullyQualifiedName~{nameof(MSTestAxesTests)}");

	[TestMethod]
	public Task A_broken_axis_fails_only_its_own_test() =>
		FrameworkChecks.ABrokenAxisFailsOnlyItsOwnTestAsync(typeof(MSTestAxesTests).Assembly, "--filter", $"FullyQualifiedName~{nameof(MSTestBrokenAxisTests)}");
}
