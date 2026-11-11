// Nullean.Entities.CsCheck — conformance of real code to a SystemSpec: the model decides what happens next, the real handlers do it.
//
// A SystemSpec proves the design. Whether the code you ship behaves like it is a separate question, and it is asked one step at a time: the model
// picks the next event (deliver this message to billing, crash billing at "after-gateway" while it handles it, restart it), the same event is done
// to a real host built around the node's real handler and fakes, and after every step the host must agree with the model on two things:
//
//   its state     what stateOf projects from it (committed rows, what the fake gateway charged) equals the node's state in the model
//   what it sent  the messages it published, or the calls it made out, equal the messages the model's handler sent
//
// Nodes without a host stay in the model. Walks are sampled (CsCheck shrinks a failing one) or, over a closed state space, every transition is
// checked down a shortest path.

using System.Globalization;
using System.Text;
using CsCheck;

namespace Nullean.Entities.CsCheck;

/// <summary>Thrown by <see cref="CrashPoints.At"/> at an armed point: the host's process "dies" there.</summary>
public sealed class SimulatedCrashException(string point) : Exception($"Simulated crash at '{point}'.")
{
	/// <summary>The point that was armed.</summary>
	public string Point { get; } = point;
}

/// <summary>
/// The crash points of a host, for its fakes to call: <c>crashes.At("after-gateway")</c> right after the fake gateway charges. A conformance run arms
/// one before a delivery the model crashes in; reaching it then throws <see cref="SimulatedCrashException"/>, and the host is restarted.
/// </summary>
public sealed class CrashPoints
{
	/// <summary>The point that will throw when reached, if any.</summary>
	public string? Armed { get; private set; }

	/// <summary>The next time <paramref name="point"/> is reached, crash.</summary>
	public void Arm(string point) => Armed = point;

	/// <summary>Called by the host's code at a crash point. Throws when the point is armed; does nothing otherwise.</summary>
	public void At(string point)
	{
		if (Armed == point)
		{
			Armed = null;
			throw new SimulatedCrashException(point);
		}
	}
}

/// <summary>A real host bound to a node of a <see cref="SystemSpec"/>: see <see cref="Host.For{TSut, TState}"/>.</summary>
public sealed class HostBinding
{
	internal HostBinding(
		INode node,
		Func<object> create,
		Func<object, object, Task> deliver,
		Func<object, object> stateOf,
		Func<object, IEnumerable<object>>? sent,
		Func<object, string, object?, Task>? client,
		Action<object, string>? arm,
		Func<object, object>? restart
	)
	{
		Node = node;
		Create = create;
		Deliver = deliver;
		StateOf = stateOf;
		Sent = sent;
		Client = client;
		Arm = arm;
		Restart = restart;
	}

	/// <summary>The node this host plays.</summary>
	public INode Node { get; }

	internal Func<object> Create { get; }

	internal Func<object, object, Task> Deliver { get; }

	internal Func<object, object> StateOf { get; }

	internal Func<object, IEnumerable<object>>? Sent { get; }

	internal Func<object, string, object?, Task>? Client { get; }

	internal Action<object, string>? Arm { get; }

	internal Func<object, object>? Restart { get; }
}

/// <summary>Binds real code to a node of a <see cref="SystemSpec"/>, for <see cref="SystemSpec.ConformAsync(HostBinding, int, int, string, long)"/>.</summary>
public static class Host
{
	/// <summary>
	/// A real host for <paramref name="node"/>.
	/// <list type="bullet">
	/// <item><paramref name="create"/> builds a fresh one: in-memory storage, fakes for what it calls out to, a <see cref="CrashPoints"/> they share.</item>
	/// <item><paramref name="deliver"/> hands it a message, the way the broker would (call the consumer).</item>
	/// <item><paramref name="stateOf"/> projects what it holds onto the node's model state.</item>
	/// <item><paramref name="sent"/> returns, and forgets, what it sent since the last call: what it published and the calls it made out. Without it, sends are not compared.</item>
	/// <item><paramref name="client"/> performs one of the node's <c>Client</c> requests, by name, with its argument (null when it takes none).</item>
	/// <item><paramref name="crash"/> arms a crash point (<see cref="CrashPoints.Arm"/>), and <paramref name="restart"/> brings a host back after a crash, from what it had made durable (a new host over the same storage).</item>
	/// </list>
	/// </summary>
	public static HostBinding For<TSut, TState>(
		Node<TState> node,
		Func<TSut> create,
		Func<TSut, object, Task> deliver,
		Func<TSut, TState> stateOf,
		Func<TSut, IEnumerable<object>>? sent = null,
		Func<TSut, string, object?, Task>? client = null,
		Action<TSut, string>? crash = null,
		Func<TSut, TSut>? restart = null
	) where TSut : notnull where TState : notnull =>
		new(
			node,
			() => create(),
			(sut, message) => deliver((TSut)sut, message),
			sut => stateOf((TSut)sut),
			sent is null ? null : sut => sent((TSut)sut),
			client is null ? null : (sut, name, arg) => client((TSut)sut, name, arg),
			crash is null ? null : (sut, point) => crash((TSut)sut, point),
			restart is null ? null : sut => restart((TSut)sut)
		);
}

