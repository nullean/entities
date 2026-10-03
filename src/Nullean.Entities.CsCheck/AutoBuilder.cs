// Nullean.Entities.CsCheck — AutoBuilder: builds a CsCheck generator for any type from its shape.
//
// Not AutoBogus: this walks constructors, properties, collections, enums and nullability itself, and draws
// every leaf from CsCheck's own PCG. That is what makes it lock-free, shrinkable, and able to mix adversarial
// edge values (EdgeCatalog) in with plausible ones (Corpus, small numbers, dates near 2026).

using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using CsCheck;

namespace Nullean.Entities.CsCheck;

internal sealed class AutoBuilder(AutoOptions options)
{
	private static readonly Type DictionaryDefinition = typeof(Dictionary<int, int>).GetGenericTypeDefinition();
	private static readonly Type IDictionaryDefinition = typeof(IDictionary<int, int>).GetGenericTypeDefinition();
	private static readonly Type IReadOnlyDictionaryDefinition = typeof(IReadOnlyDictionary<int, int>).GetGenericTypeDefinition();
	private static readonly Type ImmutableDictionaryDefinition = typeof(ImmutableDictionary<int, int>).GetGenericTypeDefinition();
	private static readonly Type IImmutableDictionaryDefinition = typeof(IImmutableDictionary<int, int>).GetGenericTypeDefinition();
	private static readonly Type FrozenDictionaryDefinition = typeof(FrozenDictionary<int, int>).GetGenericTypeDefinition();
	private static readonly Type ReadOnlyDictionaryDefinition = typeof(ReadOnlyDictionary<int, int>).GetGenericTypeDefinition();
	private static readonly Type SortedDictionaryDefinition = typeof(SortedDictionary<int, int>).GetGenericTypeDefinition();

	private static readonly Type[] CollectionDefinitions =
	[
		typeof(List<>),
		typeof(IList<>),
		typeof(ICollection<>),
		typeof(IEnumerable<>),
		typeof(IReadOnlyList<>),
		typeof(IReadOnlyCollection<>),
		typeof(HashSet<>),
		typeof(ISet<>),
		typeof(IReadOnlySet<>),
		typeof(ImmutableArray<>),
		typeof(ImmutableList<>),
		typeof(IImmutableList<>),
		typeof(ImmutableHashSet<>),
		typeof(IImmutableSet<>),
		typeof(FrozenSet<>),
		typeof(ReadOnlyCollection<>),
		typeof(Collection<>),
		typeof(SortedSet<>),
	];

	private readonly NullabilityInfoContext _nullability = new();

	public Gen<T> For<T>() => new CastGen<T>(Build(typeof(T), null, false, 0));

	public Gen<object?> ForType(Type type) => Build(type, null, false, 0);

	// ── Dispatch ─────────────────────────────────────────────────────────────

	private Gen<object?> Build(Type type, string? name, bool nullable, int depth, NullabilityInfo? info = null)
	{
		if (options.Custom.TryGetValue(type, out var custom))
		{
			return custom;
		}
		if (Nullable.GetUnderlyingType(type) is { } underlying)
		{
			return new NullGen(Build(underlying, name, false, depth), options.NullPercent);
		}
		var inner = BuildNonNull(type, name, depth, info);
		return nullable && !type.IsValueType ? new NullGen(inner, options.NullPercent) : inner;
	}

	private Gen<object?> BuildNonNull(Type type, string? name, int depth, NullabilityInfo? info = null)
	{
		if (TryLeaf(type, name, out var leaf))
		{
			return leaf;
		}
		if (type.IsEnum)
		{
			return EnumGen(type);
		}
		if (TryDictionary(type, out var keyType, out var valueType))
		{
			return depth >= options.MaxDepth
				? new ConstGen(MaterializeDictionary(type, keyType, valueType, Array.Empty<(object?, object?)>()))
				: new DictionaryGen(type, Build(keyType, "key", false, depth + 1), Build(valueType, "value", false, depth + 1), options);
		}
		if (TryCollection(type, out var elementType))
		{
			if (depth >= options.MaxDepth)
			{
				return new ConstGen(Materialize(type, elementType, []));
			}
			var elementInfo = info?.ElementType ?? info?.GenericTypeArguments.FirstOrDefault();
			var elementsNullable = elementInfo?.ReadState == NullabilityState.Nullable && !elementType.IsValueType;
			return new CollectionGen(type, elementType, Element(elementType, name, depth + 1), options, elementsNullable);
		}
		if (TryUnion(type, depth, out var union))
		{
			return union;
		}
		return Object(type, depth);
	}

	// ── Unions ───────────────────────────────────────────────────────────────
	// A C# union (`union Shape(Circle, Square)`, or the polyfill of it) is a type marked [Union] / implementing IUnion with one public
	// constructor per case type. Each draw picks a case and builds it, so every case is reached, not the one a "most parameters" rule would choose.

	internal static bool IsUnion(Type type) =>
		type.GetCustomAttributes(false).Any(a => a.GetType().FullName == "System.Runtime.CompilerServices.UnionAttribute")
		|| type.GetInterfaces().Any(i => i.FullName == "System.Runtime.CompilerServices.IUnion");

