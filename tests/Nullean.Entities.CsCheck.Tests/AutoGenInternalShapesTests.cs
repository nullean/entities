using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

// The shape a domain uses to keep production code from building or editing its records: an internal constructor, or an internal init setter.
public sealed record InternalCtor
{
	internal InternalCtor(string name, int count)
	{
		Name = name;
		Count = count;
	}

	public string Name { get; }

	public int Count { get; }
}

public sealed record InternalInit
{
	public string Name { get; internal init; } = "";

	public int Count { get; internal init; }

	public IReadOnlyList<InternalCtor> Items { get; internal init; } = [];

	// private setters are still left alone
	public string Secret { get; private set; } = "kept";
}

public class AutoGenInternalShapesTests
{
	private static List<T> Draw<T>() =>
		[.. Enumerable.Range(0, 50).Select(i => GenAuto.Of<T>(AutoMode.Plausible).Generate(new PCG(1u, (ulong)i + 1), null, out _))];

	[Test]
	public void A_record_with_an_internal_constructor_is_built_through_it()
	{
		var values = Draw<InternalCtor>();

		values.Should().OnlyContain(v => v.Name.Length > 0);
		values.Select(v => v.Count).Distinct().Should().HaveCountGreaterThan(1);
	}

	[Test]
	public void Properties_with_an_internal_init_setter_are_filled_and_private_setters_left_alone()
	{
		var values = Draw<InternalInit>();

		values.Should().OnlyContain(v => v.Name.Length > 0 && v.Secret == "kept");
		values.Should().Contain(v => v.Items.Count > 0 && v.Items.All(i => i.Name.Length > 0));
	}
}
