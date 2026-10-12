// Nullean.Entities.CsCheck — find the counter-example a rule needs, so the author only writes the ones a machine cannot.
//
// A counter-example is a valid value changed so that exactly one rule breaks. Most of them are mechanical: a member set to null or blank, a member taken from
// another valid value, an item added, copied or removed, two members swapped, one level or three down. A rule that only applies when a guard is true gets a
// second step: first make the guard true, then break the requirement. This tries those steps from valid values, keeps the first that breaks one rule and no
// other on every base it is tried on (or, failing that, the one that breaks the fewest others), and says in a few words what it changed.
// What is left is the part only the author knows.

namespace Nullean.Entities.CsCheck;

/// <summary>A counter-example the search found: how to apply it to any valid value, a short description of the change, and the other rules it breaks too (none when it breaks only the one asked for).</summary>
internal sealed record Mutant<T>(Func<T, T> Apply, string Description, IReadOnlyList<string> Overlaps);

internal static class CounterExampleSearch
{
	/// <summary>Every value of <paramref name="type"/> found inside <paramref name="root"/>: members, collection items, dictionary values, a few levels down. An argument for an action is best drawn from here.</summary>
	public static List<object> Harvest(object? root, Type type, int maxDepth = 5, int cap = 200)
	{
		var found = new List<object>();
		var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
		Walk(root, 0);
		return found;

		void Walk(object? value, int depth)
		{
			if (value is null || found.Count >= cap || depth > maxDepth)
			{
				return;
			}
			var valueType = value.GetType();
			if (type.IsInstanceOfType(value) && (valueType.IsValueType || seen.Add(value)))
			{
				found.Add(value);
			}
			if (valueType.IsPrimitive || valueType.IsEnum || value is string || (!valueType.IsValueType && !seen.Add(value) && !type.IsInstanceOfType(value)))
			{
				return;
			}
			if (value is System.Collections.IEnumerable items)
			{
				foreach (var item in items.Cast<object?>().Take(50))
				{
					if (item is not null && item.GetType() is
						{ IsGenericType: true } kv && kv.GetGenericTypeDefinition() == typeof(KeyValuePair<string, string>).GetGenericTypeDefinition())
					{
						Walk(kv.GetProperty("Value")!.GetValue(item), depth + 1);
					}
					else
					{
						Walk(item, depth + 1);
					}
				}
				return;
			}
			foreach (var property in valueType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Where(
				p => p.GetIndexParameters().Length == 0 && p.CanRead && p.Name != "EqualityContract"
			))
			{
				try
				{
					Walk(property.GetValue(value), depth + 1);
				}
				catch (Exception e) when (e is not OutOfMemoryException)
				{
					// a computed member that throws is not a place to find a value
				}
			}
		}
	}

	/// <summary>Small changes to a value (one member to an edge value, to another valid value, an item added): what an argument that is almost an existing one looks like.</summary>
	public static List<List<Func<object?, object?>>> Perturbations(Type type, object value)
	{
		try
		{
			var options = new AutoOptions { Mode = AutoMode.Adversarial };
			return [
				.. Steps(new AutoBuilder(options), options, type, value, "", 0, [])
					.OrderBy(s => s.Cost)
					.GroupBy(s => Member(s.Description))
					.Select(g => g.Take(24).Select(s => s.Apply).ToList())
			];
		}
		catch (Exception e) when (e is not OutOfMemoryException)
		{
			return [];
		}
	}

	private const int MaxBases = 6;
	private const int MaxDepth = 3;
	private const int MaxGuardSteps = 24;

	private sealed record Candidate<T>(Func<T, T> Apply, string Description);

	internal sealed record Step(Func<object?, object?> Apply, string Description, int Cost);