/// <summary>How many walks or transitions a conformance run checked.</summary>
public sealed record SystemConformanceReport(int Checked, string Mode)
{
	/// <inheritdoc />
	public override string ToString() => $"{Checked} {Mode} conformed";
}

public sealed partial class SystemSpec
{
	/// <summary>
	/// Sampled walks of the model, each done to fresh real hosts step by step; after every step a host must agree with the model on its
	/// state and on what it sent. A divergence fails with the step, model and real side by side, and a walk CsCheck has shrunk.
	/// </summary>
	public Task<SystemConformanceReport> ConformAsync(HostBinding host, int minSteps = 1, int maxSteps = 24, string? seed = null, long iter = -1) =>
		ConformAsync([host], minSteps, maxSteps, seed, iter);

	/// <inheritdoc cref="ConformAsync(HostBinding, int, int, string, long)"/>
	public async Task<SystemConformanceReport> ConformAsync(
		IReadOnlyList<HostBinding> hosts,
		int minSteps = 1,
		int maxSteps = 24,
		string? seed = null,
		long iter = -1
	)
	{
		var (spec, model) = Build(null);
		Validate(model, hosts);
		var walks = 0;
		await SampleCall.RunAsync(
			() => spec.Walks(minSteps, maxSteps).SampleAsync(
				async steps =>
				{
					Interlocked.Increment(ref walks);
					if (await RunAsync(spec, model, hosts, steps).ConfigureAwait(false) is { } problem)
					{
						throw new InvalidOperationException(problem);
					}
				},
				seed: seed,
				iter: iter,
				print: steps => string.Join(" → ", steps.Select(s => s.After.Last ?? s.Action))
			)
		).ConfigureAwait(false);
		return new SystemConformanceReport(walks, "walks");
	}

	/// <summary>
	/// Every transition of the model's reachable state space, each done to fresh real <paramref name="hosts"/> down a shortest path, so a divergence is
	/// reported with the fewest steps that reach it. Needs the space to close within <paramref name="maxStates"/>. A step that changes nothing in the model is
	/// not a transition and is not checked here; the sampled <see cref="ConformAsync(IReadOnlyList{HostBinding}, int, int, string, long)"/> runs those too.
	/// </summary>
	public async Task<SystemConformanceReport> ConformExhaustiveAsync(IReadOnlyList<HostBinding> hosts, int maxStates = 100_000)
	{
		var (spec, model) = Build(null);
		Validate(model, hosts);
		var count = 0;
		foreach (var path in spec.TransitionPaths(maxStates))
		{
			count++;
			if (await RunAsync(spec, model, hosts, path).ConfigureAwait(false) is { } problem)
			{
				throw new CsCheckException(problem);
			}
		}
		return new SystemConformanceReport(count, "transitions");
	}

	/// <inheritdoc cref="ConformExhaustiveAsync(IReadOnlyList{HostBinding}, int)"/>
	public Task<SystemConformanceReport> ConformExhaustiveAsync(HostBinding host, int maxStates = 100_000) => ConformExhaustiveAsync([host], maxStates);

