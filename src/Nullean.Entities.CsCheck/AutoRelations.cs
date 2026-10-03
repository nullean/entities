// Nullean.Entities.CsCheck — relations declared to AutoGen.
//
// The name heuristics (`Id`, `<Element>Id`, `*Ids`) find the references a model spells in the obvious way. A real model has others: a list of ids on one
// object that point into a collection on another, a URI whose scheme is a repository, a dictionary keyed by its entity's own name, a path that must be
// unique across every level of a tree. Those are declared, with expressions so the member names are checked by the compiler:
//
//   GenAuto.Of<Pair>(AutoMode.Mixed, o => o
//       .References<Docset, Product, string>(d => d.Products, p => p.Id)
//       .Reference<Mount, Repo, string, Uri>(m => m.Source, r => r.Name, name => new Uri($"{name}://x"))
//       .KeyedBy<Assembly, string, Repo>(a => a.Repositories, r => r.Name)
//       .Identity<TocNode, string>(n => n.Path));
//
// How it works: one scope per generated root value tracks, for each relation, the keys that exist (drawn for the target's key member) and the keys a
// reference has promised. A reference draws an existing key (valid); if none exists yet it promises one, and the target, drawn later, takes it. So the
// order the members are generated in does not matter. In Mixed and Adversarial data some references are drawn dangling instead, which is the case the
// rules exist for.

using System.Collections;
using System.Linq.Expressions;
using CsCheck;

namespace Nullean.Entities.CsCheck;

internal enum RelationKind
{
	Reference,
	ReferenceList,
	KeyedBy,
	Identity,
}

internal sealed class RelationDecl
{
	public required RelationKind Kind { get; init; }

	/// <summary>The type holding the referring member (or the dictionary, or the identity member).</summary>
	public required Type From { get; init; }

	public required string FromMember { get; init; }

	/// <summary>The type whose key is referred to.</summary>
	public Type? To { get; init; }

	public string? ToKeyMember { get; init; }

	public required Type KeyType { get; init; }

	/// <summary>Builds the referring member's value from a key (a URI from a repository name), when the member is not the key itself.</summary>
	public Func<object?, object?>? Make { get; init; }
}

internal sealed class RelationScope
{
	[ThreadStatic]
	private static RelationScope? CurrentSlot;

	public static RelationScope? Current { get => CurrentSlot; set => CurrentSlot = value; }

	private readonly Dictionary<RelationDecl, RelationState> _states = [];

	public RelationState For(RelationDecl decl)
	{
		if (!_states.TryGetValue(decl, out var state))
		{
			_states[decl] = state = new RelationState();
		}
		return state;
	}

	public bool HasPending(IEnumerable<RelationDecl> decls) => decls.Any(d => For(d).Pending.Count > 0);
}

internal sealed class RelationState
{
	/// <summary>Keys a target has been generated with.</summary>
	public List<object> Existing { get; } = [];

	/// <summary>Keys a reference has promised and no target has taken yet.</summary>
	public List<object> Pending { get; } = [];

	/// <summary>Keys drawn on purpose as references to nothing: a target must not take them.</summary>
	public HashSet<object> Dangling { get; } = [];

	/// <summary>A few values an identity member is drawn from, so the same one appears at different levels of a tree.</summary>
	public List<object?> Pool { get; } = [];
}

/// <summary>
/// State shared by every generator while ONE root value is built: a file-system world that every <c>IFileInfo</c> member of the value belongs to, for example.
/// A generator registered with <see cref="AutoOptions.Use{T}(Gen{T})"/> asks for its state with <see cref="GetOrAdd{T}"/>; the scope is created around the root draw
/// when <see cref="AutoOptions.NeedsScope"/> was called (or a relation was declared), and is null outside one.
/// </summary>
public sealed class AutoScope
{
	[ThreadStatic]
	private static AutoScope? CurrentSlot;

	public static AutoScope? Current { get => CurrentSlot; internal set => CurrentSlot = value; }

	private readonly Dictionary<object, object> _items = [];

	/// <summary>The value stored under <paramref name="key"/> for this root, created by <paramref name="create"/> on first use.</summary>
	public T GetOrAdd<T>(object key, Func<T> create) where T : class
	{
		if (!_items.TryGetValue(key, out var value))
		{
			_items[key] = value = create();
		}
		return (T)value;
	}
}

