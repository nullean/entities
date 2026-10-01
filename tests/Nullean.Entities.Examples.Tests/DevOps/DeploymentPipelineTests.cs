using AwesomeAssertions;
using Nullean.Entities.Examples.DevOps.DeploymentPipeline;

namespace Nullean.Entities.Examples.Tests.DevOps;

public class DeploymentPipelineTests
{
	private static readonly ConstraintSet<DeploymentPipeline> Constraints = DeploymentPipelineConstraints.Constraints;

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
		var channel = ConstraintChannel.For(Queued()).Enforce(Constraints);
		var result = channel.Apply(p => p.Start());
		result.Should().BeOfType<ChannelResult<DeploymentPipeline>.Applied>();
	}

	[Test]
	public void Channel_Failure_on_illegal_Queued_to_Succeeded_transition()
	{
		var channel = ConstraintChannel.For(Queued()).Enforce(Constraints);
		var result = channel.Apply(p => p.Complete());
		result.Should().BeOfType<ChannelResult<DeploymentPipeline>.Failure>();
	}

	[Test]
	public void Channel_Applied_on_Running_to_Succeeded()
	{
		var running = Queued().Start();
		var channel = ConstraintChannel.For(running).Enforce(Constraints);
		var result = channel.Apply(p => p.Complete());
		result.Should().BeOfType<ChannelResult<DeploymentPipeline>.Applied>();
	}
}
