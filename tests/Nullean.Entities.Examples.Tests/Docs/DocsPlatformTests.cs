using AwesomeAssertions;
using CsCheck;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.Docs.DocsPlatform;

namespace Nullean.Entities.Examples.Tests.Docs;

// The documentation platform's rules (docs-eng-team domain model; docs-builder `erd-model` branch), expressed once and tested with the library's one-liners.
public class DocsPlatformTests
{
	private static readonly Catalog ValidCatalog = new(
		[new("elasticsearch", "Elasticsearch", ProductLifecycle.Ga, "stack"), new("apm", "APM", ProductLifecycle.Beta, "stack")],
		[new("stack", new Version(8, 0), new Version(9, 1))]
	);

	private static readonly DocumentationSet ValidSet = new("kibana", [new("/a"), new("/b")], [new("/old", "/a")]);

	// ── The laws, with counter-examples written for the rules a single edge value cannot break ──────────

	[Test]
	public void The_catalog_rules_pass_the_laws() =>
		CatalogConstraints.All.Laws(
			ValidCatalog,
			new Dictionary<string, Func<Catalog, Catalog>>
			{
				["ProductIdsAreUnique"] = c => c with { Products = [.. c.Products, c.Products[0] with { Id = "ELASTICSEARCH" }] },
				["VersioningSystemsExist"] = c => c with { Products = [c.Products[0] with { VersioningSystemId = "nothing" }] },
				["BaseIsNotAfterCurrent"] = c => c with { VersioningSystems = [new("stack", new Version(9, 2), new Version(9, 1))] },
			}
		);

	[Test]
	public void The_documentation_set_rules_pass_the_laws() =>
		DocumentationSetConstraints.All.Laws(
			ValidSet,
			new Dictionary<string, Func<DocumentationSet, DocumentationSet>>
			{
				["PageUrlsAreUnique"] = d => d with { Pages = [.. d.Pages, new Page("/a")] },
				["RedirectSourcesAreUnique"] = d => d with { Redirects = [.. d.Redirects, new Redirect("/old", "/b")] },
				["RedirectTargetsExist"] = d => d with { Redirects = [new Redirect("/old", "/ghost")] },
				["RedirectsDoNotShadowPages"] = d => d with { Redirects = [new Redirect("/b", "/a")] },
			},
			// A chain A to B to C always also trips "target exists" (B is not a page) or "does not shadow a page" (B is), so no counter-example isolates it
			requireCounterExamples: false
		);

	[Test]
	public void The_laws_report_that_a_redirect_chain_cannot_break_only_the_chain_rule()
	{
		var chain = new Dictionary<string, Func<DocumentationSet, DocumentationSet>>
		{
			["RedirectsAreNotChained"] = d => d with { Redirects = [new Redirect("/old", "/older"), new Redirect("/older", "/a")] },
		};

		var act = () => DocumentationSetConstraints.All.Laws(ValidSet, chain, requireCounterExamples: false);

		act
			.Should()
			.Throw<InvalidOperationException>()
			.Which
			.Message
			.Should()
			.Contain("RedirectsAreNotChained")
			.And
			.Contain("also breaks RedirectTargetsExist");
	}

	// ── Generated data finds every rule's counter-example with no hand-made generator ────────────────────

	[Test]
	public void Generated_catalogs_break_every_catalog_rule() => CatalogConstraints.All.Probe().NeverViolated.Should().BeEmpty();

	[Test]
	public void Generated_documentation_sets_break_every_documentation_set_rule() =>
		DocumentationSetConstraints.All.Probe(samples: 3000).NeverViolated.Should().BeEmpty();

	// ── docs-builder's real shapes: ids into other aggregates, a dictionary keyed by name, a URI, a tree identified by path ─────────

	private static readonly Assembly ValidAssembly = new(new Dictionary<string, Repository> { ["kibana"] = new("kibana"), ["apm"] = new("apm") });

	private static readonly Navigation ValidNavigation = new([new(MountUri.Make("kibana"), "/kibana"), new(MountUri.Make("apm"), "/apm")]);

	private static readonly Docset ValidDocset = new(
		"kibana",
		["elasticsearch"],
		"observability",
		[new("index.md", [new("guide.md", []), new("ref.md", [new("api.md", [])])])]
	);

