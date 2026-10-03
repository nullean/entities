using System.IO.Abstractions;
using Nullean.Entities;

namespace DocsSiteBuild;

/// <summary>What a build found, and where it wrote.</summary>
public sealed record BuildReport(bool Built, IReadOnlyList<string> Findings, IReadOnlyList<string> Written);

/// <summary>
/// Loads <c>docset.yml</c> from a file system and builds the site, or refuses and says why. The format is a deliberately tiny stand-in for real YAML:
/// <c>title: X</c>, <c>page: path.md</c>, <c>image: path.png</c>, one per line, <c>#</c> for a comment.
/// </summary>
public static class SiteBuilder
{
	public static Result<LoadedDocset> Load(IFileSystem fileSystem, string root)
	{
		var source = fileSystem.FileInfo.New(fileSystem.Path.Combine(root, "docset.yml"));
		var rootDirectory = fileSystem.DirectoryInfo.New(root);
		if (!source.Exists)
		{
			return new Result<LoadedDocset>.Fail(default!, [Violation.Error($"There is no docset.yml in {rootDirectory.FullName}")]);
		}

		var title = "";
		List<IFileInfo> pages = [], images = [];
		Dictionary<string, int> lines = [];
		List<Violation> problems = [];
		var number = 0;
		foreach (var raw in fileSystem.File.ReadAllLines(source.FullName))
		{
			number++;
			var line = raw.Trim();
			if (line.Length == 0 || line.StartsWith('#'))
			{
				continue;
			}

			var colon = line.IndexOf(':', StringComparison.Ordinal);
			var (key, value) = colon < 0 ? (line, "") : (line[..colon].Trim(), line[(colon + 1)..].Trim());
			switch (key)
			{
				case "title":
					title = value;
					break;
				case "page" or "image":
					var file = fileSystem.FileInfo.New(fileSystem.Path.Combine(root, value));
					(key == "page" ? pages : images).Add(file);
					lines.TryAdd(file.FullName, number);
					break;
				default:
					problems.Add(Violation.Error($"Line {number}: unknown entry '{key}'") with { Line = number, File = source.FullName });
					break;
			}
		}

		return problems.Count > 0
			? new Result<LoadedDocset>.Fail(default!, problems)
			: new Result<LoadedDocset>.Ok(new LoadedDocset(new Docset(title, fileSystem, rootDirectory, source, pages, images), lines));
	}

	/// <summary>Validates the docset and, only if there is no error, writes one HTML file per page into <paramref name="output"/>. Warnings are reported and do not stop the build.</summary>
	public static BuildReport Build(IFileSystem fileSystem, string root, string output)
	{
		if (Load(fileSystem, root) is not Result<LoadedDocset>.Ok loaded)
		{
			return new BuildReport(false, [.. Load(fileSystem, root).ViolationsOrEmpty.Select(v => v.Message)], []);
		}

		var (docset, lines) = (loaded.Value.Docset, loaded.Value.Lines);
		var findings = DocsetRules.All.Validate(docset).Select(v => Describe(v, lines)).ToList();
		if (DocsetRules.All.Validate(docset).Any(v => v.Severity == Severity.Error))
		{
			return new BuildReport(false, findings, []);
		}

		List<string> written = [];
		fileSystem.Directory.CreateDirectory(output);
		foreach (var page in docset.Pages)
		{
			var target = fileSystem.Path.Combine(
				output,
				fileSystem.Path.ChangeExtension(fileSystem.Path.GetRelativePath(docset.Root.FullName, page.FullName), ".html").Replace('/', '_')
			);
			fileSystem.File.WriteAllText(
				target,
				$"<html><head><title>{docset.Title}</title></head><body><pre>{System.Net.WebUtility.HtmlEncode(fileSystem.File.ReadAllText(page.FullName))}</pre></body></html>"
			);
			written.Add(target);
		}
		return new BuildReport(true, findings, written);
	}

	/// <summary>A finding as a line of text, pointing at the line of <c>docset.yml</c> that named the file when there is one.</summary>
	public static string Describe(Violation violation, IReadOnlyDictionary<string, int> lines)
	{
		var evidence = violation.Evidence as string;
		var where = evidence is not null && lines.TryGetValue(evidence, out var line) ? $"docset.yml:{line} " : "";
		// most file rules already name the path in the message; the rest (a duplicate) get it appended
		var shown = evidence is null || violation.Message.Contains(evidence, StringComparison.Ordinal)
			? violation.Message
			: $"{violation.Message} [{evidence}]";
		return $"{violation.Severity.ToString().ToUpperInvariant(),-7} {where}{shown}";
	}
}
