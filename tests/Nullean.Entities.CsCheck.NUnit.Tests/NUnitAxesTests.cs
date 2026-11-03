using Nullean.Entities.CsCheck;
using Nullean.Entities.Quickstart;
using NUnit.Framework;

namespace Nullean.Entities.TestFrameworks.NUnit;

// The one-liner docs/integration/test-frameworks.md shows: one test case per axis the rules declare.
public class NUnitAxesTests
{
	public static AutoTest<Order> Auto() => AutoTest.For(OrderConstraints.Instance, OrderAxes.Orders);

	public static IEnumerable<string> Axes() => Auto().CaseNames;

	[TestCaseSource(nameof(Axes))]
	public Task EveryDeclaredAxisHolds(string axis) => Auto().Case(axis).RunAsync();
}
