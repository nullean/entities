using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using AwesomeAssertions;
using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;
using Nullean.Entities.CsCheck.FileSystem;
using Nullean.Entities.FileSystem;

namespace Nullean.Entities.CsCheck.FileSystem.Tests;

// What docs-builder's DocumentationSetDefinition holds: the file system itself, the folder, the docset file and the files it points at.
internal sealed record Docset(IFileSystem FileSystem, IDirectoryInfo Root, IFileInfo SourceFile, IReadOnlyList<IFileInfo> Images);

public class FileWorldTests
{
	private static readonly ConstraintSet<Docset> Rules = ConstraintSet.From(
		("RootExists", Files<Docset>.DirectoriesExist(d => [d.Root])),
		("SourceFileExists", Files<Docset>.Exist(d => [d.SourceFile])),
		("ImagesExist", Files<Docset>.Exist(d => d.Images)),
		("ImagesStayInRoot", Files<Docset>.StayInside(d => d.Root, d => d.Images)),
		("ImagesAreNotSymlinks", Files<Docset>.NotSymlink(d => d.Images, d => d.Root)),
		("NoHiddenFolders", Files<Docset>.NoHiddenSegments(d => d.Root, d => d.Images))
	);

	private static Gen<Docset> Generator(AutoMode mode) => GenAuto.Of<Docset>(mode, o => o.UseFileSystem());

	private static List<Docset> Draw(AutoMode mode, int count = 300) =>
		[.. Enumerable.Range(0, count).Select(i => Generator(mode).Generate(new PCG(5u, (ulong)i + 1), null, out _))];

	[Test]
	public void Plausible_worlds_satisfy_every_file_rule()
	{
		var docsets = Draw(AutoMode.Plausible);

		docsets.Any(d => d.Images.Count > 0).Should().BeTrue();
		docsets.SelectMany(d => Rules.Validate(d)).Should().BeEmpty();
	}

	[Test]
	public void Every_file_system_member_of_one_value_belongs_to_the_same_world()
	{
		foreach (var docset in Draw(AutoMode.Adversarial, 100))
		{
			docset.Root.FileSystem.Should().BeSameAs(docset.FileSystem);
			docset.SourceFile.FileSystem.Should().BeSameAs(docset.FileSystem);
			docset.Images.Should().OnlyContain(i => ReferenceEquals(i.FileSystem, docset.FileSystem));
		}
	}

	[Test]
	public void Two_values_do_not_share_a_world()
	{
		var docsets = Draw(AutoMode.Plausible, 20);

		docsets.Select(d => d.FileSystem).Distinct().Should().HaveCount(20);
	}

	[Test]
	public void Generated_worlds_break_every_file_rule_with_no_hand_made_file_system()
	{
		var report = Rules.Probe(Generator(AutoMode.Mixed), samples: 1500, throwOnContradiction: false);

		report.NeverViolated.Should().BeEmpty();
	}

	[Test]
	public void The_adversarial_kinds_are_all_reachable()
	{
		var docsets = Draw(AutoMode.Adversarial, 600);
		var files = docsets.SelectMany(d => d.Images.Append(d.SourceFile)).ToList();

		files.Any(f => !f.Exists).Should().BeTrue("a missing file");
		files.Any(f => f.Exists && f.Length == 0).Should().BeTrue("an empty file");
		files.Any(f => f.LinkTarget is not null).Should().BeTrue("a symbolic link");
		files.Any(f => f.FullName.Contains(".hidden", StringComparison.Ordinal)).Should().BeTrue("a hidden directory");
		files.Any(f => f.FullName.Contains("elsewhere", StringComparison.Ordinal)).Should().BeTrue("a file outside the root");
		files.Any(f => f.Name.EndsWith(".MD", StringComparison.Ordinal)).Should().BeTrue("a different case");
	}

