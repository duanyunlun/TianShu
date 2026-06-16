using TianShu.Contracts.Workflows;
using TianShu.Contracts.Projections;

namespace TianShu.Execution.Runtime;

public interface IWorkflowControlPlaneClient
{
    Task<Workflow> CreateWorkflowAsync(CreateWorkflow request, CancellationToken cancellationToken);

    Task<PlanProjection> PublishPlanAsync(PublishPlan request, CancellationToken cancellationToken);

    Task<TianShu.Contracts.Workflows.Task> CreateTaskAsync(CreateTask request, CancellationToken cancellationToken);

    Task<TianShu.Contracts.Workflows.Task?> UpdateTaskStateAsync(UpdateTaskState request, CancellationToken cancellationToken);

    Task<WorkflowBoardProjection?> GetWorkflowBoardProjectionAsync(GetWorkflowBoard request, CancellationToken cancellationToken);

    Task<TaskBoardProjection?> GetTaskBoardProjectionAsync(GetTaskBoard request, CancellationToken cancellationToken);

    Task<PlanProjection?> GetPlanProjectionAsync(GetPlanProjection request, CancellationToken cancellationToken);

    Task<ControlPlaneReviewStartResult> StartReviewAsync(ControlPlaneReviewStartCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneJobOperationResult> CreateAgentJobAsync(ControlPlaneCreateJobCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneJobOperationResult> DispatchAgentJobAsync(ControlPlaneDispatchJobCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneJobOperationResult> ReportAgentJobItemAsync(ControlPlaneReportJobItemCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneJobOperationResult> ReadAgentJobAsync(ControlPlaneReadJobQuery request, CancellationToken cancellationToken);

    Task<ControlPlaneJobListResult> ListAgentJobsAsync(ControlPlaneListJobsQuery request, CancellationToken cancellationToken);
}
