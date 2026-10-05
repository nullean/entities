// Shapes for the rules real configuration models keep needing, so they read as what they say instead of a hand-written Check:
// a rule per item with a message that names the item, a value from an allowed set, a count, members that go together or exclude each other,
// values that must be in order, and keys that are prefixes of each other.

using System.Runtime.CompilerServices;

namespace Nullean.Entities;

/// <summary>How <see cref="Constraints{T}.NoPrefixes{TItem}"/> decides that one key is a prefix of another.</summary>
public enum PrefixMatch
{
	/// <summary>Whole segments: <c>reference/elasticsearch</c> is a prefix of <c>reference/elasticsearch/query</c>, not of <c>reference/elasticsearch-plugins</c>.</summary>
	Segment,

	/// <summary>Plain text: <c>reference/elasticsearch</c> is also a prefix of <c>reference/elasticsearch-plugins</c>, the way a naive <c>StartsWith</c> sees it.</summary>
	Text,

	/// <summary>Only the ambiguous case: <c>reference/apm</c> and <c>reference/apm-agents</c>, where a text prefix is not a segment prefix. A real nesting (<c>reference/apm/x</c>) is not reported.</summary>
	TextNotSegment,
}

#pragma warning disable CA1000
public static partial class Constraints<T>
{
	/// <summary>
	/// Every item <paramref name="items"/> picks out must satisfy <paramref name="predicate"/>; each one that does not is a violation whose
	/// message is <paramref name="message"/> applied to the item, so it can name it (<c>item =&gt; $"page '{item.Path}' has no title"</c>).
	/// The item is the evidence, and <paramref name="at"/> points the violation at its source line.
	/// </summary>
	public static Constraint<T> Each<TItem>(
		Func<T, IEnumerable<TItem>> items,
		Func<TItem, bool> predicate,
		Func<TItem, string> message,
		Func<TItem, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	) => Each(items, item => predicate(item) ? null : message(item), at, itemsExpression);

	/// <summary>
	/// Every item <paramref name="items"/> picks out is checked by <paramref name="problem"/>, which returns what is wrong with it or null when it is fine.
	/// The shape for a rule that finds out <em>why</em> an item is bad while checking it (a path that does not resolve, a name with a forbidden character).
	/// </summary>
	public static Constraint<T> Each<TItem>(
		Func<T, IEnumerable<TItem>> items,
		Func<TItem, string?> problem,
		Func<TItem, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	)
	{
		var member = MemberOf(itemsExpression);
		return new(
			ConstraintKind.PointInTime,
			(_, next) =>
				[
					.. items(next)
						.Select(item => (Item: item, Problem: problem(item)))
						.Where(r => r.Problem is not null)
						.Select(r => new Violation(r.Problem!) { Member = member, Evidence = r.Item }.At(at?.Invoke(r.Item))),
				]
		);
	}

	/// <summary>
	/// The value <paramref name="select"/> picks out must be one of <paramref name="allowed"/>. The default message lists the allowed values,
	/// so an enum-like string key reads "'local' is not one of dev, edge, staging, prod". A null value is not one of them; guard with
	/// <see cref="When"/> for an optional member.
	/// </summary>
	public static Constraint<T> OneOf<TValue>(
		Func<T, TValue> select,
		IEnumerable<TValue> allowed,
		IEqualityComparer<TValue>? comparer = null,
		string? message = null,
		Func<T, SourceLocation?>? at = null,
		Func<TValue, TValue[], string>? describe = null,
		[CallerArgumentExpression(nameof(select))] string? selectExpression = null
	)
	{
		var list = allowed.ToArray();
		return OneOf(select, _ => list, comparer, message, at, describe, selectExpression);
	}

	/// <summary>The same as the overload with fixed allowed values, where the allowed values come from the value being checked (the keys of another dictionary in the same model).</summary>
	public static Constraint<T> OneOf<TValue>(
		Func<T, TValue> select,
		Func<T, IEnumerable<TValue>> allowed,
		IEqualityComparer<TValue>? comparer = null,
		string? message = null,
		Func<T, SourceLocation?>? at = null,
		Func<TValue, TValue[], string>? describe = null,
		[CallerArgumentExpression(nameof(select))] string? selectExpression = null
	)
	{
		var member = MemberOf(selectExpression);
		return new(ConstraintKind.PointInTime, (_, next) =>
		{
			var list = allowed(next).ToArray();
			var value = select(next);
			return new HashSet<TValue>(list, comparer).Contains(value)
				? []
				: [new Violation(describe?.Invoke(value, list) ?? message ?? NotOneOf(value, list)) { Member = member, Evidence = value }.At(at?.Invoke(next))];
		});
	}

