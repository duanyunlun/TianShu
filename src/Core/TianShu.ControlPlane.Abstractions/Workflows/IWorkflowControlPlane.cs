using ControlPlaneCreateJobCommand = TianShu.Contracts.Workflows.ControlPlaneCreateJobCommand;
using ControlPlaneDispatchJobCommand = TianShu.Contracts.Workflows.ControlPlaneDispatchJobCommand;
using ControlPlaneJobListResult = TianShu.Contracts.Workflows.ControlPlaneJobListResult;
using ControlPlaneJobOperationResult = TianShu.Contracts.Workflows.ControlPlaneJobOperationResult;
using ControlPlaneListJobsQuery = TianShu.Contracts.Workflows.ControlPlaneListJobsQuery;
using ControlPlaneReportJobItemCommand = TianShu.Contracts.Workflows.ControlPlaneReportJobItemCommand;
using ControlPlaneReadJobQuery = TianShu.Contracts.Workflows.ControlPlaneReadJobQuery;
using ControlPlaneReviewStartCommand = TianShu.Contracts.Workflows.ControlPlaneReviewStartCommand;
using ControlPlaneReviewStartResult = TianShu.Contracts.Workflows.ControlPlaneReviewStartResult;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Workflows;

namespace TianShu.ControlPlane.Abstractions.Workflows;

public interface IWorkflowControlPlane
{
    Task<Workflow> CreateWorkflowAsync(CreateWorkflow command, CancellationToken cancellationToken);

    Task<PlanProjection> PublishPlanAsync(PublishPlan command, CancellationToken cancellationToken);

    Task<TianShu.Contracts.Workflows.Task> CreateTaskAsync(CreateTask command, CancellationToken cancellationToken);

    Task<TianShu.Contracts.Workflows.Task?> UpdateTaskStateAsync(UpdateTaskState command, CancellationToken cancellationToken);

    Task<WorkflowBoardProjection?> GetWorkflowBoardProjectionAsync(GetWorkflowBoard query, CancellationToken cancellationToken);

    Task<TaskBoardProjection?> GetTaskBoardProjectionAsync(GetTaskBoard query, CancellationToken cancellationToken);

    Task<PlanProjection?> GetPlanProjectionAsync(GetPlanProjection query, CancellationToken cancellationToken);

    Task<ControlPlaneReviewStartResult> StartReviewAsync(ControlPlaneReviewStartCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneJobOperationResult> CreateJobAsync(ControlPlaneCreateJobCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneJobOperationResult> DispatchJobAsync(ControlPlaneDispatchJobCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneJobOperationResult> ReportJobItemAsync(ControlPlaneReportJobItemCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneJobOperationResult> ReadJobAsync(ControlPlaneReadJobQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneJobListResult> ListJobsAsync(ControlPlaneListJobsQuery query, CancellationToken cancellationToken);
}
