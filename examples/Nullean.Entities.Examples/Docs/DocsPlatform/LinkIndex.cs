// The link index and the publishing pipeline as an event stream: the temporal rules of the investigation (N38, N48, the link-index update after a
// successful build). They are rules over what happened and when, so they are checked over an event log, a recorded CI run or a live stream.

namespace Nullean.Entities.Examples.Docs.DocsPlatform;

public abstract record PipelineEvent
{
	public sealed record BuildSucceeded(string Repository) : PipelineEvent;

	public sealed record LinksValidated(string Repository) : PipelineEvent;

	public sealed record ManifestPublished(string Repository) : PipelineEvent;

	public sealed record LinkIndexUpdated(string Repository, DateTimeOffset UpdatedAt) : PipelineEvent;
}

public static class PipelineRules
{
	/// <summary>N38: a registry entry is replaced only by one that is strictly newer.</summary>
	public static readonly TemporalRule<PipelineEvent> RegistryOnlyMovesForward = Temporal<PipelineEvent>.Transitions(
		(previous, next) =>
			previous is not PipelineEvent.LinkIndexUpdated before || next is not PipelineEvent.LinkIndexUpdated after || after.UpdatedAt > before.UpdatedAt
	);

	/// <summary>N48: a manifest is published only right after its links validated (today the redirect validation is allowed to fail).</summary>
	public static readonly TemporalRule<PipelineEvent> PublishFollowsValidation = Temporal<PipelineEvent>.Transitions(
		(previous, next) => next is not PipelineEvent.ManifestPublished || previous is PipelineEvent.LinksValidated
	);

	/// <summary>After a successful build the link index is updated within ten minutes (the update-link-index job).</summary>
	public static readonly TemporalRule<PipelineEvent> IndexFollowsBuild = Temporal<PipelineEvent>
		.After<PipelineEvent.BuildSucceeded>()
		.Then<PipelineEvent.LinkIndexUpdated>((build, update) => build.Repository == update.Repository)
		.Within(TimeSpan.FromMinutes(10));

	public static readonly TemporalRuleSet<PipelineEvent> All = TemporalRuleSet<PipelineEvent>.From(
		(nameof(RegistryOnlyMovesForward), RegistryOnlyMovesForward),
		(nameof(PublishFollowsValidation), PublishFollowsValidation),
		(nameof(IndexFollowsBuild), IndexFollowsBuild)
	);
}
