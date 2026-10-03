using System.Runtime.CompilerServices;

namespace DataAudit;

/// <summary>What <see cref="FakeTable"/> deliberately got wrong, so the audit can be checked against it.</summary>
public sealed record Seeded
{
	public HashSet<long> Errors { get; } = [];

	public HashSet<long> Warnings { get; } = [];

	public HashSet<long> Hints { get; } = [];

	public int DuplicateEmails { get; set; }

	public int OrphanedParents { get; set; }

	public int Cycles { get; set; }
}

/// <summary>A table that reads like a cursor: rows arrive one at a time. A fixed seed makes the same dirt every run, and the dirt is recorded.</summary>
public static class FakeTable
{
	public static readonly DateTimeOffset Now = new(2026, 3, 2, 12, 0, 0, TimeSpan.Zero);

	private static readonly string[] Countries = ["DE", "NL", "GB", "FR", "US"];

	public static async IAsyncEnumerable<CustomerRow> ReadAsync(
		int rows,
		int seed,
		Seeded seeded,
		[EnumeratorCancellation] CancellationToken cancellationToken = default
	)
	{
		var random = new Random(seed);

		// Who gets what: each defect takes its own rows, so every flagged row has exactly one reason.
		var pool = Enumerable.Range(1, rows).Select(i => (long)i).OrderBy(_ => random.Next()).ToList();

		List<long> Take(int n)
		{
			var taken = pool.Take(n).ToList();
			pool.RemoveRange(0, n);
			return taken;
		}

		var blank = Take(1).ToHashSet();
		var noAt = Take(rows / 100).ToHashSet();
		var negative = Take(rows / 100).ToHashSet();
		var future = Take(rows / 100).ToHashSet();
		var badCountry = Take(rows / 50).ToHashSet();
		var upper = Take(rows / 50).ToHashSet();
		var orphans = Take(rows / 200).ToHashSet();
		var cycle = Take(2);
		var pairs = rows / 200;
		var original = Take(pairs);
		var copy = Take(pairs);
		var copyOf = copy.Zip(original).ToDictionary(p => p.First, p => p.Second);

		seeded.Errors.UnionWith(blank.Concat(noAt).Concat(negative).Concat(future));
		seeded.Warnings.UnionWith(badCountry);
		seeded.Hints.UnionWith(upper);
		seeded.DuplicateEmails = pairs;
		seeded.OrphanedParents = orphans.Count;
		seeded.Cycles = 1;

		for (long id = 1; id <= rows; id++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var email = $"user{(copyOf.TryGetValue(id, out var source) ? source : id)}@example.com";
			var parent = orphans.Contains(id)
				? rows + 1_000 + id
				: id == cycle[0] ? cycle[1] : id == cycle[1] ? cycle[0] : id > 1 && random.Next(4) == 0 ? random.NextInt64(1, id) : (long?)null;

			if (id % 1_000 == 0)
			{
				await Task.Yield();
			}
			yield return new CustomerRow(
				id,
				blank.Contains(id) ? "  " : noAt.Contains(id) ? $"user{id}.example.com" : upper.Contains(id) ? email.ToUpperInvariant() : email,
				badCountry.Contains(id) ? "Germany" : Countries[(int)(id % Countries.Length)],
				negative.Contains(id) ? -50m : 1_000m + (id % 10 * 100),
				future.Contains(id) ? Now.AddDays(30) : Now.AddDays(-(id % 900)),
				parent
			);
		}
	}
}
