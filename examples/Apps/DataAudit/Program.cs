using DataAudit;
using Nullean.Entities;
using static Apps.Narrator;

// Auditing a table that already exists, as a DBA would: stream the rows, rank what is wrong by severity, and say which rules also belong in the schema.
// The "table" is generated with a fixed seed and known dirt, so the audit can be checked against what was put in. Run it with `dotnet run`.

var clock = new ManualTimeProvider(FakeTable.Now);
var seeded = new Seeded();
var report = await Auditor.AuditAsync(FakeTable.ReadAsync(rows: 20_000, seed: 7, seeded), clock);

Section($"Audit of {report.Rows:N0} rows");
foreach (var f in report.Findings)
{
	Say($"{f.Severity,-7} {f.Rule,-26} x{f.Count,-4} e.g. {string.Join(", ", f.Examples)}");
}

Section("Checked against what was seeded");
Expect(report.RowsWithErrors.SetEquals(seeded.Errors), $"every row with a wrong value was found, and no other ({seeded.Errors.Count} rows)");
Expect(report.RowsWithWarnings.SetEquals(seeded.Warnings), $"every suspicious country was found as a warning ({seeded.Warnings.Count} rows)");
Expect(report.RowsWithHints.SetEquals(seeded.Hints), $"every upper-case email was found as a hint ({seeded.Hints.Count} rows)");
Expect(report.Count(nameof(TableRules.EmailsAreUnique)) == seeded.DuplicateEmails, $"{seeded.DuplicateEmails} shared emails, each reported once");
Expect(report.Count(nameof(TableRules.ParentsExist)) == seeded.OrphanedParents, $"{seeded.OrphanedParents} dangling parents");
Expect(report.Count(nameof(TableRules.ParentsAreNotCircular)) == seeded.Cycles, "the one parent cycle, reported once with its path");

Section("Severities decide what blocks a write");
var (accepted, notices) = Auditor.TryInsert(new CustomerRow(1, "Ada@Example.com", "Germany", 100m, FakeTable.Now, null), clock);
Say($"insert with an upper-case email and a bad country: accepted={accepted}, notices: {string.Join("; ", notices.Select(n => $"{n.Severity}: {n.Message}"))}");
Expect(accepted && notices.Count == 2, "warnings and hints ride along with an accepted row");
Expect(!Auditor.TryInsert(new CustomerRow(2, "ada@example.com", "DE", -1m, FakeTable.Now, null), clock).Accepted, "an error refuses it");

Section("Which of these rules the schema should also enforce");
foreach (var f in report.Findings.DistinctBy(f => f.Rule))
{
	Say($"{f.Rule,-26} {SchemaEquivalent.ByRule[f.Rule]}");
}
Expect(SchemaEquivalent.ByRule.Count == RowRules.All.AsConstraints().Count + TableRules.All.AsConstraints().Count, "every rule has a line in the schema table");

return Finish();
