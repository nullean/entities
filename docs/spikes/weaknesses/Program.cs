using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;

namespace Weaknesses;

// Each section is a real row of the docs-builder constraint catalogue, written the way the library lets you today, with what happened when it ran.

public sealed record PublishEnvironment(string Name, string ContentSource);

public sealed record VersioningSystem(int Base, int Current);

public sealed record VersioningRange(int Start, int End);

public sealed record Cta(string Id, IReadOnlyList<string> Benefits);

public sealed record Mount(string Repository, string PathPrefix);

public sealed record Mounts(IReadOnlyList<Mount> Items);

public static class Program
{
	public static void Main()
	{
		A_an_allowed_set_is_stated_twice();
		B_an_ordered_pair_is_found_by_its_name();
		C_a_count_bound_is_global();
		D_a_prefix_collision_is_hand_written();
		E_severity_is_a_mutation_and_there_is_no_strict_mode();
	}

	private static string Rate<T>(ConstraintSet<T> rules, Gen<T> gen)
	{
		var entry = rules.Probe(gen, samples: 400, throwOnContradiction: false).Entries.Single();
		return $"violated by {entry.Violated} of 400 generated values";
	}

	// ── G33: content_source in {next, current, edge} ────────────────────────────

	private static void A_an_allowed_set_is_stated_twice()
	{
		Console.WriteLine("A. G33  content_source is one of next, current, edge");
		var rules = ConstraintSet.From(
			("content-source-known", Constraints<PublishEnvironment>.Always(
				e => e.ContentSource is "next" or "current" or "edge",
				"content_source must be next, current or edge"
			))
		);
		Console.WriteLine($"   plausible data:  {Rate(rules, GenAuto.Of<PublishEnvironment>(AutoMode.Plausible))}");

		try
		{
			GenAuto.Of<PublishEnvironment>(AutoMode.Plausible).Valid(rules).Generate(new PCG(1u, 1UL), null, out _);
		}
		catch (Exception e)
		{
			Console.WriteLine($"   .Valid(rules):   {e.Message.Split('\n')[0]}");
		}

		// the workaround: the set is written again, in the generator
		string[] sources = ["next", "current", "edge"];
		var fixedGen = GenAuto.Of<PublishEnvironment>(
			AutoMode.Plausible,
			o => o.Normalize<PublishEnvironment>(e => e with { ContentSource = sources[e.Name.Length % 3] })
		);
		Console.WriteLine($"   after Normalize: {Rate(rules, fixedGen)}   (and the set is now written twice: once in the rule, once in the generator)");
		Console.WriteLine();
	}

	// ── G89: base <= current ────────────────────────────────────────────────────

	private static void B_an_ordered_pair_is_found_by_its_name()
	{
		Console.WriteLine("B. G89  base <= current");
		var bySpelling = ConstraintSet.From(
			("base-not-after-current", Constraints<VersioningSystem>.Always(v => v.Base <= v.Current, "base must not be after current"))
		);
		var byName = ConstraintSet.From(("start-not-after-end", Constraints<VersioningRange>.Always(v => v.Start <= v.End, "start must not be after end")));
		Console.WriteLine($"   plausible (Base, Current): {Rate(bySpelling, GenAuto.Of<VersioningSystem>(AutoMode.Plausible))}");
		Console.WriteLine(
			$"   plausible (Start, End):    {Rate(byName, GenAuto.Of<VersioningRange>(AutoMode.Plausible))}   <- no better with a pair the generator recognises by name"
		);
		Console.WriteLine(
			$"   adversarial (Start, End):  {Rate(byName, GenAuto.Of<VersioningRange>(AutoMode.Adversarial))}   <- the names only produce the boundary cases (equal, adjacent, inverted), in the hostile modes"
		);
		Console.WriteLine();
	}

	// ── D38 at most 3 benefits ──────────────────────────────────────────────────

	private static void C_a_count_bound_is_global()
	{
		Console.WriteLine("C. D38  a CTA has at most 3 benefits");
		var rules = ConstraintSet.From(("at-most-three-benefits", Constraints<Cta>.Always(c => c.Benefits.Count <= 3, "at most 3 benefits")));
		Console.WriteLine($"   default MaxCollection 4:  {Rate(rules, GenAuto.Of<Cta>(AutoMode.Plausible))}");
		Console.WriteLine(
			$"   MaxCollection 3:          {Rate(rules, GenAuto.Of<Cta>(AutoMode.Plausible, o => o.MaxCollection = 3))}   (but it is 3 for every list in the whole value)"
		);
		Console.WriteLine();
	}

	// ── N14: longest-prefix match needs a segment boundary ──────────────────────

	private static void D_a_prefix_collision_is_hand_written()
	{
		Console.WriteLine("D. N14/G67  one mount's prefix must not swallow another's (repo://reference-foo vs repo://reference)");
		var rules = ConstraintSet.From(
			("mount-prefixes-do-not-overlap",
			Constraints<Mounts>.Check(m =>
			{
				var found = new List<Violation>();
				foreach (var a in m.Items)
				{
					foreach (var b in m.Items)
					{
						var (x, y) = (a.PathPrefix.TrimEnd('/'), b.PathPrefix.TrimEnd('/'));
						if (!ReferenceEquals(a, b) && x.Length < y.Length && y.StartsWith(x, StringComparison.Ordinal) && y[x.Length] != '/')
						{
							found.Add(Violation.Error($"'{y}' is matched by the prefix '{x}' because there is no segment boundary"));
						}
					}
				}
				return found;
			}))
		);
		var clash = new Mounts([new Mount("kibana", "reference"), new Mount("kibana", "reference-foo")]);
		Console.WriteLine($"   hand-written Check finds it: {rules.Validate(clash).Single().Message}");
		Console.WriteLine("   (works, 14 lines, and nothing in the library knows what a path prefix is)");
		Console.WriteLine();
	}

	// ── severity: a warning today, an error as a target; strict builds ──────────

	private static void E_severity_is_a_mutation_and_there_is_no_strict_mode()
	{
		Console.WriteLine("E. 'Warnings fail the build in strict mode', 'Now: warning, Target: error' (the E1-E5 register)");
		var rule = Constraints<PublishEnvironment>.Always(e => e.Name.Length > 0, "an environment needs a name").WithSeverity(Severity.Warning);
		var lenient = ConstraintSet.From(("named", rule));
		var empty = new PublishEnvironment("", "next");
		Console.WriteLine($"   lenient set:           {lenient.Validate(empty).Single().Severity}");
		Console.WriteLine(
			$"   Apply, warning rule:   {(lenient.Apply(new PublishEnvironment("x", "next"), e => e with { Name = "" }).IsOk ? "accepted: only errors reject, and there is no Apply(strict: true)" : "rejected")}"
		);

		var strict = ConstraintSet.From(("named", rule.WithSeverity(Severity.Error)));
		Console.WriteLine($"   strict set:            {strict.Validate(empty).Single().Severity}");
		Console.WriteLine($"   lenient set afterwards: {lenient.Validate(empty).Single().Severity}   <- WithSeverity changed the rule both sets share");
		Console.WriteLine();
	}
}