	[Test]
	public void The_same_seed_builds_the_same_world()
	{
		string Describe(Docset d) => string.Join("|", d.Images.Select(i => i.FullName).Append(d.SourceFile.FullName).Append(d.Root.FullName));

		var first = Generator(AutoMode.Mixed).Generate(new PCG(9u, 4UL), null, out _);
		var second = Generator(AutoMode.Mixed).Generate(new PCG(9u, 4UL), null, out _);

		Describe(first).Should().Be(Describe(second));
	}

	[Test]
	public void The_laws_hold_for_the_file_rules_with_counter_examples_built_in_the_same_world()
	{
		var valid = Draw(AutoMode.Plausible, 40).First(d => d.Images.Count > 0);
		var examples = new Dictionary<string, Func<Docset, Docset>>
		{
			["RootExists"] = d => d with { Root = d.FileSystem.DirectoryInfo.New("/docs/not-there"), Images = [] },
			["SourceFileExists"] = d => d with { SourceFile = d.FileSystem.FileInfo.New("/docs/not-there.md") },
			["ImagesExist"] = d => d with { Images = [d.FileSystem.FileInfo.New("/docs/not-there.png")] },
			["ImagesStayInRoot"] = d =>
			{
				((MockFileSystem)d.FileSystem).AddFile("/elsewhere/out.png", "x");
				return d with { Images = [d.FileSystem.FileInfo.New("/elsewhere/out.png")] };
			},
			["ImagesAreNotSymlinks"] = d =>
			{
				((MockFileSystem)d.FileSystem).AddFile("/docs/real.png", "x");
				d.FileSystem.File.CreateSymbolicLink("/docs/link.png", "/docs/real.png");
				return d with { Images = [d.FileSystem.FileInfo.New("/docs/link.png")] };
			},
			["NoHiddenFolders"] = d =>
			{
				((MockFileSystem)d.FileSystem).AddFile("/docs/.git/x.png", "x");
				return d with { Images = [d.FileSystem.FileInfo.New("/docs/.git/x.png")] };
			},
		};

		Rules.Laws(valid, examples).Results.Should().HaveCount(6);
	}

	[Test]
	public void Without_UseFileSystem_the_error_says_which_package_and_call_to_use()
	{
		var act = () => GenAuto.Of<Docset>(AutoMode.Plausible).Generate(new PCG(1u, 1UL), null, out _);

		act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("UseFileSystem()");
	}

	[Test]
	public void Laws_generates_counter_examples_for_file_members_in_the_baselines_own_file_system()
	{
		var valid = Draw(AutoMode.Plausible, 40).First(d => d.Images.Count > 0);

		// A missing root also puts every image outside it, so RootExists has no counter-example of its own: that is a true overlap, reported rather than failed.
		var report = Rules.Laws(valid, requireCounterExamples: false, options: o => o.UseFileSystem());

		report
			.Results
			.Select(r => r.Constraint)
			.Should()
			.Contain(["SourceFileExists", "ImagesExist", "ImagesStayInRoot", "ImagesAreNotSymlinks", "NoHiddenFolders"]);
		report.Results.Should().OnlyContain(r => r.CounterExample.StartsWith("generated:", StringComparison.Ordinal));
		report.Results.Single(r => r.Constraint == "ImagesAreNotSymlinks").CounterExample.Should().Contain("Images = [");
	}

	[Test]
	public void Boundaries_lists_what_each_file_kind_does_to_each_member()
	{
		var valid = Draw(AutoMode.Plausible, 40).First(d => d.Images.Count > 0);

		var report = Rules.Boundaries(valid, options: o => o.UseFileSystem());

		report.Results.Where(r => r.Property == "SourceFile").SelectMany(r => r.RejectedBy).Should().Contain("SourceFileExists");
		report
			.Results
			.Where(r => r.Property == "Images")
			.SelectMany(r => r.RejectedBy)
			.Should()
			.Contain(["ImagesExist", "ImagesStayInRoot", "ImagesAreNotSymlinks", "NoHiddenFolders"]);
	}
}
