using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;

namespace Nullean.Entities.Tests;

// The type declares no Constraint<T> field at all: its DataAnnotations are the rules, turned into a generated set.
[Constraints]
public sealed partial record Signup(
	[Required] string Name,
	[property: StringLength(10, MinimumLength = 3)] string Handle,
	[Range(18, 120)] int Age,
	[Range(0.5, 5.0)] double? Rating,
	[RegularExpression("^[A-Z]{3}-[0-9]{3}$")] string Reference,
	[MaxLength(5)] string? Note,
	[property: ConstraintIgnore, Range(1, 2)] int Ignored = 1,
	[property: Required(AllowEmptyStrings = true, ErrorMessage = "{0} must be present, even if empty.")] string? Comment = ""
);

// Annotations and a hand-written rule together: one set.
[Constraints]
public sealed partial record Booking([Range(1, 10)] int Guests, DateOnly From, DateOnly To)
{
	public static readonly Constraint<Booking> EndsAfterItStarts = Constraints<Booking>.Always(b => b.To > b.From);
}

// A nested type: the generator re-declares Billing as partial around it.
public static partial class Billing
{
	[Constraints]
	public sealed partial record Line([Range(1, 10)] int Quantity)
	{
		public static readonly Constraint<Line> NotThirteen = Constraints<Line>.Always(l => l.Quantity != 13);
	}
}

// The entity is annotated; the companion class declares nothing of its own, and still gets the rules.
public sealed record Order([Required] string Number, [Range(1, 100)] int Quantity)
{
	public Order Ship() => this;
}

[Constraints<Order>]
public static partial class OrderRules;

// A plain [Constraints] companion that declares Constraint<Order> fields gets Order's annotations too.
[Constraints]
public static partial class OrderConstraints
{
	public static readonly Constraint<Order> NotThirteen = Constraints<Order>.Always(o => o.Quantity != 13);
}

// Generic types: the type parameters are carried into the generated declaration.
[Constraints]
public sealed partial record Entity<TId>([Required] string Name, [Range(1, 9)] int Level, TId Id)
{
	public static readonly Constraint<Entity<TId>> LevelNotFive = Constraints<Entity<TId>>.Always(e => e.Level != 5);
}

public sealed record Box<T>([Required] string Label, T Value);

// C# does not allow a type parameter in an attribute's type argument, so a generic companion names its type with a field and a closed one with the attribute.
[Constraints]
public static partial class BoxRules<TItem>
{
	public static readonly Constraint<Box<TItem>> HasValue = Constraints<Box<TItem>>.Always(b => b.Value is not null);
}

[Constraints<Box<int>>]
public static partial class IntBoxRules;

public class AnnotationConstraintsTests
{
	private static readonly Signup Good = new("Ada", "adal", 30, 4.5, "ABC-123", "hi");

	[Test]
	public void Each_supported_annotation_becomes_one_named_constraint_and_ConstraintIgnore_leaves_a_property_out()
	{
		var names = Signup.Constraints.AsConstraints().Select(c => c.Name).ToList();

		names.Should().BeEquivalentTo([
			"NameRequired",
			"HandleStringLength",
			"AgeRange",
			"RatingRange",
			"ReferencePattern",
			"NoteMaxLength",
			"CommentRequired"
		]);
	}

	[Test]
	public void A_value_that_satisfies_every_annotation_is_valid() => Signup.Constraints.Validate(Good).Should().BeEmpty();

	[Test]
	public void Nulls_and_empty_strings_pass_everything_except_Required()
	{
		var lenient = Good with { Rating = null, Note = null, Reference = "" };

		Signup.Constraints.Validate(lenient).Should().BeEmpty();
	}

	[Test]
	[Arguments("", "NameRequired", "Name")]
	[Arguments("   ", "NameRequired", "Name")]
	public void Required_rejects_blank_strings(string name, string constraint, string member)
	{
		var violation = Signup.Constraints.Validate(Good with { Name = name }).Single();

		violation.ConstraintId.Should().Be(constraint);
		violation.Member.Should().Be(member);
		violation.Message.Should().Be("The Name field is required.");
	}

	[Test]
	[Arguments(17)]
	[Arguments(121)]
	public void Range_rejects_values_outside_its_bounds_and_names_the_member(int age)
	{
		var violation = Signup.Constraints.Validate(Good with { Age = age }).Single();

		violation.ConstraintId.Should().Be("AgeRange");
		violation.Member.Should().Be("Age");
		violation.Evidence.Should().Be(age);
		Signup.Constraints.Validate(Good with { Age = 18 }).Should().BeEmpty();
		Signup.Constraints.Validate(Good with { Age = 120 }).Should().BeEmpty();
	}

