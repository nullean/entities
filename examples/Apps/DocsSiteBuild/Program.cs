using System.IO.Abstractions;
using DocsSiteBuild;
using static Apps.Narrator;

// Builds a tiny docs site from a real directory, then breaks the directory in the ways the rules exist for. Run it with `dotnet run`.
// A temporary directory is created and removed; nothing outside it is touched.

var disk = new FileSystem();
var work = disk.Path.Combine(disk.Path.GetTempPath(), $"docs-site-build-{Guid.NewGuid():N}");
var root = disk.Path.Combine(work, "docs");
var output = disk.Path.Combine(work, "site");

void Write(string relative, string content)
{
	var path = disk.Path.Combine(root, relative);
	disk.Directory.CreateDirectory(disk.Path.GetDirectoryName(path)!);
	disk.File.WriteAllText(path, content);
}

try
{
	Write("docset.yml", "title: Handbook\npage: index.md\npage: guide/start.md\nimage: img/logo.png\n");
	Write("index.md", "# Handbook\n");
	Write("guide/start.md", "# Start here\n");
	Write("img/logo.png", "not really a png");

	Section("A docset whose files are all where they should be");
	var clean = SiteBuilder.Build(disk, root, output);
	Say($"wrote {clean.Written.Count} pages");
	Expect(clean.Built && clean.Findings.Count == 0, "it builds, with nothing to report");
	Expect(clean.Written.All(disk.File.Exists), "and the pages are on disk");

	Section("Now the directory goes wrong");
	disk.File.Delete(disk.Path.Combine(root, "guide/start.md"));
	Write("../secret.png", "outside the docset");
	Write(".drafts/wip.md", "# not ready\n");
	Write("notes.txt", "not markdown\n");
	var link = disk.Path.Combine(root, "img/linked.png");
	disk.File.CreateSymbolicLink(link, disk.Path.Combine(root, "img/logo.png"));
	Write(
		"docset.yml",
		"title: Handbook\npage: index.md\npage: guide/start.md\npage: .drafts/wip.md\npage: notes.txt\npage: index.md\nimage: img/logo.png\nimage: ../secret.png\nimage: img/linked.png\n"
	);

	var broken = SiteBuilder.Build(disk, root, disk.Path.Combine(work, "site2"));
	foreach (var finding in broken.Findings)
	{
		Say(finding.Replace(work, "<tmp>", StringComparison.Ordinal));
	}
	Expect(!broken.Built, "the build refuses");
	Expect(
		broken.Findings.Any(f => f.Contains("docset.yml:3", StringComparison.Ordinal) && f.Contains("does not exist", StringComparison.OrdinalIgnoreCase)),
		"a missing page is reported on the line that names it"
	);
	Expect(broken.Findings.Any(f => f.Contains("docset.yml:8", StringComparison.Ordinal)), "an image outside the docset is reported on its line");
	Expect(broken.Findings.Any(f => f.Contains("docset.yml:9", StringComparison.Ordinal)), "a symbolic link is reported on its line");
	Expect(!disk.Directory.Exists(disk.Path.Combine(work, "site2")), "and nothing was written");
}
finally
{
	disk.Directory.Delete(work, recursive: true);
}

return Finish();
