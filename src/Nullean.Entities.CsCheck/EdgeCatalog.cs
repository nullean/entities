// Nullean.Entities.CsCheck — the edge values AutoGen mixes into generated data.
//
// Every catalog is ordered simplest first: CsCheck shrinks by preferring a smaller index, so a failure
// ends on the simplest hostile value that still breaks the rule ("" before a 10,000-character string,
// 0 before int.MinValue). The "Common" catalogs are used by AutoMode.Mixed; Adversarial adds the
// "Hostile" values, which break serialisers and databases on purpose (NUL, lone surrogates).

using System.Globalization;
using System.Numerics;

namespace Nullean.Entities.CsCheck;

internal static class EdgeCatalog
{
	// ── Strings ──────────────────────────────────────────────────────────────

	private static string Repeat(char c, int count) => new(c, count);

	public static readonly string[] StringCommon =
	[
		// emptiness and whitespace
		"", " ", "a", "A", "0", "-", ".", "@", "\t", "\n", "\r\n", "\r", "\f", "\v", "  ", "   ",
		" a", "a ", " a ", "a  b", "a\tb", "a\nb", "a\r\nb",
		"\u00A0", "a\u00A0", "\u3000", "\u2003", "\u2009", "\u200A", "\u2028", "\u2029", "\u0085",
		// invisible
		"\u200B", "\u200C", "\u200D", "\u2060", "\uFEFF", "\u00AD", "a\u200Bb", "\uFE0F",
		// bidirectional overrides ("trojan source")
		"\u202E", "\u202Dabc", "abc\u202Edef", "\u2066", "\u2069",
		// controls
		"\u0001", "\u001F", "\u007F", "\u0080", "\u009F", "\uE000", "\uFFFE", "\uFFFF",
		// unicode structure
		"é", "e\u0301", "ñ", "n\u0303", "😀", "👨\u200D👩\u200D👧\u200D👦", "👍🏽", "🇳🇱", "𝒳", "日本語", "مرحبا", "שלום", "a\u05D0b",
		"Z\u0351\u036B\u0343\u036A\u0302\u036B\u033D\u034F\u0334\u0319\u0324\u031E\u0349\u035A\u032F\u031E\u0320\u034Da\u0334\u0317\u0331l\u0338g\u0337o\u0336",
		// culture
		"İ", "ı", "ß", "ǅ", "ﬃ", "I", "i", "STRASSE", "straße", "ǈ",
		// look-alikes
		"аdmin", "admin", "ＡＢＣ", "٣", "ο", "Ⅳ",
		// looks like something else
		"null", "NULL", "undefined", "true", "false", "NaN", "Infinity", "-0", "0x10", "1e5", "1,000.5", "00123", "+1", "٠١٢",
		"12345678901234567890", "2026-02-30", "00000000-0000-0000-0000-000000000000", "2026-01-01T00:00:00Z", "[]", "{}", "[object Object]",
		// injection and format characters
		"'", "\"", "\\", "/", "`", "' OR 1=1 --", "'; DROP TABLE x;--", "<script>alert(1)</script>", "<![CDATA[", "]]>", "&amp;", "&#x41;",
		"%s%n%x", "{0}", "{{ }}", "${jndi:ldap://x}", "{{7*7}}", "#{7*7}", "../../etc/passwd", "..\\..\\windows", "C:\\con", "%00", "%2e%2e%2f",
		"a,b", "a\"b", "=1+1", "@SUM(1)", "-1+1", "a:b", "a=b", "{\"a\":1}", "<a/>", "a%20b", "a+b",
		// shaped like an email, url or path
		"a@", "@a", "a@b", "a@@b.c", "a b@c.d", "a@b..c", ".a", "a.", "..", "...", "http://", "//", "javascript:alert(1)", "file:///", "münchen.de",
		// length
		Repeat('a', 50), Repeat('a', 100), Repeat('a', 255), Repeat('a', 256), Repeat('a', 1000), Repeat('a', 4000), Repeat('a', 4001),
		"a" + Repeat(' ', 500) + "b", Repeat(' ', 300), Repeat('a', 10_000), string.Concat(Enumerable.Repeat("😀", 600)),
	];

	public static readonly string[] StringHostile =
	[
		"\0", "a\0b", "\uD800", "\uDC00", "a\uD800b", "\uDBFF\uDBFF", "\uFFFD", "\uD83D", "%00%00",
	];

	// ── Integers ─────────────────────────────────────────────────────────────

