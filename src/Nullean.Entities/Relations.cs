// Nullean.Entities — a relation between members, declared once and read twice.
//
// "A docset's codex group is a group the codex defines" is a rule, and it is also a fact a generator needs to draw data that already holds it. Written
// as two things they drift apart. A Relation is declared once: Rule() builds the rule from it, and the rule's shape carries what it says (which member
// refers to which key) so a test library can draw data that holds it, and break it on purpose to show the rule can fire.
//
// Nothing here compiles an expression: the rule runs the delegates it was given. The member names come from the text of the lambda the caller wrote
// ([CallerArgumentExpression]), which is read once, when the relation is declared, and only when a generator asks for it. A projection that is not a
// plain member access ("d => d.Products") has no member name, and says so instead of being guessed at.

using System.Runtime.CompilerServices;

namespace Nullean.Entities;

/// <summary>The kinds of relation a rule can be declared from.</summary>
public enum RelationKind
{
	/// <summary>One member holds one key, and a target has that key.</summary>
	Reference,

	/// <summary>One member holds many keys, and a target has each.</summary>
	ReferenceList,

	/// <summary>A member of each item is unique among all the items.</summary>
	Identity
}

/// <summary>
/// What a relation says, without its delegates. <see cref="FromMember"/> and <see cref="KeyMember"/> are null when the lambda they were read from is not a plain
/// member access; <see cref="Problem"/> then says which, so a generator can refuse loudly instead of ignoring the relation.
/// </summary>
public sealed record RelationInfo(RelationKind Kind, Type FromType, string? FromMember, Type? ToType, string? KeyMember, Type KeyType, string? Problem = null);

/// <summary>The shape of a rule declared from a <see cref="Relation"/>: an invariant that also says which members it relates.</summary>
public sealed record RelationShape<T>(RelationInfo Info) : ConstraintShape<T>
{
	public override Axis Axis => Axis.Invariant;

	public override RelationInfo? Relation => Info;

	internal override ConstraintShape<TOther> Map<TOther>(Func<TOther, T> project, Func<TOther, T, TOther>? set) =>
		new RelationShape<TOther>(Info) { Factory = Factory };
}

/// <summary>Declares relations between members. See the file header for what it is for.</summary>
public static class Relation
{
	/// <summary>
	/// Every holder's <paramref name="from"/> key (null or empty means it refers to nothing, which is not a violation) is the <paramref name="key"/> of one of the
	/// <paramref name="targets"/>. <paramref name="holders"/> and <paramref name="targets"/> say where to look in the root.
	/// </summary>
	public static Relation<TRoot, TFrom, TTo, TKey> Reference<TRoot, TFrom, TTo, TKey>(
		Func<TRoot, IEnumerable<TFrom>> holders,
		Func<TFrom, TKey?> from,
		Func<TRoot, IEnumerable<TTo>> targets,
		Func<TTo, TKey> key,
		IEqualityComparer<TKey>? comparer = null,
		[CallerArgumentExpression(nameof(holders))] string? holdersText = null,
		[CallerArgumentExpression(nameof(from))] string? fromText = null,
		[CallerArgumentExpression(nameof(key))] string? keyText = null
	) =>
		new(
			RelationKind.Reference,
			holders,
			h => from(h) is { } k && !(k is string { Length: 0 }) ? [k] : [],
			targets,
			key,
			comparer,
			holdersText,
			Describe(RelationKind.Reference, typeof(TFrom), fromText, typeof(TTo), keyText, typeof(TKey))
		);

	/// <summary>Like <see cref="Reference{TRoot, TFrom, TTo, TKey}"/>, for a member that holds a list of keys.</summary>
	public static Relation<TRoot, TFrom, TTo, TKey> ReferenceList<TRoot, TFrom, TTo, TKey>(
		Func<TRoot, IEnumerable<TFrom>> holders,
		Func<TFrom, IEnumerable<TKey>?> from,
		Func<TRoot, IEnumerable<TTo>> targets,
		Func<TTo, TKey> key,
		IEqualityComparer<TKey>? comparer = null,
		[CallerArgumentExpression(nameof(holders))] string? holdersText = null,
		[CallerArgumentExpression(nameof(from))] string? fromText = null,
		[CallerArgumentExpression(nameof(key))] string? keyText = null
	) =>
		new(
			RelationKind.ReferenceList,
			holders,
			h => from(h) ?? [],
			targets,
			key,
			comparer,
			holdersText,
			Describe(RelationKind.ReferenceList, typeof(TFrom), fromText, typeof(TTo), keyText, typeof(TKey))
		);

