using AwesomeAssertions;
using CsCheck;
using Nullean.Entities;
using Nullean.Entities.CsCheck;
using OrgChart;

namespace Nullean.Entities.Apps.Tests.OrgChart;

public class OrgServiceTests
{
	private static async Task<(OrgService Service, InMemoryOrgRepository Repository)> Founded()
	{
		var repository = new InMemoryOrgRepository();
		var service = new OrgService(repository);
		await service.FoundAsync("Grace");
		await service.HireAsync("Alan", 1);
		await service.HireAsync("Barbara", 2);
		return (service, repository);
	}

	[Test]
	public async Task Hiring_under_a_missing_manager_is_refused_by_the_rule_for_hires()
	{
		var (service, repository) = await Founded();

		var result = await service.HireAsync("Nobody", 99);

		result.ViolationsOrEmpty.Select(v => v.ConstraintId).Should().Contain("hire-adds-one-person");
		(await repository.LoadAsync()).Count.Should().Be(3);
	}

	[Test]
	public async Task Moving_a_manager_under_their_own_report_is_refused_because_it_would_lose_people()
	{
		var (service, repository) = await Founded();

		var result = await service.MoveAsync(2, 3); // Alan under Barbara, who reports to Alan

		result.ViolationsOrEmpty.Select(v => v.ConstraintId).Should().Contain("move-keeps-everyone");
		(await repository.LoadAsync()).Count.Should().Be(3);
	}

	[Test]
	public async Task Moving_someone_to_another_team_keeps_their_team()
	{
		var (service, repository) = await Founded();
		await service.HireAsync("Edsger", 1); // 4

		(await service.MoveAsync(2, 4)).IsOk.Should().BeTrue();

		var org = await repository.LoadAsync();
		org.People.Single(p => p.Id == 4).Reports.Single().Reports.Single().Name.Should().Be("Barbara");
	}

	[Test]
	public async Task Terminating_a_manager_promotes_their_reports()
	{
		var (service, repository) = await Founded();

		(await service.TerminateAsync(2)).IsOk.Should().BeTrue();

		var org = await repository.LoadAsync();
		org.Head!.Reports.Select(r => r.Name).Should().Equal("Barbara");
		(await service.TerminateAsync(1)).ViolationsOrEmpty.Select(v => v.ConstraintId).Should().Contain("terminate-removes-one-person");
	}

	[Test]
	public async Task Nobody_can_have_nine_reports_or_sit_seven_levels_down()
	{
		var (service, _) = await Founded();
		for (var i = 0; i < 6; i++)
		{
			await service.HireAsync($"R{i}", 1); // the head now has Alan and six more
		}
		(await service.HireAsync("Eighth", 1)).IsOk.Should().BeTrue();

		(await service.HireAsync("Ninth", 1)).ViolationsOrEmpty.Select(v => v.ConstraintId).Should().Contain("span-of-control");

		var id = 3; // Barbara: head > Alan > Barbara is level 3
		for (var level = 0; level < 3; level++)
		{
			id = (await service.HireAsync($"L{level}", id)).Unwrap().NextId - 1;
		}
		(await service.HireAsync("Too deep", id)).ViolationsOrEmpty.Select(v => v.ConstraintId).Should().Contain("chain-of-command");
	}

	[Test]
	public void Whatever_edits_arrive_the_hierarchy_stays_valid_and_each_edit_changes_the_headcount_as_its_rule_says()
	{
		var edit = Gen.Select(Gen.Int[0, 2], Gen.Int[0, 14], Gen.Int[0, 14]);

		edit
			.List[0, 40]
			.Sample(
				edits =>
				{
					var repository = new InMemoryOrgRepository();
					var service = new OrgService(repository);
					service.FoundAsync("Head").GetAwaiter().GetResult();

					foreach (var (kind, a, b) in edits)
					{
						var before = repository.LoadAsync().GetAwaiter().GetResult().Count;
						var result = kind switch
						{
							0 => service.HireAsync($"P{a}", a),
							1 => service.MoveAsync(a, b),
							_ => service.TerminateAsync(a),
						};
						var after = repository.LoadAsync().GetAwaiter().GetResult().Count;
						if (result.GetAwaiter().GetResult().IsOk)
						{
							after.Should().Be(kind switch
							{
								0 => before + 1,
								1 => before,
								_ => before - 1
							});
						}
						else
						{
							after.Should().Be(before);
						}
					}

					OrgRules.All.Validate(repository.LoadAsync().GetAwaiter().GetResult()).Where(v => v.Severity == Severity.Error).Should().BeEmpty();
				},
				iter: 300,
				print: edits => string.Join(", ", edits)
			);
	}

	// The default generator stops about one level of people down; the chain-of-command rule is about six, so ask for deeper trees or it can never be seen to fail.
	// MaxDepth counts a list and its element as two levels, so a tree of N levels needs about 2N.
	private static void Deep(AutoOptions o)
	{
		o.MaxDepth = 16; // a list of nodes and then a node are two levels, so this is about eight levels of people
		o.MaxCollection = 10;
		o.Identity<OrgUnit, int>(u => u.Id);
	}

	[Test]
	public void Generated_hierarchies_break_every_rule_so_none_is_a_tautology() =>
		OrgRules
			.All
			.Probe(
				Gen.OneOf(
					GenAuto.Of<Org>(AutoMode.Mixed, o => o.Identity<OrgUnit, int>(u => u.Id)),
					GenAuto.Of<Org>(AutoMode.Adversarial, Deep)
				),
				samples: 3000
			)
			.NeverViolated
			.Should()
			.BeEmpty();
}
