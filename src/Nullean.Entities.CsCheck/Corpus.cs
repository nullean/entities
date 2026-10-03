// Nullean.Entities.CsCheck — Bogus as a one-time corpus.
//
// Bogus takes a process-wide lock on every draw (Randomizer.Locker, an internal static), which flattens
// multi-threaded CsCheck runs. Here Bogus is used once, under that lock, to fill word lists from a fixed seed;
// every later pick is a lock-free index chosen by CsCheck's own PCG. Lists are sorted shortest first, so a
// smaller index is a simpler string and shrinking prefers it.

using Bogus;

namespace Nullean.Entities.CsCheck;

internal static class Corpus
{
	private sealed record Lists(
		string[] Words,
		string[] Sentences,
		string[] Names,
		string[] FirstNames,
		string[] LastNames,
		string[] Emails,
		string[] Urls,
		string[] Cities,
		string[] Countries,
		string[] Companies,
		string[] Codes,
		string[] Ibans,
		string[] Phones,
		string[] Products,
		string[] Usernames
	);

	private static readonly Lazy<Lists> Cached = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

	private static string[] Distinct(IEnumerable<string> values) =>
		[.. values.Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.Ordinal).OrderBy(v => v.Length).ThenBy(v => v, StringComparer.Ordinal)];

	private static string[] Many(int count, Func<string> make) => Distinct(Enumerable.Range(0, count).Select(_ => make()));

	private static Lists Build()
	{
		var f = new Faker("en") { Random = new Randomizer(20_260_101) };
		return new Lists(
			Words: Many(600, () => f.Lorem.Word()),
			Sentences: Many(200, () => f.Lorem.Sentence(3, 5)),
			Names: Many(300, () => f.Name.FullName()),
			FirstNames: Many(200, () => f.Name.FirstName()),
			LastNames: Many(200, () => f.Name.LastName()),
			Emails: Many(300, () => f.Internet.Email()),
			Urls: Many(200, () => f.Internet.Url()),
			Cities: Many(200, () => f.Address.City()),
			Countries: Many(100, () => f.Address.Country()),
			Companies: Many(200, () => f.Company.CompanyName()),
			Codes: Many(300, () => f.Commerce.Ean13()),
			Ibans: Many(100, () => f.Finance.Iban()),
			Phones: Many(100, () => f.Phone.PhoneNumber()),
			Products: Many(200, () => f.Commerce.ProductName()),
			Usernames: Many(200, () => f.Internet.UserName())
		);
	}

	/// <summary>A plausible list for a member called <paramref name="memberName"/> (an email for <c>Email</c>, a city for <c>City</c>, a word otherwise).</summary>
	public static string[] For(string? memberName)
	{
		var lists = Cached.Value;
		var n = memberName?.ToLowerInvariant() ?? "";
		return n switch
		{
			_ when n.Contains("email", StringComparison.Ordinal) => lists.Emails,
			_ when n.Contains("url", StringComparison.Ordinal) || n.Contains("uri", StringComparison.Ordinal) || n.Contains(
				"website",
				StringComparison.Ordinal
			) => lists.Urls,
			_ when n.Contains("firstname", StringComparison.Ordinal) || n.Contains("givenname", StringComparison.Ordinal) => lists.FirstNames,
			_ when n.Contains("lastname", StringComparison.Ordinal) || n.Contains("surname", StringComparison.Ordinal) => lists.LastNames,
			_ when n.Contains("username", StringComparison.Ordinal) || n.Contains("login", StringComparison.Ordinal) => lists.Usernames,
			_ when n.Contains("iban", StringComparison.Ordinal) => lists.Ibans,
			_ when n.Contains("phone", StringComparison.Ordinal) => lists.Phones,
			_ when n.Contains("city", StringComparison.Ordinal) => lists.Cities,
			_ when n.Contains("country", StringComparison.Ordinal) => lists.Countries,
			_ when n.Contains("company", StringComparison.Ordinal) || n.Contains("employer", StringComparison.Ordinal) => lists.Companies,
			_ when n.Contains("sku", StringComparison.Ordinal) || n.Contains("ean", StringComparison.Ordinal) || n.Contains("code", StringComparison.Ordinal) =>
				lists.Codes,
			_ when n.Contains("product", StringComparison.Ordinal) => lists.Products,
			_ when n.Contains("title", StringComparison.Ordinal) || n.Contains("description", StringComparison.Ordinal) || n.Contains(
				"message",
				StringComparison.Ordinal
			)
			|| n.Contains("comment", StringComparison.Ordinal) || n.Contains("body", StringComparison.Ordinal) || n.Contains(
				"text",
				StringComparison.Ordinal
			) => lists.Sentences,
			_ when n.EndsWith("name", StringComparison.Ordinal) || n.Contains("author", StringComparison.Ordinal) || n.Contains(
				"customer",
				StringComparison.Ordinal
			) => lists.Names,
			_ => lists.Words,
		};
	}
}