	/// <summary>
	/// <paramref name="key"/> is unique among the <paramref name="items"/> (the same as <c>Constraints&lt;T&gt;.Unique</c>), and says so to a generator: wherever an item of this
	/// type is generated, its key is different from every other's.
	/// </summary>
	public static Constraint<TRoot, TKey> Unique<TRoot, TItem, TKey>(
		Func<TRoot, IEnumerable<TItem>> items,
		Func<TItem, TKey> key,
		IEqualityComparer<TKey>? comparer = null,
		string? message = null,
		[CallerArgumentExpression(nameof(items))] string? itemsText = null,
		[CallerArgumentExpression(nameof(key))] string? keyText = null
	)
	{
		var rule = Constraints<TRoot>.Unique(items, key, comparer, message, itemsExpression: itemsText);
		_ = rule.WithShape(new RelationShape<TRoot>(
			new RelationInfo(RelationKind.Identity, typeof(TItem), MemberOf(keyText, out var problem), null, null, typeof(TKey), problem)
		)

		{
			Factory = nameof(Unique)
		});
		return rule;
	}

	private static RelationInfo Describe(RelationKind kind, Type from, string? fromText, Type to, string? keyText, Type keyType)
	{
		var fromMember = MemberOf(fromText, out var fromProblem);
		var keyMember = MemberOf(keyText, out var keyProblem);
		return new RelationInfo(kind, from, fromMember, to, keyMember, keyType, fromProblem ?? keyProblem);
	}

	// "d => d.Products", "(Docset d) => d.CodexGroup!" -> "Products" / "CodexGroup". Anything else is not a member name, and is reported as such.
	internal static string? MemberOf(string? lambda, out string? problem)
	{
		problem = null;
		var arrow = lambda?.IndexOf("=>", StringComparison.Ordinal) ?? -1;
		if (lambda is null || arrow < 0)
		{
			problem = $"'{lambda}' is not a lambda, so no member name can be read from it";
			return null;
		}
		var parameter = lambda[..arrow].Trim().Trim('(', ')').Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
		var body = lambda[(arrow + 2)..].Trim().TrimEnd('!', '?', ' ');
		if (parameter is not null && body.StartsWith(parameter + ".", StringComparison.Ordinal))
		{
			var member = body[(parameter.Length + 1)..];
			if (member.Length > 0 && member.All(c => char.IsLetterOrDigit(c) || c == '_'))
			{
				return member;
			}
		}
		problem = $"'{lambda}' is not a plain member access (x => x.Member), so a generator cannot tell which member it reads";
		return null;
	}
}

/// <summary>A relation declared with <see cref="Relation"/>. <see cref="Rule"/> builds the rule; <see cref="Info"/> is what it says.</summary>
public sealed class Relation<TRoot, TFrom, TTo, TKey>
{
	private readonly Func<TRoot, IEnumerable<TFrom>> _holders;
	private readonly Func<TFrom, IEnumerable<TKey>> _keysOf;
	private readonly Func<TRoot, IEnumerable<TTo>> _targets;
	private readonly Func<TTo, TKey> _key;
	private readonly IEqualityComparer<TKey>? _comparer;
	private readonly string? _holdersText;

	internal Relation(
		RelationKind kind,
		Func<TRoot, IEnumerable<TFrom>> holders,
		Func<TFrom, IEnumerable<TKey>> keysOf,
		Func<TRoot, IEnumerable<TTo>> targets,
		Func<TTo, TKey> key,
		IEqualityComparer<TKey>? comparer,
		string? holdersText,
		RelationInfo info
	)
	{
		Kind = kind;
		_holders = holders;
		_keysOf = keysOf;
		_targets = targets;
		_key = key;
		_comparer = comparer;
		_holdersText = holdersText;
		Info = info;
	}

	public RelationKind Kind { get; }

	/// <summary>What the relation says, readable without running it.</summary>
	public RelationInfo Info { get; }

	/// <summary>
	/// The rule. Chain <c>WithSeverity</c> and <c>Describe</c> as on any other. <paramref name="describe"/> words one dangling reference (the holder and the key it names);
	/// <paramref name="at"/> says where in the source the holder came from.
	/// </summary>
	public Constraint<TRoot, TKey> Rule(string? message = null, Func<TFrom, TKey, string>? describe = null, Func<TFrom, SourceLocation?>? at = null)
	{
		var rule = Constraints<TRoot>.References(_holders, _keysOf, root => _targets(root).Select(_key), _comparer, message, at, describe, _holdersText);
		_ = rule.WithShape(new RelationShape<TRoot>(Info) { Factory = nameof(Constraints<>.References) });
		return rule;
	}
}
