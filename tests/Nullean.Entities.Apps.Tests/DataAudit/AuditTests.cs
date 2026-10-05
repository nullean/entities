using AwesomeAssertions;
using CsCheck;
using DataAudit;
using Nullean.Entities;

namespace Nullean.Entities.Apps.Tests.DataAudit;

public class AuditTests
{
	private static readonly ManualTimeProvider Clock = new(FakeTable.Now);

	[Test]
	public async Task The_audit_finds_exactly_the_dirt_that_was_seeded_for_any_seed()
	{
		foreach (var seed in new[] { 1, 2, 3, 99 })
		{
			var seeded = new Seeded();

			var report = await Auditor.AuditAsync(FakeTable.ReadAsync(rows: 2_000, seed, seeded), Clock);

			report.RowsWithErrors.Should().BeEquivalentTo(seeded.Errors);
			report.RowsWithWarnings.Should().BeEquivalentTo(seeded.Warnings);
			report.RowsWithHints.Should().BeEquivalentTo(seeded.Hints);
			report.Count(ConstraintName.Of(nameof(TableRules.EmailsAreUnique))).Should().Be(seeded.DuplicateEmails);
			report.Count(ConstraintName.Of(nameof(TableRules.ParentsExist))).Should().Be(seeded.OrphanedParents);
			report.Count(ConstraintName.Of(nameof(TableRules.ParentsAreNotCircular))).Should().Be(seeded.Cycles);
		}
	}

	[Test]
	public async Task A_clean_table_has_no_findings()
	{
		static async IAsyncEnumerable<CustomerRow> Clean()
		{
			for (var i = 1L; i <= 100; i++)
			{
				yield return new CustomerRow(i, $"user{i}@example.com", "DE", 100m, FakeTable.Now.AddDays(-1), i > 1 ? i - 1 : null);
				await Task.Yield();
			}
		}

		var report = await Auditor.AuditAsync(Clean(), Clock);

		report.Findings.Should().BeEmpty();
		report.Rows.Should().Be(100);
	}

	[Test]
	public async Task The_audit_stops_when_cancelled()
	{
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		var act = async () => await Auditor.AuditAsync(FakeTable.ReadAsync(1_000, 1, new Seeded(), cts.Token), Clock, cancellationToken: cts.Token);

		await act.Should().ThrowAsync<OperationCanceledException>();
	}

	[Test]
	public void An_insert_is_refused_only_by_errors_and_carries_the_rest_as_notices()
	{
		var (accepted, notices) = Auditor.TryInsert(new CustomerRow(1, "Ada@Example.com", "Germany", 10m, FakeTable.Now, null), Clock);

		accepted.Should().BeTrue();
		notices.Select(n => n.Severity).Should().BeEquivalentTo([Severity.Warning, Severity.Hint]);

		Auditor.TryInsert(new CustomerRow(2, "ada@example.com", "DE", -1m, FakeTable.Now, null), Clock).Accepted.Should().BeFalse();
	}

	[Test]
	public void A_row_dated_in_the_future_is_an_error_only_relative_to_the_clock_it_is_given()
	{
		var row = new CustomerRow(1, "ada@example.com", "DE", 10m, FakeTable.Now.AddHours(2), null);

		Auditor.TryInsert(row, Clock).Accepted.Should().BeFalse();
		Auditor.TryInsert(row, new ManualTimeProvider(FakeTable.Now.AddHours(3))).Accepted.Should().BeTrue();
	}

	[Test]
	public void Every_rule_has_a_schema_note_and_none_is_left_over()
	{
		string[] names = [.. RowRules.All.AsConstraints().Select(c => c.Name!), .. TableRules.All.AsConstraints().Select(c => c.Name!)];

		names.Should().BeEquivalentTo(SchemaEquivalent.ByRule.Keys);
	}

	[Test]
	public void Row_rules_hold_for_generated_clean_rows()
	{
		var row = Gen.Select(Gen.Long[1, 1_000_000], Gen.Int[0, 50_000], Gen.Int[0, 10_000_000].Select(cents => cents / 100m))
			.Select((id, n, limit) => new CustomerRow(id, $"user{n}@example.com", "NL", limit, FakeTable.Now.AddDays(-1), null));

		row.Sample(r => RowRules.All.Validate(r, Clock).Should().BeEmpty(), iter: 200);
	}
}
