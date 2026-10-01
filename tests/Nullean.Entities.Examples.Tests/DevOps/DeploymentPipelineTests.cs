using AwesomeAssertions;
using Nullean.Entities.CsCheck;
using Nullean.Entities.Examples.DevOps.DeploymentPipeline;

namespace Nullean.Entities.Examples.Tests.DevOps;

public class DeploymentPipelineTests
{
	private static readonly ConstraintSet<DeploymentPipeline> Constraints = DeploymentPipelineConstraints.All;

	private static DeploymentPipeline Queued() =>
		new(
			Guid.NewGuid(),
			"my-repo",
			"abc1234",
			"staging",
			PipelineStatus.Queued,
			[new PipelineStage("build", StageStatus.Pending)],
			DateTimeOffset.UtcNow,
			null
		);

	[Test]
	public void Validate_queued_pipeline_passes()
	{
		Constraints.Validate(Queued()).Should().BeEmpty();
	}

	[Test]
	public void Validate_rejects_empty_commit_sha()
	{
		var bad = Queued() with { CommitSha = "" };
		Constraints.Validate(bad).Should().NotBeEmpty();
	}

	[Test]
	public void Validate_succeeded_pipeline_with_no_timestamp_fails()
	{
		var bad = Queued() with { Status = PipelineStatus.Succeeded, CompletedAt = null };
		Constraints.Validate(bad).Should().NotBeEmpty();
	}

	[Test]
	public void Channel_Applied_on_start()
	{
		var channel = ConstrainedValue.For(Queued()).Enforce(Constraints);
		var result = channel.ApplyDetailed(p => p.Start());
		result.Should().BeOfType<ApplyOutcome<DeploymentPipeline>.Applied>();
	}

	[Test]
	public void Channel_Failure_on_illegal_Queued_to_Succeeded_transition()
	{
		var channel = ConstrainedValue.For(Queued()).Enforce(Constraints);
		var result = channel.ApplyDetailed(p => p.Complete());
		result.Should().BeOfType<ApplyOutcome<DeploymentPipeline>.Failure>();
	}

	[Test]
	public void Channel_Applied_on_Running_to_Succeeded()
	{
		var running = Queued().Start();
		var channel = ConstrainedValue.For(running).Enforce(Constraints);
		var result = channel.ApplyDetailed(p => p.Complete());
		result.Should().BeOfType<ApplyOutcome<DeploymentPipeline>.Applied>();
	}

	// Complete()/Fail()/Cancel() stamp DateTimeOffset.UtcNow, so every run is a new state; the key looks only at the shape.
	[Test]
	public void AutoTest_enforced_pipelines_only_follow_legal_transitions() =>
		DeploymentPipelineConstraints
			.Instance
			.Spec(Queued())
			.Domain(new PipelineStage("build", StageStatus.Running), new PipelineStage("build", StageStatus.Failed, "boom"))
			.Domain("boom")
			.Key(p => (p.Status, p.CompletedAt is null, string.Join(",", p.Stages.Select(st => st.Status))))
			.Enforced()
			.Exhaustive();
}
