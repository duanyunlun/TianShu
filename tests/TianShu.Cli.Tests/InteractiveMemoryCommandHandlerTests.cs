using System.Text.Json;
using TianShu.Cli.Interaction.Commands.Memory;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Tests;

public sealed class InteractiveMemoryCommandHandlerTests
{
    [Fact]
    public async Task HandleMemoryCommandAsync_AddWithoutSpace_UsesDefaultWritableWorkspace()
    {
        var workspaceSpaceId = new MemorySpaceId("memory:workspace:tianshu");
        var runtime = new CliConsumerFakeRuntime
        {
            ListMemorySpacesAsyncHandler = static (_, _) => Task.FromResult<IReadOnlyList<MemorySpace>>(
            [
                new MemorySpace(new MemorySpaceId("memory:team:platform"), MemoryScopeKind.Team, "platform", "Team Memory", isReadOnly: true),
                new MemorySpace(new MemorySpaceId("memory:user:semi"), MemoryScopeKind.User, "semi", "User Memory"),
                new MemorySpace(new MemorySpaceId("memory:workspace:tianshu"), MemoryScopeKind.Workspace, "tianshu", "Workspace Memory"),
            ])
        };
        var output = new List<(string Text, bool IsError)>();
        var handler = new InteractiveMemoryCommandHandler();

        await handler.HandleMemoryCommandAsync(
            runtime,
            "add --key preference.shell --value pwsh --confidence 0.8",
            Context(output),
            CancellationToken.None);

        var command = Assert.Single(runtime.MemoryAddRequests);
        Assert.Equal(workspaceSpaceId, command.MemorySpaceId);
        Assert.Equal("preference.shell", command.Key);
        Assert.Equal("pwsh", command.Value.StringValue);
        Assert.Equal(0.8m, command.Confidence);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("\"success\":true", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleMemoryCommandAsync_Consolidate_CallsFormalConsolidationCommand()
    {
        var runtime = new CliConsumerFakeRuntime
        {
            RunMemoryConsolidationAsyncHandler = static (_, _) => Task.FromResult(new MemoryConsolidationRunResult(2, 1, LeaseAcquired: false))
        };
        var output = new List<(string Text, bool IsError)>();
        var handler = new InteractiveMemoryCommandHandler();

        await handler.HandleMemoryCommandAsync(
            runtime,
            "consolidate --payload-json {\"memorySpaceId\":{\"value\":\"space-review\"},\"enableLease\":false}",
            Context(output),
            CancellationToken.None);

        var command = Assert.Single(runtime.MemoryConsolidationRequests);
        Assert.Equal(new MemorySpaceId("space-review"), command.MemorySpaceId);
        Assert.False(command.EnableLease);
        var line = Assert.Single(output);
        Assert.False(line.IsError);
        Assert.Contains("\"proposalsCreated\":1", line.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleMemoryCommandAsync_ReviewList_WritesReadablePendingReviewSummary()
    {
        var recordId = new MemoryRecordId("memory-review-1");
        var spaceId = new MemorySpaceId("space-review");
        var runtime = new CliConsumerFakeRuntime
        {
            ListMemoryReviewsAsyncHandler = static (query, _) => Task.FromResult(new MemoryReviewQueryResult(
                [
                    new MemoryReviewItem(
                        new FactMemoryRecord(
                            "preference.language",
                            StructuredValue.FromString("中文优先"),
                            new MemorySpaceId("space-review"),
                            0.82m,
                            id: new MemoryRecordId("memory-review-1"),
                            lifecycleStatus: MemoryLifecycleStatus.PendingReview,
                            sources: [new MemorySourceRef(MemorySourceKind.Conversation, "turn-1")],
                            usageCount: 3,
                            formationPath: MemoryFormationPath.ExploratoryLearning,
                            isCounterexample: true),
                        Audit:
                        [
                            new MemoryReviewAuditSummary(
                                "extract_memory",
                                MemoryMutationEffect.Upserted,
                                "tester",
                                "conversation",
                                DateTimeOffset.UtcNow)
                        ])
                ],
                degradedProviders: ["provider-degraded"]))
        };
        var output = new List<(string Text, bool IsError)>();
        var handler = new InteractiveMemoryCommandHandler();

        await handler.HandleMemoryCommandAsync(
            runtime,
            "review",
            Context(output),
            CancellationToken.None);

        var query = Assert.Single(runtime.MemoryReviewListRequests);
        Assert.Equal(MemoryLifecycleStatus.PendingReview, query.LifecycleStatus);
        Assert.Null(query.MemorySpaceId);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("待审记忆：1 项", StringComparison.Ordinal));
        Assert.Contains(output, line => line.Text.Contains(recordId.Value, StringComparison.Ordinal));
        Assert.Contains(output, line => line.Text.Contains(spaceId.Value, StringComparison.Ordinal));
        Assert.Contains(output, static line => line.Text.Contains("preference.language", StringComparison.Ordinal));
        Assert.Contains(output, static line => line.Text.Contains("中文优先", StringComparison.Ordinal));
        Assert.Contains(output, static line => line.Text.Contains("provider-degraded", StringComparison.Ordinal));
        Assert.Contains(output, static line => line.Text.Contains("ExploratoryLearning", StringComparison.Ordinal));
        Assert.Contains(output, static line => line.Text.Contains("counterexample: yes", StringComparison.Ordinal));
        Assert.Contains(output, static line => line.Text.Contains("extract_memory", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleMemoryCommandAsync_ReviewList_WhenJsonFlagIsFirstOption_WritesJsonResult()
    {
        var runtime = new CliConsumerFakeRuntime
        {
            ListMemoryReviewsAsyncHandler = static (_, _) => Task.FromResult(new MemoryReviewQueryResult(
                [
                    new MemoryReviewItem(
                        new FactMemoryRecord(
                            "preference.language",
                            StructuredValue.FromString("中文优先"),
                            new MemorySpaceId("space-review"),
                            id: new MemoryRecordId("memory-review-json"),
                            lifecycleStatus: MemoryLifecycleStatus.PendingReview))
                ]))
        };
        var output = new List<(string Text, bool IsError)>();
        var handler = new InteractiveMemoryCommandHandler();

        await handler.HandleMemoryCommandAsync(
            runtime,
            "review --json",
            Context(output),
            CancellationToken.None);

        var query = Assert.Single(runtime.MemoryReviewListRequests);
        Assert.Equal(MemoryLifecycleStatus.PendingReview, query.LifecycleStatus);
        var line = Assert.Single(output);
        Assert.False(line.IsError);
        Assert.Contains("\"items\"", line.Text, StringComparison.Ordinal);
        Assert.Contains("memory-review-json", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleMemoryCommandAsync_ReviewReject_ResolvesRecordAndCallsForget()
    {
        var recordId = new MemoryRecordId("memory-review-reject");
        var spaceId = new MemorySpaceId("space-review");
        var runtime = new CliConsumerFakeRuntime
        {
            ListMemoryReviewsAsyncHandler = static (_, _) => Task.FromResult(new MemoryReviewQueryResult(
                [
                    new MemoryReviewItem(
                        new FactMemoryRecord(
                            "preference.shell",
                            StructuredValue.FromString("pwsh"),
                            new MemorySpaceId("space-review"),
                            id: new MemoryRecordId("memory-review-reject"),
                            lifecycleStatus: MemoryLifecycleStatus.PendingReview))
                ]))
        };
        var output = new List<(string Text, bool IsError)>();
        var handler = new InteractiveMemoryCommandHandler();

        await handler.HandleMemoryCommandAsync(
            runtime,
            $"review reject {recordId.Value}",
            Context(output),
            CancellationToken.None);

        var command = Assert.Single(runtime.MemoryForgetRequests);
        Assert.Equal(recordId, command.MemoryRecordId);
        Assert.Equal(spaceId, command.MemorySpaceId);
        Assert.Equal("preference.shell", command.Key);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("已拒绝待审记忆", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleMemoryCommandAsync_ReviewFeedback_RecordsFeedbackWithoutMutatingFact()
    {
        var recordId = new MemoryRecordId("memory-review-feedback");
        var runtime = new CliConsumerFakeRuntime();
        var output = new List<(string Text, bool IsError)>();
        var handler = new InteractiveMemoryCommandHandler();

        await handler.HandleMemoryCommandAsync(
            runtime,
            $"review feedback {recordId.Value} --decision ignored --feedback 这是错误反例",
            Context(output),
            CancellationToken.None);

        var command = Assert.Single(runtime.MemoryFeedbackRequests);
        Assert.Equal(recordId, command.MemoryRecordId);
        Assert.Equal(MemoryMergeDecision.Ignored, command.Decision);
        Assert.Equal("这是错误反例", command.Feedback);
        Assert.Empty(runtime.MemoryForgetRequests);
        Assert.Empty(runtime.MemoryDeleteRequests);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("已记录待审记忆反馈", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleMemoryCommandAsync_ReviewApprove_ResolvesRecordAndCallsApprove()
    {
        var recordId = new MemoryRecordId("memory-review-approve");
        var spaceId = new MemorySpaceId("space-review");
        var runtime = new CliConsumerFakeRuntime
        {
            ListMemoryReviewsAsyncHandler = static (_, _) => Task.FromResult(new MemoryReviewQueryResult(
                [
                    new MemoryReviewItem(
                        new FactMemoryRecord(
                            "preference.editor",
                            StructuredValue.FromString("vim"),
                            new MemorySpaceId("space-review"),
                            id: new MemoryRecordId("memory-review-approve"),
                            lifecycleStatus: MemoryLifecycleStatus.PendingReview))
                ]))
        };
        var output = new List<(string Text, bool IsError)>();
        var handler = new InteractiveMemoryCommandHandler();

        await handler.HandleMemoryCommandAsync(
            runtime,
            $"review approve {recordId.Value}",
            Context(output),
            CancellationToken.None);

        var query = Assert.Single(runtime.MemoryReviewListRequests);
        Assert.Equal(MemoryLifecycleStatus.PendingReview, query.LifecycleStatus);
        var command = Assert.Single(runtime.MemoryApproveReviewRequests);
        Assert.Equal(recordId, command.MemoryRecordId);
        Assert.Equal(spaceId, command.MemorySpaceId);
        Assert.Equal("preference.editor", command.Key);
        Assert.Empty(runtime.MemoryForgetRequests);
        Assert.Empty(runtime.MemoryDeleteRequests);
        Assert.Empty(runtime.MemoryFeedbackRequests);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("已批准待审记忆", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleMemoryCommandAsync_ReviewDemote_CallsFormalDemoteCommand()
    {
        var recordId = new MemoryRecordId("memory-review-demote");
        var runtime = new CliConsumerFakeRuntime
        {
            ListMemoryReviewsAsyncHandler = static (_, _) => Task.FromResult(new MemoryReviewQueryResult(
                [
                    new MemoryReviewItem(
                        new FactMemoryRecord(
                            "preference.editor",
                            StructuredValue.FromString("vim"),
                            new MemorySpaceId("space-review"),
                            id: new MemoryRecordId("memory-review-demote"),
                            lifecycleStatus: MemoryLifecycleStatus.PendingReview))
                ]))
        };
        var output = new List<(string Text, bool IsError)>();
        var handler = new InteractiveMemoryCommandHandler();

        await handler.HandleMemoryCommandAsync(
            runtime,
            $"review demote {recordId.Value} --reason 证据不足",
            Context(output),
            CancellationToken.None);

        var command = Assert.Single(runtime.MemoryDemoteReviewRequests);
        Assert.Equal(recordId, command.MemoryRecordId);
        Assert.Equal(new MemorySpaceId("space-review"), command.MemorySpaceId);
        Assert.Equal("preference.editor", command.Key);
        Assert.Equal("证据不足", command.Reason);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("已降权待审记忆", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleMemoryCommandAsync_ReviewRestore_CallsFormalRestoreCommand()
    {
        var recordId = new MemoryRecordId("memory-review-restore");
        var spaceId = new MemorySpaceId("space-review");
        var runtime = new CliConsumerFakeRuntime();
        var output = new List<(string Text, bool IsError)>();
        var handler = new InteractiveMemoryCommandHandler();

        await handler.HandleMemoryCommandAsync(
            runtime,
            $"review restore {recordId.Value} --memory-space-id {spaceId.Value} --key preference.editor --reason 重新审核",
            Context(output),
            CancellationToken.None);

        var command = Assert.Single(runtime.MemoryRestoreReviewRequests);
        Assert.Equal(recordId, command.MemoryRecordId);
        Assert.Equal(spaceId, command.MemorySpaceId);
        Assert.Equal("preference.editor", command.Key);
        Assert.Equal("重新审核", command.Reason);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("已恢复待审记忆", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleMemoryCommandAsync_ReviewMerge_CallsFormalMergeCommand()
    {
        var runtime = new CliConsumerFakeRuntime();
        var output = new List<(string Text, bool IsError)>();
        var handler = new InteractiveMemoryCommandHandler();

        await handler.HandleMemoryCommandAsync(
            runtime,
            "review merge memory-review-merge --target-record-id active-record-1 --memory-space-id space-review --reason 合并为现有偏好 --merged-key preference.editor",
            Context(output),
            CancellationToken.None);

        var command = Assert.Single(runtime.MemoryMergeReviewRequests);
        Assert.Equal(new MemoryRecordId("memory-review-merge"), command.ReviewRecordId);
        Assert.Equal(new MemoryRecordId("active-record-1"), command.TargetRecordId);
        Assert.Equal(new MemorySpaceId("space-review"), command.MemorySpaceId);
        Assert.Equal("合并为现有偏好", command.Reason);
        Assert.Equal("preference.editor", command.MergedKey);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("已合并待审记忆", StringComparison.Ordinal));
    }

    private static InteractiveMemoryCommandContext Context(List<(string Text, bool IsError)> output)
        => new(new JsonSerializerOptions(JsonSerializerDefaults.Web), (text, isError) => output.Add((text, isError)));
}
