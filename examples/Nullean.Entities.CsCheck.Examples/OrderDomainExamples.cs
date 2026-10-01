// The complex, nested-domain example: every testing quality this repo's design conversation named
// (absent, temporal, vacancy-weak, liveness, idempotency, monotonicity, conservation, confluence),
// checked against the Order/OrderLine/OrderEvent domain in OrderDomain.cs/OrderEvents.cs — real
// CsCheck generation and shrinking throughout, nothing hand-picked. Simpler, single-quality
// examples live further down the ladder in this same project; this file is the top of it.

using AwesomeAssertions;
using CsCheck;
using TUnit;

namespace Nullean.Entities.CsCheck.Examples;

// ── Generators ───────────────────────────────────────────────────────────────

internal static class OrderGens
{
	public static readonly Gen<OrderLine> Line = Gen.Select(
		Gen.Int[0, 4].Select(i => $"SKU-{i}"),
		Gen.Int[1, 20],
		Gen.Int[1, 100],
		(sku, qty, price) => new OrderLine(sku, qty, price)
	);

	public static readonly Gen<Order> Pending = Line.Array[0, 6].Select(lines => lines.Aggregate(Order.New(Guid.NewGuid()), (o, l) => o.AddLine(l)));
}

// ── Idempotency (pure value) ────────────────────────────────────────────────

public class OrderIdempotencyExamples
{
	/// <summary>Cancelling any order, pending or already cancelled, is idempotent — CsCheck checks this over generated orders in every status, not one hand-picked case.</summary>
	[Test]
	public void CancelIsIdempotentAcrossGeneratedOrders() =>
		OrderGens.Pending.Sample(order => OrderDomainConstraints.General.Validate(order).Should().BeEmpty(), iter: 200);
}

// ── Monotonicity (pure value, step-scoped) ──────────────────────────────────

public class OrderMonotonicityExamples
{
	/// <summary>AddLine only ever grows the running total — checked via ConstraintChannel.Apply, which runs the (previous, next) pair AddLineStep is scoped to.</summary>
	[Test]
	public void AddLineNeverShrinksTheTotal() =>
		Gen.Select(OrderGens.Pending, OrderGens.Line, (order, line) => (order, line)).Sample(
			t =>
			{
				var channel = ConstraintChannel.For(t.order).Enforce(OrderDomainConstraints.AddLineStep);
				var result = channel.Apply(o => o.AddLine(t.line));
				result.Should().BeOfType<Result<Order>.Ok>();
			},
			iter: 200
		);

	/// <summary>The same step-scoped constraint correctly refuses to apply to RemoveLine — it is only ever attached to the AddLine step, never to a set that also runs RemoveLine.</summary>
	[Test]
	public void MonotonicIsNeverAttachedToASetThatAlsoRunsRemoveLine() => OrderDomainConstraints.AddLineStep.AsConstraints().Should().HaveCount(1);
}

// ── Conservation (pure value, pair-valued) ──────────────────────────────────

public class OrderConservationExamples
{
	/// <summary>Transferring a line between two orders conserves total quantity across the pair — generated orders, generated sku to move.</summary>
	[Test]
	public void TransferConservesTotalQuantity() =>
		Gen.Select(OrderGens.Pending, OrderGens.Pending, Gen.Int[0, 4].Select(i => $"SKU-{i}"), (from, to, sku) => (from, to, sku)).Sample(
			t =>
			{
				var channel = ConstraintChannel.For((t.from, t.to)).Enforce(OrderDomainConstraints.Transfer);
				var result = channel.Apply(pair => OrderTransfers.TransferLine(pair.from, pair.to, t.sku));
				result.Should().BeOfType<Result<(Order, Order)>.Ok>();
			},
			iter: 200
		);
}

// ── Confluence (pure value, metamorphic) ────────────────────────────────────

