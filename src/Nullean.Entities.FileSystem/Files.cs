// Nullean.Entities.FileSystem — constraint shapes for files and directories.
//
// A domain that holds IFileInfo / IDirectoryInfo (System.IO.Abstractions) states its file rules as named constraints, the same way it states every other
// rule: Files<Docset>.Exist(d => d.Images), Files<Docset>.StayInside(d => d.Root, d => d.Images). They read the file system the paths came from
// (IFileSystemInfo.FileSystem), so a test that builds the model on a MockFileSystem checks the same code that runs against the disk.
//
// A rule that cannot be evaluated (a path the file system refuses) is reported as a violation of that rule, not thrown: hostile paths are exactly
// what these rules exist for.

using System.IO.Abstractions;
using System.Runtime.CompilerServices;

namespace Nullean.Entities.FileSystem;

#pragma warning disable CA1000
public static class Files<T>
{
	/// <summary>Every file exists. A missing file is one violation, with its full path as evidence.</summary>
	public static Constraint<T> Exist(
		Func<T, IEnumerable<IFileInfo>> files,
		string? message = null,
		Func<IFileInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(files))] string? filesExpression = null
	) => Each(files, f => f.Exists ? null : $"'{f.FullName}' does not exist.", message, at, filesExpression);

	/// <summary>Every directory exists.</summary>
	public static Constraint<T> DirectoriesExist(
		Func<T, IEnumerable<IDirectoryInfo>> directories,
		string? message = null,
		Func<IDirectoryInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(directories))] string? directoriesExpression = null
	) => Each(directories, d => d.Exists ? null : $"The directory '{d.FullName}' does not exist.", message, at, directoriesExpression);

	/// <summary>
	/// Every path, once <c>..</c> and <c>.</c> are resolved, is inside <paramref name="root"/>: a spec, an image or an include may not reach out of the documentation
	/// folder. The comparison is ordinal; a path equal to the root is inside it.
	/// </summary>
	public static Constraint<T> StayInside(
		Func<T, IDirectoryInfo> root,
		Func<T, IEnumerable<IFileSystemInfo>> paths,
		string? message = null,
		Func<IFileSystemInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(paths))] string? pathsExpression = null
	)
	{
		Constraint<T> self = null!;
		var member = Constraints<T>.MemberOf(pathsExpression);
		self = Rule(value =>
		{
			var rootPath = Normalize(root(value));
			return paths(value)
				.Where(p => !IsInside(rootPath, Normalize(p)))
				.Select(p => Report(message ?? $"'{p.FullName}' is outside '{rootPath}'.", member, p.FullName, at?.Invoke(p)));
		});
		return self;
	}

	/// <summary>
	/// No path is a symbolic link, and none sits below a symbolic link: a link can point anywhere, so a file reached through one may not be the file it looks like.
	/// Ancestors are checked up to <paramref name="root"/> (or to the file system's root when it is null).
	/// </summary>
	public static Constraint<T> NotSymlink(
		Func<T, IEnumerable<IFileSystemInfo>> paths,
		Func<T, IDirectoryInfo?>? root = null,
		string? message = null,
		Func<IFileSystemInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(paths))] string? pathsExpression = null
	)
	{
		Constraint<T> self = null!;
		var member = Constraints<T>.MemberOf(pathsExpression);
		self = Rule(value =>
		{
			var rootPath = root?.Invoke(value) is { } r ? Normalize(r) : null;
			return paths(value)
				.Select(p => (Path: p, Link: SymlinkAt(p, rootPath)))
				.Where(x => x.Link is not null)
				.Select(
					x =>
						Report(message ?? $"'{x.Path.FullName}' is, or is below, the symbolic link '{x.Link}'.",
							member,
							x.Path.FullName,
							at?.Invoke(x.Path)
						)
				);
		});
		return self;
	}

	/// <summary>No directory between <paramref name="root"/> and the path starts with a dot (<c>.git</c>, <c>.hidden</c>): hidden folders are not part of the content.</summary>
	public static Constraint<T> NoHiddenSegments(
		Func<T, IDirectoryInfo> root,
		Func<T, IEnumerable<IFileSystemInfo>> paths,
		string? message = null,
		Func<IFileSystemInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(paths))] string? pathsExpression = null
	)
	{
		Constraint<T> self = null!;
		var member = Constraints<T>.MemberOf(pathsExpression);
		self = Rule(value =>
		{
			var rootPath = Normalize(root(value));
			return paths(value)
				.Select(p => (Path: p, Segment: HiddenSegment(rootPath, Normalize(p))))
				.Where(x => x.Segment is not null)
				.Select(
					x =>
						Report(message ?? $"'{x.Path.FullName}' is inside the hidden directory '{x.Segment}'.",
							member,
							x.Path.FullName,
							at?.Invoke(x.Path)
						)
				);
		});
		return self;
	}

	/// <summary>Every path has one of <paramref name="extensions"/> (with the dot, compared without regard to case).</summary>
	public static Constraint<T> HaveExtension(
		Func<T, IEnumerable<IFileSystemInfo>> paths,
		string[] extensions,
		string? message = null,
		Func<IFileSystemInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(paths))] string? pathsExpression = null
	) =>
		Each(
			paths,
			p =>
				extensions.Any(e => string.Equals(p.FileSystem.Path.GetExtension(p.FullName), e, StringComparison.OrdinalIgnoreCase))
					? null
					: $"'{p.FullName}' does not have one of the extensions {string.Join(", ", extensions)}.",
			message,
			at,
			pathsExpression);

	// ── plumbing ──────────────────────────────────────────────────────────

	private static Constraint<T> Each<TInfo>(
		Func<T, IEnumerable<TInfo>> items,
		Func<TInfo, string?> problem,
		string? message,
		Func<TInfo, SourceLocation?>? at,
		string? expression
	) where TInfo : IFileSystemInfo
	{
		Constraint<T> self = null!;
		var member = Constraints<T>.MemberOf(expression);
		self = Rule(
			value =>
				items(value)
					.Select(i => (Item: i, Problem: problem(i)))
					.Where(x => x.Problem is not null)
					.Select(x => Report(message ?? x.Problem!, member, x.Item.FullName, at?.Invoke(x.Item)))
		);
		return self;
	}

	// A rule that throws on a path the file system refuses (or on a null one) is one violation saying so, not an exception out of Validate.
	private static Constraint<T> Rule(Func<T, IEnumerable<Violation>> check)
	{
		Constraint<T> self = null!;
		self = Constraints<T>.Check(value =>
		{
			try
			{
				return [.. check(value)];
			}
			catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException or NullReferenceException)
			{
				return [Violation.Error($"{self.Name ?? "Constraint"} could not check the path: {e.GetType().Name}: {e.Message}")];
			}
		});
		return self;
	}

	private static Violation Report(string message, string? member, string path, SourceLocation? location) =>
		new Violation(message) { Member = member, Evidence = path }.At(location);

	private static string Normalize(IFileSystemInfo info) => info.FileSystem.Path.GetFullPath(info.FullName).TrimEnd('/', '\\');

	private static bool IsInside(string root, string path) =>
		string.Equals(path, root, StringComparison.Ordinal) || path.StartsWith(root + "/", StringComparison.Ordinal) || path.StartsWith(
			root + "\\",
			StringComparison.Ordinal
		);

	// The first link at or above the path (below the root), or null.
	private static string? SymlinkAt(IFileSystemInfo path, string? root)
	{
		for (var current = (IFileSystemInfo?)path; current is not null; current = Parent(current))
		{
			if (root is not null && !IsInside(root, Normalize(current)))
			{
				break;
			}
			if (root is not null && string.Equals(Normalize(current), root, StringComparison.Ordinal))
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

	private static IFileSystemInfo? Parent(IFileSystemInfo info) => info is IFileInfo file ? file.Directory : ((IDirectoryInfo)info).Parent;

	private static string? HiddenSegment(string root, string path)
	{
		if (!IsInside(root, path) || path.Length <= root.Length)
		{
			return null;
		}
		var relative = path[root.Length..].TrimStart('/', '\\');
		var segments = relative.Split('/', '\\');
		// the last segment is the file itself: a dotfile is hidden too, but the rule is about directories
		return segments.Take(segments.Length - 1).FirstOrDefault(s => s.StartsWith('.'));
	}
}
