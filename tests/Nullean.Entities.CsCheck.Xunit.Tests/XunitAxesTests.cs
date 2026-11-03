using Nullean.Entities.CsCheck;
using Nullean.Entities.Quickstart;
using Xunit;

namespace Nullean.Entities.TestFrameworks.Xunit;

// The one-liner docs/integration/test-frameworks.md shows: one theory row per axis the rules declare.
public class XunitAxesTests
{
	public static AutoTest<Order> Auto() => AutoTest.For(OrderConstraints.Instance, OrderAxes.Orders);

	public static TheoryData<string> Axes() => [.. Auto().CaseNames];

	[Theory]
	[MemberData(nameof(Axes))]
	public Task EveryDeclaredAxisHolds(string axis) => Auto().Case(axis).RunAsync();
}
