using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Workflows;
using TianShu.ControlPlane;
using Task = System.Threading.Tasks.Task;

namespace TianShu.Execution.Integration.Tests;

public sealed class TianShuControlPlaneConcreteRuntimeTests
{
    [Fact]
    public void TianShuExecutionRuntime_AsControlPlane_ShouldReturnFormalShell()
    {
        var runtime = new TianShuExecutionRuntime();
        var sut = runtime.AsControlPlane();

        Assert.IsType<TianShuControlPlane>(sut);
    }

    [Fact]
    public async Task TianShuControlPlane_WithConcreteRuntime_ShouldExposeRuntimeBackedFormalShell()
    {
        var runtime = new TianShuExecutionRuntime();
        var sut = new TianShuControlPlane(runtime);
        var spaceId = new CollaborationSpaceId("space-formal-shell");

        var created = await sut.Collaboration.CreateSpaceAsync(
            new CreateCollaborationSpace(
                spaceId,
                "formal-shell",
                "Formal Shell",
                new CollaborationSpaceProfile("正式控制平面"),
                CollaborationDefaultSet.Empty),
            CancellationToken.None);
        var overview = await sut.Collaboration.GetSpaceOverviewAsync(
            new GetCollaborationSpaceOverview(spaceId),
            CancellationToken.None);
        var projection = await sut.Collaboration.GetSpaceProjectionAsync(
            new GetCollaborationSpaceProjection(spaceId),
            CancellationToken.None);
        var spaces = await sut.Memory.ListMemorySpacesAsync(
            new ListMemorySpaces(),
            CancellationToken.None);

        Assert.Equal("Formal Shell", created.DisplayName);
        Assert.Equal("Formal Shell", overview?.DisplayName);
        Assert.Equal("Formal Shell", projection?.CollaborationSpace.DisplayName);
        Assert.NotEmpty(spaces);
    }

    [Fact]
    public async Task TianShuControlPlane_WithConcreteRuntime_ShouldReturnNullOrEmptyForUnmaterializedFormalQueries()
    {
        var runtime = new TianShuExecutionRuntime();
        var sut = new TianShuControlPlane(runtime);

        var workflowBoard = await sut.Workflows.GetWorkflowBoardProjectionAsync(
            new GetWorkflowBoard(new WorkflowId("workflow-formal-null")),
            CancellationToken.None);
        var taskBoard = await sut.Workflows.GetTaskBoardProjectionAsync(
            new GetTaskBoard(new WorkflowId("workflow-formal-null")),
            CancellationToken.None);
        var plan = await sut.Workflows.GetPlanProjectionAsync(
            new GetPlanProjection(new WorkflowId("workflow-formal-null")),
            CancellationToken.None);
        var team = await sut.Agents.GetTeamProjectionAsync(
            new GetTeamProjection(new TeamId("team-formal-null")),
            CancellationToken.None);
        var executionTrace = await sut.Diagnostics.GetExecutionTraceAsync(
            new GetExecutionTrace(new ExecutionTraceId("trace-formal-null")),
            CancellationToken.None);
        var attemptSummaries = await sut.Diagnostics.ListAttemptSummariesAsync(
            new ListAttemptSummaries(new ExecutionId("execution-formal-null")),
            CancellationToken.None);
        var userInputRequests = await sut.Governance.ListUserInputRequestsAsync(
            new ListUserInputRequests(),
            CancellationToken.None);
        var artifact = await sut.Artifacts.GetArtifactProjectionAsync(
            new GetArtifactDetail(new ArtifactId("artifact-formal-null")),
            CancellationToken.None);
        var artifactCollection = await sut.Artifacts.GetArtifactCollectionProjectionAsync(
            new ListArtifacts(new CollaborationSpaceId("space-formal-null"), null),
            CancellationToken.None);

        Assert.Null(workflowBoard);
        Assert.Null(taskBoard);
        Assert.Null(plan);
        Assert.Null(team);
        Assert.Null(executionTrace);
        Assert.Empty(attemptSummaries);
        Assert.Empty(userInputRequests);
        Assert.Null(artifact);
        Assert.Null(artifactCollection);
    }

