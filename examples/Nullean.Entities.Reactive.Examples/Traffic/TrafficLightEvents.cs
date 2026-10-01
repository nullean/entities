// Reactive extension for Traffic/TrafficLight.
//
// The canonical minimal state-machine: every Signal transition must follow
// the standard Red → Green → Yellow → Red cycle.

using System.Reactive.Subjects;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Reactive;
using static Nullean.Entities.Reactive.Constraints<Nullean.Entities.Reactive.Examples.Traffic.SignalChanged>;

namespace Nullean.Entities.Reactive.Examples.Traffic;

// ── Events ────────────────────────────────────────────────────────────────────

internal enum TrafficSignal
{
	Red,
	Green,
	Yellow
}

internal sealed record SignalChanged(string IntersectionId, TrafficSignal From, TrafficSignal To);

// ── Service ───────────────────────────────────────────────────────────────────

internal sealed class TrafficController : IDisposable
{
	private readonly Subject<SignalChanged> _events = new();
	private readonly Dictionary<string, TrafficSignal> _signals = [];

	public IObservable<SignalChanged> Events => _events;

	public void Dispose() => _events.Dispose();

	public void Advance(string intersection)
	{
		var current = _signals.GetValueOrDefault(intersection, TrafficSignal.Red);
		var next = current switch
		{
			TrafficSignal.Red => TrafficSignal.Green,
			TrafficSignal.Green => TrafficSignal.Yellow,
			TrafficSignal.Yellow => TrafficSignal.Red,
			_ => TrafficSignal.Red
		};
		_signals[intersection] = next;
		_events.OnNext(new SignalChanged(intersection, current, next));
	}
}

// ── Spec ──────────────────────────────────────────────────────────────────────

internal static class TrafficLightSpec
{
	/// <summary>Every transition must follow Red→Green→Yellow→Red; no step is ever skipped.</summary>
	public static readonly ReactiveConstraint<SignalChanged> LegalCycle = Transitions((prev, next) =>
	{
		if (prev.IntersectionId != next.IntersectionId)
		{
			return true;
		}
		return (prev.To, next.To) switch
		{
			(TrafficSignal.Green, TrafficSignal.Yellow) => true,
			(TrafficSignal.Yellow, TrafficSignal.Red) => true,
			(TrafficSignal.Red, TrafficSignal.Green) => true,
			_ => false
		};
	}).Named(nameof(LegalCycle));

	public static readonly ReactiveConstraintSet<SignalChanged> All = ReactiveConstraintSet.From((nameof(LegalCycle), LegalCycle));
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class TrafficLightEventTests
{
	private static readonly string[] Intersections = ["Main/1st", "Main/2nd"];

	private static readonly Gen<string> GenIntersection = Gen.Int[0, Intersections.Length - 1].Select(i => Intersections[i]);

	private static readonly Script<TrafficController> TrafficScript = Script
		.For<TrafficController>()
		.Step("Advance", GenIntersection, (ctrl, id) => ctrl.Advance(id))
		.Delays(Gen.Int[0, 100].Select(ms => TimeSpan.FromMilliseconds(ms)))
		.Length(3, 30);

	[Test]
	public void TrafficLightCycle_is_always_legal() => TrafficScript.SampleWith(_ => new TrafficController(), ctrl => ctrl.Events, TrafficLightSpec.All);
}
