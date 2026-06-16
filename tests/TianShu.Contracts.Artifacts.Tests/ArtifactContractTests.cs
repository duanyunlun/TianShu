using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Artifacts.Tests;

public sealed class ArtifactContractTests
{
    [Fact]
    public void Artifact_RejectsBlankName()
    {
        var space = new CollaborationSpaceRef(new CollaborationSpaceId("space-artifact"), "design", "Design");

        Assert.Throws<ArgumentException>(() => new Artifact(
            new ArtifactId("artifact-001"),
            space,
            " ",
            ArtifactKind.Document));
    }

    [Fact]
    public void Artifact_PreservesLineageAndProducer()
    {
        var space = new CollaborationSpaceRef(new CollaborationSpaceId("space-artifact"), "design", "Design");
        var participant = new ServiceParticipant(
            new ParticipantId("participant-artifact"),
            "Publisher",
            "owner");
        var artifact = new Artifact(
            new ArtifactId("artifact-002"),
            space,
            "summary.md",
            ArtifactKind.Document,
            ParticipantRef.From(participant),
            new ArtifactLineage(
                new ArtifactRef(new ArtifactId("artifact-parent"), "draft.md", "document"),
                new ExecutionId("execution-artifact")));

        Assert.Equal("participant-artifact", artifact.ProducedByParticipant?.Id.Value);
        Assert.Equal("execution-artifact", artifact.Lineage?.ProducedByExecutionId?.Value);
    }

    [Fact]
    public void ControlPlaneConversationArtifactQuery_PreservesThreadAndRollout()
    {
        var query = new ControlPlaneConversationArtifactQuery
        {
            ThreadId = new ThreadId("thread-artifact-001"),
            RolloutPath = @"D:\repo\.tianshu\rollouts\artifact.jsonl",
        };

        Assert.Equal("thread-artifact-001", query.ThreadId?.Value);
        Assert.Equal(@"D:\repo\.tianshu\rollouts\artifact.jsonl", query.RolloutPath);
    }

    [Fact]
    public void ControlPlaneConversationArtifact_AndGitDiff_PreserveSnapshot()
    {
        var artifact = new ControlPlaneConversationArtifact
        {
            ConversationId = "conv-001",
            Path = @"D:\repo\.tianshu\threads\conv-001.jsonl",
            Preview = "summary preview",
            Timestamp = "2026-04-08T10:00:00Z",
            UpdatedAt = "2026-04-08T10:05:00Z",
            ModelProvider = "openai",
            WorkingDirectory = @"D:\repo",
            CliVersion = "0.1.0",
            Source = "cli",
            GitSha = "abc123",
            GitBranch = "refactor/contracts-first",
            GitOriginUrl = "https://example.com/tianshu.git",
        };
        var diff = new ControlPlaneGitDiffArtifact
        {
            HasChanges = true,
            Diff = "diff --git a/src b/src",
        };

        Assert.Equal("conv-001", artifact.ConversationId);
        Assert.Equal("summary preview", artifact.Preview);
        Assert.Equal("abc123", artifact.GitSha);
        Assert.Equal("refactor/contracts-first", artifact.GitBranch);
        Assert.True(diff.HasChanges);
        Assert.Equal("diff --git a/src b/src", diff.Diff);
    }

    [Fact]
    public void ArtifactStateProjectionModuleContracts_ShouldPreserveArtifactStepAndKernelSource()
    {
        var artifact = CreateArtifact("artifact-module-001");
        var step = CreateArtifactStep("artifact-module-step", "publish");
        var context = CreateModuleContext(step);
        var invocation = new ArtifactModuleMutationInvocation(
            step,
            new PublishArtifactModuleMutation(new PublishArtifact(artifact)),
            context);

        Assert.Equal("artifact-module-step", invocation.Context.RuntimeStepId);
        Assert.Equal("kernel-run-artifact", invocation.Context.KernelRunId.Value);
        Assert.Equal("graph-artifact", invocation.Context.SourceGraphId.Value);
        Assert.Equal("stage-artifact", invocation.Context.SourceStageId.Value);
        Assert.Equal("publish", invocation.Mutation.OperationName);
        Assert.Same(step, invocation.Step);
    }

