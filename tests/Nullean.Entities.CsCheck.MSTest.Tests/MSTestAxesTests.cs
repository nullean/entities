using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Quickstart;

namespace Nullean.Entities.TestFrameworks.MSTest;

// The one-liner docs/integration/test-frameworks.md shows: one data row per axis the rules declare.
[TestClass]
public class MSTestAxesTests
{
	public static AutoTest<Order> Auto() => AutoTest.For(OrderConstraints.Instance, OrderAxes.Orders);

	public static IEnumerable<string> Axes() => Auto().CaseNames;

	[TestMethod]
	[DynamicData(nameof(Axes))]
	public Task EveryDeclaredAxisHolds(string axis) => Auto().Case(axis).RunAsync();
}