	private void Validate(SystemModel model, IReadOnlyList<HostBinding> hosts)
	{
		foreach (var host in hosts)
		{
			var node = model.Nodes.FirstOrDefault(n => n.Name == host.Node.Name) ??
				throw new InvalidOperationException($"The host for '{host.Node.Name}' plays a node this system does not have.");
			if (node.Clients.Count > 0 && host.Client is null)
			{
				throw new InvalidOperationException(
					$"'{node.Name}' takes requests ({string.Join(", ", node.Clients.Select(c => c.Name))}) and its host has no client: pass client: (host, name, arg) => ...."
				);
			}
			if (_durable.ContainsKey(node.Name) && host.Restart is null)
			{
				throw new InvalidOperationException($"'{node.Name}' may crash and its host has no restart: pass restart: host => ....");
			}
			if (_crashPoints.TryGetValue(node.Name, out var points) && points.Length > 0 && host.Arm is null)
			{
				throw new InvalidOperationException(
					$"'{node.Name}' may crash at {string.Join(", ", points.Select(p => p.Name))} and its host cannot arm a crash point: pass crash: (host, point) => host.Crashes.Arm(point)."
				);
			}
		}
	}

	private static async Task<string?> RunAsync(
		EntitySpec<SystemState> spec,
		SystemModel model,
		IReadOnlyList<HostBinding> hosts,
		IReadOnlyList<EntityStep<SystemState>> steps
	)
	{
		var byName = hosts.ToDictionary(h => h.Node.Name, StringComparer.Ordinal);
		var suts = hosts.ToDictionary(h => h.Node.Name, h => h.Create(), StringComparer.Ordinal);
		var initial = steps.Count > 0 ? steps[0].Before : null;
		if (initial is not null && Disagree(model, byName, suts, initial, [], -1) is { } atStart)
		{
			return Report(spec, steps, -1, atStart);
		}

		for (var i = 0; i < steps.Count; i++)
		{
			var step = steps[i];
			var kind = model.Steps[step.Action];
			// A network or crash step the model refused (out of budget, nothing in that slot) did nothing, so there is nothing to do to the hosts.
			if (ReferenceEquals(step.Before, step.After) && kind is not SystemStep.ClientCall)
			{
				continue;
			}
			Outcome outcome;
			try
			{
				outcome = await DoAsync(model, kind, step, byName, suts).ConfigureAwait(false);
			}
			catch (Exception e) when (e is not OperationCanceledException)
			{
				outcome = new Outcome($"the host threw {e.GetType().Name}: {e.Message}");
			}
			var problem = outcome.Problem ?? Disagree(
				model,
				byName,
				suts,
				step.After,
				kind is SystemStep.ClientCall or SystemStep.Deliver or SystemStep.CrashAt ? Sent(step, outcome.Removed) : [],
				i,
				outcome.SentBeforeCrash
			);
			if (problem is not null)
			{
				return Report(spec, steps, i, problem);
			}
		}
		return null;
	}

	/// <summary>What doing one step to the hosts gave: a problem, the message the step consumed, and what a host that crashed had sent before it did.</summary>
	private sealed record Outcome(string? Problem, Envelope? Removed = null, (string Node, List<object> Sent)? SentBeforeCrash = null);

	private static async Task<Outcome> DoAsync(
		SystemModel model,
		SystemStep kind,
		EntityStep<SystemState> step,
		Dictionary<string, HostBinding> hosts,
		Dictionary<string, object> suts
	)
	{
		switch (kind)
		{
			case SystemStep.ClientCall call:
				if (hosts.TryGetValue(call.Node.Name, out var caller))
				{
					await caller.Client!(suts[call.Node.Name], call.Client.Name, step.ArgumentValue).ConfigureAwait(false);
				}
				return new Outcome(null);

			case SystemStep.Deliver:
				{
					var envelope = model.Deliverable(step.Before)[(int)step.ArgumentValue!];
					if (hosts.TryGetValue(envelope.To, out var receiver))
					{
						try
						{
							await receiver.Deliver(suts[envelope.To], envelope.Message).ConfigureAwait(false);
						}
						catch (SimulatedCrashException e)
						{
							return new Outcome($"{envelope.To} crashed at '{e.Point}' handling {envelope}, but the model did not crash it there", envelope);
						}
					}
					return new Outcome(null, envelope);
				}

			case SystemStep.Crash crash:
				if (hosts.TryGetValue(crash.Node.Name, out var crashed))
				{
					suts[crash.Node.Name] = crashed.Restart!(suts[crash.Node.Name]);
				}
				return new Outcome(null);

			case SystemStep.CrashAt at:
				{
					var envelope = model.CrashTargets(step.Before, at.Node, at.Point)[(int)step.ArgumentValue!];
					if (!hosts.TryGetValue(at.Node.Name, out var host))
					{
						return new Outcome(null);
					}
					var sut = suts[at.Node.Name];
					host.Arm!(sut, at.Point.Name);
					try
					{
						await host.Deliver(sut, envelope.Message).ConfigureAwait(false);
						return new Outcome($"the model crashes {at.Node.Name} at '{at.Point.Name}' handling {envelope}, but the code never reached that point");
					}
					catch (SimulatedCrashException e) when (e.Point == at.Point.Name)
					{
						// what the host sent before it died stays sent: read it before the restart replaces the host
						var before = host.Sent?.Invoke(sut).ToList();
						suts[at.Node.Name] = host.Restart!(sut);
						return new Outcome(null, null, before is null ? null : (at.Node.Name, before));
					}
				}

			default:
				return new Outcome(null);
		}
	}

