using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using AwesomeAssertions;
using Nullean.Entities;
using Nullean.Entities.FileSystem;

namespace Nullean.Entities.FileSystem.Tests;

internal sealed record Docset(IDirectoryInfo Root, IReadOnlyList<IFileInfo> Images, IReadOnlyList<IFileInfo> Located);

public class FilesTests
{
	private static MockFileSystem World()
	{
		var fs = new MockFileSystem();
		fs.AddDirectory("/docs");
		fs.AddDirectory("/docs-other");
		fs.AddFile("/docs/a.md", new MockFileData("# a"));
		fs.AddFile("/docs/sub/b.png", new MockFileData("png"));
		fs.AddFile("/docs/.hidden/c.md", new MockFileData("c"));
		fs.AddFile("/elsewhere/real.md", new MockFileData("real"));
		fs.AddFile("/docs-other/x.md", new MockFileData("x"));
		fs.File.CreateSymbolicLink("/docs/link.md", "/elsewhere/real.md");
		fs.Directory.CreateSymbolicLink("/docs/linkdir", "/elsewhere");
		return fs;
	}

	private static Docset Make(MockFileSystem fs, params string[] images) =>
		new(fs.DirectoryInfo.New("/docs"), [.. images.Select(i => fs.FileInfo.New(i))], []);

	private static ConstraintSet<Docset> Set(params (string, Constraint<Docset>)[] rules) => ConstraintSet.From(rules);

	[Test]
	public void Exist_reports_each_missing_file_with_its_path_and_the_member()
	{
		var fs = World();
		var set = Set(("ImagesExist", Files<Docset>.Exist(d => d.Images)));

		var violations = set.Validate(Make(fs, "/docs/a.md", "/docs/missing.png"));

		var violation = violations.Single();
		violation.Evidence.Should().Be("/docs/missing.png");
		violation.Member.Should().Be("Images");
		violation.ConstraintId.Should().Be("ImagesExist");
		set.Validate(Make(fs, "/docs/a.md", "/docs/sub/b.png")).Should().BeEmpty();
	}

	[Test]
	public void StayInside_resolves_dot_dot_and_does_not_confuse_a_sibling_that_shares_a_prefix()
	{
		var fs = World();
		var set = Set(("Inside", Files<Docset>.StayInside(d => d.Root, d => d.Images)));

		set.Validate(Make(fs, "/docs/a.md", "/docs/sub/../a.md")).Should().BeEmpty();
		set.Validate(Make(fs, "/docs/../elsewhere/real.md")).Single().Evidence.Should().Be("/elsewhere/real.md");
		set.Validate(Make(fs, "/docs-other/x.md")).Should().ContainSingle("'/docs-other' is not inside '/docs'");
	}

	[Test]
	public void NotSymlink_finds_a_link_and_a_file_reached_through_a_linked_directory()
	{
		var fs = World();
		var set = Set(("NoLinks", Files<Docset>.NotSymlink(d => d.Images, d => d.Root)));

		set.Validate(Make(fs, "/docs/link.md")).Should().ContainSingle().Which.Message.Should().Contain("/docs/link.md");
		set.Validate(Make(fs, "/docs/linkdir/real.md")).Should().ContainSingle().Which.Message.Should().Contain("/docs/linkdir");
		set.Validate(Make(fs, "/docs/a.md", "/docs/sub/b.png")).Should().BeEmpty();
	}

	[Test]
	public void NoHiddenSegments_flags_a_dot_directory_below_the_root_but_not_one_above_it()
	{
		var fs = World();
		var set = Set(("NoHidden", Files<Docset>.NoHiddenSegments(d => d.Root, d => d.Images)));

		set.Validate(Make(fs, "/docs/.hidden/c.md")).Single().Message.Should().Contain(".hidden");
		set.Validate(Make(fs, "/docs/a.md")).Should().BeEmpty();
	}

	[Test]
	public void HaveExtension_compares_without_regard_to_case()
	{
		var fs = World();
		fs.AddFile("/docs/Shout.PNG", new MockFileData("x"));
		var set = Set(("Pictures", Files<Docset>.HaveExtension(d => d.Images, [".png", ".svg"])));

		set.Validate(Make(fs, "/docs/Shout.PNG", "/docs/sub/b.png")).Should().BeEmpty();
		set.Validate(Make(fs, "/docs/a.md")).Single().Message.Should().Contain(".png, .svg");
	}

	[Test]
	public void The_item_location_hook_points_the_violation_at_the_source_line()
	{
		var fs = World();
		var set = Set(("ImagesExist", Files<Docset>.Exist(d => d.Images, at: f => new SourceLocation("docset.yml", 12, 3, f.Name.Length))));

		var violation = set.Validate(Make(fs, "/docs/missing.png")).Single();

		(violation.File, violation.Line, violation.Length).Should().Be(("docset.yml", 12, "missing.png".Length));
	}

	private sealed record Entry(string Path, int Line);

	private sealed record Toc(IFileSystem FileSystem, IReadOnlyList<Entry> Entries);

