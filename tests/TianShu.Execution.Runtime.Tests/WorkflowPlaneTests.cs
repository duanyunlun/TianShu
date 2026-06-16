using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Workflows;
using WorkflowTask = TianShu.Contracts.Workflows.Task;
using Task = System.Threading.Tasks.Task;

namespace TianShu.Execution.Runtime.Tests;

public sealed class WorkflowPlaneTests
{
    [Fact]
    public async Task TianShuExecutionRuntime_ShouldManageWorkflowWritesThroughFormalSkeleton()
    {
        var sut = new TianShuExecutionRuntime();
        var spaceId = new CollaborationSpaceId("space-workflow-runtime");
        var owner = new ParticipantRef(new ParticipantId("agent-workflow-owner"), ParticipantKind.Agent, "Workflow Owner");

        await sut.CreateSpaceAsync(
            new CreateCollaborationSpace(
                spaceId,
                "workflow-runtime",
                "Workflow Runtime",
                new CollaborationSpaceProfile("工作流骨架"),
                CollaborationDefaultSet.Empty),
            CancellationToken.None);

        var workflow = await sut.CreateWorkflowAsync(
            new CreateWorkflow(
                new WorkflowId("workflow-runtime-001"),
                spaceId,
                "Runtime Workflow",
                owner,
                new ThreadId("thread-workflow-runtime-001")),
            CancellationToken.None);
        var plan = await sut.PublishPlanAsync(
            new PublishPlan(
                workflow.Id,
                new Plan(
                    "Runtime Plan",
                    [
                        new PlanStep(0, "Wire workflow writes", "Connect formal write entry points."),
                    ])),
            CancellationToken.None);
        var createdTask = await sut.CreateTaskAsync(
            new CreateTask(
                new WorkflowTask(
                    new TaskId("task-workflow-runtime-001"),
                    workflow.Id,
                    "Implement workflow store",
                    TaskState.Todo,
                    owner)),
            CancellationToken.None);
        var updatedTask = await sut.UpdateTaskStateAsync(
            new UpdateTaskState(
                createdTask.Id,
                TaskState.InProgress,
                owner),
            CancellationToken.None);
        var board = await sut.GetWorkflowBoardProjectionAsync(new GetWorkflowBoard(workflow.Id), CancellationToken.None);
        var taskBoard = await sut.GetTaskBoardProjectionAsync(new GetTaskBoard(workflow.Id), CancellationToken.None);
        var planProjection = await sut.GetPlanProjectionAsync(new GetPlanProjection(workflow.Id), CancellationToken.None);

        Assert.Equal("Runtime Workflow", workflow.DisplayName);
        Assert.Equal("Workflow Runtime", workflow.CollaborationSpace.DisplayName);
        Assert.Equal("Runtime Plan", plan.Plan.Title);
        Assert.Equal("Implement workflow store", createdTask.Title);
        Assert.NotNull(updatedTask);
        Assert.Equal(TaskState.InProgress, updatedTask!.State);
        Assert.NotNull(board);
        Assert.Equal(1, board!.RunningTaskCount);
        Assert.Equal(0, board.PendingTaskCount);
        Assert.NotNull(taskBoard);
        var taskItem = Assert.Single(taskBoard!.Items);
        Assert.Equal("Implement workflow store", taskItem.Title);
        Assert.Equal("in_progress", taskItem.State);
        Assert.NotNull(planProjection);
        Assert.Equal("Runtime Plan", planProjection!.Plan.Title);
        Assert.Equal("Wire workflow writes", Assert.Single(planProjection.Plan.Steps).Title);
    }
}
