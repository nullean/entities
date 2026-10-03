using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

internal enum ItemKind
{
	Alpha,
	Beta,
	Gamma
}

internal sealed record Item(string Name, int Quantity, decimal Price, double Ratio, DateTimeOffset At, Guid Id, ItemKind Kind, string? Note);

internal sealed record Span(DateOnly Start, DateOnly End);

internal sealed record Basket(string Owner, IReadOnlyList<Item> Items, Dictionary<string, int> Counts);

internal sealed record Account(string Id, string Email);

internal sealed record Ledger(IReadOnlyList<Account> Accounts);

internal sealed record Node(string Name, List<Node> Children);

internal sealed record Guarded
{
	public Guarded(int count)
	{
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0);
		Count = count;
	}

	public int Count { get; }
}

internal sealed class Pouch
{
	public string Label { get; set; } = "";

	public int[] Numbers { get; set; } = [];
}

internal interface IShape;

public class AutoGenTests
{
	private static IEnumerable<T> Draw<T>(Gen<T> gen, int count, uint stream = 1) =>
		Enumerable.Range(0, count).Select(i => gen.Generate(new PCG(stream, (ulong)i + 1), null, out _));

	[Test]
	public void The_same_seed_gives_the_same_value() =>
		Draw(GenAuto.Of<Item>(), 200).Should().Equal(Draw(GenAuto.Of<Item>(), 200));

	[Test]
	public void Plausible_values_contain_no_edge_cases()
	{
		var items = Draw(GenAuto.Of<Item>(AutoMode.Plausible), 1_000).ToList();

		items.Should().OnlyContain(i => i.Name.Length > 0 && i.Name.Trim() == i.Name);
		items.Should().OnlyContain(i => i.Quantity >= 0 && i.Quantity <= 100_000 && i.Price > 0 && !double.IsNaN(i.Ratio) && i.Ratio >= 0);
		items.Should().OnlyContain(i => Enum.IsDefined(i.Kind) && i.Note != null && i.Id != Guid.Empty);
	}

	[Test]
	public void Mixed_values_reach_the_common_edge_cases()
	{
		var items = Draw(GenAuto.Of<Item>(), 4_000).ToList();

		items.Should().Contain(i => i.Name == "");
		items.Should().Contain(i => i.Name == " ");
		items.Should().Contain(i => i.Name.Trim() != i.Name && i.Name.Trim().Length > 0);
		items.Should().Contain(i => i.Quantity < 0);
		items.Should().Contain(i => i.Quantity == int.MinValue);
		items.Should().Contain(i => i.Price < 0);
		items.Should().Contain(i => i.Price == 0m);
		items.Should().Contain(i => double.IsNaN(i.Ratio));
		items.Should().Contain(i => double.IsInfinity(i.Ratio));
		items.Should().Contain(i => i.Ratio < 0);
		items.Should().Contain(i => !Enum.IsDefined(i.Kind));
		items.Should().Contain(i => i.Note == null);
		items.Should().Contain(i => i.Id == Guid.Empty);
		items.Should().Contain(i => i.At.Offset != TimeSpan.Zero);
		items.Should().Contain(i => i.At == DateTimeOffset.MinValue.AddTicks(1) || i.At == DateTimeOffset.MaxValue);
	}

	[Test]
	public void Mixed_excludes_the_hostile_strings_and_adversarial_includes_them()
	{
		var mixed = Draw(GenAuto.Of<Item>(), 4_000).Select(i => i.Name).ToList();
		var adversarial = Draw(GenAuto.Of<Item>(AutoMode.Adversarial), 4_000).Select(i => i.Name).ToList();

		mixed.Should().NotContain(n => n.Contains('\0') || n.Any(char.IsSurrogate) && !char.IsHighSurrogate(n[0]) && n.Length == 1);
		adversarial.Should().Contain(n => n.Contains('\0'));
		adversarial.Should().Contain(n => n.Length == 1 && char.IsLowSurrogate(n[0]));
	}