/// <summary>Opens a scope around one root value, so every relation, and every generator that asked for shared state, sees the same keys while it is built.</summary>
internal sealed class ScopedGen(Gen<object?> inner) : Gen<object?>
{
	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		var previous = RelationScope.Current;
		var previousScope = AutoScope.Current;
		RelationScope.Current ??= new RelationScope();
		AutoScope.Current ??= new AutoScope();
		try
		{
			return inner.Generate(pcg, min, out size);
		}
		finally
		{
			RelationScope.Current = previous;
			AutoScope.Current = previousScope;
		}
	}
}

/// <summary>The target's key member: takes a promised key first, otherwise a fresh one, and records it as existing.</summary>
internal sealed class RelationKeyGen(RelationDecl decl, Gen<object?> inner) : Gen<object?>
{
	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		if (RelationScope.Current is not { } scope)
		{
			return inner.Generate(pcg, min, out size);
		}
		var state = scope.For(decl);
		if (state.Pending.Count > 0)
		{
			var promised = state.Pending[^1];
			state.Pending.RemoveAt(state.Pending.Count - 1);
			state.Existing.Add(promised);
			size = new Size(0);
			return promised;
		}
		object? key = null;
		size = new Size(0);
		for (var attempt = 0; attempt < 8; attempt++)
		{
			key = inner.Generate(pcg, min, out size);
			if (key is null || (!state.Dangling.Contains(key) && !state.Existing.Contains(key)))
			{
				break;
			}
		}
		if (key is not null)
		{
			state.Existing.Add(key);
		}
		return key;
	}
}

/// <summary>The referring member: a key that exists, or in the other modes sometimes one that does not.</summary>
internal sealed class RelationRefGen(
	RelationDecl decl,
	Gen<object?> inner,
	Type memberType,
	bool nullable,
	Gen<object?> keyGen,
	AutoOptions options
) : Gen<object?>
{
	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		if (RelationScope.Current is not { } scope)
		{
			return inner.Generate(pcg, min, out size);
		}
		size = new Size(0);
		var state = scope.For(decl);
		if (decl.Kind == RelationKind.ReferenceList)
		{
			AutoBuilder.TryCollection(memberType, out var elementType);
			var items = new List<object?>();
			for (var i = pcg.Next(4); i > 0; i--)
			{
				if (Key(pcg, state) is { } reference)
				{
					items.Add(reference);
				}
			}
			return AutoBuilder.Materialize(memberType, elementType, items);
		}
		if (nullable && options.Mode != AutoMode.Plausible && pcg.Next(100) < 15)
		{
			return null;
		}
		var key = Key(pcg, state);
		if (decl.Make is not { } make)
		{
			return key;
		}
		try
		{
			return make(key);
		}
		catch (Exception e) when (e is FormatException or ArgumentException or UriFormatException)
		{
			// a key the member cannot be built from (a repository name that is not a legal URI): the member is drawn on its own instead
			return inner.Generate(pcg, null, out _);
		}
	}

	private object? Key(PCG pcg, RelationState state)
	{
		var dangling = options.Mode switch
		{
			AutoMode.Plausible => false,
			AutoMode.Adversarial => pcg.Next(100) < 60,
			_ => pcg.Next(100) < 25,
		};
		if (dangling)
		{
			var missing = Fresh(pcg, state);
			if (missing is not null)
			{
				state.Dangling.Add(missing);
			}
			return missing;
		}
		if (decl.From == decl.To)
		{
			// A relation from a type to itself (an include graph, a parent link). Plausible data refers only to EARLIER items, so it is a forest or a DAG and never a cycle;
			// the other modes also refer to the item itself, and forward to an item not generated yet, which is what closes a cycle. The key member is drawn before the
			// referring member, so the last key that exists is this item's own.
			var roll = (int)pcg.Next(100);
			var self = options.Mode switch
			{
				AutoMode.Plausible => 0,
				AutoMode.Adversarial => 20,
				_ => 10
			};
			var forward = options.Mode switch
			{
				AutoMode.Plausible => 0,
				AutoMode.Adversarial => 25,
				_ => 12
			};
			if (state.Existing.Count > 0 && roll < self)
			{
				return state.Existing[^1];
			}
			if (roll < self + forward)
			{
				var ahead = Fresh(pcg, state);
				if (ahead is not null)
				{
					state.Pending.Add(ahead);
				}
				return ahead;
			}
			var earlier = state.Existing.Count - 1;
			return earlier > 0 ? state.Existing[(int)pcg.Next((uint)earlier)] : null;
		}
		if (state.Existing.Count > 0)
		{
			return state.Existing[(int)pcg.Next((uint)state.Existing.Count)];
		}
		if (state.Pending.Count > 0 && pcg.Next(2) == 0)
		{
			return state.Pending[(int)pcg.Next((uint)state.Pending.Count)];
		}
		var promised = Fresh(pcg, state);
		if (promised is not null)
		{
			state.Pending.Add(promised);
		}
		return promised;
	}

	private object? Fresh(PCG pcg, RelationState state)
	{
		object? key = null;
		for (var attempt = 0; attempt < 8; attempt++)
		{
			key = keyGen.Generate(pcg, null, out _);
			if (key is null || (!state.Existing.Contains(key) && !state.Pending.Contains(key) && !state.Dangling.Contains(key)))
			{
				break;
			}
		}
		return key;
	}
}

