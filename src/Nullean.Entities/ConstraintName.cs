// Also compiled into Nullean.Entities.SourceGenerators (a linked file), so the generator and the library cannot disagree about an id.

// netstandard2.0 in the generator has no ^1 index, so the plain form stays.
#pragma warning disable IDE0056

namespace Nullean.Entities;

/// <summary>The id a constraint gets from the member that declares it.</summary>
public static class ConstraintName
{
	/// <summary>
	/// The constraint id for a member name: <c>PathPrefixesUnique</c> becomes <c>path-prefixes-unique</c>, <c>URLPath</c> becomes <c>url-path</c>, <c>EntryTitleMax80</c> becomes <c>entry-title-max-80</c>.
	/// The source generator names every constraint this way, so <c>ConstraintName.Of(nameof(Rules.PathPrefixesUnique))</c> is the id a violation of that rule carries.
	/// </summary>
	public static string Of(string memberName)
	{
		var sb = new System.Text.StringBuilder(memberName.Length + 8);
		for (var i = 0; i < memberName.Length; i++)
		{
			var c = memberName[i];
			if (c == '_')
			{
				if (sb.Length > 0 && sb[sb.Length - 1] != '-')
				{
					_ = sb.Append('-');
				}
				continue;
			}
			if (char.IsUpper(c) && sb.Length > 0 && sb[sb.Length - 1] != '-')
			{
				var previous = memberName[i - 1];
				var nextIsLower = i + 1 < memberName.Length && char.IsLower(memberName[i + 1]);
				if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
				{
					_ = sb.Append('-');
				}
			}
			if (char.IsDigit(c) && sb.Length > 0 && char.IsLetter(sb[sb.Length - 1]))
			{
				_ = sb.Append('-');
			}
			_ = sb.Append(char.ToLowerInvariant(c));
		}
		return sb.ToString();
	}
}