	private bool TryUnion(Type type, int depth, out Gen<object?> gen)
	{
		gen = null!;
		if (!IsUnion(type))
		{
			return false;
		}
		var cases = new List<(ConstructorInfo Constructor, Gen<object?> Value)>();
		foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Where(c => c.GetParameters().Length == 1))
		{
			try
			{
				cases.Add((constructor, Build(constructor.GetParameters()[0].ParameterType, null, false, depth + 1)));
			}
			catch (NotSupportedException)
			{
				// a case AutoGen cannot build is never chosen
			}
		}
		if (cases.Count == 0)
		{
			return false;
		}
		gen = new UnionGen(type, [.. cases]);
		return true;
	}

	// The smallest valid value of a type, for where the nesting limit cuts: a collection is empty, a nested object is built from its own smallest
	// parts. Only a type that contains itself non-nullably ends in null, because there is nothing smaller to build.
	private readonly HashSet<Type> _terminal = [];

	private Gen<object?> Terminal(Type type)
	{
		if (!_terminal.Add(type))
		{
			return new ConstGen(null);
		}
		try
		{
			var plan = Plan(type, options.MaxDepth);
			return new ConstGen(new ObjectGen(plan, options).Generate(new PCG(1u, 1UL), null, out _));
		}
		catch (Exception e) when (e is NotSupportedException or InvalidOperationException)
		{
			return new ConstGen(null);
		}
		finally
		{
			_terminal.Remove(type);
		}
	}

	private (Gen<object?> Value, ObjectPlan? Plan) Element(Type elementType, string? name, int depth)
	{
		var plan = IsObject(elementType) && !IsUnion(elementType) ? Plan(elementType, depth) : null;
		return (plan is null ? Build(elementType, name, false, depth) : new ObjectGen(plan, options), plan);
	}

	private static bool IsObject(Type type) =>
		!type.IsEnum && !type.IsPrimitive && type != typeof(string) && type != typeof(decimal) && type != typeof(Guid) && type != typeof(DateTime) && type != typeof(DateTimeOffset)
		&& type != typeof(DateOnly) && type != typeof(TimeOnly) && type != typeof(TimeSpan) && type != typeof(Uri) && !ExtraLeaves.Handles(
			type
		) && Nullable.GetUnderlyingType(type) is null
		&& !type.IsArray && !TryCollection(type, out _) && !TryDictionary(type, out _, out _);

	// ── Leaves ───────────────────────────────────────────────────────────────

	private Gen<object?> Leaf(Func<PCG, (object?, ulong)> plausible, object?[] edges) => new LeafGen(new FuncGen(plausible), edges, options);

	private static object?[] Boxed<T>(IEnumerable<T> values) => [.. values.Select(v => (object?)v)];

	private bool TryLeaf(Type type, string? name, out Gen<object?> gen)
	{
		gen = null!;
		if (type == typeof(string))
		{
			var words = Corpus.For(name);
			List<string> strings = [.. EdgeCatalog.StringCommon];
			if (options.Mode == AutoMode.Adversarial)
			{
				strings.AddRange(EdgeCatalog.StringHostile);
			}
			strings.AddRange(options.ExtraStringEdges);
			object?[] edges = [.. strings];
			gen = Leaf(pcg =>
			{
				var i = pcg.Next((uint)words.Length);
				return (words[i], i);
			}, edges);
			return true;
		}
		if (type == typeof(bool))
		{
			gen = new FuncGen(pcg => { var b = pcg.Next(2) == 1; return (b, b ? 1UL : 0UL); });
			return true;
		}
		if (type == typeof(char))
		{
			char[] common = ['a', '0', ' ', '-', '\t', '\n', (char)0xFFFF, (char)0x00A0, (char)0x200B];
			List<char> chars = [.. common];
			if (options.Mode == AutoMode.Adversarial)
			{
				chars.AddRange(['\0', (char)0xD800]);
			}
			var edges = Boxed(chars);
			gen = Leaf(pcg => { var c = (char)('a' + pcg.Next(26)); return (c, (ulong)(c - 'a')); }, edges);
			return true;
		}
		var result = IntegerLeaf(type) ?? FloatLeaf(type) ?? TimeLeaf(type);
		if (result is not null)
		{
			gen = result;
			return true;
		}
		if (type == typeof(Guid))
		{
			gen = Leaf(pcg => (new Guid([.. BitConverter.GetBytes(pcg.Next64()), .. BitConverter.GetBytes(pcg.Next64())]), 0UL), Boxed(EdgeCatalog.Guids));
			return true;
		}
		if (ExtraLeaves.For(type, options) is { } extra)
		{
			gen = extra;
			return true;
		}
		if (type == typeof(Uri))
		{
			var words = Corpus.For("word");
			gen = Leaf(
				pcg => { var i = pcg.Next((uint)words.Length); return (new Uri($"https://example.com/{words[i]}"), i); },
				[.. EdgeCatalog.Uris.Select(u => Uri.TryCreate(u, UriKind.RelativeOrAbsolute, out var uri) ? (object?)uri : null).Where(u => u is not null)]
			);
			return true;
		}
		return false;
	}

	private Gen<object?>? IntegerLeaf(Type type) =>
		type == typeof(int)
			? Integer<int>()
			: type == typeof(long)
				? Integer<long>()
				: type == typeof(short)
					? Integer<short>()
					: type == typeof(byte)
						? Integer<byte>()
						: type == typeof(sbyte)
							? Integer<sbyte>()
							: type == typeof(uint)
								? Integer<uint>()
								: type == typeof(ulong) ? Integer<ulong>() : type == typeof(ushort) ? Integer<ushort>() : null;

	private Gen<object?> Integer<T>() where T : IBinaryInteger<T>, IMinMaxValue<T> =>
		Leaf(pcg =>
		{
			var v = pcg.Next(5) == 0 ? pcg.Next(100_001) : pcg.Next(101);
			return (T.CreateSaturating(v), v);
		}, Boxed(EdgeCatalog.Integers<T>()));

	private Gen<object?>? FloatLeaf(Type type)
	{
		if (type == typeof(double))
		{
			return Leaf(pcg => { var v = pcg.Next(100_001); return (v / 100.0, v); }, Boxed(EdgeCatalog.Doubles));
		}
		if (type == typeof(float))
		{
			return Leaf(pcg => { var v = pcg.Next(100_001); return (v / 100f, v); }, Boxed(EdgeCatalog.Doubles.Select(d => (float)d).Distinct()));
		}
		if (type == typeof(decimal))
		{
			return Leaf(pcg => { var v = pcg.Next(100_000) + 1; return (v / 100m, v); }, Boxed(EdgeCatalog.Decimals));
		}
		return null;
	}

	private Gen<object?>? TimeLeaf(Type type)
	{
		var baseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		if (type == typeof(DateTime))
		{
			return Leaf(pcg => { var d = pcg.Next(730); return (baseDate.AddDays(d).AddMinutes(pcg.Next(1440)), d); }, Boxed(EdgeCatalog.DateTimes));
		}
		if (type == typeof(DateTimeOffset))
		{
			return Leaf(
				pcg => { var d = pcg.Next(730); return (new DateTimeOffset(baseDate.AddDays(d).AddMinutes(pcg.Next(1440)), TimeSpan.Zero), d); },
				Boxed(EdgeCatalog.DateTimeOffsets)
			);
		}
		if (type == typeof(DateOnly))
		{
			return Leaf(pcg => { var d = pcg.Next(730); return (DateOnly.FromDateTime(baseDate.AddDays(d)), d); }, Boxed(EdgeCatalog.DateOnlys));
		}
		if (type == typeof(TimeOnly))
		{
			return Leaf(pcg => { var m = pcg.Next(1440); return (new TimeOnly(0, 0).AddMinutes(m), m); }, Boxed(EdgeCatalog.TimeOnlys));
		}
		if (type == typeof(TimeSpan))
		{
			return Leaf(pcg => { var s = pcg.Next(86_400); return (TimeSpan.FromSeconds(s), s); }, Boxed(EdgeCatalog.TimeSpans));
		}
		return null;
	}

	// ── Enums ────────────────────────────────────────────────────────────────

	private Gen<object?> EnumGen(Type type)
	{
		var defined = Enum.GetValues(type).Cast<object>().OrderBy(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)).ToArray();
		var undefined = new List<object?>();
		var numbers = defined.Select(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)).ToArray();
		var all = numbers.Length == 0 ? 0m : numbers.Max();
		foreach (var candidate in new[]
		{
			0m,
			-1m,
			all + 1m,
			numbers.Length == 0 ? 1m : numbers.Min() - 1m,
			int.MaxValue,
			byte.MaxValue + 1m,
			(all * 2m) + 1m
		})
		{
			try
			{
				var boxed = Enum.ToObject(type, Convert.ToInt64(candidate, CultureInfo.InvariantCulture));
				if (!Enum.IsDefined(type, boxed) && !undefined.Contains(boxed))
				{
					undefined.Add(boxed);
				}
			}
			catch (Exception e) when (e is OverflowException or ArgumentException)
			{
				// not representable in the underlying type: no such edge value
			}
		}
		return new LeafGen(new IndexGen([.. defined]), [.. defined, .. undefined], options);
	}

	// ── Collections ──────────────────────────────────────────────────────────

	internal static bool TryCollection(Type type, out Type elementType)
	{
		elementType = null!;
		if (type == typeof(string))
		{
			return false;
		}
		if (type.IsArray)
		{
			elementType = type.GetElementType()!;
			return true;
		}
		if (!type.IsGenericType)
		{
			return false;
		}
		if (Array.IndexOf(CollectionDefinitions, type.GetGenericTypeDefinition()) >= 0)
		{
			elementType = type.GetGenericArguments()[0];
			return true;
		}
		return false;
	}

	internal static bool TryDictionary(Type type, out Type keyType, out Type valueType)
	{
		keyType = valueType = null!;
		if (!type.IsGenericType)
		{
			return false;
		}
		var definition = type.GetGenericTypeDefinition();
		if (
			definition == DictionaryDefinition
			|| definition == IDictionaryDefinition
			|| definition == IReadOnlyDictionaryDefinition
			|| definition == ImmutableDictionaryDefinition
			|| definition == IImmutableDictionaryDefinition
			|| definition == FrozenDictionaryDefinition
			|| definition == ReadOnlyDictionaryDefinition
			|| definition == SortedDictionaryDefinition
		)
		{
			var args = type.GetGenericArguments();
			keyType = args[0];
			valueType = args[1];
			return true;
		}
		return false;
	}

	internal static object Materialize(Type target, Type element, IReadOnlyList<object?> items)
	{
		var definition = target.IsGenericType ? target.GetGenericTypeDefinition() : null;
		if (definition == typeof(List<>))
		{
			var list = (IList)Activator.CreateInstance(target)!;
			foreach (var item in items)
			{
				list.Add(item);
			}
			return list;
		}
		if (definition == typeof(HashSet<>) || definition == typeof(ISet<>) || definition == typeof(IReadOnlySet<>))
		{
			var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(element))!;
			var add = set.GetType().GetMethod("Add")!;
			foreach (var item in items)
			{
				add.Invoke(set, [item]);
			}
			return set;
		}
		var array = Array.CreateInstance(element, items.Count);
		for (var i = 0; i < items.Count; i++)
		{
			array.SetValue(items[i], i);
		}
		if (definition is null)
		{
			return array;
		}
		if (definition == typeof(ImmutableArray<>))
		{
			return CreateRange(typeof(ImmutableArray), element, array);
		}
		if (definition == typeof(ImmutableList<>) || definition == typeof(IImmutableList<>))
		{
			return CreateRange(typeof(ImmutableList), element, array);
		}
		if (definition == typeof(ImmutableHashSet<>) || definition == typeof(IImmutableSet<>))
		{
			return CreateRange(typeof(ImmutableHashSet), element, array);
		}
		if (definition == typeof(FrozenSet<>))
		{
			var toFrozen = typeof(FrozenSet).GetMethods().First(m => m.Name == "ToFrozenSet" && m.GetParameters().Length == 2);
			return toFrozen.MakeGenericMethod(element).Invoke(null, [array, null])!;
		}
		if (definition == typeof(ReadOnlyCollection<>))
		{
			return Activator.CreateInstance(target, [array])!;
		}
		if (definition == typeof(Collection<>))
		{
			var collection = (IList)Activator.CreateInstance(target)!;
			foreach (var item in items)
			{
				collection.Add(item);
			}
			return collection;
		}
		if (definition == typeof(SortedSet<>))
		{
			return Activator.CreateInstance(target, [array])!;
		}
		return array;
	}

	// ImmutableArray.CreateRange<T>(IEnumerable<T>) and friends: one static factory class per immutable collection.
	private static object CreateRange(Type factory, Type element, Array items)
	{
		var method = factory.GetMethods(BindingFlags.Public | BindingFlags.Static).First(
			m => m.Name == "CreateRange" && m.GetParameters().Length == 1 && m.IsGenericMethodDefinition
		);
		return method.MakeGenericMethod(element).Invoke(null, [items])!;
	}

	internal static object MaterializeDictionary(Type target, Type key, Type value, IReadOnlyList<(object? Key, object? Value)> pairs)
	{
		var dictionary = (IDictionary)Activator.CreateInstance(DictionaryDefinition.MakeGenericType(key, value))!;
		foreach (var (k, v) in pairs)
		{
			if (k is not null)
			{
				dictionary[k] = v;
			}
		}
		var definition = target.IsGenericType ? target.GetGenericTypeDefinition() : null;
		if (definition == ImmutableDictionaryDefinition || definition == IImmutableDictionaryDefinition)
		{
			var create = typeof(ImmutableDictionary).GetMethods().First(
				m => m.Name == "CreateRange" && m.GetParameters().Length == 1 && m.GetGenericArguments().Length == 2
			);
			return create.MakeGenericMethod(key, value).Invoke(null, [dictionary])!;
		}
		if (definition == FrozenDictionaryDefinition)
		{
			var freeze = typeof(FrozenDictionary).GetMethods().First(
				m =>
					m.Name == "ToFrozenDictionary" && m.GetGenericArguments().Length == 2 && m.GetParameters().Length == 2 && m
						.GetParameters()[0]
						.ParameterType
						.GetGenericArguments()[0]
						.IsGenericType
			);
			return freeze.MakeGenericMethod(key, value).Invoke(null, [dictionary, null])!;
		}
		if (definition == ReadOnlyDictionaryDefinition || definition == SortedDictionaryDefinition)
		{
			return Activator.CreateInstance(target, [dictionary])!;
		}
		return dictionary;
	}

	// ── Objects ──────────────────────────────────────────────────────────────

	private Gen<object?> Object(Type type, int depth)
	{
		if (depth >= options.MaxDepth && !type.IsValueType)
		{
			return Terminal(type);
		}
		return new ObjectGen(Plan(type, depth), options);
	}

	// A record's [Range] sits on its constructor parameter unless it is written [property: Range], so look at both.
	private static IEnumerable<Attribute> ParameterAttributes(Type type, ParameterInfo parameter) =>
		parameter.GetCustomAttributes(true).OfType<Attribute>()
			.Concat(
				parameter.Name is null
					? []
					: type.GetProperty(parameter.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetCustomAttributes(
						true
					).OfType<Attribute>() ?? []
			);

	private Gen<object?> Annotate(Gen<object?> gen, Type memberType, IEnumerable<Attribute> attributes)
	{
		if (options.Custom.ContainsKey(memberType))
		{
			return gen;
		}
		var rules = AutoAnnotations.RulesFor(memberType, attributes);
		return rules.Count == 0 ? gen : new AnnotatedGen(gen, rules, options);
	}

	private static readonly string[] FactoryNames = ["From", "Create", "Of", "New", "Parse", "FromString", "FromValue"];

	private static MethodInfo? Factory(Type type) =>
		type.GetMethods(BindingFlags.Public | BindingFlags.Static)
			.Where(
				m =>
					m.ReturnType == type && Array.IndexOf(
						FactoryNames,
						m.Name
					) >= 0 && !m.IsGenericMethodDefinition && m.GetParameters().Length > 0 && m.GetParameters().All(p => !p.ParameterType.IsByRef)
			)
			.OrderByDescending(m => m.GetParameters().Length)
			.ThenBy(m => Array.IndexOf(FactoryNames, m.Name))
			.FirstOrDefault();

	internal ObjectPlan Plan(Type type, int depth)
	{
		if (type.IsInterface || type.IsAbstract)
		{
			throw new NotSupportedException(
				$"AutoGen cannot build {type.Name}: it is {(type.IsInterface ? "an interface" : "abstract")}. Provide a generator with AutoOptions.Use<{type.Name}>(gen), or use GenAuto.Bogus<T>()."
			);
		}
		var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
		// [JsonConstructor] says which constructor is the real one; otherwise the one with the most parameters (a record's primary constructor).
		MethodBase? ctor = constructors.FirstOrDefault(c => c.GetCustomAttributes(false).Any(a => a.GetType().Name == "JsonConstructorAttribute"))
		?? constructors.OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
		var usable = ctor is ConstructorInfo { } found && (found.GetParameters().Length > 0 || !type.IsValueType);
		if (!usable && Factory(type) is { } factory)
		{
			// A value object with a private constructor and a static From/Create/Parse: build it the way its author intended.
			ctor = factory;
		}
		else if (ctor is null && !type.IsValueType)
		{
			throw new NotSupportedException(
				$"AutoGen cannot build {type.Name}: no public constructor. Provide a generator with AutoOptions.Use<{type.Name}>(gen)."
			);
		}

		var parameters = ctor?.GetParameters() ?? [];
		var gens = new List<Gen<object?>>();
		var names = new List<string>();
		var types = new List<Type>();
		var nullables = new List<bool>();
		foreach (var p in parameters)
		{
			var info = _nullability.Create(p);
			var nullable = info.ReadState == NullabilityState.Nullable;
			gens.Add(Annotate(Build(p.ParameterType, p.Name, nullable, depth + 1, info), p.ParameterType, ParameterAttributes(type, p)));
			names.Add(p.Name ?? "");
			types.Add(p.ParameterType);
			nullables.Add(nullable || System.Nullable.GetUnderlyingType(p.ParameterType) is not null);
		}

		var setters = new List<PropertyInfo>();
		foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (!prop.CanWrite || prop.GetSetMethod() is null || prop.GetIndexParameters().Length > 0 || names.Contains(
					prop.Name,
					StringComparer.OrdinalIgnoreCase
				))
			{
				continue;
			}
			try
			{
				var info = _nullability.Create(prop);
				var nullable = info.ReadState == NullabilityState.Nullable;
				gens.Add(
					Annotate(
						Build(prop.PropertyType, prop.Name, nullable, depth + 1, info),
						prop.PropertyType,
						prop.GetCustomAttributes(true).OfType<Attribute>()
					)
				);
				names.Add(prop.Name);
				types.Add(prop.PropertyType);
				nullables.Add(nullable || System.Nullable.GetUnderlyingType(prop.PropertyType) is not null);
				setters.Add(prop);
			}
			catch (NotSupportedException)
			{
				// a property AutoGen cannot build is left at its default
			}
		}

		return new ObjectPlan(type, ctor, parameters.Length, [.. setters], [.. gens], [.. names], [.. types], [.. nullables]);
	}
}

