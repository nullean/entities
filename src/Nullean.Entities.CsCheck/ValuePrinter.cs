// Nullean.Entities.CsCheck — render a generated value for a failure message.
//
// A record's ToString prints a collection as its type name (`BundledProduct[]`), which is exactly what a reader of a failing generated value needs to see through. This
// prints members and items, to a fixed depth and width, and shortens long strings, so a failure shows the value that broke the rule and not its shape.

using System.Collections;
using System.Reflection;
using System.Text;

namespace Nullean.Entities.CsCheck;

internal static class ValuePrinter
{
	private const int MaxDepth = 4;
	private const int MaxItems = 4;
	private const int MaxText = 60;
	private const int MaxTotal = 1500;

	public static string Print(object? value)
	{
		var sb = new StringBuilder();
		try
		{
			Write(sb, value, 0);
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			sb.Append('<').Append(e.GetType().Name).Append(" while printing>");
		}
		return sb.Length > MaxTotal ? sb.ToString(0, MaxTotal) + "..." : sb.ToString();
	}

	private static void Write(StringBuilder sb, object? value, int depth)
	{
		switch (value)
		{
			case null:
				sb.Append("null");
				return;
			case string text:
				sb.Append('"').Append(text.Length > MaxText ? text[..MaxText] + "..." : text).Append('"');
				return;
		}
		var type = value.GetType();
		if (type.IsPrimitive || type.IsEnum || value is decimal or Guid or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan or Uri)
		{
			sb.Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
			return;
		}
		if (depth >= MaxDepth)
		{
			sb.Append(type.Name).Append("{...}");
			return;
		}
		if (value is IEnumerable items)
		{
			var shown = 0;
			var total = 0;
			sb.Append('[');
			foreach (var item in items)
			{
				total++;
				if (shown >= MaxItems)
				{
					continue;
				}
				sb.Append(shown++ > 0 ? ", " : "");
				Write(sb, item, depth + 1);
			}
			sb.Append(total > shown ? ", ... " + total.ToString(System.Globalization.CultureInfo.InvariantCulture) + " items]" : "]");
			return;
		}
		if (type.Namespace is { } ns && ns.StartsWith("System", StringComparison.Ordinal) && !type.Name.StartsWith("KeyValuePair", StringComparison.Ordinal))
		{
			sb.Append(value);
			return;
		}
		sb.Append(type.Name).Append(" { ");
		var first = true;
		foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (property.GetIndexParameters().Length > 0 || !property.CanRead || property.Name == "EqualityContract")
			{
				continue;
			}
			object? child;
			try
			{
				child = property.GetValue(value);
			}
			catch (Exception e) when (e is not OperationCanceledException)
			{
				child = $"<{e.GetType().Name}>";
			}
			sb.Append(first ? "" : ", ").Append(property.Name).Append(" = ");
			first = false;
			Write(sb, child, depth + 1);
		}
		sb.Append(" }");
	}
}
