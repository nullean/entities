// The path arithmetic behind the Files<T> shapes, public so a rule that needs the answer inside a larger check (a chain that stops at the first problem,
// a message that names the reason) asks the same question the shapes do instead of re-deriving it.

using System.IO.Abstractions;

namespace Nullean.Entities.FileSystem;

public static class FilePaths
{
	/// <summary>The full path with <c>..</c> and <c>.</c> resolved and no trailing separator.</summary>
	public static string Normalize(IFileSystemInfo info) => info.FileSystem.Path.GetFullPath(info.FullName).TrimEnd('/', '\\');

	/// <summary>
	/// Whether <paramref name="path"/> is <paramref name="root"/> or below it. Both are normalized paths; the comparison is ordinal unless <paramref name="comparison"/> says otherwise
	/// (a case-insensitive file system), and a sibling that shares a prefix (<c>/docs-other</c> against <c>/docs</c>) is outside.
	/// </summary>
	public static bool IsInside(string root, string path, StringComparison comparison = StringComparison.Ordinal) =>
		string.Equals(path, root, comparison) || path.StartsWith(root + "/", comparison) || path.StartsWith(root + "\\", comparison);

	/// <summary>Whether the path, once <c>..</c> is resolved, is inside <paramref name="root"/>.</summary>
	public static bool IsInside(IDirectoryInfo root, IFileSystemInfo path, StringComparison comparison = StringComparison.Ordinal) =>
		IsInside(Normalize(root), Normalize(path), comparison);

	/// <summary>The first symbolic link at or above the path, below <paramref name="root"/> (or up to the file system's root when it is null); null when there is none.</summary>
	public static string? SymlinkAt(IFileSystemInfo path, string? root, StringComparison comparison = StringComparison.Ordinal)
	{
		for (var current = (IFileSystemInfo?)path; current is not null; current = Parent(current))
		{
			if (root is not null && (!IsInside(root, Normalize(current), comparison) || string.Equals(Normalize(current), root, comparison)))
			{
				break;
			}
			if (current.LinkTarget is not null)
			{
				return current.FullName;
			}
		}
		return null;
	}

	/// <summary>The first directory between <paramref name="root"/> and the path whose name starts with a dot, or null. The last segment is the file itself and does not count.</summary>
	public static string? HiddenSegment(string root, string path, StringComparison comparison = StringComparison.Ordinal)
	{
		if (!IsInside(root, path, comparison) || path.Length <= root.Length)
		{
			return null;
		}
		var relative = path[root.Length..].TrimStart('/', '\\');
		var segments = relative.Split('/', '\\');
		return segments.Take(segments.Length - 1).FirstOrDefault(s => s.StartsWith('.'));
	}

	/// <summary>
	/// Why a path may not be read as content, or null when it may: it is a symbolic link, or sits below one, or below a hidden directory, between it and <paramref name="root"/>.
	/// The one question a loader asks before it opens a file the author named.
	/// </summary>
	public static string? UnsafeToRead(IFileSystemInfo path, IDirectoryInfo root, StringComparison comparison = StringComparison.Ordinal)
	{
		var rootPath = Normalize(root);
		if (SymlinkAt(path, rootPath, comparison) is { } link)
		{
			return $"it is, or is below, the symbolic link '{link}'";
		}
		return HiddenSegment(rootPath, Normalize(path), comparison) is { } hidden ? $"it is inside the hidden directory '{hidden}'" : null;
	}

	private static IFileSystemInfo? Parent(IFileSystemInfo info) => info is IFileInfo file ? file.Directory : ((IDirectoryInfo)info).Parent;
}
