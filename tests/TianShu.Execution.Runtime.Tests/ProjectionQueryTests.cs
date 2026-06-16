using System.Reflection;
using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Identity;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;
using TianShu.IdentityMemory;
using ControlPlaneListJobsQuery = TianShu.Contracts.Workflows.ControlPlaneListJobsQuery;

namespace TianShu.Execution.Runtime.Tests;

public sealed class ProjectionQueryTests
{
    [Fact]
    public async Task ProjectionQueries_WhenRuntimeEventsObserved_ReturnThreadTraceAndAttempts()
    {
        var runtime = new TianShuExecutionRuntime();
        var threadId = new ThreadId("thread-projection-runtime");
        var turnId = new TurnId("turn-projection-runtime");

        InvokePrivate(
            runtime,
            "RaiseEvent",
            new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.ThreadNameUpdated,
                ThreadId = threadId,
                Text = "Runtime Projection Thread",
            });
        InvokePrivate(
            runtime,
            "RaiseEvent",
            new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.TurnCompleted,
                ThreadId = threadId,
                TurnId = turnId,
                Status = "completed",
                Message = "turn completed",
            });

        var thread = await runtime.GetThreadProjectionAsync(new GetThreadProjection(threadId), CancellationToken.None);
        var trace = await runtime.GetExecutionTraceAsync(
            new GetExecutionTrace(new ExecutionTraceId("trace:turn-projection-runtime")),
            CancellationToken.None);
        var attempts = await runtime.ListAttemptSummariesAsync(
            new ListAttemptSummaries(new ExecutionId("execution:trace:turn-projection-runtime")),
            CancellationToken.None);

        Assert.NotNull(thread);
        Assert.Equal("Runtime Projection Thread", thread!.Title);
        Assert.Equal(turnId, thread.ActiveTurnId);
        Assert.True(thread.HasActiveTurn);
        Assert.NotNull(trace);
        Assert.NotEmpty(trace!.AuditTrail);
        Assert.Single(attempts);
        Assert.True(attempts[0].Succeeded);
    }

    [Fact]
    public async Task ProjectionQueries_WhenPendingGovernanceTracked_ReturnApprovalAndUserInputQueues()
    {
        var runtime = new TianShuExecutionRuntime();

        InvokePrivate(runtime, "TrackPendingApprovalProjection", "approval-runtime-001", "thread-runtime-001", "执行命令审批", "需要执行 shell 命令。");
        InvokePrivate(runtime, "TrackPendingUserInputProjection", "user-input-runtime-001", "thread-runtime-001", "请选择目标环境。");

        var approvals = await runtime.GetApprovalQueueProjectionAsync(new ListPendingApprovals(), CancellationToken.None);
        var userInputs = await runtime.ListUserInputRequestsAsync(new ListUserInputRequests(), CancellationToken.None);

        Assert.NotNull(approvals);
        var approval = Assert.Single(approvals!.Items);
        Assert.Equal("approval-runtime-001", approval.ApprovalId.Value);
        Assert.Equal("执行命令审批", approval.Title);
        Assert.Equal("需要执行 shell 命令。", approval.Reason);
        var userInput = Assert.Single(userInputs);
        Assert.Equal("user-input-runtime-001", userInput.Id.Value);
        Assert.Equal("请选择目标环境。", userInput.Prompt);
    }

    [Fact]
    public async Task ProjectionQueries_WhenMemoryCandidateNeedsReview_ReturnMemoryApprovalQueueItem()
    {
        var workspace = new MemorySpaceId("memory:workspace:d/gitrepos/personal/tianshu");
        var runtime = new TianShuExecutionRuntime(
            new PendingReviewIdentityMemoryPlane(
                new FactMemoryRecord(
                "preference.default",
                StructuredValue.FromString("用中文汇报"),
                    workspace,
                0.74m,
                    DateTimeOffset.UtcNow,
                    lifecycleStatus: MemoryLifecycleStatus.PendingReview,
                    sources:
                    [
                        new MemorySourceRef(
                            MemorySourceKind.Conversation,
                            "thread-memory-governance",
                            role: "user",
                            snippet: "以后默认用中文汇报")
                    ])));

        var approvals = await runtime.GetApprovalQueueProjectionAsync(new ListPendingApprovals(), CancellationToken.None);

        Assert.NotNull(approvals);
        var approval = Assert.Single(approvals!.Items, static item => item.ApprovalId.Value.StartsWith("memory-review:", StringComparison.Ordinal));
        Assert.Equal("记忆审核：preference.default", approval.Title);
        Assert.Equal("Approval", approval.CheckpointKind);
        Assert.Equal("policy_rule", approval.RiskSource);
        Assert.Equal("pending", approval.UserDecision);
        Assert.Equal("not_executed", approval.ExecutionResult);
        Assert.Contains("key=preference.default", approval.RequestContent);
        Assert.True(approval.Metadata.TryGetValue("domain", out var domain));
        Assert.Equal("memory", domain.GetString());
    }

    [Fact]
    public async Task ProjectionQueries_WhenLocalGovernanceEmpty_ShouldFallbackToAppHostRpc()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };
        SetPrivate(runtime, "stdin", writer);
        SetPrivate(runtime, "process", Process.GetCurrentProcess());

        var approvalsTask = runtime.GetApprovalQueueProjectionAsync(new ListPendingApprovals(), CancellationToken.None);
        var approvalRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            approvalRequestId,
            """
            {
              "items": [
                {
                  "approvalId": { "value": "approval-rpc-fallback" },
                  "title": "commandExecution 审批",
                  "reason": "需要执行远端 AppHost 中的 pending 命令。",
                  "requestedFrom": { "id": { "value": "tianshu-user" }, "kind": 0, "displayName": "TianShu User" },
                  "requestedAt": "2026-05-20T00:00:00+00:00"
                }
              ]
            }
            """);
        var approvals = await approvalsTask;

        var userInputsTask = runtime.ListUserInputRequestsAsync(new ListUserInputRequests(), CancellationToken.None);
        var userInputRequestId = await WaitForPendingResponseIdAsync(runtime, approvalRequestId);
        CompletePendingResponse(
            runtime,
            userInputRequestId,
            """
            [
              {
                "id": { "value": "user-input-rpc-fallback" },
                "prompt": "请选择远端 AppHost 中的目标环境。",
                "requestedFromParticipant": { "id": { "value": "tianshu-user" }, "kind": 0, "displayName": "TianShu User" },
                "status": 0,
                "requestedAt": "2026-05-20T00:00:00+00:00"
              }
            ]
            """);
        var userInputs = await userInputsTask;

        Assert.NotNull(approvals);
        Assert.Equal("approval-rpc-fallback", Assert.Single(approvals!.Items).ApprovalId.Value);
        Assert.Equal("user-input-rpc-fallback", Assert.Single(userInputs).Id.Value);

        var requests = await ReadRequestDocumentsAsync(stream);
        Assert.Contains(requests, static request => request.RootElement.GetProperty("method").GetString() == "governance/approvalQueue/read");
        Assert.Contains(requests, static request => request.RootElement.GetProperty("method").GetString() == "governance/userInputs/list");
        foreach (var request in requests)
        {
            request.Dispose();
        }
    }

    [Fact]
    public async Task ListAgentJobsAsync_WritesExpectedRpcAndParsesJobs()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };
        SetPrivate(runtime, "stdin", writer);
        SetPrivate(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ListAgentJobsAsync(
            new ControlPlaneListJobsQuery
            {
                Statuses = ["running"],
                Limit = 5,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "jobs": [
                {
                  "id": { "value": "job-rpc-list" },
                  "name": "active job",
                  "status": "running",
                  "instruction": "处理数据"
                }
              ]
            }
            """);

        var result = await pendingTask;

        var job = Assert.Single(result.Jobs);
        Assert.Equal("job-rpc-list", job.Id.Value);
        Assert.Equal("running", job.Status);
        using var request = Assert.Single(await ReadRequestDocumentsAsync(stream));
        Assert.Equal("agent/jobs/list", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("running", Assert.Single(parameters.GetProperty("statuses").EnumerateArray()).GetString());
        Assert.Equal(5, parameters.GetProperty("limit").GetInt32());
    }

    private static void InvokePrivate(TianShuExecutionRuntime runtime, string methodName, params object?[] args)
    {
        var method = typeof(TianShuExecutionRuntime).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(TianShuExecutionRuntime).FullName, methodName);
        _ = method.Invoke(runtime, args);
    }

    private static void SetPrivate(TianShuExecutionRuntime runtime, string fieldName, object? value)
    {
        var field = typeof(TianShuExecutionRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(TianShuExecutionRuntime).FullName, fieldName);
        field.SetValue(runtime, value);
    }

    private static async Task<long> WaitForPendingResponseIdAsync(TianShuExecutionRuntime runtime, params long[] excludedRequestIds)
    {
        var excluded = excludedRequestIds.Length == 0 ? null : excludedRequestIds.ToHashSet();
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var pendingResponses = (IEnumerable?)GetPrivate(runtime, "pendingResponses");
            Assert.NotNull(pendingResponses);

            foreach (var entry in pendingResponses!)
            {
                var keyProperty = entry.GetType().GetProperty("Key");
                Assert.NotNull(keyProperty);
                var requestId = (long)keyProperty!.GetValue(entry)!;
                if (excluded?.Contains(requestId) != true)
                {
                    return requestId;
                }
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("未捕获到待完成的 RPC 请求。");
    }

    private static void CompletePendingResponse(TianShuExecutionRuntime runtime, long requestId, string resultJson)
    {
        var pendingResponses = (IEnumerable?)GetPrivate(runtime, "pendingResponses");
        Assert.NotNull(pendingResponses);

        foreach (var entry in pendingResponses!)
        {
            var keyProperty = entry.GetType().GetProperty("Key");
            var valueProperty = entry.GetType().GetProperty("Value");
            Assert.NotNull(keyProperty);
            Assert.NotNull(valueProperty);
            if ((long)keyProperty!.GetValue(entry)! != requestId)
            {
                continue;
            }

            var completionSource = valueProperty!.GetValue(entry);
            Assert.NotNull(completionSource);
            var trySetResult = completionSource!.GetType().GetMethod("TrySetResult", [typeof(JsonElement)]);
            Assert.NotNull(trySetResult);
            using var document = JsonDocument.Parse(resultJson);
            var success = (bool)trySetResult!.Invoke(completionSource, [document.RootElement.Clone()])!;
            Assert.True(success);
            return;
        }

        throw new InvalidOperationException($"未找到待完成的 RPC 请求：{requestId}");
    }

    private static object? GetPrivate(TianShuExecutionRuntime runtime, string fieldName)
    {
        var field = typeof(TianShuExecutionRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(TianShuExecutionRuntime).FullName, fieldName);
        return field.GetValue(runtime);
    }

    private static async Task<IReadOnlyList<JsonDocument>> ReadRequestDocumentsAsync(MemoryStream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        return payload
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();
    }

    private sealed class PendingReviewIdentityMemoryPlane(FactMemoryRecord pendingReview) : ITianShuIdentityMemoryPlane
    {
        public Task<Account?> GetAccountProfileAsync(
            GetAccountProfile query,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult<Account?>(null);

        public Task<IReadOnlyList<DeviceBinding>> ListBoundDevicesAsync(
            ListBoundDevices query,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DeviceBinding>>(Array.Empty<DeviceBinding>());

        public Task<IReadOnlyList<MemoryProviderDescriptor>> ListMemoryProvidersAsync(
            ListMemoryProviders query,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MemoryProviderDescriptor>>(Array.Empty<MemoryProviderDescriptor>());

        public Task<IReadOnlyList<MemorySpace>> ListMemorySpacesAsync(
            ListMemorySpaces query,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MemorySpace>>(Array.Empty<MemorySpace>());

        public Task<MemoryOverlay> ResolveMemoryOverlayAsync(
            ResolveMemoryOverlay query,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryOverlay());

        public Task<MemoryQueryResult> FilterMemoryAsync(
            FilterMemory query,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(query.LifecycleStatus == MemoryLifecycleStatus.PendingReview
                ? new MemoryQueryResult([pendingReview])
                : new MemoryQueryResult());

        public Task<MemoryReviewQueryResult> ListMemoryReviewsAsync(
            ListMemoryReviews query,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(query.LifecycleStatus is null or MemoryLifecycleStatus.PendingReview
                ? new MemoryReviewQueryResult([new MemoryReviewItem(pendingReview)])
                : new MemoryReviewQueryResult());

        public Task<MemoryMutationResult> AddMemoryAsync(
            AddMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false));

        public Task<IReadOnlyList<MemoryCandidate>> ExtractMemoryAsync(
            ExtractMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MemoryCandidate>>(Array.Empty<MemoryCandidate>());

        public Task<MemoryMutationResult> ImportMemoryAsync(
            ImportMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false));

        public Task<MemoryQueryResult> ExportMemoryAsync(
            ExportMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryQueryResult());

        public Task<MemoryMutationResult> BindMemoryProviderAsync(
            BindMemoryProvider command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false));

        public Task<MemoryConsolidationRunResult> RunMemoryConsolidationAsync(
            RunMemoryConsolidation command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryConsolidationRunResult(0, 0));

        public Task<MemoryMutationResult> ForgetMemoryAsync(
            ForgetMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false));

        public Task<MemoryMutationResult> DeleteMemoryAsync(
            DeleteMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false));

        public Task<MemoryMutationResult> SupersedeMemoryAsync(
            SupersedeMemory command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false));

        public Task<MemoryMutationResult> ApproveMemoryReviewAsync(
            ApproveMemoryReview command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false));

        public Task<MemoryMutationResult> DemoteMemoryReviewAsync(
            DemoteMemoryReview command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false));

        public Task<MemoryMutationResult> MergeMemoryReviewAsync(
            MergeMemoryReview command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false));

        public Task<MemoryMutationResult> RestoreMemoryReviewAsync(
            RestoreMemoryReview command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false));

        public Task<MemoryMutationResult> RecordMemoryFeedbackAsync(
            RecordMemoryFeedback command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false));

        public Task<MemoryMutationResult> RecordMemoryCitationAsync(
            RecordMemoryCitation command,
            TianShuIdentityMemoryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(false));
    }
}
