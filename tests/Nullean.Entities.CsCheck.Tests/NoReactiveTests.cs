using System.Reflection;
using AwesomeAssertions;
using Nullean.Entities.CsCheck;
using Assembly = System.Reflection.Assembly;

namespace Nullean.Entities.CsCheck.Tests;

public class NoReactiveTests
{
	/// <summary>
	/// Law: the default CsCheck package depends on neither Nullean.Entities.Reactive nor System.Reactive, directly or through anything it references.
	/// Consequence if broken: every consumer of the default test story pulls Rx into its test project, and the opt-in Nullean.Entities.CsCheck.Reactive package stops being optional.
	/// </summary>
	[Test]
	public void The_default_package_references_no_reactive_assembly_anywhere_in_its_closure()
	{
		var seen = new HashSet<string>();
		var pending = new Stack<Assembly>([typeof(AutoTest).Assembly]);
		while (pending.Count > 0)
		{
			var assembly = pending.Pop();
			foreach (var reference in assembly.GetReferencedAssemblies().Where(r => seen.Add(r.Name!)))
			{
				if (TryLoad(reference) is { } loaded)
				{
					pending.Push(loaded);
				}
			}
		}

		seen.Should().Contain("Nullean.Entities", "the walk must have reached the core package, or the next assertion proves nothing");
		seen.Where(n => n.Contains("Reactive", StringComparison.Ordinal)).Should().BeEmpty();
	}

	private static Assembly? TryLoad(AssemblyName name)
	{
		try
		{
			return Assembly.Load(name);
		}
		catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException)
		{
			return null;
		}
	}
}
