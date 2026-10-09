// Nullean.Entities.CsCheck — every axis a type declares, attacked from one declaration.
//
//   AutoTest.For(Constraints.Instance).CounterExamples(...).Run();
//
// AutoTest<T> holds what the axes share (the constraint set, a generator of valid values, the counter-examples, the operations, how to sample) and
// exposes each axis as a named method. Report() runs every axis and records what each did; Run() does the same and throws when any failed; Cases lists the
// axes the type actually declares, one per test, for a test framework's data source. An axis the type declares no rule for is reported, never passed in silence.

using System.Text.RegularExpressions;
using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>What an axis means, in the words a failing test should say it in.</summary>
public static class AxisInfo
{
	/// <summary>The rule the axis holds a type to.</summary>
	public static string Law(Axis axis) => axis switch
	{
		Axis.Invariant => "Every generated value satisfies every rule that must hold in every state, and each of those rules can fire.",
		Axis.Idempotency => "Doing an operation twice gives what doing it once gives, for every generated value.",
		Axis.Monotonicity => "No operation moves a value that may only go one way in the other direction.",
		Axis.Conservation => "No operation creates or destroys a quantity that must be conserved.",
		Axis.Absence => "A sequence the domain forbids never happens, whatever the calls and their timing.",
		Axis.Temporal => "What must follow something within a deadline does, however the time between calls is spent.",
		Axis.Liveness => "What must eventually happen does, once a run has settled.",
		Axis.Confluence => "The order things arrive in does not change the result.",
		Axis.Vacancy => "Every rule can fire, and every rule's guard is reached by the data the tests use.",
		_ => "Nothing is declared for this axis."
	};

	/// <summary>What goes wrong in the real system when the law does not hold.</summary>
	public static string Consequence(Axis axis) => axis switch
	{
		Axis.Invariant => "A value that breaks a stated rule is accepted into the domain, and nothing shows the rule can even be broken.",
		Axis.Idempotency => "A retry, a double submit or a re-run changes the state a second time, so it ends up different from a single run.",
		Axis.Monotonicity => "An older result overwrites a newer one, a counter goes back or a timestamp regresses, without anything saying so.",
		Axis.Conservation => "Totals drift: something appears or vanishes between the two halves of a change.",
		Axis.Absence => "A forbidden state is reachable in production by a sequence nobody wrote a test for.",
		Axis.Temporal => "A deadline is missed under timing the tests never tried, so a stuck state is reported as in progress for good.",
		Axis.Liveness => "Something that was started never finishes: an endless spinner, a build that never reports an end.",
		Axis.Confluence => "Two runs over the same inputs publish different outputs depending only on the order they were listed in.",
		Axis.Vacancy => "A rule that can never fire, or a test that never reached it, reports green while checking nothing.",
		_ => "Rules on this axis exist but no test attacks them."
	};
}

/// <summary>One axis as one test: its law and consequence for the failure message, and how to run it. <see cref="Run"/> throws when the axis fails.</summary>
public sealed record AxisCase(Axis Axis, string Law, string Consequence, Func<AxisResult> Run)
{
	/// <summary>The axis name, so a test framework displays one test per axis.</summary>
	public override string ToString() => Axis.ToString();
}

/// <summary>What every axis did for one type: a result per axis, and the rules no axis can attack because nothing declares their shape.</summary>
public sealed record AutoTestReport(IReadOnlyList<AxisResult> Results, IReadOnlyList<string> Unclassified)
{
	/// <summary>The axes that found a bug or proved a test vacuous.</summary>
	public IReadOnlyList<AxisResult.Failed> Failures => [.. Results.OfType<AxisResult.Failed>()];

	/// <summary>True when no axis failed. An axis that is not applicable does not fail unless it was required.</summary>
	public bool Passed => Failures.Count == 0;

	public override string ToString()
	{
		var lines = Results.Select(r => r.ToString()).ToList();
		if (Unclassified.Count > 0)
		{
			lines.Add(
				$"Unclassified: {string.Join(", ", Unclassified)} (sequential rules written by hand: checked, but no axis can choose a technique from their shape)"
			);
		}
		return string.Join("\n", lines);
	}