/// <summary>A dictionary whose keys are its entities' own keys; in the other modes some entries keep a different key, the drift the rule looks for.</summary>
internal sealed class KeyedDictionaryGen(RelationDecl decl, Gen<object?> inner, Type memberType, AutoOptions options) : Gen<object?>
{
	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		var dictionary = inner.Generate(pcg, min, out size);
		if (dictionary is not IEnumerable entries || !AutoBuilder.TryDictionary(memberType, out var keyType, out var valueType))
		{
			return dictionary;
		}
		var pairs = new List<(object?, object?)>();
		foreach (var entry in entries)
		{
			var entryType = entry!.GetType();
			var key = entryType.GetProperty("Key")!.GetValue(entry);
			var value = entryType.GetProperty("Value")!.GetValue(entry);
			var own = value?.GetType().GetProperty(
				decl.ToKeyMember!,
				System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase
			)?.GetValue(value);
			var drift = options.Mode switch
			{
				AutoMode.Plausible => false,
				AutoMode.Adversarial => pcg.Next(100) < 50,
				_ => pcg.Next(100) < 20,
			};
			pairs.Add((own is not null && !drift ? own : key, value));
		}
		return AutoBuilder.MaterializeDictionary(memberType, keyType, valueType, pairs);
	}
}

/// <summary>An identity member drawn, in the other modes, from a few values shared by the whole root, so duplicates appear across the levels of a tree.</summary>
internal sealed class PoolGen(RelationDecl decl, Gen<object?> inner, AutoOptions options) : Gen<object?>
{
	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		var value = inner.Generate(pcg, min, out size);
		if (RelationScope.Current is not { } scope)
		{
			return value;
		}
		if (options.Mode == AutoMode.Plausible)
		{
			// Plausible data keeps the identity unique across the whole root, so a rule against duplicates holds.
			var seen = scope.For(decl).Existing;
			for (var attempt = 0; attempt < 12 && value is not null && seen.Contains(value); attempt++)
			{
				value = inner.Generate(pcg, min, out size);
			}
			if (value is not null)
			{
				seen.Add(value);
			}
			return value;
		}
		var pool = scope.For(decl).Pool;
		var reuse = pool.Count > 0 && pcg.Next(100) < (options.Mode == AutoMode.Adversarial ? 55u : 25u);
		if (reuse)
		{
			return pool[(int)pcg.Next((uint)pool.Count)];
		}
		if (pool.Count < 3)
		{
			pool.Add(value);
		}
		return value;
	}
}

public sealed partial class AutoOptions
{
	internal List<RelationDecl> Relations { get; } = [];

	internal Dictionary<Type, Func<object, object>> Normalizers { get; } = [];

	internal Dictionary<Type, Func<object, IEnumerable<object?>>> EdgeProviders { get; } = [];

	/// <summary>
	/// Edge values for a member of type <typeparamref name="T"/> that depend on the valid value they are swapped into: a file in the file system the baseline already has. <c>Boundaries()</c> and
	/// <c>Laws()</c> (which take the same options) ask for them, one at a time, and swap each into the matching members, or into a one-element collection of them.
	/// </summary>
	public AutoOptions EdgesFrom<T>(Func<object, IEnumerable<T>> edges)
	{
		EdgeProviders[typeof(T)] = baseline => edges(baseline).Select(e => (object?)e);
		return this;
	}

	/// <summary>
	/// <paramref name="fix"/> turns a generated <typeparamref name="T"/> into one that satisfies the invariants only the domain knows: a value derived from other members
	/// (<c>PrivateRepositories</c> is the private subset of <c>AvailableRepositories</c>), which a generator cannot guess. Typically the type's own derivation function, the one the
	/// loader uses. Plausible data is always passed through it, <c>Mixed</c> half of the time, <c>Adversarial</c> never, so the other modes still see the values the invariant forbids.
	/// </summary>
	public AutoOptions Normalize<T>(Func<T, T> fix)
	{
		Normalizers[typeof(T)] = value => fix((T)value)!;
		return this;
	}

	internal bool ScopeRequired { get; private set; }