	/// <summary>
	/// What the model's handler sent in this step: what is in flight after it and was not before (less the message it consumed). Only a step where a node
	/// acts (a request, a delivery, a crash inside a handler) sends anything; a duplicate is the network's doing.
	/// </summary>
	private static List<Envelope> Sent(EntityStep<SystemState> step, Envelope? removed)
	{
		var remaining = step.Before.InFlight.Select(StructuralKey.Of).ToList();
		if (removed is not null)
		{
			remaining.Remove(StructuralKey.Of(removed));
		}
		var sent = new List<Envelope>();
		foreach (var envelope in step.After.InFlight)
		{
			if (!remaining.Remove(StructuralKey.Of(envelope)))
			{
				sent.Add(envelope);
			}
		}
		return sent;
	}

	private static string? Disagree(
		SystemModel model,
		Dictionary<string, HostBinding> hosts,
		Dictionary<string, object> suts,
		SystemState expected,
		List<Envelope> modelSent,
		int step,
		(string Node, List<object> Sent)? sentBeforeCrash = null
	)
	{
		foreach (var (name, host) in hosts)
		{
			var sut = suts[name];
			var node = model.Nodes.Single(n => n.Name == name);
			var real = host.StateOf(sut);
			var wanted = expected.StateOf(node);
			if (!Equals(StructuralKey.Of(real), StructuralKey.Of(wanted)))
			{
				return $"{name} holds {ValuePrinter.Print(real)}, the model says {ValuePrinter.Print(wanted)}";
			}
			if (host.Sent is null)
			{
				continue;
			}
			var sent = host.Sent(sut).ToList();
			if (sentBeforeCrash is { } crashed && crashed.Node == name)
			{
				sent.InsertRange(0, crashed.Sent);
			}
			if (step < 0)
			{
				continue;
			}
			var expectedSent = modelSent.Where(e => e.From == name).Select(e => e.Message).ToList();
			if (!SameMessages(sent, expectedSent))
			{
				return $"{name} sent [{string.Join(", ", sent.Select(ValuePrinter.Print))}], the model sends [{string.Join(", ", expectedSent.Select(ValuePrinter.Print))}]";
			}
		}
		return null;
	}

	private static bool SameMessages(List<object> real, List<object> model)
	{
		var left = model.Select(StructuralKey.Of).ToList();
		foreach (var message in real)
		{
			if (!left.Remove(StructuralKey.Of(message)))
			{
				return false;
			}
		}
		return left.Count == 0;
	}

	private static string Report(EntitySpec<SystemState> spec, IReadOnlyList<EntityStep<SystemState>> steps, int at, string problem)
	{
		var text = new StringBuilder();
		text.AppendLine("Conformance failed.");
		text.AppendLine();
		text.AppendLine(at < 0 ? "Step          the start, before anything happened" : $"Step          {at + 1}: {steps[at].After.Last ?? steps[at].Action}");
		text.AppendLine(CultureInfo.InvariantCulture, $"Diverged      {problem}");
		if (steps.Count > 0)
		{
			text.AppendLine();
			text.AppendLine(CultureInfo.InvariantCulture, $"Path          {spec.PrintState(steps[0].Before)}");
			for (var i = 0; i <= at; i++)
			{
				text.AppendLine(CultureInfo.InvariantCulture, $"{(i == at ? "          >> " : "             ")}{i + 1,2} {spec.PrintState(steps[i].After)}");
			}
		}
		return text.ToString();
	}
}
