using System.Reflection;
using AwesomeAssertions;

namespace Nullean.Entities.Tests;

internal sealed record Basket(IReadOnlyList<string> Items, int Version, int Total)
{
	public Basket Reverse() => this with { Items = [.. Items.Reverse()] };
}

internal sealed record Wrapper(Basket Basket, string Label);

public class AxisShapeTests
{
	private static readonly Basket Pair = new(["a", "b"], 1, 3);

	// ── every declared factory says which axis it is on ──────────────────────────

	[Test]
	public void Idempotent_is_on_the_idempotency_axis_and_keeps_its_action()
	{
		var rule = Constraints<int>.Idempotent(Math.Abs);

		rule.Axis.Should().Be(Axis.Idempotency);
		var shape = rule.Shape.Should().BeOfType<IdempotentShape<int>>().Subject;
		shape.Factory.Should().Be("Idempotent");
		shape.Action!(-3).Should().Be(3);
	}

	[Test]
	public void Monotonic_is_on_the_monotonicity_axis_and_probes_whether_the_value_moved()
	{
		var rule = Constraints<Basket>.Monotonic(b => b.Version, MonotonicDirection.NonDecreasing);

		rule.Axis.Should().Be(Axis.Monotonicity);
		var shape = rule.Shape.Should().BeOfType<MonotonicShape<Basket>>().Subject;
		shape.PerKey.Should().BeFalse();
		shape.Probe(Pair, Pair).Should().Be(new ShapeProbe(1, 0));
		shape.Probe(Pair, Pair with { Version = 2 }).Should().Be(new ShapeProbe(1, 1));
	}

	[Test]
	public void MonotonicPerKey_counts_the_keys_it_compared_and_the_ones_that_moved()
	{
		var rule = Constraints<IReadOnlyDictionary<string, int>>.MonotonicPerKey(
			d => d,
			kv => kv.Key,
			kv => kv.Value,
			MonotonicDirection.NonDecreasing
		);
		IReadOnlyDictionary<string, int> before = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
		IReadOnlyDictionary<string, int> after = new Dictionary<string, int> { ["a"] = 1, ["b"] = 3, ["c"] = 9 };

		rule.Axis.Should().Be(Axis.Monotonicity);
		var shape = rule.Shape.Should().BeOfType<MonotonicShape<IReadOnlyDictionary<string, int>>>().Subject;
		shape.PerKey.Should().BeTrue();
		shape.Probe(before, after).Should().Be(new ShapeProbe(2, 1));
	}

	[Test]
	public void Conserves_is_on_the_conservation_axis()
	{
		var rule = Constraints<Basket>.Conserves(b => b.Total);

		rule.Axis.Should().Be(Axis.Conservation);
		rule.Shape.Should().BeOfType<ConservesShape<Basket>>().Which.Select(Pair).Should().Be(3);
	}

	[Test]
	public void Transitions_over_a_selector_is_an_invariant_and_still_exposes_its_edges()
	{
		var rule = Constraints<Basket>.Transitions(b => b.Version).Allow(1, 2).Allow(2, 3);

		((Constraint<Basket>)rule).Axis.Should().Be(Axis.Invariant);
		var constraint = (Constraint<Basket>)rule;
		constraint.Shape.Should().BeOfType<TransitionsShape<Basket>>();
		constraint.StateOf!(Pair).Should().Be("1");
		constraint.Edges.Should().Equal(("1", "2"), ("2", "3"));
	}

	[Test]
	public void A_closure_with_nothing_declared_is_opaque_and_names_the_factory_that_built_it()
	{
		var invariant = Constraints<Basket>.Always(b => b.Total >= 0, "A basket is never negative.");
		var sequential = Constraints<Basket>.CheckTransition((_, _) => []);

		invariant.Axis.Should().Be(Axis.Invariant);
		invariant.Shape.Factory.Should().Be("Always");
		sequential.Axis.Should().Be(Axis.Unclassified);
		sequential.Shape.Factory.Should().Be("CheckTransition");
	}

	[Test]
	public void A_rule_that_carries_evidence_names_its_factory_too()
	{
		var rule = Constraints<Basket>.Unique(b => b.Items, item => item);

		rule.Axis.Should().Be(Axis.Invariant);
		rule.Shape.Factory.Should().Be("Unique");
	}

	// ── OrderIndependent: the new declared shape for confluence ──────────────────

	[Test]
	public void OrderIndependent_holds_when_the_result_ignores_the_order_of_the_items()
	{
		var rule = Constraints<Basket>.OrderIndependent(
			b => b.Items,
			(b, items) => b with { Items = items },
			b => string.Join(",", b.Items.Order()));

		ConstraintSet.From(("order-independent", rule)).Validate(Pair).Should().BeEmpty();
	}