	/// <summary>Every value <paramref name="values"/> picks out must be one of <paramref name="allowed"/>; each stray value is one violation.</summary>
	public static Constraint<T> EachOneOf<TValue>(
		Func<T, IEnumerable<TValue>> values,
		IEnumerable<TValue> allowed,
		IEqualityComparer<TValue>? comparer = null,
		string? message = null,
		Func<TValue, TValue[], string>? describe = null,
		[CallerArgumentExpression(nameof(values))] string? valuesExpression = null
	)
	{
		var list = allowed.ToArray();
		return EachOneOf(values, _ => list, comparer, message, describe, valuesExpression);
	}

	/// <summary>The same as the overload with fixed allowed values, with the allowed values taken from the value being checked.</summary>
	public static Constraint<T> EachOneOf<TValue>(
		Func<T, IEnumerable<TValue>> values,
		Func<T, IEnumerable<TValue>> allowed,
		IEqualityComparer<TValue>? comparer = null,
		string? message = null,
		Func<TValue, TValue[], string>? describe = null,
		[CallerArgumentExpression(nameof(values))] string? valuesExpression = null
	)
	{
		var member = MemberOf(valuesExpression);
		return new(ConstraintKind.PointInTime, (_, next) =>
		{
			var list = allowed(next).ToArray();
			var known = new HashSet<TValue>(list, comparer);
			return [
				.. values(next)
					.Where(v => !known.Contains(v))
					.Select(v => new Violation(describe?.Invoke(v, list) ?? message ?? NotOneOf(v, list)) { Member = member, Evidence = v }),
			];
		});
	}

	private static string NotOneOf<TValue>(TValue value, TValue[] allowed) => $"'{value}' is not one of {string.Join(", ", allowed)}.";

	/// <summary>
	/// The number of items <paramref name="items"/> picks out must be at least <paramref name="min"/> and, when given, at most <paramref name="max"/>.
	/// One violation, with the count as evidence.
	/// </summary>
	public static Constraint<T> CountBetween<TItem>(
		Func<T, IEnumerable<TItem>> items,
		int min = 0,
		int? max = null,
		string? message = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	)
	{
		var member = MemberOf(itemsExpression);
		var expected = (min, max) switch
		{
			(var lo, { } hi) when lo == hi => $"exactly {lo}",
			(var lo, { } hi) when lo > 0 => $"between {lo} and {hi}",
			(_, { } hi) => $"at most {hi}",
			(var lo, null) => $"at least {lo}",
		};
		return new(ConstraintKind.PointInTime, (_, next) =>
		{
			var count = items(next).Count();
			return count >= min && (max is null || count <= max)
				? []
				: [
					new Violation(message ?? $"{member ?? "The collection"} has {count} item{(count == 1 ? "" : "s")}, expected {expected}.")
					{
						Member = member,
						Evidence = count
					}
				];
		});
	}

	/// <summary>Violated when more than one of the named members is set: two ways of saying the same thing (<c>to</c> next to <c>many</c>, an old key next to its replacement).</summary>
	public static Constraint<T> AtMostOne(params (string Name, Func<T, bool> IsSet)[] members) =>
		MemberGroup(members, set => set <= 1, "at most one of");

	/// <summary>Violated unless exactly one of the named members is set: a choice that must be made, and made once.</summary>
	public static Constraint<T> ExactlyOne(params (string Name, Func<T, bool> IsSet)[] members) =>
		MemberGroup(members, set => set == 1, "exactly one of");

	/// <summary>Violated when some but not all of the named members are set: values that only mean something together (a user and its password, a start and an end).</summary>
	public static Constraint<T> AllOrNone(params (string Name, Func<T, bool> IsSet)[] members) =>
		MemberGroup(members, set => set == 0 || set == members.Length, "all or none of");

	private static Constraint<T> MemberGroup((string Name, Func<T, bool> IsSet)[] members, Func<int, bool> holds, string rule)
	{
		var names = string.Join(", ", members.Select(m => m.Name));
		return new(ConstraintKind.PointInTime, (_, next) =>
		{
			var set = members.Where(m => m.IsSet(next)).Select(m => m.Name).ToArray();
			return holds(set.Length)
				? []
				: [new Violation($"Set {rule} {names}; {(set.Length == 0 ? "none is set" : "set: " + string.Join(", ", set))}.") { Evidence = set }];
		});
	}

