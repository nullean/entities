using System.Text;
using CsCheck;
using Elastic.Documentation.Publishing;
using Nullean.Entities.CsCheck;

namespace SpikeApp;

public static class Repro
{
	private static string Escape(string s)
	{
		var sb = new StringBuilder();
		foreach (var c in s)
		{
			sb.Append(c is >= ' ' and < '\u007f' ? c.ToString() : $"\\u{(int)c:X4}");
		}
		return sb.ToString();
	}

	public static void Run()
	{
		Console.WriteLine("=== Uri.TryCreate repro ===");
		var found = new HashSet<string>();
		foreach (var mode in new[] { AutoMode.Mixed, AutoMode.Adversarial })
		{
			for (var i = 0; i < 600 && found.Count < 4; i++)
			{
				var nav = GenAuto.Of<SiteNavigationDefinition>(mode).Generate(new PCG(5u, (ulong)i + 1), null, out _);
				foreach (var mount in SiteNavigationConstraints.AllMounts(nav))
				{
					if (string.IsNullOrEmpty(mount.PathPrefix))
					{
						continue;
					}
					var text = $"{mount.Source.Scheme}://{mount.PathPrefix.TrimEnd('/')}/";
					try
					{
						_ = Uri.TryCreate(text, UriKind.Absolute, out _);
					}
					catch (Exception e)
					{
						if (found.Add(text))
						{
							Console.WriteLine($"TryCreate threw {e.GetType().Name} for: {Escape(text)}  (length {text.Length})");
						}
					}
				}
			}
		}
		Console.WriteLine($"distinct inputs that make Uri.TryCreate throw: {found.Count}");
	}
}
