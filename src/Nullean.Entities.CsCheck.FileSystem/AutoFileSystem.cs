using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using CsCheck;

namespace Nullean.Entities.CsCheck.FileSystem;

public static class AutoFileSystemExtensions
{
	private const string WorldKey = "Nullean.Entities.CsCheck.FileSystem.World";

	/// <summary>
	/// Teaches AutoGen to build <see cref="IFileSystem"/>, <see cref="IFileInfo"/>, <see cref="IDirectoryInfo"/> and <see cref="IFileSystemInfo"/> members from one
	/// <c>MockFileSystem</c> per generated value (see the notes at the top of <c>FileWorld</c>). Plausible data is files that exist inside the root; the other modes
	/// add missing, empty, symlinked, hidden, outside-the-root, <c>..</c> and wrong-case files.
	/// </summary>
	public static AutoOptions UseFileSystem(this AutoOptions options)
	{
		options.NeedsScope();
		options.Use<IFileSystem>(new WorldGen<IFileSystem>(options, (_, world, _) => world.FileSystem));
		options.Use<IFileInfo>(new WorldGen<IFileInfo>(options, (pcg, world, mode) => world.File(pcg, PickFile(pcg, mode))));
		options.Use<IDirectoryInfo>(new WorldGen<IDirectoryInfo>(options, (pcg, world, mode) => world.Directory(pcg, PickDirectory(pcg, mode))));
		// Boundaries() and Laws() swap these into a valid value, in the file system it already has.
		options.EdgesFrom<IFileInfo>(EdgeFiles);
		options.EdgesFrom<IDirectoryInfo>(EdgeDirectories);
		options.Use<IFileSystemInfo>(
			new WorldGen<IFileSystemInfo>(
				options,
				(pcg, world, mode) => pcg.Next(2) == 0 ? world.File(pcg, PickFile(pcg, mode)) : world.Directory(pcg, PickDirectory(pcg, mode))
			)
		);
		return options;
	}

	private static MockFileSystem? WorldOf(object baseline)
	{
		foreach (var property in baseline.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0))
		{
			var value = property.GetValue(baseline);
			var found = value switch
			{
				MockFileSystem mock => mock,
				IFileSystemInfo info => info.FileSystem as MockFileSystem,
				_ => null,
			};
			if (found is not null)
			{
				return found;
			}
		}
		return null;
	}

	// one file of every kind but the plain one, in the file system the baseline already has
	private static IEnumerable<IFileInfo> EdgeFiles(object baseline) =>
		WorldOf(baseline) is { } mock
			? Enum.GetValues<FileKind>().Where(k => k != FileKind.Existing).Select(k => new FileWorld(mock).File(new PCG(1u, (ulong)k + 1), k))
			: [];

	private static IEnumerable<IDirectoryInfo> EdgeDirectories(object baseline) =>
		WorldOf(baseline) is { } mock
			? Enum.GetValues<DirectoryKind>().Where(k => k != DirectoryKind.Existing).Select(k => new FileWorld(mock).Directory(new PCG(1u, (ulong)k + 1), k))
			: [];

	private static FileKind PickFile(PCG pcg, AutoMode mode)
	{
		var plain = mode switch
		{
			AutoMode.Plausible => 100u,
			AutoMode.Adversarial => 10u,
			_ => 60u,
		};
		if (pcg.Next(100) < plain)
		{
			return FileKind.Existing;
		}
		var others = Enum.GetValues<FileKind>().Where(k => k != FileKind.Existing).ToArray();
		return others[(int)pcg.Next((uint)others.Length)];
	}

	private static DirectoryKind PickDirectory(PCG pcg, AutoMode mode)
	{
		var plain = mode switch
		{
			AutoMode.Plausible => 100u,
			AutoMode.Adversarial => 15u,
			_ => 70u,
		};
		if (pcg.Next(100) < plain)
		{
			return DirectoryKind.Existing;
		}
		var others = Enum.GetValues<DirectoryKind>().Where(k => k != DirectoryKind.Existing).ToArray();
		return others[(int)pcg.Next((uint)others.Length)];
	}

	private sealed class WorldGen<T>(AutoOptions options, Func<PCG, FileWorld, AutoMode, T> draw) : Gen<T>
	{
		public override T Generate(PCG pcg, Size? min, out Size size)
		{
			// One world per generated root value; outside a scope (a generator used on its own) each draw gets its own.
			var world = AutoScope.Current?.GetOrAdd(WorldKey, () => new FileWorld()) ?? new FileWorld();
			size = new Size(0);
			return draw(pcg, world, options.Mode);
		}
	}
}