	/// <summary>
	/// <paramref name="first"/> must not come after <paramref name="second"/> (strictly before, with <paramref name="strict"/>): a minimum below its maximum, a
	/// start before its end, a version lower than the one that replaces it. Compared with <paramref name="comparer"/>, which is where version ordering goes
	/// (<c>Comparer&lt;Version&gt;.Default</c>, a semver comparer). Nothing is checked when either side is null: an open bound is not out of order.
	/// </summary>
	public static Constraint<T> InOrder<TValue>(
		Func<T, TValue> first,
		Func<T, TValue> second,
		bool strict = false,
		IComparer<TValue>? comparer = null,
		string? message = null,
		Func<T, SourceLocation?>? at = null,
		string? member = null,
		[CallerArgumentExpression(nameof(first))] string? firstExpression = null
	)
	{
		comparer ??= Comparer<TValue>.Default;
		member ??= MemberOf(firstExpression);
		return new(ConstraintKind.PointInTime, (_, next) =>
		{
			var a = first(next);
			var b = second(next);
			if ((object?)a is null || (object?)b is null)
			{
				return [];
			}
			var c = comparer.Compare(a, b);
			return strict ? c < 0 ? [] : Out(a, b, next) : c <= 0 ? [] : Out(a, b, next);
		});

		IEnumerable<Violation> Out(TValue a, TValue b, T value) =>
			[
				new Violation(message ?? $"'{a}' must come {(strict ? "strictly " : "")}before '{b}'.") { Member = member, Evidence = (a, b) }.At(
					at?.Invoke(value)
				)
			];
	}

	/// <summary>The items must be listed in <paramref name="key"/> order (ascending; strictly, with <paramref name="strict"/>): a version list newest-last, an ordered table of contents. Each item out of place is one violation.</summary>
	public static Constraint<T> Sorted<TItem, TKey>(
		Func<T, IEnumerable<TItem>> items,
		Func<TItem, TKey> key,
		bool strict = false,
		IComparer<TKey>? comparer = null,
		Func<TItem, string>? describe = null,
		Func<TItem, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	)
	{
		comparer ??= Comparer<TKey>.Default;
		var member = MemberOf(itemsExpression);
		return new(ConstraintKind.PointInTime, (_, next) =>
		{
			var found = new List<Violation>();
			var list = items(next).ToList();
			for (var i = 1; i < list.Count; i++)
			{
				var c = comparer.Compare(key(list[i - 1]), key(list[i]));
				if (strict ? c < 0 : c <= 0)
				{
					continue;
				}
				found.Add(
					new Violation(describe?.Invoke(list[i]) ?? $"'{key(list[i])}' is listed after '{key(list[i - 1])}', which sorts after it.")
					{
						Member = member,
						Evidence = key(list[i])
					}.At(at?.Invoke(list[i]))
				);
			}
			return found;
		});
	}

	/// <summary>
	/// No key may be a prefix of another: mount points that must not nest, URL prefixes that must stay distinct. <paramref name="match"/> says whether
	/// <c>a/b</c> prefixes <c>a/bc</c> (<see cref="PrefixMatch.Text"/>) or only <c>a/b/c</c> (<see cref="PrefixMatch.Segment"/>). An identical key is not a prefix
	/// of itself: use <see cref="Unique{TItem, TKey}"/> for that. One violation per (outer, inner) pair, with the pair as evidence.
	/// </summary>
	public static Constraint<T> NoPrefixes<TItem>(
		Func<T, IEnumerable<TItem>> items,
		Func<TItem, string> key,
		PrefixMatch match = PrefixMatch.Segment,
		char separator = '/',
		StringComparison comparison = StringComparison.Ordinal,
		string? message = null,
		Func<TItem, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	)
	{
		var member = MemberOf(itemsExpression);
		var order = StringComparer.FromComparison(comparison);
		return new(ConstraintKind.PointInTime, (_, next) =>
		{
			var sorted = items(next).Select(i => (Item: i, Key: key(i))).OrderBy(i => i.Key, order).ToList();
			var found = new List<Violation>();
			for (var i = 0; i < sorted.Count; i++)
			{
				for (var j = i + 1; j < sorted.Count && sorted[j].Key.StartsWith(sorted[i].Key, comparison); j++)
				{
					var outer = sorted[i].Key;
					var inner = sorted[j].Key;
					if (inner.Length == outer.Length)
					{
						continue;
					}
					var onSegment = outer.EndsWith(separator) || inner[outer.Length] == separator;
					if (match == PrefixMatch.Segment ? !onSegment : match == PrefixMatch.TextNotSegment && onSegment)
					{
						continue;
					}
					found.Add(
						new Violation(message ?? $"'{outer}' is a prefix of '{inner}'.") { Member = member, Evidence = (outer, inner) }.At(
							at?.Invoke(sorted[j].Item)
						)
					);
				}
			}
			return found;
		});
	}
	/// <summary><see cref="Each{TItem}(Func{T, IEnumerable{TItem}}, Func{TItem, string}, Func{TItem, SourceLocation}, string)"/> where the check also needs the value that holds the items (the catalogue an item refers into, the other items).</summary>
	public static Constraint<T> Each<TItem>(
		Func<T, IEnumerable<TItem>> items,
		Func<T, TItem, string?> problem,
		Func<TItem, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	)
	{
		var member = MemberOf(itemsExpression);
		return new(
			ConstraintKind.PointInTime,
			(_, next) =>
				[
					.. items(next)
						.Select(item => (Item: item, Problem: problem(next, item)))
						.Where(r => r.Problem is not null)
						.Select(r => new Violation(r.Problem!) { Member = member, Evidence = r.Item }.At(at?.Invoke(r.Item))),
				]
		);
	}