	public static T[] Integers<T>() where T : IBinaryInteger<T>, IMinMaxValue<T>
	{
		var min = BigInteger.Parse(T.MinValue.ToString(null, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
		var max = BigInteger.Parse(T.MaxValue.ToString(null, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
		var values = new List<BigInteger> { 0, 1, -1, 2, -2, min, max, min + 1, max - 1 };
		foreach (var p in new[] { 7, 8, 15, 16, 31, 32, 63, 64, 127, 128, 255, 256, 32767, 32768, 65535, 65536 })
		{
			values.Add(p);
			values.Add(p - 1);
			values.Add(p + 1);
			values.Add(-p);
		}
		foreach (var shift in new[] { 31, 32, 53, 62, 63 })
		{
			var p = BigInteger.One << shift;
			values.AddRange([p, p - 1, p + 1, -p, -p + 1, -p - 1]);
		}
		foreach (var n in new[] { 10, 100, 1000, 10_000, 100_000, 1_000_000 })
		{
			values.AddRange([n - 1, n, n + 1, -n]);
		}
		return [
			.. values
				.Where(v => v >= min && v <= max)
				.Distinct()
				// simplest first: smaller magnitude, positive before negative
				.OrderBy(BigInteger.Abs)
				.ThenByDescending(v => v.Sign)
				.Select(v => T.CreateChecked(v)),
		];
	}

	// ── Floating point and decimal ───────────────────────────────────────────

	public static readonly double[] Doubles =
	[
		0.0, 1.0, -1.0, 0.5, -0.5, 0.1, 0.2, 0.3, 0.1 + 0.2, double.Epsilon, -double.Epsilon, -0.0, 2.0, 100.0, 1e-7, 1e21,
		9007199254740992.0, 9007199254740993.0, -9007199254740992.0, 2.2250738585072014E-308, 1.7976931348623157E+308, -1.7976931348623157E+308,
		double.NaN, double.PositiveInfinity, double.NegativeInfinity, BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000001UL)),
		BitConverter.Int64BitsToDouble(unchecked((long)0x7FF8000000000123UL)), 4.94065645841247E-324 * 3, 1.0000000000000002, 0.9999999999999999,
	];

	public static readonly decimal[] Decimals =
	[
		0m, 1m, -1m, 0.01m, -0.01m, 0.1m, 0.2m, 0.1m + 0.2m, 0.005m, 0.015m, 0.025m, -0.005m, 1.0m, 1.00m, 1.000000000m, -0.0m, 100m,
		0.0000000000000000000000000001m, -0.0000000000000000000000000001m, decimal.MaxValue, decimal.MinValue,
		1234567890123456789012345678m, 7.9228162514264337593543950335m, 0.99999999999999999999999999m,
	];

	// ── Time ─────────────────────────────────────────────────────────────────

	private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	public static readonly DateTime[] DateTimes =
	[
		default, Epoch, new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.MinValue.AddTicks(
			1
		), DateTime.MinValue.AddDays(1),
		DateTime.MaxValue, DateTime.MaxValue.AddTicks(-1), new(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc), new(1900, 2, 28, 0, 0, 0, DateTimeKind.Utc),
		new(1900, 3, 1, 0, 0, 0, DateTimeKind.Utc), new(2000, 2, 29, 0, 0, 0, DateTimeKind.Utc), new(2024, 2, 29, 23, 59, 59, 999, DateTimeKind.Utc),
		new(2023, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc), new(2038, 1, 19, 3, 14, 7, DateTimeKind.Utc), new(2038, 1, 19, 3, 14, 8, DateTimeKind.Utc),
		new(1969, 12, 31, 23, 59, 59, DateTimeKind.Utc), new(2026, 3, 29, 2, 30, 0, DateTimeKind.Unspecified), new(
			2026,
			10,
			25,
			2,
			30,
			0,
			DateTimeKind.Unspecified
		),
		new(2026, 3, 8, 2, 30, 0, DateTimeKind.Unspecified), new(2026, 11, 1, 1, 30, 0, DateTimeKind.Unspecified), new(
			2026,
			6,
			15,
			12,
			0,
			0,
			DateTimeKind.Local
		),
		new(2026, 6, 15, 12, 0, 0, DateTimeKind.Unspecified),
	];

	public static readonly DateTimeOffset[] DateTimeOffsets =
	[
		default, DateTimeOffset.UnixEpoch, new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.MinValue.AddTicks(
			1
		), DateTimeOffset.MaxValue, DateTimeOffset.MaxValue.AddTicks(-1),
		new(2000, 2, 29, 0, 0, 0, TimeSpan.Zero), new(1900, 3, 1, 0, 0, 0, TimeSpan.Zero), new(2038, 1, 19, 3, 14, 7, TimeSpan.Zero),
		new(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(14)), new(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(-12)), new(
			2026,
			6,
			15,
			12,
			0,
			0,
			new TimeSpan(5, 45, 0)
		),
		new(2026, 6, 15, 23, 59, 59, TimeSpan.FromHours(14)), new(2026, 6, 15, 0, 0, 0, TimeSpan.FromHours(-12)), new(
			2026,
			3,
			29,
			1,
			59,
			59,
			TimeSpan.FromHours(1)
		),
		new(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)), new(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2)), new(
			2026,
			10,
			25,
			2,
			30,
			0,
			TimeSpan.FromHours(1)
		),
		new(2026, 12, 31, 23, 59, 59, TimeSpan.Zero), new(2021, 1, 3, 0, 0, 0, TimeSpan.Zero), new(2020, 12, 31, 0, 0, 0, TimeSpan.Zero),
	];

