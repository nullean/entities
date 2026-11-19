using AwesomeAssertions;

namespace Nullean.Entities.SourceGenerators.Tests;

// C# initialises static fields in declaration order, so a rule that reads a field declared below it while it is built gets default.
public class FieldOrderTests
{
	private static string Source(string members) =>
		$$"""
		using System;
		using Nullean.Entities;

		namespace Sample;

		public sealed record User(string Name);
		public sealed record Order(int Id);

		[Constraints<Order>]
		public static partial class OrderRules
		{
		{{members}}
		}
		""";

	private const string Rule = "public static readonly Constraint<Order> Named = Constraints<Order>.Always(Make(Clerk), \"named\");";
	private const string Make = "private static Func<Order, bool> Make(User u) => _ => u.Name.Length > 0;";
	private const string Clerk = "private static readonly User Clerk = new(\"clerk\");";

	[Test]
	public void A_direct_read_of_a_field_declared_below_is_reported()
	{
		var run = GeneratorTestHost.Run(Source($"{Rule}\n{Make}\n{Clerk}"));

		var d = run.Diagnostics.Single(d => d.Id == "NECG030");
		d.GetMessage().Should().Contain("'Named'").And.Contain("'Clerk'");
	}

	[Test]
	public void A_field_declared_above_is_not_reported()
	{
		var run = GeneratorTestHost.Run(Source($"{Clerk}\n{Make}\n{Rule}"));

		run.HasDiagnostic("NECG030").Should().BeFalse();
	}

	[Test]
	public void A_read_inside_a_lambda_is_not_reported_because_it_happens_when_the_lambda_runs()
	{
		var rule = "public static readonly Constraint<Order> Named = Constraints<Order>.Always(_ => Clerk.Name.Length > 0, \"named\");";
		var run = GeneratorTestHost.Run(Source($"{rule}\n{Clerk}"));

		run.HasDiagnostic("NECG030").Should().BeFalse();
	}
}
