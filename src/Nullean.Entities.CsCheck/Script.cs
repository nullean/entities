// Nullean.Entities.CsCheck — Script<TSut>: a CsCheck-driven action sequence for a SUT
// whose reactions are observed as an event stream.
//
// NOTE: this is checkpoint-A scaffolding carried over from the original exploration
// (Nullean.CsCheck.Reactive). The step/delay/length declarations below do not yet
// record any state — SampleWith/RunWith in ScriptExtensions.cs still throw
// NotImplementedException. Driving a SUT from a generated action sequence, replaying
// a shrunk seed, and producing EntityEvent.SmallerFailure traces are the next things
// to build here; nothing downstream depends on this file executing yet.
//
// No doc comments yet (checkpoint-A exploration code) — CS1591 is suppressed project-wide
// in Directory.Build.props rather than per file.

using CsCheck;

namespace Nullean.Entities.CsCheck;

public sealed class Script<TSut>
{
	internal Script()
	{ }

	// Every parameter below is genuinely unused: these are no-op builder stubs, not yet wired
	// to any executed step sequence (see the file-level note above).
#pragma warning disable IDE0060
	public Script<TSut> Step(string name, Action<TSut> action) => this;
	public Script<TSut> Step<TArg>(Gen<TArg> gen, Action<TSut, TArg> action) => this;
	public Script<TSut> Delays(Gen<TimeSpan> delays) => this;
	public Script<TSut> Length(int min, int max) => this;
#pragma warning restore IDE0060
}

public static class Script
{
	public static Script<TSut> For<TSut>() => new();
}
