using Nullean.Entities.CsCheck;
using Xunit;

namespace Nullean.Entities.TestFrameworks.Xunit;

// The same theory over an auto-test FrameworkRun breaks on one axis in a child run. Green in a normal run.
public class XunitBrokenAxisTests
{
	public static TheoryData<string> Axes() => [.. OrderAxes.MaybeBroken().CaseNames];

	[Theory]
	[MemberData(nameof(Axes))]
	public Task EveryDeclaredAxisHolds(string axis) => OrderAxes.MaybeBroken().Case(axis).RunAsync();
}

public class XunitDiscoveryTests
{
	[Fact]
	public Task Lists_one_test_per_axis() =>
		FrameworkChecks.ListsOneTestPerAxisAsync(typeof(XunitAxesTests).Assembly, "--filter-class", typeof(XunitAxesTests).FullName!);

	[Fact]
	public Task A_broken_axis_fails_only_its_own_test() =>
		FrameworkChecks.ABrokenAxisFailsOnlyItsOwnTestAsync(typeof(XunitAxesTests).Assembly, "--filter-class", typeof(XunitBrokenAxisTests).FullName!);
}
