// Nullean.Entities — what a temporal rule is about, kept next to the rule so a test library can aim at it.
//
// A TemporalRule is a factory of monitors; the closure hides what it watches. The shape keeps the parts a test needs: which events open it,
// which events answer it, and how long it waits. From those a test derives the delays worth trying (just before and just after the window)
// and what a run must contain for passing to mean something (a trigger, a target, a deadline crossed).

namespace Nullean.Entities;

/// <summary>What a <see cref="TemporalRule{TEvent}"/> watches. <see cref="Trigger"/> opens it (null: every event), <see cref="Target"/> answers it, <see cref="Window"/> bounds the wait.</summary>
public sealed record EventShape<TEvent>(
	Axis Axis,
	string Factory,
	Func<TEvent, bool>? Trigger = null,
	Func<TEvent, bool>? Target = null,
	TimeSpan? Window = null
)
{
	/// <summary>A rule built by hand, or by a factory this version does not describe: no axis can aim at it.</summary>
#pragma warning disable CA1000
	public static EventShape<TEvent> Opaque { get; } = new(Axis.Unclassified, "custom");
#pragma warning restore CA1000
}