	/// <summary>
	/// Sizes for a realistic value rather than a quick one: nesting six levels deep and up to eight items per collection (narrowing by one at each level), which gives a
	/// table of contents or navigation of a few dozen nodes. The defaults (three levels, four items) keep property tests fast.
	/// </summary>
	public AutoOptions Large()
	{
		MaxDepth = 6;
		MaxCollection = 8;
		return this;
	}

	/// <summary>
	/// Opens an <see cref="AutoScope"/> around every generated root value. For a generator registered with <see cref="Use{T}(Gen{T})"/> that shares state across
	/// the members of one value (the file-system world of <c>UseFileSystem()</c>).
	/// </summary>
	public AutoOptions NeedsScope()
	{
		ScopeRequired = true;
		return this;
	}

	/// <summary>
	/// <typeparamref name="TFrom"/>'s <paramref name="from"/> member (a key) refers to a <typeparamref name="TTo"/> that has that <paramref name="key"/>, anywhere in
	/// the generated value. Plausible data always refers to one that exists; the other modes sometimes refer to nothing.
	/// </summary>
	public AutoOptions Reference<TFrom, TTo, TKey>(Expression<Func<TFrom, TKey>> from, Expression<Func<TTo, TKey>> key)
	{
		Relations.Add(new RelationDecl
		{
			Kind = RelationKind.Reference,
			From = typeof(TFrom),
			FromMember = MemberName(from),
			To = typeof(TTo),
			ToKeyMember = MemberName(key),
			KeyType = typeof(TKey)
		});
		return this;
	}

	/// <summary>Like <see cref="Reference{TFrom, TTo, TKey}"/>, but the member holds a list of keys (<c>Docset.Products</c> of product ids).</summary>
	public AutoOptions References<TFrom, TTo, TKey>(Expression<Func<TFrom, IEnumerable<TKey>>> from, Expression<Func<TTo, TKey>> key)
	{
		Relations.Add(new RelationDecl
		{
			Kind = RelationKind.ReferenceList,
			From = typeof(TFrom),
			FromMember = MemberName(from),
			To = typeof(TTo),
			ToKeyMember = MemberName(key),
			KeyType = typeof(TKey)
		});
		return this;
	}

	/// <summary>Like <see cref="Reference{TFrom, TTo, TKey}"/>, but the member is built from the key (<c>m.Source</c> is <c>new Uri($"{name}://...")</c>).</summary>
	public AutoOptions Reference<TFrom, TTo, TKey, TValue>(Expression<Func<TFrom, TValue>> from, Expression<Func<TTo, TKey>> key, Func<TKey, TValue> make)
	{
		Relations.Add(new RelationDecl
		{
			Kind = RelationKind.Reference,
			From = typeof(TFrom),
			FromMember = MemberName(from),
			To = typeof(TTo),
			ToKeyMember = MemberName(key),
			KeyType = typeof(TKey),
			Make = k => make((TKey)k!),
		});
		return this;
	}

	/// <summary>
	/// <typeparamref name="TOwner"/>'s dictionary member is keyed by its values' own <paramref name="key"/>. Plausible data keeps the two equal; the other modes let some drift.
	/// </summary>
	public AutoOptions KeyedBy<TOwner, TKey, TValue>(
		Expression<Func<TOwner, IEnumerable<KeyValuePair<TKey, TValue>>>> dictionary,
		Expression<Func<TValue, TKey>> key
	)
	{
		Relations.Add(new RelationDecl
		{
			Kind = RelationKind.KeyedBy,
			From = typeof(TOwner),
			FromMember = MemberName(dictionary),
			To = typeof(TValue),
			ToKeyMember = MemberName(key),
			KeyType = typeof(TKey)
		});
		return this;
	}

	/// <summary>
	/// <typeparamref name="T"/>'s <paramref name="key"/> must be unique among the instances of one generated value, wherever they sit (every level of a tree). The other
	/// modes reuse a few values across levels, so a rule that forbids duplicates has something to find.
	/// </summary>
	public AutoOptions Identity<T, TKey>(Expression<Func<T, TKey>> key)
	{
		Relations.Add(new RelationDecl { Kind = RelationKind.Identity, From = typeof(T), FromMember = MemberName(key), KeyType = typeof(TKey) });
		return this;
	}

	private static string MemberName(LambdaExpression expression)
	{
		var body = expression.Body;
		while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
		{
			body = unary.Operand;
		}
		return body is MemberExpression member
			? member.Member.Name
			: throw new ArgumentException($"A relation is declared with a member access such as x => x.Id, not '{expression}'.", nameof(expression));
	}
}