	/// <summary>Throws one exception that lists every failed axis, so a broken type is diagnosed in one run. Passes the report through otherwise.</summary>
	public AutoTestReport ThrowIfFailed() =>
		Passed
			? this
			: throw new InvalidOperationException(
				$"{Failures.Count} of {Results.Count} axes failed:\n{string.Join("\n\n", Failures.Select(f => f.ToString()))}"
			);
}

/// <summary>Starts an <see cref="AutoTest{T}"/>.</summary>
public static class AutoTest
{
	/// <summary>
	/// An auto-test over <paramref name="constraints"/>. Without <paramref name="valid"/> the values are <c>GenAuto</c> plausible values kept only if they satisfy the set;
	/// pass a generator when the valid values need relations the shape alone does not give (a foreign key, a derived field).
	/// </summary>
	public static AutoTest<T> For<T>(ConstraintSet<T> constraints, Gen<T>? valid = null) => new(constraints, valid, null);

	/// <summary>An auto-test over a generated <c>[Constraints]</c> companion (<c>XConstraints.Instance</c>): its rules and the operations the generator found on <typeparamref name="T"/>.</summary>
	public static AutoTest<T> For<T>(IConstraintsFor<T> companion, Gen<T>? valid = null) => new(companion.All, valid, companion.Actions);

	/// <summary>An auto-test over a type that attaches its own constraints (<see cref="IHaveConstraints{TSelf}"/>).</summary>
	public static AutoTest<T> For<T>(Gen<T>? valid = null) where T : IHaveConstraints<T> => new(T.Constraints, valid, T.Actions);
}

/// <summary>
/// Every axis a type declares, attacked from one declaration. Immutable: each <c>With</c>-style method returns a copy, so a shared base can be specialised per test
/// without the tests seeing each other. See <see cref="AutoTest"/> to start one.
/// </summary>
public sealed partial class AutoTest<T>
{
	private static readonly Axis[] Axes = [Axis.Invariant, Axis.Idempotency, Axis.Monotonicity, Axis.Conservation, Axis.Confluence, Axis.Vacancy];

	private readonly ConstraintSet<T> _constraints;
	// null: each axis draws plausible-or-hostile values for the Data and Time settings in force when it runs, so Data(...) and Time(...) apply wherever they are called.
	private readonly Gen<T>? _valid;
	private readonly IReadOnlyDictionary<string, Func<T, T>> _counterExamples;
	private readonly IReadOnlyList<Func<AutoMode, AutoAction<T>>> _makers;
	private readonly AxisRun _settings;
	private readonly IReadOnlyList<Axis> _required;

	internal AutoTest(ConstraintSet<T> constraints, Gen<T>? valid, EntityActions<T>? actions)
	{
		_constraints = constraints;
		_settings = new AxisRun(Data: AutoMode.Plausible);
		_valid = valid;
		_counterExamples = new Dictionary<string, Func<T, T>>();
		_makers = actions is null ? [] : [.. actions.Select(a => (Func<AutoMode, AutoAction<T>>)(data => AutoAction.From(a, data)))];
		_required = [];
	}

	private AutoTest(
		AutoTest<T> from,
		IReadOnlyDictionary<string, Func<T, T>>? counterExamples = null,
		IReadOnlyList<Func<AutoMode, AutoAction<T>>>? makers = null,
		AxisRun? settings = null,
		IReadOnlyList<Axis>? required = null
	)
	{
		_constraints = from._constraints;
		_valid = from._valid;
		_counterExamples = counterExamples ?? from._counterExamples;
		_makers = makers ?? from._makers;
		_settings = settings ?? from._settings;
		_required = required ?? from._required;
	}

	private Gen<T> CurrentValid => _valid ?? GenAuto.Of<T>(_settings.Data).Valid(_constraints, _settings.Time);

	private IReadOnlyList<AutoAction<T>> CurrentActions => [.. _makers.Select(make => make(_settings.Data))];