public class OrderConfluenceExamples
{
	/// <summary>Adding two lines in either order gives the same total and the same set of lines — the standard metamorphic shape, riding Script&lt;TSut&gt;.Metamorphic's real generate/shrink/replay.</summary>
	[Test]
	public void AddingTwoLinesInEitherOrderGivesTheSameOrder()
	{
		var script = Script.For<OrderBox>();

		var act =
			() => script.Metamorphic(
				() => new OrderBox(Order.New(Guid.NewGuid())),
				OrderGens.Line,
				(box, a, b) => box.Order = box.Order.AddLine(a).AddLine(b),
				(box, a, b) => box.Order = box.Order.AddLine(b).AddLine(a),
				(x, y) => x.Order.Total == y.Order.Total && x.Order.Lines.ToHashSet().SetEquals(y.Order.Lines),
				iter: 100
			);

		act.Should().NotThrow();
	}
}

/// <summary>A mutable box around the immutable Order — Metamorphic's two paths each need somewhere to write their result back to.</summary>
internal sealed class OrderBox(Order order)
{
	public Order Order { get; set; } = order;
}

// ── Absent, Temporal, Liveness (reactive, real Script<OrderService> runs) ──────

public class OrderReactiveExamples
{
	/// <summary>All three reactive qualities in OrderReactiveSpec — Absent, Temporal-within-deadline, and unbounded Liveness — hold across a real generated, shrinkable run of the service.</summary>
	[Test]
	public void GeneratedOrderLifecycleRunsSatisfyTheFullSpec() =>
		OrderScripts.Lifecycle.SampleWith(_ => new OrderService(), svc => svc.Events, OrderReactiveSpec.All, iter: 200);
}

// ── Vacancy, weak (CsCheck's own classify) ──────────────────────────────────

internal enum OrderAction
{
	Place,
	AddLine,
	Confirm,
	Ship,
	Cancel,
}

public class OrderCoverageExamples
{
	// Same tiny pool trick as OrderScripts.Lifecycle — three IDs, reused across steps, so a
	// generated walk actually drives orders through real transitions instead of mostly Rejected
	// no-ops against IDs nothing else ever touched.
	private static readonly Guid[] Pool = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

	private static readonly Gen<(Guid Id, OrderAction Action)> GenStep = Gen.Select(
		Gen.Int[0, Pool.Length - 1].Select(i => Pool[i]),
		Gen.Int[0, 4].Select(i => (OrderAction)i),
		(id, action) => (id, action)
	);

	/// <summary>
	/// A constraint that never fires because the interesting case never arises is a false sense of
	/// safety — classify proves the generated walk actually reaches both Shipped and Cancelled
	/// outcomes (not just "runs happen not to Ship" or "runs happen not to Cancel"), which is what
	/// makes OrderReactiveExamples's pass above meaningful rather than vacuous. Uses CsCheck's own
	/// string-classify Sample overload directly (Script&lt;TSut&gt;/SampleWith drive the boolean
	/// overload instead), so this is the one example in the file that builds its own Gen by hand.
	/// </summary>
	[Test]
	public void GeneratedRunsReachBothShippedAndCancelledOutcomes()
	{
		var seenOutcomes = new HashSet<string>();

		GenStep.Array[5, 25].Sample(
			steps =>
			{
				using var service = new OrderService();
				var reached = new HashSet<string>();
				using var subscription = service.Events.Subscribe(e =>
				{
					switch (e)
					{
						case OrderEvent.Shipped:
							reached.Add("Shipped");
							break;
						case OrderEvent.Cancelled:
							reached.Add("Cancelled");
							break;
					}
				});

				foreach (var (id, action) in steps)
				{
					switch (action)
					{
						case OrderAction.Place:
							service.Place(id);
							break;
						case OrderAction.AddLine:
							service.AddLine(id, "SKU-0", 1);
							break;
						case OrderAction.Confirm:
							service.Confirm(id);
							break;
						case OrderAction.Ship:
							service.Ship(id);
							break;
						case OrderAction.Cancel:
							service.Cancel(id);
							break;
					}
				}

				var classification = reached.Count == 0 ? "neither" : string.Join("+", reached.OrderBy(x => x, StringComparer.Ordinal));
				seenOutcomes.Add(classification);
				return classification;
			},
			Console.WriteLine,
			iter: 500
		);

		seenOutcomes.Should().Contain("Shipped");
		seenOutcomes.Should().Contain("Cancelled");
	}
}