	[Test]
	public void The_assembly_and_navigation_rules_pass_the_laws()
	{
		AssemblyConstraints.All.Laws(
			ValidAssembly,
			new Dictionary<string, Func<Assembly, Assembly>>
			{
				["RepositoryKeysMatchNames"] = _ => new Assembly(new Dictionary<string, Repository> { ["kibana"] = new("kibana-other") }),
			}
		);
		NavigationConstraints.All.Laws(
			ValidNavigation,
			new Dictionary<string, Func<Navigation, Navigation>>
			{
				["PathPrefixesAreUnique"] = n => new Navigation([.. n.Mounts, new Mount(MountUri.Make("apm"), "/kibana")]),
			}
		);
		NavigationInAssemblyConstraints.All.Laws(
			new NavigationInAssembly(ValidNavigation, ValidAssembly),
			new Dictionary<string, Func<NavigationInAssembly, NavigationInAssembly>>
			{
				["MountIsARepository"] = p => p with { Navigation = new Navigation([new Mount(MountUri.Make("ghost"), "/x")]) },
			}
		);
	}

	[Test]
	public void The_docset_rules_pass_the_laws()
	{
		DocsetConstraints.All.Laws(
			ValidDocset,
			new Dictionary<string, Func<Docset, Docset>>
			{
				["NoFileIsListedTwice"] = d => d with { Toc = [new TocNode("a.md", [new TocNode("b.md", [new TocNode("a.md", [])])])] },
				["FilesStayInTheirFolder"] = d => d with { Toc = [new TocNode("../x.md", [])] },
			}
		);
		DocsetInCatalogConstraints.All.Laws(
			new DocsetInCatalog(ValidDocset, ValidCatalog),
			new Dictionary<string, Func<DocsetInCatalog, DocsetInCatalog>>
			{
				["ProductsExist"] = p => p with { Docset = p.Docset with { Products = ["ghost"] } },
			}
		);
		DocsetInCodexConstraints.All.Laws(
			new DocsetInCodex(ValidDocset, new CodexConfig([new CodexGroup("observability", "Observability")])),
			new Dictionary<string, Func<DocsetInCodex, DocsetInCodex>>
			{
				["CodexGroupExists"] = p => p with { Docset = p.Docset with { CodexGroup = "ghost" } },
			}
		);
		TocIncludesConstraints.All.Laws(
			new TocIncludes([new TocFile("a.yml", ["b.yml"]), new TocFile("b.yml", [])]),
			new Dictionary<string, Func<TocIncludes, TocIncludes>>
			{
				["IncludesAreAcyclic"] = _ => new TocIncludes([new TocFile("a.yml", ["b.yml"]), new TocFile("b.yml", ["a.yml"])]),
				["IncludesExist"] = _ => new TocIncludes([new TocFile("a.yml", ["ghost.yml"])]),
			}
		);
	}

	private static Gen<T> Both<T>(Action<AutoOptions> declare) =>
		Gen.OneOf(GenAuto.Of<T>(AutoMode.Mixed, declare), GenAuto.Of<T>(AutoMode.Adversarial, declare));

	[Test]
	public void With_the_relations_declared_generated_data_breaks_every_cross_aggregate_and_tree_rule()
	{
		AssemblyConstraints
			.All
			.Probe(Both<Assembly>(o => o.KeyedBy<Assembly, string, Repository>(a => a.Repositories, r => r.Name)))
			.NeverViolated
			.Should()
			.BeEmpty();
		NavigationConstraints.All.Probe(Both<Navigation>(o => o.Identity<Mount, string>(m => m.PathPrefix))).NeverViolated.Should().BeEmpty();
		NavigationInAssemblyConstraints
			.All
			.Probe(
				Both<NavigationInAssembly>(o =>
						o
							.Reference<Mount, Repository, string, Uri>(m => m.Source, r => r.Name, MountUri.Make)
							.KeyedBy<Assembly, string, Repository>(a => a.Repositories, r => r.Name))
			)
			.NeverViolated
			.Should()
			.BeEmpty();
		DocsetInCatalogConstraints
			.All
			.Probe(Both<DocsetInCatalog>(o => o.References<Docset, Product, string>(d => d.Products, p => p.Id)))
			.NeverViolated
			.Should()
			.BeEmpty();
		DocsetInCodexConstraints
			.All
			.Probe(Both<DocsetInCodex>(o => o.Reference<Docset, CodexGroup, string>(d => d.CodexGroup, g => g.Id)), samples: 2000)
			.NeverViolated
			.Should()
			.BeEmpty();
		DocsetConstraints.All.Probe(Both<Docset>(o => o.Identity<TocNode, string>(n => n.Path)), samples: 3000).NeverViolated.Should().BeEmpty();
		TocIncludesConstraints
			.All
			.Probe(Both<TocIncludes>(o => o.References<TocFile, TocFile, string>(f => f.Includes, f => f.Path)))
			.NeverViolated
			.Should()
			.BeEmpty();
	}

	// ── Lifecycles ────────────────────────────────────────────────────────────────────────────────