	/// <summary>For each rule in <paramref name="wanted"/>, a mutant that breaks it: alone when one exists, else with the fewest other rules. Rules nothing found are absent.</summary>
	public static IReadOnlyDictionary<string, Mutant<T>> Find<T>(
		ConstraintSet<T> constraints,
		IReadOnlyList<T> bases,
		IReadOnlyCollection<string> wanted,
		TimeProvider? time,
		Action<AutoOptions>? configure
	)
	{
		var found = new Dictionary<string, Mutant<T>>();
		if (wanted.Count == 0 || bases.Count == 0)
		{
			return found;
		}
		var options = new AutoOptions { Mode = AutoMode.Adversarial };
		configure?.Invoke(options);
		var builder = new AutoBuilder(options);
		if (builder.Plan(typeof(T), 0).ReadArgs(bases[0]!) is null)
		{
			return found;
		}
		var tried = bases.Take(MaxBases).ToList();
		var fallback = new Dictionary<string, Mutant<T>>();

		var singles = Candidates<T>(builder, options, tried[0]!, [.. tried.Skip(1).Cast<object?>()]).ToList();
		foreach (var candidate in singles)
		{
			Consider(constraints, tried, candidate, wanted, time, found, fallback);
			if (found.Count == wanted.Count)
			{
				return found;
			}
		}
		// A rule that only applies when its guard is true: make the guard true first, then break the requirement.
		foreach (var rule in constraints.AsConstraints().Where(
			c => c.Name is not null && c.Guard is not null && wanted.Contains(c.Name) && !found.ContainsKey(c.Name)
		))
		{
			foreach (var second in GuardedCandidates(builder, options, rule.Guard!, singles, tried))
			{
				Consider(constraints, tried, second, [rule.Name!], time, found, fallback);
				if (found.ContainsKey(rule.Name!))
				{
					break;
				}
			}
		}
		foreach (var (name, mutant) in fallback)
		{
			found.TryAdd(name, mutant);
		}
		return found;
	}

	private static void Consider<T>(
		ConstraintSet<T> constraints,
		List<T> bases,
		Candidate<T> candidate,
		IReadOnlyCollection<string> wanted,
		TimeProvider? time,
		Dictionary<string, Mutant<T>> found,
		Dictionary<string, Mutant<T>> fallback
	)
	{
		var broken = Breaks(constraints, bases, candidate.Apply, time);
		if (broken is null)
		{
			return;
		}
		foreach (var name in broken.Where(wanted.Contains))
		{
			var overlaps = broken.Where(n => n != name).ToList();
			var mutant = new Mutant<T>(candidate.Apply, candidate.Description, overlaps);
			if (overlaps.Count == 0)
			{
				found.TryAdd(name, mutant);
			}
			else if (!fallback.TryGetValue(name, out var existing) || existing.Overlaps.Count > overlaps.Count)
			{
				fallback[name] = mutant;
			}
		}
	}

	// The rules the candidate breaks across the bases it changes, or null when it breaks none or a rule throws on it.
	private static List<string>? Breaks<T>(ConstraintSet<T> constraints, List<T> bases, Func<T, T> apply, TimeProvider? time)
	{
		var ids = new List<string>();
		foreach (var b in bases)
		{
			T changed;
			try
			{
				changed = apply(b);
			}
			catch (Exception e) when (e is not OutOfMemoryException)
			{
				continue;
			}
			if (EqualityComparer<T>.Default.Equals(b, changed))
			{
				continue;
			}
			ids.AddRange(BrokenBy(constraints, b, changed, time));
		}
		var distinct = ids.Distinct().ToList();
		return distinct.Count == 0 ? null : distinct;
	}

	// The rules the change breaks. A rule that throws on a value it never expected (a null member) is left out rather than ruling the whole change out: it cannot be the one this counter-example is for.
	private static List<string> BrokenBy<T>(ConstraintSet<T> constraints, T before, T after, TimeProvider? time)
	{
		try
		{
			return [.. constraints.Check(before, after, time).Select(v => v.ConstraintId ?? "?")];
		}
		catch (Exception e) when (e is not OutOfMemoryException)
		{
			var ids = new List<string>();
			foreach (var rule in constraints.AsConstraints().Where(c => c.Name is not null))
			{
				try
				{
					ids.AddRange(rule.CheckAt(before, after, time ?? TimeProvider.System).Select(_ => rule.Name!));
				}
				catch (Exception inner) when (inner is not OutOfMemoryException)
				{
					// this rule threw: not a rule the change is a counter-example for
				}
			}
			return ids;
		}
	}