internal sealed class ObjectPlan
{
	private readonly Type _type;
	private readonly MethodBase? _ctor;
	private readonly int _ctorCount;
	private readonly PropertyInfo[] _setters;
	private readonly Gen<object?>[] _gens;
	private readonly (int Low, int High)[] _relations;

	public ObjectPlan(
		Type type,
		MethodBase? ctor,
		int ctorCount,
		PropertyInfo[] setters,
		Gen<object?>[] gens,
		string[] names,
		Type[] types,
		bool[] nullable
	)
	{
		Names = names;
		Nullable = nullable;
		Types = types;
		_type = type;
		_ctor = ctor;
		_ctorCount = ctorCount;
		_setters = setters;
		_gens = gens;
		_relations = FindRelations(names, types);
		Identity =
		[
			.. Enumerable.Range(0, names.Length).Where(
				i =>
					EdgeCatalog.LooksLikeIdentity(
						names[i]
					) && IsKeyType(types[i])
			)
		];
		Pk = FindPrimaryKey(type, names, types);
		Fks = Pk is { } pk
			? [
				.. Enumerable.Range(0, names.Length).Where(
					i => i != pk && names[i].EndsWith("id", StringComparison.OrdinalIgnoreCase) && BaseType(types[i]) == BaseType(types[pk])
				)
			]
			: [];
	}

