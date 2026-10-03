using Nullean.Entities;

namespace Banking;

/// <summary>A positive amount in a currency, with at most two decimals. Only <see cref="Create"/> makes one.</summary>
public sealed record Money
{
	private Money(decimal amount, string currency) => (Amount, Currency) = (amount, currency);

	public decimal Amount { get; }

	public string Currency { get; }

	private static readonly ConstraintSet<Money> Rules = ConstraintSet.From(
		("amount-is-positive", Constraints<Money>.Always(m => m.Amount > 0, "The amount must be greater than zero")),
		("amount-has-cents", Constraints<Money>.Always(m => decimal.Round(m.Amount, 2) == m.Amount, "The amount has at most two decimals")),
		("currency-is-iso", Constraints<Money>.Always(
			m => m.Currency.Length == 3 && m.Currency.All(char.IsAsciiLetterUpper),
			"The currency is a three-letter code like EUR"
		))
	);

	public static Result<Money> Create(decimal amount, string? currency) => Rules.Validated(new Money(amount, currency ?? ""));

	public override string ToString() => $"{Amount:0.00} {Currency}";
}

/// <summary>An IBAN, checked for shape and for the mod-97 checksum, so a mistyped digit is caught at the edge instead of at the bank.</summary>
public sealed record Iban
{
	private Iban(string value) => Value = value;

	public string Value { get; }

	private static readonly ConstraintSet<Iban> Rules = ConstraintSet.From(
		("iban-shape", Constraints<Iban>.Always(i => HasShape(i.Value), "An IBAN is two letters, two digits and up to 30 letters or digits")),
		("iban-checksum", Constraints<Iban>.Always(i => !HasShape(i.Value) || ChecksumHolds(i.Value), "The IBAN's check digits do not match"))
	);

	public static Result<Iban> Create(string? value) => Rules.Validated(new Iban(Normalize(value)));

	private static string Normalize(string? value) => (value ?? "").Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

	private static bool HasShape(string v) =>
		v.Length is >= 15 and <= 34 && char.IsAsciiLetterUpper(v[0]) && char.IsAsciiLetterUpper(v[1]) && char.IsAsciiDigit(v[2]) && char.IsAsciiDigit(v[3])
		&& v.All(char.IsAsciiLetterOrDigit);

	// ISO 13616: move the first four characters to the end, read letters as 10..35, and the number must leave 1 when divided by 97.
	private static bool ChecksumHolds(string v)
	{
		var remainder = 0;
		foreach (var c in v[4..] + v[..4])
		{
			remainder = char.IsAsciiDigit(c) ? ((remainder * 10) + (c - '0')) % 97 : ((remainder * 100) + c - 'A' + 10) % 97;
		}
		return remainder == 1;
	}

	public override string ToString() => Value;
}