    [Fact]
    public async Task TianShuControlPlane_WithConcreteRuntime_ShouldManageWorkflowWritesThroughFormalShell()
    {
        var runtime = new TianShuExecutionRuntime();
        var sut = new TianShuControlPlane(runtime);
        var spaceId = new CollaborationSpaceId("space-formal-workflow");
        var owner = new ParticipantRef(new ParticipantId("agent-formal-owner"), ParticipantKind.Agent, "Formal Owner");

        await sut.Collaboration.CreateSpaceAsync(
            new CreateCollaborationSpace(
                spaceId,
                "formal-workflow",
                "Formal Workflow",
                new CollaborationSpaceProfile("工作流正式壳"),
                CollaborationDefaultSet.Empty),
            CancellationToken.None);

        var workflow = await sut.Workflows.CreateWorkflowAsync(
            new CreateWorkflow(
                new WorkflowId("workflow-formal-001"),
                spaceId,
                "Formal Workflow",
                owner,
                new ThreadId("thread-formal-001")),
            CancellationToken.None);
        var publishedPlan = await sut.Workflows.PublishPlanAsync(
            new PublishPlan(
                workflow.Id,
                new Plan(
                    "Formal Plan",
                    [
                        new PlanStep(0, "Expose workflow writes"),
                    ])),
            CancellationToken.None);
        var createdTask = await sut.Workflows.CreateTaskAsync(
            new CreateTask(
                new TianShu.Contracts.Workflows.Task(
                    new TaskId("task-formal-001"),
                    workflow.Id,
                    "Formal Task",
                    TaskState.Todo,
                    owner)),
            CancellationToken.None);
        var updatedTask = await sut.Workflows.UpdateTaskStateAsync(
            new UpdateTaskState(createdTask.Id, TaskState.Done, owner),
            CancellationToken.None);
        var workflowBoard = await sut.Workflows.GetWorkflowBoardProjectionAsync(new GetWorkflowBoard(workflow.Id), CancellationToken.None);
        var taskBoard = await sut.Workflows.GetTaskBoardProjectionAsync(new GetTaskBoard(workflow.Id), CancellationToken.None);
        var planProjection = await sut.Workflows.GetPlanProjectionAsync(new GetPlanProjection(workflow.Id), CancellationToken.None);

        Assert.Equal("Formal Workflow", workflow.DisplayName);
        Assert.Equal("Formal Plan", publishedPlan.Plan.Title);
        Assert.NotNull(updatedTask);
        Assert.Equal(TaskState.Done, updatedTask!.State);
        Assert.NotNull(workflowBoard);
        Assert.Equal(1, workflowBoard!.CompletedTaskCount);
        Assert.NotNull(taskBoard);
        Assert.Equal("done", Assert.Single(taskBoard!.Items).State);
        Assert.NotNull(planProjection);
        Assert.Equal("Expose workflow writes", Assert.Single(planProjection!.Plan.Steps).Title);
    }

    [Fact]
    public async Task TianShuControlPlane_WithConcreteRuntime_ShouldManageArtifactWritesThroughFormalShell()
    {
        var runtime = new TianShuExecutionRuntime();
        var sut = new TianShuControlPlane(runtime);
        var spaceRef = new CollaborationSpaceRef(
            new CollaborationSpaceId("space-formal-artifacts"),
            "formal-artifacts",
            "Formal Artifacts");
        var producer = new ParticipantRef(
            new ParticipantId("agent-formal-artifact"),
            ParticipantKind.Agent,
            "Artifact Agent");
        var artifact = new Artifact(
            new ArtifactId("artifact-formal-001"),
            spaceRef,
            "formal-artifact.md",
            ArtifactKind.Document,
            producer);

        var published = await sut.Artifacts.PublishArtifactAsync(
            new PublishArtifact(artifact),
            CancellationToken.None);
        var promoted = await sut.Artifacts.PromoteArtifactAsync(
            new PromoteArtifact(artifact.Id, "stable"),
            CancellationToken.None);
        var attached = await sut.Artifacts.AttachArtifactToTaskAsync(
            new AttachArtifactToTask(artifact.Id, new TaskId("task-formal-artifact")),
            CancellationToken.None);
        var projection = await sut.Artifacts.GetArtifactProjectionAsync(
            new GetArtifactDetail(artifact.Id),
            CancellationToken.None);
        var collection = await sut.Artifacts.GetArtifactCollectionProjectionAsync(
            new ListArtifacts(spaceRef.Id, null),
            CancellationToken.None);

        Assert.Equal(ArtifactLifecycleState.Published, published.State);
        Assert.Equal(ArtifactLifecycleState.Promoted, promoted.State);
        Assert.Equal(ArtifactLifecycleState.Promoted, attached.State);
        Assert.NotNull(projection);
        Assert.Equal("formal-artifact.md", projection!.Name);
        Assert.Equal(ArtifactLifecycleState.Promoted, projection.State);
        var item = Assert.Single(collection!.Items);
        Assert.Equal("stable", Assert.Single(item.PromotionChannels));
        Assert.Equal("task-formal-artifact", Assert.Single(item.AttachedTaskIds).Value);
    }
}