	[Test]
	public void OrderIndependent_reports_a_result_that_depends_on_which_item_comes_first()
	{
		var rule = Constraints<Basket>.OrderIndependent(b => b.Items, (b, items) => b with { Items = items }, b => b.Items[0]);

		var violations = ConstraintSet.From(("order-independent", rule)).Validate(Pair);

		violations.Should().ContainSingle().Which.Message.Should().Contain("depends on the order");
	}

	[Test]
	public void OrderIndependent_has_nothing_to_compare_below_two_items()
	{
		var rule = Constraints<Basket>.OrderIndependent(b => b.Items, (b, items) => b with { Items = items }, b => b.Items.Count > 0 ? b.Items[0] : null);

		ConstraintSet.From(("order-independent", rule)).Validate(Pair with { Items = ["only"] }).Should().BeEmpty();
	}

	[Test]
	public void OrderIndependent_shape_rebuilds_the_value_in_the_requested_order()
	{
		var rule = Constraints<Basket>.OrderIndependent(b => b.Items, (b, items) => b with { Items = items }, b => b.Total);

		rule.Axis.Should().Be(Axis.Confluence);
		var shape = rule.Shape.Should().BeOfType<OrderIndependentShape<Basket>>().Subject;
		shape.Count(Pair).Should().Be(2);
		shape.Permute!(Pair, [1, 0]).Items.Should().Equal("b", "a");
		shape.SameResult(Pair, Pair.Reverse()).Should().BeTrue();
	}

	// ── a rule keeps its shape through the operations that copy it ───────────────

	[Test]
	public void A_rule_copied_into_a_set_with_another_severity_keeps_its_axis()
	{
		var set = ConstraintSet.From(("keeps-total", Constraints<Basket>.Conserves(b => b.Total))).WithSeverity("keeps-total", Severity.Warning);

		var rule = set.AsConstraints().Single();

		rule.Axis.Should().Be(Axis.Conservation);
		rule.Shape.Factory.Should().Be("Conserves");
	}

	[Test]
	public void Mapping_a_rule_composes_what_the_shape_reads()
	{
		var rule = Constraints<Basket>.Monotonic(b => b.Version, MonotonicDirection.NonDecreasing);
		var mapped = rule.Map<Wrapper>(w => w.Basket);

		var shape = mapped.Shape.Should().BeOfType<MonotonicShape<Wrapper>>().Subject;
		shape.Probe(new Wrapper(Pair, "x"), new Wrapper(Pair with { Version = 5 }, "x")).Should().Be(new ShapeProbe(1, 1));
		mapped.Axis.Should().Be(Axis.Monotonicity);
	}

	[Test]
	public void Mapping_an_idempotent_rule_without_a_way_back_drops_the_action_and_with_one_keeps_it()
	{
		var rule = Constraints<Basket>.Idempotent(b => b with { Total = Math.Abs(b.Total) });

		rule.Map<Wrapper>(w => w.Basket).Shape.Should().BeOfType<IdempotentShape<Wrapper>>().Which.Action.Should().BeNull();

		var lifted = rule.Map<Wrapper>(w => w.Basket, (w, basket) => w with { Basket = basket });
		var action = lifted.Shape.Should().BeOfType<IdempotentShape<Wrapper>>().Subject.Action;
		action.Should().NotBeNull();
		action(new Wrapper(Pair with { Total = -4 }, "x")).Basket.Total.Should().Be(4);
	}

	[Test]
	public void Mapping_a_transition_rule_composes_its_state_selector()
	{
		var rule = (Constraint<Basket>)Constraints<Basket>.Transitions(b => b.Version).Allow(1, 2);

		var mapped = rule.Map<Wrapper>(w => w.Basket);

		mapped.StateOf!(new Wrapper(Pair, "x")).Should().Be("1");
		mapped.Edges.Should().Equal(("1", "2"));
	}

	// ── OfAxis ───────────────────────────────────────────────────────────────────

	[Test]
	public void OfAxis_selects_the_rules_on_one_axis_and_keeps_prerequisites()
	{
		var present = Constraints<Basket>.Always(b => b.Items is not null, "The items are required.").AsPrerequisite();
		var set = ConstraintSet.From(
			("items-required", present),
			("version-grows", Constraints<Basket>.Monotonic(b => b.Version, MonotonicDirection.NonDecreasing)),
			("total-kept", Constraints<Basket>.Conserves(b => b.Total)),
			("idempotent", Constraints<Basket>.Idempotent(b => b))
		);

		Names(set.OfAxis(Axis.Monotonicity)).Should().Equal("items-required", "version-grows");
		Names(set.OfAxis(Axis.Monotonicity, includePrerequisites: false)).Should().Equal("version-grows");
		Names(set.OfAxis(Axis.Conservation)).Should().Equal("items-required", "total-kept");
		Names(set.OfAxis(Axis.Confluence, includePrerequisites: false)).Should().BeEmpty();
	}

