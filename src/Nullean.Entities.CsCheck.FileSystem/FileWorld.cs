// Nullean.Entities.CsCheck.FileSystem — generated file-system worlds.
//
// A model that holds IFileSystem, IFileInfo or IDirectoryInfo (a documentation set with its source file, its folder and the images it lists) cannot be built by
// shape: the interfaces have no constructor, and what matters is what is on the disk. UseFileSystem() gives AutoGen a MockFileSystem per generated value: every
// file-system member of that one value belongs to the same world, so SourceFile, Root and FileSystem agree. Plausible data is files that exist inside the root;
// the other modes add what the rules about files exist to catch: a missing file, an empty one, a symbolic link, a file below a linked directory, one below a
// hidden directory, one outside the root, one reached through "..", one in a different case.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using CsCheck;

namespace Nullean.Entities.CsCheck.FileSystem;

internal enum FileKind
{
	Existing,
	Missing,
	Empty,
	Symlink,
	Hidden,
	BelowLinkedDirectory,
	Outside,
	DotDot,
	CaseVariant,
}

internal enum DirectoryKind
{
	Existing,
	Missing,
	Outside,
	Hidden,
	Symlink,
}

internal sealed class FileWorld
{
	private static readonly string[] Words =
	[
		"intro", "guide", "setup", "release", "reference", "overview", "install", "config", "usage", "notes",
		"upgrade", "faq", "glossary", "tutorial", "limits", "security", "api", "cli", "index", "changes",
	];

	private static readonly string[] Folders = ["", "guide", "guide/images", "reference"];

	private int _counter;
	private bool _linkedDirectoryMade;

	public FileWorld()
	{
		FileSystem = new MockFileSystem();
		Root = OperatingSystem.IsWindows() ? @"C:\docs" : "/docs";
		Outside = OperatingSystem.IsWindows() ? @"C:\elsewhere" : "/elsewhere";
		FileSystem.AddDirectory(Root);
		FileSystem.AddDirectory(Outside);
	}

	public MockFileSystem FileSystem { get; }

	public string Root { get; }

	public string Outside { get; }

	private string Combine(params string[] parts) => FileSystem.Path.Combine(parts);

	private string NextName(PCG pcg) => $"{Words[(int)pcg.Next((uint)Words.Length)]}-{_counter++}";

	private string Folder(PCG pcg)
	{
		var folder = Folders[(int)pcg.Next((uint)Folders.Length)];
		return folder.Length == 0 ? Root : Combine(Root, folder.Replace('/', FileSystem.Path.DirectorySeparatorChar));
	}

	public IFileInfo File(PCG pcg, FileKind kind)
	{
		var name = NextName(pcg);
		switch (kind)
		{
			case FileKind.Missing:
				return FileSystem.FileInfo.New(Combine(Folder(pcg), name + ".md"));
			case FileKind.Empty:
				{
					var path = Combine(Folder(pcg), name + ".md");
					FileSystem.AddFile(path, new MockFileData(""));
					return FileSystem.FileInfo.New(path);
				}
			case FileKind.Symlink:
				{
					var target = Combine(Outside, name + ".md");
					FileSystem.AddFile(target, new MockFileData("target"));
					var link = Combine(Root, "link-" + name + ".md");
					FileSystem.File.CreateSymbolicLink(link, target);
					return FileSystem.FileInfo.New(link);
				}
			case FileKind.Hidden:
				{
					var path = Combine(Root, ".hidden", name + ".md");
					FileSystem.AddFile(path, new MockFileData("hidden"));
					return FileSystem.FileInfo.New(path);
				}
			case FileKind.BelowLinkedDirectory:
				{
					var linked = Combine(Root, "linked");
					if (!_linkedDirectoryMade)
					{
						FileSystem.Directory.CreateSymbolicLink(linked, Outside);
						_linkedDirectoryMade = true;
					}
					FileSystem.AddFile(Combine(Outside, name + ".md"), new MockFileData("through a link"));
					return FileSystem.FileInfo.New(Combine(linked, name + ".md"));
				}
			case FileKind.Outside:
				{
					var path = Combine(Outside, name + ".md");
					FileSystem.AddFile(path, new MockFileData("outside"));
					return FileSystem.FileInfo.New(path);
				}
			case FileKind.DotDot:
				{
					var real = Combine(Outside, name + ".md");
					FileSystem.AddFile(real, new MockFileData("reached through .."));
					return FileSystem.FileInfo.New(Combine(Root, "guide", "..", "..", FileSystem.Path.GetFileName(Outside), name + ".md"));
				}
			case FileKind.CaseVariant:
				{
					var path = Combine(Root, name + ".md");
					FileSystem.AddFile(path, new MockFileData("# case"));
					return FileSystem.FileInfo.New(Combine(Root, name.ToUpperInvariant() + ".MD"));
				}
			default:
				{
					var path = Combine(Folder(pcg), name + ".md");
					FileSystem.AddFile(path, new MockFileData("# " + name));
					return FileSystem.FileInfo.New(path);
				}
		}
	}

	public IDirectoryInfo Directory(PCG pcg, DirectoryKind kind)
	{
		switch (kind)
		{
			case DirectoryKind.Missing:
				return FileSystem.DirectoryInfo.New(Combine(Root, "missing-" + NextName(pcg)));
			case DirectoryKind.Outside:
				return FileSystem.DirectoryInfo.New(Outside);
			case DirectoryKind.Hidden:
				{
					var path = Combine(Root, ".hidden");
					FileSystem.AddDirectory(path);
					return FileSystem.DirectoryInfo.New(path);
				}
			case DirectoryKind.Symlink:
				{
					var link = Combine(Root, "dirlink-" + NextName(pcg));
					FileSystem.Directory.CreateSymbolicLink(link, Outside);
					return FileSystem.DirectoryInfo.New(link);
				}
			default:
				return FileSystem.DirectoryInfo.New(Root);
		}
	}
}