	/// <summary>
	/// Every item is checked by <paramref name="check"/>, which returns any number of violations of any severity: one item that breaks several rules,
	/// or a warning for some items and an error for others. A violation without a member gets the collection's; one without a location gets <paramref name="at"/>'s.
	/// </summary>
	public static Constraint<T> EachCheck<TItem>(
		Func<T, IEnumerable<TItem>> items,
		Func<T, TItem, IEnumerable<Violation>> check,
		Func<TItem, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	)
	{
		var member = MemberOf(itemsExpression);
		return new(
			ConstraintKind.PointInTime,
			(_, next) =>
				[
					.. items(next)
						.SelectMany(
							item =>
								check(next, item)
									.Select(v => (v.Member is null ? v with { Member = member } : v) with { Evidence = v.Evidence ?? item })
									.Select(v => v.Line is null ? v.At(at?.Invoke(item)) : v)
						),
				]
		);
	}

	/// <summary>Nothing may be in both <paramref name="first"/> and <paramref name="second"/>: a page both listed and excluded, a key both added and already present. One violation per shared key.</summary>
	public static Constraint<T> Disjoint<TKey>(
		Func<T, IEnumerable<TKey>> first,
		Func<T, IEnumerable<TKey>> second,
		IEqualityComparer<TKey>? comparer = null,
		string? message = null,
		Func<TKey, string>? describe = null,
		[CallerArgumentExpression(nameof(first))] string? firstExpression = null
	)
	{
		var member = MemberOf(firstExpression);
		return new(ConstraintKind.PointInTime, (_, next) =>
		{
			var other = new HashSet<TKey>(second(next), comparer);
			return [
				.. first(next)
					.Distinct(comparer)
					.Where(other.Contains)
					.Select(k => new Violation(describe?.Invoke(k) ?? message ?? $"'{k}' is in both collections.") { Member = member, Evidence = k }),
			];
		});
	}

	/// <summary>Every item must be used: <paramref name="used"/> lists the keys that are referred to, and an item whose key is not among them is an orphan (a declaration nothing uses, a mounted source nothing links to).</summary>
	public static Constraint<T> EachUsed<TItem, TKey>(
		Func<T, IEnumerable<TItem>> items,
		Func<TItem, TKey> key,
		Func<T, IEnumerable<TKey>> used,
		IEqualityComparer<TKey>? comparer = null,
		string? message = null,
		Func<TItem, string>? describe = null,
		Func<TItem, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	)
	{
		var member = MemberOf(itemsExpression);
		return new(ConstraintKind.PointInTime, (_, next) =>
		{
			var seen = new HashSet<TKey>(used(next), comparer);
			return [
				.. items(next)
					.Where(i => !seen.Contains(key(i)))
					.Select(
						i =>
							new Violation(describe?.Invoke(i) ?? message ?? $"'{key(i)}' is never used.") { Member = member, Evidence = key(i) }.At(
								at?.Invoke(i)
							)
					),
			];
		});
	}

