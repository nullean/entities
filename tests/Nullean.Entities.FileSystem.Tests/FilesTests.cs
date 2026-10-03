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

	[Test]
	public void A_directory_that_is_missing_is_reported_by_DirectoriesExist()
	{
		var fs = World();
		var set = Set(("RootExists", Files<Docset>.DirectoriesExist(d => [d.Root])));

		set.Validate(new Docset(fs.DirectoryInfo.New("/nope"), [], [])).Single().Message.Should().Contain("/nope");
		set.Validate(Make(fs)).Should().BeEmpty();
	}
}
