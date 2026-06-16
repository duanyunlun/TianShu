using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;
using TianShu.Kernel;
using Xunit;

namespace TianShu.ControlPlane.Tests;

public sealed class SessionContextProjectorTests
{
    [Fact]
    public void Project_AppliesBudgetAndPreservesSourceCheckpointIds()
    {
        var projector = new SessionContextProjector();
        var stage = BuiltInStageDefinitions.All.Single(static item => item.Id == BuiltInStageDefinitions.Review);
        var checkpoint = new StageCheckpoint(
            "checkpoint-coding",
            BuiltInStageDefinitions.Coding,
            StageExecutionState.Completed,
            DateTimeOffset.Parse("2026-06-09T00:00:00Z"));

        var package = projector.Project(
            "ctx-review",
            stage,
            new SessionId("session-001"),
            new ThreadId("thread-001"),
            [checkpoint],
            [
                new StageContextSegment("summary", "required summary", required: true, estimatedTokens: 32),
                new StageContextSegment("trace", "large optional trace", estimatedTokens: 64),
            ],
            contextBudgetTokens: 40);

        var segment = Assert.Single(package.Segments);
        Assert.Equal("summary", segment.Kind);
        Assert.Equal("checkpoint-coding", Assert.Single(package.SourceCheckpointIds));
        Assert.Equal(StageContextProjectionMode.SelectedSegments, package.ProjectionMode);
        Assert.Equal(40, package.BudgetTokens);
    }

    [Fact]
    public void Project_ReferencesOnlyKeepsReferenceSegments()
    {
        var projector = new SessionContextProjector();
        var stage = BuiltInStageDefinitions.All.Single(static item => item.Id == BuiltInStageDefinitions.LongContext);

        var package = projector.Project(
            "ctx-long-context",
            stage,
            new SessionId("session-001"),
            new ThreadId("thread-001"),
            [],
            [
                new StageContextSegment("summary", "plain summary"),
                new StageContextSegment("reference", "reference by kind"),
                new StageContextSegment(
                    "trace",
                    "reference by source",
                    source: new ResourceRef("artifact", "artifact-001")),
            ],
            contextBudgetTokens: null);

        Assert.Equal(StageContextProjectionMode.ReferencesOnly, package.ProjectionMode);
        Assert.Equal(["reference", "trace"], package.Segments.Select(static segment => segment.Kind).ToArray());
    }

    [Fact]
    public void Project_IncludesObservedStateSegments()
    {
        var projector = new SessionContextProjector();
        var stage = BuiltInStageDefinitions.All.Single(static item => item.Id == BuiltInStageDefinitions.Review);
        var observed = new SessionObservedState(
            workspaceStateSegments:
            [
                new StageContextSegment(
                    "workspace_state",
                    "cwd=D:\\GitRepos\\Personal\\TianShu",
                    source: new ResourceRef("workspace", "repo"),
                    required: true),
            ],
            artifactStateSegments:
            [
                new StageContextSegment(
                    "artifact_state",
                    "id=artifact-001",
                    source: new ResourceRef("artifact_state", "thread")),
            ],
            diagnosticStateSegments:
            [
                new StageContextSegment(
                    "diagnostic_state",
                    "code=extension_stage_disabled",
                    source: new ResourceRef("stage_registry_issue", "extension_stage_disabled")),
            ],
            memoryStateSegments:
            [
                new StageContextSegment(
                    "memory_state",
                    "memory_mode=enabled",
                    source: new ResourceRef("memory", "thread")),
            ],
            policyStateSegments:
            [
                new StageContextSegment(
                    "policy_state",
                    "approval_policy=never",
                    source: new ResourceRef("policy", "runtime")),
            ]);

        var package = projector.Project(
            "ctx-review",
            stage,
            new SessionId("session-001"),
            new ThreadId("thread-001"),
            [],
            [new StageContextSegment("summary", "ledger summary")],
            contextBudgetTokens: null,
            observedState: observed);

        Assert.Contains(package.Segments, static segment => segment.Kind == "workspace_state");
        Assert.Contains(package.Segments, static segment => segment.Kind == "artifact_state");
        Assert.Contains(package.Segments, static segment => segment.Kind == "diagnostic_state");
        Assert.Contains(package.Segments, static segment => segment.Kind == "memory_state");
        Assert.Contains(package.Segments, static segment => segment.Kind == "policy_state");
    }

    [Fact]
    public void SessionOrchestrator_DoesNotOwnContextProjection()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "TianShu.Kernel", "SessionOrchestrator.cs"));
        var entryPlannerSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "TianShu.Kernel", "SessionCoreLoopEntryPlanner.cs"));

        Assert.DoesNotContain("SessionContextProjector", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new StageContextPackage(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private IReadOnlyList<StageContextSegment> ProjectSegments(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StageContextProjectionMode.ReferencesOnly", source, StringComparison.Ordinal);
        Assert.Contains("private readonly SessionContextProjector contextProjector;", entryPlannerSource, StringComparison.Ordinal);
        Assert.Contains("contextProjector.Project(", entryPlannerSource, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
