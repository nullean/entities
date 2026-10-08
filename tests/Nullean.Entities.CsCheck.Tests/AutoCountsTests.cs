using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// What spike questions 1 and 2 (AGENTS.md) found, kept true: a collection can have every count up to MaxCollection (a failure at exactly four items must be reachable at MaxCollection 12 as well as at 4),
// a failure shrinks to the fewest items, and early exit only ever applies to big collections.

public sealed record CountBag(IReadOnlyList<int> Items);

public sealed record CountBasket(IReadOnlyList<CountBag> Bags);

public class AutoCountsTests
{
	private static List<int> Counts(AutoMode mode, int maxCollection, int draws = 4000) =>
		[
			.. Enumerable
				.Range(0, draws)
				.Select(i => GenAuto.Of<CountBag>(mode, o => o.MaxCollection = maxCollection).Generate(new PCG(7u, (ulong)i + 1), null, out _).Items.Count)
		];

	[Test]
	[Arguments(AutoMode.Plausible)]
	[Arguments(AutoMode.Mixed)]
	public void Every_count_from_none_to_MaxCollection_can_occur(AutoMode mode)
	{
		foreach (var max in new[] { 4, 7, 12, 30 })
		{
			var seen = Counts(mode, max, 6000).Distinct().Order().ToList();

			seen.Should().Equal(Enumerable.Range(0, max + 1), $"MaxCollection {max}: counts that cannot occur are failures that cannot be found");
		}
	}

	[Test]
	public void Hostile_data_has_every_count_from_none_to_five_and_a_wide_one()
	{
		var seen = Counts(AutoMode.Adversarial, 12, 6000).Distinct().Order().ToList();

		seen.Should().Contain([0, 1, 2, 3, 4, 5]);
		seen.Max().Should().BeGreaterThanOrEqualTo(10);
	}

	[Test]
	public void The_default_MaxCollection_gives_the_counts_it_always_did()
	{
		Counts(AutoMode.Plausible, 4).Distinct().Order().Should().Equal(0, 1, 2, 3, 4);
	}

	[Test]
	public void A_failure_at_exactly_four_items_shrinks_to_four_items_at_every_MaxCollection()
	{
		foreach (var max in new[] { 4, 12, 40 })
		{
			var gen = GenAuto.Of<CountBag>(AutoMode.Plausible, o => o.MaxCollection = max);
			var finals = new List<int>();
			for (var run = 0; run < 15; run++)
			{
				var last = 0;
				try
				{
					gen.Sample(
						bag =>
						{
							if (bag.Items.Count > 3)
							{
								last = bag.Items.Count;
								throw new InvalidOperationException("more than three");
							}
						},
						iter: 2_000,
						threads: 1
					);
				}
				catch (CsCheckException)
				{
					finals.Add(last);
				}
			}

			// CsCheck's shrinking is a bounded search: about one run in a hundred at a wide MaxCollection stops one item short of the fewest, so the law is "nearly always, and never far".
			finals.Should().HaveCount(15);
			finals.Count(f => f == 4).Should().BeGreaterThanOrEqualTo(13, $"MaxCollection {max} must shrink to the fewest items");
			finals.Max().Should().BeLessThanOrEqualTo(5);
		}
	}

	// ── early exit ───────────────────────────────────────────────────────────────

	private static Gen<CountBag> Big(int earlyExitFrom, int maxCollection = 400) =>
		GenAuto.Of<CountBag>(AutoMode.Plausible, o =>
		{
			o.MaxCollection = maxCollection;
			o.EarlyExitFrom = earlyExitFrom;
		});

	[Test]
	public void Without_a_bound_to_beat_nothing_is_cut_short_whatever_the_size()
	{
		for (var i = 0; i < 100; i++)
		{
			var a = Big(32).Generate(new PCG(3u, (ulong)i + 1), null, out var sa);
			var b = Big(int.MaxValue).Generate(new PCG(3u, (ulong)i + 1), null, out var sb);

			a.Items.Should().Equal(b.Items);
			sa.I.Should().Be(sb.I);
		}
	}

	[Test]
	public void A_small_collection_is_generated_exactly_the_same_whether_or_not_there_is_a_bound()
	{
		var bound = new Size(1);

		for (var i = 0; i < 300; i++)
		{
			var a = Big(32, maxCollection: 12).Generate(new PCG(3u, (ulong)i + 1), bound, out var sa);
			var b = Big(int.MaxValue, maxCollection: 12).Generate(new PCG(3u, (ulong)i + 1), bound, out var sb);

			a.Items.Should().Equal(b.Items);
			sa.I.Should().Be(sb.I);
		}
	}

	[Test]
	public void A_big_collection_stops_once_it_cannot_beat_the_bound_and_what_it_returns_is_still_a_valid_value()
	{
		var bound = new Size(1);
		long whole = 0;
		long cut = 0;
		for (var i = 0; i < 200; i++)
		{
			var full = Big(int.MaxValue).Generate(new PCG(3u, (ulong)i + 1), bound, out _);
			var value = Big(32).Generate(new PCG(3u, (ulong)i + 1), bound, out var size);
			whole += full.Items.Count;
			cut += value.Items.Count;

			value.Items.Should().NotBeNull();
			if (value.Items.Count < full.Items.Count)
			{
				Size
					.IsLessThan(size, bound)
					.Should()
					.BeFalse("a candidate that was cut short must still not be smaller than the bound, or it would be mistaken for an improvement");
			}
		}

		cut.Should().BeLessThan(whole / 10, "big collections are cut to their first items");
	}

	[Test]
	public void Cutting_big_collections_short_finds_the_same_minimum()
	{
		foreach (var from in new[] { int.MaxValue, 32 })
		{
			var gen = Big(from);
			var finals = new List<int>();
			for (var run = 0; run < 15; run++)
			{
				var last = 0;
				try
				{
					gen.Sample(
						bag =>
						{
							if (bag.Items.Count > 3)
							{
								last = bag.Items.Count;
								throw new InvalidOperationException("more than three");
							}
						},
						iter: 2_000,
						threads: 1
					);
				}
				catch (CsCheckException)
				{
					finals.Add(last);
				}
			}

			finals.Should().HaveCount(15);
			finals.Count(f => f == 4).Should().BeGreaterThanOrEqualTo(13, $"EarlyExitFrom {from}: the same minimum, as often as without it");
			finals.Max().Should().BeLessThanOrEqualTo(5);
		}
	}

	[Test]
	public void Nested_collections_stop_too_and_their_items_are_never_null()
	{
		var gen = GenAuto.Of<CountBasket>(AutoMode.Plausible, o =>
		{
			o.MaxCollection = 200;
			o.EarlyExitFrom = 32;
		});

		for (var i = 0; i < 100; i++)
		{
			var basket = gen.Generate(new PCG(3u, (ulong)i + 1), new Size(1), out _);

			basket.Bags.Should().NotBeNull().And.OnlyContain(b => b != null && b.Items != null);
		}
	}
}
