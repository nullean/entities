using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using AwesomeAssertions;
using CsCheck;
using DocsSiteBuild;
using Nullean.Entities;
using Nullean.Entities.CsCheck;
using Nullean.Entities.CsCheck.FileSystem;

namespace Nullean.Entities.Apps.Tests.DocsSiteBuild;

// The same builder and the same rules as the app, on a MockFileSystem: no temp directory, no clean-up, and a symlink or a hidden folder is one line.
// Then the rules against generated worlds: the generator builds the missing files, the links and the paths that escape, so none has to be written by hand.

public class DocsetTests
{
	private const string Root = "/docs";

	private static MockFileSystem World(string docset = "title: Handbook\npage: index.md\npage: guide/start.md\nimage: img/logo.svg\n")
	{
		var fs = new MockFileSystem();
		fs.AddFile("/docs/docset.yml", new MockFileData(docset));
		fs.AddFile("/docs/index.md", new MockFileData("# Handbook"));
		fs.AddFile("/docs/guide/start.md", new MockFileData("# Start"));
		fs.AddFile("/docs/img/logo.svg", new MockFileData("<svg/>"));
		return fs;
	}

	private static Docset Load(IFileSystem fs) => SiteBuilder.Load(fs, Root).Unwrap().Docset;

	[Test]
	public void A_docset_whose_files_are_in_place_builds_and_writes_a_page_each()
	{
		var fs = World();

		var report = SiteBuilder.Build(fs, Root, "/site");

		report.Built.Should().BeTrue();
		report.Findings.Should().BeEmpty();
		report.Written.Should().HaveCount(2).And.OnlyContain(path => fs.File.Exists(path));
	}

	[Test]
	public void A_missing_file_is_reported_on_the_line_that_names_it_and_nothing_is_written()
	{
		var fs = World();
		fs.RemoveFile("/docs/guide/start.md");

		var report = SiteBuilder.Build(fs, Root, "/site");

		report.Built.Should().BeFalse();
		report.Findings.Should().ContainSingle().Which.Should().Contain("docset.yml:3").And.Contain("does not exist");
		fs.Directory.Exists("/site").Should().BeFalse();
	}

	[Test]
	public void A_path_that_climbs_out_of_the_docset_is_refused()
	{
		var fs = World("title: T\npage: index.md\nimage: ../outside.svg\n");
		fs.AddFile("/outside.svg", new MockFileData("<svg/>"));

		DocsetRules.All.Validate(Load(fs)).Should().ContainSingle(v => v.ConstraintId == ConstraintName.Of(nameof(DocsetRules.FilesStayInTheDocset)));
	}

	[Test]
	public void A_sibling_that_shares_a_prefix_with_the_root_is_outside_it()
	{
		var fs = World("title: T\npage: ../docs-other/secret.md\n");
		fs.AddFile("/docs-other/secret.md", new MockFileData("secret"));

		DocsetRules.All.Validate(Load(fs)).Select(v => v.ConstraintId).Should().Contain(ConstraintName.Of(nameof(DocsetRules.FilesStayInTheDocset)));
	}

	[Test]
	public void A_symbolic_link_and_a_hidden_folder_are_refused()
	{
		var fs = World("title: T\npage: index.md\npage: .drafts/wip.md\nimage: img/linked.svg\n");
		fs.AddFile("/docs/.drafts/wip.md", new MockFileData("wip"));
		fs.File.CreateSymbolicLink("/docs/img/linked.svg", "/docs/img/logo.svg");

		var ids = DocsetRules.All.Validate(Load(fs)).Select(v => v.ConstraintId).ToList();

		ids.Should().Contain(ConstraintName.Of(nameof(DocsetRules.NoSymlinks))).And.Contain(ConstraintName.Of(nameof(DocsetRules.NoHiddenFolders)));
	}

	[Test]
	public void A_page_listed_twice_and_a_page_that_is_not_markdown_are_refused()
	{
		var fs = World("title: T\npage: index.md\npage: index.md\npage: guide/start.md\n");
		fs.AddFile("/docs/notes.txt", new MockFileData("n"));
		var twice = DocsetRules.All.Validate(Load(fs));
		twice.Select(v => v.ConstraintId).Should().Contain(ConstraintName.Of(nameof(DocsetRules.PagesAreListedOnce)));

		var txt = World("title: T\npage: notes.txt\n");
		txt.AddFile("/docs/notes.txt", new MockFileData("n"));
		DocsetRules
			.All
			.Validate(Load(txt))
			.Select(v => v.ConstraintId)
			.Should()
			.Contain(ConstraintName.Of(ConstraintName.Of(nameof(DocsetRules.PagesAreMarkdown))));
	}

	[Test]
	public void An_unknown_entry_is_a_finding_with_its_line_not_an_exception()
	{
		var fs = World("title: T\nlayout: wide\n");

		var result = SiteBuilder.Load(fs, Root);

		result.ViolationsOrEmpty.Should().ContainSingle().Which.Line.Should().Be(2);
	}

	[Test]
	public void Rules_have_no_effect_on_the_files_they_inspect()
	{
		var fs = World();
		var before = fs.AllFiles.Count();

		DocsetRules.All.Validate(Load(fs));

		fs.AllFiles.Count().Should().Be(before);
	}

	// ── generated worlds ─────────────────────────────────────────────────────────

	[Test]
	public void Generated_worlds_break_every_file_rule_so_none_is_a_tautology()
	{
		var gen = Gen.OneOf(
			GenAuto.Of<Docset>(AutoMode.Mixed, o => o.UseFileSystem()),
			GenAuto.Of<Docset>(AutoMode.Adversarial, o => o.UseFileSystem())
		);

		DocsetRules.All.Probe(gen, samples: 2000).NeverViolated.Should().BeEmpty();
	}

	[Test]
	public void Plausible_generated_worlds_satisfy_the_file_rules()
	{
		var gen = GenAuto.Of<Docset>(AutoMode.Plausible, o => o.UseFileSystem());

		gen.Sample(
			docset =>
				DocsetRules
					.All
					.Validate(docset)
					.Where(
						v =>
							v.ConstraintId != ConstraintName.Of(nameof(DocsetRules.TitleIsRequired)) && v.ConstraintId != ConstraintName.Of(
								nameof(DocsetRules.PagesAreMarkdown)
							) && v.ConstraintId != ConstraintName.Of(nameof(DocsetRules.ImagesArePictures))
					)
					.Should()
					.BeEmpty(),
			iter: 100
		);
	}
}
