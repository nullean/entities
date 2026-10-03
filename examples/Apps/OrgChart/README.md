# OrgChart

A reporting hierarchy held as a nested tree. `dotnet run --project examples/Apps/OrgChart` (exits non-zero if anything stops behaving as narrated).

| File | What to copy |
|---|---|
| `Org.cs` | A recursive record (`OrgUnit` with `Reports`), edits as plain tree surgery with `Tree.Map`, rules over **every node at every depth** with `Tree.Flatten`/`FlattenWithAncestors`, and one rule set per kind of edit (`ForHire`, `ForMove`, `ForTerminate`). |
| `OrgService.cs` | Perform the edit, hand it to the rule set for that kind of edit. Uses `ApplyDetailed` so that "nothing changed" is a failure for an edit that names someone who is not there. |
| `Program.cs` | The scenarios, narrated. |

Tests: `tests/Nullean.Entities.Apps.Tests/OrgChart`. Any sequence of hires, moves and terminations leaves a valid hierarchy, with the headcount changing exactly as the accepted edit's rule says; and `Probe` over generated trees proves each rule can be broken.

Things worth noticing:

- The edits do not check anything. A hire under a manager who does not exist just does not change the tree, and the rule for hires says that is a failure ("a hire adds exactly one person"). Moving a manager under their own team loses them, and `Conserves(headcount)` says that is a failure. Rules about the *effect* of an edit are shorter, and harder to get wrong, than checks on its *inputs*.
- Rules attached to one kind of edit (`Conserves` for a move, a decrease for a termination) are right for that edit and wrong for the others, so they are merged into a set per operation instead of living in the general one.
- A nested tree cannot contain a cycle, but it can contain the same person twice or lose a subtree; those are the failures it has, and `one-person-one-place` and the headcount rules are what catch them.
- **`Apply` and an edit that changes nothing.** If the result equals the input, `Apply` returns `Ok` and runs no rule. Whether a tree edit returns an equal value can depend on details (does it rebuild the tree or return the same one?), so an operation where "nothing happened" is an error should use `ApplyDetailed` and say so. A property test over random edits found this inconsistency here: terminating a missing person was refused in a non-empty org and accepted in an empty one.