	[Test]
	public void Exist_for_items_that_are_not_files_speaks_about_the_item_and_points_at_it()
	{
		var fs = World();
		var set = ConstraintSet.From(
			("EntriesExist",
			Files<Toc>.Exist(
				t => t.Entries,
				(t, e) => t.FileSystem.FileInfo.New(t.FileSystem.Path.Join("/docs", e.Path)),
				describe: e => $"Entry '{e.Path}' does not exist on disk.",
				at: e => new SourceLocation("docset.yml", e.Line, 1, e.Path.Length)
			))
		);

		set.Validate(new Toc(fs, [new Entry("a.md", 1)])).Should().BeEmpty();
		var violation = set.Validate(new Toc(fs, [new Entry("a.md", 1), new Entry("gone.md", 7)])).Single();

		violation.Message.Should().Be("Entry 'gone.md' does not exist on disk.");
		violation.Evidence.Should().Be("/docs/gone.md");
		violation.Member.Should().Be("Entries");
		(violation.File, violation.Line).Should().Be(("docset.yml", 7));
	}

	[Test]
	public void DirectoriesExist_for_items_resolves_each_item_to_its_directory()
	{
		var fs = World();
		var set = ConstraintSet.From(
			("FoldersExist",
			Files<Toc>.DirectoriesExist(
				t => t.Entries,
				(t, e) => t.FileSystem.DirectoryInfo.New(t.FileSystem.Path.Join("/docs", e.Path)),
				e => $"No folder {e.Path}"
			))
		);

		set.Validate(new Toc(fs, [new Entry("sub", 1)])).Should().BeEmpty();
		set.Validate(new Toc(fs, [new Entry("nope", 2)])).Single().Message.Should().Be("No folder nope");
	}

	[Test]
	public void Describe_words_the_finding_for_each_path_and_message_still_wins()
	{
		var fs = World();
		var described = Set(("Inside", Files<Docset>.StayInside(d => d.Root, d => d.Images, describe: p => $"{p.Name} leaves the docs")));
		var fixedMessage = Set(("Inside", Files<Docset>.StayInside(d => d.Root, d => d.Images, "no", p => "ignored")));

		described.Validate(Make(fs, "/elsewhere/real.md")).Single().Message.Should().Be("real.md leaves the docs");
		fixedMessage.Validate(Make(fs, "/elsewhere/real.md")).Single().Message.Should().Be("no");
	}

	[Test]
	public void NotEmpty_and_MaxSize_judge_files_that_exist_and_leave_a_missing_one_to_Exist()
	{
		var fs = World();
		fs.AddFile("/docs/empty.md", new MockFileData(""));
		fs.AddFile("/docs/big.png", new MockFileData(new string('x', 50)));
		var notEmpty = Set(("Filled", Files<Docset>.NotEmpty(d => d.Images)));
		var small = Set(("Small", Files<Docset>.MaxSize(d => d.Images, 10)));

		notEmpty.Validate(Make(fs, "/docs/empty.md")).Single().Message.Should().Contain("is empty");
		notEmpty.Validate(Make(fs, "/docs/a.md", "/docs/missing.md")).Should().BeEmpty();
		small.Validate(Make(fs, "/docs/big.png")).Single().Message.Should().Contain("50 bytes").And.Contain("limit is 10");
		small.Validate(Make(fs, "/docs/a.md", "/docs/missing.png")).Should().BeEmpty();
	}

	[Test]
	public void UniqueIgnoringCase_reports_the_second_spelling_and_not_the_same_path_twice()
	{
		var fs = World();
		fs.AddFile("/docs/Readme.md", new MockFileData("a"));
		fs.AddFile("/docs/README.md", new MockFileData("b"));
		var set = Set(("NoCaseTwins", Files<Docset>.UniqueIgnoringCase(d => d.Images)));

		var violation = set.Validate(Make(fs, "/docs/Readme.md", "/docs/README.md")).Single();

		violation.Message.Should().Contain("/docs/README.md").And.Contain("/docs/Readme.md");
		set.Validate(Make(fs, "/docs/Readme.md", "/docs/Readme.md", "/docs/a.md")).Should().BeEmpty();
	}

	[Test]
	public void FilePaths_answers_the_questions_the_shapes_ask()
	{
		var fs = World();
		var root = fs.DirectoryInfo.New("/docs");

		FilePaths.IsInside(root, fs.FileInfo.New("/docs/sub/../a.md")).Should().BeTrue();
		FilePaths.IsInside(root, fs.FileInfo.New("/docs-other/x.md")).Should().BeFalse();
		FilePaths.IsInside("/Docs", "/docs/a.md", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
		FilePaths.UnsafeToRead(fs.FileInfo.New("/docs/a.md"), root).Should().BeNull();
		FilePaths.UnsafeToRead(fs.FileInfo.New("/docs/link.md"), root).Should().Contain("symbolic link");
		FilePaths.UnsafeToRead(fs.FileInfo.New("/docs/.hidden/c.md"), root).Should().Contain(".hidden");
	}

	[Test]
	public void A_directory_that_is_missing_is_reported_by_DirectoriesExist()
	{
		var fs = World();
		var set = Set(("RootExists", Files<Docset>.DirectoriesExist(d => [d.Root])));

		set.Validate(new Docset(fs.DirectoryInfo.New("/nope"), [], [])).Single().Message.Should().Contain("/nope");
		set.Validate(Make(fs)).Should().BeEmpty();
	}
}
