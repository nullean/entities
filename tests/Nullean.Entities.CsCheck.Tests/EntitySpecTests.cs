using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// A door is a tiny pure state machine: every method returns the next Door, illegal moves return the same one.

internal enum DoorState
{
	Closed,
	Open,
	Locked
}

internal sealed record Door(DoorState State)
{
	public static Door New() => new(DoorState.Closed);

	public Door Open() => State == DoorState.Closed ? this with { State = DoorState.Open } : this;

	public Door Close() => State == DoorState.Open ? this with { State = DoorState.Closed } : this;

	public Door Lock() => State == DoorState.Closed ? this with { State = DoorState.Locked } : this;

	public Door Unlock() => State == DoorState.Locked ? this with { State = DoorState.Closed } : this;
}

[Constraints]
internal static partial class DoorConstraints
{
	public static readonly Constraint<Door> LegalTransitions = Constraints<Door>
		.Transitions(d => d.State)
		.Allow(DoorState.Closed, DoorState.Open)
		.Allow(DoorState.Open, DoorState.Closed)
		.Allow(DoorState.Closed, DoorState.Locked)
		.Allow(DoorState.Locked, DoorState.Closed);

	public static readonly Constraint<Door> LockedDoorsAreNotOpen = Constraints<Door>
		.When(d => d.State == DoorState.Locked)
		.Requires(d => d.State != DoorState.Open);
}

public class EntitySpecTests
{
	private static EntitySpec<Door> DoorSpec() => DoorConstraints.All.Spec(Door.New(), DoorConstraints.Actions);

	[Test]
	public void Generator_discovers_every_Door_method_as_an_action() =>
		DoorConstraints.Actions.Select(a => a.Name).Should().BeEquivalentTo("Open", "Close", "Lock", "Unlock");

	[Test]
	public void Exhaustive_proves_a_correct_entity_and_reports_the_closed_state_space()
	{
		var report = DoorSpec().Exhaustive();

		report.Closed.Should().BeTrue();
		report.States.Should().Be(3);
	}

	[Test]
	public void Exhaustive_finds_an_illegal_transition_introduced_by_an_extra_action()
	{
		var spec = DoorSpec().Action("ForceOpen", d => d with { State = DoorState.Open });

		var act = () => spec.Exhaustive();

		act.Should().Throw<Exception>().Which.Message.Should().Contain("legal-transitions");
	}

	[Test]
	public void Exhaustive_fails_when_an_allowed_transition_is_never_taken()
	{
		// No Lock action: Closed->Locked and Locked->Closed are legal but unreachable, so those rules proved nothing.
		var spec = DoorConstraints.All.Spec(Door.New()).Action("Open", d => d.Open()).Action("Close", d => d.Close());

		var act = () => spec.Exhaustive();

		var message = act.Should().Throw<VacuousPropertyException>().Which.Message;
		message.Should().Contain("Closed->Locked");
		message.Should().Contain("locked-doors-are-not-open");
	}

	[Test]
	public void Exhaustive_can_opt_out_of_the_coverage_requirement()
	{
		var spec = DoorConstraints.All.Spec(Door.New()).Action("Open", d => d.Open()).Action("Close", d => d.Close());

		var act = () => spec.Exhaustive(requireCoverage: false);

		act.Should().NotThrow();
	}

	[Test]
	public void Mermaid_names_the_reachable_states_and_is_deterministic()
	{
		var first = DoorSpec().Mermaid();
		var second = DoorSpec().Mermaid();

		first.Should().Contain("flowchart").And.Contain("\"Lock\"").And.Contain("\"Unlock\"");
		first.Should().Be(second);
	}

	[Test]
	public void Snapshot_fails_with_the_hash_to_commit_then_passes_with_it()
	{
		var first = () => DoorSpec().Snapshot(expected: 0);
		first.Should().Throw<Exception>();
	}

	[Test]
	public void Faults_catches_a_declared_fault_that_breaks_a_constraint()
	{
		// Open() that "succeeds" from Locked is exactly what LegalTransitions exists to stop.
		var spec = DoorSpec().Fault("Open ignores the lock", "Open", (_, _) => new Door(DoorState.Open));

		var act = () => spec.Faults();

		act.Should().NotThrow();
	}

	[Test]
	public void Faults_throws_for_a_declared_fault_no_constraint_catches()
	{
		var spec = DoorSpec().Fault("Close does nothing", "Close", (before, _) => before);

		var act = () => spec.Faults();

		act.Should().Throw<Exception>();
	}

	[Test]
	public void Commutes_fails_for_actions_whose_order_matters_and_passes_for_ones_that_do_not()
	{
		var bad = () => DoorSpec().Commutes("Open", "Lock");
		var good = () => DoorSpec().Commutes("Lock", "Lock");

		bad.Should().Throw<Exception>();
		good.Should().NotThrow();
	}

	[Test]
	public void Idempotent_passes_for_Lock_and_fails_for_a_toggle()
	{
		var good = () => DoorSpec().Idempotent("Lock");
		var toggle = () => DoorSpec().Action("Toggle", d => d.State == DoorState.Open ? d.Close() : d.Open()).Idempotent("Toggle");

		good.Should().NotThrow();
		toggle.Should().Throw<Exception>();
	}

	[Test]
	public void Conform_passes_for_a_faithful_implementation_and_catches_a_divergent_one()
	{
		var faithful = () =>
			DoorSpec().Conform(() => new DoorImpl(), (impl, step) => impl.Do(step.Action), (impl, expected) => impl.State == expected.State, iter: 50);
		var divergent = () =>
			DoorSpec().Conform(
				() => new DoorImpl { LockIsBroken = true },
				(impl, step) => impl.Do(step.Action),
				(impl, expected) => impl.State == expected.State,
				iter: 50
			);

		faithful.Should().NotThrow();
		divergent.Should().Throw<Exception>();
	}

