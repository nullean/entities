using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Nullean.Entities.Quickstart.Tests;

public partial class DocsMatchExamplesTests
{
	private static DirectoryInfo Root()
	{
		var root = new DirectoryInfo(AppContext.BaseDirectory);
		while (root is not null && !File.Exists(Path.Combine(root.FullName, "nullean-entities.slnx")))
		{
			root = root.Parent;
		}
		return root!;
	}

	[GeneratedRegex(@"<!-- file: (?<path>[^ ]+) -->\r?\n```csharp\r?\n(?<code>.*?)\r?\n```", RegexOptions.Singleline)]
	private static partial Regex Fence();

	/// <summary>
	/// Law: every code block in the docs that is marked <c>&lt;!-- file: path --&gt;</c> is, character for character, the file it names.
	/// Consequence if broken: the docs show code that no longer compiles or no longer does what the page says, because the example changed and the page did not.
	/// </summary>
	[Test]
	public void Code_blocks_marked_with_a_file_are_that_file()
	{
		var root = Root();
		var checkedBlocks = 0;
		foreach (var page in Directory.EnumerateFiles(Path.Combine(root.FullName, "docs"), "*.md", SearchOption.AllDirectories))
		{
			foreach (Match match in Fence().Matches(File.ReadAllText(page)))
			{
				var source = File.ReadAllText(Path.Combine(root.FullName, match.Groups["path"].Value)).TrimEnd();
				match.Groups["code"].Value.TrimEnd().Should().Be(source, $"{Path.GetFileName(page)} embeds {match.Groups["path"].Value}");
				checkedBlocks++;
			}
		}

		checkedBlocks.Should().BeGreaterThan(0, "the getting-started page embeds the example files");
	}

	/// <summary>
	/// Law: every failure text the docs include is one the tests wrote, so each page that shows a failure has a snippet to include.
	/// Consequence if broken: a page shows output that no test produces.
	/// </summary>
	[Test]
	public void Every_included_snippet_exists()
	{
		var root = Root();
		var include = new Regex(@"\{include\} (?<path>\S*_snippets/\S+)");
		foreach (var page in Directory.EnumerateFiles(Path.Combine(root.FullName, "docs"), "*.md", SearchOption.AllDirectories))
		{
			foreach (Match match in include.Matches(File.ReadAllText(page)))
			{
				File
					.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(page)!, match.Groups["path"].Value)))
					.Should()
					.BeTrue($"{Path.GetFileName(page)} includes {match.Groups["path"].Value}");
			}
		}
	}
}
