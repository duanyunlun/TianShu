using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Workflows;
using WorkflowTask = TianShu.Contracts.Workflows.Task;
using Task = System.Threading.Tasks.Task;

namespace TianShu.Execution.Runtime;

public sealed partial class TianShuExecutionRuntime
{
    private readonly InMemoryTianShuWorkflowPlane workflowPlane;

    public Task<Workflow> CreateWorkflowAsync(CreateWorkflow command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workflowPlane.CreateWorkflow(command));
    }

    public Task<PlanProjection> PublishPlanAsync(PublishPlan command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workflowPlane.PublishPlan(command));
    }

    public Task<WorkflowTask> CreateTaskAsync(CreateTask command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workflowPlane.CreateTask(command));
    }

    public Task<WorkflowTask?> UpdateTaskStateAsync(UpdateTaskState command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(workflowPlane.UpdateTaskState(command));
    }
}

internal sealed class InMemoryTianShuWorkflowPlane
{
    private readonly object gate = new();
    private readonly Dictionary<string, Workflow> workflows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Plan> plans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkflowTask> tasks = new(StringComparer.Ordinal);
    private readonly InMemoryTianShuCollaborationPlane collaborationPlane;

    public InMemoryTianShuWorkflowPlane(InMemoryTianShuCollaborationPlane collaborationPlane)
    {
        this.collaborationPlane = collaborationPlane ?? throw new ArgumentNullException(nameof(collaborationPlane));
    }

    public Workflow CreateWorkflow(CreateWorkflow command)
    {
        lock (gate)
        {
            var collaborationSpace = ResolveCollaborationSpaceRef(command.CollaborationSpaceId);
            var created = new Workflow(
                command.WorkflowId,
                collaborationSpace,
                command.DisplayName,
                WorkflowState.Draft,
                command.OwnerParticipant,
                command.ThreadId);
            workflows[command.WorkflowId.Value] = created;
            return created;
        }
    }

    public PlanProjection PublishPlan(PublishPlan command)
    {
        lock (gate)
        {
            EnsureWorkflowExists(command.WorkflowId);
            plans[command.WorkflowId.Value] = command.Plan;
            return new PlanProjection(command.WorkflowId, command.Plan);
        }
    }

    public WorkflowTask CreateTask(CreateTask command)
    {
        lock (gate)
        {
            EnsureWorkflowExists(command.Task.WorkflowId);
            tasks[command.Task.Id.Value] = command.Task;
            return command.Task;
        }
    }

    public WorkflowTask? UpdateTaskState(UpdateTaskState command)
    {
        lock (gate)
        {
            if (!tasks.TryGetValue(command.TaskId.Value, out var existing))
            {
                return null;
            }

            var updated = new WorkflowTask(
                existing.Id,
                existing.WorkflowId,
                existing.Title,
                command.State,
                command.OwnerParticipant ?? existing.OwnerParticipant,
                existing.OutputArtifact);
            tasks[command.TaskId.Value] = updated;
            return updated;
        }
    }

    public WorkflowBoardProjection? GetWorkflowBoardProjection(GetWorkflowBoard query)
    {
        lock (gate)
        {
            if (!workflows.TryGetValue(query.WorkflowId.Value, out var workflow))
            {
                return null;
            }

            var workflowTasks = tasks.Values.Where(task => task.WorkflowId == query.WorkflowId).ToArray();
            return new WorkflowBoardProjection(
                workflow.Id,
                workflow.DisplayName,
                workflow.CollaborationSpace,
                PendingTaskCount: workflowTasks.Count(task => task.State == TaskState.Todo || task.State == TaskState.Blocked),
                RunningTaskCount: workflowTasks.Count(task => task.State == TaskState.InProgress),
                CompletedTaskCount: workflowTasks.Count(task => task.State == TaskState.Done));
        }
    }

    public TaskBoardProjection? GetTaskBoardProjection(GetTaskBoard query)
    {
        lock (gate)
        {
            if (!workflows.ContainsKey(query.WorkflowId.Value))
            {
                return null;
            }

            var items = tasks.Values
                .Where(task => task.WorkflowId == query.WorkflowId)
                .OrderBy(static task => task.Title, StringComparer.Ordinal)
                .Select(static task => new TaskBoardItem(
                    task.Id,
                    task.Title,
                    ToTaskBoardState(task.State),
                    task.OwnerParticipant))
                .ToArray();
            return new TaskBoardProjection(query.WorkflowId, items);
        }
    }

    public PlanProjection? GetPlanProjection(GetPlanProjection query)
    {
        lock (gate)
        {
            return workflows.ContainsKey(query.WorkflowId.Value)
                   && plans.TryGetValue(query.WorkflowId.Value, out var plan)
                ? new PlanProjection(query.WorkflowId, plan)
                : null;
        }
    }

    private Workflow EnsureWorkflowExists(WorkflowId workflowId)
    {
        if (!workflows.TryGetValue(workflowId.Value, out var workflow))
        {
            throw new InvalidOperationException($"未找到工作流：{workflowId.Value}");
        }

        return workflow;
    }

    private CollaborationSpaceRef ResolveCollaborationSpaceRef(CollaborationSpaceId spaceId)
        => collaborationPlane.TryGetSpaceReference(spaceId)
           ?? new CollaborationSpaceRef(spaceId, spaceId.Value, spaceId.Value);

    private static string ToTaskBoardState(TaskState state)
        => state switch
        {
            TaskState.Todo => "todo",
            TaskState.InProgress => "in_progress",
            TaskState.Blocked => "blocked",
            TaskState.Done => "done",
            TaskState.Cancelled => "cancelled",
            _ => "todo",
        };
}