	public static readonly DateOnly[] DateOnlys =
	[
		default, DateOnly.MinValue.AddDays(1), new(1970, 1, 1), new(2000, 1, 1), new(2026, 1, 1), DateOnly.MaxValue, DateOnly.MaxValue.AddDays(-1), new(
			1900,
			2,
			28
		),
		new(1900, 3, 1), new(2000, 2, 29), new(2024, 2, 29), new(2038, 1, 19), new(2023, 12, 31), new(2021, 1, 3), new(2020, 12, 31),
	];

	public static readonly TimeOnly[] TimeOnlys =
	[
		TimeOnly.MinValue, new(12, 0), TimeOnly.MaxValue, new(23, 59, 59), new(0, 0, 1), new(2, 30), new(1, 0), new(23, 59), TimeOnly.MinValue.Add(
			TimeSpan.FromTicks(1)
		),
	];

	public static readonly TimeSpan[] TimeSpans =
	[
		TimeSpan.Zero, TimeSpan.FromTicks(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-1), TimeSpan.FromMinutes(1), TimeSpan.FromHours(
			1
		), TimeSpan.FromHours(24),
		TimeSpan.FromHours(24) + TimeSpan.FromTicks(1), TimeSpan.FromDays(-1), TimeSpan.FromDays(365), TimeSpan.MaxValue, TimeSpan.MinValue, TimeSpan.FromTicks(
			-1
		),
		TimeSpan.MaxValue - TimeSpan.FromTicks(1), TimeSpan.FromMilliseconds(1),
	];

	// ── Identities ───────────────────────────────────────────────────────────

	public static readonly Guid[] Guids =
	[
		Guid.Empty, new("ffffffff-ffff-ffff-ffff-ffffffffffff"), new("00000000-0000-0000-0000-000000000001"), new("11111111-1111-1111-1111-111111111111"),
		new("00000000-0000-1000-8000-000000000000"), new("018f0000-0000-7000-8000-000000000000"), new("ABCDEFAB-CDEF-ABCD-EFAB-CDEFABCDEFAB"),
	];

	// ── Uris ─────────────────────────────────────────────────────────────────

	public static readonly string[] Uris =
	[
		"http://a/", "https://example.com/", "http://localhost/", "http://127.0.0.1:0/", "http://[::1]/", "ftp://example.com/", "file:///c:/", "mailto:a@b.c",
		"http://user:pass@example.com/", "https://münchen.de/", "https://xn--mnchen-3ya.de/", "https://example.com:65535/", "https://example.com./", "https://example.com/" + new string(
			'a',
			2100
		),
		"https://example.com/a%20b?q=%00#f", "urn:uuid:00000000-0000-0000-0000-000000000000", "data:text/plain,a",
	];

	// ── Names that signal an identity (used for duplicate-id relations) ──────

	public static bool LooksLikeIdentity(string? name)
	{
		if (name is null)
		{
			return false;
		}
		var n = name.ToLowerInvariant();
		return n is "id" or "key" or "sku" or "code" or "name" or "email" or "username" or "slug" or "number" or "iban" or "reference" || n.EndsWith(
			"id",
			StringComparison.Ordinal
		);
	}

	// ── Pairs that signal a relation (used for Start/End style relations) ────

	public static readonly (string Low, string High)[] RelationPairs =
	[
		("start", "end"), ("begin", "end"), ("from", "to"), ("min", "max"), ("minimum", "maximum"), ("lower", "upper"), ("low", "high"), ("open", "close"),
		("opened", "closed"), ("created", "updated"), ("earliest", "latest"), ("first", "last"), ("debit", "credit"), ("before", "after"), ("onhand", "reserved"),
	];
}
