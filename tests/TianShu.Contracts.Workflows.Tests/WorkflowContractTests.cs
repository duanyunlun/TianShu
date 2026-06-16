using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Workflows;

namespace TianShu.Contracts.Workflows.Tests;

public sealed class WorkflowContractTests
{
    [Fact]
    public void PlanStep_RejectsNegativeOrder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanStep(-1, "设计"));
    }

    [Fact]
    public void Workflow_PreservesOwnerAndCollaboration()
    {
        var space = new CollaborationSpace(
            new CollaborationSpaceId("space-workflow"),
            "design",
            "Design",
            new CollaborationSpaceProfile("工作流"),
            CollaborationDefaultSet.Empty);
        var participant = new ServiceParticipant(
            new ParticipantId("participant-workflow"),
            "Coordinator",
            "owner");
        var workflow = new Workflow(
            new WorkflowId("workflow-001"),
            CollaborationSpaceRef.From(space),
            "Contracts Workflow",
            WorkflowState.Active,
            ParticipantRef.From(participant));

        Assert.Equal("Contracts Workflow", workflow.DisplayName);
        Assert.Equal("participant-workflow", workflow.OwnerParticipant?.Id.Value);
        Assert.Equal(WorkflowState.Active, workflow.State);
    }

    [Fact]
    public void ControlPlaneCreateJobCommand_PreservesStructuredItems()
    {
        var command = new ControlPlaneCreateJobCommand
        {
            JobId = new JobId("job-001"),
            Name = "批量导出",
            Instruction = "生成批处理任务",
            InputHeaders = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["name"] = StructuredValue.FromString("标题"),
            }),
            OutputSchema = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["type"] = StructuredValue.FromString("object"),
            }),
            Items =
            [
                StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["id"] = StructuredValue.FromString("item-001"),
                    ["title"] = StructuredValue.FromString("第一项"),
                }),
            ],
        };

        Assert.Equal("job-001", command.JobId?.Value);
        Assert.Equal("批量导出", command.Name);
        Assert.Equal("生成批处理任务", command.Instruction);
        Assert.Equal("标题", command.InputHeaders?.Properties["name"].StringValue);
        Assert.Equal("object", command.OutputSchema?.Properties["type"].StringValue);
        Assert.Equal("第一项", command.Items[0].Properties["title"].StringValue);
    }

    [Fact]
    public void ControlPlaneJobOperationResult_PreservesJobAndItemDetails()
    {
        var result = new ControlPlaneJobOperationResult
        {
            Job = new ControlPlaneJobDetails
            {
                Id = new JobId("job-002"),
                Name = "批量派发",
                Status = "running",
                Instruction = "dispatch",
            },
            Items =
            [
                new ControlPlaneJobItemDetails
                {
                    ItemId = new JobItemId("job-item-001"),
                    SourceId = "csv-row-1",
                    ThreadId = new ThreadId("thread-001"),
                    AssignedThreadId = new ThreadId("thread-002"),
                    Status = "completed",
                    Result = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["ok"] = StructuredValue.FromBoolean(true),
                    }),
                },
            ],
        };

        Assert.Equal("job-002", result.Job?.Id.Value);
        var item = Assert.Single(result.Items);
        Assert.Equal("job-item-001", item.ItemId.Value);
        Assert.Equal("thread-001", item.ThreadId?.Value);
        Assert.Equal("thread-002", item.AssignedThreadId?.Value);
        Assert.True(item.Result?.Properties["ok"].BooleanValue);
    }

    [Fact]
    public void ControlPlaneJobListResult_PreservesActiveJobs()
    {
        var query = new ControlPlaneListJobsQuery
        {
            Statuses = ["pending", "running"],
            Limit = 10,
        };
        var result = new ControlPlaneJobListResult
        {
            Jobs =
            [
                new ControlPlaneJobDetails
                {
                    Id = new JobId("job-active-001"),
                    Name = "运行中作业",
                    Status = "running",
                    Instruction = "dispatch",
                },
            ],
        };

        Assert.Equal(["pending", "running"], query.Statuses);
        Assert.Equal(10, query.Limit);
        var job = Assert.Single(result.Jobs);
        Assert.Equal("job-active-001", job.Id.Value);
        Assert.Equal("running", job.Status);
    }
}
