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
		Func<IFileInfo, string>? describe = null,
		Func<IFileInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(files))] string? filesExpression = null
	) => Each(files, f => f.Exists ? null : $"'{f.FullName}' does not exist.", message, describe, at, filesExpression);

	/// <summary>
	/// Every item names a file that exists, for a model whose items are not files themselves (a table-of-contents entry, a redirect): <paramref name="file"/> resolves
	/// the item to the file it names, and <paramref name="describe"/> and <paramref name="at"/> speak about the item, not the path.
	/// </summary>
	public static Constraint<T> Exist<TItem>(
		Func<T, IEnumerable<TItem>> items,
		Func<T, TItem, IFileInfo> file,
		Func<TItem, string>? describe = null,
		Func<TItem, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	) => EachItem(items, file, f => f.Exists, f => $"'{f.FullName}' does not exist.", describe, at, itemsExpression);

	/// <summary>Every directory exists.</summary>
	public static Constraint<T> DirectoriesExist(
		Func<T, IEnumerable<IDirectoryInfo>> directories,
		string? message = null,
		Func<IDirectoryInfo, string>? describe = null,
		Func<IDirectoryInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(directories))] string? directoriesExpression = null
	) => Each(directories, d => d.Exists ? null : $"The directory '{d.FullName}' does not exist.", message, describe, at, directoriesExpression);

	/// <summary>Every item names a directory that exists; see <see cref="Exist{TItem}"/>.</summary>
	public static Constraint<T> DirectoriesExist<TItem>(
		Func<T, IEnumerable<TItem>> items,
		Func<T, TItem, IDirectoryInfo> directory,
		Func<TItem, string>? describe = null,
		Func<TItem, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	) => EachItem(items, directory, d => d.Exists, d => $"The directory '{d.FullName}' does not exist.", describe, at, itemsExpression);

	/// <summary>
	/// Every path, once <c>..</c> and <c>.</c> are resolved, is inside <paramref name="root"/>: a spec, an image or an include may not reach out of the documentation
	/// folder. The comparison is ordinal; a path equal to the root is inside it.
	/// </summary>
	public static Constraint<T> StayInside(
		Func<T, IDirectoryInfo> root,
		Func<T, IEnumerable<IFileSystemInfo>> paths,
		string? message = null,
		Func<IFileSystemInfo, string>? describe = null,
		Func<IFileSystemInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(paths))] string? pathsExpression = null
	)
	{
		var member = Constraints<T>.MemberOf(pathsExpression);
		return Rule(value =>
		{
			var rootPath = FilePaths.Normalize(root(value));
			return paths(value)
				.Where(p => !FilePaths.IsInside(rootPath, FilePaths.Normalize(p)))
				.Select(p => Report(message ?? describe?.Invoke(p) ?? $"'{p.FullName}' is outside '{rootPath}'.", member, p.FullName, at?.Invoke(p)));
		});
	}

	/// <summary>
	/// No path is a symbolic link, and none sits below a symbolic link: a link can point anywhere, so a file reached through one may not be the file it looks like.
	/// Ancestors are checked up to <paramref name="root"/> (or to the file system's root when it is null).
	/// </summary>
	public static Constraint<T> NotSymlink(
		Func<T, IEnumerable<IFileSystemInfo>> paths,
		Func<T, IDirectoryInfo?>? root = null,
		string? message = null,
		Func<IFileSystemInfo, string>? describe = null,
		Func<IFileSystemInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(paths))] string? pathsExpression = null
	)
	{
		var member = Constraints<T>.MemberOf(pathsExpression);
		return Rule(value =>
		{
			var rootPath = root?.Invoke(value) is { } r ? FilePaths.Normalize(r) : null;
			return paths(value)
				.Select(p => (Path: p, Link: FilePaths.SymlinkAt(p, rootPath)))
				.Where(x => x.Link is not null)
				.Select(
					x =>
						Report(message ?? describe?.Invoke(x.Path) ?? $"'{x.Path.FullName}' is, or is below, the symbolic link '{x.Link}'.",
							member,
							x.Path.FullName,
							at?.Invoke(x.Path)
						)
				);
		});
	}

	/// <summary>No directory between <paramref name="root"/> and the path starts with a dot (<c>.git</c>, <c>.hidden</c>): hidden folders are not part of the content.</summary>
	public static Constraint<T> NoHiddenSegments(
		Func<T, IDirectoryInfo> root,
		Func<T, IEnumerable<IFileSystemInfo>> paths,
		string? message = null,
		Func<IFileSystemInfo, string>? describe = null,
		Func<IFileSystemInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(paths))] string? pathsExpression = null
	)
	{
		var member = Constraints<T>.MemberOf(pathsExpression);
		return Rule(value =>
		{
			var rootPath = FilePaths.Normalize(root(value));
			return paths(value)
				.Select(p => (Path: p, Segment: FilePaths.HiddenSegment(rootPath, FilePaths.Normalize(p))))
				.Where(x => x.Segment is not null)
				.Select(
					x =>
						Report(message ?? describe?.Invoke(x.Path) ?? $"'{x.Path.FullName}' is inside the hidden directory '{x.Segment}'.",
							member,
							x.Path.FullName,
							at?.Invoke(x.Path)
						)
				);
		});
	}

	/// <summary>Every path has one of <paramref name="extensions"/> (with the dot, compared without regard to case).</summary>
	public static Constraint<T> HaveExtension(
		Func<T, IEnumerable<IFileSystemInfo>> paths,
		string[] extensions,
		string? message = null,
		Func<IFileSystemInfo, string>? describe = null,
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
			describe,
			at,
			pathsExpression);

	/// <summary>Every existing file has content: an empty page or image is a file somebody forgot to write. A missing file is not this rule's concern (see <see cref="Exist"/>).</summary>
	public static Constraint<T> NotEmpty(
		Func<T, IEnumerable<IFileInfo>> files,
		string? message = null,
		Func<IFileInfo, string>? describe = null,
		Func<IFileInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(files))] string? filesExpression = null
	) => Each(files, f => f.Exists && f.Length == 0 ? $"'{f.FullName}' is empty." : null, message, describe, at, filesExpression);

	/// <summary>Every item names a file that is not empty when it exists; see <see cref="Exist{TItem}"/>.</summary>
	public static Constraint<T> NotEmpty<TItem>(
		Func<T, IEnumerable<TItem>> items,
		Func<T, TItem, IFileInfo> file,
		Func<TItem, string>? describe = null,
		Func<TItem, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	) => EachItem(items, file, f => !f.Exists || f.Length > 0, f => $"'{f.FullName}' is empty.", describe, at, itemsExpression);

	/// <summary>No existing file is larger than <paramref name="maxBytes"/>: a page or image that large is a mistake, or slows every build and page load.</summary>
	public static Constraint<T> MaxSize(
		Func<T, IEnumerable<IFileInfo>> files,
		long maxBytes,
		string? message = null,
		Func<IFileInfo, string>? describe = null,
		Func<IFileInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(files))] string? filesExpression = null
	) =>
		Each(
			files,
			f => f.Exists && f.Length > maxBytes ? $"'{f.FullName}' is {f.Length} bytes; the limit is {maxBytes}." : null,
			message,
			describe,
			at,
			filesExpression);

	/// <summary>
	/// No two paths differ only in case. They are two files on Linux and one on macOS or Windows, so a tree that holds both cannot be checked out on every machine.
	/// Each spelling after the first of a group is one violation naming the one it collides with; the same path listed twice is the same file, not a collision.
	/// </summary>
	public static Constraint<T> UniqueIgnoringCase(
		Func<T, IEnumerable<IFileSystemInfo>> paths,
		string? message = null,
		Func<IFileSystemInfo, string>? describe = null,
		Func<IFileSystemInfo, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(paths))] string? pathsExpression = null
	)
	{
		var member = Constraints<T>.MemberOf(pathsExpression);
		return Rule(value =>
		{
			var first = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var found = new List<Violation>();
			foreach (var p in paths(value))
			{
				var full = FilePaths.Normalize(p);
				if (first.TryAdd(full, full) || string.Equals(first[full], full, StringComparison.Ordinal))
				{
					continue;
				}
				found.Add(Report(message ?? describe?.Invoke(p) ?? $"'{full}' differs only in case from '{first[full]}'.", member, p.FullName, at?.Invoke(p)));
			}
			return found;
		});
	}

	/// <summary>
	/// No two items name paths that differ only in case, for a model whose items are not files (see <c>Exist</c> for items).
	/// <paramref name="describe"/> and <paramref name="at"/> speak about the later item, the one that collides, and <paramref name="describe"/> is also given the path it collides with.
	/// </summary>
	public static Constraint<T> UniqueIgnoringCase<TItem>(
		Func<T, IEnumerable<TItem>> items,
		Func<T, TItem, IFileSystemInfo> path,
		Func<TItem, string, string>? describe = null,
		Func<TItem, SourceLocation?>? at = null,
		[CallerArgumentExpression(nameof(items))] string? itemsExpression = null
	)
	{
		var member = Constraints<T>.MemberOf(itemsExpression);
		return Rule(value =>
		{
			var first = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var found = new List<Violation>();
			foreach (var item in items(value))
			{
				var info = path(value, item);
				var full = FilePaths.Normalize(info);
				if (first.TryAdd(full, full) || string.Equals(first[full], full, StringComparison.Ordinal))
				{
					continue;
				}
				found.Add(
					Report(
						describe?.Invoke(item, first[full]) ?? $"'{full}' differs only in case from '{first[full]}'.",
						member,
						info.FullName,
						at?.Invoke(item)
					)
				);
			}
			return found;
		});
	}

	// ── plumbing ──────────────────────────────────────────────────────────

	private static Constraint<T> Each<TInfo>(
		Func<T, IEnumerable<TInfo>> items,
		Func<TInfo, string?> problem,
		string? message,
		Func<TInfo, string>? describe,
		Func<TInfo, SourceLocation?>? at,
		string? expression
	) where TInfo : IFileSystemInfo
	{
		var member = Constraints<T>.MemberOf(expression);
		return Rule(
			value =>
				items(value)
					.Select(i => (Item: i, Problem: problem(i)))
					.Where(x => x.Problem is not null)
					.Select(x => Report(message ?? describe?.Invoke(x.Item) ?? x.Problem!, member, x.Item.FullName, at?.Invoke(x.Item)))
		);
	}

	private static Constraint<T> EachItem<TItem, TInfo>(
		Func<T, IEnumerable<TItem>> items,
		Func<T, TItem, TInfo> resolve,
		Func<TInfo, bool> holds,
		Func<TInfo, string> problem,
		Func<TItem, string>? describe,
		Func<TItem, SourceLocation?>? at,
		string? expression
	) where TInfo : IFileSystemInfo
	{
		var member = Constraints<T>.MemberOf(expression);
		return Rule(
			value =>
				items(value)
					.Select(i => (Item: i, Info: resolve(value, i)))
					.Where(x => !holds(x.Info))
					.Select(x => Report(describe?.Invoke(x.Item) ?? problem(x.Info), member, x.Info.FullName, at?.Invoke(x.Item)))
		);
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
}
