// Nullean.Entities.CsCheck — what an axis says when it fails, in words a newcomer can read.
//
// An axis finds a problem inside a CsCheck Sample, and CsCheck flattens whatever is thrown there into one string ("Set seed: ... \n<printed case>\n<message>"). So a failure is
// raised as an AxisFailure (labelled fields) whose message carries the fields in a marked block; the axis method catches CsCheck's exception, reads the block back, and
// rebuilds the text as labelled lines: what rule broke, what was done, what it started from and produced, the law and what it costs, and how to replay it.

using System.Text;
using System.Text.RegularExpressions;
using CsCheck;

namespace Nullean.Entities.CsCheck;

internal sealed partial class AxisFailure : InvalidOperationException
{
	private const char FieldSeparator = '\u001f';
	private const char LabelSeparator = '\u001d';

	private readonly string? _legacy;

	public AxisFailure(IReadOnlyList<(string Label, string Value)> fields, bool vacuous = false, string? printedCaseLabel = null, string? legacy = null) : base(
			legacy ?? string.Join("; ", fields.Select(f => $"{f.Label}: {f.Value}"))
		)
	{
		Fields = fields;
		Vacuous = vacuous;
		PrintedCaseLabel = printedCaseLabel;
		_legacy = legacy;
	}

	public IReadOnlyList<(string Label, string Value)> Fields { get; }

	/// <summary>The test passed, but without proving anything: a rule that was never reached.</summary>
	public bool Vacuous { get; }

	/// <summary>When set, the case CsCheck printed (the sequence of calls, say) is shown under this label.</summary>
	public string? PrintedCaseLabel { get; }

	/// <summary>The plain sentence the older API threw, for callers that still read it.</summary>
	public string LegacyText => _legacy ?? base.Message;

	// What CsCheck carries through its own message: a marked block the axis method reads back.
	public override string Message =>
		$"\u001eAXIS{(Vacuous ? 'V' : 'F')}\u001e{PrintedCaseLabel}\u001e{string.Join(FieldSeparator, Fields.Select(f => f.Label + LabelSeparator + f.Value))}\u001eEND";

	[GeneratedRegex("\u001eAXIS(?<kind>[VF])\u001e(?<case>[^\u001e]*)\u001e(?<fields>.*?)\u001eEND", RegexOptions.Singleline)]
	private static partial Regex Block();

	public static bool TryParse(string text, out AxisFailure failure, out string printed)
	{
		failure = null!;
		printed = "";
		var match = Block().Match(text);
		if (!match.Success)
		{
			return false;
		}
		var fields = match
			.Groups["fields"]
			.Value.Split(FieldSeparator, StringSplitOptions.RemoveEmptyEntries)
			.Select(f => f.Split(LabelSeparator, 2))
			.Select(parts => (parts[0], parts.Length > 1 ? parts[1] : ""))
			.ToList();
		failure = new AxisFailure(fields, match.Groups["kind"].Value == "V", match.Groups["case"].Value is { Length: > 0 } label ? label : null);
		// what CsCheck printed for the case sits between its seed line and our block
		var before = text[..match.Index];
		var firstLine = before.IndexOf('\n', StringComparison.Ordinal);
		printed = firstLine < 0 ? "" : before[(firstLine + 1)..].Trim();
		return true;
	}
}

internal static partial class AxisReport
{
	[GeneratedRegex("Set seed: \"([^\"]+)\"")]
	private static partial Regex SeedPattern();

	[GeneratedRegex(@"\((\d+) shrinks")]
	private static partial Regex ShrinkPattern();

	/// <summary>Runs an axis; a failure inside it comes out as labelled lines instead of CsCheck's flattened text.</summary>
	public static async Task<AxisResult> GuardAsync(Axis axis, Func<Task<AxisResult>> body)
	{
		try
		{
			return await body().ConfigureAwait(false);
		}
		catch (AxisFailure failure)
		{
			throw Build(axis, failure, null);
		}
		catch (CsCheckException e) when (AxisFailure.TryParse(e.Message, out _, out _))
		{
			_ = AxisFailure.TryParse(e.Message, out var failure, out var printed);
			throw Build(axis, failure, (e.Message, printed));
		}
	}

	private static Exception Build(Axis axis, AxisFailure failure, (string Text, string Printed)? csCheck)
	{
		var text = Format(axis, failure, csCheck);
		return failure.Vacuous ? new VacuousPropertyException(text) : new InvalidOperationException(text, failure);
	}

	public static string Format(
		Axis axis,
		AxisFailure failure,
		(string Text, string Printed)? csCheck,
		string? title = null,
		string? law = null,
		string? consequence = null
	)
	{
		var fields = new List<(string Label, string Value)>(failure.Fields);
		if (csCheck is { Printed.Length: > 0 } && failure.PrintedCaseLabel is { } label)
		{
			fields.Add((label, csCheck.Value.Printed));
		}
		var sb = new StringBuilder();
		sb.AppendLine(
			failure.Vacuous ? $"{title ?? axis.ToString()}: the test passed without proving anything." : $"{title ?? axis.ToString()} failed."
		).AppendLine();
		Append(sb, fields, Width(fields));
		sb.AppendLine();
		Append(sb, [("Law", law ?? AxisInfo.Law(axis)), ("Consequence", consequence ?? AxisInfo.Consequence(axis))], Width(fields));
		if (csCheck is { } found && SeedPattern().Match(found.Text) is { Success: true } seed)
		{
			var shrinks = ShrinkPattern().Match(found.Text) is { Success: true } s
				? int.Parse(s.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
				: 0;
			sb.AppendLine()
				.Append(
					shrinks == 0
						? "This is the smallest case: nothing smaller failed."
						: $"Smallest case found, after {shrinks} shrink{(shrinks == 1 ? "" : "s")}."
				)
				.Append(string.Concat(" To run this case again: .Seed(\"", seed.Groups[1].Value, "\")"));
		}
		return sb.ToString().TrimEnd();
	}

	private static int Width(IReadOnlyList<(string Label, string Value)> fields) => Math.Max("Consequence".Length + 2, fields.Max(f => f.Label.Length) + 2);

	private static void Append(StringBuilder sb, IReadOnlyList<(string Label, string Value)> fields, int width)
	{
		string? previous = null;
		foreach (var (label, value) in fields)
		{
			var shown = label == previous ? "" : label;
			var lines = value.Split('\n');
			sb.Append(shown.PadRight(width)).AppendLine(lines[0].TrimEnd('\r'));
			foreach (var more in lines.Skip(1))
			{
				sb.Append(' ', width).AppendLine(more.TrimEnd('\r'));
			}
			previous = label;
		}
	}
}
