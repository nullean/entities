using Nullean.Entities;

namespace DataAudit;

public sealed record CustomerRow(long Id, string? Email, string Country, decimal CreditLimit, DateTimeOffset CreatedAt, long? ParentId);

/// <summary>The whole table, for the rules that are about rows together (uniqueness, references). A row rule cannot state these.</summary>
public sealed record CustomerTable(IReadOnlyList<CustomerRow> Rows);

/// <summary>
/// Rules about one row. The severity is part of the rule: an error is data that is wrong, a warning is data that is suspicious, a hint is data that is untidy.
/// </summary>
[Constraints]
public static partial class RowRules
{
	// NOT NULL, as a rule
	public static readonly Constraint<CustomerRow> EmailIsRequired = Constraints<CustomerRow>.Always(
		r => !string.IsNullOrWhiteSpace(r.Email),
		"Email is missing"
	);

	// CHECK (email LIKE '%@%.%')
	public static readonly Constraint<CustomerRow> EmailLooksValid = Constraints<CustomerRow>.Always(
		r => string.IsNullOrWhiteSpace(r.Email) || (r.Email.Contains('@') && r.Email[(r.Email.IndexOf('@') + 1)..].Contains('.')),
		"Email is not an address"
	);

	// CHECK (credit_limit >= 0)
	public static readonly Constraint<CustomerRow> CreditLimitIsNotNegative = Constraints<CustomerRow>.Always(
		r => r.CreditLimit >= 0,
		"Credit limit is negative"
	);

	// CHECK (created_at <= now()): a clock-dependent rule, so it takes the audit's TimeProvider
	public static readonly Constraint<CustomerRow> CreatedAtIsNotInTheFuture = Constraints<CustomerRow>.NotInFuture(
		r => r.CreatedAt,
		tolerance: TimeSpan.FromMinutes(5)
	);

	public static readonly Constraint<CustomerRow> CountryIsAnIsoCode = Constraints<CustomerRow>
		.Always(r => r.Country.Length == 2 && r.Country.All(char.IsAsciiLetterUpper), "Country is not a two-letter ISO code")
		.WithSeverity(Severity.Warning);

	public static readonly Constraint<CustomerRow> EmailIsLowerCase = Constraints<CustomerRow>
		.Always(r => r.Email is null || r.Email.All(c => !char.IsUpper(c)), "Email has upper-case letters")
		.WithSeverity(Severity.Hint);
}

/// <summary>Rules about the table as a whole: the ones a database enforces with an index or a foreign key.</summary>
[Constraints]
public static partial class TableRules
{
	// UNIQUE (lower(email))
	public static readonly Constraint<CustomerTable> EmailsAreUnique = Constraints<CustomerTable>.Unique(
		t => t.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Email)),
		r => r.Email!.ToLowerInvariant(),
		message: "Two customers share an email"
	);

	// FOREIGN KEY (parent_id) REFERENCES customer (id)
	public static readonly Constraint<CustomerTable> ParentsExist = Constraints<CustomerTable>.References<CustomerRow, long>(
		t => t.Rows,
		r => r.ParentId is { } parent ? [parent] : [],
		t => t.Rows.Select(r => r.Id),
		message: "A customer's parent does not exist"
	);

	// no database constraint does this one; a trigger or a recursive check at write time does
	public static readonly Constraint<CustomerTable> ParentsAreNotCircular = Constraints<CustomerTable>.Acyclic(
		t => t.Rows,
		r => r.Id,
		r => r.ParentId is { } parent ? [parent] : [],
		message: "Customers are their own ancestors"
	);
}

/// <summary>
/// Which rules belong in the schema as well. The rules here check data after the fact; the schema is what stops it arriving. This table is written by hand:
/// the library does not generate DDL, and a rule that reads a clock or spans several tables has no column-level equivalent.
/// </summary>
public static class SchemaEquivalent
{
	public static readonly IReadOnlyDictionary<string, string> ByRule = new Dictionary<string, string>
	{
		[ConstraintName.Of(nameof(RowRules.EmailIsRequired))] = "email text NOT NULL",
		[ConstraintName.Of(nameof(RowRules.EmailLooksValid))] = "CHECK (email LIKE '%_@_%._%')",
		[ConstraintName.Of(nameof(RowRules.CreditLimitIsNotNegative))] = "CHECK (credit_limit >= 0)",
		[ConstraintName.Of(nameof(RowRules.CreatedAtIsNotInTheFuture))] = "DEFAULT now(), and a trigger if clients may set it (a CHECK cannot read the clock)",
		[ConstraintName.Of(nameof(RowRules.CountryIsAnIsoCode))] = "country char(2) REFERENCES country (code), once the cleanup is done",
		[ConstraintName.Of(nameof(RowRules.EmailIsLowerCase))] = "normalise on write; UNIQUE (lower(email)) makes the casing irrelevant",
		[ConstraintName.Of(nameof(TableRules.EmailsAreUnique))] = "CREATE UNIQUE INDEX ON customer (lower(email))",
		[ConstraintName.Of(nameof(TableRules.ParentsExist))] = "FOREIGN KEY (parent_id) REFERENCES customer (id)",
		[ConstraintName.Of(nameof(TableRules.ParentsAreNotCircular))] = "no declarative form: a trigger, or a recursive check in the write path",
	};
}