	[Test]
	public void String_edges_cover_whitespace_unicode_injection_and_length()
	{
		var names = Draw(GenAuto.Of<Item>(AutoMode.Adversarial), 6_000).Select(i => i.Name).ToHashSet();

		names.Should().Contain(" ");
		names.Should().Contain("​");
		names.Should().Contain("‮");
		names.Should().Contain("é");
		names.Should().Contain("İ");
		names.Should().Contain("' OR 1=1 --");
		names.Should().Contain("../../etc/passwd");
		names.Should().Contain("null");
		names.Should().Contain(n => n.Length >= 10_000);
	}

	[Test]
	public void Related_fields_are_equal_inverted_and_adjacent_in_adversarial_mode()
	{
		var spans = Draw(GenAuto.Of<Span>(AutoMode.Adversarial), 3_000).ToList();

		spans.Should().Contain(s => s.Start == s.End);
		spans.Should().Contain(s => s.Start > s.End);
		spans.Should().Contain(s => s.End == s.Start.AddDays(1));
	}

	[Test]
	public void Plausible_mode_does_not_manufacture_equal_fields() =>
		Draw(GenAuto.Of<Span>(AutoMode.Plausible), 2_000).Count(s => s.Start == s.End).Should().BeLessThan(20);

	[Test]
	public void Collections_are_empty_single_and_larger_and_dictionaries_are_filled()
	{
		var baskets = Draw(GenAuto.Of<Basket>(), 2_000).ToList();

		baskets.Should().Contain(b => b.Items.Count == 0);
		baskets.Should().Contain(b => b.Items.Count == 1);
		baskets.Should().Contain(b => b.Items.Count >= 3);
		baskets.Should().Contain(b => b.Counts.Count > 0);
	}

	[Test]
	public void Duplicate_identities_across_items_are_generated()
	{
		var ledgers = Draw(GenAuto.Of<Ledger>(AutoMode.Adversarial), 3_000).ToList();

		ledgers.Should().Contain(l => l.Accounts.Count >= 2 && l.Accounts[0].Id == l.Accounts[1].Id);
		ledgers.Should().Contain(
			l =>
				l.Accounts.Count >= 2 && l.Accounts[0].Id != l.Accounts[1].Id && string.Equals(
					l.Accounts[0].Id,
					l.Accounts[1].Id,
					StringComparison.OrdinalIgnoreCase
				)
		);
	}

	[Test]
	public void Recursive_types_stop_at_the_depth_limit() =>
		Draw(GenAuto.Of<Node>(), 200).Should().OnlyContain(n => Depth(n) <= 5);

	private static int Depth(Node n) => 1 + (n.Children.Count == 0 ? 0 : n.Children.Max(Depth));

	[Test]
	public void Settable_properties_and_arrays_are_filled()
	{
		var pouches = Draw(GenAuto.Of<Pouch>(AutoMode.Plausible), 200).ToList();

		pouches.Should().OnlyContain(b => b.Label.Length > 0);
		pouches.Should().Contain(b => b.Numbers.Length > 0);
	}

	[Test]
	public void A_constructor_that_rejects_hostile_values_is_not_a_generator_failure()
	{
		var act = () => Draw(GenAuto.Of<Guarded>(AutoMode.Adversarial), 500).ToList();

		act.Should().NotThrow();
		act().Should().OnlyContain(g => g.Count > 0);
	}

	[Test]
	public void A_custom_generator_replaces_the_built_in_one_for_that_type()
	{
		var gen = GenAuto.Of<Item>(AutoMode.Adversarial, o => o.Use(Gen.Const(ItemKind.Beta)));

		Draw(gen, 200).Should().OnlyContain(i => i.Kind == ItemKind.Beta);
	}

	[Test]
	public void An_unbuildable_type_says_what_to_do()
	{
		var act = () => GenAuto.Of<IShape>();

		act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("AutoOptions.Use");
	}

	[Test]
	public void A_failing_property_still_shrinks_and_reports_a_replayable_seed()
	{
		var act = () => GenAuto.Of<Basket>().Sample(b => b.Items.Count < 2, iter: 500);

		act.Should().Throw<CsCheckException>().Which.Message.Should().Contain("Set seed");
	}

	[Test]
	public void Many_threads_can_draw_at_once()
	{
		var gen = GenAuto.Of<Item>();

		var act = () => Parallel.For(0, 8, t => Draw(gen, 2_000, (uint)t + 1).ToList());

		act.Should().NotThrow();
	}
}
