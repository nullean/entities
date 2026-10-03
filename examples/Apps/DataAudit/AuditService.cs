using Nullean.Entities;

namespace DataAudit;

/// <summary>What the audit found for one rule.</summary>
public sealed record RuleFinding(string Rule, Severity Severity, int Count, IReadOnlyList<string> Examples);

public sealed record AuditReport(
	int Rows,
	IReadOnlyList<RuleFinding> Findings,
	IReadOnlySet<long> RowsWithErrors,
	IReadOnlySet<long> RowsWithWarnings,
	IReadOnlySet<long> RowsWithHints
)
{
	public int Count(string rule) => Findings.Where(f => f.Rule == rule).Sum(f => f.Count);
}

/// <summary>
/// Audits a table that already exists. Row rules run over the stream as it arrives (<c>ValidateAsync</c>: nothing but one row is held), table rules run
/// over the rows kept for them. A real audit over a table too large to hold would keep only the keys the table rules need (an email hash, an id, a parent id):
/// the rules take a value, so give them one that holds just those.
/// </summary>
public static class Auditor
{
	public static async Task<AuditReport> AuditAsync(
		IAsyncEnumerable<CustomerRow> rows,
		TimeProvider clock,
		int examples = 3,
		CancellationToken cancellationToken = default
	)
	{
		var findings = new Dictionary<(string Rule, Severity Severity), (int Count, List<string> Examples)>();
		HashSet<long> errors = [], warnings = [], hints = [];
		var kept = new List<CustomerRow>();

		void Record(string rule, Severity severity, string example)
		{
			var (count, shown) = findings.GetValueOrDefault((rule, severity), (0, []));
			if (shown.Count < examples)
			{
				shown.Add(example);
			}
			findings[(rule, severity)] = (count + 1, shown);
		}

		await foreach (var (row, violations) in RowRules.All.ValidateAsync(rows, clock, cancellationToken).ConfigureAwait(false))
		{
			kept.Add(row);
			foreach (var v in violations)
			{
				Record(v.ConstraintId ?? "?", v.Severity, $"#{row.Id}");
				(v.Severity switch
				{
					Severity.Error => errors,
					Severity.Warning => warnings,
					_ => hints
				}).Add(row.Id);
			}
		}

		foreach (var v in TableRules.All.Validate(new CustomerTable(kept), clock))
		{
			Record(v.ConstraintId ?? "?", v.Severity, v.Evidence?.ToString() ?? v.Message);
		}

		return new AuditReport(
			kept.Count,
			[
				.. findings
					.OrderByDescending(f => f.Key.Severity == Severity.Error)
					.ThenBy(f => f.Key.Rule, StringComparer.Ordinal)
					.Select(f => new RuleFinding(f.Key.Rule, f.Key.Severity, f.Value.Count, f.Value.Examples))
			],
			errors,
			warnings,
			hints
		);
	}

	/// <summary>
	/// The write path for the same rules: an insert is accepted unless an <b>error</b> rule is broken, and warnings and hints come back with it
	/// as notices, so the caller can show them without refusing the row.
	/// </summary>
	public static (bool Accepted, IReadOnlyList<Violation> Notices) TryInsert(CustomerRow row, TimeProvider clock)
	{
		var violations = RowRules.All.Validate(row, clock);
		return (!violations.Any(v => v.Severity == Severity.Error), [.. violations.Where(v => v.Severity != Severity.Error)]);
	}
}