	[Test]
	public void Range_works_on_nullable_and_floating_point_properties()
	{
		Signup.Constraints.Validate(Good with { Rating = 0.4 }).Single().ConstraintId.Should().Be("RatingRange");
		Signup.Constraints.Validate(Good with { Rating = 5.1 }).Single().ConstraintId.Should().Be("RatingRange");
		Signup.Constraints.Validate(Good with { Rating = 0.5 }).Should().BeEmpty();
		Signup.Constraints.Validate(Good with { Rating = double.NaN }).Single().ConstraintId.Should().Be("RatingRange");
	}

	[Test]
	[Arguments("ab")]
	[Arguments("abcdefghijk")]
	public void StringLength_enforces_both_bounds(string handle) =>
		Signup.Constraints.Validate(Good with { Handle = handle }).Single().ConstraintId.Should().Be("HandleStringLength");

	[Test]
	public void RegularExpression_must_match_the_whole_string()
	{
		Signup.Constraints.Validate(Good with { Reference = "ABC-1234" }).Single().ConstraintId.Should().Be("ReferencePattern");
		Signup.Constraints.Validate(Good with { Reference = "xABC-123" }).Single().ConstraintId.Should().Be("ReferencePattern");
		Signup.Constraints.Validate(Good with { Reference = "ABC-123" }).Should().BeEmpty();
	}

	[Test]
	public void MaxLength_and_a_custom_error_message_with_the_member_name_filled_in()
	{
		Signup.Constraints.Validate(Good with { Note = "toolong" }).Single().ConstraintId.Should().Be("NoteMaxLength");
		Signup.Constraints.Validate(Good with { Comment = null }).Single().Message.Should().Be("Comment must be present, even if empty.");
	}

	[Test]
	public void Annotations_and_hand_written_constraints_share_one_set()
	{
		var names = Booking.Constraints.AsConstraints().Select(c => c.Name).ToList();

		names.Should().BeEquivalentTo(["EndsAfterItStarts", "GuestsRange"]);
		Booking
			.Constraints
			.Validate(new Booking(11, new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 1)))
			.Select(v => v.ConstraintId)
			.Should()
			.BeEquivalentTo(["GuestsRange", "EndsAfterItStarts"]);
	}

	[Test]
	public void Apply_enforces_annotations_like_any_other_constraint()
	{
		var result = Signup.Constraints.Apply(Good, s => s with { Age = 5 });

		result.Should().BeOfType<Result<Signup>.Fail>().Which.Violations.Should().ContainSingle().Which.Member.Should().Be("Age");
	}

	[Test]
	public void A_nested_type_gets_its_constraints_too()
	{
		Billing.Line.Constraints.AsConstraints().Select(c => c.Name).Should().BeEquivalentTo(["NotThirteen", "QuantityRange"]);
		Billing.Line.Constraints.Validate(new Billing.Line(11)).Single().ConstraintId.Should().Be("QuantityRange");
	}

	[Test]
	public void A_Constraints_of_T_companion_with_no_members_gets_the_annotations_of_T()
	{
		OrderRules.All.AsConstraints().Select(c => c.Name).Should().BeEquivalentTo(["NumberRequired", "QuantityRange"]);
		OrderRules.All.Validate(new Order("A-1", 500)).Single().Member.Should().Be("Quantity");
		OrderRules.Instance.All.Should().BeSameAs(OrderRules.All);
		OrderRules.Instance.Actions!.Select(a => a.Name).Should().Contain("Ship");
	}

	[Test]
	public void A_plain_Constraints_companion_with_fields_also_picks_up_the_annotations_of_its_type()
	{
		OrderConstraints.All.AsConstraints().Select(c => c.Name).Should().BeEquivalentTo(["NotThirteen", "NumberRequired", "QuantityRange"]);
		OrderConstraints.All.Validate(new Order("", 13)).Select(v => v.ConstraintId).Should().BeEquivalentTo(["NotThirteen", "NumberRequired"]);
	}

	[Test]
	public void A_generic_entity_is_closed_per_use_and_gets_its_own_set()
	{
		Entity<Guid>.Constraints.AsConstraints().Select(c => c.Name).Should().BeEquivalentTo(["LevelNotFive", "NameRequired", "LevelRange"]);
		Entity<int>.Constraints.Validate(new Entity<int>("", 5, 7)).Select(v => v.ConstraintId).Should().BeEquivalentTo(["LevelNotFive", "NameRequired"]);
	}

	[Test]
	public void A_generic_companion_with_a_field_gets_the_annotations_of_the_generic_type_too()
	{
		BoxRules<int>.All.AsConstraints().Select(c => c.Name).Should().BeEquivalentTo(["HasValue", "LabelRequired"]);
		BoxRules<string>.All.Validate(new Box<string>(" ", "x")).Single().Member.Should().Be("Label");
	}

	[Test]
	public void A_closed_generic_type_can_be_named_by_the_attribute()
	{
		IntBoxRules.All.AsConstraints().Select(c => c.Name).Should().Equal("LabelRequired");
		IntBoxRules.All.Validate(new Box<int>("", 3)).Single().ConstraintId.Should().Be("LabelRequired");
	}
}
