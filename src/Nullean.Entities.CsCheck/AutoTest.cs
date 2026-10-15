// Nullean.Entities.CsCheck — every axis a type declares, attacked from one declaration.
//
//   AutoTest.For(Constraints.Instance).CounterExamples(...).RunAsync();
//   AutoTest.For(rules, () => new Service()).MutateAsync("Op", s => s.OpAsync()).RunAsync();   // a service or a class changed in place: see AutoTestSubject.cs
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

/// <summary>One axis as one test: its law and consequence for the failure message, and how to run it. <see cref="RunAsync"/> throws when the axis fails.</summary>
public sealed record AxisCase(Axis Axis, string Law, string Consequence, Func<Task<AxisResult>> RunAsync, string? Name = null)
{
	/// <summary>The axis name (or the name of the deeper technique), so a test framework displays one test per case.</summary>
	public override string ToString() => Name ?? Axis.ToString();
}

/// <summary>What every axis did for one type: a result per axis, and the rules no axis can attack because nothing declares their shape.</summary>
public sealed record AutoTestReport(IReadOnlyList<AxisResult> Results, IReadOnlyList<string> Unclassified, IReadOnlyList<string>? NotOperations = null)
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
		if (NotOperations is { Count: > 0 })
		{
			lines.Add($"Not run as operations: {string.Join(", ", NotOperations)} (public methods no Mutate or Action names: they are never attacked)");
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
public static partial class AutoTest
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
	private readonly EntityActions<T>? _entityActions;
	private readonly Func<EntitySpec<T>, EntitySpec<T>>? _deeper;
	private readonly IReadOnlyList<(string First, string Second)> _commutes;
	private readonly Subject<T>? _subject;
	private readonly IReadOnlyList<SubjectOp<T>> _ops;
	private readonly (int Min, int Max) _steps;

	internal AutoTest(ConstraintSet<T> constraints, Gen<T>? valid, EntityActions<T>? actions, object? subject = null)
	{
		_subject = (Subject<T>?)subject;
		_ops = [];
		_steps = (1, 5);
		_constraints = constraints;
		_settings = new AxisRun(Data: AutoMode.Plausible, Options: o => o.From(constraints), CounterExamplesRequired: subject is null);
		_valid = valid;
		_counterExamples = new Dictionary<string, Func<T, T>>();
		_makers = actions is null ? [] : [.. actions.Select(a => (Func<AutoMode, AutoAction<T>>)(data => AutoAction.From(a, data)))];
		_required = [];
		_entityActions = actions;
		_commutes = [];
	}

	private AutoTest(
		AutoTest<T> from,
		IReadOnlyDictionary<string, Func<T, T>>? counterExamples = null,
		IReadOnlyList<Func<AutoMode, AutoAction<T>>>? makers = null,
		AxisRun? settings = null,
		IReadOnlyList<Axis>? required = null,
		Func<EntitySpec<T>, EntitySpec<T>>? deeper = null,
		IReadOnlyList<(string First, string Second)>? commutes = null,
		IReadOnlyList<SubjectOp<T>>? ops = null,
		(int Min, int Max)? steps = null
	)
	{
		_subject = from._subject;
		_ops = ops ?? from._ops;
		_steps = steps ?? from._steps;
		_constraints = from._constraints;
		_valid = from._valid;
		_counterExamples = counterExamples ?? from._counterExamples;
		_makers = makers ?? from._makers;
		_settings = settings ?? from._settings;
		_required = required ?? from._required;
		_entityActions = from._entityActions;
		_deeper = deeper ?? from._deeper;
		_commutes = commutes ?? from._commutes;
	}

	private Gen<T> CurrentValid => _valid ?? GenAuto.Of<T>(_settings.Data, _settings.Options).Valid(_constraints, _settings.Time);

	private IReadOnlyList<AutoAction<T>> CurrentActions =>
		[.. _makers.Select(make => make(_settings.Data)), .. _subject?.Actions(_ops, _settings.Data, _steps) ?? []];

	// ── what the axes share ──────────────────────────────────────────────────────

	/// <summary>
	/// Counter-examples, by rule name: each turns a valid value into one that breaks that rule and only it. A rule without one gets one derived from an
	/// edge value where a single property breaks it alone; the others (sequential rules, <c>Idempotent</c>, cross-property rules) need one here, and axis 09 fails without it.
	/// </summary>
	public AutoTest<T> CounterExamples(IReadOnlyDictionary<string, Func<T, T>> counterExamples) =>
		new(this, counterExamples: _counterExamples.Concat(counterExamples).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Last().Value));

	/// <summary>
	/// The counter-example for one rule, named by the rule itself rather than by a string: a rename cannot orphan it and a typo does not compile.
	/// The rule must be in this auto-test's set.
	/// </summary>
	public AutoTest<T> CounterExample(Constraint<T> rule, Func<T, T> breakIt)
	{
		var named = _constraints.AsConstraints().FirstOrDefault(c => ReferenceEquals(c, rule) || (rule.Name is not null && c.Name == rule.Name));
		return named?.Name is { } name
			? CounterExamples(new Dictionary<string, Func<T, T>> { [name] = breakIt })
			: throw new ArgumentException(
				$"The rule {rule.Name ?? rule.Shape.Factory ?? "(unnamed)"} is not in this set, so a counter-example for it would never run.",
				nameof(rule)
			);
	}

	/// <summary>Configures the generated values (<c>UseFileSystem()</c>, <c>Normalize&lt;T&gt;(...)</c>) for the starts and for the search for counter-examples.</summary>
	public AutoTest<T> Options(Action<AutoOptions> configure) =>
		new(this, settings: _settings with { Options = _settings.Options is { } existing ? existing + configure : configure });

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

	/// <inheritdoc cref="AxisTests.InvariantAsync{T}"/>
	public Task<AxisResult> InvariantAsync() => _constraints.InvariantAsync(CurrentValid, _counterExamples, _settings, CurrentActions);

	/// <inheritdoc cref="AxisTests.IdempotencyAsync{T}"/>
	public Task<AxisResult> IdempotencyAsync() => _constraints.IdempotencyAsync(_valid, _settings);

	/// <inheritdoc cref="AxisTests.MonotonicityAsync{T}(ConstraintSet{T}, IReadOnlyList{AutoAction{T}}, Gen{T}, AxisRun)"/>
	public Task<AxisResult> MonotonicityAsync() => _constraints.MonotonicityAsync(CurrentActions, _valid, _settings);

	/// <inheritdoc cref="AxisTests.ConservationAsync{T}(ConstraintSet{T}, IReadOnlyList{AutoAction{T}}, Gen{T}, AxisRun)"/>
	public Task<AxisResult> ConservationAsync() => _constraints.ConservationAsync(CurrentActions, _valid, _settings);

	/// <inheritdoc cref="AxisTests.ConfluenceAsync{T}"/>
	public Task<AxisResult> ConfluenceAsync() => _constraints.ConfluenceAsync(_valid, _settings);

	/// <inheritdoc cref="AxisTests.VacancyAsync{T}"/>
	public Task<AxisResult> VacancyAsync() => _constraints.VacancyAsync(CurrentValid, _counterExamples, _settings);

	/// <summary>
	/// For each rule: the counter-example you gave, one the search found (with a short description of the change), or none.
	/// Run it once on a new type to see what you still have to write. With <paramref name="alsoSearchGiven"/>, a rule you gave one for also says what the search finds for it
	/// (<see cref="CounterExampleLine.Description"/> is then set on a <see cref="CounterExampleKind.Given"/> line), so the ones the search would have found can be dropped.
	/// </summary>
	public CounterExampleReport CounterExampleReport(bool alsoSearchGiven = false)
	{
		var rules = _constraints.AsConstraints().Where(c => c.Name is not null).ToList();
		List<string> given = [.. rules.Where(c => _counterExamples.ContainsKey(c.Name!)).Select(c => c.Name!)];
		List<string> wanted = alsoSearchGiven
			? [.. rules.Select(c => c.Name!)]
			: [.. rules.Where(c => !_counterExamples.ContainsKey(c.Name!)).Select(c => c.Name!)];
		var bases = new List<T>();
		var valid = CurrentValid;
		for (var i = 0; i < 25; i++)
		{
			bases.Add(valid.Generate(new PCG(_settings.GeneratorSeed, (ulong)i + 1), null, out _));
		}
		var found = CounterExampleSearch.Find(_constraints, bases, wanted, _settings.Time, _settings.Options);
		return new CounterExampleReport([
			.. given.Select(
				n =>
					new CounterExampleLine(
						n,
						CounterExampleKind.Given,
						found.TryGetValue(n, out var also) ? also.Description : null,
						found.TryGetValue(n, out var overlap) ? overlap.Overlaps : []
					)
			),
			.. wanted
				.Except(given)
				.Where(found.ContainsKey)
				.Select(n => new CounterExampleLine(n, CounterExampleKind.Found, found[n].Description, found[n].Overlaps)),
			.. wanted.Except(given).Where(n => !found.ContainsKey(n)).Select(n => new CounterExampleLine(n, CounterExampleKind.None, null, []))
		]);
	}

	// ── composing them ───────────────────────────────────────────────────────────

	/// <summary>Runs every axis and records what each did. Never throws for an axis that fails: that is a <see cref="AxisResult.Failed"/> in the report.</summary>
	public async Task<AutoTestReport> ReportAsync()
	{
		var unclassified = _constraints.AsConstraints().Where(c => c.Axis == Axis.Unclassified).Select(c => c.Name ?? c.Shape.Factory ?? "unnamed rule");
		var results = new List<AxisResult>();
		foreach (var axis in Axes)
		{
			results.Add(await AttemptAsync(axis).ConfigureAwait(false));
		}
		foreach (var deeper in DeeperCases())
		{
			results.Add(await AttemptDeeperAsync(deeper).ConfigureAwait(false));
		}
		return new AutoTestReport(results, [.. unclassified], _subject is null ? null : [.. UnusedMethods()]);
	}

	/// <summary>Runs every axis, writes the report to <see cref="Output"/>, and throws one exception listing every axis that failed.</summary>
	public async Task<AutoTestReport> RunAsync()
	{
		var report = await ReportAsync().ConfigureAwait(false);
		_settings.WriteLine?.Invoke(report.ToString());
		return report.ThrowIfFailed();
	}

	/// <summary>
	/// One case per axis the type declares (plus any <see cref="RequireAxes"/> demands), for a test framework's data source: each test is one axis, its failure message
	/// carries the axis's law and consequence, and an axis with no rule is simply not a test (see <see cref="ReportAsync"/> for what is not applicable).
	/// </summary>
	public IReadOnlyList<AxisCase> Cases =>
		[
			.. Axes.Where(a => IsDeclared(a) || _required.Contains(a))
				.Select(a => new AxisCase(a, AxisInfo.Law(a), AxisInfo.Consequence(a), () => RunCaseAsync(a))),
			.. DeeperCases()
		];

	/// <summary>
	/// The cases as a test framework's data source: <c>[MethodDataSource(nameof(Axes))]</c> over a method that returns this, with one test that awaits <c>axis.RunAsync()</c>.
	/// Each is a function, so the framework creates and runs one axis at a time and a failing axis fails only its own test.
	/// </summary>
	public IEnumerable<Func<AxisCase>> AsDataSource() => Cases.Select(c => (Func<AxisCase>)(() => c));

	private bool IsDeclared(Axis axis) => axis == Axis.Vacancy ? _constraints.AsConstraints().Count > 0 : _constraints.AsConstraints().Any(c => c.Axis == axis);

	private Task<AxisResult> ExecuteAsync(Axis axis) => axis switch
	{
		Axis.Invariant => InvariantAsync(),
		Axis.Idempotency => IdempotencyAsync(),
		Axis.Monotonicity => MonotonicityAsync(),
		Axis.Conservation => ConservationAsync(),
		Axis.Confluence => ConfluenceAsync(),
		_ => VacancyAsync()
	};

	// The axis result, with an axis that was required but could not be attacked counted as a failure.
	private async Task<AxisResult> AttemptAsync(Axis axis)
	{
		AxisResult result;
		try
		{
			result = await ExecuteAsync(axis).ConfigureAwait(false);
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

	private async Task<AxisResult> RunCaseAsync(Axis axis)
	{
		var result = await AttemptAsync(axis).ConfigureAwait(false);
		return result is AxisResult.Failed failed
			? throw new InvalidOperationException(
				failed.IsFormatted
					? failed.Report
					: $"{axis} failed.\n\nProblem      {failed.Report}\n\nLaw          {AxisInfo.Law(axis)}\nConsequence  {AxisInfo.Consequence(axis)}"
			)
			: result;
	}

	private static string? SeedIn(string message)
	{
		var match = SeedPattern().Match(message);
		return match.Success ? match.Groups[1].Value : null;
	}

	// CsCheck's own "Set seed: "..." or -e CsCheck_Seed=... to reproduce" message: the documented, user-facing reproduction string. Source-generated so it stays AOT-friendly.
	[GeneratedRegex("\\.Seed\\(\"([^\"]+)\"\\)")]
	private static partial Regex SeedPattern();
}

/// <summary>Where a rule's counter-example comes from.</summary>
public enum CounterExampleKind
{
	/// <summary>You gave it.</summary>
	Given,

	/// <summary>The search found a single-step change that breaks this rule and only it.</summary>
	Found,

	/// <summary>Nothing the search tried breaks this rule alone: only you know how to.</summary>
	None
}

/// <summary>One rule, and where its counter-example comes from. <paramref name="Description"/> says in a few words what the search changed, when it found one; <paramref name="Overlaps"/> are the other rules it breaks too.</summary>
public sealed record CounterExampleLine(string Rule, CounterExampleKind Kind, string? Description, IReadOnlyList<string> Overlaps);

/// <summary>What <c>AutoTest.CounterExampleReport()</c> found: how many rules still need a counter-example, and the ones the search wrote for you.</summary>
public sealed record CounterExampleReport(IReadOnlyList<CounterExampleLine> Lines)
{
	private static string OverlapNote(CounterExampleLine line) => line.Overlaps.Count == 0 ? "" : " (also breaks " + string.Join(", ", line.Overlaps) + ")";

	public IReadOnlyList<string> Missing => [.. Lines.Where(l => l.Kind == CounterExampleKind.None).Select(l => l.Rule)];

	public override string ToString() =>
		string.Join(
			"\n",
			Lines.Select(l =>
					l.Kind switch
					{
						CounterExampleKind.Given => $"{l.Rule}: given",
						CounterExampleKind.Found => $"{l.Rule}: found ({l.Description}){OverlapNote(l)}",
						_ => $"{l.Rule}: NONE FOUND, write one"
					})
		);
}
