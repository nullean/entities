using AwesomeAssertions;

namespace Nullean.Entities.SourceGenerators.Tests;

// What the generator decides about each method of an entity: which are actions, how the next value is read out of a wrapper, and which are passed over with an
// info diagnostic. The same fixtures run for real in tests/Nullean.Entities.Tests (ActionInferenceTests), where the generated Actions are applied.
public class ActionInferenceTests
{
	private const string Entity =
		"""
		using System.Collections.Generic;
		using System.Threading.Tasks;
		using Nullean.Entities;

		namespace Sample;

		public sealed record Outcome<T>(T? Value, IReadOnlyList<string> Notes);

		public sealed record Pair<T>(T Before, T After);

		public sealed record Held<T>(T Entity, T Spare);

		public sealed record Ticket(string Status, int Version)
		{
		    public Ticket Touch() => this with { Version = Version + 1 };

		    public Outcome<Ticket> Close() => new(this with { Status = "closed" }, []);

		    public Outcome<Ticket> Reopen(string reason) => new(this with { Status = reason }, []);

		    public (Ticket Ticket, int Changes) Archive() => (this with { Status = "archived" }, 1);

		    public Held<Ticket> Hold() => new(this with { Status = "held" }, this);

		    [ConstraintAction(nameof(Pair<Ticket>.After))]
		    public Pair<Ticket> Fork() => new(this, this with { Status = "forked" });

		    public Task<Ticket> Sync() => Task.FromResult(this);

		    public bool TryMerge(out Ticket merged)
		    {
		        merged = this;
		        return true;
		    }

		    public Pair<Ticket> Split() => new(this, this);

		    public string Label() => Status;

		    public Outcome<string> Describe() => new(Status, []);
		}

		public static partial class TicketConstraints
		{
		    [Constraint]
		    public static readonly Constraint<Ticket> HasStatus = Constraints<Ticket>.Always(t => t.Status.Length > 0, "A ticket has a status.");
		}
		""";

	[Test]
	public void Methods_returning_the_entity_or_a_wrapper_with_a_readable_member_become_actions_that_read_it()
	{
		var run = GeneratorTestHost.Run(Entity);

		var source = run.SingleGeneratedSource();
		source.Should().Contain("\"Touch\", null, (x, _) => x.Touch())");
		source.Should().Contain("\"Close\", null, (x, _) => x.Close().Value!)");
		source.Should().Contain("\"Reopen\", typeof(string), (x, a) => x.Reopen((string)a!).Value!)");
		source.Should().Contain("\"Archive\", null, (x, _) => x.Archive().Item1)");
	}

	[Test]
	public void With_several_members_of_the_entity_type_a_conventional_name_wins_and_the_attribute_overrides()
	{
		var source = GeneratorTestHost.Run(Entity).SingleGeneratedSource();

		source.Should().Contain("\"Hold\", null, (x, _) => x.Hold().Entity!)");
		source.Should().Contain("\"Fork\", null, (x, _) => x.Fork().After!)");
	}

	[Test]
	public void A_method_that_is_not_about_the_entity_is_not_an_action_and_is_not_reported()
	{
		var run = GeneratorTestHost.Run(Entity);

		var source = run.SingleGeneratedSource();
		source.Should().NotContain("\"Label\"").And.NotContain("\"Describe\"");
		run.Diagnostics.Select(d => d.GetMessage()).Should().NotContain(m => m.Contains("Label") || m.Contains("Describe"));
	}

	[Test]
	public void A_method_that_looks_like_an_operation_but_cannot_be_used_is_skipped_with_an_info_diagnostic_saying_why()
	{
		var run = GeneratorTestHost.Run(Entity);

		var declined = run.Diagnostics.Where(d => d.Id == "NECG020").ToList();

		declined.Should().OnlyContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Info);
		declined.Select(d => d.GetMessage()).Should().SatisfyRespectively(
			async => async.Should().Contain("Ticket.Sync").And.Contain("asynchronous"),
			tryMethod => tryMethod.Should().Contain("Ticket.TryMerge").And.Contain("out parameter"),
			ambiguous => ambiguous.Should().Contain("Ticket.Split").And.Contain("several members").And.Contain("Before").And.Contain("After")
		);
		run.SingleGeneratedSource().Should().NotContain("\"Sync\"").And.NotContain("\"TryMerge\"").And.NotContain("\"Split\"");
	}

	[Test]
	public void Naming_a_member_that_does_not_exist_is_declined_with_the_reason()
	{
		var run = GeneratorTestHost.Run(
			"""
			using System.Collections.Generic;
			using Nullean.Entities;

			namespace Sample;

			public sealed record Outcome<T>(T? Value, IReadOnlyList<string> Notes);

			public sealed record Ticket(int Version)
			{
			    [ConstraintAction("Missing")]
			    public Outcome<Ticket> Close() => new(this, []);
			}

			public static partial class TicketConstraints
			{
			    [Constraint]
			    public static readonly Constraint<Ticket> Positive = Constraints<Ticket>.Always(t => t.Version > 0, "no");
			}
			"""
		);

		run.Diagnostics.Single(d => d.Id == "NECG020").GetMessage().Should().Contain("'Missing'").And.Contain("not a readable member");
	}

	[Test]
	public void An_entity_with_no_usable_operation_has_no_Actions_property()
	{
		var run = GeneratorTestHost.Run(
			"""
			using System.Threading.Tasks;
			using Nullean.Entities;

			namespace Sample;

			public sealed record Ticket(int Version)
			{
			    public Task<Ticket> Sync() => Task.FromResult(this);
			}

			public static partial class TicketConstraints
			{
			    [Constraint]
			    public static readonly Constraint<Ticket> Positive = Constraints<Ticket>.Always(t => t.Version > 0, "no");
			}
			"""
		);

		run.SingleGeneratedSource().Should().NotContain("Actions");
		run.HasDiagnostic("NECG020").Should().BeTrue();
	}
}
