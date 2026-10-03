namespace Nullean.Entities;

/// <summary>
/// Helpers for rules over trees: a table of contents, a navigation, a folder. <see cref="Flatten{T}"/> turns a tree into the sequence of all its nodes so
/// <c>Unique</c>, <c>References</c> and the other shapes can run over every level at once.
/// </summary>
public static class Tree
{
	/// <summary>
	/// Every node of the forest <paramref name="roots"/>, parents before children, in document order. Iterative, so a very deep tree cannot overflow the stack.
	/// A node reached twice (a shared child) is returned each time it is reached; a cycle would not end, so check <c>Acyclic</c> on a graph before flattening it.
	/// </summary>
	public static IEnumerable<T> Flatten<T>(IEnumerable<T> roots, Func<T, IEnumerable<T>> children)
	{
		var stack = new Stack<IEnumerator<T>>();
		stack.Push(roots.GetEnumerator());
		try
		{
			while (stack.Count > 0)
			{
				var current = stack.Peek();
				if (!current.MoveNext())
				{
					stack.Pop().Dispose();
					continue;
				}
				yield return current.Current;
				stack.Push(children(current.Current).GetEnumerator());
			}
		}
		finally
		{
			while (stack.Count > 0)
			{
				stack.Pop().Dispose();
			}
		}
	}

	/// <summary>Every node below <paramref name="root"/>, and the root itself first.</summary>
	public static IEnumerable<T> Descendants<T>(T root, Func<T, IEnumerable<T>> children) => Flatten([root], children);

	/// <summary>The nodes with the path of ancestors that leads to each, for a rule whose message should say where in the tree the node is.</summary>
	public static IEnumerable<(T Node, IReadOnlyList<T> Ancestors)> FlattenWithAncestors<T>(IEnumerable<T> roots, Func<T, IEnumerable<T>> children)
	{
		var path = new List<T>();

		IEnumerable<(T, IReadOnlyList<T>)> Walk(IEnumerable<T> level)
		{
			foreach (var node in level)
			{
				yield return (node, [.. path]);
				path.Add(node);
				foreach (var below in Walk(children(node)))
				{
					yield return below;
				}
				path.RemoveAt(path.Count - 1);
			}
		}

		return Walk(roots);
	}
}
