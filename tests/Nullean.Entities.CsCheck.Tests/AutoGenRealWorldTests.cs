using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Numerics;
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// A value object with a private constructor and a validating factory, like docs-builder's DocsetPath.
internal readonly record struct DocsetPath
{
	private DocsetPath(string value) => Value = value;

	public string Value { get; }

	public static DocsetPath From(string value) =>
		string.IsNullOrWhiteSpace(value) || value.StartsWith('/')
			? throw new ArgumentException("A docset path is relative and not blank.", nameof(value))
			: new DocsetPath(value);
}

internal sealed record PathHolder(DocsetPath Path, string Title);

internal readonly record struct OrderId(Guid Value);

internal sealed record TrackedOrder(OrderId Id, OrderId? ParentId, string Note);

internal sealed record TrackedOrders(IReadOnlyList<TrackedOrder> Items);

internal sealed record Circle(double Radius);

internal sealed record Square(double Side);

#if NET11_0_OR_GREATER
internal readonly union Shape(Circle, Square);
#else
[System.Runtime.CompilerServices.Union]
internal readonly struct Shape
{
	public Shape(Circle value) => Value = value;

	public Shape(Square value) => Value = value;

	public object? Value { get; }
}
#endif

internal sealed record Drawing(string Name, IReadOnlyList<Shape> Shapes);

internal sealed record Form(
	[property: Range(1, 5)] int Rating,
	[StringLength(10, MinimumLength = 3)] string Code,
	[RegularExpression("^[A-Z]{3}-[0-9]{3}$")] string Reference,
	[Required] string Owner
);

internal sealed record Wide(IPAddress Address, Version Release, Int128 Big, BigInteger Huge, Half Small, System.Text.Rune Letter);

internal sealed record Deep(string Y);

internal sealed record Inner(string X, Deep Deep);

internal sealed record Outer(string Name, Inner Inner);

internal sealed record MaybeNames(IReadOnlyList<string?> Maybe, IReadOnlyList<string> Always);

internal sealed record Customer(string Id, string Name);

internal sealed record PurchaseOrder(string Id, string CustomerId);

internal sealed record Shop(IReadOnlyList<Customer> Customers, IReadOnlyList<PurchaseOrder> Orders);

internal sealed record Doc(string Id, IReadOnlyList<string> IncludedIds);

internal sealed record DocSet(IReadOnlyList<Doc> Docs);

internal abstract record Notice
{
	public sealed record Info(string Text) : Notice;

	public sealed record Alert(string Text, int Level) : Notice;
}

internal interface IAnimal
{
	string Name { get; }
}

internal sealed record Dog(string Name) : IAnimal;

internal sealed record Cat(string Name, bool Indoor) : IAnimal;

[System.Text.Json.Serialization.JsonDerivedType(typeof(Wheel))]
internal abstract record Part(string Label);

internal sealed record Wheel(string Label, int Spokes) : Part(Label);

internal sealed record Bolt(string Label) : Part(Label);

internal sealed record Inbox(IReadOnlyList<Notice> Notices, IAnimal Pet, Part Part);

internal interface IUnimplemented;

public class AutoGenRealWorldTests
{
	private static List<T> Draw<T>(AutoMode mode, int count = 400, Action<AutoOptions>? configure = null) =>
		[
			.. Enumerable.Range(0, count).Select(
				i => (configure is null ? GenAuto.Of<T>(mode) : GenAuto.Of<T>(mode, configure)).Generate(new PCG(7u, (ulong)i + 1), null, out _)
			)
		];

	[Test]
	public void A_value_object_with_a_private_constructor_is_built_through_its_static_factory()
	{
		foreach (var mode in new[] { AutoMode.Plausible, AutoMode.Mixed, AutoMode.Adversarial })
		{
			var values = Draw<PathHolder>(mode);

			values
				.All(h => !string.IsNullOrWhiteSpace(h.Path.Value) && !h.Path.Value.StartsWith('/'))
				.Should()
				.BeTrue($"{mode}: From() guards, so every built value passed it");
		}
	}