    [Fact]
    public void ProjectionSnapshotView_ShouldExposeOnlyContractView()
    {
        var delta = new ProjectionDelta(
            new ArtifactProjectionPayload(
                new ArtifactProjection(
                    new ArtifactId("artifact-projection-001"),
                    "projection.md",
                    ArtifactKind.Document,
                    ArtifactLifecycleState.Published,
                    CreateSpaceRef())));

        var view = new ProjectionSnapshotView(
            ProjectionScopeKind.Artifact,
            "artifact-projection-001",
            delta: delta,
            version: 2,
            updatedAt: new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(ProjectionScopeKind.Artifact, view.ScopeKind);
        Assert.Equal("artifact-projection-001", view.ScopeKey);
        Assert.Equal(2, view.Version);
        Assert.Equal("artifact", view.Delta?.Payload.Kind);
        Assert.Throws<ArgumentException>(() => new ProjectionSnapshotView(
            ProjectionScopeKind.Artifact,
            "artifact-projection-001"));
    }

    [Fact]
    public void ArtifactCheckpointMaterializationRequest_ShouldBindKernelRunGraphStageAndExecution()
    {
        var request = new ArtifactCheckpointMaterializationRequest(
            new KernelRunId("kernel-run-artifact"),
            new StageGraphId("graph-artifact"),
            new StageId("stage-artifact"),
            new ExecutionId("execution-artifact"),
            new ExecutionTraceId("trace-artifact"),
            new RecoveryCheckpoint(
                new ExecutionId("execution-artifact"),
                "stage-artifact",
                StructuredValue.FromString("checkpoint")));

        Assert.Equal("kernel-run-artifact", request.KernelRunId.Value);
        Assert.Equal("graph-artifact", request.SourceGraphId.Value);
        Assert.Equal("stage-artifact", request.SourceStageId.Value);
        Assert.Equal("execution-artifact", request.ExecutionId.Value);
        Assert.Throws<ArgumentException>(() => new ArtifactCheckpointMaterializationRequest(
            new KernelRunId("kernel-run-artifact"),
            new StageGraphId("graph-artifact"),
            new StageId("stage-artifact"),
            new ExecutionId("execution-artifact"),
            new ExecutionTraceId("trace-artifact"),
            new RecoveryCheckpoint(new ExecutionId("execution-other"), "stage-artifact")));
    }

    private static ArtifactStep CreateArtifactStep(string stepId, string operationName)
        => new(
            stepId,
            new CoreIntentId("intent-artifact"),
            new StageGraphId("graph-artifact"),
            new StageId("stage-artifact"),
            new KernelOperationId("operation-artifact"),
            operationName,
            StructuredValue.FromString("artifact"),
            new PermissionEnvelope(scopes: ["artifact.write"], requiresHumanGate: false),
            new SideEffectProfile(SideEffectLevel.WorkspaceWrite, ["artifact"], reversible: false, requiresAudit: true),
            new KernelBudget(tokenBudget: 100, timeBudgetMs: 1000, costBudget: 1, retryBudget: 1, toolCallBudget: 1),
            new ContractRef("artifact.output", "v1"),
            new TracePolicy());

    private static ArtifactModuleInvocationContext CreateModuleContext(ArtifactStep step)
        => new(
            step.StepId,
            step.SourceIntentId,
            step.SourceGraphId,
            step.SourceStageId,
            step.SourceKernelOperationId,
            new KernelRunId("kernel-run-artifact"),
            new ExecutionId("execution-artifact"),
            step.Permission,
            step.SideEffect,
            step.Metadata);

    private static Artifact CreateArtifact(string artifactId)
        => new(
            new ArtifactId(artifactId),
            CreateSpaceRef(),
            "summary.md",
            ArtifactKind.Document,
            ParticipantRef.From(new ServiceParticipant(
                new ParticipantId("participant-artifact"),
                "publisher",
                "service")));

    private static CollaborationSpaceRef CreateSpaceRef()
        => new(
            new CollaborationSpaceId("space-artifact"),
            "artifact-space",
            "Artifact Space");
}
