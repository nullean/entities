// Nullean.Entities.CsCheck — the deeper techniques, behind the same front door.
//
// AutoTest samples. When the state space is small enough to close, EntitySpec can do more with the same rules and operations: prove every reachable state and step
// (Exhaustive), mutation-test the rules themselves (Faults), and check that two operations commute. AutoTest.Deeper(...) opts in; each becomes one more case next to
// the axes, so there is one place to start and one report.

using CsCheck;

namespace Nullean.Entities.CsCheck;

public sealed partial class AutoTest<T>
{
	/// <summary>
	/// Adds the deeper checks, built from the same rules and operations: <c>Exhaustive</c> (every reachable state and step satisfies every rule, or the shortest sequence that
	/// breaks one) and <c>Faults</c> (each deliberately broken operation is noticed by some rule). <paramref name="configure"/> tunes the exploration:
	/// <c>spec =&gt; spec.Domain(...).Boundary(...).Key(...)</c> gives arguments, a bound, and what makes two states the same. The start is a generated valid value unless it says <c>From(...)</c>.
	/// </summary>
	public AutoTest<T> Deeper(Func<EntitySpec<T>, EntitySpec<T>>? configure = null) => new(this, deeper: configure ?? (spec => spec));

	/// <summary>Applying <paramref name="first"/> then <paramref name="second"/> gives what the other order gives, from every reachable state. Adds the deeper checks if they are not on.</summary>
	public AutoTest<T> Commutes(string first, string second) => new(this, deeper: _deeper ?? (spec => spec), commutes: [.. _commutes, (first, second)]);

	/// <summary>Every state reachable by any sequence of operations satisfies every rule, proved over the whole state space. Fails with the shortest sequence that breaks one.</summary>
	public AxisResult Exhaustive()
	{
		var report = BuildSpec().Exhaustive(writeLine: _settings.WriteLine);
		return new AxisResult.Passed(
			Axis.Invariant,
			[.. _constraints.AsConstraints().Select(c => c.Name ?? c.Shape.Factory ?? "unnamed rule")],
			report.States,
			$"(proved over {report.States} reachable states)"
		);
	}

	/// <summary>Each deliberately broken operation is noticed by some rule: a rule is missing if one is not.</summary>
	public AxisResult Faults()
	{
		var report = BuildSpec().Faults(_settings.WriteLine);
		return new AxisResult.Passed(
			Axis.Vacancy,
			[.. _constraints.AsConstraints().Select(c => c.Name ?? c.Shape.Factory ?? "unnamed rule")],
			0,
			$"({report})"
		);
	}

	private EntitySpec<T> BuildSpec()
	{
		var start = CurrentValid.Generate(new PCG(_settings.GeneratorSeed, 1), null, out _);
		var spec = EntitySpec.For(_constraints, _entityActions, start);
		spec = _settings.Time is { } time ? spec.Clock(time) : spec;
		return (_deeper ?? (s => s))(spec);
	}

	private List<AxisCase> DeeperCases()
	{
		if (_deeper is null)
		{
			return [];
		}
		var cases = new List<AxisCase>
		{
			new(
				Axis.Invariant,
				"Every state reachable by any sequence of operations satisfies every rule, over the whole state space.",
				"A rule is broken by a sequence of operations nobody tried, and random sampling happened to miss it.",
				() => WithLaw(Axis.Invariant, "Exhaustive", Exhaustive),
				"Exhaustive"
			),
			new(
				Axis.Vacancy,
				"Every deliberately broken operation is noticed by some rule.",
				"A rule is missing: an operation can be wrong and nothing fails.",
				() => WithLaw(Axis.Vacancy, "Faults", Faults),
				"Faults"
			)
		};
		cases.AddRange(
			_commutes.Select(
				c =>
					new AxisCase(
						Axis.Confluence,
						$"Applying {c.First} then {c.Second} gives what {c.Second} then {c.First} gives, from every reachable state.",
						"The result depends on the order the operations are called in.",
						() =>
							WithLaw(Axis.Confluence, $"Commutes({c.First}, {c.Second})", () =>
							{
								BuildSpec().Commutes(c.First, c.Second, seed: _settings.EffectiveSeed);
								return new AxisResult.Passed(Axis.Confluence, [$"{c.First} and {c.Second} commute"], _settings.Samples);
							}),
						$"Commutes({c.First}, {c.Second})"
					)
			)
		);
		return cases;
	}

	// the same failure text as an axis: what failed, the law, what it costs, then the details. These checks explore a state space in memory, so there is nothing to await.
	private Task<AxisResult> WithLaw(Axis axis, string name, Func<AxisResult> run)
	{
		try
		{
			return Task.FromResult(run());
		}
		catch (Exception e) when (e is not OutOfMemoryException)
		{
			var law = DeeperCases().First(c => c.ToString() == name);
			return Task.FromException<AxisResult>(
				new InvalidOperationException(
					AxisReport.Format(axis, new AxisFailure([("Details", e.Message)]), null, $"{axis} ({name})", law.Law, law.Consequence),
					e
				)
			);
		}
	}

	private static async Task<AxisResult> AttemptDeeperAsync(AxisCase c)
	{
		try
		{
			return await c.RunAsync().ConfigureAwait(false);
		}
		catch (Exception e) when (e is not OutOfMemoryException)
		{
			return new AxisResult.Failed(c.Axis, $"{c}: {e.Message}");
		}
	}
}