	private static IEnumerable<Candidate<T>> GuardedCandidates<T>(
		AutoBuilder builder,
		AutoOptions options,
		Func<T, bool> guard,
		List<Candidate<T>> singles,
		List<T> bases
	)
	{
		var openers = singles.Where(s => SafeGuard(guard, s.Apply, bases[0])).Take(MaxGuardSteps).ToList();
		foreach (var first in openers)
		{
			T opened;
			try
			{
				opened = first.Apply(bases[0]);
			}
			catch (Exception e) when (e is not OutOfMemoryException)
			{
				continue;
			}
			foreach (var second in Candidates<T>(builder, options, opened!, []))
			{
				var (a, b) = (first.Apply, second.Apply);
				yield return new Candidate<T>(v => b(a(v)), $"{first.Description}, then {second.Description}");
			}
		}
	}

	private static bool SafeGuard<T>(Func<T, bool> guard, Func<T, T> apply, T value)
	{
		try
		{
			return guard(apply(value));
		}
		catch (Exception e) when (e is not OutOfMemoryException)
		{
			return false;
		}
	}

	private static IEnumerable<Candidate<T>> Candidates<T>(AutoBuilder builder, AutoOptions options, object sample, List<object?> donors)
	{
		// the cheapest first: one member to an edge value, then taken from another value, then a collection edited, then two members swapped; each also one to three levels down
		foreach (var step in Steps(builder, options, typeof(T), sample, "", 0, donors).OrderBy(step => step.Cost))
		{
			var apply = step.Apply;
			yield return new Candidate<T>(v => (T)apply(v)!, step.Description);
		}
	}

	// "Name = ..." or "Owner.Label = ..." -> "Name" / "Owner": the member a step changes, so a perturbation can pick a member first and a change to it second
	private static string Member(string description)
	{
		var end = 0;
		while (end < description.Length && (char.IsLetterOrDigit(description[end]) || description[end] == '_'))
		{
			end++;
		}
		return description[..end];
	}