	[Test]
	public void A_product_lifecycle_is_a_machine_with_five_states_and_no_way_back()
	{
		var report = ProductMachine.Machine.Spec().Exhaustive();

		report.States.Should().Be(5);
	}

	[Test]
	public void The_product_methods_conform_to_the_declared_lifecycle() =>
		ProductMachine.Machine.ConformTo(
			new Product("p", "P", ProductLifecycle.Preview, "stack"),
			p => p.Lifecycle,
			bindings: [("Promote", p => p.Promote()), ("Deprecate", p => p.Deprecate()), ("Remove", p => p.Remove())]
		);

	// ── Temporal rules over a pipeline's event log ────────────────────────────────────────────────

	private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	private static Timestamped<PipelineEvent> At(int seconds, PipelineEvent e) => new(T0.AddSeconds(seconds), e);

	[Test]
	public void A_good_run_satisfies_every_pipeline_rule()
	{
		var run = new[]
		{
			At(0, new PipelineEvent.BuildSucceeded("kibana")),
			At(1, new PipelineEvent.LinksValidated("kibana")),
			At(2, new PipelineEvent.ManifestPublished("kibana")),
			At(30, new PipelineEvent.LinkIndexUpdated("kibana", T0.AddSeconds(30))),
		};

		Trace.Check(run, PipelineRules.All).Passed.Should().BeTrue();
	}

	[Test]
	public void Publishing_without_validating_the_links_breaks_N48()
	{
		var run = new[] { At(0, new PipelineEvent.BuildSucceeded("kibana")), At(1, new PipelineEvent.ManifestPublished("kibana")) };

		var report = Trace.Check(run, PipelineRules.All, T0.AddSeconds(2));

		report.Passed.Should().BeFalse();
		report.ToString().Should().Contain("PublishFollowsValidation");
	}

	[Test]
	public void An_older_registry_write_after_a_newer_one_breaks_N38()
	{
		var run = new[]
		{
			At(0, new PipelineEvent.LinkIndexUpdated("kibana", T0.AddMinutes(5))),
			At(1, new PipelineEvent.LinkIndexUpdated("kibana", T0.AddMinutes(1))),
		};

		Trace.Check(run, PipelineRules.All, T0.AddSeconds(2)).ToString().Should().Contain("RegistryOnlyMovesForward");
	}

	[Test]
	public void A_build_whose_link_index_update_never_arrives_breaks_the_deadline_rule()
	{
		var run = new[] { At(0, new PipelineEvent.BuildSucceeded("kibana")) };

		var report = Trace.Check(run, PipelineRules.All, T0.AddMinutes(11));

		report.Passed.Should().BeFalse();
		report.ToString().Should().Contain("IndexFollowsBuild");
	}

	// ── Event-sourced: the publish step, for every command sequence rather than a sample ──────────────

	private enum Phase
	{
		Idle,
		Built,
		Validated,
		Published
	}

	private static EventSourcedSpec<Phase, string, PipelineEvent> Pipeline(bool publishWithoutValidating) =>
		EntitySpec.EventSourced<Phase, string, PipelineEvent>(
			Phase.Idle,
			(phase, command) =>
				(command, phase) switch
				{
					("Build", Phase.Idle) => [new PipelineEvent.BuildSucceeded("r")],
					("Validate", Phase.Built) => [new PipelineEvent.LinksValidated("r")],
					("Publish", Phase.Validated) => [new PipelineEvent.ManifestPublished("r")],
					("Publish", Phase.Built) when publishWithoutValidating => [new PipelineEvent.ManifestPublished("r")],
					_ => [],
				},
			(phase, e) =>
				e switch
				{
					PipelineEvent.BuildSucceeded => Phase.Built,
					PipelineEvent.LinksValidated => Phase.Validated,
					PipelineEvent.ManifestPublished => Phase.Published,
					_ => phase,
				},
			["Build", "Validate", "Publish"]
		);

	private static readonly TemporalRuleSet<PipelineEvent> OnlyN48 = TemporalRuleSet<PipelineEvent>.From(
		("PublishFollowsValidation", PipelineRules.PublishFollowsValidation)
	);

	[Test]
	public void The_pipeline_never_publishes_without_validating_for_any_command_sequence() =>
		Pipeline(publishWithoutValidating: false).Exhaustive(OnlyN48).Closed.Should().BeTrue();

	[Test]
	public void A_pipeline_that_can_publish_a_built_but_unvalidated_manifest_is_caught_with_the_commands_that_do_it()
	{
		var act = () => Pipeline(publishWithoutValidating: true).Exhaustive(OnlyN48);

		act.Should().Throw<Exception>().Which.Message.Should().Contain("PublishFollowsValidation").And.Contain("Build").And.Contain("Publish");
	}
}