	private static bool Dangling(TrackedOrders o) => o.Items.Any(i => i.ParentId is { } p && o.Items.All(x => x.Id != p));

	[Test]
	public void Strongly_typed_ids_take_part_in_foreign_keys()
	{
		var plausible = Draw<TrackedOrders>(AutoMode.Plausible, 500).Where(o => o.Items.Select(i => i.Id).Distinct().Count() == o.Items.Count).ToList();

		plausible.All(o => !Dangling(o)).Should().BeTrue();
		Draw<TrackedOrders>(AutoMode.Adversarial, 800).Any(Dangling).Should().BeTrue("a reference to an id nothing has");
	}

	[Test]
	public void Union_cases_are_all_reached()
	{
		var shapes = Draw<Drawing>(AutoMode.Plausible).SelectMany(d => d.Shapes).ToList();

		shapes.Should().NotBeEmpty();
		shapes.Any(s => s.Value is Circle).Should().BeTrue();
		shapes.Any(s => s.Value is Square).Should().BeTrue();
		shapes.All(s => s.Value is not null).Should().BeTrue();
	}

	[Test]
	public void Data_annotations_hold_in_plausible_data_and_their_boundaries_are_drawn_in_adversarial_data()
	{
		var plausible = Draw<Form>(AutoMode.Plausible);

		plausible.All(f => f.Rating is >= 1 and <= 5).Should().BeTrue();
		plausible.All(f => f.Code.Length is >= 3 and <= 10).Should().BeTrue();
		plausible.All(f => System.Text.RegularExpressions.Regex.IsMatch(f.Reference, "^[A-Z]{3}-[0-9]{3}$")).Should().BeTrue();
		plausible.All(f => !string.IsNullOrWhiteSpace(f.Owner)).Should().BeTrue();

		var adversarial = Draw<Form>(AutoMode.Adversarial, 600);

		adversarial.Select(f => f.Rating).Should().Contain(0).And.Contain(1).And.Contain(5).And.Contain(6);
		adversarial.Select(f => f.Code.Length).Should().Contain(2).And.Contain(3).And.Contain(10).And.Contain(11);
	}

	[Test]
	public void IPAddress_Version_Int128_BigInteger_Half_and_Rune_are_built_with_edge_values()
	{
		var plausible = Draw<Wide>(AutoMode.Plausible);

		plausible.All(w => w.Address != null && w.Release != null).Should().BeTrue();

		var adversarial = Draw<Wide>(AutoMode.Adversarial, 600);

		adversarial.Any(w => IPAddress.IsLoopback(w.Address) || w.Address.Equals(IPAddress.Any)).Should().BeTrue();
		adversarial.Any(w => Half.IsNaN(w.Small)).Should().BeTrue();
		adversarial.Any(w => w.Big == Int128.MinValue || w.Big == Int128.MaxValue).Should().BeTrue();
		adversarial.Any(w => BigInteger.Abs(w.Huge) > BigInteger.Pow(2, 64)).Should().BeTrue();
	}

	[Test]
	public void At_the_nesting_limit_a_non_nullable_object_is_the_smallest_valid_value_not_null()
	{
		var outers = Draw<Outer>(AutoMode.Plausible, 100, o => o.MaxDepth = 1);

		outers.All(o => o.Inner != null).Should().BeTrue();
		outers.All(o => o.Inner.Deep != null && o.Inner.Deep.Y.Length > 0).Should().BeTrue();
	}

	[Test]
	public void Nullable_elements_are_sometimes_null_and_non_nullable_elements_never_are()
	{
		var values = Draw<MaybeNames>(AutoMode.Mixed, 600);

		values.SelectMany(v => v.Maybe).Any(x => x == null).Should().BeTrue();
		values.SelectMany(v => v.Always).All(x => x != null).Should().BeTrue();
	}