	[Test]
	public void OfAxis_hands_back_the_same_rule_objects()
	{
		var rule = Constraints<Basket>.Conserves(b => b.Total);
		var set = ConstraintSet.From(("total-kept", rule));

		set.OfAxis(Axis.Conservation).AsConstraints().Single().Should().BeSameAs(set.AsConstraints().Single());
	}

	// ── the drift guard: a new factory must be classified ────────────────────────

	/// <summary>Every public factory on <c>Constraints&lt;T&gt;</c> and the axis a rule it builds is on. A new factory fails the test below until it is listed here.</summary>
	private static readonly Dictionary<string, Axis> Factories = new()
	{
		["Acyclic"] = Axis.Invariant,
		["AllOrNone"] = Axis.Invariant,
		["Always"] = Axis.Invariant,
		["AtMostOne"] = Axis.Invariant,
		["Check"] = Axis.Invariant,
		["CheckTransition"] = Axis.Unclassified,
		["Conserves"] = Axis.Conservation,
		["Consistent"] = Axis.Invariant,
		["CountBetween"] = Axis.Invariant,
		["Disjoint"] = Axis.Invariant,
		["Each"] = Axis.Invariant,
		["EachCheck"] = Axis.Invariant,
		["EachOneOf"] = Axis.Invariant,
		["EachUsed"] = Axis.Invariant,
		["ExactlyOne"] = Axis.Invariant,
		["Idempotent"] = Axis.Idempotency,
		["InOrder"] = Axis.Invariant,
		["KeysMatch"] = Axis.Invariant,
		["Matches"] = Axis.Invariant,
		["Monotonic"] = Axis.Monotonicity,
		["MonotonicPerKey"] = Axis.Monotonicity,
		["NoChains"] = Axis.Invariant,
		["NoneOf"] = Axis.Invariant,
		["NoPrefixes"] = Axis.Invariant,
		["NotInFuture"] = Axis.Invariant,
		["NotOlderThan"] = Axis.Invariant,
		["NoUnknownKeys"] = Axis.Invariant,
		["OneOf"] = Axis.Invariant,
		["OrderIndependent"] = Axis.Confluence,
		["ParsesAsEnum"] = Axis.Invariant,
		["RatioAtMost"] = Axis.Invariant,
		["References"] = Axis.Invariant,
		["Sorted"] = Axis.Invariant,
		["Transitions"] = Axis.Invariant,
		["Unique"] = Axis.Invariant,
		["When"] = Axis.Invariant
	};

	[Test]
	public void Every_public_factory_is_classified_on_an_axis()
	{
		var factories = typeof(Constraints<>)
			.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
			.Where(m => BuildsARule(m.ReturnType))
			.Select(m => m.Name)
			.Distinct()
			.Order()
			.ToArray();

		factories.Should().Equal(
			Factories.Keys.Order(),
			"a factory was added or removed: classify it in AxisShapeTests.Factories (and give it a shape if it is more than an invariant)"
		);
	}

	[Test]
	public void The_axis_a_factory_builds_matches_the_table()
	{
		var built = new Dictionary<string, Axis>
		{
			["Always"] = Constraints<Basket>.Always(b => b.Total >= 0, "x").Axis,
			["Check"] = Constraints<Basket>.Check(_ => []).Axis,
			["CheckTransition"] = Constraints<Basket>.CheckTransition((_, _) => []).Axis,
			["Conserves"] = Constraints<Basket>.Conserves(b => b.Total).Axis,
			["Idempotent"] = Constraints<Basket>.Idempotent(b => b).Axis,
			["Monotonic"] = Constraints<Basket>.Monotonic(b => b.Version, MonotonicDirection.NonDecreasing).Axis,
			["OrderIndependent"] = Constraints<Basket>.OrderIndependent(b => b.Items, (b, i) => b with { Items = i }, b => b.Total).Axis,
			["Transitions"] = ((Constraint<Basket>)Constraints<Basket>.Transitions(b => b.Version).Allow(1, 2)).Axis,
			["Unique"] = Constraints<Basket>.Unique(b => b.Items, i => i).Axis,
			["When"] = Constraints<Basket>.When(b => b.Total > 0).Requires(b => b.Version > 0).Axis
		};

		foreach (var (factory, axis) in built)
		{
			axis.Should().Be(Factories[factory], $"{factory} is classified as {Factories[factory]}");
		}
	}

	// A factory returns a rule (Constraint<T>, Constraint<T, TEvidence>) or a builder that converts to one; helpers that return anything else are not factories.
	private static bool BuildsARule(Type returnType) =>
		returnType.IsGenericType && returnType.Name is "Constraint`1" or "Constraint`2" or "WhenBuilder`1" or "TransitionBuilder`2";

	private static string[] Names(ConstraintSet<Basket> set) => [.. set.AsConstraints().Select(c => c.Name!)];
}