	private static string At(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

	internal static IEnumerable<Step> Steps(AutoBuilder builder, AutoOptions options, Type type, object sample, string path, int depth, List<object?> donors)
	{
		ObjectPlan plan;
		object?[]? args;
		try
		{
			plan = builder.Plan(type, depth);
			args = plan.ReadArgs(sample);
		}
		catch (Exception e) when (e is not OutOfMemoryException)
		{
			yield break;
		}
		if (args is null)
		{
			yield break;
		}

		object? Rebuild(object? value, Func<object?[], object?[]> change) => value is null ? null : plan.Construct(change(plan.ReadArgs(value)!));

		var weight = depth * 10;

		for (var i = 0; i < plan.Names.Length; i++)
		{
			var (index, name) = (i, plan.Names[i]);
			// a string that must not be null: the edge catalog has blank and hostile strings, and null only for members that may be null, but a rule that says "required" fires on null
			var edges = plan.Types[i] == typeof(string)
				? ConstraintBoundaries.Edges(builder, options, plan.Types[i], sample).Append(null)
				: ConstraintBoundaries.Edges(builder, options, plan.Types[i], sample);
			// a bool or an enum has no hostile values, but each other value is a change: it may open a guard, or be the one a rule forbids
			var alternatives = plan.Types[i] == typeof(bool)
				? [!(bool)args[i]!]
				: plan.Types[i].IsEnum ? Enum.GetValues(plan.Types[i]).Cast<object?>().Where(v => !Equals(v, args[i])) : [];
			foreach (var edge in edges.Concat(alternatives).Concat(Nearby(args[i])).Distinct())
			{
				var value = edge;
				yield return new Step(v => Rebuild(v, a => With(a, index, value)), $"{At(path, name)} = {ValuePrinter.Print(value)}", weight);
			}
			foreach (var donor in donors)
			{
				var donated = plan.ReadArgs(donor!)?[i];
				if (Equals(donated, args[i]))
				{
					continue;
				}
				yield return new Step(v => Rebuild(v, a => With(a, index, donated)), $"{At(path, name)} taken from another valid value", weight + 1);
			}
			if (AutoBuilder.TryCollection(plan.Types[i], out var element))
			{
				var (type1, element1) = (plan.Types[i], element);
				foreach (var (how, edit) in CollectionEdits())
				{
					yield return new Step(v => Rebuild(v, a => With(a, index, Edit(type1, element1, a[index], edit))), $"{At(path, name)}: {how}", weight + 2);
				}
			}
			if (AutoBuilder.TryDictionary(plan.Types[i], out var keyType, out var valueType) && args[i] is not null)
			{
				var (dictType, keyType1, valueType1) = (plan.Types[i], keyType, valueType);
				yield return new Step(
					v =>
						Rebuild(
							v,
							a => With(a, index, AutoBuilder.MaterializeDictionary(dictType, keyType1, valueType1, [.. DictionaryPairs(a[index]).Skip(1)]))
						),
					$"{At(path, name)}: without its first entry",
					weight + 2
				);
			}
			if (depth >= MaxDepth || Exemplar(plan, i, args[i], donors) is not { } member)
			{
				continue;
			}
			foreach (var inner in Descend(builder, options, plan.Types[i], member, At(path, name), depth + 1))
			{
				var innerApply = inner.Apply;
				yield return new Step(v => Rebuild(v, a => With(a, index, innerApply(a[index]))), inner.Description, inner.Cost);
			}
		}
		for (var i = 0; i < plan.Names.Length; i++)
		{
			for (var j = i + 1; j < plan.Names.Length; j++)
			{
				if (plan.Types[i] != plan.Types[j] || Equals(args[i], args[j]))
				{
					continue;
				}
				var (a0, b0, na, nb) = (i, j, plan.Names[i], plan.Names[j]);
				yield return new Step(
					v => Rebuild(v, a => { var swapped = (object?[])a.Clone(); (swapped[a0], swapped[b0]) = (swapped[b0], swapped[a0]); return swapped; }),
					$"{At(path, na)} and {At(path, nb)} swapped",
					weight + 3
				);
			}
		}
	}

	// the same steps applied to a member's value: the object itself, the first item of a collection, or the first value of a dictionary
	private static IEnumerable<Step> Descend(AutoBuilder builder, AutoOptions options, Type type, object member, string path, int depth)
	{
		if (type == typeof(string) || type.IsPrimitive || type.IsEnum)
		{
			yield break;
		}
		if (AutoBuilder.TryCollection(type, out var element))
		{
			var items = member is System.Collections.IEnumerable all ? all.Cast<object?>().ToList() : [];
			if (items.Count == 0 || items[0] is not { } head || element.IsValueType || element == typeof(string))
			{
				yield break;
			}
			foreach (var inner in Descend(builder, options, element, head, $"{path}[first]", depth))
			{
				var innerApply = inner.Apply;
				yield return new Step(
					current =>
					{
						var list = (current as System.Collections.IEnumerable)?.Cast<object?>().ToList() ?? [];
						if (list.Count > 0)
						{
							list[0] = innerApply(list[0]);
						}
						return AutoBuilder.Materialize(type, element, list);
					},
					inner.Description,
					inner.Cost + 4
				);
				// or leave the first item alone and add a changed copy of it: a second product with another target, a second page with the same path
				yield return new Step(
					current =>
					{
						var list = (current as System.Collections.IEnumerable)?.Cast<object?>().ToList() ?? [];
						if (list.Count > 0)
						{
							list.Add(innerApply(list[0]));
						}
						return AutoBuilder.Materialize(type, element, list);
					},
					$"{path}: add a copy of the first item with {inner.Description}",
					inner.Cost + 5
				);
			}
			yield break;
		}
		if (AutoBuilder.TryDictionary(type, out var keyType, out var valueType))
		{
			var pairs = DictionaryPairs(member);
			if (pairs.Count == 0 || pairs[0].Value is not { } first || valueType.IsValueType || valueType == typeof(string))
			{
				yield break;
			}
			foreach (var inner in Descend(builder, options, valueType, first, $"{path}[first]", depth))
			{
				var innerApply = inner.Apply;
				yield return new Step(
					current =>
					{
						var list = DictionaryPairs(current);
						if (list.Count > 0)
						{
							list[0] = (list[0].Key, innerApply(list[0].Value));
						}
						return AutoBuilder.MaterializeDictionary(type, keyType, valueType, list);
					},
					inner.Description,
					inner.Cost + 5
				);
			}
			yield break;
		}
		foreach (var inner in Steps(builder, options, type, member, path, depth, []))
		{
			yield return inner with { Cost = inner.Cost + 3 };
		}
	}

	private static List<(object? Key, object? Value)> DictionaryPairs(object? dictionary)
	{
		var list = new List<(object?, object?)>();
		if (dictionary is System.Collections.IDictionary map)
		{
			foreach (System.Collections.DictionaryEntry entry in map)
			{
				list.Add((entry.Key, entry.Value));
			}
			return list;
		}
		if (dictionary is System.Collections.IEnumerable pairs)
		{
			foreach (var pair in pairs)
			{
				var type = pair!.GetType();
				list.Add((type.GetProperty("Key")!.GetValue(pair), type.GetProperty("Value")!.GetValue(pair)));
			}
		}
		return list;
	}

	// The values just either side of this one: what an operation that compares ("newer than", "larger than", "after") decides on, which an edge catalog of extremes never lands next to.
	private static IEnumerable<object?> Nearby(object? value) => value switch
	{
		DateTime d when d.Year is > 1 and < 9999 => [d.AddDays(1), d.AddDays(-1)],
		DateTimeOffset d when d.Year is > 1 and < 9999 => [d.AddDays(1), d.AddDays(-1)],
		int n when n is > int.MinValue and < int.MaxValue => [n + 1, n - 1],
		long n when n is > long.MinValue and < long.MaxValue => [n + 1, n - 1],
		double n when double.IsFinite(n) => [n + 1, n - 1],
		decimal n when n is > decimal.MinValue and < decimal.MaxValue => [n + 1, n - 1],
		TimeSpan t when t > TimeSpan.Zero && t < TimeSpan.MaxValue => [t + TimeSpan.FromSeconds(1), t - TimeSpan.FromSeconds(1)],
		_ => []
	};

	// A value to read the shape from: this base's member, or another base's when this one has none (an empty collection tells nothing about its items).
	private static object? Exemplar(ObjectPlan plan, int index, object? own, List<object?> donors)
	{
		if (!IsEmpty(own))
		{
			return own;
		}
		return donors.Select(d => plan.ReadArgs(d!)?[index]).FirstOrDefault(v => !IsEmpty(v));
	}

	private static bool IsEmpty(object? value) =>
		value is null || (value is System.Collections.IEnumerable items and not string && !items.GetEnumerator().MoveNext());

	private static object?[] With(object?[] args, int index, object? value)
	{
		var copy = (object?[])args.Clone();
		copy[index] = value;
		return copy;
	}

	private static IEnumerable<(string How, Func<List<object?>, List<object?>> Edit)> CollectionEdits()
	{
		yield return ("copy the first item", items => items.Count == 0 ? items : [.. items, items[0]]);
		yield return ("remove the first item", items => items.Count == 0 ? items : [.. items.Skip(1)]);
		yield return ("remove the last item", items => items.Count == 0 ? items : [.. items.SkipLast(1)]);
		yield return ("reverse", items => items.Count < 2 ? items : [.. Enumerable.Reverse(items)]);
	}

	private static object? Edit(Type type, Type element, object? current, Func<List<object?>, List<object?>> edit)
	{
		var items = current is System.Collections.IEnumerable all ? all.Cast<object?>().ToList() : [];
		return AutoBuilder.Materialize(type, element, edit(items));
	}
}
