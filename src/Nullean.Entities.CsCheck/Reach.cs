// Nullean.Entities.CsCheck — how large and how deep the generated values actually were.
//
// A rule that is "never violated" is either a tautology or about something the generator never produced: a rule about more than six levels, or more than four items, cannot fail on values
// that never had them. Probe measures the values it drew and reports it next to the list, so the two can be told apart without reading the generator.

using System.Collections;
using System.Reflection;

namespace Nullean.Entities.CsCheck;

/// <summary>The largest collection and the deepest nesting among the values a generator produced.</summary>
/// <param name="LargestCollection">The most items any one collection held.</param>
/// <param name="Depth">The deepest level reached, counting the value itself as 1 and a collection and then each of its items as a level each (the way <c>AutoOptions.MaxDepth</c> counts).</param>
public sealed record GeneratedReach(int LargestCollection, int Depth);

internal sealed class ReachMeter
{
	private const int MaxLevels = 64;
	private const int MaxItems = 100_000;
	private const int MaxNodes = 50_000;

	private int _largest;
	private int _deepest;
	private int _budget;

	public int Measured { get; private set; }

	public GeneratedReach Result => new(_largest, _deepest);

	public void Add(object? value)
	{
		Measured++;
		_budget = MaxNodes;
		Walk(value, 1);
	}

	private void Walk(object? value, int depth)
	{
		// A cyclic graph is cut by the depth, and a very wide one by the budget: this measures reach, it is not a traversal that has to be complete.
		if (value is null || depth > MaxLevels || --_budget < 0)
		{
			return;
		}
		var type = value.GetType();
		if (type.IsPrimitive || type.IsEnum || value is string)
		{
			return;
		}

		_deepest = Math.Max(_deepest, depth);
		if (value is IEnumerable items)
		{
			var count = 0;
			foreach (var item in items)
			{
				if (++count > MaxItems)
				{
					break;
				}
				Walk(item, depth + 1);
			}
			_largest = Math.Max(_largest, count);
			return;
		}

		// Types the framework owns hold nothing the model chose (a DateTime's ticks, an IFileInfo's handle), except a dictionary's pairs.
		var owned = type.Namespace is { } ns && ns.StartsWith("System", StringComparison.Ordinal) && !type.Name.StartsWith(
			"KeyValuePair",
			StringComparison.Ordinal
		);
		if (owned)
		{
			return;
		}

		foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (property.GetIndexParameters().Length == 0 && property.CanRead && Read(() => property.GetValue(value)) is { } child)
			{
				Walk(child, depth + 1);
			}
		}
		foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
		{
			if (Read(() => field.GetValue(value)) is { } child)
			{
				Walk(child, depth + 1);
			}
		}
	}

	// A property can throw on a generated value (a computed one that overflows); that is the rule's business, not the measure's.
	private static object? Read(Func<object?> read)
	{
		try
		{
			return read();
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			return null;
		}
	}
}