	private static bool DanglingOrder(Shop s) => s.Orders.Any(o => s.Customers.All(c => c.Id != o.CustomerId));

	[Test]
	public void Foreign_keys_between_two_collections_of_one_object_are_valid_in_plausible_data_and_dangle_in_adversarial_data()
	{
		var plausible = Draw<Shop>(AutoMode.Plausible, 500).Where(s => s.Customers.Count > 0 && s.Orders.Count > 0).ToList();

		plausible.Should().NotBeEmpty();
		plausible.All(s => !DanglingOrder(s)).Should().BeTrue();
		Draw<Shop>(AutoMode.Adversarial, 800).Where(s => s.Customers.Count > 0).Any(DanglingOrder).Should().BeTrue("an order for a customer that is not there");
	}

	private static bool Includes(DocSet set, Func<Doc, IEnumerable<string>, bool> bad) => set.Docs.Any(d => bad(d, d.IncludedIds));

	private static bool HasCycle(DocSet set)
	{
		var byId = set.Docs.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());

		bool Reaches(string from, string target, HashSet<string> seen) =>
			byId.TryGetValue(from, out var doc) && doc.IncludedIds.Any(next => next == target || (seen.Add(next) && Reaches(next, target, seen)));

		return set.Docs.Any(d => Reaches(d.Id, d.Id, []));
	}

	[Test]
	public void Lists_of_keys_are_valid_graphs_in_plausible_data_and_reach_dangling_self_and_cyclic_includes_in_adversarial_data()
	{
		var plausible = Draw<DocSet>(AutoMode.Plausible, 600).Where(s => s.Docs.Select(d => d.Id).Distinct().Count() == s.Docs.Count).ToList();

		plausible.Should().NotBeEmpty();
		plausible.All(s => !Includes(s, (d, ids) => ids.Any(i => s.Docs.All(x => x.Id != i)))).Should().BeTrue("no dangling include");
		plausible.All(s => !HasCycle(s)).Should().BeTrue("no cycle");

		var adversarial = Draw<DocSet>(AutoMode.Adversarial, 1000);

		adversarial.Any(s => Includes(s, (d, ids) => ids.Any(i => s.Docs.All(x => x.Id != i)))).Should().BeTrue("an include of nothing");
		adversarial.Any(s => Includes(s, (d, ids) => ids.Contains(d.Id))).Should().BeTrue("a doc that includes itself");
		adversarial.Any(s => HasCycle(s) && !Includes(s, (d, ids) => ids.Contains(d.Id))).Should().BeTrue("a longer cycle");
	}

	[Test]
	public void Abstract_records_and_interfaces_are_built_as_each_of_their_concrete_subtypes()
	{
		var inboxes = Draw<Inbox>(AutoMode.Plausible, 300);

		var notices = inboxes.SelectMany(i => i.Notices).ToList();
		notices.Any(n => n is Notice.Info).Should().BeTrue();
		notices.Any(n => n is Notice.Alert).Should().BeTrue();
		inboxes.Any(i => i.Pet is Dog).Should().BeTrue();
		inboxes.Any(i => i.Pet is Cat).Should().BeTrue();
		inboxes.All(i => i.Pet is not null && i.Part is not null).Should().BeTrue();
	}

	[Test]
	public void A_JsonDerivedType_list_restricts_the_subtypes()
	{
		var parts = Draw<Inbox>(AutoMode.Plausible, 300).Select(i => i.Part).ToList();

		parts.All(p => p is Wheel).Should().BeTrue("only Wheel is listed, Bolt is not");
	}

	[Test]
	public void An_interface_nothing_implements_still_says_what_to_do()
	{
		var act = () => GenAuto.Of<IUnimplemented>(AutoMode.Plausible).Generate(new PCG(1u, 1UL), null, out _);

		act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("AutoOptions.Use");
	}
}
