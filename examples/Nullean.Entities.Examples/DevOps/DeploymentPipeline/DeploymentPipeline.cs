namespace Nullean.Entities.Examples.DevOps.DeploymentPipeline;

public enum StageStatus
{
	Pending,
	Running,
	Succeeded,
	Failed,
	Skipped
}

public enum PipelineStatus
{
	Queued,
	Running,
	Succeeded,
	Failed,
	Cancelled
}

public sealed record PipelineStage(string Name, StageStatus Status, string? ErrorMessage = null)
{
	public PipelineStage Start() => this with { Status = StageStatus.Running };

	public PipelineStage Succeed() => this with { Status = StageStatus.Succeeded };

	public PipelineStage Fail(string error) => this with { Status = StageStatus.Failed, ErrorMessage = error };

	public PipelineStage Skip() => this with { Status = StageStatus.Skipped };
}

public sealed record DeploymentPipeline(
	Guid Id,
	string Repository,
	string CommitSha,
	string TargetEnvironment,
	PipelineStatus Status,
	IReadOnlyList<PipelineStage> Stages,
	DateTimeOffset CreatedAt,
	DateTimeOffset? CompletedAt
)
{
	public DeploymentPipeline Start() => this with { Status = PipelineStatus.Running };

	public DeploymentPipeline Complete() => this with { Status = PipelineStatus.Succeeded, CompletedAt = DateTimeOffset.UtcNow };

	public DeploymentPipeline Fail() => this with { Status = PipelineStatus.Failed, CompletedAt = DateTimeOffset.UtcNow };

	public DeploymentPipeline Cancel() => this with { Status = PipelineStatus.Cancelled, CompletedAt = DateTimeOffset.UtcNow };

	public DeploymentPipeline UpdateStage(PipelineStage updated) => this with { Stages = [.. Stages.Select(s => s.Name == updated.Name ? updated : s)] };
}

[Constraints]
public static partial class DeploymentPipelineConstraints
{
	public static readonly Constraint<DeploymentPipeline> LegalStatusTransitions = Constraints<DeploymentPipeline>
		.Transitions(p => p.Status)
		.Allow(PipelineStatus.Queued, PipelineStatus.Running)
		.Allow(PipelineStatus.Queued, PipelineStatus.Cancelled)
		.Allow(PipelineStatus.Running, PipelineStatus.Succeeded)
		.Allow(PipelineStatus.Running, PipelineStatus.Failed)
		.Allow(PipelineStatus.Running, PipelineStatus.Cancelled);

	public static readonly Constraint<DeploymentPipeline> CommitShaNotEmpty = Constraints<DeploymentPipeline>.Always(
		p => !string.IsNullOrWhiteSpace(p.CommitSha),
		"Commit SHA must not be empty."
	);

	public static readonly Constraint<DeploymentPipeline> CompletedPipelineHasTimestamp = Constraints<DeploymentPipeline>.When(
		p => p.Status is PipelineStatus.Succeeded or PipelineStatus.Failed or PipelineStatus.Cancelled
	).Requires(p => p.CompletedAt is not null, "Completed pipelines must record a completion timestamp.");

	public static readonly Constraint<DeploymentPipeline> NoFailedStagesOnSuccess = Constraints<DeploymentPipeline>.When(
		p => p.Status == PipelineStatus.Succeeded
	).Requires(p => p.Stages.All(s => s.Status != StageStatus.Failed), "A succeeded pipeline must not have any failed stages.");
}