	/// <summary><see cref="Monotonic{TValue}"/> for every item of a collection, matched by key across the change: each entry's timestamp or counter may only move one way. Items that are new or gone are not compared.</summary>
	public static Constraint<T> MonotonicPerKey<TItem, TKey, TValue>(
		Func<T, IEnumerable<TItem>> items,
		Func<TItem, TKey> key,
		Func<TItem, TValue> value,
		MonotonicDirection direction,
		string? message = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	) where TKey : notnull
	{
		var member = MemberOf(itemsExpression);
		return new(ConstraintKind.Sequential, (prev, next) =>
		{
			var before = items(prev).GroupBy(key).ToDictionary(g => g.Key, g => value(g.First()));
			return [
				.. items(next)
					.Where(i => before.ContainsKey(key(i)))
					.Select(i => (Key: key(i), Before: before[key(i)], After: value(i)))
					.Where(
						r =>
							direction == MonotonicDirection.NonDecreasing
								? Comparer<TValue>.Default.Compare(r.After, r.Before) < 0
								: Comparer<TValue>.Default.Compare(r.After, r.Before) > 0
					)
					.Select(
						r =>
							new Violation(message ?? $"'{r.Key}' moved from {r.Before} to {r.After}, but may only be {direction}.")
							{
								Member = member,
								Evidence = (r.Before, r.After)
							}
					),
			];
		});
	}
}
#pragma warning restore CA1000

public sealed partial class ConstraintSet<T>
{
	/// <summary>
	/// The same set with the named rule reported at <paramref name="severity"/>: an independent copy, so the set it came from, and every other set that
	/// shares the rule, keep theirs. (<see cref="Constraint{T}.WithSeverity"/> changes the rule it is called on; this changes only the copy.)
	/// </summary>
	public ConstraintSet<T> WithSeverity(string name, Severity severity) => WithSeverities((name, severity));

	/// <summary>The same set with several rules re-leveled: a profile ("treat these as errors in CI") over a shared rule set. A name that is not in the set throws.</summary>
	public ConstraintSet<T> WithSeverities(params (string Name, Severity Severity)[] levels)
	{
		var byName = levels.ToDictionary(l => l.Name, l => l.Severity, StringComparer.Ordinal);
		var unknown = byName.Keys.Where(n => _constraints.All(c => c.Name != n)).ToArray();
		if (unknown.Length > 0)
		{
			throw new ArgumentException($"No constraint named {string.Join(", ", unknown)} in this set.", nameof(levels));
		}
		return new([.. _constraints.Select(c => c.Name is { } n && byName.TryGetValue(n, out var level) ? c.Copy().WithSeverity(level) : c)]);
	}

	/// <summary>The same set with every warning raised to an error (hints and errors are left as they are): a strict mode for a release build.</summary>
	public ConstraintSet<T> Strict() =>
		new([.. _constraints.Select(c => c.Copy().Escalate())]);

	/// <summary>The same set without the named rules: a deliberate suppression, visible where it is made. A name that is not in the set throws.</summary>
	public ConstraintSet<T> Without(params string[] names)
	{
		var unknown = names.Where(n => _constraints.All(c => c.Name != n)).ToArray();
		if (unknown.Length > 0)
		{
			throw new ArgumentException($"No constraint named {string.Join(", ", unknown)} in this set.", nameof(names));
		}
		return new([.. _constraints.Where(c => c.Name is null || !names.Contains(c.Name))]);
	}
}

public sealed partial class Constraint<T>
{
	/// <summary>
	/// A rule that is introduced gently: reported at <paramref name="before"/> until <paramref name="until"/>, at <paramref name="after"/> from then on, by
	/// the <see cref="TimeProvider"/> the rule is checked with (so a test controls the clock). Changes the rule it is called on, like <see cref="WithSeverity"/>.
	/// </summary>
	public Constraint<T> WithSeverityUntil(Severity before, DateTimeOffset until, Severity after)
	{
		var inner = _checkAt;
		_checkAt = (previous, next, time) =>
		{
			var severity = time.GetUtcNow() < until ? before : after;
			return inner(previous, next, time).Select(v => v with { Severity = severity });
		};
		return this;
	}

	// Warnings become errors; everything else is left as it is. Applied to a copy.
	internal Constraint<T> Escalate()
	{
		var inner = _checkAt;
		_checkAt = (previous, next, time) => inner(previous, next, time).Select(v => v.Severity == Severity.Warning ? v with { Severity = Severity.Error } : v);
		return this;
	}
}
