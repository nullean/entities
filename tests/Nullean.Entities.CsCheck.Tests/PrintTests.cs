using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;

namespace Nullean.Entities.CsCheck.Tests;

internal sealed record Registry(IReadOnlyDictionary<string, int> Entries);

public class PrintTests
{
	/// <summary>
	/// Law: a failure shows the entries of a collection a record holds, not the collection's type name.
	/// Consequence if broken: the reader of a failing generated value sees <c>Dictionary`2[...]</c> and cannot tell what broke the rule.
	/// </summary>
	[Test]
	public async Task A_value_holding_a_dictionary_is_printed_with_its_entries()
	{
		var set = ConstraintSet.From(("no-bad-key", Constraints<Registry>.Always(r => !r.Entries.ContainsKey("bad"), "No bad key.")));
		var start = new Registry(new Dictionary<string, int> { ["bad"] = 1 });

		var act = () => set.InvariantAsync(Gen.Const(start));

		var message = (await act.Should().ThrowAsync<Exception>()).Which.Message;
		message.Should().Contain("bad").And.NotContain("Dictionary`2");
	}
}