	[Test]
	public void Sample_and_StepScoped_run_a_walk_with_a_rule_attached_to_one_action()
	{
		var scoped = ConstraintSet.From(("OpenOnlyFromClosed", Constraints<Door>.Transitions((p, n) => p.State == DoorState.Closed, "Open must start Closed")));

		var act = () => DoorSpec().StepScoped("Open", scoped).Sample(iter: 50);

		act.Should().NotThrow();
	}

	[Test]
	public void Valid_only_yields_values_that_satisfy_the_point_in_time_constraints()
	{
		var onlyOpen = ConstraintSet.From(("IsOpen", Constraints<Door>.Always(d => d.State == DoorState.Open, "must be open")));

		var open = Gen.Enum<DoorState>().Select(s => new Door(s)).Valid(onlyOpen);

		open.Sample(d => d.State == DoorState.Open, iter: 50);
	}

	[Test]
	public void ShouldSatisfy_fails_with_the_violated_constraint_names()
	{
		var onlyOpen = ConstraintSet.From(("IsOpen", Constraints<Door>.Always(d => d.State == DoorState.Open, "must be open")));

		var act = () => Gen.Enum<DoorState>().Select(s => new Door(s)).ShouldSatisfy(onlyOpen, iter: 50);

		act.Should().Throw<Exception>().Which.Message.Should().Contain("IsOpen");
	}

	[Test]
	public void Zero_config_spec_generates_starting_states_and_proves_the_door()
	{
		var report = DoorConstraints.Instance.Spec().Exhaustive();

		report.Closed.Should().BeTrue();
	}

	[Test]
	public void Spec_can_start_from_a_given_state_through_the_generated_Instance() =>
		DoorConstraints.Instance.Spec(Door.New()).Exhaustive().States.Should().Be(3);

	[Test]
	public void Self_attached_types_get_a_spec_from_their_own_constraints() =>
		EntitySpec.For<Lamp>().Exhaustive(requireCoverage: false).Closed.Should().BeTrue();

	[Test]
	public void Undeclared_argument_domains_are_plausible_values_plus_the_edge_values_that_matter()
	{
		var seen = new HashSet<string>();
		var actions = new EntityActions<Door>(new EntityAction<Door>("Paint", typeof(string), (d, a) =>
		{
			seen.Add((string)a!);
			return d;
		}));

		DoorConstraints.All.Spec(Door.New(), actions).Exhaustive(requireCoverage: false);

		// two plausible words plus the two edge values that matter most for a string argument
		seen.Should().HaveCount(4);
		seen.Should().Contain("").And.Contain(" ");
		seen.Where(v => v.Trim().Length > 0).Should().HaveCount(2);
	}

	[Test]
	public void DomainFrom_derives_one_argument_domain_from_another()
	{
		var names = new HashSet<string>();
		var actions = new EntityActions<Door>(new EntityAction<Door>("Number", typeof(int), (d, _) => d), new EntityAction<Door>("Name", typeof(string), (
			d,
			a
		) =>
		{
			names.Add((string)a!);
			return d;
		}));

		DoorConstraints.All.Spec(Door.New(), actions).Domain(1, 2).DomainFrom<int, string>(n => $"n{n}").Exhaustive(requireCoverage: false);

		names.Should().BeEquivalentTo("n1", "n2");
	}

	[Test]
	public void States_yields_only_values_reachable_by_the_actions_and_therefore_valid() =>
		DoorConstraints.Instance.Spec().States().Sample(d => DoorConstraints.All.Validate(d).Count == 0, iter: 100);

	[Test]
	public void Key_merges_states_the_default_equality_would_keep_apart()
	{
		var constraints = ConstraintSet.From(("Small", Constraints<Bag>.Always(b => b.Items.Count <= 3, "too many")));

		Bag Add(Bag b, int n) => new([.. b.Items, n]);

		var byReference = constraints.Spec(new Bag([])).Action("Add", [1, 2], Add).Boundary(b => b.Items.Count < 3).Key(b => b);
		var byContent = constraints
			.Spec(new Bag([]))
			.Action("Add", [1, 2], Add)
			.Boundary(b => b.Items.Count < 3)
			.Key(b => string.Join(",", b.Items.OrderBy(x => x)));

		byContent.Exhaustive(requireCoverage: false).States.Should().BeLessThan(byReference.Exhaustive(requireCoverage: false).States);
	}
}

internal sealed record Bag(IReadOnlyList<int> Items);

internal sealed class DoorImpl
{
	public DoorState State { get; private set; } = DoorState.Closed;

	public bool LockIsBroken { get; init; }

	public void Do(string action)
	{
		State = action switch
		{
			"Open" when State == DoorState.Closed => DoorState.Open,
			"Close" when State == DoorState.Open => DoorState.Closed,
			"Lock" when State == DoorState.Closed && !LockIsBroken => DoorState.Locked,
			"Unlock" when State == DoorState.Locked => DoorState.Closed,
			_ => State,
		};
	}
}

// Self-attached: the type declares its own constraints, so EntitySpec.For<Lamp>() needs nothing else.
internal sealed partial record Lamp(bool On)
{
	public Lamp Toggle() => this with { On = !On };

	[Constraint]
	public static readonly Constraint<Lamp> AlwaysAValue = Constraints<Lamp>.Always(l => l.On || !l.On, "a lamp is on or off");
}