	private static Type BaseType(Type t) => System.Nullable.GetUnderlyingType(t) ?? t;

	private static bool IsPrimitiveKey(Type t) => t == typeof(string) || t == typeof(Guid) || t == typeof(int) || t == typeof(long);

	// A key is a string, Guid, int or long, or a strongly-typed id that wraps one (record struct OrderId(Guid Value)).
	private static bool IsKeyType(Type t)
	{
		var b = BaseType(t);
		if (IsPrimitiveKey(b))
		{
			return true;
		}
		if (b.IsPrimitive || b.IsEnum || !(b.IsValueType || b.IsSealed))
		{
			return false;
		}
		var props = b.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0).ToList();
		return props.Count == 1 && IsPrimitiveKey(props[0].PropertyType);
	}

	private List<CrossReference>? _cross;

	/// <summary>
	/// Two collection members whose items refer to each other by key: <c>Customers</c> (items with an <c>Id</c>) and <c>Orders</c> (items with a
	/// <c>CustomerId</c>). Found by name, so no attribute is needed.
	/// </summary>
	private List<CrossReference> FindCrossReferences()
	{
		var found = new List<CrossReference>();
		for (var a = 0; a < _gens.Length; a++)
		{
			if (_gens[a] is not CollectionGen { ElementPlan: { Pk: { } pk } aPlan } aGen)
			{
				continue;
			}
			for (var b = 0; b < _gens.Length; b++)
			{
				if (b == a || _gens[b] is not CollectionGen { ElementPlan: { } bPlan } bGen)
				{
					continue;
				}
				for (var k = 0; k < bPlan.Names.Length; k++)
				{
					if (bPlan.Names[k].Equals(aGen.ElementType.Name + "Id", StringComparison.OrdinalIgnoreCase) && BaseType(bPlan.Types[k]) == BaseType(
							aPlan.Types[pk]
						))
					{
						found.Add(new CrossReference(a, b, pk, k, aPlan, bPlan, bGen.Target, bGen.ElementType));
					}
				}
			}
		}
		return found;
	}

	private sealed record CrossReference(int A, int B, int APk, int BFk, ObjectPlan APlan, ObjectPlan BPlan, Type BTarget, Type BElement);

	/// <summary>
	/// Makes <c>Order.CustomerId</c> point at a customer in the same object: always, in plausible data; in the other modes some of the time at a customer
	/// that is not there, which is the referential-integrity case a hand-written generator forgets.
	/// </summary>
	public void ApplyCrossReferences(object?[] args, AutoOptions options, PCG pcg)
	{
		if (!options.Relational)
		{
			return;
		}
		_cross ??= FindCrossReferences();
		foreach (var reference in _cross)
		{
			var owners = (args[reference.A] as System.Collections.IEnumerable)?.Cast<object?>().Where(o => o is not null).ToList();
			var children = (args[reference.B] as System.Collections.IEnumerable)?.Cast<object?>().ToList();
			if (owners is null || children is null || owners.Count == 0 || children.Count == 0)
			{
				continue;
			}
			var keys = owners.Select(o => reference.APlan.ReadArgs(o!)?[reference.APk]).Where(k => k is not null).ToList();
			if (keys.Count == 0)
			{
				continue;
			}
			var dangling = options.Mode switch
			{
				AutoMode.Plausible => 0u,
				AutoMode.Adversarial => 75u,
				_ => 35u,
			};
			var broken = pcg.Next(100) < dangling ? (int)pcg.Next((uint)children.Count) : -1;
			var nullable = reference.BPlan.Nullable[reference.BFk];
			for (var i = 0; i < children.Count; i++)
			{
				if (children[i] is not { } child || reference.BPlan.ReadArgs(child) is not { } childArgs)
				{
					continue;
				}
				childArgs[reference.BFk] = i == broken
					? CollectionGen.Missing(keys, reference.APlan, reference.APk, pcg)
					: nullable && pcg.Next(100) < 10 ? null : keys[(int)pcg.Next((uint)keys.Count)];
				try
				{
					children[i] = reference.BPlan.Construct(childArgs);
				}
				catch (TargetInvocationException)
				{
					// the child's own constructor refused this reference: keep the child as drawn
				}
			}
			args[reference.B] = AutoBuilder.Materialize(reference.BTarget, reference.BElement, children);
		}
	}

	/// <summary>A fresh value for member <paramref name="index"/>, drawn the way the plan would.</summary>
	public object? DrawMember(int index, PCG pcg) => _gens[index].Generate(pcg, null, out _);

	private static int? FindPrimaryKey(Type type, string[] names, Type[] types)
	{
		for (var i = 0; i < names.Length; i++)
		{
			var isKeyName = names[i].Equals("id", StringComparison.OrdinalIgnoreCase) || names[i].Equals(type.Name + "Id", StringComparison.OrdinalIgnoreCase);
			if (isKeyName && IsKeyType(types[i]))
			{
				return i;
			}
		}
		return null;
	}

	/// <summary>The constructor parameters, then the settable properties, by name.</summary>
	public string[] Names { get; }

	public Type[] Types { get; }

	/// <summary>Reads the arguments back out of <paramref name="instance"/> (by property name) so one can be swapped and the value rebuilt; null when a member is not readable.</summary>
	public object?[]? ReadArgs(object instance)
	{
		var args = new object?[Names.Length];
		for (var i = 0; i < Names.Length; i++)
		{
			var property = instance.GetType().GetProperty(Names[i], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
			if (property is null || property.GetIndexParameters().Length > 0)
			{
				return null;
			}
			args[i] = property.GetValue(instance);
		}
		return args;
	}

	/// <summary>Whether each member may be null (a nullable reference or <c>Nullable&lt;T&gt;</c>).</summary>
	public bool[] Nullable { get; }

	/// <summary>The index of the primary key (<c>Id</c> or <c>&lt;Type&gt;Id</c>) when the type has one.</summary>
	public int? Pk { get; }

	/// <summary>Indexes of the references to a key of the same type (<c>ManagerId</c>, <c>ParentId</c>): the foreign keys among the items of a collection.</summary>
	public int[] Fks { get; }

	/// <summary>Indexes of the arguments that look like an identity (<c>Id</c>, <c>Sku</c>, <c>Email</c>...).</summary>
	public int[] Identity { get; }

	private static (int, int)[] FindRelations(string[] names, Type[] types)
	{
		var found = new List<(int, int)>();
		foreach (var (low, high) in EdgeCatalog.RelationPairs)
		{
			for (var i = 0; i < names.Length; i++)
			{
				if (!names[i].Contains(low, StringComparison.OrdinalIgnoreCase) || !IsComparable(types[i]))
				{
					continue;
				}
				for (var j = 0; j < names.Length; j++)
				{
					if (i != j && types[i] == types[j] && names[j].Contains(high, StringComparison.OrdinalIgnoreCase))
					{
						found.Add((i, j));
						break;
					}
				}
			}
		}
		return [.. found.Distinct()];
	}

	private static bool IsComparable(Type t) =>
		(t.IsPrimitive && t != typeof(bool) && t != typeof(char)) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(DateOnly) || t == typeof(TimeOnly) || t == typeof(TimeSpan);

	public object?[] DrawArgs(PCG pcg, AutoOptions options, out Size size)
	{
		var args = new object?[_gens.Length];
		size = new Size(0);
		for (var i = 0; i < _gens.Length; i++)
		{
			args[i] = _gens[i].Generate(pcg, null, out var s);
			size.Add(s);
		}
		var percent = options.Mode switch
		{
			AutoMode.Plausible => 0,
			AutoMode.Adversarial => 60,
			_ => 20
		};
		if (options.Relational && percent > 0)
		{
			foreach (var (low, high) in _relations)
			{
				if (pcg.Next(100) < (uint)percent)
				{
					Relate(args, low, high, pcg.Next(3));
				}
			}
		}
		return args;
	}

	private static void Relate(object?[] args, int low, int high, uint kind)
	{
		var a = args[low];
		var b = args[high];
		if (a is null)
		{
			return;
		}
		switch (kind)
		{
			case 0:
				args[high] = a; // equal: Start == End
				break;
			case 1:
				if (b is IComparable c && c.CompareTo(a) > 0)
				{
					(args[low], args[high]) = (b, a); // inverted: Start > End
				}
				else
				{
					args[high] = Successor(a);
					(args[low], args[high]) = (args[high], args[low]);
				}
				break;
			default:
				args[high] = Successor(a); // adjacent: End is one unit after Start
				break;
		}
	}

	private static object? Successor(object v) => v switch
	{
		int i when i < int.MaxValue => i + 1,
		long l when l < long.MaxValue => l + 1,
		short s when s < short.MaxValue => (short)(s + 1),
		byte b when b < byte.MaxValue => (byte)(b + 1),
		uint u when u < uint.MaxValue => u + 1,
		ulong ul when ul < ulong.MaxValue => ul + 1,
		double d => Math.BitIncrement(d),
		float f => MathF.BitIncrement(f),
		decimal m when m < decimal.MaxValue - 0.01m => m + 0.01m,
		DateTime dt when dt < DateTime.MaxValue.AddTicks(-1) => dt.AddTicks(1),
		DateTimeOffset dto when dto < DateTimeOffset.MaxValue.AddTicks(-1) => dto.AddTicks(1),
		DateOnly d when d < DateOnly.MaxValue => d.AddDays(1),
		TimeOnly t when t < TimeOnly.MaxValue => t.Add(TimeSpan.FromTicks(1)),
		TimeSpan ts when ts < TimeSpan.MaxValue - TimeSpan.FromTicks(1) => ts + TimeSpan.FromTicks(1),
		_ => v,
	};

	public object? Construct(object?[] args)
	{
		var instance = _ctor switch
		{
			null => Activator.CreateInstance(_type),
			ConstructorInfo c => c.Invoke(args[.._ctorCount]),
			_ => ((MethodInfo)_ctor).Invoke(null, args[.._ctorCount]),
		};
		for (var i = 0; i < _setters.Length; i++)
		{
			_setters[i].SetValue(instance, args[_ctorCount + i]);
		}
		return instance;
	}
}

