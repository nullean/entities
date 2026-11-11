using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Quickstart;

namespace Nullean.Entities.TestFrameworks;

// The quickstart Order, shared by the xUnit, NUnit and MSTest projects: each runs it with its own one-liner.
public static class OrderAxes
{
	// Set only by FrameworkRun in a child run: the auto-test then gets an operation that resets the version, which breaks exactly one axis (monotonicity).
	public const string BrokenVariable = "NULLEAN_ENTITIES_BREAK_AN_AXIS";

	private static readonly Gen<OrderLine> Lines = Gen.Select(
		Gen.OneOf("apple", "pear", "fig"),
		Gen.Int[1, 5],
		Gen.Int[1, 20],
		(sku, quantity, price) => new OrderLine(sku, quantity, price)
	);

	private static readonly Gen<Order> Pending = Lines.Array[0, 3].Select(lines => lines.Aggregate(Order.New(), (order, line) => order.AddLine(line)));

	public static readonly Gen<Order> Orders = Gen.Select(
		Pending,
		Gen.Int[0, 3],
		(order, step) =>
			step switch
			{
				1 => order.Confirm(),
				2 => order.Confirm().Ship(),
				3 => order.Cancel(),
				_ => order
			}
	);

	public static bool Broken => Environment.GetEnvironmentVariable(BrokenVariable) == "1";

	public static AutoTest<Order> Sound() => AutoTest.For(OrderConstraints.Instance, Orders);

	// Sound in a normal run, so the suite stays green; broken on one axis when FrameworkRun asks for it.
	public static AutoTest<Order> MaybeBroken() => Broken ? Sound().Action("Reset", o => o with { Version = 0 }) : Sound();

	public static IReadOnlyList<string> CaseNames => [.. Sound().Cases.Select(c => c.ToString())];
}
