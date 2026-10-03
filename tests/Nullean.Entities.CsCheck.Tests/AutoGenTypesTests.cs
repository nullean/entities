using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

internal sealed record Collections(
	ImmutableArray<int> Array,
	ImmutableList<string> List,
	IImmutableList<int> InterfaceList,
	ImmutableHashSet<int> HashSet,
	FrozenSet<int> Frozen,
	ReadOnlyCollection<int> ReadOnly,
	ImmutableDictionary<string, int> Dictionary,
	FrozenDictionary<string, int> FrozenDictionary,
	Tuple<int, string> Tuple,
	KeyValuePair<string, int> Pair,
	(int Number, string Word) ValueTuple
);

internal sealed record Person(string Id, string? ManagerId, string Name);

internal sealed record Org(IReadOnlyList<Person> People);

public class AutoGenTypesTests
{
	private static IEnumerable<T> Draw<T>(AutoMode mode, int count = 400) =>
		Enumerable.Range(0, count).Select(i => GenAuto.Of<T>(mode).Generate(new PCG(1u, (ulong)i + 1), null, out _));

	[Test]
	public void Immutable_frozen_and_read_only_collections_are_built_in_every_mode()
	{
		foreach (var mode in new[] { AutoMode.Plausible, AutoMode.Mixed, AutoMode.Adversarial })
		{
			var values = Draw<Collections>(mode).ToList();

			values.Should().OnlyContain(c => !c.Array.IsDefault, $"{mode}: an ImmutableArray must be a real array, not default");
			values.Should().Contain(c => c.Array.Length > 0);
			values.Should().Contain(c => c.List.Count > 0);
			values.Should().Contain(c => c.InterfaceList.Count > 0);
			values.Should().Contain(c => c.HashSet.Count > 0);
			values.Should().Contain(c => c.Frozen.Count > 0);
			values.Should().Contain(c => c.ReadOnly.Count > 0);
			values.Should().Contain(c => c.Dictionary.Count > 0);
			values.Should().Contain(c => c.FrozenDictionary.Count > 0);
		}
	}

	[Test]
	public void System_Tuple_KeyValuePair_and_ValueTuple_are_built_through_their_constructors()
	{
		var values = Draw<Collections>(AutoMode.Plausible).ToList();

		values.Should().OnlyContain(c => c.Tuple != null && c.Tuple.Item2.Length > 0);
		values.Should().OnlyContain(c => c.Pair.Key.Length > 0 && c.ValueTuple.Word.Length > 0);
	}

	[Test]
	public void Each_collection_type_can_also_be_asked_for_directly()
	{
		Draw<ImmutableArray<string>>(AutoMode.Mixed).Should().OnlyContain(a => !a.IsDefault);
		Draw<FrozenSet<int>>(AutoMode.Mixed).Should().Contain(s => s.Count > 0);
		Draw<ImmutableDictionary<string, int>>(AutoMode.Mixed).Should().Contain(d => d.Count > 0);
		Draw<Tuple<int, string>>(AutoMode.Mixed).Should().OnlyContain(t => t != null);
	}

	private static bool Dangling(Org o) => o.People.Any(p => p.ManagerId is not null && o.People.All(q => q.Id != p.ManagerId));

	private static bool SelfManaged(Org o) => o.People.Any(p => p.ManagerId == p.Id);

	private static bool Cyclic(Org o)
	{
		var byId = o.People.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
		foreach (var start in o.People)
		{
			var seen = new HashSet<string>();
			var at = start;
			while (at.ManagerId is not null && byId.TryGetValue(at.ManagerId, out var next) && next != at)
			{
				if (!seen.Add(at.Id) || next.Id == start.Id)
				{
					return true;
				}
				at = next;
			}
		}
		return false;
	}

	[Test]
	public void Plausible_foreign_keys_always_point_at_an_earlier_item_or_nothing()
	{
		var orgs = Draw<Org>(AutoMode.Plausible, 600)
			.Where(o => o.People.Count > 1 && o.People.Select(p => p.Id).Distinct().Count() == o.People.Count)
			.ToList();

		orgs.Should().NotBeEmpty();
		orgs.Should().OnlyContain(o => !Dangling(o) && !SelfManaged(o) && !Cyclic(o));
	}

	[Test]
	public void Adversarial_foreign_keys_reach_dangling_self_and_cyclic_references()
	{
		var orgs = Draw<Org>(AutoMode.Adversarial, 800).ToList();

		orgs.Should().Contain(o => Dangling(o), "a child pointing at nothing");
		orgs.Should().Contain(o => SelfManaged(o), "a child pointing at itself");
		orgs.Should().Contain(o => Cyclic(o), "a cycle");
	}
}
