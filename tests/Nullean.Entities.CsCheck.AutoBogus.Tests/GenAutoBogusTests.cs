using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.CsCheck.AutoBogus;

namespace Nullean.Entities.CsCheck.AutoBogus.Tests;

internal sealed record Line(string Sku, int Quantity, decimal Price);

internal sealed record Cart(string Owner, List<Line> Lines);

public class GenAutoBogusTests
{
	private static IEnumerable<T> Draw<T>(Gen<T> gen, int count, uint stream = 1) =>
		Enumerable.Range(0, count).Select(i => gen.Generate(new PCG(stream, (ulong)i + 1), null, out _));

	[Test]
	public void The_same_seed_gives_the_same_value() => Draw(GenAutoBogus.Of<Line>(), 100).Should().Equal(Draw(GenAutoBogus.Of<Line>(), 100));

	[Test]
	public void Every_public_member_is_filled_including_nested_collections()
	{
		var carts = Draw(GenAutoBogus.Of<Cart>(), 200).ToList();

		carts.Should().OnlyContain(c => !string.IsNullOrEmpty(c.Owner));
		carts.Should().Contain(c => c.Lines.Count > 0);
		carts.SelectMany(c => c.Lines).Should().OnlyContain(l => !string.IsNullOrEmpty(l.Sku));
	}

	[Test]
	public void The_configure_delegate_reaches_the_AutoFaker_once_per_thread()
	{
		var configured = 0;
		var gen = GenAutoBogus.Of<Line>(_ => Interlocked.Increment(ref configured));

		Draw(gen, 50).ToList();

		configured.Should().Be(1);
	}

	[Test]
	public void Values_shrink_when_a_property_fails_and_a_seed_is_reported()
	{
		var act = () => GenAutoBogus.Of<Cart>().Sample(c => c.Owner.Length <= 2, iter: 500);

		act.Should().Throw<CsCheckException>().Which.Message.Should().Contain("Set seed");
	}

	[Test]
	public void Many_threads_can_draw_at_once()
	{
		var gen = GenAutoBogus.Of<Cart>();

		var act = () => Parallel.For(0, 8, t => Draw(gen, 200, (uint)t + 1).ToList());

		act.Should().NotThrow();
	}

	[Test]
	public void It_plugs_into_the_property_helpers_like_any_other_generator()
	{
		var rules = ConstraintSet.From(("HasOwner", Constraints<Cart>.Always(c => c.Owner.Length > 0, "owner is required")));

		var act = () => rules.ForAll(c => c.Owner.Length > 0, GenAutoBogus.Of<Cart>(), iter: 100);

		act.Should().NotThrow();
	}

	[Test]
	public void Unlike_the_default_generator_it_produces_plausible_values_only()
	{
		var lines = Draw(GenAutoBogus.Of<Line>(), 2_000).ToList();

		lines.Should().NotContain(l => string.IsNullOrWhiteSpace(l.Sku));
		lines.Should().NotContain(l => l.Price < 0);
	}
}
