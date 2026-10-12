// Nullean.Entities.CsCheck — what one axis found.
//
// An axis test either has nothing to attack (NotApplicable), cannot be attacked with what it was given (Skipped, with the reason),
// passes (Passed, naming the rules it attacked so a pass is auditable), or fails. The named axis methods throw on a failure, the
// way every other terminal here does; AutoTest<T>.Run() catches the throw and records it as Failed so one report can say it all.

namespace Nullean.Entities.CsCheck;

/// <summary>The outcome of attacking one <see cref="Axis"/>.</summary>
public abstract record AxisResult(Axis Axis)
{
	/// <summary>The axis ran: every rule on it held, and each was exercised (a rule that was never reached fails as vacuous instead).</summary>
	public sealed record Passed(Axis Axis, IReadOnlyList<string> Rules, int Samples, string? Note = null) : AxisResult(Axis)
	{
		public override string ToString() =>
			$"{Axis}: passed ({Rules.Count} rule{(Rules.Count == 1 ? "" : "s")}: {string.Join(", ", Rules)}; {Samples} samples){(Note is null ? "" : $" {Note}")}";
	}

	/// <summary>The type declares no rule on this axis. Reported, never a silent pass; <c>RequireAxes</c> turns it into a failure.</summary>
	public sealed record NotApplicable(Axis Axis) : AxisResult(Axis)
	{
		public override string ToString() => $"{Axis}: not applicable (no rule declared on this axis)";
	}

	/// <summary>There are rules on the axis, but it cannot be attacked with what was supplied (no actions, a shape mapped without a way back).</summary>
	public sealed record Skipped(Axis Axis, string Reason) : AxisResult(Axis)
	{
		public override string ToString() => $"{Axis}: skipped ({Reason})";
	}

	/// <summary>The axis found a bug, or proved the test vacuous. Only <c>AutoTest.Run()</c> produces this; a named axis method throws.</summary>
	public sealed record Failed(Axis Axis, string Report, string? Seed = null) : AxisResult(Axis)
	{
		public override string ToString() => $"{Axis}: FAILED{(Seed is null ? "" : $" (seed {Seed})")}\n{Report}";
	}

	/// <summary>Throws when this result is a failure; passes the other results through. For a test that calls <c>AutoTest.Run()</c> and wants one assertion.</summary>
	public AxisResult ThrowIfFailed() =>
		this is Failed failed ? throw new InvalidOperationException(failed.ToString()) : this;
}

/// <summary>
/// How an axis method samples. Defaults are fixed, so a result (and a vacuity count) is the same on every run; a failure prints the seed that
/// replays it. <see cref="Seed"/> is a CsCheck seed string, <see cref="GeneratorSeed"/> seeds the <c>Laws</c> generator draws.
/// </summary>
public sealed record AxisRun(
	int Samples = 200,
	string? Seed = null,
	uint GeneratorSeed = 20_260_101,
	AutoMode Data = AutoMode.Mixed,
	TimeProvider? Time = null,
	Action<string>? WriteLine = null,
	Action<AutoOptions>? Options = null
)
{
	/// <summary>The seed used when none is given: fixed, so a green or vacuous result does not depend on luck.</summary>
	public const string DefaultSeed = "0N0XIzNsQ0O2";

	internal string EffectiveSeed => Seed ?? DefaultSeed;
}