	// ── what the axes share ──────────────────────────────────────────────────────

	/// <summary>
	/// Counter-examples, by rule name: each turns a valid value into one that breaks that rule and only it. A rule without one gets one derived from an
	/// edge value where a single property breaks it alone; the others (sequential rules, <c>Idempotent</c>, cross-property rules) need one here, and axis 09 fails without it.
	/// </summary>
	public AutoTest<T> CounterExamples(IReadOnlyDictionary<string, Func<T, T>> counterExamples) =>
		new(this, counterExamples: _counterExamples.Concat(counterExamples).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Last().Value));

	/// <summary>Operations the sequential axes (monotonicity, conservation) run. The generated actions of a <c>[Constraints]</c> companion are already there.</summary>
	public AutoTest<T> Actions(EntityActions<T> actions) =>
		new(this, makers: [.. _makers, .. actions.Select(a => (Func<AutoMode, AutoAction<T>>)(data => AutoAction.From(a, data)))]);

	/// <summary>An operation with no argument, for one that is not a method returning <typeparamref name="T"/>. An operation that returns a wrapper is adapted here: <c>r =&gt; r.Op().Value</c>.</summary>
	public AutoTest<T> Action(string name, Func<T, T> apply) => new(this, makers: [.. _makers, _ => AutoAction.Of(name, apply)]);

	/// <summary>
	/// An operation with one argument. <paramref name="arguments"/> draws the argument from the start value, so it can name something that is really there; without it
	/// the argument is drawn from its type alone, which for a key or an id rarely collides with anything.
	/// </summary>
	public AutoTest<T> Action<TArg>(string name, Func<T, TArg, T> apply, Func<T, Gen<TArg>>? arguments = null) =>
		new(this, makers: [.. _makers, data => AutoAction.Of(name, apply, arguments, data)]);

	/// <summary>An operation built with <see cref="AutoAction"/>.</summary>
	public AutoTest<T> Action(AutoAction<T> action) => new(this, makers: [.. _makers, _ => action]);

	/// <summary>How many values each axis draws. The default is 200.</summary>
	public AutoTest<T> Samples(int samples) => new(this, settings: _settings with { Samples = samples });

	/// <summary>A CsCheck seed, so every axis replays the same data. The default is fixed, so a result does not depend on luck.</summary>
	public AutoTest<T> Seed(string seed) => new(this, settings: _settings with { Seed = seed });

	/// <summary>Plausible (the default), Mixed (some edge values) or Adversarial data for the generated starts and arguments.</summary>
	public AutoTest<T> Data(AutoMode data) => new(this, settings: _settings with { Data = data });

	/// <summary>The clock time-aware rules read.</summary>
	public AutoTest<T> Time(TimeProvider time) => new(this, settings: _settings with { Time = time });

	/// <summary>Where each axis writes what it did.</summary>
	public AutoTest<T> Output(Action<string> writeLine) => new(this, settings: _settings with { WriteLine = writeLine });

	/// <summary>Axes the type must declare a rule for. One it does not (or cannot attack) fails instead of being reported as not applicable.</summary>
	public AutoTest<T> RequireAxes(params Axis[] axes) => new(this, required: [.. _required, .. axes]);

	// ── the axes ─────────────────────────────────────────────────────────────────

	/// <inheritdoc cref="AxisTests.Invariant{T}"/>
	public AxisResult Invariant() => _constraints.Invariant(CurrentValid, _counterExamples, _settings);

	/// <inheritdoc cref="AxisTests.Idempotency{T}"/>
	public AxisResult Idempotency() => _constraints.Idempotency(_valid, _settings);

	/// <inheritdoc cref="AxisTests.Monotonicity{T}(ConstraintSet{T}, IReadOnlyList{AutoAction{T}}, Gen{T}, AxisRun)"/>
	public AxisResult Monotonicity() => _constraints.Monotonicity(CurrentActions, _valid, _settings);

	/// <inheritdoc cref="AxisTests.Conservation{T}(ConstraintSet{T}, IReadOnlyList{AutoAction{T}}, Gen{T}, AxisRun)"/>
	public AxisResult Conservation() => _constraints.Conservation(CurrentActions, _valid, _settings);

	/// <inheritdoc cref="AxisTests.Confluence{T}"/>
	public AxisResult Confluence() => _constraints.Confluence(_valid, _settings);

	/// <inheritdoc cref="AxisTests.Vacancy{T}"/>
	public AxisResult Vacancy() => _constraints.Vacancy(CurrentValid, _counterExamples, _settings);

	// ── composing them ───────────────────────────────────────────────────────────

	/// <summary>Runs every axis and records what each did. Never throws for an axis that fails: that is a <see cref="AxisResult.Failed"/> in the report.</summary>
	public AutoTestReport Report()
	{
		var unclassified = _constraints.AsConstraints().Where(c => c.Axis == Axis.Unclassified).Select(c => c.Name ?? c.Shape.Factory ?? "unnamed rule");
		return new AutoTestReport([.. Axes.Select(Attempt)], [.. unclassified]);
	}

	/// <summary>Runs every axis, writes the report to <see cref="Output"/>, and throws one exception listing every axis that failed.</summary>
	public AutoTestReport Run()
	{
		var report = Report();
		_settings.WriteLine?.Invoke(report.ToString());
		return report.ThrowIfFailed();
	}

	/// <summary>
	/// One case per axis the type declares (plus any <see cref="RequireAxes"/> demands), for a test framework's data source: each test is one axis, its failure message
	/// carries the axis's law and consequence, and an axis with no rule is simply not a test (see <see cref="Report"/> for what is not applicable).
	/// </summary>
	public IReadOnlyList<AxisCase> Cases =>
		[
			.. Axes.Where(a => IsDeclared(a) || _required.Contains(a))
				.Select(a => new AxisCase(a, AxisInfo.Law(a), AxisInfo.Consequence(a), () => RunCase(a)))
		];

	private bool IsDeclared(Axis axis) => axis == Axis.Vacancy ? _constraints.AsConstraints().Count > 0 : _constraints.AsConstraints().Any(c => c.Axis == axis);

	private AxisResult Execute(Axis axis) => axis switch
	{
		Axis.Invariant => Invariant(),
		Axis.Idempotency => Idempotency(),
		Axis.Monotonicity => Monotonicity(),
		Axis.Conservation => Conservation(),
		Axis.Confluence => Confluence(),
		_ => Vacancy()
	};

	// The axis result, with an axis that was required but could not be attacked counted as a failure.
	private AxisResult Attempt(Axis axis)
	{
		AxisResult result;
		try
		{
			result = Execute(axis);
		}
		catch (Exception e) when (e is not OutOfMemoryException)
		{
			return new AxisResult.Failed(axis, e.Message, SeedIn(e.Message));
		}
		return _required.Contains(axis)
			? result switch
			{
				AxisResult.NotApplicable => new AxisResult.Failed(axis, $"{axis} is required (RequireAxes), but the type declares no rule on it."),
				AxisResult.Skipped skipped => new AxisResult.Failed(axis, $"{axis} is required (RequireAxes), but it could not be attacked: {skipped.Reason}."),
				_ => result
			}
			: result;
	}

	private AxisResult RunCase(Axis axis)
	{
		var result = Attempt(axis);
		return result is AxisResult.Failed failed
			? throw new InvalidOperationException($"{axis} failed.\nLaw: {AxisInfo.Law(axis)}\nConsequence: {AxisInfo.Consequence(axis)}\n{failed.Report}")
			: result;
	}

	private static string? SeedIn(string message)
	{
		var match = SeedPattern().Match(message);
		return match.Success ? match.Groups[1].Value : null;
	}

	// CsCheck's own "Set seed: "..." or -e CsCheck_Seed=... to reproduce" message: the documented, user-facing reproduction string. Source-generated so it stays AOT-friendly.
	[GeneratedRegex("Set seed: \"([^\"]+)\"")]
	private static partial Regex SeedPattern();
}