internal sealed class ObjectGen(ObjectPlan plan, AutoOptions options) : Gen<object?>
{
	public ObjectPlan Plan => plan;

	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		// A constructor that guards its arguments throws on a hostile value. Redraw: the constructor
		// rejecting a value is correct behaviour, not something the generator should surface.
		Exception? last = null;
		for (var attempt = 0; attempt < 20; attempt++)
		{
			var args = plan.DrawArgs(pcg, options, out size);
			plan.ApplyCrossReferences(args, options, pcg);
			try
			{
				return plan.Construct(args);
			}
			catch (TargetInvocationException e)
			{
				last = e.InnerException ?? e;
			}
		}
		throw new InvalidOperationException(
			$"AutoGen could not construct a valid value in 20 attempts; the constructor kept throwing: {last?.Message}. Use AutoMode.Plausible or AutoOptions.Use<T>(gen).",
			last
		);
	}
}

internal sealed class CollectionGen(
	Type target,
	Type elementType,
	(Gen<object?> Value, ObjectPlan? Plan) element,
	AutoOptions options,
	bool elementsNullable = false
) : Gen<object?>
{
	public Type Target => target;

	public Type ElementType => elementType;

	public ObjectPlan? ElementPlan => element.Plan;

	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		var count = (int)Count(pcg, options);
		size = new Size((ulong)count << 24);
		var items = new List<object?>(count);

		if (element.Plan is { } plan)
		{
			var argSets = new List<object?[]>(count);
			for (var i = 0; i < count; i++)
			{
				argSets.Add(plan.DrawArgs(pcg, options, out var s));
				size.Add(s);
			}
			DuplicateIdentity(argSets, plan, pcg);
			ReferenceKeys(argSets, plan, pcg);
			foreach (var args in argSets)
			{
				try
				{
					items.Add(plan.Construct(args));
				}
				catch (TargetInvocationException)
				{
					items.Add(element.Value.Generate(pcg, null, out _));
				}
			}
		}
		else
		{
			for (var i = 0; i < count; i++)
			{
				items.Add(element.Value.Generate(pcg, null, out var s));
				size.Add(s);
			}
			if (count >= 2 && options.Relational && options.Mode != AutoMode.Plausible && pcg.Next(100) < (options.Mode == AutoMode.Adversarial ? 40u : 12u))
			{
				items[1] = items[0]; // duplicates, or the same instance twice
			}
		}
		if (elementsNullable && options.NullPercent > 0)
		{
			for (var i = 0; i < items.Count; i++)
			{
				if (pcg.Next(100) < (uint)options.NullPercent)
				{
					items[i] = null;
				}
			}
		}
		return AutoBuilder.Materialize(target, elementType, items);
	}

	/// <summary>
	/// Makes the foreign keys among the items (<c>Person.ManagerId</c> pointing at another <c>Person.Id</c>) mean something. Plausible data is a valid
	/// forest (every reference points at an earlier item, or nothing); otherwise a share of collections get the cases that break referential
	/// integrity: a reference to nothing, to itself, or round a cycle of two or three.
	/// </summary>
	private void ReferenceKeys(List<object?[]> sets, ObjectPlan plan, PCG pcg)
	{
		if (plan.Pk is not { } pk || plan.Fks.Length == 0 || sets.Count == 0 || !options.Relational)
		{
			return;
		}
		var keys = sets.Select(s => s[pk]).ToList();
		var (valid, dangling, self, cycle) = options.Mode switch
		{
			AutoMode.Plausible => (100, 0, 0, 0),
			AutoMode.Adversarial => (10, 30, 20, 30),
			_ => (45, 20, 10, 15),
		};
		foreach (var fk in plan.Fks)
		{
			var roll = (int)pcg.Next(100);
			var nullable = plan.Nullable[fk];
			if (roll < valid)
			{
				Forest(sets, keys, fk, nullable, pcg);
			}
			else if (roll < valid + dangling)
			{
				sets[(int)pcg.Next((uint)sets.Count)][fk] = Missing(keys, plan, pk, pcg);
			}
			else if (roll < valid + dangling + self)
			{
				var i = (int)pcg.Next((uint)sets.Count);
				sets[i][fk] = keys[i];
			}
			else if (roll < valid + dangling + self + cycle)
			{
				if (sets.Count < 2)
				{
					sets[0][fk] = keys[0];
					continue;
				}
				var length = sets.Count >= 3 && pcg.Next(2) == 0 ? 3 : 2;
				for (var k = 0; k < length; k++)
				{
					sets[k][fk] = keys[(k + 1) % length];
				}
			}
			else if (nullable)
			{
				sets[(int)pcg.Next((uint)sets.Count)][fk] = null;
			}
			else
			{
				Forest(sets, keys, fk, nullable, pcg);
			}
		}
	}

	// A valid forest: each item refers to an earlier one, or to nothing (a root). A root with a non-nullable reference refers to itself.
	private static void Forest(List<object?[]> sets, List<object?> keys, int fk, bool nullable, PCG pcg)
	{
		for (var i = 0; i < sets.Count; i++)
		{
			if (i == 0)
			{
				sets[0][fk] = nullable ? null : keys[0];
			}
			else if (nullable && pcg.Next(100) < 15)
			{
				sets[i][fk] = null;
			}
			else
			{
				sets[i][fk] = keys[(int)pcg.Next((uint)i)];
			}
		}
	}

	// A key value no item has.
	internal static object? Missing(List<object?> keys, ObjectPlan plan, int pk, PCG pcg)
	{
		var sample = keys.FirstOrDefault(k => k is not null);
		switch (sample)
		{
			case not null when sample is not (Guid or int or long or string):
				// a strongly-typed id: draw until one is not among the keys
				for (var attempt = 0; attempt < 8; attempt++)
				{
					var fresh = plan.DrawMember(pk, pcg);
					if (!keys.Contains(fresh))
					{
						return fresh;
					}
				}
				return sample;
			case Guid:
				var bytes = new byte[16];
				BitConverter.GetBytes(pcg.Next64()).CopyTo(bytes, 0);
				BitConverter.GetBytes(pcg.Next64()).CopyTo(bytes, 8);
				return new Guid(bytes);
			case int:
				return keys.OfType<int>().DefaultIfEmpty(0).Max() + 1;
			case long:
				return keys.OfType<long>().DefaultIfEmpty(0).Max() + 1;
			default:
				var candidate = $"missing-{pcg.Next(10_000)}";
				return keys.Contains(candidate) ? candidate + "-x" : candidate;
		}
	}

	private void DuplicateIdentity(List<object?[]> argSets, ObjectPlan plan, PCG pcg)
	{
		if (argSets.Count < 2 || plan.Identity.Length == 0 || !options.Relational || options.Mode == AutoMode.Plausible)
		{
			return;
		}
		if (pcg.Next(100) >= (options.Mode == AutoMode.Adversarial ? 50u : 15u))
		{
			return;
		}
		var index = plan.Identity[pcg.Next((uint)plan.Identity.Length)];
		var original = argSets[0][index];
		argSets[1][index] = original is string s
			? pcg.Next(4) switch
			{
				0 => s,
				1 => s.ToUpperInvariant(),
				2 => s + " ",
				_ => " " + s
			}
			: original;
	}

	internal static uint Count(PCG pcg, AutoOptions options)
	{
		if (options.Mode == AutoMode.Adversarial)
		{
			return pcg.Next(8) switch
			{
				0 or 1 or 2 => 0u,
				3 or 4 => 1u,
				5 => 2u,
				6 => 5u,
				_ => 10u
			};
		}
		var weights = new[] { 0u, 0u, 1u, 1u, 2u, 2u, 3u, 4u };
		return Math.Min(weights[pcg.Next((uint)weights.Length)], (uint)options.MaxCollection);
	}
}

internal sealed class DictionaryGen(Type target, Gen<object?> key, Gen<object?> value, AutoOptions options) : Gen<object?>
{
	public override object? Generate(PCG pcg, Size? min, out Size size)
	{
		var count = (int)CollectionGen.Count(pcg, options);
		size = new Size((ulong)count << 24);
		var pairs = new List<(object?, object?)>(count);
		var args = target.GetGenericArguments();
		for (var i = 0; i < count; i++)
		{
			var k = key.Generate(pcg, null, out var ks);
			var v = value.Generate(pcg, null, out var vs);
			size.Add(ks);
			size.Add(vs);
			pairs.Add((k, v));
		}
		return AutoBuilder.MaterializeDictionary(target, args[0], args[1], pairs);
	}
}
