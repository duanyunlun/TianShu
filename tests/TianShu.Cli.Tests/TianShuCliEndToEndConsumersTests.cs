using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using static TianShu.Cli.Tests.CliConsumerFakeRuntime;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Identity;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Tools;
using TianShu.Contracts.Workflows;
using TianShu.Cli.Interaction.Orchestration;
using AgentRosterEntry = TianShu.Contracts.Projections.AgentRosterEntry;
using AgentRosterProjection = TianShu.Contracts.Projections.AgentRosterProjection;
using ArtifactCollectionProjection = TianShu.Contracts.Projections.ArtifactCollectionProjection;
using ArtifactProjection = TianShu.Contracts.Projections.ArtifactProjection;
using TaskBoardProjection = TianShu.Contracts.Projections.TaskBoardProjection;
using WorkflowBoardProjection = TianShu.Contracts.Projections.WorkflowBoardProjection;
using TianShu.ControlPlane;
using TianShu.Execution.Runtime.Models;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Events;
using Task = System.Threading.Tasks.Task;

namespace TianShu.Cli.Tests;

[Collection("ConsoleCapture")]
public sealed class TianShuCliEndToEndConsumersTests
{
    private static readonly Assembly CliAssembly = ReflectionTestHelper.LoadRequiredAssembly("TianShu.Cli");

    private static StructuredInteractionItem AssertCliGovernanceEnvelope(
        InteractionEnvelope? envelope,
        string expectedId,
        string expectedSemanticKind)
    {
        Assert.NotNull(envelope);
        Assert.Equal(expectedId, envelope!.Id.Value);
        Assert.Equal(InteractionSourceKind.Host, envelope.Source.Kind);
        Assert.Equal("cli", envelope.Source.Surface);
        Assert.Equal(expectedSemanticKind, envelope.RoutingHint?.Intent);
        Assert.Equal("cli", envelope.RoutingHint?.Surface);
        var item = Assert.IsType<StructuredInteractionItem>(Assert.Single(envelope.Items));
        Assert.Equal(expectedSemanticKind, item.SemanticKind);
        return item;
    }

    private static IReadOnlyList<InteractionItem> AssertCliConversationEnvelope(
        InteractionEnvelope? envelope,
        string expectedIdPrefix,
        string expectedIntent)
    {
        Assert.NotNull(envelope);
        Assert.StartsWith(expectedIdPrefix, envelope!.Id.Value, StringComparison.Ordinal);
        Assert.Equal(InteractionSourceKind.Host, envelope.Source.Kind);
        Assert.Equal("cli", envelope.Source.Surface);
        Assert.Equal(expectedIntent, envelope.RoutingHint?.Intent);
        Assert.Equal("cli", envelope.RoutingHint?.Surface);
        return envelope.Items;
    }
    private static readonly string CodexInitPrompt = string.Join('\n',
    [
        "Generate a file named AGENTS.md that serves as a contributor guide for this repository.",
        "Your goal is to produce a clear, concise, and well-structured document with descriptive headings and actionable explanations for each section.",
        "Follow the outline below, but adapt as needed — add sections if relevant, and omit those that do not apply to this project.",
        string.Empty,
        "Document Requirements",
        string.Empty,
        "- Title the document \"Repository Guidelines\".",
        "- Use Markdown headings (#, ##, etc.) for structure.",
        "- Keep the document concise. 200-400 words is optimal.",
        "- Keep explanations short, direct, and specific to this repository.",
        "- Provide examples where helpful (commands, directory paths, naming patterns).",
        "- Maintain a professional, instructional tone.",
        string.Empty,
        "Recommended Sections",
        string.Empty,
        "Project Structure & Module Organization",
        string.Empty,
        "- Outline the project structure, including where the source code, tests, and assets are located.",
        string.Empty,
        "Build, Test, and Development Commands",
        string.Empty,
        "- List key commands for building, testing, and running locally (e.g., npm test, make build).",
        "- Briefly explain what each command does.",
        string.Empty,
        "Coding Style & Naming Conventions",
        string.Empty,
        "- Specify indentation rules, language-specific style preferences, and naming patterns.",
        "- Include any formatting or linting tools used.",
        string.Empty,
        "Testing Guidelines",
        string.Empty,
        "- Identify testing frameworks and coverage requirements.",
        "- State test naming conventions and how to run tests.",
        string.Empty,
        "Commit & Pull Request Guidelines",
        string.Empty,
        "- Summarize commit message conventions found in the project’s Git history.",
        "- Outline pull request requirements (descriptions, linked issues, screenshots, etc.).",
        string.Empty,
        "(Optional) Add other sections if relevant, such as Security & Configuration Tips, Architecture Overview, or Agent-Specific Instructions.",
    ]);

    [Fact]
    public void CliConsumerFakeRuntime_ShouldOnlyExposeTypedStreamEmitSurface()
    {
        var agentStreamEmit = typeof(CliConsumerFakeRuntime)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(static method =>
                string.Equals(method.Name, "Emit", StringComparison.Ordinal)
                && method.GetParameters() is [{ ParameterType: not null } parameters]
                && parameters.ParameterType == typeof(AgentStreamEvent));

        Assert.Null(agentStreamEmit);
    }

    private static void EmitProjectedStreamEvent(CliConsumerFakeRuntime runtime, AgentStreamEvent streamEvent)
        => runtime.Emit(ControlPlaneConversationStreamEventCompatibility.ToControlPlaneConversationStreamEvent(streamEvent));

    [Fact]
    public async Task CliRuntimeCommandRunner_CoversDedicatedThreadInterfaces()
    {
        using var workspace = new CliConsumerWorkspace();

        var listRuntime = new CliConsumerFakeRuntime();
        listRuntime.ListThreadsRequestAsyncHandler = static (request, _) =>
            Task.FromResult(
                new AgentThreadListResult
                {
                    Data =
                    [
                        new AgentThreadInfo
                        {
                            ThreadId = "thread-list-001",
                            Preview = "列表验证",
                            Name = "配置 GUI 线程",
                            Cwd = "D:/Work/TianShu",
                            UpdatedAt = new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero),
                        },
                    ],
                    NextCursor = "cursor-thread-002",
                });
        var (listCode, listOut, _) = await RunThreadAsync(
            workspace,
            listRuntime,
            "list",
            "--limit",
            "5",
            "--sort-key",
            "updated_at",
            "--model-provider",
            "anthropic",
            "--model-provider",
            "openai",
            "--source-kind",
            "subAgentReview",
            "--search-term",
            "配置");
        Assert.Equal(0, listCode);
        Assert.Contains("配置 GUI 线程", listOut, StringComparison.Ordinal);
        Assert.Contains("nextCursor\tcursor-thread-002", listOut, StringComparison.Ordinal);
        Assert.Collection(
            listRuntime.ThreadListRequestCalls,
            call =>
            {
                Assert.Equal(5, call.Limit);
                Assert.Equal("updated_at", call.SortKey);
                Assert.Equal(["anthropic", "openai"], call.ModelProviders);
                Assert.Equal(
                    new[]
                    {
                        ControlPlaneThreadSourceKind.SubAgentReview,
                    },
                    call.SourceKinds);
                Assert.Equal("配置", call.SearchTerm);
                Assert.Equal(workspace.RootPath, call.WorkingDirectory);
            });

        var startRuntime = new CliConsumerFakeRuntime();
        startRuntime.StartNewThreadRequestAsyncHandler = static (request, _) => Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
        {
            ThreadId = "thread-new-001",
            Preview = request.ModelProvider ?? "新线程",
            Cwd = "D:/Work/TianShu",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var (startCode, startOut, _) = await RunThreadAsync(
            workspace,
            startRuntime,
            "start",
            "--thread-model-provider",
            "anthropic",
            "--thread-experimental-raw-events",
            "true",
            "--thread-dynamic-tools-json",
            "[{\"name\":\"task_lookup\",\"description\":\"查询任务\",\"inputSchema\":{\"type\":\"object\"}}]");
        Assert.Equal(0, startCode);
        Assert.Contains("已创建新线程。", startOut, StringComparison.Ordinal);
        Assert.Empty(startRuntime.StartThreadCalls);
        Assert.Collection(
            startRuntime.StartThreadRequestCalls,
            call =>
            {
                Assert.Equal("anthropic", call.ModelProvider);
                Assert.True(call.ExperimentalRawEvents);
                Assert.Single(call.DynamicTools!);
            });

        var forkRuntime = new CliConsumerFakeRuntime();
        forkRuntime.ForkThreadAsyncHandler = static (threadId, _) => Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
        {
            ThreadId = $"{threadId}-fork",
            Preview = "分叉预览",
            Name = "配置 GUI 分叉线程",
            Cwd = "D:/Work/TianShu",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var (forkCode, forkOut, _) = await RunThreadAsync(workspace, forkRuntime, "fork", "--thread-id", "thread-fork-001");
        Assert.Equal(0, forkCode);
        Assert.Contains("已分叉线程。", forkOut, StringComparison.Ordinal);
        Assert.Single(forkRuntime.ForkThreadCalls);

        var compactRuntime = new CliConsumerFakeRuntime();
        compactRuntime.CompactThreadAsyncHandler = static (_, _, _) => Task.FromResult(new ControlPlaneThreadCommandAcceptedResult());
        var (compactCode, compactOut, _) = await RunThreadAsync(workspace, compactRuntime, "compact", "--thread-id", "thread-compact-001", "--keep-recent-turns", "3");
        Assert.Equal(0, compactCode);
        Assert.Contains("已启动线程压缩。", compactOut, StringComparison.Ordinal);
        Assert.Collection(
            compactRuntime.CompactThreadCalls,
            call =>
            {
                Assert.Equal("thread-compact-001", call.ThreadId);
                Assert.Equal(3, call.KeepRecentTurns);
            });

        var resumeRuntime = new CliConsumerFakeRuntime();
        resumeRuntime.ResumeThreadRequestAsyncHandler = static (request, _) => Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
        {
            ThreadId = request.ThreadId.Value,
            Preview = "恢复预览",
            Name = "配置 GUI 恢复线程",
            Cwd = "D:/Work/TianShu",
            SeedHistory = [new AgentThreadSeedHistoryItem { Role = "user", Content = "hello" }],
            Turns = [new AgentThreadTurn { Id = "turn-resume-001", Status = "completed" }],
        });
        var (resumeCode, resumeOut, _) = await RunThreadAsync(workspace, resumeRuntime, "resume", "--thread-id", "thread-resume-001");
        Assert.Equal(0, resumeCode);
        Assert.Contains("标题：配置 GUI 恢复线程", resumeOut, StringComparison.Ordinal);
        Assert.Contains("种子历史：1", resumeOut, StringComparison.Ordinal);
        Assert.Contains("回合数：1", resumeOut, StringComparison.Ordinal);
        Assert.Empty(resumeRuntime.ResumeThreadCalls);
        Assert.Collection(
            resumeRuntime.ResumeThreadRequestCalls,
            call => Assert.Equal("thread-resume-001", call.ThreadId.Value));
    }

    [Fact]
    public async Task CliRuntimeCommandRunner_ThreadResume_ReplaysPendingInteractiveRequests_AndRestoresPendingFollowUps()
    {
        using var workspace = new CliConsumerWorkspace();
        var permissionsPath = WriteUtf8File(workspace.RootPath, "thread-resume-permissions.json", "{\"requests\":{\"permission-thread-resume-001\":{\"permissions\":{\"network\":{\"enabled\":true}},\"scope\":\"session\"}}}");
        var userInputPath = WriteUtf8File(workspace.RootPath, "thread-resume-user-input.json", "{\"requests\":{\"input-thread-resume-001\":{\"config_path\":\".tianshu/tianshu.toml\"}}}");

        var runtime = new CliConsumerFakeRuntime();
        runtime.ResumeThreadAsyncHandler = (threadId, cancellationToken) =>
        {
            runtime.ActiveThreadId = threadId;
            runtime.HasActiveTurn = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                runtime.HasActiveTurn = false;
                EmitProjectedStreamEvent(runtime, new AgentStreamEvent
                {
                    Kind = AgentStreamEventKind.TurnCompleted,
                    ThreadId = threadId,
                    TurnId = "turn-thread-resume-active-001",
                    Status = "completed",
                });
            }, cancellationToken);

            return Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
            {
                ThreadId = threadId,
                Preview = "恢复预览",
                Name = "线程恢复主链验证",
                Cwd = workspace.RootPath,
                SeedHistory = [new AgentThreadSeedHistoryItem { Role = "user", Content = "resume" }],
                Turns = [new AgentThreadTurn { Id = "turn-thread-resume-active-001", Status = "in_progress" }],
                PendingInteractiveRequests =
                [
                    ToControlPlanePendingInteractiveRequest(new PendingInteractiveRequestReplay
                    {
                        RequestId = 1101,
                        RequestKind = "approval_requested",
                        RequestMethod = "item/tool/requestApproval",
                        CallId = "approval-thread-resume-001",
                        ThreadId = threadId,
                        TurnId = "turn-thread-resume-active-001",
                        ToolName = "shell",
                        Text = "需要批准 shell 调用",
                        Status = "awaitingApproval",
                        Phase = "request_approval",
                        RequiresApproval = true,
                        AvailableDecisions = ["accept", "acceptForSession", "decline"],
                        ApprovalRequest = new ApprovalRequestPayload(
                            "shell",
                            null,
                            ["accept", "acceptForSession", "decline"],
                            "需要批准 shell 调用",
                            Array.Empty<ApprovalMetadataFieldPayload>())
                    }),
                    ToControlPlanePendingInteractiveRequest(new PendingInteractiveRequestReplay
                    {
                        RequestId = 1102,
                        RequestKind = "permission_requested",
                        RequestMethod = "item/permissions/requestApproval",
                        CallId = "permission-thread-resume-001",
                        ThreadId = threadId,
                        TurnId = "turn-thread-resume-active-001",
                        ToolName = "request_permissions",
                        Text = "需要网络权限",
                        Status = "awaitingPermission",
                        Phase = "request_permission",
                        PermissionRequest = new PermissionRequestPayload(
                            "需要网络权限",
                            Array.Empty<PermissionFieldPayload>(),
                            "{\"network\":{\"enabled\":true}}",
                            "需要网络权限")
                    }),
                    ToControlPlanePendingInteractiveRequest(new PendingInteractiveRequestReplay
                    {
                        RequestId = 1103,
                        RequestKind = "request_user_input",
                        RequestMethod = "item/tool/requestUserInput",
                        CallId = "input-thread-resume-001",
                        ThreadId = threadId,
                        TurnId = "turn-thread-resume-active-001",
                        ToolName = "requestUserInput",
                        Text = "请选择配置文件",
                        Status = "awaitingUserInput",
                        Phase = "request_user_input",
                        UserInputRequest = new UserInputRequestPayload(
                            [new UserInputQuestionPayload("config_path", "配置文件", "请选择配置文件", false, true, null)],
                            "请选择配置文件")
                    }),
                ],
                PendingInputState = ToControlPlanePendingInputState(new PendingInputStatePayload(
                    Entries: Array.Empty<PendingInputStateEntryPayload>(),
                    InterruptRequestPending: false,
                    SubmitPendingSteersAfterInterrupt: false,
                    QueuedUserMessages:
                    [
                        new PendingInputStateEntryPayload(
                            "corr-thread-resume-queue-001",
                            "Queue",
                            "Queue",
                            "awaiting_commit",
                            "turn-thread-resume-active-001",
                            null,
                            new PendingFollowUpCompareKeyPayload("恢复后的排队消息", 0),
                            "QueuedUserMessage",
                            [new TextUserInput { Type = "text", Text = "恢复后的排队消息" }]),
                    ],
                PendingSteers:
                    [
                        new PendingInputStateEntryPayload(
                            "corr-thread-resume-steer-001",
                            "Steer",
                            "Steer",
                            "awaiting_commit",
                            "turn-thread-resume-active-001",
                            null,
                            new PendingFollowUpCompareKeyPayload("恢复后的引导消息", 0),
                            "PendingSteer",
                            [new TextUserInput { Type = "text", Text = "恢复后的引导消息" }]),
                    ]))
            });
        };
        runtime.SendFollowUpAsyncHandler = static (_, _, _, _) =>
            throw new Xunit.Sdk.XunitException("thread resume 恢复到待编辑 follow-up 后不应自动发送。");

        var (exitCode, stdout, stderr) = await RunThreadAsync(
            workspace,
            runtime,
            "resume",
            "--thread-id",
            "thread-resume-replay-001",
            "--approve-all",
            "--permissions-json",
            permissionsPath,
            "--user-input-json",
            userInputPath);

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        Assert.Contains("标题：线程恢复主链验证", stdout, StringComparison.Ordinal);
        Assert.Contains("已回放待处理交互：审批 1，权限 1，补录 1。", stdout, StringComparison.Ordinal);
        Assert.Contains("已恢复待编辑 follow-up：2。", stdout, StringComparison.Ordinal);
        Assert.Contains("已恢复到待编辑 Queue follow-up：恢复后的排队消息", stdout, StringComparison.Ordinal);
        Assert.Contains("已恢复到待编辑 Steer follow-up：恢复后的引导消息", stdout, StringComparison.Ordinal);
        Assert.Single(runtime.ApprovalResponses);
        Assert.Equal("approval-thread-resume-001", runtime.ApprovalResponses[0].CallId.Value);
        Assert.Equal(ControlPlaneApprovalDecision.Approve, runtime.ApprovalResponses[0].Decision);
        var replayApprovalPayload = AssertCliGovernanceEnvelope(
            runtime.ApprovalResponses[0].Envelope,
            "cli-approval-approval-thread-resume-001",
            "approval_response");
        Assert.Equal("approval-thread-resume-001", replayApprovalPayload.Payload.Properties["callId"].GetString());
        Assert.Equal("accept", replayApprovalPayload.Payload.Properties["decision"].GetString());
        Assert.Single(runtime.PermissionResponses);
        Assert.Equal("permission-thread-resume-001", runtime.PermissionResponses[0].CallId.Value);
        var replayPermissionPayload = AssertCliGovernanceEnvelope(
            runtime.PermissionResponses[0].Envelope,
            "cli-permission-permission-thread-resume-001",
            "permission_response");
        Assert.Equal("session", replayPermissionPayload.Payload.Properties["scope"].GetString());
        Assert.Single(runtime.UserInputResponses);
        Assert.Equal("input-thread-resume-001", runtime.UserInputResponses[0].CallId.Value);
        var replayUserInputPayload = AssertCliGovernanceEnvelope(
            runtime.UserInputResponses[0].Envelope,
            "cli-userinput-input-thread-resume-001",
            "user_input_submission");
        Assert.Equal(".tianshu/tianshu.toml", replayUserInputPayload.Payload.Properties["answers"].Properties["config_path"].GetString());
        Assert.Empty(runtime.FollowUpCalls);
    }

    [Fact]
    public void CliRuntimeCommandRunner_ThreadResumeReplayState_ClearsPendingInteractiveStateOnResolvedAndTurnCompleted()
    {
        using var workspace = new CliConsumerWorkspace();
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-replay-state-001" };
        var options = ParseCliCommand(WithCommonOptions(workspace, "thread", "resume", "--thread-id", "thread-replay-state-001"));
        var replayStateType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner+ThreadResumeReplayState");
        var governance = new TianShuControlPlane(runtime).Governance;
        var ctor = replayStateType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(x => x.GetParameters().Length == 6);
        var replayState = ctor.Invoke([runtime, governance, options, null, null, CancellationToken.None]);

        void Emit(ControlPlaneConversationStreamEvent streamEvent)
        {
            ReflectionTestHelper.InvokeMethod(
                replayState,
                "OnStreamEvent",
                null,
                new ControlPlaneConversationStreamEventArgs(streamEvent));
        }

        Emit(CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.ApprovalRequested,
            runtime.ActiveThreadId,
            "turn-replay-state-001",
            text: "需要批准 shell 调用",
            callId: "approval-replay-state-001",
            toolName: "shell"));
        Emit(CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.PermissionRequested,
            runtime.ActiveThreadId,
            "turn-replay-state-001",
            text: "需要更高权限",
            callId: "permission-replay-state-001",
            toolName: "request_permissions"));
        Emit(CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.UserInputRequested,
            runtime.ActiveThreadId,
            "turn-replay-state-001",
            text: "请选择配置文件",
            callId: "input-replay-state-001",
            toolName: "requestUserInput"));

        Assert.Single(((System.Collections.IEnumerable)ReflectionTestHelper.GetField(replayState, "pendingApprovals")!).Cast<object>());
        Assert.Single(((System.Collections.IEnumerable)ReflectionTestHelper.GetField(replayState, "pendingPermissionRequests")!).Cast<object>());
        Assert.Single(((System.Collections.IEnumerable)ReflectionTestHelper.GetField(replayState, "pendingUserInputs")!).Cast<object>());

        Emit(CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.ServerRequestResolved,
            runtime.ActiveThreadId,
            "turn-replay-state-001",
            callId: "approval-replay-state-001"));

        Assert.Empty(((System.Collections.IEnumerable)ReflectionTestHelper.GetField(replayState, "pendingApprovals")!).Cast<object>());
        Assert.Single(((System.Collections.IEnumerable)ReflectionTestHelper.GetField(replayState, "pendingPermissionRequests")!).Cast<object>());
        Assert.Single(((System.Collections.IEnumerable)ReflectionTestHelper.GetField(replayState, "pendingUserInputs")!).Cast<object>());

        Emit(CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.TurnCompleted,
            runtime.ActiveThreadId,
            "turn-replay-state-001",
            status: "completed"));

        Assert.Empty(((System.Collections.IEnumerable)ReflectionTestHelper.GetField(replayState, "pendingApprovals")!).Cast<object>());
        Assert.Empty(((System.Collections.IEnumerable)ReflectionTestHelper.GetField(replayState, "pendingPermissionRequests")!).Cast<object>());
        Assert.Empty(((System.Collections.IEnumerable)ReflectionTestHelper.GetField(replayState, "pendingUserInputs")!).Cast<object>());
    }

    [Fact]
    public async Task CliRuntimeCommandRunner_ThreadResume_IgnoresCompareKeyOnlyPendingFollowUps()
    {
        using var workspace = new CliConsumerWorkspace();

        var runtime = new CliConsumerFakeRuntime();
        runtime.ResumeThreadAsyncHandler = (threadId, cancellationToken) =>
        {
            runtime.ActiveThreadId = threadId;
            runtime.HasActiveTurn = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                runtime.HasActiveTurn = false;
                EmitProjectedStreamEvent(runtime, new AgentStreamEvent
                {
                    Kind = AgentStreamEventKind.TurnCompleted,
                    ThreadId = threadId,
                    TurnId = "turn-thread-resume-comparekey-only-001",
                    Status = "completed",
                });
            }, cancellationToken);

            return Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
            {
                ThreadId = threadId,
                Preview = "compareKey-only 恢复验证",
                Cwd = workspace.RootPath,
                SeedHistory = [new AgentThreadSeedHistoryItem { Role = "user", Content = "resume" }],
                Turns = [new AgentThreadTurn { Id = "turn-thread-resume-comparekey-only-001", Status = "in_progress" }],
                PendingInputState = ToControlPlanePendingInputState(new PendingInputStatePayload(
                    Entries: Array.Empty<PendingInputStateEntryPayload>(),
                    InterruptRequestPending: false,
                    SubmitPendingSteersAfterInterrupt: false,
                    QueuedUserMessages:
                    [
                        new PendingInputStateEntryPayload(
                            "corr-thread-resume-comparekey-only-queue-001",
                            "Queue",
                            "Queue",
                            "awaiting_commit",
                            "turn-thread-resume-comparekey-only-001",
                            null,
                            new PendingFollowUpCompareKeyPayload("只剩 compareKey 的排队消息", 0),
                            "QueuedUserMessage",
                            Array.Empty<AgentUserInput>()),
                    ],
                    PendingSteers: Array.Empty<PendingInputStateEntryPayload>()))
            });
        };
        runtime.SendFollowUpAsyncHandler = static (_, _, _, _) =>
            throw new Xunit.Sdk.XunitException("compareKey-only pending follow-up 不应被自动恢复发送。");

        var (exitCode, stdout, stderr) = await RunThreadAsync(
            workspace,
            runtime,
            "resume",
            "--thread-id",
            "thread-resume-comparekey-only-001");

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        Assert.DoesNotContain("已恢复待发送 follow-up：", stdout, StringComparison.Ordinal);
        Assert.Empty(runtime.FollowUpCalls);
    }

    [Fact]
    public async Task CliRuntimeCommandRunner_CoversRpcExecAndFuzzyInterfaces()
    {
        using var workspace = new CliConsumerWorkspace();

        var rpcRuntime = new CliConsumerFakeRuntime();
        var rpcCommand = ParseCliCommand(WithCommonOptions(workspace, "rpc", "--method", "diagnostics/trace/read", "--params-json", "{\"traceId\":\"trace-rpc-formal-cover-1\"}"));
        var rpcRunner = CreateRunner("TianShu.Cli.CliRuntimeCommandRunner", rpcRuntime);
        var (rpcCode, rpcOut, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(rpcRunner, "RunRpcAsync", rpcCommand, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });
        Assert.Equal(0, rpcCode);
        Assert.Contains("trace-rpc-formal-cover-1", rpcOut, StringComparison.Ordinal);
        Assert.Collection(
            rpcRuntime.ExecutionTraceCalls,
            call => Assert.Equal("trace-rpc-formal-cover-1", call.TraceId.Value));
        Assert.Empty(rpcRuntime.RpcCalls);

        var execRuntime = new CliConsumerFakeRuntime();
        execRuntime.StartCommandExecutionAsyncHandler = static (_, _) => Task.FromResult(
            new ControlPlaneCommandExecutionResult
            {
                Started = true,
                ProcessId = "proc-001",
                Pid = 321,
            });
        var execCommand = ParseCliCommand(WithCommonOptions(workspace, "command", "exec", "--command", "git status"));
        var execRunner = CreateRunner("TianShu.Cli.CliRuntimeCommandRunner", execRuntime);
        var (execCode, execOut, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(execRunner, "RunCommandExecAsync", execCommand, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });
        Assert.Equal(0, execCode);
        Assert.Contains("命令已启动。processId=proc-001", execOut, StringComparison.Ordinal);
        Assert.Collection(execRuntime.CommandExecutionStartCalls, call => Assert.Equal("git status", call.CommandText));

        var fuzzyRuntime = new CliConsumerFakeRuntime();
        fuzzyRuntime.UpdateFuzzyFileSearchSessionAsyncHandler = (_, _) =>
        {
            fuzzyRuntime.Emit(new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.Info,
                Message = "fuzzyFileSearch/sessionUpdated",
                PayloadKind = ControlPlaneConversationStreamPayloadKind.FuzzyFileSearchSession,
                Payload = StructuredJson(
                    """
                    {
                      "sessionId": "session-001",
                      "files": [
                        {
                          "path": "src/TianShu.Cli/Program.cs",
                          "fileName": null
                        }
                      ],
                      "isCompleted": false
                    }
                    """),
            });

            return Task.FromResult(new ControlPlaneFuzzyFileSearchCommandAcceptedResult());
        };
        var fuzzyCommand = ParseCliCommand(WithCommonOptions(workspace, "fuzzy-file-search", "update", "--session-id", "session-001", "--query", "chat", "--root", "src"));
        var fuzzyRunner = CreateRunner("TianShu.Cli.CliRuntimeCommandRunner", fuzzyRuntime);
        var (fuzzyCode, fuzzyOut, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(fuzzyRunner, "RunFuzzyFileSearchAsync", fuzzyCommand, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });
        Assert.Equal(0, fuzzyCode);
        Assert.Contains("src/TianShu.Cli/Program.cs", fuzzyOut, StringComparison.Ordinal);
        Assert.Equal(1, fuzzyRuntime.StartFuzzyFileSearchSessionCallCount);
        Assert.Equal(1, fuzzyRuntime.UpdateFuzzyFileSearchSessionCallCount);
        Assert.DoesNotContain(fuzzyRuntime.RpcCalls, static x => x.Method.StartsWith("fuzzyFileSearch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CliRuntimeCommandRunner_RpcUnknownMethod_IsRejectedBeforeDiagnosticsFallback()
    {
        using var workspace = new CliConsumerWorkspace();

        var runtime = new CliConsumerFakeRuntime();
        runtime.InvokeDiagnosticRpcAsyncHandler = static (_, _, _) =>
            throw new Xunit.Sdk.XunitException("未 formalized 的 RPC method 不应落到 diagnostics fallback。");
        var command = ParseCliCommand(WithCommonOptions(workspace, "rpc", "--method", "diagnostics/ping", "--params-json", "{\"value\":42}"));
        var runner = CreateRunner("TianShu.Cli.CliRuntimeCommandRunner", runtime);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunRpcAsync", command, CancellationToken.None);
            _ = Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Contains("正式 runtime surface", exception.Message, StringComparison.Ordinal);
        Assert.Empty(runtime.RpcCalls);
    }

    [Fact]
    public async Task CliRuntimeCommandRunner_RpcFormalMethod_UsesTypedControlPlaneRequest()
    {
        using var workspace = new CliConsumerWorkspace();

        var runtime = new CliConsumerFakeRuntime();
        var command = ParseCliCommand(
            WithCommonOptions(
                workspace,
                "rpc",
                "--method",
                "diagnostics/trace/read",
                "--params-json",
                "{\"traceId\":\"trace-rpc-formal-1\"}"));
        var runner = CreateRunner("TianShu.Cli.CliRuntimeCommandRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunRpcAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        Assert.Contains("trace-rpc-formal-1", stdout, StringComparison.Ordinal);
        Assert.Collection(
            runtime.ExecutionTraceCalls,
            call => Assert.Equal("trace-rpc-formal-1", call.TraceId.Value));
        Assert.Empty(runtime.RpcCalls);
    }

    [Fact]
    public async Task CliRuntimeCommandRunner_FuzzySearch_UsesTypedRuntimeMethod()
    {
        using var workspace = new CliConsumerWorkspace();

        var fuzzyRuntime = new CliConsumerFakeRuntime();
        fuzzyRuntime.SearchFuzzyFilesAsyncHandler = static (_, _) => Task.FromResult(
            new ControlPlaneFuzzyFileSearchResult
            {
                Files =
                [
                    new ControlPlaneFuzzyFileSearchFile
                    {
                        Path = "src/TianShu.Execution.Runtime/TianShuExecutionRuntime.cs",
                        FileName = "TianShuExecutionRuntime.cs",
                    },
                ],
            });

        var fuzzyCommand = ParseCliCommand(WithCommonOptions(workspace, "fuzzy-file-search", "search", "--query", "TianShuExecutionRuntime", "--root", "src"));
        var fuzzyRunner = CreateRunner("TianShu.Cli.CliRuntimeCommandRunner", fuzzyRuntime);
        var (fuzzyCode, fuzzyOut, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(fuzzyRunner, "RunFuzzyFileSearchAsync", fuzzyCommand, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, fuzzyCode);
        Assert.Contains("src/TianShu.Execution.Runtime/TianShuExecutionRuntime.cs", fuzzyOut, StringComparison.Ordinal);
        Assert.Equal(1, fuzzyRuntime.SearchFuzzyFilesCallCount);
        Assert.DoesNotContain(fuzzyRuntime.RpcCalls, static x => x.Method.StartsWith("fuzzyFileSearch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecCommandRunner_CoversTopLevelExecInterface_And_CodeModeWaitRemainsTyped()
    {
        using var workspace = new CliConsumerWorkspace();

        var execRuntime = new CliConsumerFakeRuntime();
        execRuntime.StartNewThreadAsyncHandler = _ =>
        {
            execRuntime.ActiveThreadId = "thread-exec-001";
            return Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
            {
                ThreadId = "thread-exec-001",
                Preview = "exec thread",
                Cwd = workspace.RootPath,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        };
        execRuntime.SendAsyncHandler = (userMessage, _, _) =>
        {
            execRuntime.Emit(CliConsumerFakeRuntime.CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextDelta,
                execRuntime.ActiveThreadId,
                "turn-exec-001",
                text: "workspace is ready"));
            execRuntime.Emit(CliConsumerFakeRuntime.CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextCompleted,
                execRuntime.ActiveThreadId,
                "turn-exec-001"));
            execRuntime.Emit(CliConsumerFakeRuntime.CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.TurnCompleted,
                execRuntime.ActiveThreadId,
                "turn-exec-001",
                status: "completed"));
            return Task.FromResult(AgentSendResult.Ok(
                "workspace is ready",
                turnId: "turn-exec-001",
                turnStatus: "completed"));
        };
        var execCommand = ParseCliCommand(WithCommonOptions(workspace, "exec", "--full-auto", "当前目录是什么？"));
        var execRunner = CreateRunner("TianShu.Cli.ExecCommandRunner", execRuntime);
        var (execCode, execOut, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(execRunner, "RunAsync", execCommand, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });
        Assert.Equal(0, execCode);
        Assert.Equal("workspace is ready" + Environment.NewLine, execOut);
        Assert.Empty(execRuntime.RpcCalls);
        Assert.Single(execRuntime.StartThreadCalls);
        var sendCall = Assert.Single(execRuntime.SendCalls);
        Assert.Equal("当前目录是什么？", sendCall.UserMessage);
        var execItems = AssertCliConversationEnvelope(
            Assert.Single(execRuntime.TurnSubmissionCommands).Envelope,
            "cli-turn-",
            "turn_submission");
        Assert.Equal("当前目录是什么？", Assert.IsType<TextInteractionItem>(Assert.Single(execItems)).Text);
        Assert.Equal("workspace-write", execRuntime.InitializedOptions?.SandboxMode);
        Assert.Equal("never", execRuntime.InitializedOptions?.ApprovalPolicy);
        Assert.Collection(execRuntime.UnsubscribeThreadCalls, threadId => Assert.Equal("thread-exec-001", threadId));

        var waitRuntime = new CliConsumerFakeRuntime();
        waitRuntime.InvokeDiagnosticRpcAsyncHandler = static (_, _, _) => Task.FromResult(
            StructuredJson(
                """
                {
                  "success": true,
                  "status": "completed",
                  "threadId": "thread-code-wait-001",
                  "turnId": "turn-code-wait-001",
                  "cellId": "cell-code-wait-001",
                  "output": "done"
                }
                """));
        var waitCommand = ParseCliCommand(WithCommonOptions(workspace, "code-mode", "wait", "--thread-id", "thread-code-wait-001", "--cell-id", "cell-code-wait-001", "--max-tokens", "32", "--terminate", "--json"));
        var waitRunner = CreateRunner("TianShu.Cli.CliRuntimeCommandRunner", waitRuntime);
        var (waitCode, waitOut, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(waitRunner, "RunCodeModeAsync", waitCommand, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });
        Assert.Equal(0, waitCode);
        using (var waitDocument = JsonDocument.Parse(waitOut))
        {
            Assert.True(waitDocument.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("completed", waitDocument.RootElement.GetProperty("status").GetString());
            Assert.Equal("cell-code-wait-001", waitDocument.RootElement.GetProperty("cellId").GetString());
            Assert.Equal("done", waitDocument.RootElement.GetProperty("output").GetString());
        }

        Assert.Collection(
            waitRuntime.RpcCalls,
            call =>
            {
                Assert.Equal("exec_wait", call.Method);
                Assert.NotNull(call.Parameters);
                Assert.Equal("thread-code-wait-001", call.Parameters!.GetProperty("threadId").GetString());
                Assert.Equal("cell-code-wait-001", call.Parameters.GetProperty("cellId").GetString());
                Assert.Equal(32, call.Parameters.GetProperty("maxTokens").GetInt32());
                Assert.True(call.Parameters.GetProperty("terminate").GetBoolean());
            });
    }

    [Fact]
    public async Task ExecCommandRunner_CoversTopLevelExecReviewInterface()
    {
        using var workspace = new CliConsumerWorkspace();

        var reviewRuntime = new CliConsumerFakeRuntime();
        reviewRuntime.StartNewThreadAsyncHandler = _ =>
        {
            reviewRuntime.ActiveThreadId = "thread-exec-review-001";
            return Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
            {
                ThreadId = "thread-exec-review-001",
                Preview = "exec review thread",
                Cwd = workspace.RootPath,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        };
        reviewRuntime.StartReviewAsyncHandler = (request, _) =>
        {
            reviewRuntime.Emit(CliConsumerFakeRuntime.CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextDelta,
                request.ThreadId,
                "turn-exec-review-001",
                text: "review ready"));
            reviewRuntime.Emit(CliConsumerFakeRuntime.CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.TurnCompleted,
                request.ThreadId,
                "turn-exec-review-001",
                status: "completed"));
            return Task.FromResult(new ControlPlaneReviewStartResult
            {
                ReviewThreadId = request.ThreadId,
                Turn = new ControlPlaneReviewTurn
                {
                    Id = "turn-exec-review-001",
                    Status = "inProgress",
                    DisplayText = "检查当前仓库",
                },
            });
        };

        var reviewCommand = ParseCliCommand(WithCommonOptions(workspace, "exec", "review", "--uncommitted"));
        var reviewRunner = CreateRunner("TianShu.Cli.ExecCommandRunner", reviewRuntime);
        var (reviewCode, reviewOut, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(reviewRunner, "RunAsync", reviewCommand, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, reviewCode);
        Assert.Equal("review ready" + Environment.NewLine, reviewOut);
        Assert.Empty(reviewRuntime.RpcCalls);
        Assert.Single(reviewRuntime.StartThreadCalls);
        var reviewRequest = Assert.Single(reviewRuntime.ReviewStartCalls);
        Assert.Equal("thread-exec-review-001", reviewRequest.ThreadId);
        Assert.IsType<ControlPlaneReviewUncommittedChangesTarget>(reviewRequest.Target);
        Assert.Collection(reviewRuntime.UnsubscribeThreadCalls, threadId => Assert.Equal("thread-exec-review-001", threadId));
    }

    [Fact]
    public async Task CliRuntimeCommandRunner_CodeModeInputFile_PreservesPragmaAndStoreLoadScript()
    {
        using var workspace = new CliConsumerWorkspace();
        const string script = """
// @exec: {"yield_time_ms":250,"max_output_tokens":64}
await store("state", { value: 1 });
const restored = await load("state");
console.log(restored);
""";
        var scriptPath = WriteUtf8File(workspace.RootPath, "code-mode-script.js", script);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-code-file-001" };
        runtime.InvokeDiagnosticRpcAsyncHandler = static (_, _, _) => Task.FromResult(
            StructuredJson(
                """
                {
                  "success": true,
                  "status": "running",
                  "threadId": "thread-code-file-001",
                  "turnId": "turn-code-file-001",
                  "cellId": "cell-code-file-001",
                  "output": "partial output"
                }
                """));

        var command = ParseCliCommand(WithCommonOptions(
            workspace,
            "code-mode",
            "exec",
            "--input-file",
            scriptPath,
            "--yield-time-ms",
            "250",
            "--max-output-tokens",
            "64",
            "--json"));
        var runner = CreateRunner("TianShu.Cli.CliRuntimeCommandRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunCodeModeAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        using (var document = JsonDocument.Parse(stdout))
        {
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("cell-code-file-001", document.RootElement.GetProperty("cellId").GetString());
        }

        Assert.Collection(
            runtime.RpcCalls,
            call =>
            {
                Assert.Equal("exec", call.Method);
                Assert.NotNull(call.Parameters);
                Assert.Equal("thread-code-file-001", call.Parameters!.GetProperty("threadId").GetString());
                Assert.Equal(script, call.Parameters.GetProperty("input").GetString());
                Assert.Equal(250, call.Parameters.GetProperty("yieldTimeMs").GetInt32());
                Assert.Equal(64, call.Parameters.GetProperty("maxOutputTokens").GetInt32());
            });
    }

    [Fact]
    public async Task ConversationTurnCommandRunner_UsesFollowUpAndAutoResponders()
    {
        using var workspace = new CliConsumerWorkspace();
        var permissionsPath = WriteUtf8File(workspace.RootPath, "followup-permissions.json", "{\"requests\":{\"permission-followup-001\":{\"permissions\":{\"network\":{\"enabled\":true}},\"scope\":\"session\"}}}");
        var userInputPath = WriteUtf8File(workspace.RootPath, "followup-user-input.json", "{\"requests\":{\"input-followup-001\":{\"choice\":\"A\",\"confirmed\":true}}}");
        var approvalTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var permissionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inputTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-followup-001" };
        runtime.RespondToApprovalAsyncHandler = (_, _) =>
        {
            approvalTcs.TrySetResult(true);
            return Task.FromResult(true);
        };
        runtime.RespondToPermissionRequestAsyncHandler = (response, _) =>
        {
            Assert.Equal(ControlPlanePermissionScope.Session, response.Scope);
            permissionTcs.TrySetResult(true);
            return Task.FromResult(true);
        };
        runtime.RespondToUserInputAsyncHandler = (_, _) =>
        {
            inputTcs.TrySetResult(true);
            return Task.FromResult(true);
        };
        runtime.SendFollowUpAsyncHandler = async (message, mode, cancellationToken, correlationId) =>
        {
            Assert.False(string.IsNullOrWhiteSpace(correlationId));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.ApprovalRequested, callId: "approval-followup-001", toolName: "shell"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.PermissionRequested, callId: "permission-followup-001", toolName: "request_permissions"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.UserInputRequested, callId: "input-followup-001"));
            await approvalTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            await permissionTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            await inputTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextDelta, text: "follow-up 集成验证通过"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextCompleted));
            return AgentSendResult.Ok(message, turnId: "turn-followup-001", turnStatus: "completed", correlationId: correlationId, requestedMode: mode, effectiveMode: mode);
        };

        var command = ParseCliCommand(WithCommonOptions(workspace, "follow-up", "--mode", "queue", "--message", "继续下一步", "--approve-all", "--permissions-json", permissionsPath, "--user-input-json", userInputPath, "--json"));
        var runner = CreateRunner("TianShu.Cli.ConversationTurnCommandRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunFollowUpAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal("thread-followup-001", document.RootElement.GetProperty("threadId").GetString());
        Assert.Equal("turn-followup-001", document.RootElement.GetProperty("turnId").GetString());
        Assert.Equal("completed", document.RootElement.GetProperty("turnStatus").GetString());
        Assert.Equal(runtime.FollowUpCalls[0].CorrelationId, document.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("Queue", document.RootElement.GetProperty("requestedMode").GetString());
        Assert.Equal("Queue", document.RootElement.GetProperty("effectiveMode").GetString());
        Assert.Equal("follow-up 集成验证通过", document.RootElement.GetProperty("assistantText").GetString());
        Assert.Collection(runtime.FollowUpCalls, call =>
        {
            Assert.Equal(("继续下一步", FollowUpMode.Queue), (call.UserMessage, call.Mode));
            Assert.False(string.IsNullOrWhiteSpace(call.CorrelationId));
        });
        var followUpItems = AssertCliConversationEnvelope(
            Assert.Single(runtime.FollowUpSubmissionCommands).Envelope,
            "cli-followup-",
            "follow_up_submission");
        Assert.Equal("继续下一步", Assert.IsType<TextInteractionItem>(Assert.Single(followUpItems)).Text);
        Assert.Single(runtime.ApprovalResponses);
        Assert.Equal(ControlPlaneApprovalDecision.Approve, runtime.ApprovalResponses[0].Decision);
        var followUpApprovalPayload = AssertCliGovernanceEnvelope(
            runtime.ApprovalResponses[0].Envelope,
            "cli-approval-approval-followup-001",
            "approval_response");
        Assert.Equal("approval-followup-001", followUpApprovalPayload.Payload.Properties["callId"].GetString());
        Assert.Single(runtime.PermissionResponses);
        var followUpPermissionPayload = AssertCliGovernanceEnvelope(
            runtime.PermissionResponses[0].Envelope,
            "cli-permission-permission-followup-001",
            "permission_response");
        Assert.Equal("session", followUpPermissionPayload.Payload.Properties["scope"].GetString());
        Assert.Single(runtime.UserInputResponses);
        var followUpUserInputPayload = AssertCliGovernanceEnvelope(
            runtime.UserInputResponses[0].Envelope,
            "cli-userinput-input-followup-001",
            "user_input_submission");
        Assert.Equal("A", followUpUserInputPayload.Payload.Properties["answers"].Properties["choice"].GetString());
    }

    [Fact]
    public async Task ConversationTurnCommandRunner_KernelRuntimeLoopInterrupt_WritesFileBackedCancelSignal()
    {
        using var workspace = new CliConsumerWorkspace();
        WriteKernelRuntimeProviderConfig(workspace, providerBaseUrl: "https://provider.example.invalid/v1", envKey: $"TIANSHU_CLI_TEST_MISSING_{Guid.NewGuid():N}");
        var cancelSignalPath = WriteHostControlActiveRunRecord(
            workspace.RootPath,
            "thread-product-interrupt-001",
            "turn-product-interrupt-001",
            "run-product-interrupt-001");
        var command = ParseCliCommand(
            WithCommonOptions(
                workspace,
                "follow-up",
                "--kernel-runtime-loop",
                "--mode",
                "interrupt",
                "--resume-thread-id",
                "thread-product-interrupt-001",
                "--turn-id",
                "turn-product-interrupt-001",
                "--message",
                "user.cancel",
                "--json"));
        var runner = CreateRunner("TianShu.Cli.ConversationTurnCommandRunner", new CliConsumerFakeRuntime());

        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunFollowUpAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(cancelSignalPath));
        using (var cancel = JsonDocument.Parse(File.ReadAllText(cancelSignalPath)))
        {
            Assert.Equal("user.cancel", cancel.RootElement.GetProperty("reason").GetString());
            Assert.Equal("thread-product-interrupt-001", cancel.RootElement.GetProperty("threadId").GetString());
            Assert.Equal("turn-product-interrupt-001", cancel.RootElement.GetProperty("turnId").GetString());
            Assert.Equal("run-product-interrupt-001", cancel.RootElement.GetProperty("runId").GetString());
        }

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        Assert.Equal("kernel-runtime-loop", root.GetProperty("executionPath").GetString());
        Assert.Equal("interrupt", root.GetProperty("operation").GetString());
        Assert.Equal("interrupted", root.GetProperty("status").GetString());
        var cancellation = root
            .GetProperty("kernelRuntime")
            .GetProperty("terminalProjection")
            .GetProperty("activeRunCancellation");
        Assert.True(cancellation.GetProperty("available").GetBoolean());
        Assert.Equal("active-run://thread-product-interrupt-001/turn-product-interrupt-001/run-product-interrupt-001", cancellation.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task ConversationTurnCommandRunner_KernelRuntimeLoopSteer_ProjectsAppliedSteerAndProviderInput()
    {
        using var workspace = new CliConsumerWorkspace();
        using var provider = new SingleResponseProviderServer(BuildProviderSse("OK"));
        var envKey = $"TIANSHU_CLI_TEST_PROVIDER_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(envKey, "test-api-key");
        try
        {
            WriteKernelRuntimeProviderConfig(workspace, provider.BaseUrl + "/v1", envKey);
            var command = ParseCliCommand(
                WithCommonOptions(
                    workspace,
                    "follow-up",
                    "--kernel-runtime-loop",
                    "--mode",
                    "steer",
                    "--resume-thread-id",
                    "thread-product-steer-001",
                    "--turn-id",
                    "turn-product-steer-001",
                    "--message",
                    "中途引导：请只回答 OK",
                    "--turn-timeout-seconds",
                    "10",
                    "--json"));
            var runner = CreateRunner("TianShu.Cli.ConversationTurnCommandRunner", new CliConsumerFakeRuntime());

            var (exitCode, stdout, _) = await CaptureAsync(async () =>
            {
                var task = ReflectionTestHelper.InvokeMethod(runner, "RunFollowUpAsync", command, CancellationToken.None);
                return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
            });

            Assert.Equal(0, exitCode);
            var requestBody = await provider.RequestBodyTask.WaitAsync(TimeSpan.FromSeconds(5));
            using (var requestDocument = JsonDocument.Parse(requestBody))
            {
                Assert.True(
                    JsonContainsString(requestDocument.RootElement, "中途引导：请只回答 OK"),
                    "Provider request body must contain the steer text after JSON string decoding.");
            }

            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            Assert.Equal("kernel-runtime-loop", root.GetProperty("executionPath").GetString());
            Assert.Equal("steer", root.GetProperty("operation").GetString());
            Assert.Equal("completed", root.GetProperty("status").GetString());
            var steer = root
                .GetProperty("kernelRuntime")
                .GetProperty("terminalProjection")
                .GetProperty("steer");
            Assert.True(steer.GetProperty("available").GetBoolean());
            Assert.Equal("applied_to_model_input", steer.GetProperty("disposition").GetString());
            Assert.Contains(
                steer.GetProperty("inputs").EnumerateArray().Select(static item => item.GetString()),
                static item => item == "中途引导：请只回答 OK");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }
    }

    [Fact]
    public async Task ConversationTurnCommandRunner_KernelRuntimeLoopResume_ProjectsCheckpointReentry()
    {
        using var workspace = new CliConsumerWorkspace();
        WriteKernelRuntimeProviderConfig(workspace, providerBaseUrl: "https://provider.example.invalid/v1", envKey: $"TIANSHU_CLI_TEST_MISSING_{Guid.NewGuid():N}");
        var checkpointRef = WriteHostControlCheckpointRecord(
            workspace.RootPath,
            sessionId: "session-product-resume-001",
            threadId: "thread-product-resume-001",
            turnId: "turn-product-resume-001",
            steerInputs: ["恢复前的 pending steer"]);
        WriteHostControlPendingSteerRecord(
            workspace.RootPath,
            threadId: "thread-product-resume-001",
            turnId: "turn-product-resume-001",
            inputs: ["恢复前的 pending steer"]);
        var command = ParseCliCommand(
            WithCommonOptions(
                workspace,
                "follow-up",
                "--kernel-runtime-loop",
                "--checkpoint-ref",
                checkpointRef,
                "--resume-thread-id",
                "thread-product-resume-001",
                "--resume-token",
                "resume-token-product-001",
                "--json"));
        var runner = CreateRunner("TianShu.Cli.ConversationTurnCommandRunner", new CliConsumerFakeRuntime());

        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunFollowUpAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        Assert.Equal("kernel-runtime-loop", root.GetProperty("executionPath").GetString());
        Assert.Equal("resume", root.GetProperty("operation").GetString());
        Assert.Equal("completed", root.GetProperty("status").GetString());
        var terminalProjection = root
            .GetProperty("kernelRuntime")
            .GetProperty("terminalProjection");
        Assert.Equal("thread-product-resume-001", terminalProjection.GetProperty("threadId").GetString());
        var checkpoint = terminalProjection.GetProperty("checkpointResume");
        Assert.True(checkpoint.GetProperty("available").GetBoolean());
        Assert.Equal(checkpointRef, checkpoint.GetProperty("reference").GetString());
        var steer = terminalProjection.GetProperty("steer");
        Assert.True(steer.GetProperty("available").GetBoolean());
        Assert.Contains(
            steer.GetProperty("inputs").EnumerateArray().Select(static item => item.GetString()),
            static item => item == "恢复前的 pending steer");
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；对应主线行为由 Kernel→Runtime loop 用例覆盖。")]
    public async Task SendCommandRunner_UsesSendAndAutoResponders()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts");
        Directory.CreateDirectory(artifactsRoot);
        var permissionsPath = WriteUtf8File(workspace.RootPath, "send-permissions.json", "{\"requests\":{\"permission-send-001\":{\"permissions\":{\"file_system\":{\"write\":[\"src\"]}},\"scope\":\"turn\"}}}");
        var userInputPath = WriteUtf8File(workspace.RootPath, "send-user-input.json", "{\"requests\":{\"input-send-001\":{\"selection\":\"continue\"}}}");
        var approvalTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var permissionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inputTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-send-001" };
        runtime.RespondToApprovalAsyncHandler = (_, _) =>
        {
            approvalTcs.TrySetResult(true);
            return Task.FromResult(true);
        };
        runtime.RespondToPermissionRequestAsyncHandler = (response, _) =>
        {
            Assert.Equal(ControlPlanePermissionScope.Turn, response.Scope);
            permissionTcs.TrySetResult(true);
            return Task.FromResult(true);
        };
        runtime.RespondToUserInputAsyncHandler = (_, _) =>
        {
            inputTcs.TrySetResult(true);
            return Task.FromResult(true);
        };
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.ApprovalRequested, callId: "approval-send-001", toolName: "shell"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.PermissionRequested, callId: "permission-send-001", toolName: "request_permissions"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.UserInputRequested, callId: "input-send-001"));
            await approvalTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            await permissionTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            await inputTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextDelta, text: "send 集成验证通过"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextCompleted, text: "send 集成验证通过"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.TurnCompleted, runtime.ActiveThreadId, "turn-send-001", status: "completed"));
            return AgentSendResult.Ok(message, turnId: "turn-send-001", turnStatus: "completed");
        };

        var command = ParseCliCommand("send", "--apphost-control-plane", "--message", "请继续验证", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--approve-all", "--permissions-json", permissionsPath, "--user-input-json", userInputPath, "--artifacts", artifactsRoot, "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        Assert.NotNull(result);
        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summaryDocument = JsonDocument.Parse(summaryJson);
        Assert.True(summaryDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Success", summaryDocument.RootElement.GetProperty("exitCodeName").GetString());
        Assert.Equal("thread-send-001", summaryDocument.RootElement.GetProperty("threadId").GetString());
        Assert.True(summaryDocument.RootElement.GetProperty("approvalAutoResponded").GetBoolean());
        Assert.True(summaryDocument.RootElement.GetProperty("permissionAutoResponded").GetBoolean());
        Assert.True(summaryDocument.RootElement.GetProperty("userInputAutoResponded").GetBoolean());
        Assert.Collection(runtime.SendCalls, call => Assert.Equal("请继续验证", call.UserMessage));
        var sendItems = AssertCliConversationEnvelope(
            Assert.Single(runtime.TurnSubmissionCommands).Envelope,
            "cli-turn-",
            "turn_submission");
        Assert.Equal("请继续验证", Assert.IsType<TextInteractionItem>(Assert.Single(sendItems)).Text);
        Assert.Single(runtime.ApprovalResponses);
        Assert.Equal(ControlPlaneApprovalDecision.Approve, runtime.ApprovalResponses[0].Decision);
        var sendApprovalPayload = AssertCliGovernanceEnvelope(
            runtime.ApprovalResponses[0].Envelope,
            "cli-approval-approval-send-001",
            "approval_response");
        Assert.Equal("approval-send-001", sendApprovalPayload.Payload.Properties["callId"].GetString());
        Assert.Single(runtime.PermissionResponses);
        var sendPermissionPayload = AssertCliGovernanceEnvelope(
            runtime.PermissionResponses[0].Envelope,
            "cli-permission-permission-send-001",
            "permission_response");
        Assert.Equal("turn", sendPermissionPayload.Payload.Properties["scope"].GetString());
        Assert.Single(runtime.UserInputResponses);
        var sendUserInputPayload = AssertCliGovernanceEnvelope(
            runtime.UserInputResponses[0].Envelope,
            "cli-userinput-input-send-001",
            "user_input_submission");
        Assert.Equal("continue", sendUserInputPayload.Payload.Properties["answers"].Properties["selection"].GetString());
    }

    [Fact]
    public async Task SendCommandRunner_WithKernelRuntimeLoop_FailsClosedWithoutProviderCredentialsAndPreservesReplayEvidence()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-kernel-runtime-loop");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-old-path-should-not-run" };
        runtime.SendAsyncHandler = static (_, _, _) => throw new InvalidOperationException("旧 SendAsync 不应在 --kernel-runtime-loop 路径被调用。");

        var missingProviderEnvKey = $"TIANSHU_CLI_TEST_MISSING_KEY_{Guid.NewGuid():N}";
        File.WriteAllText(
            workspace.ConfigPath,
            $$"""
            profile = "work"

            [profiles.work]
            model = "claude-3-7-sonnet"
            provider = "anthropic"
            approval_policy = "on-request"

            [providers.anthropic]
            base_url = "https://api.anthropic.com/v1"
            api_key_env = "{{missingProviderEnvKey}}"
            default_protocol = "responses"
            """,
            new UTF8Encoding(false));

        var command = ParseCliCommand(
            "send",
            "--message", "请验证新 Kernel loop",
            "--cwd", workspace.RootPath,
            "--config", workspace.ConfigPath,
            "--profile", "work",
            "--artifacts", artifactsRoot,
            "--kernel-runtime-loop",
            "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("kernel-runtime-loop", summary.RootElement.GetProperty("executionPath").GetString());
        Assert.Equal("RuntimeFailed", summary.RootElement.GetProperty("kernelRuntimeDisposition").GetString());
        Assert.Equal("failed", summary.RootElement.GetProperty("turnStatus").GetString());
        Assert.Equal("TurnFailed", summary.RootElement.GetProperty("exitCodeName").GetString());
        Assert.Equal("回合未成功完成，当前状态：failed。", summary.RootElement.GetProperty("failureMessage").GetString());
        Assert.StartsWith("trace://execution/", summary.RootElement.GetProperty("runtimeTraceRef").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("diagnostics://execution/", summary.RootElement.GetProperty("runtimeDiagnosticsRef").GetString(), StringComparison.Ordinal);
        Assert.Equal("live-partial", summary.RootElement.GetProperty("replayCompleteness").GetString());
        Assert.Equal(
            ["stage.prepare-context", "stage.model-reason", "stage.finalize"],
            summary.RootElement.GetProperty("stagePath").EnumerateArray().Select(static item => item.GetString()!).ToArray());
        Assert.True(summary.RootElement.GetProperty("runtimeExecutionStepCount").GetInt32() > 0);
        Assert.Equal(
            "live-partial",
            summary.RootElement.GetProperty("replaySummary").GetProperty("completeness").GetString());
        Assert.Contains(
            summary.RootElement.GetProperty("replaySummary").GetProperty("failureCodes").EnumerateArray(),
            item => string.Equals(item.GetString(), "provider_api_key_missing", StringComparison.Ordinal));
        Assert.Empty(runtime.SendCalls);
        Assert.Null(runtime.InitializedOptions);

        var runDirectory = summary.RootElement.GetProperty("artifactsDirectory").GetString();
        Assert.False(string.IsNullOrWhiteSpace(runDirectory));
        using var persistedSummary = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory!, "summary.json")));
        Assert.Equal("kernel-runtime-loop", persistedSummary.RootElement.GetProperty("executionPath").GetString());
        Assert.Equal("live-partial", persistedSummary.RootElement.GetProperty("replayCompleteness").GetString());
    }

    [Fact]
    public async Task SendCommandRunner_WithoutKernelRuntimeLoop_UsesDefaultKernelRuntimeLoop()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-default-send-path");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-default-send-should-not-run" };
        runtime.SendAsyncHandler = static (_, _, _) => throw new InvalidOperationException("旧 SendAsync 不应在默认新 loop 路径被调用。");
        WriteMissingProviderConfig(workspace);

        var command = ParseCliCommand(
            "send",
            "--message", "默认路径应进入新 Kernel loop",
            "--cwd", workspace.RootPath,
            "--config", workspace.ConfigPath,
            "--profile", "work",
            "--artifacts", artifactsRoot,
            "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
        AssertKernelRuntimeRouteEvidence(summary.RootElement);
        Assert.Equal("RuntimeFailed", summary.RootElement.GetProperty("kernelRuntimeDisposition").GetString());
        Assert.Equal("failed", summary.RootElement.GetProperty("turnStatus").GetString());
        Assert.Equal("TurnFailed", summary.RootElement.GetProperty("exitCodeName").GetString());
        Assert.Equal("live-partial", summary.RootElement.GetProperty("replayCompleteness").GetString());
        Assert.Equal(
            ["stage.prepare-context", "stage.model-reason", "stage.finalize"],
            summary.RootElement.GetProperty("stagePath").EnumerateArray().Select(static item => item.GetString()!).ToArray());
        var terminalProjection = summary.RootElement.GetProperty("kernelRuntimeTerminalProjection");
        Assert.Equal("failed", terminalProjection.GetProperty("turnStatus").GetString());
        Assert.Equal(summary.RootElement.GetProperty("threadId").GetString(), terminalProjection.GetProperty("threadId").GetString());
        Assert.Equal(summary.RootElement.GetProperty("turnId").GetString(), terminalProjection.GetProperty("turnId").GetString());
        Assert.True(terminalProjection.GetProperty("turnLog").GetProperty("available").GetBoolean());
        Assert.Null(terminalProjection.GetProperty("turnLog").GetProperty("reason").GetString());
        Assert.True(File.Exists(terminalProjection.GetProperty("turnLog").GetProperty("reference").GetString()));
        Assert.True(terminalProjection.GetProperty("rolloutRecord").GetProperty("available").GetBoolean());
        Assert.Null(terminalProjection.GetProperty("rolloutRecord").GetProperty("reason").GetString());
        Assert.True(File.Exists(terminalProjection.GetProperty("rolloutRecord").GetProperty("reference").GetString()));
        Assert.DoesNotContain(
            terminalProjection.GetProperty("downgradeReasons").EnumerateArray(),
            item => string.Equals(item.GetString(), "rollout_record_not_migrated_23_6", StringComparison.Ordinal));
        Assert.Contains(
            summary.RootElement.GetProperty("requiredCapabilities").EnumerateArray(),
            item => string.Equals(item.GetString(), "BasicTurn", StringComparison.Ordinal));
        Assert.Empty(runtime.SendCalls);
        Assert.Null(runtime.InitializedOptions);
    }

    [Fact]
    public async Task SendCommandRunner_WithoutExplicitConfig_UsesTianShuHomeDefaultRouteSetAndFailsClosedOnMissingApiKey()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-default-user-config-path");
        var tianShuHome = Path.Combine(workspace.RootPath, "tianshu-home");
        var defaultWorkingDirectory = Path.Combine(tianShuHome, "workspace");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        Directory.CreateDirectory(artifactsRoot);
        Directory.CreateDirectory(defaultWorkingDirectory);
        WriteDefaultRouteSetUserConfig(tianShuHome);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-default-home-should-not-run" };
        runtime.SendAsyncHandler = static (_, _, _) => throw new InvalidOperationException("旧 SendAsync 不应在默认新 loop 路径被调用。");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            var command = ParseCliCommand(
                "send",
                "--message", "默认用户配置应解析 route-set",
                "--cwd", defaultWorkingDirectory,
                "--artifacts", artifactsRoot,
                "--json");
            var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
            var (result, _, _) = await CaptureAsync(async () =>
            {
                var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
                return await ReflectionTestHelper.AwaitTaskResultAsync(task);
            });

            var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
            using var summary = JsonDocument.Parse(summaryJson);
            Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
            AssertKernelRuntimeRouteEvidence(summary.RootElement);
            Assert.Equal(userConfigPath, summary.RootElement.GetProperty("configFilePath").GetString());
            Assert.Equal("openai-compatible-default", summary.RootElement.GetProperty("model").GetString());
            Assert.Equal("openai-compatible", summary.RootElement.GetProperty("modelProvider").GetString());
            Assert.Equal("RuntimeFailed", summary.RootElement.GetProperty("kernelRuntimeDisposition").GetString());
            Assert.Equal("failed", summary.RootElement.GetProperty("turnStatus").GetString());
            Assert.Equal("live-partial", summary.RootElement.GetProperty("replayCompleteness").GetString());
            Assert.Contains(
                summary.RootElement.GetProperty("replaySummary").GetProperty("failureCodes").EnumerateArray(),
                item => string.Equals(item.GetString(), "provider_api_key_missing", StringComparison.Ordinal));
            Assert.DoesNotContain(
                summary.RootElement.GetProperty("replaySummary").GetProperty("failureCodes").EnumerateArray(),
                item => string.Equals(item.GetString(), "provider_model_missing", StringComparison.Ordinal));
            Assert.Empty(runtime.SendCalls);
            Assert.Null(runtime.InitializedOptions);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
        }
    }

    [Fact]
    public async Task SendCommandRunner_DefaultKernelRuntimeLoop_MatchesOptInSemanticProjection()
    {
        using var workspace = new CliConsumerWorkspace();
        WriteMissingProviderConfig(workspace);

        var defaultSummary = await RunKernelRuntimeSummaryAsync(useOptInFlag: false, "artifacts-default-new-loop");
        var optInSummary = await RunKernelRuntimeSummaryAsync(useOptInFlag: true, "artifacts-opt-in-new-loop");

        AssertKernelRuntimeRouteEvidence(defaultSummary);
        AssertKernelRuntimeRouteEvidence(optInSummary);
        AssertKernelRuntimeSemanticProjectionEquivalent(defaultSummary, optInSummary);

        async Task<JsonElement> RunKernelRuntimeSummaryAsync(bool useOptInFlag, string artifactsDirectoryName)
        {
            var artifactsRoot = Path.Combine(workspace.RootPath, artifactsDirectoryName);
            Directory.CreateDirectory(artifactsRoot);
            var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-old-path-should-not-run" };
            runtime.SendAsyncHandler = static (_, _, _) => throw new InvalidOperationException("旧 SendAsync 不应在新 loop 路径被调用。");
            var args = new List<string>
            {
                "send",
                "--message", "比较默认路径与 opt-in 新 loop",
                "--cwd", workspace.RootPath,
                "--config", workspace.ConfigPath,
                "--profile", "work",
                "--artifacts", artifactsRoot,
                "--json",
            };
            if (useOptInFlag)
            {
                args.Add("--kernel-runtime-loop");
            }

            var command = ParseCliCommand(args.ToArray());
            var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
            var (result, _, _) = await CaptureAsync(async () =>
            {
                var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
                return await ReflectionTestHelper.AwaitTaskResultAsync(task);
            });

            Assert.Empty(runtime.SendCalls);
            var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
            using var document = JsonDocument.Parse(summaryJson);
            return document.RootElement.Clone();
        }
    }

    [Fact]
    public async Task SendCommandRunner_KernelRuntimeProductParityGate_RemainsOpenWhileTerminalDeltasExist()
    {
        using var workspace = new CliConsumerWorkspace();
        WriteMissingProviderConfig(workspace);
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-product-parity-gate");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-old-path-should-not-run" };
        runtime.SendAsyncHandler = static (_, _, _) => throw new InvalidOperationException("旧 SendAsync 不应在默认新 loop 路径被调用。");

        var command = ParseCliCommand(
            "send",
            "--message", "验证完整产品 parity gate",
            "--cwd", workspace.RootPath,
            "--config", workspace.ConfigPath,
            "--profile", "work",
            "--artifacts", artifactsRoot,
            "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        Assert.Empty(runtime.SendCalls);
        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        AssertKernelRuntimeRouteEvidence(summary.RootElement);
        AssertKernelRuntimeFullProductParityGateOpen(summary.RootElement);
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；对应主线行为由 Kernel→Runtime loop 用例覆盖。")]
    public async Task SendCommandRunner_AutoApprovalDecision_DowngradesToAcceptForSession_WhenAlwaysUnavailable()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-downgrade");
        Directory.CreateDirectory(artifactsRoot);
        var approvalTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-send-decision-001" };
        runtime.RespondToApprovalAsyncHandler = (response, _) =>
        {
            Assert.Equal(ControlPlaneApprovalDecision.ApproveForSession, response.Decision);
            approvalTcs.TrySetResult(true);
            return Task.FromResult(true);
        };
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ApprovalRequested,
                callId: "approval-send-decision-001",
                toolName: "mcp__custom_server__dangerous_tool",
                availableDecisions: ["accept", "acceptForSession", "decline", "cancel"]));
            await approvalTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextCompleted, text: "decision downgrade ok"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.TurnCompleted, runtime.ActiveThreadId, "turn-send-decision-001", status: "completed"));
            return AgentSendResult.Ok(message, turnId: "turn-send-decision-001", turnStatus: "completed");
        };

        var command = ParseCliCommand(
            "send",
            "--apphost-control-plane",
            "--message", "请自动处理审批",
            "--cwd", workspace.RootPath,
            "--config", workspace.ConfigPath,
            "--profile", "work",
            "--approve-all",
            "--approval-decision", "always",
            "--artifacts", artifactsRoot,
            "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        Assert.NotNull(result);
        Assert.Single(runtime.ApprovalResponses);
        Assert.Equal(ControlPlaneApprovalDecision.ApproveForSession, runtime.ApprovalResponses[0].Decision);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_CoversInteractiveInterfaces()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script.txt", string.Join(Environment.NewLine, new[]
        {
            "需要审批",
            "/wait-event ApprovalRequested 2",
            "/wait-event PermissionRequested 2",
            "/wait-event UserInputRequested 2",
            "/approve-session approval-chat-001 已批准",
            "/permissions permission-chat-001 {\"permissions\":{\"network\":{\"enabled\":true}},\"scope\":\"session\"}",
            "/input input-chat-001 {\"choice\":\"B\"}",
            "/wait-complete 2",
            "/follow-up queue 再来一轮",
            "/wait-complete 2",
            "/interrupt",
            "/threads",
            "/new",
            "/fork thread-source-001",
            "/archive thread-archive-001",
            "/rename thread-rename-001 新标题",
            "/resume thread-resume-001",
            "/rpc diagnostics/trace/read {\"traceId\":\"trace-chat-rpc-001\"}",
        }));

        var approvalTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var permissionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inputTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-001" };
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            runtime.HasActiveTurn = true;
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.ApprovalRequested, callId: "approval-chat-001", toolName: "shell"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.PermissionRequested, callId: "permission-chat-001", toolName: "request_permissions"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.UserInputRequested, callId: "input-chat-001"));
            await approvalTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            await permissionTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            await inputTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextDelta, text: "收到：" + message));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextCompleted));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.TurnCompleted, runtime.ActiveThreadId, "turn-chat-send-001", status: "completed"));
            runtime.HasActiveTurn = false;
            return AgentSendResult.Ok(message, turnId: "turn-chat-send-001", turnStatus: "completed");
        };
        runtime.SendFollowUpAsyncHandler = (message, mode, _, correlationId) =>
        {
            Assert.False(string.IsNullOrWhiteSpace(correlationId));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextDelta, text: $"follow-up:{mode}:{message}"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextCompleted));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.TurnCompleted, runtime.ActiveThreadId, "turn-chat-followup-001", status: "completed"));
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-followup-001", turnStatus: "completed", correlationId: correlationId, requestedMode: mode, effectiveMode: mode));
        };
        runtime.RespondToApprovalAsyncHandler = (_, _) => { approvalTcs.TrySetResult(true); return Task.FromResult(true); };
        runtime.RespondToPermissionRequestAsyncHandler = (response, _) =>
        {
            Assert.Equal(ControlPlanePermissionScope.Session, response.Scope);
            permissionTcs.TrySetResult(true);
            return Task.FromResult(true);
        };
        runtime.RespondToUserInputAsyncHandler = (_, _) => { inputTcs.TrySetResult(true); return Task.FromResult(true); };
        runtime.InterruptAsyncHandler = static _ => Task.CompletedTask;
        runtime.ListThreadsAsyncHandler = static (limit, archived, matchCurrentCwd, _) => Task.FromResult<IReadOnlyList<AgentThreadInfo>>(
        [
            new AgentThreadInfo
            {
                ThreadId = "thread-chat-001",
                Preview = "当前聊天线程",
                Name = "配置 GUI 会话",
                Cwd = matchCurrentCwd ? "D:/Work/TianShu" : null,
                UpdatedAt = new DateTimeOffset(2026, 3, 11, 13, 0, 0, TimeSpan.Zero),
            },
        ]);
        runtime.StartNewThreadAsyncHandler = _ => Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo { ThreadId = "thread-new-chat-001", Preview = "新聊天线程", Cwd = workspace.RootPath, UpdatedAt = DateTimeOffset.UtcNow });
        runtime.ForkThreadAsyncHandler = static (threadId, _) => Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo { ThreadId = threadId + "-fork", Preview = "分叉聊天线程", Cwd = "D:/Work/TianShu", UpdatedAt = DateTimeOffset.UtcNow });
        runtime.ArchiveThreadAsyncHandler = static (_, _) => Task.FromResult(true);
        runtime.RenameThreadAsyncHandler = static (_, _, _) => Task.FromResult(true);
        runtime.ResumeThreadAsyncHandler = static (threadId, _) => Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
        {
            ThreadId = threadId,
            Preview = "恢复聊天线程",
            Cwd = "D:/Work/TianShu",
            SeedHistory = [new AgentThreadSeedHistoryItem { Role = "user", Content = "hello" }],
            Turns = [new AgentThreadTurn { Id = "turn-resume-chat-001", Status = "completed" }],
        });
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Contains("天枢 TianShu 已启动。", stdout, StringComparison.Ordinal);
        Assert.Contains("收到：需要审批", stdout, StringComparison.Ordinal);
        Assert.Contains("follow-up:Queue:再来一轮", stdout, StringComparison.Ordinal);
        Assert.Contains("已请求中断当前回合，等待确认。", stdout, StringComparison.Ordinal);
        Assert.Contains("配置 GUI 会话", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("thread-chat-001\t", stdout, StringComparison.Ordinal);
        Assert.Contains(new DateTimeOffset(2026, 3, 11, 13, 0, 0, TimeSpan.Zero).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), stdout, StringComparison.Ordinal);
        Assert.Contains("已创建线程：thread-new-chat-001", stdout, StringComparison.Ordinal);
        Assert.Contains("已分叉线程：thread-source-001-fork", stdout, StringComparison.Ordinal);
        Assert.Contains("已恢复线程：thread-resume-001（种子历史 1，回合数 1）", stdout, StringComparison.Ordinal);
        Assert.Single(runtime.SendCalls);
        Assert.Single(runtime.FollowUpCalls);
        Assert.NotNull(runtime.InitializedOptions);
        Assert.False(runtime.InitializedOptions!.CreateThreadOnInitialize);
        Assert.Equal(1, runtime.InterruptCallCount);
        Assert.Single(runtime.PermissionResponses);
        Assert.Collection(
            runtime.ThreadListCalls,
            call =>
            {
                Assert.Equal(20, call.Limit);
                Assert.False(call.Archived);
                Assert.True(call.MatchCurrentCwd);
            });
        Assert.Single(runtime.StartThreadCalls);
        Assert.Single(runtime.ForkThreadCalls);
        Assert.Single(runtime.ResumeThreadCalls);
        Assert.Empty(runtime.RpcCalls);
        Assert.Collection(
            runtime.ExecutionTraceCalls,
            call => Assert.Equal("trace-chat-rpc-001", call.TraceId.Value));
        Assert.Single(runtime.ApprovalResponses);
        Assert.Equal(ControlPlaneApprovalDecision.ApproveForSession, runtime.ApprovalResponses[0].Decision);
    }

    [Fact]
    public async Task InteractiveChatRunner_ResumeHydratesPendingInteractiveRequestsIntoCliState()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-resume-hydrate.txt", string.Join(Environment.NewLine, new[]
        {
            "/resume thread-resume-hydrate-001",
            "/state",
            "/approve approval-resume-001",
            "/permissions permission-resume-001 {\"scope\":\"session\",\"permissions\":{\"network\":{\"enabled\":true}}}",
            "/input input-resume-001 {\"config_path\":\".tianshu/tianshu.toml\"}",
            "/exit",
        }));

        var runtime = new CliConsumerFakeRuntime();
        runtime.ResumeThreadAsyncHandler = static (threadId, _) => Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
        {
            ThreadId = threadId,
            Preview = "恢复待处理交互线程",
            Cwd = "D:/Work/TianShu",
            SeedHistory = [new AgentThreadSeedHistoryItem { Role = "user", Content = "resume hydrate" }],
            Turns = [new AgentThreadTurn { Id = "turn-resume-hydrate-001", Status = "in_progress" }],
            PendingInteractiveRequests =
            [
                ToControlPlanePendingInteractiveRequest(new PendingInteractiveRequestReplay
                {
                    RequestId = 901,
                    RequestKind = "approval_requested",
                    RequestMethod = "item/tool/requestApproval",
                    CallId = "approval-resume-001",
                    ThreadId = threadId,
                    TurnId = "turn-resume-hydrate-001",
                    ToolName = "browser",
                    Text = "需要批准 browser 调用",
                    Status = "awaitingApproval",
                    Phase = "request_approval",
                    RequiresApproval = true,
                    AvailableDecisions = ["accept", "acceptForSession", "decline"],
                    ApprovalRequest = new ApprovalRequestPayload(
                        "browser",
                        null,
                        ["accept", "acceptForSession", "decline"],
                        "需要批准 browser 调用",
                        Array.Empty<ApprovalMetadataFieldPayload>())
                }),
                ToControlPlanePendingInteractiveRequest(new PendingInteractiveRequestReplay
                {
                    RequestId = 902,
                    RequestKind = "permission_requested",
                    RequestMethod = "item/permissions/requestApproval",
                    CallId = "permission-resume-001",
                    ThreadId = threadId,
                    TurnId = "turn-resume-hydrate-001",
                    ToolName = "request_permissions",
                    Text = "需要更高权限",
                    Status = "awaitingPermission",
                    Phase = "request_permission",
                    PermissionRequest = new PermissionRequestPayload(
                        "需要更高权限",
                        Array.Empty<PermissionFieldPayload>(),
                        "{\"network\":{\"enabled\":true}}",
                        "需要更高权限")
                }),
                ToControlPlanePendingInteractiveRequest(new PendingInteractiveRequestReplay
                {
                    RequestId = 903,
                    RequestKind = "request_user_input",
                    RequestMethod = "item/tool/requestUserInput",
                    CallId = "input-resume-001",
                    ThreadId = threadId,
                    TurnId = "turn-resume-hydrate-001",
                    ToolName = "requestUserInput",
                    Text = "请选择配置文件",
                    Status = "awaitingUserInput",
                    Phase = "request_user_input",
                    UserInputRequest = new UserInputRequestPayload(
                        [new UserInputQuestionPayload("config_path", "配置文件", "请选择配置文件", false, true, null)],
                        "请选择配置文件")
                }),
            ],
        });

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Contains("已恢复线程：thread-resume-hydrate-001（种子历史 1，回合数 1）", stdout, StringComparison.Ordinal);
        Assert.Contains("已回放待处理交互：审批 1，权限 1，补录 1。", stdout, StringComparison.Ordinal);
        Assert.Contains("\"approval-resume-001\"", stdout, StringComparison.Ordinal);
        Assert.Contains("\"permission-resume-001\"", stdout, StringComparison.Ordinal);
        Assert.Contains("\"input-resume-001\"", stdout, StringComparison.Ordinal);
        Assert.Single(runtime.ApprovalResponses);
        Assert.Equal("approval-resume-001", runtime.ApprovalResponses[0].CallId.Value);
        Assert.Single(runtime.PermissionResponses);
        Assert.Equal("permission-resume-001", runtime.PermissionResponses[0].CallId.Value);
        Assert.Single(runtime.UserInputResponses);
        Assert.Equal("input-resume-001", runtime.UserInputResponses[0].CallId.Value);
    }

    [Fact]
    public async Task InteractiveChatRunner_StartupResumeHydratesPendingInteractiveRequestsIntoCliState()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-startup-resume-hydrate.txt", string.Join(Environment.NewLine, new[]
        {
            "/state",
            "/approve approval-startup-001",
            "/permissions permission-startup-001 {\"scope\":\"session\",\"permissions\":{\"network\":{\"enabled\":true}}}",
            "/input input-startup-001 {\"config_path\":\".tianshu/tianshu.toml\"}",
            "/exit",
        }));

        var runtime = new CliConsumerFakeRuntime();
        runtime.InitializeAsyncHandler = (_, _) =>
        {
            runtime.ActiveThreadId = "thread-startup-resume-001";
            return Task.CompletedTask;
        };
        runtime.ResumeThreadAsyncHandler = static (threadId, _) => Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
        {
            ThreadId = threadId,
            Preview = "启动期恢复待处理交互线程",
            Cwd = "D:/Work/TianShu",
            SeedHistory = [new AgentThreadSeedHistoryItem { Role = "user", Content = "startup resume hydrate" }],
            Turns = [new AgentThreadTurn { Id = "turn-startup-resume-001", Status = "in_progress" }],
            PendingInteractiveRequests =
            [
                ToControlPlanePendingInteractiveRequest(new PendingInteractiveRequestReplay
                {
                    RequestId = 1001,
                    RequestKind = "approval_requested",
                    RequestMethod = "item/tool/requestApproval",
                    CallId = "approval-startup-001",
                    ThreadId = threadId,
                    TurnId = "turn-startup-resume-001",
                    ToolName = "browser",
                    Text = "需要批准 browser 调用",
                    Status = "awaitingApproval",
                    Phase = "request_approval",
                    RequiresApproval = true,
                    AvailableDecisions = ["accept", "acceptForSession", "decline"],
                    ApprovalRequest = new ApprovalRequestPayload(
                        "browser",
                        null,
                        ["accept", "acceptForSession", "decline"],
                        "需要批准 browser 调用",
                        Array.Empty<ApprovalMetadataFieldPayload>())
                }),
                ToControlPlanePendingInteractiveRequest(new PendingInteractiveRequestReplay
                {
                    RequestId = 1002,
                    RequestKind = "permission_requested",
                    RequestMethod = "item/permissions/requestApproval",
                    CallId = "permission-startup-001",
                    ThreadId = threadId,
                    TurnId = "turn-startup-resume-001",
                    ToolName = "request_permissions",
                    Text = "需要更高权限",
                    Status = "awaitingPermission",
                    Phase = "request_permission",
                    PermissionRequest = new PermissionRequestPayload(
                        "需要更高权限",
                        Array.Empty<PermissionFieldPayload>(),
                        "{\"network\":{\"enabled\":true}}",
                        "需要更高权限")
                }),
                ToControlPlanePendingInteractiveRequest(new PendingInteractiveRequestReplay
                {
                    RequestId = 1003,
                    RequestKind = "request_user_input",
                    RequestMethod = "item/tool/requestUserInput",
                    CallId = "input-startup-001",
                    ThreadId = threadId,
                    TurnId = "turn-startup-resume-001",
                    ToolName = "requestUserInput",
                    Text = "请选择配置文件",
                    Status = "awaitingUserInput",
                    Phase = "request_user_input",
                    UserInputRequest = new UserInputRequestPayload(
                        [new UserInputQuestionPayload("config_path", "配置文件", "请选择配置文件", false, true, null)],
                        "请选择配置文件")
                }),
            ],
        });

        var command = ParseCliCommand(
            WithCommonOptions(
                workspace,
                "chat",
                "--script",
                scriptPath,
                "--resume-thread-id",
                "thread-startup-resume-001"));
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Single(runtime.ResumeThreadCalls);
        Assert.Equal("thread-startup-resume-001", runtime.ResumeThreadCalls[0]);
        Assert.Contains("已回放待处理交互：审批 1，权限 1，补录 1。", stdout, StringComparison.Ordinal);
        Assert.Contains("\"approval-startup-001\"", stdout, StringComparison.Ordinal);
        Assert.Contains("\"permission-startup-001\"", stdout, StringComparison.Ordinal);
        Assert.Contains("\"input-startup-001\"", stdout, StringComparison.Ordinal);
        Assert.Single(runtime.ApprovalResponses);
        Assert.Equal("approval-startup-001", runtime.ApprovalResponses[0].CallId.Value);
        var startupApprovalPayload = AssertCliGovernanceEnvelope(
            runtime.ApprovalResponses[0].Envelope,
            "cli-approval-approval-startup-001",
            "approval_response");
        Assert.Equal("approval-startup-001", startupApprovalPayload.Payload.Properties["callId"].GetString());
        Assert.Single(runtime.PermissionResponses);
        Assert.Equal("permission-startup-001", runtime.PermissionResponses[0].CallId.Value);
        var startupPermissionPayload = AssertCliGovernanceEnvelope(
            runtime.PermissionResponses[0].Envelope,
            "cli-permission-permission-startup-001",
            "permission_response");
        Assert.Equal("session", startupPermissionPayload.Payload.Properties["scope"].GetString());
        Assert.Single(runtime.UserInputResponses);
        Assert.Equal("input-startup-001", runtime.UserInputResponses[0].CallId.Value);
        var startupUserInputPayload = AssertCliGovernanceEnvelope(
            runtime.UserInputResponses[0].Envelope,
            "cli-userinput-input-startup-001",
            "user_input_submission");
        Assert.Equal(".tianshu/tianshu.toml", startupUserInputPayload.Payload.Properties["answers"].Properties["config_path"].GetString());
    }

    [Fact]
    public async Task InteractiveChatRunner_TurnCompletedClearsHydratedPendingInteractiveRequestsBeforeStateSnapshot()
    {
        using var workspace = new CliConsumerWorkspace();
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-resume-pending-cleanup-001" };
        var command = ParseCliCommand("chat", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.Interaction.Host.InteractiveChatSessionHost", runtime);
        var approvalEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.ApprovalRequested,
            runtime.ActiveThreadId,
            "turn-resume-pending-cleanup-001",
            text: "需要批准 shell 调用",
            callId: "approval-pending-cleanup-001",
            toolName: "shell");
        var permissionEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.PermissionRequested,
            runtime.ActiveThreadId,
            "turn-resume-pending-cleanup-001",
            text: "需要更高权限",
            callId: "permission-pending-cleanup-001",
            toolName: "request_permissions");
        var userInputEvent = CreateStreamEvent(
            ControlPlaneConversationStreamEventKind.UserInputRequested,
            runtime.ActiveThreadId,
            "turn-resume-pending-cleanup-001",
            text: "请选择配置文件",
            callId: "input-pending-cleanup-001",
            toolName: "requestUserInput");

        await CaptureAsync(() =>
        {
            ReflectionTestHelper.InvokeMethod(runner, "OnStreamEvent", runtime, command, null, null, approvalEvent, CancellationToken.None);
            ReflectionTestHelper.InvokeMethod(runner, "OnStreamEvent", runtime, command, null, null, permissionEvent, CancellationToken.None);
            ReflectionTestHelper.InvokeMethod(runner, "OnStreamEvent", runtime, command, null, null, userInputEvent, CancellationToken.None);
            return Task.FromResult(0);
        });

        var pendingRequests = (PendingInteractiveRequestStore)ReflectionTestHelper.GetField(runner, "pendingInteractiveRequests")!;
        Assert.Equal(1, pendingRequests.Snapshot.ApprovalCount);
        Assert.Equal(1, pendingRequests.Snapshot.PermissionCount);
        Assert.Equal(1, pendingRequests.Snapshot.UserInputCount);

        await CaptureAsync(() =>
        {
            ReflectionTestHelper.InvokeMethod(
                runner,
                "OnStreamEvent",
                runtime,
                command,
                null,
                null,
                CreateStreamEvent(
                    ControlPlaneConversationStreamEventKind.ServerRequestResolved,
                    runtime.ActiveThreadId,
                    "turn-resume-pending-cleanup-001",
                    callId: "approval-pending-cleanup-001"),
                CancellationToken.None);
            return Task.FromResult(0);
        });
        Assert.Equal(0, pendingRequests.Snapshot.ApprovalCount);
        Assert.Equal(1, pendingRequests.Snapshot.PermissionCount);
        Assert.Equal(1, pendingRequests.Snapshot.UserInputCount);

        await CaptureAsync(() =>
        {
            ReflectionTestHelper.InvokeMethod(
                runner,
                "OnStreamEvent",
                runtime,
                command,
                null,
                null,
                CreateStreamEvent(
                    ControlPlaneConversationStreamEventKind.TurnCompleted,
                    runtime.ActiveThreadId,
                    "turn-resume-pending-cleanup-001",
                    status: "completed"),
                CancellationToken.None);
            return Task.FromResult(0);
        });

        Assert.Equal(0, pendingRequests.Snapshot.ApprovalCount);
        Assert.Equal(0, pendingRequests.Snapshot.PermissionCount);
        Assert.Equal(0, pendingRequests.Snapshot.UserInputCount);
    }

    [Fact]
    public async Task InteractiveChatRunner_ResumeRestoresPendingFollowUpsIntoDraftState()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-resume-followups.txt", string.Join(Environment.NewLine, new[]
        {
            "/resume thread-resume-followup-001",
            "/state",
            "/exit",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-resume-followup-001" };
        runtime.ResumeThreadAsyncHandler = (threadId, _) =>
        {
            runtime.ActiveThreadId = threadId;
            runtime.HasActiveTurn = false;
            return Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
            {
                ThreadId = threadId,
                Preview = "恢复待发送 follow-up 线程",
                Cwd = "D:/Work/TianShu",
                SeedHistory = [new AgentThreadSeedHistoryItem { Role = "user", Content = "resume followups" }],
                Turns = [new AgentThreadTurn { Id = "turn-resume-active-001", Status = "in_progress" }],
                PendingInputState = ToControlPlanePendingInputState(new PendingInputStatePayload(
                    Entries: Array.Empty<PendingInputStateEntryPayload>(),
                    InterruptRequestPending: false,
                    SubmitPendingSteersAfterInterrupt: false,
                    QueuedUserMessages:
                    [
                        new PendingInputStateEntryPayload(
                            "corr-resume-queue-001",
                            "Queue",
                            "Queue",
                            "awaiting_commit",
                            "turn-resume-active-001",
                            null,
                            new PendingFollowUpCompareKeyPayload("恢复后的排队消息", 0),
                            "QueuedUserMessage",
                            [new TextUserInput { Type = "text", Text = "恢复后的排队消息" }]),
                    ],
                    PendingSteers:
                    [
                        new PendingInputStateEntryPayload(
                            "corr-resume-steer-001",
                            "Steer",
                            "Steer",
                            "awaiting_commit",
                            "turn-resume-active-001",
                            null,
                            new PendingFollowUpCompareKeyPayload("恢复后的引导消息", 0),
                            "PendingSteer",
                            [new TextUserInput { Type = "text", Text = "恢复后的引导消息" }]),
                    ])),
            });
        };
        runtime.SendFollowUpAsyncHandler = static (_, _, _, _) =>
            throw new Xunit.Sdk.XunitException("恢复后的 pending follow-up 不应自动发送。");

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Empty(runtime.FollowUpCalls);
        Assert.Contains("已恢复待编辑 follow-up：2。", stdout, StringComparison.Ordinal);
        Assert.Contains("已恢复到待编辑 Queue follow-up：恢复后的排队消息", stdout, StringComparison.Ordinal);
        var stateJson = stdout[stdout.IndexOf('{')..];
        using var state = JsonDocument.Parse(stateJson);
        Assert.Equal("恢复后的排队消息", state.RootElement.GetProperty("restoredComposerDraftPreview").GetString());
        Assert.Equal("Queue", state.RootElement.GetProperty("restoredComposerDraftMode").GetString());
        Assert.Equal("corr-resume-queue-001", state.RootElement.GetProperty("restoredComposerDraftCorrelationId").GetString());
        Assert.Equal(1, state.RootElement.GetProperty("queuedRestoredFollowUpCount").GetInt32());
        var restoredPendingFollowUpCorrelations = state.RootElement
            .GetProperty("restoredPendingFollowUpCorrelations")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        Assert.Collection(
            restoredPendingFollowUpCorrelations,
            item => Assert.Equal("corr-resume-queue-001", item),
            item => Assert.Equal("corr-resume-steer-001", item));
    }

    [Fact]
    public async Task InteractiveChatRunner_ResumeIgnoresCompareKeyOnlyPendingFollowUps()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-resume-comparekey-only.txt", string.Join(Environment.NewLine, new[]
        {
            "/resume thread-resume-comparekey-only-001",
            "/wait-complete 3",
            "/exit",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-resume-comparekey-only-001" };
        runtime.ResumeThreadAsyncHandler = (threadId, cancellationToken) =>
        {
            runtime.ActiveThreadId = threadId;
            runtime.HasActiveTurn = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                runtime.HasActiveTurn = false;
                EmitProjectedStreamEvent(runtime, new AgentStreamEvent
                {
                    Kind = AgentStreamEventKind.TurnCompleted,
                    ThreadId = threadId,
                    TurnId = "turn-resume-comparekey-only-001",
                    Status = "completed",
                });
            }, cancellationToken);

            return Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
            {
                ThreadId = threadId,
                Preview = "compareKey-only 恢复验证",
                Cwd = workspace.RootPath,
                SeedHistory = [new AgentThreadSeedHistoryItem { Role = "user", Content = "resume compareKey only" }],
                Turns = [new AgentThreadTurn { Id = "turn-resume-comparekey-only-001", Status = "in_progress" }],
                PendingInputState = ToControlPlanePendingInputState(new PendingInputStatePayload(
                    Entries: Array.Empty<PendingInputStateEntryPayload>(),
                    InterruptRequestPending: false,
                    SubmitPendingSteersAfterInterrupt: false,
                    QueuedUserMessages:
                    [
                        new PendingInputStateEntryPayload(
                            "corr-resume-comparekey-only-001",
                            "Queue",
                            "Queue",
                            "awaiting_commit",
                            "turn-resume-comparekey-only-001",
                            null,
                            new PendingFollowUpCompareKeyPayload("只剩 compareKey 的排队消息", 0),
                            "QueuedUserMessage",
                            Array.Empty<AgentUserInput>()),
                    ],
                    PendingSteers: Array.Empty<PendingInputStateEntryPayload>())),
            });
        };
        runtime.SendFollowUpAsyncHandler = static (_, _, _, _) =>
            throw new Xunit.Sdk.XunitException("compareKey-only pending follow-up 不应被自动恢复发送。");

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        Assert.DoesNotContain("已恢复待发送 follow-up：", stdout, StringComparison.Ordinal);
        Assert.Empty(runtime.FollowUpCalls);
    }

    [Fact]
    public async Task InteractiveChatRunner_StartupResumeRestoresEntriesIntoQueueDraftAfterInterrupt()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-startup-resume-entries.txt", string.Join(Environment.NewLine, new[]
        {
            "/state",
            "/exit",
        }));

        var runtime = new CliConsumerFakeRuntime();
        runtime.InitializeAsyncHandler = (_, _) =>
        {
            runtime.ActiveThreadId = "thread-startup-followup-001";
            runtime.HasActiveTurn = false;
            return Task.CompletedTask;
        };
        runtime.ResumeThreadAsyncHandler = static (threadId, _) => Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
        {
            ThreadId = threadId,
            Preview = "启动期恢复 follow-up 线程",
            Cwd = "D:/Work/TianShu",
            SeedHistory = [new AgentThreadSeedHistoryItem { Role = "user", Content = "startup followups" }],
            Turns = [new AgentThreadTurn { Id = "turn-startup-followup-001", Status = "interrupted" }],
            PendingInputState = ToControlPlanePendingInputState(new PendingInputStatePayload(
                Entries:
                [
                    new PendingInputStateEntryPayload(
                        "corr-startup-steer-001",
                        "Steer",
                        "Steer",
                        "awaiting_commit",
                        null,
                        null,
                        new PendingFollowUpCompareKeyPayload("恢复后的引导消息", 0),
                        "PendingSteer",
                        [new TextUserInput { Type = "text", Text = "恢复后的引导消息" }]),
                ],
                InterruptRequestPending: false,
                SubmitPendingSteersAfterInterrupt: true,
                QueuedUserMessages: Array.Empty<PendingInputStateEntryPayload>(),
                PendingSteers: Array.Empty<PendingInputStateEntryPayload>())),
        });
        runtime.SendFollowUpAsyncHandler = static (_, _, _, _) =>
            throw new Xunit.Sdk.XunitException("启动恢复后的 pending follow-up 不应自动发送。");

        var command = ParseCliCommand(
            WithCommonOptions(
                workspace,
                "chat",
                "--script",
                scriptPath,
                "--resume-thread-id",
                "thread-startup-followup-001"));
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Single(runtime.ResumeThreadCalls);
        Assert.Empty(runtime.FollowUpCalls);
        Assert.Contains("已恢复待编辑 follow-up：1。", stdout, StringComparison.Ordinal);
        Assert.Contains("已恢复到待编辑 Queue follow-up：恢复后的引导消息", stdout, StringComparison.Ordinal);
        var stateJson = stdout[stdout.IndexOf('{')..];
        using var state = JsonDocument.Parse(stateJson);
        Assert.Equal("恢复后的引导消息", state.RootElement.GetProperty("restoredComposerDraftPreview").GetString());
        Assert.Equal("Queue", state.RootElement.GetProperty("restoredComposerDraftMode").GetString());
        Assert.Equal(0, state.RootElement.GetProperty("queuedRestoredFollowUpCount").GetInt32());
    }

    [Fact]
    public async Task InteractiveChatRunner_SendRestoredDraft_DispatchesCurrentDraftWithoutEditing()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-send-restored.txt", string.Join(Environment.NewLine, new[]
        {
            "/resume thread-send-restored-001",
            "/send-restored",
            "/wait-complete 5",
            "/exit",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-send-restored-001" };
        runtime.ResumeThreadAsyncHandler = (threadId, _) =>
        {
            runtime.ActiveThreadId = threadId;
            runtime.HasActiveTurn = false;
            return Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
            {
                ThreadId = threadId,
                Preview = "恢复草稿发送验证",
                Cwd = workspace.RootPath,
                SeedHistory = [new AgentThreadSeedHistoryItem { Role = "user", Content = "resume draft send" }],
                Turns = [new AgentThreadTurn { Id = "turn-send-restored-001", Status = "interrupted" }],
                PendingInputState = ToControlPlanePendingInputState(new PendingInputStatePayload(
                    Entries:
                    [
                        new PendingInputStateEntryPayload(
                            "corr-send-restored-001",
                            "Queue",
                            "Queue",
                            "awaiting_commit",
                            null,
                            null,
                            new PendingFollowUpCompareKeyPayload("恢复后的草稿消息", 0),
                            "QueuedUserMessage",
                            [new TextUserInput { Type = "text", Text = "恢复后的草稿消息" }]),
                    ],
                    InterruptRequestPending: false,
                    SubmitPendingSteersAfterInterrupt: false,
                    QueuedUserMessages: Array.Empty<PendingInputStateEntryPayload>(),
                    PendingSteers: Array.Empty<PendingInputStateEntryPayload>())),
            });
        };
        runtime.SendFollowUpAsyncHandler = async (message, mode, cancellationToken, correlationId) =>
        {
            Assert.Equal("恢复后的草稿消息", message);
            Assert.Equal(FollowUpMode.Queue, mode);
            Assert.Equal("corr-send-restored-001", correlationId);
            runtime.HasActiveTurn = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                runtime.HasActiveTurn = false;
                EmitProjectedStreamEvent(runtime, new AgentStreamEvent
                {
                    Kind = AgentStreamEventKind.TurnCompleted,
                    ThreadId = runtime.ActiveThreadId,
                    TurnId = "turn-send-restored-dispatched-001",
                    Status = "completed",
                });
            }, cancellationToken);

            return AgentSendResult.Ok(message, turnId: "turn-send-restored-dispatched-001", turnStatus: "submitted", correlationId: correlationId, requestedMode: mode, effectiveMode: mode);
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Single(runtime.FollowUpCalls);
        Assert.Equal("恢复后的草稿消息", runtime.FollowUpCalls[0].UserMessage);
        Assert.Equal(FollowUpMode.Queue, runtime.FollowUpCalls[0].Mode);
        Assert.Equal("corr-send-restored-001", runtime.FollowUpCalls[0].CorrelationId);
        Assert.Contains("已发送恢复草稿 Queue follow-up：恢复后的草稿消息", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_SendRestoredDraft_WaitsForTurnCompletionBeforePromotingNextDraft()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-send-restored-sequencing.txt", string.Join(Environment.NewLine, new[]
        {
            "/resume thread-send-restored-sequencing-001",
            "/send-restored",
            "/wait 50",
            "/state",
            "/wait-complete 5",
            "/state",
            "/exit",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-send-restored-sequencing-001" };
        runtime.ResumeThreadAsyncHandler = (threadId, _) =>
        {
            runtime.ActiveThreadId = threadId;
            runtime.HasActiveTurn = false;
            return Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
            {
                ThreadId = threadId,
                Preview = "恢复草稿推进时序验证",
                Cwd = workspace.RootPath,
                SeedHistory = [new AgentThreadSeedHistoryItem { Role = "user", Content = "resume draft sequencing" }],
                Turns = [new AgentThreadTurn { Id = "turn-send-restored-sequencing-001", Status = "interrupted" }],
                PendingInputState = ToControlPlanePendingInputState(new PendingInputStatePayload(
                    Entries:
                    [
                        new PendingInputStateEntryPayload(
                            "corr-send-restored-first-001",
                            "Queue",
                            "Queue",
                            "awaiting_commit",
                            null,
                            null,
                            new PendingFollowUpCompareKeyPayload("恢复后的第一条草稿", 0),
                            "QueuedUserMessage",
                            [new TextUserInput { Type = "text", Text = "恢复后的第一条草稿" }]),
                        new PendingInputStateEntryPayload(
                            "corr-send-restored-second-001",
                            "Steer",
                            "Steer",
                            "awaiting_commit",
                            null,
                            null,
                            new PendingFollowUpCompareKeyPayload("恢复后的第二条草稿", 1),
                            "PendingSteer",
                            [new TextUserInput { Type = "text", Text = "恢复后的第二条草稿" }]),
                    ],
                    InterruptRequestPending: false,
                    SubmitPendingSteersAfterInterrupt: false,
                    QueuedUserMessages: Array.Empty<PendingInputStateEntryPayload>(),
                    PendingSteers: Array.Empty<PendingInputStateEntryPayload>())),
            });
        };
        runtime.SendFollowUpAsyncHandler = async (message, mode, cancellationToken, correlationId) =>
        {
            Assert.Equal("恢复后的第一条草稿", message);
            Assert.Equal(FollowUpMode.Queue, mode);
            Assert.Equal("corr-send-restored-first-001", correlationId);
            runtime.HasActiveTurn = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                runtime.HasActiveTurn = false;
                EmitProjectedStreamEvent(runtime, new AgentStreamEvent
                {
                    Kind = AgentStreamEventKind.TurnCompleted,
                    ThreadId = runtime.ActiveThreadId,
                    TurnId = "turn-send-restored-sequencing-dispatched-001",
                    Status = "completed",
                });
            }, cancellationToken);

            return AgentSendResult.Ok(message, turnId: "turn-send-restored-sequencing-dispatched-001", turnStatus: "submitted", correlationId: correlationId, requestedMode: mode, effectiveMode: mode);
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Single(runtime.FollowUpCalls);
        var stateJsons = ExtractJsonObjects(stdout);
        Assert.True(stateJsons.Count >= 2);

        using var firstState = JsonDocument.Parse(stateJsons[0]);
        Assert.True(firstState.RootElement.GetProperty("restoredComposerDraftPreview").ValueKind is JsonValueKind.Null);
        Assert.Equal("恢复后的第一条草稿", firstState.RootElement.GetProperty("pendingRestoredFollowUpDispatchPreview").GetString());
        Assert.Equal("corr-send-restored-first-001", firstState.RootElement.GetProperty("pendingRestoredFollowUpDispatchCorrelationId").GetString());
        Assert.Equal(1, firstState.RootElement.GetProperty("queuedRestoredFollowUpCount").GetInt32());

        using var secondState = JsonDocument.Parse(stateJsons[1]);
        Assert.Equal("恢复后的第二条草稿", secondState.RootElement.GetProperty("restoredComposerDraftPreview").GetString());
        Assert.Equal("Steer", secondState.RootElement.GetProperty("restoredComposerDraftMode").GetString());
        Assert.True(secondState.RootElement.GetProperty("pendingRestoredFollowUpDispatchPreview").ValueKind is JsonValueKind.Null);
        Assert.Equal(0, secondState.RootElement.GetProperty("queuedRestoredFollowUpCount").GetInt32());

        var secondPromotionIndex = stdout.IndexOf("已恢复到待编辑 Steer follow-up：恢复后的第二条草稿", StringComparison.Ordinal);
        Assert.DoesNotContain("回合完成", stdout, StringComparison.Ordinal);
        Assert.True(secondPromotionIndex >= 0);
    }

    [Fact]
    public async Task InteractiveChatRunner_SendRestoredDraft_WhenDispatchFails_PreservesDraftAndDoesNotPromoteNext()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-send-restored-fail.txt", string.Join(Environment.NewLine, new[]
        {
            "/resume thread-send-restored-fail-001",
            "/send-restored",
            "/wait 50",
            "/state",
            "/exit",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-send-restored-fail-001" };
        runtime.ResumeThreadAsyncHandler = (threadId, _) =>
        {
            runtime.ActiveThreadId = threadId;
            runtime.HasActiveTurn = false;
            return Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
            {
                ThreadId = threadId,
                Preview = "恢复草稿失败保留验证",
                Cwd = workspace.RootPath,
                SeedHistory = [new AgentThreadSeedHistoryItem { Role = "user", Content = "resume draft failure" }],
                Turns = [new AgentThreadTurn { Id = "turn-send-restored-fail-001", Status = "interrupted" }],
                PendingInputState = ToControlPlanePendingInputState(new PendingInputStatePayload(
                    Entries:
                    [
                        new PendingInputStateEntryPayload(
                            "corr-send-restored-fail-first-001",
                            "Queue",
                            "Queue",
                            "awaiting_commit",
                            null,
                            null,
                            new PendingFollowUpCompareKeyPayload("恢复失败时应保留的草稿", 0),
                            "QueuedUserMessage",
                            [new TextUserInput { Type = "text", Text = "恢复失败时应保留的草稿" }]),
                        new PendingInputStateEntryPayload(
                            "corr-send-restored-fail-second-001",
                            "Queue",
                            "Queue",
                            "awaiting_commit",
                            null,
                            null,
                            new PendingFollowUpCompareKeyPayload("恢复失败时不应提前提升的下一条", 1),
                            "QueuedUserMessage",
                            [new TextUserInput { Type = "text", Text = "恢复失败时不应提前提升的下一条" }]),
                    ],
                    InterruptRequestPending: false,
                    SubmitPendingSteersAfterInterrupt: false,
                    QueuedUserMessages: Array.Empty<PendingInputStateEntryPayload>(),
                    PendingSteers: Array.Empty<PendingInputStateEntryPayload>())),
            });
        };
        runtime.SendFollowUpAsyncHandler = (message, mode, _, correlationId) =>
        {
            Assert.Equal("恢复失败时应保留的草稿", message);
            Assert.Equal(FollowUpMode.Queue, mode);
            Assert.Equal("corr-send-restored-fail-first-001", correlationId);
            return Task.FromResult(AgentSendResult.Fail("模拟失败", turnId: "turn-send-restored-fail-dispatched-001", turnStatus: "failed", correlationId: correlationId, requestedMode: mode, effectiveMode: mode));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.Single(runtime.FollowUpCalls);
        Assert.DoesNotContain("已恢复到待编辑 Queue follow-up：恢复失败时不应提前提升的下一条", stdout, StringComparison.Ordinal);

        var stateJson = ExtractJsonObjects(stdout).Single();
        using var state = JsonDocument.Parse(stateJson);
        Assert.Equal("恢复失败时应保留的草稿", state.RootElement.GetProperty("restoredComposerDraftPreview").GetString());
        Assert.Equal("corr-send-restored-fail-first-001", state.RootElement.GetProperty("restoredComposerDraftCorrelationId").GetString());
        Assert.True(state.RootElement.GetProperty("pendingRestoredFollowUpDispatchPreview").ValueKind is JsonValueKind.Null);
        Assert.Equal(1, state.RootElement.GetProperty("queuedRestoredFollowUpCount").GetInt32());
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WaitNextToolCall_IgnoresApprovalPhase_ThenSendsSteer()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-steer.txt", string.Join(Environment.NewLine, new[]
        {
            "开始执行",
            "/wait-next-tool-call 2",
            "/follow-up steer 中途引导",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-steer-001" };
        var realToolStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var steerAccepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            runtime.HasActiveTurn = true;
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallStarted,
                runtime.ActiveThreadId,
                "turn-chat-steer-001",
                callId: "call-approval-001",
                toolName: "shell",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        "item-approval-001",
                        "call-approval-001",
                        "shell",
                        null,
                        "approval gate",
                        null,
                        "in_progress",
                        "request_approval",
                        true)))));

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallStarted,
                runtime.ActiveThreadId,
                "turn-chat-steer-001",
                callId: "call-real-001",
                toolName: "shell",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        "item-real-001",
                        "call-real-001",
                        "shell",
                        null,
                        "Get-ChildItem",
                        null,
                        "in_progress",
                        "started",
                        false)))));
            realToolStarted.TrySetResult(true);

            await steerAccepted.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextDelta, text: "mid-turn steer ok"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextCompleted, text: "mid-turn steer ok"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.TurnCompleted, runtime.ActiveThreadId, "turn-chat-steer-001", status: "completed"));
            runtime.HasActiveTurn = false;
            return AgentSendResult.Ok(message, turnId: "turn-chat-steer-001", turnStatus: "completed");
        };
        runtime.SendFollowUpAsyncHandler = (message, mode, _, correlationId) =>
        {
            Assert.Equal(FollowUpMode.Steer, mode);
            Assert.True(realToolStarted.Task.IsCompleted, "应在真实工具调用开始后才发送 steer。");
            Assert.Equal("中途引导", message);
            Assert.False(string.IsNullOrWhiteSpace(correlationId));
            steerAccepted.TrySetResult(true);
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.TurnSteered, runtime.ActiveThreadId, "turn-chat-steer-001", source: "script"));
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-steer-followup-001", turnStatus: "submitted", correlationId: correlationId, requestedMode: mode, effectiveMode: mode));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Collection(runtime.FollowUpCalls, call =>
        {
            Assert.Equal(("中途引导", FollowUpMode.Steer), (call.UserMessage, call.Mode));
            Assert.False(string.IsNullOrWhiteSpace(call.CorrelationId));
        });
        Assert.Contains("call-real-001", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("call-approval-001", stdout, StringComparison.Ordinal);
        Assert.Contains("mid-turn steer ok", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WritesFollowUpCorrelationIntoArtifacts_AndVerboseOutput()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "chat-followup-artifacts");
        Directory.CreateDirectory(artifactsRoot);
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-followup-artifacts.txt", string.Join(Environment.NewLine, new[]
        {
            "开始执行",
            "/wait-next-tool-call 2",
            "/follow-up steer 中途引导",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-followup-artifacts-001" };
        var steerAccepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            runtime.HasActiveTurn = true;
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallStarted,
                runtime.ActiveThreadId,
                "turn-chat-followup-artifacts-001",
                callId: "call-chat-followup-artifacts-001",
                toolName: "shell",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        "item-chat-followup-artifacts-001",
                        "call-chat-followup-artifacts-001",
                        "shell",
                        null,
                        "Get-ChildItem",
                        null,
                        "in_progress",
                        "started",
                        false)))));

            await steerAccepted.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextDelta, text: "follow-up artifact result"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextCompleted, text: "follow-up artifact result"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.TurnCompleted, runtime.ActiveThreadId, "turn-chat-followup-artifacts-001", status: "completed"));
            runtime.HasActiveTurn = false;
            return AgentSendResult.Ok(message, turnId: "turn-chat-followup-artifacts-001", turnStatus: "completed");
        };
        runtime.SendFollowUpAsyncHandler = (message, mode, _, correlationId) =>
        {
            Assert.Equal(FollowUpMode.Steer, mode);
            Assert.False(string.IsNullOrWhiteSpace(correlationId));
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated,
                runtime.ActiveThreadId,
                "turn-chat-followup-artifacts-001",
                status: "awaiting_commit",
                payloadKind: ControlPlaneConversationStreamPayloadKind.PendingFollowUp,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new PendingFollowUpLifecyclePayload(
                        correlationId!,
                        "Steer",
                        "Steer",
                        "awaiting_commit",
                        "turn-chat-followup-artifacts-001",
                        "turn-chat-followup-artifacts-001",
                        new PendingFollowUpCompareKeyPayload(message, 0))))));
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.UserMessageCommitted,
                runtime.ActiveThreadId,
                "turn-chat-followup-artifacts-001",
                text: message,
                payloadKind: ControlPlaneConversationStreamPayloadKind.CommittedUserMessage,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new CommittedUserMessagePayload(
                        "item-chat-followup-user-001",
                        message,
                        0,
                        correlationId)))));
            steerAccepted.TrySetResult(true);
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-followup-artifacts-followup-001", turnStatus: "submitted", correlationId: correlationId, requestedMode: mode, effectiveMode: mode));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot, "--verbose-events");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Contains("已提交引导信息，等待进入当前回合上下文。", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("运行时已接收引导 follow-up", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("引导内容：", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("正在引导", stdout, StringComparison.Ordinal);
        Assert.Contains("引导成功", stdout, StringComparison.Ordinal);
        Assert.Contains("PendingFollowUpUpdated: correlationId=", stdout, StringComparison.Ordinal);
        Assert.Contains("UserMessageCommitted: text=中途引导, correlationId=", stdout, StringComparison.Ordinal);

        var runDirectory = Assert.Single(Directory.GetDirectories(artifactsRoot));
        var events = File.ReadAllLines(Path.Combine(runDirectory, "events.jsonl"))
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();

        try
        {
            var pendingFollowUpEvent = Assert.Single(events.Where(static document => document.RootElement.GetProperty("Kind").GetString() == "PendingFollowUpUpdated"));
            var committedEvent = Assert.Single(events.Where(static document => document.RootElement.GetProperty("Kind").GetString() == "UserMessageCommitted"));

            var pendingCorrelationId = pendingFollowUpEvent.RootElement.GetProperty("PendingFollowUp").GetProperty("CorrelationId").GetString();
            var committedCorrelationId = committedEvent.RootElement.GetProperty("CommittedUserMessage").GetProperty("CorrelationId").GetString();

            Assert.False(string.IsNullOrWhiteSpace(pendingCorrelationId));
            Assert.Equal(pendingCorrelationId, committedCorrelationId);
            Assert.Equal("awaiting_commit", pendingFollowUpEvent.RootElement.GetProperty("PendingFollowUp").GetProperty("LifecycleState").GetString());
            Assert.Equal("中途引导", committedEvent.RootElement.GetProperty("CommittedUserMessage").GetProperty("Text").GetString());
        }
        finally
        {
            foreach (var document in events)
            {
                document.Dispose();
            }
        }
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WritesArtifactsFiles_WithExpectedPayloads()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "chat-artifacts-output");
        Directory.CreateDirectory(artifactsRoot);
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-artifacts.txt", string.Join(Environment.NewLine, new[]
        {
            "写入 chat 产物",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-artifacts-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.ToolCallStarted,
                ThreadId = runtime.ActiveThreadId,
                TurnId = "turn-chat-artifacts-001",
                ToolName = "shell",
                CallId = "call-chat-artifacts-001",
                ToolCall = new ToolCallEventPayload(
                    "item-chat-artifacts-001",
                    "call-chat-artifacts-001",
                    "shell",
                    null,
                    "Get-Location",
                    null,
                    "in_progress",
                    "started",
                    false),
            });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextDelta, Text = "chat artifact result" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextCompleted, Text = "chat artifact result" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-chat-artifacts-001", Status = "completed" });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-artifacts-001", turnStatus: "completed"));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot);
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("回合完成", stdout, StringComparison.Ordinal);
        var runDirectory = Assert.Single(Directory.GetDirectories(artifactsRoot));
        Assert.True(File.Exists(Path.Combine(runDirectory, "summary.json")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "resolved-options.json")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "events.jsonl")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "projection-records.jsonl")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "transcript-records.jsonl")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "commands.txt")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "transcript.txt")));
        Assert.Contains("写入 chat 产物", File.ReadAllText(Path.Combine(runDirectory, "commands.txt")), StringComparison.Ordinal);
        Assert.Contains("chat artifact result", File.ReadAllText(Path.Combine(runDirectory, "transcript.txt")), StringComparison.Ordinal);
        var eventsJsonl = File.ReadAllText(Path.Combine(runDirectory, "events.jsonl"));
        Assert.Contains("ToolCallStarted", eventsJsonl, StringComparison.Ordinal);
        Assert.DoesNotContain("projection/assistant_block_committed", eventsJsonl, StringComparison.Ordinal);
        var projectionRecordsJsonl = File.ReadAllText(Path.Combine(runDirectory, "projection-records.jsonl"));
        Assert.Contains("\"kind\":\"assistant_delta_received\"", projectionRecordsJsonl, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"assistant_block_committed\"", projectionRecordsJsonl, StringComparison.Ordinal);
        Assert.Contains("chat artifact result", projectionRecordsJsonl, StringComparison.Ordinal);
        var transcriptRecordsJsonl = File.ReadAllText(Path.Combine(runDirectory, "transcript-records.jsonl"));
        Assert.Contains("\"kind\":\"assistant_text\"", transcriptRecordsJsonl, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"lifecycle_debug\"", transcriptRecordsJsonl, StringComparison.Ordinal);
        Assert.Contains("turn-chat-artifacts-001", transcriptRecordsJsonl, StringComparison.Ordinal);

        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "summary.json")));
        Assert.True(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("thread-chat-artifacts-001", summary.RootElement.GetProperty("threadId").GetString());
        Assert.Equal("turn-chat-artifacts-001", summary.RootElement.GetProperty("turnId").GetString());
        Assert.True(summary.RootElement.GetProperty("projectionRecordCount").GetInt32() > 0);
        Assert.True(summary.RootElement.GetProperty("transcriptRecordCount").GetInt32() > 0);
    }
    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WhenTurnFails_ReturnsNonZero()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-fail.txt", string.Join(Environment.NewLine, new[]
        {
            "会失败",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-fail-001" };
        runtime.SendAsyncHandler = static (_, _, _) =>
            Task.FromResult(AgentSendResult.Fail("模拟失败", turnId: "turn-chat-fail-001", turnStatus: "failed"));

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("当前没有运行中的回合。", stdout, StringComparison.Ordinal);
        Assert.Contains("send 执行失败：模拟失败", stderr, StringComparison.Ordinal);
        Assert.Single(runtime.SendCalls);
    }

    [Fact]
    public async Task InteractiveChatRunner_HumanOutput_ShowsToolStatusAndDeduplicatesProviderError()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-tool-error.txt", string.Join(Environment.NewLine, new[]
        {
            "创建文件",
            "/wait-complete 2",
        }));
        const string providerError = "模型请求失败：HTTP 400 BadRequest，{\"error\":{\"message\":\"assistant tool_calls 缺少对应 tool 消息\"}}";

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-tool-error-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallStarted,
                callId: "call-tool-error-001",
                toolName: "write",
                status: "in_progress",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        ItemId: "item-tool-error-001",
                        CallId: "call-tool-error-001",
                        ToolName: "write",
                        ServerName: null,
                        InputText: "C:\\Users\\Example\\Desktop\\TestTianShu\\MainWindow.xaml",
                        OutputText: null,
                        Status: "in_progress",
                        Phase: "started",
                        RequiresApproval: false)))));
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallStarted,
                callId: "call-tool-error-001",
                toolName: "write",
                status: "hook",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        ItemId: "item-tool-error-001",
                        CallId: "call-tool-error-001",
                        ToolName: "write",
                        ServerName: null,
                        InputText: "内部 hook",
                        OutputText: null,
                        Status: "hook",
                        Phase: "hook",
                        RequiresApproval: false)))));
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallCompleted,
                callId: "call-tool-error-001",
                toolName: "write",
                status: "completed",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        ItemId: "item-tool-error-001",
                        CallId: "call-tool-error-001",
                        ToolName: "write",
                        ServerName: null,
                        InputText: null,
                        OutputText: "写入 1 个文件",
                        Status: "completed",
                        Phase: "completed",
                        RequiresApproval: false)))));
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallCompleted,
                callId: "call-tool-error-001",
                toolName: "write",
                status: "completed",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        ItemId: "item-tool-error-001",
                        CallId: "call-tool-error-001",
                        ToolName: "write",
                        ServerName: null,
                        InputText: null,
                        OutputText: "写入 1 个文件",
                        Status: "completed",
                        Phase: "completed",
                        RequiresApproval: false)))));
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.PlanUpdated,
                message: "turn/plan/updated",
                payloadKind: ControlPlaneConversationStreamPayloadKind.Plan,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new PlanEventPayload(
                        "创建 WPF HTTP 测试工具",
                        [
                            new PlanStepEventPayload(1, "确认 .NET SDK 和 WPF 模板", "completed"),
                            new PlanStepEventPayload(2, "创建项目并实现 GET/POST 示例", "in_progress"),
                        ])))));
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.Error,
                message: providerError));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.TurnCompleted, runtime.ActiveThreadId, "turn-chat-tool-error-001", status: "failed"));
            return Task.FromResult(AgentSendResult.Fail(providerError, turnId: "turn-chat-tool-error-001", turnStatus: "failed"));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("● 修改文件", stdout, StringComparison.Ordinal);
        Assert.Contains("  C:\\Users\\Example\\Desktop\\TestTianShu\\MainWindow.xaml", stdout, StringComparison.Ordinal);
        Assert.Contains("  ✓ 写入 1 个文件", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("● 计划更新  1/2 完成  创建 WPF HTTP 测试工具", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("- 文件写入完成", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("call-tool-error-001", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("内部 hook", stdout, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(stdout, "● 修改文件"));
        Assert.Equal(0, CountOccurrences(stdout, "- 文件写入完成："));
        Assert.Contains("模型请求失败：HTTP 400 BadRequest，assistant tool_calls 缺少对应 tool 消息", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("send 执行失败", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("{\"error\"", stderr, StringComparison.Ordinal);
        Assert.Single(runtime.SendCalls);
    }

    [Fact]
    public async Task InteractiveChatRunner_HumanOutput_ClearsWaitingPlaceholderBeforeToolSummary()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-waiting-tool.txt", string.Join(Environment.NewLine, new[]
        {
            "执行命令",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-waiting-tool-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallStarted,
                callId: "call-waiting-tool-001",
                toolName: "shell",
                status: "in_progress",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        ItemId: "item-waiting-tool-001",
                        CallId: "call-waiting-tool-001",
                        ToolName: "shell",
                        ServerName: null,
                        InputText: "{\"command\":\"Get-Location\",\"output\":true}",
                        OutputText: null,
                        Status: "in_progress",
                        Phase: "started",
                        RequiresApproval: false)))));
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallCompleted,
                callId: "call-waiting-tool-001",
                toolName: "shell",
                status: "completed",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        ItemId: "item-waiting-tool-001",
                        CallId: "call-waiting-tool-001",
                        ToolName: "shell",
                        ServerName: null,
                        InputText: null,
                        OutputText: "{\"output\":\"C:\\\\Users\\\\Example\",\"metadata\":{\"exit_code\":0,\"duration_seconds\":0.2}}",
                        Status: "completed",
                        Phase: "completed",
                        RequiresApproval: false)))));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextDelta, text: "完成"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextCompleted));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.TurnCompleted, runtime.ActiveThreadId, "turn-chat-waiting-tool-001", status: "completed"));
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-waiting-tool-001", turnStatus: "completed"));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Working", stdout, StringComparison.Ordinal);
        Assert.Contains("● 执行命令", stdout, StringComparison.Ordinal);
        Assert.Contains("  Get-Location", stdout, StringComparison.Ordinal);
        Assert.Contains("  ✓ C:\\Users\\Example", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("output=True", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("{\"output\"", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("- 命令执行完成", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_HumanOutput_ItemToolLifecycle_CommitsVisibleToolBlock()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-item-tool.txt", string.Join(Environment.NewLine, new[]
        {
            "执行命令",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-item-tool-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ItemStarted,
                status: "inProgress",
                payloadKind: ControlPlaneConversationStreamPayloadKind.Item,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(new
                {
                    itemId = "item-dynamic-tool-001",
                    itemType = "dynamicToolCall",
                    toolName = "shell",
                    callId = "call-dynamic-tool-001",
                    arguments = "{\"command\":\"Get-Location\"}",
                    status = "inProgress",
                }))));
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ItemCompleted,
                status: "completed",
                payloadKind: ControlPlaneConversationStreamPayloadKind.Item,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(new
                {
                    itemId = "item-dynamic-tool-001",
                    itemType = "dynamicToolCall",
                    toolName = "shell",
                    callId = "call-dynamic-tool-001",
                    outputText = "{\"output\":\"C:\\\\Users\\\\Example\",\"metadata\":{\"exit_code\":0,\"duration_seconds\":0.2}}",
                    status = "completed",
                }))));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.TurnCompleted, runtime.ActiveThreadId, "turn-chat-item-tool-001", status: "completed"));
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-item-tool-001", turnStatus: "completed"));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Contains("● 执行命令", stdout, StringComparison.Ordinal);
        Assert.Contains("  Get-Location", stdout, StringComparison.Ordinal);
        Assert.Contains("  ✓ C:\\Users\\Example", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("call-dynamic-tool-001", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_HumanOutput_MergesLatestStartedInputWhenCompletedCallIdDiffers()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-tool-missing-start-callid.txt", string.Join(Environment.NewLine, new[]
        {
            "执行命令",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-tool-missing-start-callid-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallStarted,
                toolName: "shell",
                status: "in_progress",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        ItemId: "item-tool-missing-start-callid-001",
                        CallId: null,
                        ToolName: "shell",
                        ServerName: null,
                        InputText: "{\"command\":\"dotnet --version\"}",
                        OutputText: null,
                        Status: "in_progress",
                        Phase: "started",
                        RequiresApproval: false)))));
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallCompleted,
                callId: "call-tool-completed-only-001",
                toolName: "shell",
                status: "completed",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        ItemId: "item-tool-missing-start-callid-001",
                        CallId: "call-tool-completed-only-001",
                        ToolName: "shell",
                        ServerName: null,
                        InputText: null,
                        OutputText: "{\"output\":\"10.0.202\",\"metadata\":{\"exit_code\":0,\"duration_seconds\":0.2}}",
                        Status: "completed",
                        Phase: "completed",
                        RequiresApproval: false)))));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.TurnCompleted, runtime.ActiveThreadId, "turn-chat-tool-missing-start-callid-001", status: "completed"));
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-tool-missing-start-callid-001", turnStatus: "completed"));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Contains("● 执行命令", stdout, StringComparison.Ordinal);
        Assert.Contains("  dotnet --version", stdout, StringComparison.Ordinal);
        Assert.Contains("  ✓ 10.0.202", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("{\"output\"", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_VerboseEvents_IncludeToolMetadata()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-verbose.txt", string.Join(Environment.NewLine, new[]
        {
            "输出详细事件",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-verbose-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallStarted,
                callId: "call-verbose-001",
                toolName: "shell",
                message: "item/tool/call",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        ItemId: "item-verbose-001",
                        CallId: "call-verbose-001",
                        ToolName: "shell",
                        ServerName: null,
                        InputText: "Get-Location",
                        OutputText: null,
                        Status: "in_progress",
                        Phase: "started",
                        RequiresApproval: false)))));
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.PlanUpdated,
                message: "turn/plan/updated",
                payloadKind: ControlPlaneConversationStreamPayloadKind.Plan,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new PlanEventPayload(
                        "先检查当前目录",
                        [new PlanStepEventPayload(1, "读取工作目录", "in_progress")])))));
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.OperationReported,
                payloadKind: ControlPlaneConversationStreamPayloadKind.Operation,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(new OperationEventPayload("exec", null))),
                operationName: "exec"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextDelta, text: "ok"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextCompleted));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.TurnCompleted, runtime.ActiveThreadId, "turn-chat-verbose-001", status: "completed"));
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-verbose-001", turnStatus: "completed"));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--verbose-events", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Contains("ToolCallStarted: tool=shell, callId=call-verbose-001, input=Get-Location", stdout, StringComparison.Ordinal);
        Assert.Contains("PlanUpdated: 先检查当前目录 | 1.读取工作目录[in_progress]", stdout, StringComparison.Ordinal);
        Assert.Contains("OperationReported: exec", stdout, StringComparison.Ordinal);
        Assert.Contains("回合完成：thread=thread-chat-verbose-001, turn=turn-chat-verbose-001, status=completed", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_VerboseEvents_IncludeToolSearchLifecycle()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-tool-search.txt", string.Join(Environment.NewLine, new[]
        {
            "输出 tool_search 事件",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-tool-search-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallStarted,
                callId: "tool-search-call-001",
                toolName: "tool_search",
                status: "inProgress",
                message: "item/tool/call",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        ItemId: "item-tool-search-001",
                        CallId: "tool-search-call-001",
                        ToolName: "tool_search",
                        ServerName: null,
                        InputText: "query=tool_search",
                        OutputText: null,
                        Status: "inProgress",
                        Phase: "started",
                        RequiresApproval: false)))));
            runtime.Emit(CreateStreamEvent(
                ControlPlaneConversationStreamEventKind.ToolCallCompleted,
                callId: "tool-search-call-001",
                toolName: "tool_search",
                status: "completed",
                message: "item/tool/call",
                payloadKind: ControlPlaneConversationStreamPayloadKind.ToolCall,
                payload: StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(
                    new ToolCallEventPayload(
                        ItemId: "item-tool-search-001",
                        CallId: "tool-search-call-001",
                        ToolName: "tool_search",
                        ServerName: null,
                        InputText: "query=tool_search",
                        OutputText: "命中 3 个工具",
                        Status: "completed",
                        Phase: "completed",
                        RequiresApproval: false)))));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextDelta, text: "tool_search 验证完成"));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.AssistantTextCompleted));
            runtime.Emit(CreateStreamEvent(ControlPlaneConversationStreamEventKind.TurnCompleted, runtime.ActiveThreadId, "turn-chat-tool-search-001", status: "completed"));
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-tool-search-001", turnStatus: "completed"));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--verbose-events", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Contains("ToolCallStarted: tool=tool_search, callId=tool-search-call-001, input=query=tool_search", stdout, StringComparison.Ordinal);
        Assert.Contains("ToolCallCompleted: tool=tool_search, callId=tool-search-call-001, status=completed, output=命中 3 个工具", stdout, StringComparison.Ordinal);
        Assert.Contains("回合完成：thread=thread-chat-tool-search-001, turn=turn-chat-tool-search-001, status=completed", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_WithApproveAll_CanCompleteToolSuggestElicitation()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-tool-suggest.txt", string.Join(Environment.NewLine, new[]
        {
            "建议安装日历连接器",
            "/wait-complete 2",
        }));

        var approvalTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-tool-suggest-001" };
        runtime.RespondToApprovalAsyncHandler = (_, _) =>
        {
            approvalTcs.TrySetResult(true);
            return Task.FromResult(true);
        };
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.ApprovalRequested,
                ToolName = "tool_suggest",
                CallId = "tool-suggest-approval-001",
                Message = "mcpServer/elicitation/request",
                Text = "Google Calendar could help with this request.",
            });
            await approvalTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextDelta, Text = "tool_suggest 验证完成" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextCompleted });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-chat-tool-suggest-001", Status = "completed" });
            return AgentSendResult.Ok(message, turnId: "turn-chat-tool-suggest-001", turnStatus: "completed");
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--approve-all", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Contains("tool_suggest 验证完成", stdout, StringComparison.Ordinal);
        Assert.Single(runtime.ApprovalResponses);
        Assert.Equal("tool-suggest-approval-001", runtime.ApprovalResponses[0].CallId.Value);
        Assert.Equal(ControlPlaneApprovalDecision.Approve, runtime.ApprovalResponses[0].Decision);
    }

    [Fact]
    public async Task InteractiveChatRunner_JsonlProtocol_EmitsStructuredRecordsWithoutPrompt()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-jsonl.txt", string.Join(Environment.NewLine, new[]
        {
            "JSONL 输出",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-jsonl-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextDelta, Text = "ok-jsonl" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextCompleted });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-chat-jsonl-001", Status = "completed" });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-jsonl-001", turnStatus: "completed"));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--protocol", "jsonl", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.DoesNotContain("> ", stdout, StringComparison.Ordinal);

        var records = stdout
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();

        Assert.Contains(records, static document =>
            document.RootElement.GetProperty("type").GetString() == "stdout"
            && document.RootElement.GetProperty("partial").GetBoolean()
            && document.RootElement.GetProperty("text").GetString() == "ok-jsonl");
        Assert.DoesNotContain(records, static document =>
            document.RootElement.GetProperty("type").GetString() == "stdout"
            && document.RootElement.GetProperty("text").GetString()!.Contains("回合完成", StringComparison.Ordinal));

        foreach (var record in records)
        {
            record.Dispose();
        }
    }

    [Fact]
    public async Task InteractiveChatRunner_JsonlProtocol_WhenRetryableErrorEventObservedAndTurnCompletes_ReturnsZero()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-jsonl-retry.txt", string.Join(Environment.NewLine, new[]
        {
            "JSONL 可重试错误",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-jsonl-retry-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.Error, Message = "stream retry", WillRetry = true });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextDelta, Text = "retry recovered" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextCompleted, Text = "retry recovered" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-chat-jsonl-retry-001", Status = "completed" });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-jsonl-retry-001", turnStatus: "completed"));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--protocol", "jsonl", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));

        var records = stdout
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();

        Assert.Contains(records, static document =>
            document.RootElement.GetProperty("type").GetString() == "stderr"
            && document.RootElement.GetProperty("text").GetString() == "stream retry");
        Assert.Contains(records, static document =>
            document.RootElement.GetProperty("type").GetString() == "stdout"
            && document.RootElement.GetProperty("partial").GetBoolean()
            && document.RootElement.GetProperty("text").GetString() == "retry recovered");
        Assert.DoesNotContain(records, static document =>
            document.RootElement.GetProperty("type").GetString() == "stdout"
            && document.RootElement.GetProperty("text").GetString()!.Contains("回合完成", StringComparison.Ordinal));

        foreach (var record in records)
        {
            record.Dispose();
        }
    }

    [Fact]
    public async Task ConversationTurnCommandRunner_HumanOutput_WritesAssistantText_AndErrorsToStderr()
    {
        using var workspace = new CliConsumerWorkspace();
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-followup-human-001" };
        runtime.SendFollowUpAsyncHandler = (message, mode, _, correlationId) =>
        {
            Assert.False(string.IsNullOrWhiteSpace(correlationId));
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextDelta, Text = "human follow-up" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextCompleted });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.Error, Message = "follow-up error" });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-followup-human-001", turnStatus: "completed", correlationId: correlationId, requestedMode: mode, effectiveMode: mode));
        };

        var command = ParseCliCommand(WithCommonOptions(workspace, "follow-up", "--mode", "steer", "--message", "请收口"));
        var runner = CreateRunner("TianShu.Cli.ConversationTurnCommandRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunFollowUpAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("human follow-up", stdout, StringComparison.Ordinal);
        Assert.Contains("状态：completed", stdout, StringComparison.Ordinal);
        Assert.Contains("follow-up error", stderr, StringComparison.Ordinal);
        Assert.Collection(runtime.FollowUpCalls, call =>
        {
            Assert.Equal(("请收口", FollowUpMode.Steer), (call.UserMessage, call.Mode));
            Assert.False(string.IsNullOrWhiteSpace(call.CorrelationId));
        });
    }

    [Fact]
    public async Task ConversationTurnCommandRunner_JsonOutput_WhenErrorEventObserved_ReturnsNonZero_AndIncludesErrors()
    {
        using var workspace = new CliConsumerWorkspace();
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-followup-json-err-001" };
        runtime.SendFollowUpAsyncHandler = (message, mode, _, correlationId) =>
        {
            Assert.False(string.IsNullOrWhiteSpace(correlationId));
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextDelta, Text = "json follow-up" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextCompleted });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.Error, Message = "json follow-up error" });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-followup-json-err-001", turnStatus: "completed", correlationId: correlationId, requestedMode: mode, effectiveMode: mode));
        };

        var command = ParseCliCommand(WithCommonOptions(workspace, "follow-up", "--mode", "queue", "--message", "继续", "--json"));
        var runner = CreateRunner("TianShu.Cli.ConversationTurnCommandRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunFollowUpAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal("thread-followup-json-err-001", document.RootElement.GetProperty("threadId").GetString());
        Assert.Equal("completed", document.RootElement.GetProperty("turnStatus").GetString());
        Assert.Equal(runtime.FollowUpCalls[0].CorrelationId, document.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("json follow-up", document.RootElement.GetProperty("assistantText").GetString());
        Assert.Contains(document.RootElement.GetProperty("errors").EnumerateArray().Select(static x => x.GetString()), static x => x == "json follow-up error");
    }

    [Fact]
    public async Task ConversationTurnCommandRunner_WhenFollowUpThrowsAppServerRpcException_JsonMode_IncludesAppServerError()
    {
        using var workspace = new CliConsumerWorkspace();
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-followup-rpc-err-001" };
        runtime.SendFollowUpAsyncHandler = static (_, _, _, _) => throw new AppServerRpcException(
            -32600,
            "failed to load configuration: boom",
            StructuredJson("""{"reason":"cloudRequirements","detail":"boom"}"""));

        var command = ParseCliCommand(WithCommonOptions(workspace, "follow-up", "--mode", "queue", "--message", "继续", "--json"));
        var runner = CreateRunner("TianShu.Cli.ConversationTurnCommandRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunFollowUpAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));

        using var document = JsonDocument.Parse(stdout);
        Assert.Equal("thread-followup-rpc-err-001", document.RootElement.GetProperty("threadId").GetString());
        Assert.Contains("failed to load configuration: boom", document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        var appServerError = document.RootElement.GetProperty("appServerError").GetProperty("error");
        Assert.Equal(-32600, appServerError.GetProperty("code").GetInt32());
        Assert.Equal("failed to load configuration: boom", appServerError.GetProperty("message").GetString());
        Assert.Equal("cloudRequirements", appServerError.GetProperty("data").GetProperty("reason").GetString());
        Assert.Contains(
            document.RootElement.GetProperty("errors").EnumerateArray().Select(static x => x.GetString()),
            static x => x is not null && x.Contains("cloudRequirements", StringComparison.Ordinal));
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；send 错误摘要由 Kernel→Runtime loop 用例覆盖。")]
    public async Task SendCommandRunner_WhenSendFailsWithoutErrorEvent_ReturnsSendFailed()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-send-failed");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-send-failed-001" };
        runtime.SendAsyncHandler = static (message, _, _) => Task.FromResult(AgentSendResult.Fail("send failed"));

        var command = ParseCliCommand("send", "--apphost-control-plane", "--message", "触发发送失败", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot, "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(6, summary.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("SendFailed", summary.RootElement.GetProperty("exitCodeName").GetString());
        Assert.Equal("send failed", summary.RootElement.GetProperty("failureMessage").GetString());
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；send 错误摘要由 Kernel→Runtime loop 用例覆盖。")]
    public async Task SendCommandRunner_WhenSendThrowsAppServerRpcException_PreservesAppServerErrorInSummary()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-send-rpc-error");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-send-rpc-error-001" };
        runtime.SendAsyncHandler = static (_, _, _) => throw new AppServerRpcException(
            -32600,
            "failed to load configuration: boom",
            StructuredJson("""{"reason":"cloudRequirements","detail":"boom"}"""));

        var command = ParseCliCommand("send", "--apphost-control-plane", "--message", "触发结构化错误", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot, "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(6, summary.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("SendFailed", summary.RootElement.GetProperty("exitCodeName").GetString());
        Assert.Contains("failed to load configuration: boom", summary.RootElement.GetProperty("failureMessage").GetString(), StringComparison.Ordinal);
        var appServerError = summary.RootElement.GetProperty("appServerError").GetProperty("error");
        Assert.Equal(-32600, appServerError.GetProperty("code").GetInt32());
        Assert.Equal("failed to load configuration: boom", appServerError.GetProperty("message").GetString());
        Assert.Equal("cloudRequirements", appServerError.GetProperty("data").GetProperty("reason").GetString());
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；send 错误摘要由 Kernel→Runtime loop 用例覆盖。")]
    public async Task SendCommandRunner_WhenErrorEventObserved_ReturnsTurnFailed()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-turn-failed");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-turn-failed-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.Error, Message = "turn failed" });
            return Task.FromResult(AgentSendResult.Fail("send failed"));
        };

        var command = ParseCliCommand("send", "--apphost-control-plane", "--message", "触发回合失败", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot, "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(7, summary.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("TurnFailed", summary.RootElement.GetProperty("exitCodeName").GetString());
        Assert.Equal("turn failed", summary.RootElement.GetProperty("failureMessage").GetString());
        Assert.True(summary.RootElement.GetProperty("errorEventObserved").GetBoolean());
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；send 终态判定由 Kernel→Runtime loop 用例覆盖。")]
    public async Task SendCommandRunner_WhenTurnRemainsInProgress_ReturnsTurnFailedInsteadOfSuccess()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-turn-in-progress");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-turn-in-progress-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextDelta, Text = "已经开始处理" });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-turn-in-progress-001", turnStatus: "inProgress"));
        };

        var command = ParseCliCommand(
            "send",
            "--apphost-control-plane",
            "--message",
            "触发未完成回合",
            "--cwd",
            workspace.RootPath,
            "--config",
            workspace.ConfigPath,
            "--profile",
            "work",
            "--artifacts",
            artifactsRoot,
            "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(7, summary.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("TurnFailed", summary.RootElement.GetProperty("exitCodeName").GetString());
        Assert.Equal("inProgress", summary.RootElement.GetProperty("turnStatus").GetString());
        Assert.Equal("回合未在 CLI 等待窗口内完成，当前状态：inProgress。", summary.RootElement.GetProperty("failureMessage").GetString());
        Assert.Equal("已经开始处理", summary.RootElement.GetProperty("resultText").GetString());
    }

    [Theory(Skip = "旧 AppHost send 兼容路径已移除；send 终态判定由 Kernel→Runtime loop 用例覆盖。")]
    [InlineData("interrupted")]
    [InlineData("failed")]
    public async Task SendCommandRunner_WhenRuntimeReturnsTerminalFailureStatus_ReturnsTurnFailed(string turnStatus)
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, $"artifacts-terminal-{turnStatus}");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = $"thread-terminal-{turnStatus}-001" };
        runtime.SendAsyncHandler = (_, _, _) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.TurnCompleted,
                ThreadId = runtime.ActiveThreadId,
                TurnId = $"turn-terminal-{turnStatus}-001",
                Status = turnStatus,
            });
            return Task.FromResult(AgentSendResult.Fail($"status={turnStatus}", turnId: $"turn-terminal-{turnStatus}-001", turnStatus: turnStatus));
        };

        var command = ParseCliCommand(
            "send",
            "--apphost-control-plane",
            "--message",
            $"验证 {turnStatus}",
            "--cwd",
            workspace.RootPath,
            "--config",
            workspace.ConfigPath,
            "--profile",
            "work",
            "--artifacts",
            artifactsRoot,
            "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(7, summary.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("TurnFailed", summary.RootElement.GetProperty("exitCodeName").GetString());
        Assert.Equal(turnStatus, summary.RootElement.GetProperty("turnStatus").GetString());
        Assert.Equal($"回合未成功完成，当前状态：{turnStatus}。", summary.RootElement.GetProperty("failureMessage").GetString());

        var runDirectory = Assert.IsType<string>(summary.RootElement.GetProperty("artifactsDirectory").GetString());
        Assert.True(File.Exists(Path.Combine(runDirectory, "summary.json")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "resolved-options.json")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "events.jsonl")));

        using var persistedSummary = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "summary.json")));
        Assert.False(persistedSummary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(turnStatus, persistedSummary.RootElement.GetProperty("turnStatus").GetString());
        Assert.Equal("TurnFailed", persistedSummary.RootElement.GetProperty("exitCodeName").GetString());

        using var resolvedOptions = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "resolved-options.json")));
        Assert.Equal(workspace.ConfigPath, resolvedOptions.RootElement.GetProperty("configFilePath").GetString());
        Assert.Equal("work", resolvedOptions.RootElement.GetProperty("profileName").GetString());

        var eventsJsonl = File.ReadAllText(Path.Combine(runDirectory, "events.jsonl"));
        Assert.Contains($"\"Status\":\"{turnStatus}\"", eventsJsonl, StringComparison.Ordinal);
        Assert.Contains("TurnCompleted", eventsJsonl, StringComparison.Ordinal);
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；审批阻塞由 Kernel→Runtime loop 治理用例覆盖。")]
    public async Task SendCommandRunner_WhenApprovalRequiredWithoutApproveAll_ReturnsApprovalOrInputRequired()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-approval-required");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-approval-required-001" };
        runtime.SendAsyncHandler = async (_, _, cancellationToken) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.ApprovalRequested, CallId = "approval-required-001", ToolName = "browser" });
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return AgentSendResult.Ok("blocked", turnId: "turn-approval-required-001", turnStatus: "completed");
        };

        var command = ParseCliCommand("send", "--apphost-control-plane", "--message", "等待审批", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot, "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(8, summary.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("ApprovalOrInputRequired", summary.RootElement.GetProperty("exitCodeName").GetString());
        Assert.True(summary.RootElement.GetProperty("approvalRequested").GetBoolean());
        Assert.True(summary.RootElement.GetProperty("approvalBlocked").GetBoolean());
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；artifact 摘要由 Kernel→Runtime loop 用例覆盖。")]
    public async Task SendCommandRunner_WritesArtifactsFiles_WithExpectedPayloads()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-output");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-artifacts-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextDelta, Text = "artifact result" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextCompleted, Text = "artifact result" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-artifacts-001", Status = "completed" });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-artifacts-001", turnStatus: "completed"));
        };

        var command = ParseCliCommand("send", "--apphost-control-plane", "--message", "写入产物", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot, "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        var runDirectory = Assert.IsType<string>(summary.RootElement.GetProperty("artifactsDirectory").GetString());
        Assert.True(Directory.Exists(runDirectory));
        Assert.True(File.Exists(Path.Combine(runDirectory, "summary.json")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "resolved-options.json")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "events.jsonl")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "request.txt")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "result.txt")));
        Assert.Equal("写入产物", File.ReadAllText(Path.Combine(runDirectory, "request.txt")));
        Assert.Equal("artifact result", File.ReadAllText(Path.Combine(runDirectory, "result.txt")));
        Assert.Contains("AssistantTextDelta", File.ReadAllText(Path.Combine(runDirectory, "events.jsonl")), StringComparison.Ordinal);
        Assert.Contains("Success", File.ReadAllText(Path.Combine(runDirectory, "summary.json")), StringComparison.Ordinal);
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；agent job 事件不属于当前默认 send 主线验收。")]
    public async Task SendCommandRunner_WhenAgentJobProgressEventsArrive_PreservesVerboseOutputAndArtifacts()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-agent-job-progress");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-agent-job-progress-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.AgentJobProgress,
                ThreadId = runtime.ActiveThreadId,
                TurnId = "turn-agent-job-progress-001",
                Text = "agent_job_progress:{\"job_id\":\"job-progress-001\"}",
                AgentJobProgress = new AgentJobProgressPayload(
                    "job-progress-001",
                    4,
                    1,
                    1,
                    2,
                    0,
                    9),
            });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextCompleted, Text = "agent job done" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-agent-job-progress-001", Status = "completed" });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-agent-job-progress-001", turnStatus: "completed"));
        };

        var command = ParseCliCommand("send", "--apphost-control-plane", "--message", "触发 agent job", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot, "--verbose-events");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (_, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        Assert.Contains("AgentJobProgress: jobId=job-progress-001, completed=2/4, pending=1, running=1, failed=0, etaSeconds=9", stdout, StringComparison.Ordinal);

        var runDirectory = Assert.Single(Directory.GetDirectories(artifactsRoot));
        var eventsJsonl = File.ReadAllText(Path.Combine(runDirectory, "events.jsonl"));
        Assert.Contains("AgentJobProgress", eventsJsonl, StringComparison.Ordinal);
        Assert.Contains("job-progress-001", eventsJsonl, StringComparison.Ordinal);
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；审批 skill metadata 不属于当前默认 send 主线验收。")]
    public async Task SendCommandRunner_ArtifactsEvents_PreserveApprovalSkillMetadata()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-skill-metadata");
        Directory.CreateDirectory(artifactsRoot);

        const string metadataJson = """{"pathToSkillsMd":"D:/repo/.tianshu/skills/test/SKILL.md"}""";
        const string rawJson = """{"threadId":"thread-artifacts-skill-001","turnId":"turn-artifacts-skill-001","itemId":"approval-skill-001","command":"D:/repo/.tianshu/skills/test/scripts/hello.cmd","skillMetadata":{"pathToSkillsMd":"D:/repo/.tianshu/skills/test/SKILL.md"}}""";
        using var approvalMetadataDocument = JsonDocument.Parse("""{"pathToSkillsMd":"D:/repo/.tianshu/skills/test/SKILL.md"}""");
        var approvalMetadata = approvalMetadataDocument.RootElement.Clone();

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-artifacts-skill-001" };
        runtime.RespondToApprovalAsyncHandler = (_, _) => Task.FromResult(true);
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.ApprovalRequested,
                ThreadId = "thread-artifacts-skill-001",
                TurnId = "turn-artifacts-skill-001",
                CallId = "approval-skill-001",
                ToolName = "commandExecution",
                Message = "item/commandExecution/requestApproval",
                Text = "D:/repo/.tianshu/skills/test/scripts/hello.cmd | manual review required",
                ApprovalRequest = new ApprovalRequestPayload(
                    "commandExecution",
                    "tool_approval",
                    ["accept", "decline"],
                    "manual review required",
                    [new ApprovalMetadataFieldPayload("pathToSkillsMd", "string", approvalMetadata.GetProperty("pathToSkillsMd").GetString() ?? string.Empty)]),
                MetadataJson = metadataJson,
                RawJson = rawJson,
            });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextDelta, Text = "artifact result" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextCompleted, Text = "artifact result" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-artifacts-skill-001", Status = "completed" });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-artifacts-skill-001", turnStatus: "completed"));
        };

        var command = ParseCliCommand("send", "--apphost-control-plane", "--message", "写入审批元数据", "--approve-all", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot, "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        var runDirectory = Assert.IsType<string>(summary.RootElement.GetProperty("artifactsDirectory").GetString());
        var eventsJsonl = File.ReadAllText(Path.Combine(runDirectory, "events.jsonl"));

        Assert.Contains("ApprovalRequested", eventsJsonl, StringComparison.Ordinal);
        Assert.Contains("\"ApprovalRequest\":", eventsJsonl, StringComparison.Ordinal);
        Assert.Contains("\"AvailableDecisions\":[\"accept\",\"decline\"]", eventsJsonl, StringComparison.Ordinal);
        Assert.Contains("pathToSkillsMd", eventsJsonl, StringComparison.Ordinal);
        Assert.Contains("/skills/test/SKILL.md", eventsJsonl, StringComparison.Ordinal);
        Assert.Contains("\"Diagnostics\":", eventsJsonl, StringComparison.Ordinal);
        Assert.Contains("\"MetadataJson\":", eventsJsonl, StringComparison.Ordinal);
        Assert.Contains("\"RawJson\":", eventsJsonl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WhenWaitCompleteTimesOut_ReturnsNonZero()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-timeout.txt", string.Join(Environment.NewLine, new[]
        {
            "长任务",
            "/wait-complete 1",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-timeout-001" };
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-chat-timeout-001", Status = "completed" });
            return AgentSendResult.Ok(message, turnId: "turn-chat-timeout-001", turnStatus: "completed");
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, _, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("等待回合结束超时。", stderr, StringComparison.Ordinal);
        Assert.Single(runtime.SendCalls);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WhenScriptExplicitlyExits_SkipsFinalDrainWait()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "chat-artifacts-explicit-exit");
        Directory.CreateDirectory(artifactsRoot);
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-explicit-exit.txt", string.Join(Environment.NewLine, new[]
        {
            "第一轮",
            "/follow-up interrupt 第二轮",
            "/wait-complete 1",
            "/exit",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-explicit-exit-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            runtime.HasActiveTurn = true;
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-explicit-exit-001", turnStatus: "inProgress"));
        };
        runtime.SendFollowUpAsyncHandler = (message, mode, _, correlationId) =>
        {
            Assert.Equal(FollowUpMode.Interrupt, mode);
            runtime.HasActiveTurn = true;
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-explicit-exit-002", turnStatus: "inProgress", correlationId: correlationId, requestedMode: mode, effectiveMode: FollowUpMode.Queue));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot);
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var stopwatch = Stopwatch.StartNew();
        var (exitCode, _, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, cts.Token);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });
        stopwatch.Stop();

        Assert.Equal(1, exitCode);
        Assert.Contains("等待回合结束超时。", stderr, StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"显式 /exit 后不应继续等待 5 分钟，实际耗时：{stopwatch.Elapsed}。");
        Assert.Single(runtime.SendCalls);
        Assert.Single(runtime.FollowUpCalls);

        var runDirectory = Assert.Single(Directory.GetDirectories(artifactsRoot));
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "summary.json")));
        Assert.Equal(1, summary.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("等待回合结束超时。", summary.RootElement.GetProperty("failureMessage").GetString());
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WhenInterruptFollowUpSupersedesCurrentTurn_DoesNotCountUserInterruptAsFailure()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "chat-artifacts-interrupt-superseded");
        Directory.CreateDirectory(artifactsRoot);
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-interrupt-superseded.txt", string.Join(Environment.NewLine, new[]
        {
            "第一轮",
            "/wait-event ToolCallStarted 2",
            "/follow-up interrupt 第二轮",
            "/wait-complete 2",
        }));

        var interruptRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-interrupt-superseded-001" };
        runtime.SendAsyncHandler = async (_, _, cancellationToken) =>
        {
            runtime.HasActiveTurn = true;
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.ToolCallStarted,
                ThreadId = runtime.ActiveThreadId,
                TurnId = "turn-chat-interrupt-superseded-001",
                CallId = "tool-shell-interrupt-superseded-001",
                ToolName = "shell",
                Status = "in_progress",
            });

            await interruptRequested.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.TurnCompleted,
                ThreadId = runtime.ActiveThreadId,
                TurnId = "turn-chat-interrupt-superseded-001",
                Status = "interrupted",
            });
            runtime.HasActiveTurn = false;
            return AgentSendResult.Fail("回合已中断。", turnId: "turn-chat-interrupt-superseded-001", turnStatus: "interrupted");
        };
        runtime.SendFollowUpAsyncHandler = async (message, mode, _, correlationId) =>
        {
            Assert.Equal(FollowUpMode.Interrupt, mode);
            interruptRequested.TrySetResult(true);
            await Task.Delay(50).ConfigureAwait(false);
            runtime.HasActiveTurn = true;
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextDelta, Text = "已中断" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextCompleted, Text = "已中断" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.TurnCompleted,
                ThreadId = runtime.ActiveThreadId,
                TurnId = "turn-chat-interrupt-superseded-002",
                Status = "completed",
            });
            runtime.HasActiveTurn = false;
            return AgentSendResult.Ok(message, turnId: "turn-chat-interrupt-superseded-002", turnStatus: "completed", correlationId: correlationId, requestedMode: mode, effectiveMode: FollowUpMode.Queue);
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot);
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("回合完成", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("send 执行失败：回合已中断。", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("执行失败", stderr, StringComparison.Ordinal);

        var runDirectory = Assert.Single(Directory.GetDirectories(artifactsRoot));
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "summary.json")));
        Assert.True(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, summary.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(0, summary.RootElement.GetProperty("failureCount").GetInt32());
        Assert.Equal("completed", summary.RootElement.GetProperty("turnStatus").GetString());
        Assert.True(summary.RootElement.GetProperty("failureMessage").ValueKind is JsonValueKind.Null);

        var eventsJsonl = File.ReadAllText(Path.Combine(runDirectory, "events.jsonl"));
        Assert.Contains("CliConversationInterrupted", eventsJsonl, StringComparison.Ordinal);
        Assert.DoesNotContain("CliConversationFailed", eventsJsonl, StringComparison.Ordinal);
        var transcriptRecordsJsonl = File.ReadAllText(Path.Combine(runDirectory, "transcript-records.jsonl"));
        Assert.Contains("\"kind\":\"lifecycle_debug\"", transcriptRecordsJsonl, StringComparison.Ordinal);
        Assert.Contains("status=interrupted", transcriptRecordsJsonl, StringComparison.Ordinal);
        Assert.Contains("status=completed", transcriptRecordsJsonl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WhenSendReturnsInProgress_WaitsForTurnCompletedEvent()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-inprogress-success.txt", string.Join(Environment.NewLine, new[]
        {
            "长任务",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-inprogress-success-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            runtime.HasActiveTurn = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(200).ConfigureAwait(false);
                runtime.HasActiveTurn = false;
                EmitProjectedStreamEvent(runtime, new AgentStreamEvent
                {
                    Kind = AgentStreamEventKind.TurnCompleted,
                    ThreadId = runtime.ActiveThreadId,
                    TurnId = "turn-chat-inprogress-success-001",
                    Status = "completed",
                });
            });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-inprogress-success-001", turnStatus: "inProgress"));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Contains("当前没有运行中的回合。", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("回合完成", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WhenWaitCompleteSeesOngoingActivity_StillTimesOutByWallClock()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-activity-success.txt", string.Join(Environment.NewLine, new[]
        {
            "长任务",
            "/wait-complete 1",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-activity-success-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            runtime.HasActiveTurn = true;
            _ = Task.Run(async () =>
            {
                for (var index = 0; index < 4; index++)
                {
                    await Task.Delay(350).ConfigureAwait(false);
                    EmitProjectedStreamEvent(runtime, new AgentStreamEvent
                    {
                        Kind = AgentStreamEventKind.AssistantTextDelta,
                        ThreadId = runtime.ActiveThreadId,
                        TurnId = "turn-chat-activity-success-001",
                        Status = "inProgress",
                        Text = $"chunk-{index}",
                    });
                }

                runtime.HasActiveTurn = false;
                EmitProjectedStreamEvent(runtime, new AgentStreamEvent
                {
                    Kind = AgentStreamEventKind.TurnCompleted,
                    ThreadId = runtime.ActiveThreadId,
                    TurnId = "turn-chat-activity-success-001",
                    Status = "completed",
                });
            });

            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-activity-success-001", turnStatus: "inProgress"));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("当前没有运行中的回合。", stdout, StringComparison.Ordinal);
        Assert.Contains("等待回合结束超时。", stderr, StringComparison.Ordinal);
        Assert.Single(runtime.SendCalls);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WhenAcceptedTurnStaysInProgress_ArtifactsReportFailure()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "chat-artifacts-inprogress-timeout");
        Directory.CreateDirectory(artifactsRoot);
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-inprogress-timeout.txt", string.Join(Environment.NewLine, new[]
        {
            "长任务",
            "/wait-complete 1",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-inprogress-timeout-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            runtime.HasActiveTurn = true;
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.AssistantTextDelta,
                ThreadId = runtime.ActiveThreadId,
                TurnId = "turn-chat-inprogress-timeout-001",
                Text = "incomplete assistant trace",
            });
            _ = Task.Run(async () =>
            {
                await Task.Delay(1200).ConfigureAwait(false);
                runtime.HasActiveTurn = false;
            });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-inprogress-timeout-001", turnStatus: "inProgress"));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot);
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, _, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("等待回合结束超时。", stderr, StringComparison.Ordinal);

        var runDirectory = Assert.Single(Directory.GetDirectories(artifactsRoot));
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "summary.json")));
        Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, summary.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("inProgress", summary.RootElement.GetProperty("turnStatus").GetString());
        Assert.Equal("等待回合结束超时。", summary.RootElement.GetProperty("failureMessage").GetString());

        var eventsJsonl = File.ReadAllText(Path.Combine(runDirectory, "events.jsonl"));
        Assert.Contains("CliConversationAccepted", eventsJsonl, StringComparison.Ordinal);
        Assert.DoesNotContain("CliConversationCompleted", eventsJsonl, StringComparison.Ordinal);
        var projectionRecordsJsonl = File.ReadAllText(Path.Combine(runDirectory, "projection-records.jsonl"));
        Assert.Contains("\"kind\":\"assistant_delta_received\"", projectionRecordsJsonl, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"assistant_block_incomplete\"", projectionRecordsJsonl, StringComparison.Ordinal);
        Assert.Contains("incomplete assistant trace", projectionRecordsJsonl, StringComparison.Ordinal);
        Assert.Contains("incomplete assistant trace", File.ReadAllText(Path.Combine(runDirectory, "transcript-records.jsonl")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WhenOnlyToolStatusChanges_ArtifactsKeepTurnInProgress()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "chat-artifacts-tool-status-timeout");
        Directory.CreateDirectory(artifactsRoot);
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-tool-status-timeout.txt", string.Join(Environment.NewLine, new[]
        {
            "长任务",
            "/wait-complete 1",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-tool-status-timeout-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            runtime.HasActiveTurn = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(200).ConfigureAwait(false);
                EmitProjectedStreamEvent(runtime, new AgentStreamEvent
                {
                    Kind = AgentStreamEventKind.ToolCallCompleted,
                    ThreadId = runtime.ActiveThreadId,
                    TurnId = "turn-chat-tool-status-timeout-001",
                    CallId = "tool-shell-timeout-001",
                    ToolName = "shell",
                    Status = "completed",
                    Text = "ok",
                });

                await Task.Delay(1500).ConfigureAwait(false);
                runtime.HasActiveTurn = false;
            });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-tool-status-timeout-001", turnStatus: "inProgress"));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot);
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, _, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("等待回合结束超时。", stderr, StringComparison.Ordinal);

        var runDirectory = Assert.Single(Directory.GetDirectories(artifactsRoot));
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "summary.json")));
        Assert.Equal("inProgress", summary.RootElement.GetProperty("turnStatus").GetString());
        Assert.Equal("等待回合结束超时。", summary.RootElement.GetProperty("failureMessage").GetString());

        var eventsJsonl = File.ReadAllText(Path.Combine(runDirectory, "events.jsonl"));
        Assert.Contains("ToolCallCompleted", eventsJsonl, StringComparison.Ordinal);
        Assert.DoesNotContain("\"turnStatus\":\"completed\"", eventsJsonl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WhenInterruptCompletesThenPlainSecondMessageStartsNewTurn_AndToolActivityNeverCloses_TimesOutOnSecondTurn()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "chat-artifacts-interrupt-second-turn-timeout");
        Directory.CreateDirectory(artifactsRoot);
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-interrupt-second-turn-timeout.txt", string.Join(Environment.NewLine, new[]
        {
            "第一轮",
            "/wait-event ToolCallStarted 2",
            "/interrupt",
            "/wait-complete 2",
            "第二轮",
            "/wait-event ToolCallStarted 2",
            "/wait-complete 1",
        }));

        var interruptRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTurnReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sendCount = 0;
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-interrupt-second-turn-timeout-001" };
        runtime.SendAsyncHandler = async (message, history, cancellationToken) =>
        {
            sendCount++;
            if (sendCount == 1)
            {
                runtime.HasActiveTurn = true;
                EmitProjectedStreamEvent(runtime, new AgentStreamEvent
                {
                    Kind = AgentStreamEventKind.ToolCallStarted,
                    ThreadId = runtime.ActiveThreadId,
                    TurnId = "turn-chat-interrupt-second-turn-timeout-001",
                    CallId = "tool-shell-interrupt-second-turn-timeout-001",
                    ToolName = "shell",
                    Status = "in_progress",
                });

                await interruptRequested.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                EmitProjectedStreamEvent(runtime, new AgentStreamEvent
                {
                    Kind = AgentStreamEventKind.TurnCompleted,
                    ThreadId = runtime.ActiveThreadId,
                    TurnId = "turn-chat-interrupt-second-turn-timeout-001",
                    Status = "interrupted",
                });
                runtime.HasActiveTurn = false;
                firstTurnReleased.TrySetResult(true);
                return AgentSendResult.Fail("回合已中断。", turnId: "turn-chat-interrupt-second-turn-timeout-001", turnStatus: "interrupted");
            }

            Assert.Equal(2, sendCount);
            await firstTurnReleased.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            runtime.HasActiveTurn = true;
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.ToolCallStarted,
                ThreadId = runtime.ActiveThreadId,
                TurnId = "turn-chat-interrupt-second-turn-timeout-002",
                CallId = "tool-spawn-agent-interrupt-second-turn-timeout-001",
                ToolName = "spawn_agent",
                Status = "in_progress",
            });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.ToolCallCompleted,
                ThreadId = runtime.ActiveThreadId,
                TurnId = "turn-chat-interrupt-second-turn-timeout-002",
                CallId = "tool-spawn-agent-interrupt-second-turn-timeout-001",
                ToolName = "spawn_agent",
                Status = "completed",
                Text = "{\"agent_id\":\"agent-timeout-001\"}",
            });

            _ = Task.Run(async () =>
            {
                await Task.Delay(1300).ConfigureAwait(false);
                runtime.HasActiveTurn = false;
            });

            return AgentSendResult.Ok(message, turnId: "turn-chat-interrupt-second-turn-timeout-002", turnStatus: "inProgress");
        };
        runtime.InterruptAsyncHandler = _ =>
        {
            interruptRequested.TrySetResult(true);
            return Task.CompletedTask;
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot);
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.Equal(2, runtime.SendCalls.Count);
        Assert.Equal(1, runtime.InterruptCallCount);
        Assert.DoesNotContain("回合完成", stdout, StringComparison.Ordinal);
        Assert.Contains("等待回合结束超时。", stderr, StringComparison.Ordinal);

        var runDirectory = Assert.Single(Directory.GetDirectories(artifactsRoot));
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDirectory, "summary.json")));
        Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, summary.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("inProgress", summary.RootElement.GetProperty("turnStatus").GetString());
        Assert.Equal("等待回合结束超时。", summary.RootElement.GetProperty("failureMessage").GetString());

        var eventsJsonl = File.ReadAllText(Path.Combine(runDirectory, "events.jsonl"));
        Assert.Contains("CliConversationInterrupted", eventsJsonl, StringComparison.Ordinal);
        Assert.Contains("CliConversationAccepted", eventsJsonl, StringComparison.Ordinal);
        Assert.DoesNotContain("CliConversationFailed", eventsJsonl, StringComparison.Ordinal);
        Assert.DoesNotContain("CliConversationCompleted", eventsJsonl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WhenBusyAndPlainTextInput_BecomesSteerFollowUp()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-busy.txt", string.Join(Environment.NewLine, new[]
        {
            "第一轮",
            "第二轮",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-busy-001" };
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-chat-busy-001", Status = "completed" });
            return AgentSendResult.Ok(message, turnId: "turn-chat-busy-001", turnStatus: "completed");
        };
        runtime.SendFollowUpAsyncHandler = (message, mode, _, correlationId) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-chat-busy-follow-up-001", Status = "completed" });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-busy-follow-up-001", turnStatus: "completed", correlationId: correlationId, requestedMode: mode, effectiveMode: mode));
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, _, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("当前已有运行中的回合。请等待完成，或使用 /follow-up、/interrupt。", stderr, StringComparison.Ordinal);
        Assert.Single(runtime.SendCalls);
        var followUp = Assert.Single(runtime.FollowUpCalls);
        Assert.Equal("第二轮", followUp.UserMessage);
        Assert.Equal(FollowUpMode.Steer, followUp.Mode);
        Assert.False(string.IsNullOrWhiteSpace(followUp.CorrelationId));
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WhenBusyAndBangCommand_UsesUserShell()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-bang-busy.txt", string.Join(Environment.NewLine, new[]
        {
            "第一轮",
            "!Get-Location",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-bang-001" };
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.TurnCompleted,
                ThreadId = runtime.ActiveThreadId,
                TurnId = "turn-chat-bang-001",
                Status = "completed",
            });
            return AgentSendResult.Ok(message, turnId: "turn-chat-bang-001", turnStatus: "completed");
        };
        runtime.RunUserShellCommandAsyncHandler = static (command, _) =>
            Task.FromResult(AgentSendResult.Ok(
                command,
                turnId: "turn-chat-bang-001",
                turnStatus: "inProgress"));

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, _, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Single(runtime.SendCalls);
        Assert.Collection(runtime.UserShellCommandCalls, commandText => Assert.Equal("Get-Location", commandText));
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WhenBusyAndInitCommand_IsRejected()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-init-busy.txt", string.Join(Environment.NewLine, new[]
        {
            "第一轮",
            "/init",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-init-busy-001" };
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-chat-init-busy-001", Status = "completed" });
            return AgentSendResult.Ok(message, turnId: "turn-chat-init-busy-001", turnStatus: "completed");
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, _, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("'/init' is disabled while a task is in progress.", stderr, StringComparison.Ordinal);
        Assert.Single(runtime.SendCalls);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_InitCommand_WhenAgentsGuideExists_SkipsWithoutOverwrite()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-init-skip.txt", "/init");
        var existingPath = WriteUtf8File(workspace.RootPath, "AGENTS.md", "existing-guide");

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-init-skip-001" };
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Contains("AGENTS.md already exists here. Skipping /init to avoid overwriting it.", stdout, StringComparison.Ordinal);
        Assert.Empty(runtime.SendCalls);
        Assert.Equal("existing-guide", File.ReadAllText(existingPath));
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_InitCommand_SendsCodexPromptAsUserMessage()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-init-send.txt", "/init");

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-init-send-001" };
        runtime.SendAsyncHandler = static (message, _, _) =>
            Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-chat-init-send-001", turnStatus: "completed"));

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, _, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        var sendCall = Assert.Single(runtime.SendCalls);
        Assert.Equal(CodexInitPrompt, sendCall.UserMessage);
        var initItems = AssertCliConversationEnvelope(
            Assert.Single(runtime.TurnSubmissionCommands).Envelope,
            "cli-turn-",
            "turn_submission");
        Assert.Equal(CodexInitPrompt, Assert.IsType<TextInteractionItem>(Assert.Single(initItems)).Text);
        Assert.False(File.Exists(Path.Combine(workspace.RootPath, "AGENTS.md")));
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_HelpOutput_IncludesInitCommand()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-help.txt", "/help");

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-help-001" };
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Contains("/init", stdout, StringComparison.Ordinal);
        Assert.Contains("/model-route [route-set|status [--matrix]]", stdout, StringComparison.Ordinal);
        Assert.Contains("/config reload", stdout, StringComparison.Ordinal);
        Assert.Contains("/thread delete --thread-id <id>", stdout, StringComparison.Ordinal);
        Assert.Contains("/thread clear", stdout, StringComparison.Ordinal);
        Assert.Contains("输入 !<shell command> 会执行本地 user shell", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_ThreadClearCommand_ClearsWithoutSendingMessage()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-thread-clear.txt", "thread clear --confirm DELETE ALL THREADS");

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-clear-chat-001" };
        runtime.ClearThreadsAsyncHandler = static _ => Task.FromResult(new ControlPlaneClearThreadsResult { DeletedCount = 3 });
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Equal(1, runtime.ClearThreadsCallCount);
        Assert.Empty(runtime.SendCalls);
        Assert.Empty(runtime.TurnSubmissionCommands);
        Assert.Contains("已清空线程：3 个。", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_ThreadDeleteCommand_DeletesWithoutSendingMessage()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(
            workspace.RootPath,
            "chat-script-thread-delete.txt",
            "/thread delete --thread-id thread-delete-chat-001 --confirm thread-delete-chat-001");

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-delete-chat-001" };
        runtime.DeleteThreadAsyncHandler = static (_, _) => Task.FromResult(true);
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Equal(["thread-delete-chat-001"], runtime.DeleteThreadCalls);
        Assert.Empty(runtime.SendCalls);
        Assert.Empty(runtime.TurnSubmissionCommands);
        Assert.Contains("已删除线程：thread-delete-chat-001", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_ThreadDeleteCurrent_DetachesBeforeRuntimeCall()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(
            workspace.RootPath,
            "chat-script-thread-delete-current-detach.txt",
            "/thread delete --thread-id thread-delete-current-detach-001 --confirm thread-delete-current-detach-001");

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-delete-current-detach-001" };
        object? runner = null;
        string? currentThreadDuringRuntimeCall = "<not-called>";
        runtime.DeleteThreadAsyncHandler = (_, _) =>
        {
            currentThreadDuringRuntimeCall = (string?)ReflectionTestHelper.InvokeMethod(runner!, "GetCurrentSessionThreadId");
            return Task.FromResult(false);
        };
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        runner = CreateRunner("TianShu.Cli.Interaction.Host.InteractiveChatSessionHost", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(stderr));
        Assert.Equal(["thread-delete-current-detach-001"], runtime.DeleteThreadCalls);
        Assert.Null(currentThreadDuringRuntimeCall);
        Assert.Empty(runtime.SendCalls);
        Assert.Empty(runtime.TurnSubmissionCommands);
        Assert.Contains("删除线程失败：thread-delete-current-detach-001", stdout + stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_ThreadClearCommand_WithWrongConfirmation_DoesNotCallRuntime()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-thread-clear-cancel.txt", "thread clear --confirm wrong");

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-clear-cancel-001" };
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Equal(0, runtime.ClearThreadsCallCount);
        Assert.Empty(runtime.SendCalls);
        Assert.Empty(runtime.TurnSubmissionCommands);
        Assert.Contains("已取消清空线程。", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_ModelRouteCommand_ListsRouteSets()
    {
        using var workspace = new CliConsumerWorkspace();
        WriteRouteSetConfig(workspace);
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-model-route-list.txt", "/model-route");
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-model-list");

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-model-list-001" };
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot);
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Contains("当前模型路由方案：default", stdout, StringComparison.Ordinal);
        Assert.Contains("可用模型路由方案：", stdout, StringComparison.Ordinal);
        Assert.Contains("default", stdout, StringComparison.Ordinal);
        Assert.Contains("Fast Route Set", stdout, StringComparison.Ordinal);
        Assert.Empty(runtime.ModelListCalls);

        var runDirectory = Assert.Single(Directory.GetDirectories(artifactsRoot));
        var transcriptText = File.ReadAllText(Path.Combine(runDirectory, "transcript.txt"));
        var transcriptRecordsJsonl = File.ReadAllText(Path.Combine(runDirectory, "transcript-records.jsonl"));
        Assert.DoesNotContain("当前模型路由方案：", transcriptText, StringComparison.Ordinal);
        Assert.DoesNotContain("可用模型路由方案：", transcriptText, StringComparison.Ordinal);
        Assert.DoesNotContain("Fast Route Set", transcriptRecordsJsonl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_HostOperationCommands_DoNotPersistControlOutputToTranscript()
    {
        using var workspace = new CliConsumerWorkspace();
        WriteRouteSetConfig(workspace);
        var scriptPath = WriteUtf8File(
            workspace.RootPath,
            "chat-script-host-operation-output.txt",
            string.Join(Environment.NewLine, "/model-route", "/config reload", "/threads"));
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-host-operation-output");

        var runtime = new CliConsumerFakeRuntime();
        runtime.ListThreadsAsyncHandler = static (_, _, _, _) => Task.FromResult<IReadOnlyList<AgentThreadInfo>>(
        [
            new AgentThreadInfo
            {
                ThreadId = "thread-control-output-001",
                Name = "控制命令线程",
                Preview = "不应进入 transcript",
                Cwd = "D:/Work/TianShu",
                UpdatedAt = new DateTimeOffset(2026, 6, 14, 9, 0, 0, TimeSpan.Zero),
            },
        ]);

        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot);
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Contains("当前模型路由方案：default", stdout, StringComparison.Ordinal);
        Assert.Contains("已刷新配置：model=gpt-5.5, provider=openai-compatible", stdout, StringComparison.Ordinal);
        Assert.Contains("控制命令线程", stdout, StringComparison.Ordinal);
        Assert.Empty(runtime.SendCalls);
        Assert.Single(runtime.ProviderPackageReloadCalls);
        Assert.Single(runtime.ThreadListCalls);

        var runDirectory = Assert.Single(Directory.GetDirectories(artifactsRoot));
        var transcriptText = File.ReadAllText(Path.Combine(runDirectory, "transcript.txt"));
        var transcriptRecordsJsonl = File.ReadAllText(Path.Combine(runDirectory, "transcript-records.jsonl"));
        Assert.DoesNotContain("当前模型路由方案：", transcriptText, StringComparison.Ordinal);
        Assert.DoesNotContain("已刷新配置：", transcriptText, StringComparison.Ordinal);
        Assert.DoesNotContain("控制命令线程", transcriptText, StringComparison.Ordinal);
        Assert.DoesNotContain("Fast Route Set", transcriptRecordsJsonl, StringComparison.Ordinal);
        Assert.DoesNotContain("模型 Provider 包已刷新", transcriptRecordsJsonl, StringComparison.Ordinal);
        Assert.DoesNotContain("控制命令线程", transcriptRecordsJsonl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_ModelRouteCommand_SelectsRouteSetForNextNewThread()
    {
        using var workspace = new CliConsumerWorkspace();
        WriteRouteSetConfig(workspace);
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-model-route-select.txt", string.Join(Environment.NewLine, "/model-route fast", "/new"));

        var runtime = new CliConsumerFakeRuntime();
        runtime.StartNewThreadRequestAsyncHandler = (request, _) => Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
        {
            ThreadId = "thread-selected-model-001",
            Preview = request.WorkingDirectory ?? string.Empty,
            Cwd = request.WorkingDirectory,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Contains("已选择模型路由方案：fast", stdout, StringComparison.Ordinal);
        Assert.Contains("已创建线程：thread-selected-model-001", stdout, StringComparison.Ordinal);
        var startRequest = Assert.Single(runtime.StartThreadRequestCalls);
        Assert.Null(startRequest.Model);
        Assert.Equal(workspace.RootPath, startRequest.WorkingDirectory);
        var configWrite = Assert.Single(runtime.ConfigBatchWriteCalls);
        var configItem = Assert.Single(configWrite.Items);
        Assert.Equal("profiles.work.model_route_set", configItem.KeyPath);
        Assert.Equal("fast", configItem.Value?.StringValue);
        Assert.True(configWrite.ReloadUserConfig);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_ModelRouteCommand_UsesSelectedRouteSetForNextUserMessage()
    {
        using var workspace = new CliConsumerWorkspace();
        WriteRouteSetConfig(workspace);
        var scriptPath = WriteUtf8File(
            workspace.RootPath,
            "chat-script-model-route-select-send.txt",
            string.Join(Environment.NewLine, "/model-route fast", "测试发送"));

        var runtime = new CliConsumerFakeRuntime();
        runtime.StartNewThreadRequestAsyncHandler = (request, _) =>
        {
            runtime.ActiveThreadId = "thread-selected-model-send-001";
            return Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
            {
                ThreadId = runtime.ActiveThreadId,
                Preview = request.WorkingDirectory ?? string.Empty,
                Cwd = request.WorkingDirectory,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        };
        runtime.SendAsyncHandler = static (_, _, _) => Task.FromResult(AgentSendResult.Ok("ok", turnId: "turn-selected-model-send-001", turnStatus: "completed"));
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Contains("已选择模型路由方案：fast", stdout, StringComparison.Ordinal);
        Assert.Empty(runtime.StartThreadRequestCalls);
        var configWrite = Assert.Single(runtime.ConfigBatchWriteCalls);
        var configItem = Assert.Single(configWrite.Items);
        Assert.Equal("profiles.work.model_route_set", configItem.KeyPath);
        Assert.Equal("fast", configItem.Value?.StringValue);
        Assert.True(configWrite.ReloadUserConfig);
        var turnCommand = Assert.Single(runtime.TurnSubmissionCommands);
        var input = Assert.IsType<ControlPlaneTextInput>(Assert.Single(turnCommand.Inputs));
        Assert.Equal("测试发送", input.Text);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_ModelRouteCommand_UpdatesCurrentThreadRouteSet()
    {
        using var workspace = new CliConsumerWorkspace();
        WriteRouteSetConfig(workspace);
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-model-route-current.txt", "/model-route fast");

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-current-model-001" };
        runtime.ResumeThreadRequestAsyncHandler = (request, _) => Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
        {
            ThreadId = request.ThreadId.Value,
            Preview = "当前模型线程",
            Cwd = workspace.RootPath,
            UpdatedAt = DateTimeOffset.UtcNow,
            SessionConfiguration = new AgentThreadSessionConfiguration(),
        });
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Contains("已切换模型路由方案：fast", stdout, StringComparison.Ordinal);
        var resumeRequest = Assert.Single(runtime.ResumeThreadRequestCalls);
        Assert.Equal("thread-current-model-001", resumeRequest.ThreadId.Value);
        Assert.Null(resumeRequest.Model);
        Assert.Null(resumeRequest.ModelProvider);
        Assert.Equal("fast", resumeRequest.Configuration?["model_route_set"].StringValue);
        var configWrite = Assert.Single(runtime.ConfigBatchWriteCalls);
        var configItem = Assert.Single(configWrite.Items);
        Assert.Equal("profiles.work.model_route_set", configItem.KeyPath);
        Assert.Equal("fast", configItem.Value?.StringValue);
        Assert.True(configWrite.ReloadUserConfig);
        Assert.Empty(runtime.StartThreadRequestCalls);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_ModelStatus_ShowsResolvedProviderProtocol()
    {
        using var workspace = new CliConsumerWorkspace();
        File.WriteAllText(
            workspace.ConfigPath,
            """
            profile = "work"
            model_route_set = "default"

            [profiles.work]
            model_route_set = "default"

            [providers.openai]
            base_url = "https://api.openai.com/v1"
            api_key_env = "TIANSHU_CLI_TEST_MISSING_KEY"
            default_protocol = "openai_responses"

            [providers.anthropic]
            base_url = "https://api.anthropic.com"
            api_key_env = "TIANSHU_CLI_TEST_MISSING_KEY"
            default_protocol = "anthropic_messages"

            [providers.google]
            base_url = "https://generativelanguage.googleapis.com/v1beta"
            api_key_env = "TIANSHU_CLI_TEST_MISSING_KEY"
            default_protocol = "google_generative"

            [providers.openai-compatible]
            base_url = "https://proxy.example/v1"
            api_key_env = "TIANSHU_CLI_TEST_MISSING_KEY"
            default_protocol = "openai_chat_completions"

            [model_route_sets.default]
            display_name = "Default Route Set"
            routes = [
              { kind = "default", candidates = [
                { provider = "openai", model = "gpt-5.5", protocol = "auto" },
                { provider = "openai-compatible", model = "unknown-model", protocol = "auto" },
              ] },
              { kind = "planning", candidates = [
                { provider = "anthropic", model = "claude-opus-4.8", protocol = "auto" },
              ] },
              { kind = "review", candidates = [
                { provider = "google", model = "gemini-2.5-pro", protocol = "auto" },
              ] },
              { kind = "coding", candidates = [
                { provider = "openai-compatible", model = "openai-compatible-default", protocol = "auto" },
              ] },
            ]
            """,
            new UTF8Encoding(false));
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-model-route-status.txt", "/model-route status");

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-current-model-status-001" };
        runtime.ReadThreadAsyncHandler = (request, _) => Task.FromResult(new ControlPlaneThreadOperationResult
        {
            Thread = new ControlPlaneThreadDetail
            {
                ThreadId = request.ThreadId,
                Preview = "status",
                UpdatedAt = DateTimeOffset.UtcNow,
                SessionConfiguration = new ControlPlaneThreadSessionConfiguration
                {
                    Model = "openai-compatible-default",
                    ModelProvider = "openai-compatible",
                    ProviderBaseUrl = "https://proxy.example/v1",
                    ProviderApiKeyEnvironmentVariable = "TIANSHU_CLI_TEST_MISSING_KEY",
                    ProviderWireApi = "openai_chat_completions",
                },
            },
        });
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Contains("模型路由方案状态验收", stdout, StringComparison.Ordinal);
        Assert.Contains("openai/gpt-5.5", stdout, StringComparison.Ordinal);
        Assert.Contains("routeSet=default", stdout, StringComparison.Ordinal);
        Assert.Contains("stageModels=4", stdout, StringComparison.Ordinal);
        Assert.Contains("protocol=per-candidate resolved", stdout, StringComparison.Ordinal);
        Assert.Contains("序号", stdout, StringComparison.Ordinal);
        Assert.Contains("模型", stdout, StringComparison.Ordinal);
        Assert.Contains("协议", stdout, StringComparison.Ordinal);
        Assert.Contains("测试结果", stdout, StringComparison.Ordinal);
        Assert.Contains("思考", stdout, StringComparison.Ordinal);
        Assert.Contains("耗时", stdout, StringComparison.Ordinal);
        Assert.Contains("报错", stdout, StringComparison.Ordinal);
        Assert.Contains("gpt-5.5", stdout, StringComparison.Ordinal);
        Assert.Contains("responses", stdout, StringComparison.Ordinal);
        Assert.Contains("claude-opus-4.8", stdout, StringComparison.Ordinal);
        Assert.Contains("anthropic_messages", stdout, StringComparison.Ordinal);
        Assert.Contains("gemini-2.5-pro", stdout, StringComparison.Ordinal);
        Assert.Contains("google_generative", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown-model", stdout, StringComparison.Ordinal);
        Assert.Contains("openai_chat_completions", stdout, StringComparison.Ordinal);
        Assert.Contains("报错", stdout, StringComparison.Ordinal);
        Assert.Contains("TIANSHU_CLI_TEST_MISSING_KEY", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("未实现", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("testing 1/4", stdout, StringComparison.Ordinal);
        Assert.Contains("汇总：0/4 个路由阶段模型通过当前路由协议验收。", stdout, StringComparison.Ordinal);
        Assert.Empty(runtime.ModelListCalls);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_ModelStatusMatrix_UsesModelRouteSetAndReportsRedactedProbe()
    {
        using var workspace = new CliConsumerWorkspace();
        File.WriteAllText(
            workspace.ConfigPath,
            """
            profile = "work"
            model_route_set = "default"

            [profiles.work]
            model_route_set = "default"

            [providers.openai-compatible]
            base_url = "https://proxy.example/v1"
            api_key_env = "TIANSHU_CLI_TEST_MISSING_KEY"
            default_protocol = "auto"

            [model_route_sets.default]
            display_name = "Default Route Set"
            routes = [
              { kind = "default", candidates = [{ provider = "openai-compatible", model = "gpt-5.4", protocol = "auto" }] },
            ]
            """,
            new UTF8Encoding(false));
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-model-route-status-matrix.txt", "/model-route status --matrix");

        var runtime = new CliConsumerFakeRuntime();
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Contains("模型路由方案协议兼容矩阵", stdout, StringComparison.Ordinal);
        Assert.Contains("routeSet=default", stdout, StringComparison.Ordinal);
        Assert.Contains("stageModels=1", stdout, StringComparison.Ordinal);
        Assert.Contains("gpt-5.4", stdout, StringComparison.Ordinal);
        Assert.Contains("openai_chat_completions", stdout, StringComparison.Ordinal);
        Assert.Contains("openai_responses", stdout, StringComparison.Ordinal);
        Assert.Contains("anthropic_messages", stdout, StringComparison.Ordinal);
        Assert.Contains("google_generative", stdout, StringComparison.Ordinal);
        Assert.Contains("报错", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("未实现", stdout, StringComparison.Ordinal);
        Assert.Contains("TIANSHU_CLI_TEST_MISSING_KEY", stdout, StringComparison.Ordinal);
        Assert.Contains("汇总：0/4 个模型协议组合连通。", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("testing 1/4", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runtime.ModelListCalls);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_ModelStatusProbeAlias_IsPlainRouteSetArgumentWithoutReminder()
    {
        using var workspace = new CliConsumerWorkspace();
        WriteRouteSetConfig(workspace);
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-model-route-status-old-probe.txt", "/model-route status --probe");

        var runtime = new CliConsumerFakeRuntime();
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        var combinedOutput = stdout + stderr;
        Assert.Contains("未找到模型路由方案：status --probe", combinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("用法：/model-route status [--matrix]", combinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("模型开发可用性验收", combinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("模型协议兼容矩阵", combinedOutput, StringComparison.Ordinal);
        Assert.Empty(runtime.ModelListCalls);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_ConfigReload_UsesRouteSetForNextNewThread()
    {
        using var workspace = new CliConsumerWorkspace();
        File.WriteAllText(
            workspace.ConfigPath,
            """
            profile = "work"

            [profiles.work]
            model_route_set = "default"

            [providers.openai-compatible]
            base_url = "http://127.0.0.1:3001/v1"
            api_key_env = "OPENAI_COMPATIBLE_API_KEY"
            default_protocol = "responses"

            [model_route_sets.default]
            routes = [
              { kind = "default", candidates = [{ provider = "openai-compatible", model = "gpt-5.4", protocol = "responses" }] },
            ]
            """,
            new UTF8Encoding(false));
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-config-reload-new.txt", string.Join(Environment.NewLine, "/reload", "/new"));

        var runtime = new CliConsumerFakeRuntime();
        runtime.StartNewThreadRequestAsyncHandler = (request, _) => Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
        {
            ThreadId = "thread-config-reload-new-001",
            Preview = request.Model ?? string.Empty,
            Cwd = request.WorkingDirectory,
            UpdatedAt = DateTimeOffset.UtcNow,
            ModelProvider = request.ModelProvider,
        });
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Contains("已刷新配置：model=gpt-5.4, provider=openai-compatible", stdout, StringComparison.Ordinal);
        Assert.Contains("已创建线程：thread-config-reload-new-001", stdout, StringComparison.Ordinal);
        var startRequest = Assert.Single(runtime.StartThreadRequestCalls);
        Assert.Null(startRequest.Model);
        Assert.Null(startRequest.ModelProvider);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_ConfigReload_RefreshesCurrentThreadSnapshot()
    {
        using var workspace = new CliConsumerWorkspace();
        File.WriteAllText(
            workspace.ConfigPath,
            """
            profile = "work"

            [profiles.work]
            model_route_set = "default"

            [providers.openai-compatible]
            base_url = "http://127.0.0.1:3001/v1"
            api_key_env = "OPENAI_COMPATIBLE_API_KEY"
            default_protocol = "responses"

            [model_route_sets.default]
            routes = [
              { kind = "default", candidates = [{ provider = "openai-compatible", model = "gpt-5.4", protocol = "responses" }] },
            ]
            """,
            new UTF8Encoding(false));
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-config-reload-current.txt", "/config reload");

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-config-reload-current-001" };
        runtime.ResumeThreadRequestAsyncHandler = (request, _) => Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
        {
            ThreadId = request.ThreadId.Value,
            Preview = "当前刷新线程",
            Cwd = workspace.RootPath,
            UpdatedAt = DateTimeOffset.UtcNow,
            ModelProvider = request.ModelProvider,
            SessionConfiguration = new AgentThreadSessionConfiguration
            {
                Model = "gpt-5.4",
                ModelProvider = "openai-compatible",
            },
        });
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Contains("已刷新当前会话配置：model=gpt-5.4, provider=openai-compatible", stdout, StringComparison.Ordinal);
        var resumeRequest = Assert.Single(runtime.ResumeThreadRequestCalls);
        Assert.Equal("thread-config-reload-current-001", resumeRequest.ThreadId.Value);
        Assert.Null(resumeRequest.Model);
        Assert.Null(resumeRequest.ModelProvider);
        Assert.Equal(workspace.RootPath, resumeRequest.WorkingDirectory);
        Assert.Empty(runtime.StartThreadRequestCalls);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_CommandValidationErrors_ReturnNonZero()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-errors.txt", string.Join(Environment.NewLine, new[]
        {
            "/approve",
            "/input input-001 not-json",
            "/unknown",
            "/rpc model/list {bad}",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-errors-001" };
        var command = ParseCliCommand("chat", "--script", scriptPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, _, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("用法：/approve|/approve-session|/approve-always|/reject|/cancel-approval <callId> [note]", stderr, StringComparison.Ordinal);
        Assert.Contains("解析补录 JSON 失败：", stderr, StringComparison.Ordinal);
        Assert.Contains("未知命令：/unknown", stderr, StringComparison.Ordinal);
        Assert.Contains("解析 RPC 参数 JSON 失败：", stderr, StringComparison.Ordinal);
        Assert.Empty(runtime.RpcCalls);
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_AutoResponders_UseApproveAll_AndUserInputJson()
    {
        using var workspace = new CliConsumerWorkspace();
        var permissionsPath = WriteUtf8File(workspace.RootPath, "chat-auto-permissions.json", "{\"requests\":{\"permission-chat-auto-001\":{\"permissions\":{\"network\":{\"enabled\":true}},\"scope\":\"turn\"}}}");
        var userInputPath = WriteUtf8File(workspace.RootPath, "chat-auto-user-input.json", "{\"requests\":{\"input-chat-auto-001\":{\"choice\":\"A\",\"confirmed\":true}}}");
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-auto.txt", string.Join(Environment.NewLine, new[]
        {
            "自动应答",
            "/wait-complete 2",
        }));
        var approvalTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var permissionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inputTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-auto-001" };
        runtime.RespondToApprovalAsyncHandler = (_, _) =>
        {
            approvalTcs.TrySetResult(true);
            return Task.FromResult(true);
        };
        runtime.RespondToPermissionRequestAsyncHandler = (response, _) =>
        {
            Assert.Equal(ControlPlanePermissionScope.Turn, response.Scope);
            permissionTcs.TrySetResult(true);
            return Task.FromResult(true);
        };
        runtime.RespondToUserInputAsyncHandler = (_, _) =>
        {
            inputTcs.TrySetResult(true);
            return Task.FromResult(true);
        };
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.ApprovalRequested, CallId = "approval-chat-auto-001", ToolName = "shell" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.PermissionRequested, CallId = "permission-chat-auto-001", ToolName = "request_permissions" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.UserInputRequested, CallId = "input-chat-auto-001" });
            await approvalTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            await permissionTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            await inputTcs.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-chat-auto-001", Status = "completed" });
            return AgentSendResult.Ok(message, turnId: "turn-chat-auto-001", turnStatus: "completed");
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--approve-all", "--permissions-json", permissionsPath, "--user-input-json", userInputPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Contains("已自动提交审批响应：approval-chat-auto-001 (accept)", stdout, StringComparison.Ordinal);
        Assert.Contains("已自动提交权限响应：permission-chat-auto-001", stdout, StringComparison.Ordinal);
        Assert.Contains("已自动提交补录答案：input-chat-auto-001", stdout, StringComparison.Ordinal);
        Assert.Single(runtime.ApprovalResponses);
        Assert.Equal(ControlPlaneApprovalDecision.Approve, runtime.ApprovalResponses[0].Decision);
        var chatApprovalPayload = AssertCliGovernanceEnvelope(
            runtime.ApprovalResponses[0].Envelope,
            "cli-approval-approval-chat-auto-001",
            "approval_response");
        Assert.Equal("accept", chatApprovalPayload.Payload.Properties["decision"].GetString());
        Assert.Single(runtime.PermissionResponses);
        var chatPermissionPayload = AssertCliGovernanceEnvelope(
            runtime.PermissionResponses[0].Envelope,
            "cli-permission-permission-chat-auto-001",
            "permission_response");
        Assert.Equal("turn", chatPermissionPayload.Payload.Properties["scope"].GetString());
        Assert.Single(runtime.UserInputResponses);
        var chatUserInputPayload = AssertCliGovernanceEnvelope(
            runtime.UserInputResponses[0].Envelope,
            "cli-userinput-input-chat-auto-001",
            "user_input_submission");
        Assert.Equal("A", chatUserInputPayload.Payload.Properties["answers"].Properties["choice"].GetString());
    }
    [Fact(Skip = "旧 AppHost send 兼容路径已移除；用户补录阻塞由新 loop 治理路径另行覆盖。")]
    public async Task SendCommandRunner_WhenUserInputRequestedWithoutScript_ReturnsApprovalOrInputRequired()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-userinput-required");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-userinput-required-001" };
        runtime.SendAsyncHandler = async (_, _, cancellationToken) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.UserInputRequested, CallId = "input-required-001" });
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return AgentSendResult.Ok("blocked", turnId: "turn-userinput-required-001", turnStatus: "completed");
        };

        var command = ParseCliCommand("send", "--apphost-control-plane", "--message", "等待补录", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot, "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(8, summary.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("ApprovalOrInputRequired", summary.RootElement.GetProperty("exitCodeName").GetString());
        Assert.True(summary.RootElement.GetProperty("userInputRequested").GetBoolean());
        Assert.True(summary.RootElement.GetProperty("userInputBlocked").GetBoolean());
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；权限阻塞由新 loop 治理路径另行覆盖。")]
    public async Task SendCommandRunner_WhenPermissionRequestedWithoutScript_ReturnsApprovalOrInputRequired()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-permission-required");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-permission-required-001" };
        runtime.SendAsyncHandler = async (_, _, cancellationToken) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.PermissionRequested, CallId = "permission-required-001", ToolName = "request_permissions" });
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return AgentSendResult.Ok("blocked", turnId: "turn-permission-required-001", turnStatus: "completed");
        };

        var command = ParseCliCommand("send", "--apphost-control-plane", "--message", "等待权限", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot, "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        Assert.NotNull(result);
        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.Equal("ApprovalOrInputRequired", summary.RootElement.GetProperty("exitCodeName").GetString());
        Assert.True(summary.RootElement.GetProperty("permissionRequested").GetBoolean());
        Assert.True(summary.RootElement.GetProperty("permissionBlocked").GetBoolean());
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；错误终态由 Kernel→Runtime loop 用例覆盖。")]
    public async Task SendCommandRunner_WhenErrorEventObservedAndSendSucceeds_ReturnsTurnFailed()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-turn-failed-success");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-turn-failed-success-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.Error, Message = "stream error" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextDelta, Text = "partial result" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextCompleted, Text = "partial result" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-turn-failed-success-001", Status = "completed" });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-turn-failed-success-001", turnStatus: "completed"));
        };

        var command = ParseCliCommand("send", "--apphost-control-plane", "--message", "即使成功也要失败", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot, "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(7, summary.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("TurnFailed", summary.RootElement.GetProperty("exitCodeName").GetString());
        Assert.Equal("stream error", summary.RootElement.GetProperty("failureMessage").GetString());
        Assert.Equal("partial result", summary.RootElement.GetProperty("resultText").GetString());
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；retry 事件终态由 provider streaming parity 用例覆盖。")]
    public async Task SendCommandRunner_WhenRetryableErrorEventObservedAndSendSucceeds_ReturnsSuccess()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-retryable-error-success");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-retryable-error-success-001" };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.Error, Message = "stream retry", WillRetry = true });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextDelta, Text = "retry recovered" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextCompleted, Text = "retry recovered" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-retryable-error-success-001", Status = "completed" });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-retryable-error-success-001", turnStatus: "completed"));
        };

        var command = ParseCliCommand("send", "--apphost-control-plane", "--message", "可重试错误后完成", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot, "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.True(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Success", summary.RootElement.GetProperty("exitCodeName").GetString());
        Assert.True(summary.RootElement.GetProperty("errorEventObserved").GetBoolean());
        Assert.Equal("retry recovered", summary.RootElement.GetProperty("resultText").GetString());

        var runDirectory = Assert.IsType<string>(summary.RootElement.GetProperty("artifactsDirectory").GetString());
        var eventsJsonl = File.ReadAllText(Path.Combine(runDirectory, "events.jsonl"));
        Assert.Contains("\"WillRetry\":true", eventsJsonl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConversationTurnCommandRunner_WhenRetryableErrorEventObservedAndTurnCompletes_ReturnsZero()
    {
        using var workspace = new CliConsumerWorkspace();
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-followup-retryable-001" };
        runtime.SendFollowUpAsyncHandler = (message, _, _, correlationId) =>
        {
            Assert.False(string.IsNullOrWhiteSpace(correlationId));
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.Error, Message = "follow-up retry", WillRetry = true });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextDelta, Text = "follow-up recovered" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.AssistantTextCompleted, Text = "follow-up recovered" });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-followup-retryable-001", turnStatus: "completed", correlationId: correlationId, requestedMode: FollowUpMode.Queue, effectiveMode: FollowUpMode.Queue));
        };

        var command = ParseCliCommand(WithCommonOptions(workspace, "follow-up", "--mode", "queue", "--message", "继续处理", "--json"));
        var runner = CreateRunner("TianShu.Cli.ConversationTurnCommandRunner", runtime);
        var (exitCode, stdout, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunFollowUpAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal("completed", document.RootElement.GetProperty("turnStatus").GetString());
        Assert.Equal("follow-up recovered", document.RootElement.GetProperty("assistantText").GetString());
        Assert.Empty(document.RootElement.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task InteractiveChatRunner_ScriptedFlow_WhenAutoRespondersFail_ReturnsNonZero()
    {
        using var workspace = new CliConsumerWorkspace();
        var userInputPath = WriteUtf8File(workspace.RootPath, "chat-auto-fail-user-input.json", "{\"requests\":{\"input-chat-fail-001\":{\"choice\":\"A\"}}}");
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-auto-fail.txt", string.Join(Environment.NewLine, new[]
        {
            "自动失败",
            "/wait-complete 2",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-auto-fail-001" };
        runtime.RespondToApprovalAsyncHandler = static (_, _) => Task.FromResult(false);
        runtime.RespondToUserInputAsyncHandler = static (_, _) => Task.FromResult(false);
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.ApprovalRequested, CallId = "approval-chat-fail-001", ToolName = "shell" });
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.UserInputRequested, CallId = "input-chat-fail-001" });
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent { Kind = AgentStreamEventKind.TurnCompleted, ThreadId = runtime.ActiveThreadId, TurnId = "turn-chat-auto-fail-001", Status = "completed" });
            return AgentSendResult.Ok(message, turnId: "turn-chat-auto-fail-001", turnStatus: "completed");
        };

        var command = ParseCliCommand("chat", "--script", scriptPath, "--approve-all", "--user-input-json", userInputPath, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, _, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.Contains("自动提交审批响应失败：approval-chat-fail-001", stderr, StringComparison.Ordinal);
        Assert.Contains("自动提交补录答案失败：input-chat-fail-001", stderr, StringComparison.Ordinal);
    }
    [Fact]
    public async Task ConversationTurnCommandRunner_WhenSendFollowUpThrows_JsonMode_ReturnsNonZeroAndIncludesError()
    {
        using var workspace = new CliConsumerWorkspace();
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-followup-ex-001" };
        runtime.SendFollowUpAsyncHandler = static (_, _, _, _) => throw new InvalidOperationException("follow-up crashed");

        var command = ParseCliCommand(WithCommonOptions(workspace, "follow-up", "--mode", "queue", "--message", "继续", "--json"));
        var runner = CreateRunner("TianShu.Cli.ConversationTurnCommandRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunFollowUpAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal("thread-followup-ex-001", document.RootElement.GetProperty("threadId").GetString());
        Assert.Equal("follow-up crashed", document.RootElement.GetProperty("message").GetString());
        Assert.Contains(document.RootElement.GetProperty("errors").EnumerateArray().Select(static x => x.GetString()), static x => x == "follow-up crashed");
    }

    [Fact(Skip = "旧 AppHost send 兼容路径已移除；初始化失败不再通过 send 旧 runtime 分支验收。")]
    public async Task SendCommandRunner_WhenInitializeFails_WritesFailureArtifactsAndExitCode()
    {
        using var workspace = new CliConsumerWorkspace();
        var artifactsRoot = Path.Combine(workspace.RootPath, "artifacts-init-failed");
        Directory.CreateDirectory(artifactsRoot);

        var runtime = new CliConsumerFakeRuntime();
        runtime.InitializeAsyncHandler = static (_, _) => throw new InvalidOperationException("initialize failed");

        var command = ParseCliCommand("send", "--apphost-control-plane", "--message", "初始化失败", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work", "--artifacts", artifactsRoot, "--json");
        var runner = CreateRunner("TianShu.Cli.SendCommandRunner", runtime);
        var (result, _, _) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return await ReflectionTestHelper.AwaitTaskResultAsync(task);
        });

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(result!, "SummaryJson"));
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.False(summary.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(5, summary.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("InitializeFailed", summary.RootElement.GetProperty("exitCodeName").GetString());
        Assert.Equal("initialize failed", summary.RootElement.GetProperty("failureMessage").GetString());

        var runDirectory = Assert.IsType<string>(summary.RootElement.GetProperty("artifactsDirectory").GetString());
        Assert.True(File.Exists(Path.Combine(runDirectory, "summary.json")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "resolved-options.json")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "events.jsonl")));
        Assert.Equal("初始化失败", File.ReadAllText(Path.Combine(runDirectory, "request.txt")));
        Assert.Equal("initialize failed", File.ReadAllText(Path.Combine(runDirectory, "result.txt")));
    }

    [Fact]
    public async Task InteractiveChatRunner_JsonlProtocol_WhenScriptFails_EmitsStderrRecordAndReturnsNonZero()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-jsonl-fail.txt", string.Join(Environment.NewLine, new[]
        {
            "/unknown",
        }));

        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-jsonl-fail-001" };
        var command = ParseCliCommand("chat", "--script", scriptPath, "--protocol", "jsonl", "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work");
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(1, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        var records = stdout
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();
        Assert.Contains(records, static document =>
            document.RootElement.GetProperty("type").GetString() == "stderr"
            && document.RootElement.GetProperty("text").GetString() == "未知命令：/unknown"
            && !document.RootElement.GetProperty("partial").GetBoolean());
        foreach (var record in records)
        {
            record.Dispose();
        }
    }
    [Fact]
    public async Task InteractiveChatRunner_InitialMessage_WithConsoleEof_WaitsForCompletion_AndPreservesResolvedResumeLatestOptions()
    {
        using var workspace = new CliConsumerWorkspace();
        var sendStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-eof-001" };
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            sendStarted.TrySetResult(true);
            await releaseSend.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            return AgentSendResult.Ok("ok", turnId: "turn-chat-eof-001", turnStatus: "completed");
        };

        var command = ParseCliCommand(WithCommonOptions(workspace, "chat", "--message", "请只回答ok", "--resume-latest"));
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);

        var runTask = CaptureWithInputAsync(string.Empty, async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(200);
        Assert.False(runTask.IsCompleted);

        releaseSend.TrySetResult(true);
        var (exitCode, _, stderr) = await runTask;

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Single(runtime.SendCalls);
        Assert.Equal("请只回答ok", runtime.SendCalls[0].UserMessage);
        Assert.NotNull(runtime.InitializedOptions);
        Assert.True(runtime.InitializedOptions!.ResumeLatestThread);
        Assert.Equal("ANTHROPIC_API_KEY", runtime.InitializedOptions.ProviderApiKeyEnvironmentVariable);
        Assert.Equal("openai-responses", runtime.InitializedOptions.ProtocolAdapter);
    }

    [Fact]
    public async Task InteractiveChatRunner_InitialMessage_WithImages_UsesStructuredSendPreview()
    {
        using var workspace = new CliConsumerWorkspace();
        var imagePath = Path.Combine(workspace.RootPath, "diagram.png");
        File.WriteAllText(imagePath, "stub");

        var sendStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-image-001" };
        runtime.SendAsyncHandler = async (message, _, cancellationToken) =>
        {
            sendStarted.TrySetResult(true);
            await releaseSend.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            return AgentSendResult.Ok("ok", turnId: "turn-chat-image-001", turnStatus: "completed");
        };

        var command = ParseCliCommand(WithCommonOptions(workspace, "chat", "--message", "请描述图片", "-i", imagePath));
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);

        var runTask = CaptureWithInputAsync(string.Empty, async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(200);
        Assert.False(runTask.IsCompleted);

        releaseSend.TrySetResult(true);
        var (exitCode, _, stderr) = await runTask;

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
        Assert.Single(runtime.SendCalls);
        Assert.Equal($"{imagePath}{Environment.NewLine}请描述图片", runtime.SendCalls[0].UserMessage);
        var imageItems = AssertCliConversationEnvelope(
            Assert.Single(runtime.TurnSubmissionCommands).Envelope,
            "cli-turn-",
            "turn_submission");
        Assert.Collection(
            imageItems,
            item => Assert.Equal(imagePath, Assert.IsType<LocalImageInteractionItem>(item).Path),
            item => Assert.Equal("请描述图片", Assert.IsType<TextInteractionItem>(item).Text));
    }

    [Fact]
    public async Task InteractiveChatRunner_InitialMessage_WithConsoleEof_WhenSendFails_ReturnsNonZero()
    {
        using var workspace = new CliConsumerWorkspace();
        var sendStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CliConsumerFakeRuntime { ActiveThreadId = "thread-chat-eof-fail-001" };
        runtime.SendAsyncHandler = async (_, _, cancellationToken) =>
        {
            sendStarted.TrySetResult(true);
            await releaseSend.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            return AgentSendResult.Fail("模拟发送失败");
        };

        var command = ParseCliCommand(WithCommonOptions(workspace, "chat", "--message", "请只回答ok", "--resume-latest"));
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);

        var runTask = CaptureWithInputAsync(string.Empty, async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(200);
        Assert.False(runTask.IsCompleted);

        releaseSend.TrySetResult(true);
        var (exitCode, _, stderr) = await runTask;

        Assert.Equal(1, exitCode);
        Assert.Contains("send 执行失败：模拟发送失败", stderr, StringComparison.Ordinal);
        Assert.NotNull(runtime.InitializedOptions);
        Assert.True(runtime.InitializedOptions!.ResumeLatestThread);
        Assert.Equal("ANTHROPIC_API_KEY", runtime.InitializedOptions.ProviderApiKeyEnvironmentVariable);
    }

    [Fact]
    public async Task InteractiveChatRunner_StartupResumeLast_ResumesMostRecentThread()
    {
        using var workspace = new CliConsumerWorkspace();
        var runtime = new CliConsumerFakeRuntime();
        runtime.ListThreadsAsyncHandler = static (limit, archived, matchCurrentCwd, _) =>
        {
            Assert.Equal(1, limit);
            Assert.False(archived);
            Assert.True(matchCurrentCwd);
            return Task.FromResult<IReadOnlyList<AgentThreadInfo>>(
            [
                new AgentThreadInfo
                {
                    ThreadId = "thread-last-001",
                    Name = "最近线程",
                    Preview = "最近线程",
                    Cwd = "D:/Work/TianShu",
                    UpdatedAt = new DateTimeOffset(2026, 3, 26, 9, 0, 0, TimeSpan.Zero),
                },
            ]);
        };
        runtime.ResumeThreadAsyncHandler = (threadId, _) =>
        {
            runtime.ActiveThreadId = threadId;
            return Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
            {
                ThreadId = threadId,
                Preview = "最近线程",
                Cwd = workspace.RootPath,
                SeedHistory = [],
                Turns = [],
            });
        };

        var command = ParseCliCommand(WithCommonOptions(workspace, "resume", "--last"));
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureWithInputAsync(string.Empty, async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.NotNull(runtime.InitializedOptions);
        Assert.False(runtime.InitializedOptions!.CreateThreadOnInitialize);
        Assert.Single(runtime.ThreadListCalls);
        Assert.Single(runtime.ResumeThreadCalls);
        Assert.Equal("thread-last-001", runtime.ResumeThreadCalls[0]);
        Assert.Contains("已恢复线程：thread-last-001（种子历史 0，回合数 0）", stdout, StringComparison.Ordinal);
        Assert.Contains("天枢 TianShu 已启动。", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_StartupResumeLast_WhenSnapshotHasStaleActiveTurn_PlainTextStartsNewTurn()
    {
        using var workspace = new CliConsumerWorkspace();
        var scriptPath = WriteUtf8File(workspace.RootPath, "chat-script-resume-stale-active.txt", string.Join(Environment.NewLine, new[]
        {
            "你用的是什么模型?",
            "/wait-complete 2",
        }));
        var runtime = new CliConsumerFakeRuntime();
        runtime.ListThreadsAsyncHandler = static (_, _, _, _) =>
            Task.FromResult<IReadOnlyList<AgentThreadInfo>>(
            [
                new AgentThreadInfo
                {
                    ThreadId = "thread-stale-active-001",
                    Name = "已完成线程",
                    Cwd = "D:/Work/TianShu",
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
            ]);
        runtime.ResumeThreadAsyncHandler = (threadId, _) =>
        {
            runtime.ActiveThreadId = threadId;
            runtime.HasActiveTurn = true;
            return Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
            {
                ThreadId = threadId,
                Preview = "已完成线程",
                Cwd = workspace.RootPath,
                SeedHistory = [],
                Turns = [],
            });
        };
        runtime.SendAsyncHandler = (message, _, _) =>
        {
            runtime.HasActiveTurn = false;
            EmitProjectedStreamEvent(runtime, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.TurnCompleted,
                ThreadId = runtime.ActiveThreadId,
                TurnId = "turn-stale-active-new-001",
                Status = "completed",
            });
            return Task.FromResult(AgentSendResult.Ok(message, turnId: "turn-stale-active-new-001", turnStatus: "completed"));
        };
        runtime.SendFollowUpAsyncHandler = static (message, mode, _, correlationId) =>
            Task.FromResult(AgentSendResult.Fail(
                $"不应发送 follow-up：{message}/{mode}/{correlationId}",
                turnStatus: "failed"));

        var command = ParseCliCommand(WithCommonOptions(workspace, "resume", "--last", "--script", scriptPath));
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, _, stderr) = await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        var send = Assert.Single(runtime.SendCalls);
        Assert.Equal("你用的是什么模型?", send.UserMessage);
        Assert.Empty(runtime.FollowUpCalls);
        Assert.DoesNotContain("/follow-up 执行失败", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_StartupFork_ResolvesExplicitNameAcrossAllCwds()
    {
        using var workspace = new CliConsumerWorkspace();
        var runtime = new CliConsumerFakeRuntime();
        runtime.ListThreadsAsyncHandler = static (limit, archived, matchCurrentCwd, _) =>
        {
            Assert.Equal(100, limit);
            Assert.False(archived);
            Assert.False(matchCurrentCwd);
            return Task.FromResult<IReadOnlyList<AgentThreadInfo>>(
            [
                new AgentThreadInfo
                {
                    ThreadId = "thread-source-001",
                    Name = "别的会话",
                    Preview = "别的会话",
                    Cwd = "D:/Elsewhere",
                    UpdatedAt = new DateTimeOffset(2026, 3, 25, 12, 0, 0, TimeSpan.Zero),
                },
                new AgentThreadInfo
                {
                    ThreadId = "thread-source-002",
                    Name = "目标会话",
                    Preview = "目标会话",
                    Cwd = "D:/Elsewhere",
                    UpdatedAt = new DateTimeOffset(2026, 3, 26, 12, 0, 0, TimeSpan.Zero),
                },
            ]);
        };
        runtime.ForkThreadAsyncHandler = (threadId, _) =>
        {
            runtime.ActiveThreadId = threadId + "-fork";
            return Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
            {
                ThreadId = threadId + "-fork",
                Preview = "分叉线程",
                Cwd = workspace.RootPath,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        };

        var command = ParseCliCommand(WithCommonOptions(workspace, "fork", "目标会话", "--all"));
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureWithInputAsync(string.Empty, async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.NotNull(runtime.InitializedOptions);
        Assert.False(runtime.InitializedOptions!.CreateThreadOnInitialize);
        Assert.Single(runtime.ThreadListCalls);
        Assert.Single(runtime.ForkThreadCalls);
        Assert.Equal("thread-source-002", runtime.ForkThreadCalls[0]);
        Assert.Contains("已分叉线程：thread-source-002-fork", stdout, StringComparison.Ordinal);
        Assert.Contains("天枢 TianShu 已启动。", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractiveChatRunner_StartupResumePicker_CanSelectThreadFromConsole()
    {
        using var workspace = new CliConsumerWorkspace();
        var runtime = new CliConsumerFakeRuntime();
        runtime.ListThreadsAsyncHandler = static (limit, archived, matchCurrentCwd, _) =>
        {
            Assert.Equal(20, limit);
            Assert.False(archived);
            Assert.True(matchCurrentCwd);
            return Task.FromResult<IReadOnlyList<AgentThreadInfo>>(
            [
                new AgentThreadInfo
                {
                    ThreadId = "thread-picker-001",
                    Name = "第一个线程",
                    Preview = "第一个线程",
                    Cwd = "D:/Work/TianShu",
                    UpdatedAt = new DateTimeOffset(2026, 3, 25, 9, 0, 0, TimeSpan.Zero),
                },
                new AgentThreadInfo
                {
                    ThreadId = "thread-picker-002",
                    Name = "第二个线程",
                    Preview = "第二个线程",
                    Cwd = "D:/Work/TianShu",
                    UpdatedAt = new DateTimeOffset(2026, 3, 26, 9, 0, 0, TimeSpan.Zero),
                },
            ]);
        };
        runtime.ResumeThreadAsyncHandler = (threadId, _) =>
        {
            runtime.ActiveThreadId = threadId;
            return Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
            {
                ThreadId = threadId,
                Preview = "picker 恢复线程",
                Cwd = workspace.RootPath,
                SeedHistory = [],
                Turns = [],
            });
        };

        var command = ParseCliCommand(WithCommonOptions(workspace, "resume"));
        var runner = CreateRunner("TianShu.Cli.InteractiveChatRunner", runtime);
        var (exitCode, stdout, _) = await CaptureWithInputAsync("2\r\n", async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });

        Assert.Equal(0, exitCode);
        Assert.Single(runtime.ThreadListCalls);
        Assert.Single(runtime.ResumeThreadCalls);
        Assert.Equal("thread-picker-002", runtime.ResumeThreadCalls[0]);
        Assert.Contains("请选择要恢复的线程：", stdout, StringComparison.Ordinal);
        Assert.Contains("已恢复线程：thread-picker-002（种子历史 0，回合数 0）", stdout, StringComparison.Ordinal);
    }

    private static string[] WithCommonOptions(CliConsumerWorkspace workspace, params string[] args)
        => [.. args, "--cwd", workspace.RootPath, "--config", workspace.ConfigPath, "--profile", "work"];

    private static void WriteKernelRuntimeProviderConfig(CliConsumerWorkspace workspace, string providerBaseUrl, string envKey)
        => File.WriteAllText(
            workspace.ConfigPath,
            $$"""
            profile = "work"
            model = "gpt-test"
            provider = "openai"

            [profiles.work]
            model = "gpt-test"
            provider = "openai"

            [providers.openai]
            base_url = "{{providerBaseUrl}}"
            api_key_env = "{{envKey}}"
            default_protocol = "responses"
            protocol_fallbacks = ["responses"]
            request_max_retries = 0
            stream_max_retries = 0
            stream_idle_timeout_ms = 30000
            supports_websockets = false
            """,
            new UTF8Encoding(false));

    private static string WriteHostControlActiveRunRecord(string workspacePath, string threadId, string turnId, string runId)
    {
        var root = Path.Combine(workspacePath, ".tianshu", "kernel-runtime", "host-control");
        var threadIndexDirectory = Path.Combine(root, "active-runs", "by-thread");
        var turnIndexDirectory = Path.Combine(root, "active-runs", "by-turn");
        var cancellationDirectory = Path.Combine(root, "cancellations");
        Directory.CreateDirectory(threadIndexDirectory);
        Directory.CreateDirectory(turnIndexDirectory);
        Directory.CreateDirectory(cancellationDirectory);
        var threadIndexPath = Path.Combine(threadIndexDirectory, threadId + ".json");
        var turnIndexPath = Path.Combine(turnIndexDirectory, turnId + ".json");
        var cancelSignalPath = Path.Combine(cancellationDirectory, threadId + "." + turnId + ".cancel.json");
        var payload = JsonSerializer.Serialize(
            new
            {
                threadId,
                turnId,
                runId,
                message = "external product active run",
                startedAtUtc = DateTimeOffset.UtcNow,
                rootDirectory = root,
                threadIndexPath,
                turnIndexPath,
                cancelSignalPath,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        File.WriteAllText(threadIndexPath, payload, new UTF8Encoding(false));
        File.WriteAllText(turnIndexPath, payload, new UTF8Encoding(false));
        return cancelSignalPath;
    }

    private static string WriteHostControlCheckpointRecord(
        string workspacePath,
        string sessionId,
        string threadId,
        string turnId,
        IReadOnlyList<string> steerInputs)
    {
        var root = Path.Combine(workspacePath, ".tianshu", "kernel-runtime", "host-control");
        var checkpointDirectory = Path.Combine(root, "checkpoints");
        Directory.CreateDirectory(checkpointDirectory);
        var checkpointRef = $"checkpoint://kernel-runtime/{threadId}/{turnId}/terminal";
        var checkpointPath = Path.Combine(checkpointDirectory, SanitizePathSegment(checkpointRef) + ".json");
        var payload = JsonSerializer.Serialize(
            new
            {
                checkpointRef,
                sessionId,
                threadId,
                turnId,
                turnStatus = "completed",
                replayCompleteness = "live-pass-reactive-graph",
                stagePath = new[] { "prepare-context", "model-reason", "finalize" },
                turnLogRef = "turn-log://product-resume-test",
                rolloutRef = "rollout://product-resume-test",
                steerInputs,
                createdAtUtc = DateTimeOffset.UtcNow,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        File.WriteAllText(checkpointPath, payload, new UTF8Encoding(false));
        return checkpointRef;
    }

    private static void WriteHostControlPendingSteerRecord(
        string workspacePath,
        string threadId,
        string turnId,
        IReadOnlyList<string> inputs)
    {
        var root = Path.Combine(workspacePath, ".tianshu", "kernel-runtime", "host-control");
        var pendingSteerDirectory = Path.Combine(root, "pending-steers");
        Directory.CreateDirectory(pendingSteerDirectory);
        var payload = JsonSerializer.Serialize(
            new
            {
                threadId,
                turnId,
                inputs,
                updatedAtUtc = DateTimeOffset.UtcNow,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        File.WriteAllText(Path.Combine(pendingSteerDirectory, SanitizePathSegment(threadId) + ".json"), payload, new UTF8Encoding(false));
    }

    private static bool JsonContainsString(JsonElement element, string expected)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return string.Equals(element.GetString(), expected, StringComparison.Ordinal);
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(item => JsonContainsString(item, expected));
            case JsonValueKind.Object:
                return element.EnumerateObject().Any(property => JsonContainsString(property.Value, expected));
            default:
                return false;
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static string BuildProviderSse(string text)
    {
        var builder = new StringBuilder();
        builder.Append("data: ");
        builder.Append(JsonSerializer.Serialize(new { type = "response.output_text.delta", delta = text }));
        builder.Append("\n\n");
        builder.Append("data: ");
        builder.Append(JsonSerializer.Serialize(new
        {
            type = "response.completed",
            response = new
            {
                output_text = text,
                usage = new { input_tokens = 4, output_tokens = 1 },
            },
        }));
        builder.Append("\n\n");
        builder.Append("data: [DONE]\n\n");
        return builder.ToString();
    }

    private static object ParseCliCommand(params string[] args)
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(parserType, "Parse", (object)args);
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);
        return command!;
    }

    private static object CreateRunner(string typeName, IExecutionRuntime runtime)
    {
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, typeName);
        var ctor = runnerType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(x => x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(Func<IExecutionRuntime>));
        return ctor.Invoke([new Func<IExecutionRuntime>(() => runtime)]);
    }

    private static async Task<(T Result, string StdOut, string StdErr)> CaptureAsync<T>(Func<Task<T>> action)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var result = await action().ConfigureAwait(false);
            return (result, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    private static async Task<(T Result, string StdOut, string StdErr)> CaptureWithInputAsync<T>(string input, Func<Task<T>> action)
    {
        using var stdin = new StringReader(input);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var originalIn = Console.In;
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        try
        {
            Console.SetIn(stdin);
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var result = await action().ConfigureAwait(false);
            return (result, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunThreadAsync(CliConsumerWorkspace workspace, CliConsumerFakeRuntime runtime, params string[] tailArgs)
    {
        var command = ParseCliCommand(WithCommonOptions(workspace, ["thread", .. tailArgs]));
        var runner = CreateRunner("TianShu.Cli.CliRuntimeCommandRunner", runtime);
        return await CaptureAsync(async () =>
        {
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunThreadAsync", command, CancellationToken.None);
            return Assert.IsType<int>(await ReflectionTestHelper.AwaitTaskResultAsync(task));
        });
    }

    private static JsonElement Json(string json)
        => ReflectionTestHelper.ParseJsonElement(json);

    private static StructuredValue StructuredJson(string json)
        => StructuredValue.FromJsonElement(ReflectionTestHelper.ParseJsonElement(json));

    private static string WriteUtf8File(string directory, string fileName, string content)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static IReadOnlyList<string> ExtractJsonObjects(string text)
    {
        var blocks = new List<string>();
        var searchIndex = 0;
        while (searchIndex < text.Length)
        {
            var startIndex = text.IndexOf('{', searchIndex);
            if (startIndex < 0)
            {
                break;
            }

            var endIndex = FindJsonObjectEnd(text, startIndex);
            if (endIndex < 0)
            {
                break;
            }

            blocks.Add(text[startIndex..(endIndex + 1)]);
            searchIndex = endIndex + 1;
        }

        return blocks;
    }

    private static int FindJsonObjectEnd(string text, int startIndex)
    {
        var depth = 0;
        var inString = false;
        var isEscaped = false;
        for (var index = startIndex; index < text.Length; index++)
        {
            var ch = text[index];
            if (inString)
            {
                if (isEscaped)
                {
                    isEscaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    isEscaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
                continue;
            }

            if (ch != '}')
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static void WriteMissingProviderConfig(CliConsumerWorkspace workspace)
    {
        var missingProviderEnvKey = $"TIANSHU_CLI_TEST_MISSING_KEY_{Guid.NewGuid():N}";
        File.WriteAllText(
            workspace.ConfigPath,
            $$"""
            profile = "work"

            [profiles.work]
            model = "claude-3-7-sonnet"
            provider = "anthropic"
            approval_policy = "on-request"

            [providers.anthropic]
            base_url = "https://api.anthropic.com/v1"
            api_key_env = "{{missingProviderEnvKey}}"
            default_protocol = "responses"
            """,
            new UTF8Encoding(false));
    }

    private static void WriteDefaultRouteSetUserConfig(string tianShuHome)
    {
        var missingProviderEnvKey = $"TIANSHU_CLI_TEST_MISSING_KEY_{Guid.NewGuid():N}";
        var routeSetDirectory = Path.Combine(tianShuHome, "modules", "model", "route-sets");
        var protocolRuleDirectory = Path.Combine(tianShuHome, "modules", "model", "protocol-rules");
        var providerInstancesDirectory = Path.Combine(tianShuHome, "modules", "model", "provider-instances");
        Directory.CreateDirectory(tianShuHome);
        Directory.CreateDirectory(routeSetDirectory);
        Directory.CreateDirectory(protocolRuleDirectory);
        Directory.CreateDirectory(providerInstancesDirectory);
        File.WriteAllText(
            Path.Combine(tianShuHome, "tianshu.toml"),
            """
            profile = "default"
            model_route_set = "default"
            model_protocol_rule_set = "default"
            provider_instances = "default"
            approval_policy = "never"
            sandbox_mode = "danger-full-access"

            [profiles.default]
            model_route_set = "default"
            """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(routeSetDirectory, "default.toml"),
            """
            [model_route_sets.default]
            display_name = "Default Model Route Set"
            routes = [
              { kind = "default", candidates = [{ provider = "openai-compatible", model = "openai-compatible-default", protocol = "openai_chat_completions" }] }
            ]
            """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(protocolRuleDirectory, "default.toml"),
            """
            [model_protocol_rule_sets.default]
            display_name = "Default Model Protocol Rules"
            rules = [
              { match = "deepseek*", protocols = ["openai_chat_completions"] }
            ]
            """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(providerInstancesDirectory, "default.toml"),
            $$"""
            [providers.openai-compatible]
            base_url = "https://provider.example.invalid/v1"
            api_key_env = "{{missingProviderEnvKey}}"
            default_protocol = "openai_chat_completions"
            protocol_fallbacks = ["openai_chat_completions"]
            request_max_retries = 0
            stream_max_retries = 0
            stream_idle_timeout_ms = 30000
            websocket_connect_timeout_ms = 15000
            supports_websockets = false
            """,
            new UTF8Encoding(false));
    }

    private static void AssertKernelRuntimeRouteEvidence(JsonElement summary)
    {
        Assert.Equal("kernel-runtime-loop", summary.GetProperty("executionPath").GetString());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("fallbackReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("failureCode").ValueKind);
    }

    private static void AssertKernelRuntimeSemanticProjectionEquivalent(JsonElement expected, JsonElement actual)
    {
        Assert.Equal(expected.GetProperty("turnStatus").GetString(), actual.GetProperty("turnStatus").GetString());
        Assert.Equal(expected.GetProperty("kernelRuntimeDisposition").GetString(), actual.GetProperty("kernelRuntimeDisposition").GetString());
        Assert.Equal(expected.GetProperty("replayCompleteness").GetString(), actual.GetProperty("replayCompleteness").GetString());
        Assert.Equal(
            expected.GetProperty("stagePath").EnumerateArray().Select(static item => item.GetString()).ToArray(),
            actual.GetProperty("stagePath").EnumerateArray().Select(static item => item.GetString()).ToArray());

        var expectedReplay = expected.GetProperty("replaySummary");
        var actualReplay = actual.GetProperty("replaySummary");
        Assert.Equal(expectedReplay.GetProperty("completeness").GetString(), actualReplay.GetProperty("completeness").GetString());
        Assert.Equal(
            expectedReplay.GetProperty("failureCodes").EnumerateArray().Select(static item => item.GetString()).Order(StringComparer.Ordinal).ToArray(),
            actualReplay.GetProperty("failureCodes").EnumerateArray().Select(static item => item.GetString()).Order(StringComparer.Ordinal).ToArray());

        var expectedDiagnostics = expected.GetProperty("runtimeDiagnosticsProjection");
        var actualDiagnostics = actual.GetProperty("runtimeDiagnosticsProjection");
        Assert.Equal(expectedDiagnostics.GetProperty("modelMetricsEventCount").GetInt32(), actualDiagnostics.GetProperty("modelMetricsEventCount").GetInt32());
        Assert.Equal(expectedDiagnostics.GetProperty("toolMetricsEventCount").GetInt32(), actualDiagnostics.GetProperty("toolMetricsEventCount").GetInt32());
        Assert.Equal(
            expectedDiagnostics.GetProperty("failureCodes").EnumerateArray().Select(static item => item.GetString()).Order(StringComparer.Ordinal).ToArray(),
            actualDiagnostics.GetProperty("failureCodes").EnumerateArray().Select(static item => item.GetString()).Order(StringComparer.Ordinal).ToArray());

        var expectedTokenUsage = expectedDiagnostics.GetProperty("tokenUsage");
        var actualTokenUsage = actualDiagnostics.GetProperty("tokenUsage");
        Assert.Equal(expectedTokenUsage.GetProperty("available").GetBoolean(), actualTokenUsage.GetProperty("available").GetBoolean());
        Assert.Equal(expectedTokenUsage.GetProperty("estimated").GetBoolean(), actualTokenUsage.GetProperty("estimated").GetBoolean());
        Assert.Equal(expectedTokenUsage.GetProperty("missingReason").GetString(), actualTokenUsage.GetProperty("missingReason").GetString());

        var expectedCost = expectedDiagnostics.GetProperty("cost");
        var actualCost = actualDiagnostics.GetProperty("cost");
        Assert.Equal(expectedCost.GetProperty("available").GetBoolean(), actualCost.GetProperty("available").GetBoolean());
        Assert.Equal(expectedCost.GetProperty("missingReason").GetString(), actualCost.GetProperty("missingReason").GetString());

        var expectedTerminal = expected.GetProperty("kernelRuntimeTerminalProjection");
        var actualTerminal = actual.GetProperty("kernelRuntimeTerminalProjection");
        Assert.Equal(expectedTerminal.GetProperty("turnStatus").GetString(), actualTerminal.GetProperty("turnStatus").GetString());
        Assert.Equal(expectedTerminal.GetProperty("replayCompleteness").GetString(), actualTerminal.GetProperty("replayCompleteness").GetString());
        Assert.Equal(
            expectedTerminal.GetProperty("turnLog").GetProperty("available").GetBoolean(),
            actualTerminal.GetProperty("turnLog").GetProperty("available").GetBoolean());
        Assert.Equal(
            expectedTerminal.GetProperty("rolloutRecord").GetProperty("available").GetBoolean(),
            actualTerminal.GetProperty("rolloutRecord").GetProperty("available").GetBoolean());
        Assert.Equal(
            expectedTerminal.GetProperty("downgradeReasons").EnumerateArray().Select(static item => item.GetString()).Order(StringComparer.Ordinal).ToArray(),
            actualTerminal.GetProperty("downgradeReasons").EnumerateArray().Select(static item => item.GetString()).Order(StringComparer.Ordinal).ToArray());
    }

    private static void AssertKernelRuntimeFullProductParityGateOpen(JsonElement summary)
    {
        Assert.Equal("kernel-runtime-loop", summary.GetProperty("executionPath").GetString());
        Assert.NotEqual("live-pass-reactive-graph", summary.GetProperty("replayCompleteness").GetString());

        var terminalProjection = summary.GetProperty("kernelRuntimeTerminalProjection");
        Assert.True(terminalProjection.GetProperty("turnLog").GetProperty("available").GetBoolean());
        Assert.Null(terminalProjection.GetProperty("turnLog").GetProperty("reason").GetString());
        Assert.True(File.Exists(terminalProjection.GetProperty("turnLog").GetProperty("reference").GetString()));
        Assert.True(terminalProjection.GetProperty("rolloutRecord").GetProperty("available").GetBoolean());
        Assert.Null(terminalProjection.GetProperty("rolloutRecord").GetProperty("reason").GetString());
        Assert.True(File.Exists(terminalProjection.GetProperty("rolloutRecord").GetProperty("reference").GetString()));

        var downgradeReasons = terminalProjection
            .GetProperty("downgradeReasons")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        Assert.DoesNotContain("turn_log_not_migrated_23_6", downgradeReasons);
        Assert.DoesNotContain("rollout_record_not_migrated_23_6", downgradeReasons);
    }

    private static void WriteRouteSetConfig(CliConsumerWorkspace workspace)
        => File.WriteAllText(
            workspace.ConfigPath,
            """
            profile = "work"
            model_route_set = "default"

            [profiles.work]
            model_route_set = "default"
            approval_policy = "on-request"

            [providers.openai-compatible]
            base_url = "https://proxy.example/v1"
            api_key_env = "TIANSHU_CLI_TEST_MISSING_KEY"
            default_protocol = "auto"

            [model_route_sets.default]
            display_name = "Default Route Set"
            routes = [
              { kind = "default", candidates = [{ provider = "openai-compatible", model = "gpt-5.5", protocol = "auto" }] },
            ]

            [model_route_sets.fast]
            display_name = "Fast Route Set"
            routes = [
              { kind = "default", candidates = [{ provider = "openai-compatible", model = "gpt-5.5-mini", protocol = "auto" }] },
            ]
            """,
            new UTF8Encoding(false));

    private sealed class SingleResponseProviderServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource shutdown = new();
        private readonly string responseBody;
        private readonly Task acceptTask;
        private readonly TaskCompletionSource<string> requestBody = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SingleResponseProviderServer(string responseBody)
        {
            this.responseBody = responseBody;
            var port = GetFreePort();
            BaseUrl = $"http://127.0.0.1:{port}";
            listener.Prefixes.Add($"{BaseUrl}/");
            listener.Start();
            acceptTask = AcceptAsync();
        }

        public string BaseUrl { get; }

        public Task<string> RequestBodyTask => requestBody.Task;

        public void Dispose()
        {
            listener.Close();
            shutdown.Cancel();
            shutdown.Dispose();
            try
            {
                acceptTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }
        }

        private async Task AcceptAsync()
        {
            try
            {
                var context = await listener.GetContextAsync().WaitAsync(shutdown.Token).ConfigureAwait(false);
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                {
                    requestBody.TrySetResult(await reader.ReadToEndAsync().ConfigureAwait(false));
                }

                var buffer = Encoding.UTF8.GetBytes(responseBody);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/event-stream; charset=utf-8";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, shutdown.Token).ConfigureAwait(false);
                context.Response.Close();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException or OperationCanceledException)
            {
                requestBody.TrySetCanceled();
            }
        }

        private static int GetFreePort()
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            try
            {
                return ((IPEndPoint)tcp.LocalEndpoint).Port;
            }
            finally
            {
                tcp.Stop();
            }
        }
    }
}

internal sealed class CliConsumerWorkspace : IDisposable
{
    private readonly TestTempDirectory tempDirectory = new();

    public CliConsumerWorkspace()
    {
        RootPath = tempDirectory.Path;
        Directory.CreateDirectory(Path.Combine(RootPath, ".git"));
        File.WriteAllText(Path.Combine(RootPath, "TianShu.sln"), string.Empty, new UTF8Encoding(false));

        var kernelProjectDirectory = Path.Combine(RootPath, "src", "Infrastructure", "TianShu.Kernel");
        Directory.CreateDirectory(kernelProjectDirectory);
        File.WriteAllText(Path.Combine(kernelProjectDirectory, "TianShu.AppHost.csproj"), "<Project />", new UTF8Encoding(false));

        var appHostProjectDirectory = Path.Combine(RootPath, "src", "Hosting", "TianShu.AppHost");
        Directory.CreateDirectory(appHostProjectDirectory);
        File.WriteAllText(Path.Combine(appHostProjectDirectory, "TianShu.AppHost.csproj"), "<Project />", new UTF8Encoding(false));

        ConfigPath = Path.Combine(RootPath, ".tianshu", "tianshu.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(
            ConfigPath,
            """
            profile = "work"
            model = "gpt-5-codex"
            provider = "openai"
            approval_policy = "never"

            [profiles.work]
            model = "claude-3-7-sonnet"
            provider = "anthropic"
            approval_policy = "on-request"

            [providers.openai]
            base_url = "https://api.openai.com/v1"
            api_key_env = "OPENAI_API_KEY"
            default_protocol = "responses"

            [providers.anthropic]
            base_url = "https://api.anthropic.com/v1"
            api_key_env = "ANTHROPIC_API_KEY"
            default_protocol = "responses"
            """,
            new UTF8Encoding(false));
    }

    public string RootPath { get; }

    public string ConfigPath { get; }

    public void Dispose()
        => tempDirectory.Dispose();
}

internal sealed class CliConsumerFakeRuntime : IExecutionRuntimeDiagnostics
{
    public ControlPlaneInitializeRuntimeCommand? InitializedOptions { get; private set; }
    public List<(string UserMessage, IReadOnlyList<ConversationMessage> History)> SendCalls { get; } = [];
    public List<ControlPlaneSubmitTurnCommand> TurnSubmissionCommands { get; } = [];
    public List<string> UserShellCommandCalls { get; } = [];
    public List<(string UserMessage, FollowUpMode Mode, string? CorrelationId)> FollowUpCalls { get; } = [];
    public List<ControlPlaneSubmitFollowUpCommand> FollowUpSubmissionCommands { get; } = [];
    public List<ControlPlaneMutatePendingFollowUpCommand> PendingFollowUpMutationCommands { get; } = [];
    public int InterruptCallCount { get; private set; }
    public List<ControlPlaneApprovalResolution> ApprovalResponses { get; } = [];
    public List<ControlPlanePermissionGrant> PermissionResponses { get; } = [];
    public List<ControlPlaneUserInputSubmission> UserInputResponses { get; } = [];
    public List<(int Limit, bool Archived, bool MatchCurrentCwd)> ThreadListCalls { get; } = [];
    public List<ControlPlaneThreadListQuery> ThreadListRequestCalls { get; } = [];
    public List<string> StartThreadCalls { get; } = [];
    public List<ControlPlaneStartThreadCommand> StartThreadRequestCalls { get; } = [];
    public List<string> ForkThreadCalls { get; } = [];
    public List<string> ArchiveThreadCalls { get; } = [];
    public List<string> DeleteThreadCalls { get; } = [];
    public int ClearThreadsCallCount { get; private set; }
    public List<(string ThreadId, string Name)> RenameThreadCalls { get; } = [];
    public List<string> ResumeThreadCalls { get; } = [];
    public List<ControlPlaneResumeThreadCommand> ResumeThreadRequestCalls { get; } = [];
    public List<ControlPlaneLoadedThreadListQuery> ThreadLoadedListCalls { get; } = [];
    public List<ControlPlaneReadThreadQuery> ThreadReadCalls { get; } = [];
    public List<(string ThreadId, int KeepRecentTurns)> CompactThreadCalls { get; } = [];
    public List<string> CleanBackgroundTerminalsCalls { get; } = [];
    public List<string> UnsubscribeThreadCalls { get; } = [];
    public List<string> UnarchiveThreadCalls { get; } = [];
    public List<ControlPlaneUpdateThreadMetadataCommand> ThreadMetadataUpdateCalls { get; } = [];
    public List<ControlPlaneRollbackThreadCommand> ThreadRollbackCalls { get; } = [];
    public List<ControlPlaneRealtimeStartCommand> RealtimeStartCalls { get; } = [];
    public List<ControlPlaneRealtimeAppendTextCommand> RealtimeAppendTextCalls { get; } = [];
    public List<ControlPlaneRealtimeAppendAudioCommand> RealtimeAppendAudioCalls { get; } = [];
    public List<ControlPlaneRealtimeHandoffOutputCommand> RealtimeHandoffOutputCalls { get; } = [];
    public List<ControlPlaneRealtimeStopCommand> RealtimeStopCalls { get; } = [];
    public List<ControlPlaneRegisterAgentThreadCommand> AgentThreadRegistrationCalls { get; } = [];
    public List<ControlPlaneCreateJobCommand> AgentJobCreateCalls { get; } = [];
    public List<ControlPlaneDispatchJobCommand> AgentJobDispatchCalls { get; } = [];
    public List<ControlPlaneReportJobItemCommand> AgentJobItemReportCalls { get; } = [];
    public List<ControlPlaneReadJobQuery> AgentJobReadCalls { get; } = [];
    public List<ControlPlaneWindowsSandboxSetupStartCommand> WindowsSandboxSetupCalls { get; } = [];
    public List<ControlPlaneMcpServerOauthLoginStartCommand> McpServerOauthLoginCalls { get; } = [];
    public List<ControlPlaneSkillCatalogQuery> SkillsListCalls { get; } = [];
    public List<ControlPlaneMcpServerStatusQuery> McpServerStatusCalls { get; } = [];
    public List<ControlPlaneProviderPackageReloadCommand> ProviderPackageReloadCalls { get; } = [];
    public List<ControlPlaneModelCatalogQuery> ModelListCalls { get; } = [];
    public List<GetCapabilityCatalog> ProviderCatalogCalls { get; } = [];
    public List<ResolveEngineBinding> EngineBindingCalls { get; } = [];
    public List<GetExecutionTrace> ExecutionTraceCalls { get; } = [];
    public List<ListAttemptSummaries> AttemptSummaryListCalls { get; } = [];
    public List<ControlPlaneConfigBatchWriteCommand> ConfigBatchWriteCalls { get; } = [];
    public List<ControlPlaneReviewStartCommand> ReviewStartCalls { get; } = [];
    public List<FilterMemory> MemoryFilterRequests { get; } = [];
    public List<AddMemory> MemoryAddRequests { get; } = [];
    public List<ListMemoryReviews> MemoryReviewListRequests { get; } = [];
    public List<RunMemoryConsolidation> MemoryConsolidationRequests { get; } = [];
    public List<ForgetMemory> MemoryForgetRequests { get; } = [];
    public List<DeleteMemory> MemoryDeleteRequests { get; } = [];
    public List<ApproveMemoryReview> MemoryApproveReviewRequests { get; } = [];
    public List<DemoteMemoryReview> MemoryDemoteReviewRequests { get; } = [];
    public List<MergeMemoryReview> MemoryMergeReviewRequests { get; } = [];
    public List<RestoreMemoryReview> MemoryRestoreReviewRequests { get; } = [];
    public List<RecordMemoryFeedback> MemoryFeedbackRequests { get; } = [];
    public int CollaborationModeListCallCount { get; private set; }
    public int SearchFuzzyFilesCallCount { get; private set; }
    public int StartFuzzyFileSearchSessionCallCount { get; private set; }
    public int UpdateFuzzyFileSearchSessionCallCount { get; private set; }
    public int StopFuzzyFileSearchSessionCallCount { get; private set; }
    public List<(string Method, StructuredValue? Parameters)> RpcCalls { get; } = [];
    public List<ControlPlaneCommandExecutionStartCommand> CommandExecutionStartCalls { get; } = [];
    public List<ControlPlaneCommandExecutionWriteCommand> CommandExecutionWriteCalls { get; } = [];
    public List<ControlPlaneCommandExecutionTerminateCommand> CommandExecutionTerminateCalls { get; } = [];
    public List<ControlPlaneCommandExecutionResizeCommand> CommandExecutionResizeCalls { get; } = [];

    public Func<ControlPlaneInitializeRuntimeCommand, CancellationToken, Task>? InitializeAsyncHandler { get; set; }
    public Func<string, IReadOnlyList<ConversationMessage>, CancellationToken, Task<AgentSendResult>>? SendAsyncHandler { get; set; }
    public Func<string, CancellationToken, Task<AgentSendResult>>? RunUserShellCommandAsyncHandler { get; set; }
    public Func<string, FollowUpMode, CancellationToken, string?, Task<AgentSendResult>>? SendFollowUpAsyncHandler { get; set; }
    public Func<CancellationToken, Task>? InterruptAsyncHandler { get; set; }
    public Func<ControlPlaneApprovalResolution, CancellationToken, Task<bool>>? RespondToApprovalAsyncHandler { get; set; }
    public Func<ControlPlanePermissionGrant, CancellationToken, Task<bool>>? RespondToPermissionRequestAsyncHandler { get; set; }
    public Func<ControlPlaneUserInputSubmission, CancellationToken, Task<bool>>? RespondToUserInputAsyncHandler { get; set; }
    public Func<int, bool, bool, CancellationToken, Task<IReadOnlyList<AgentThreadInfo>>>? ListThreadsAsyncHandler { get; set; }
    public Func<ControlPlaneThreadListQuery, CancellationToken, Task<AgentThreadListResult>>? ListThreadsRequestAsyncHandler { get; set; }
    public Func<CancellationToken, Task<AgentThreadInfo?>>? StartNewThreadAsyncHandler { get; set; }
    public Func<ControlPlaneStartThreadCommand, CancellationToken, Task<AgentThreadInfo?>>? StartNewThreadRequestAsyncHandler { get; set; }
    public Func<string, CancellationToken, Task<AgentThreadInfo?>>? ForkThreadAsyncHandler { get; set; }
    public Func<string, CancellationToken, Task<bool>>? ArchiveThreadAsyncHandler { get; set; }
    public Func<string, CancellationToken, Task<bool>>? DeleteThreadAsyncHandler { get; set; }
    public Func<CancellationToken, Task<ControlPlaneClearThreadsResult>>? ClearThreadsAsyncHandler { get; set; }
    public Func<string, string, CancellationToken, Task<bool>>? RenameThreadAsyncHandler { get; set; }
    public Func<string, CancellationToken, Task<AgentThreadResumeResult?>>? ResumeThreadAsyncHandler { get; set; }
    public Func<ControlPlaneResumeThreadCommand, CancellationToken, Task<AgentThreadResumeResult?>>? ResumeThreadRequestAsyncHandler { get; set; }
    public Func<ControlPlaneLoadedThreadListQuery, CancellationToken, Task<ControlPlaneLoadedThreadListResult>>? ListLoadedThreadsAsyncHandler { get; set; }
    public Func<ControlPlaneReadThreadQuery, CancellationToken, Task<ControlPlaneThreadOperationResult>>? ReadThreadAsyncHandler { get; set; }
    public Func<string, int, CancellationToken, Task<ControlPlaneThreadCommandAcceptedResult>>? CompactThreadAsyncHandler { get; set; }
    public Func<string, CancellationToken, Task<ControlPlaneThreadCommandAcceptedResult>>? CleanBackgroundTerminalsAsyncHandler { get; set; }
    public Func<string, CancellationToken, Task<ControlPlaneThreadUnsubscribeResult>>? UnsubscribeThreadAsyncHandler { get; set; }
    public Func<string, CancellationToken, Task<ControlPlaneThreadElicitationResult>>? IncrementThreadElicitationAsyncHandler { get; set; }
    public Func<string, CancellationToken, Task<ControlPlaneThreadElicitationResult>>? DecrementThreadElicitationAsyncHandler { get; set; }
    public Func<string, CancellationToken, Task<ControlPlaneThreadOperationResult>>? UnarchiveThreadAsyncHandler { get; set; }
    public Func<ControlPlaneUpdateThreadMetadataCommand, CancellationToken, Task<ControlPlaneThreadOperationResult>>? UpdateThreadMetadataAsyncHandler { get; set; }
    public Func<ControlPlaneRollbackThreadCommand, CancellationToken, Task<ControlPlaneThreadOperationResult>>? RollbackThreadAsyncHandler { get; set; }
    public Func<ControlPlaneRealtimeStartCommand, CancellationToken, Task<ControlPlaneRealtimeCommandAcceptedResult>>? StartRealtimeAsyncHandler { get; set; }
    public Func<ControlPlaneRealtimeAppendTextCommand, CancellationToken, Task<ControlPlaneRealtimeCommandAcceptedResult>>? AppendRealtimeTextAsyncHandler { get; set; }
    public Func<ControlPlaneRealtimeAppendAudioCommand, CancellationToken, Task<ControlPlaneRealtimeCommandAcceptedResult>>? AppendRealtimeAudioAsyncHandler { get; set; }
    public Func<ControlPlaneRealtimeHandoffOutputCommand, CancellationToken, Task<ControlPlaneRealtimeCommandAcceptedResult>>? HandoffRealtimeOutputAsyncHandler { get; set; }
    public Func<ControlPlaneRealtimeStopCommand, CancellationToken, Task<ControlPlaneRealtimeCommandAcceptedResult>>? StopRealtimeAsyncHandler { get; set; }
    public Func<ControlPlaneRegisterAgentThreadCommand, CancellationToken, Task<ControlPlaneAgentThreadRegistrationResult>>? RegisterAgentThreadAsyncHandler { get; set; }
    public Func<ControlPlaneCreateJobCommand, CancellationToken, Task<ControlPlaneJobOperationResult>>? CreateAgentJobAsyncHandler { get; set; }
    public Func<ControlPlaneDispatchJobCommand, CancellationToken, Task<ControlPlaneJobOperationResult>>? DispatchAgentJobAsyncHandler { get; set; }
    public Func<ControlPlaneReportJobItemCommand, CancellationToken, Task<ControlPlaneJobOperationResult>>? ReportAgentJobItemAsyncHandler { get; set; }
    public Func<ControlPlaneReadJobQuery, CancellationToken, Task<ControlPlaneJobOperationResult>>? ReadAgentJobAsyncHandler { get; set; }
    public Func<ControlPlaneListJobsQuery, CancellationToken, Task<ControlPlaneJobListResult>>? ListAgentJobsAsyncHandler { get; set; }
    public Func<ControlPlaneWindowsSandboxSetupStartCommand, CancellationToken, Task<ControlPlaneWindowsSandboxSetupStartResult>>? StartWindowsSandboxSetupAsyncHandler { get; set; }
    public Func<ControlPlaneMcpServerOauthLoginStartCommand, CancellationToken, Task<ControlPlaneMcpServerOauthLoginStartResult>>? StartMcpServerOauthLoginAsyncHandler { get; set; }
    public Func<ControlPlaneSkillCatalogQuery, CancellationToken, Task<ControlPlaneSkillCatalogResult>>? ListSkillsAsyncHandler { get; set; }
    public Func<ControlPlaneMcpServerStatusQuery, CancellationToken, Task<ControlPlaneMcpServerCatalogResult>>? ListMcpServerStatusAsyncHandler { get; set; }
    public Func<ControlPlaneProviderPackageReloadCommand, CancellationToken, Task<ControlPlaneProviderPackageReloadResult>>? ReloadProviderPackagesAsyncHandler { get; set; }
    public Func<ControlPlaneModelCatalogQuery, CancellationToken, Task<ControlPlaneModelCatalogResult>>? ListModelsAsyncHandler { get; set; }
    public Func<ControlPlaneConfigBatchWriteCommand, CancellationToken, Task<ControlPlaneConfigWriteResult>>? WriteConfigBatchAsyncHandler { get; set; }
    public Func<ControlPlaneReviewStartCommand, CancellationToken, Task<ControlPlaneReviewStartResult>>? StartReviewAsyncHandler { get; set; }
    public Func<FilterMemory, CancellationToken, Task<MemoryQueryResult>>? FilterMemoryAsyncHandler { get; set; }
    public Func<ListMemorySpaces, CancellationToken, Task<IReadOnlyList<MemorySpace>>>? ListMemorySpacesAsyncHandler { get; set; }
    public Func<AddMemory, CancellationToken, Task<MemoryMutationResult>>? AddMemoryAsyncHandler { get; set; }
    public Func<ListMemoryReviews, CancellationToken, Task<MemoryReviewQueryResult>>? ListMemoryReviewsAsyncHandler { get; set; }
    public Func<RunMemoryConsolidation, CancellationToken, Task<MemoryConsolidationRunResult>>? RunMemoryConsolidationAsyncHandler { get; set; }
    public Func<ForgetMemory, CancellationToken, Task<MemoryMutationResult>>? ForgetMemoryAsyncHandler { get; set; }
    public Func<DeleteMemory, CancellationToken, Task<MemoryMutationResult>>? DeleteMemoryAsyncHandler { get; set; }
    public Func<ApproveMemoryReview, CancellationToken, Task<MemoryMutationResult>>? ApproveMemoryReviewAsyncHandler { get; set; }
    public Func<DemoteMemoryReview, CancellationToken, Task<MemoryMutationResult>>? DemoteMemoryReviewAsyncHandler { get; set; }
    public Func<MergeMemoryReview, CancellationToken, Task<MemoryMutationResult>>? MergeMemoryReviewAsyncHandler { get; set; }
    public Func<RestoreMemoryReview, CancellationToken, Task<MemoryMutationResult>>? RestoreMemoryReviewAsyncHandler { get; set; }
    public Func<RecordMemoryFeedback, CancellationToken, Task<MemoryMutationResult>>? RecordMemoryFeedbackAsyncHandler { get; set; }
    public Func<CancellationToken, Task<ControlPlaneCollaborationModeCatalogResult>>? ListCollaborationModesAsyncHandler { get; set; }
    public Func<ControlPlaneCommandExecutionStartCommand, CancellationToken, Task<ControlPlaneCommandExecutionResult>>? StartCommandExecutionAsyncHandler { get; set; }
    public Func<ControlPlaneCommandExecutionWriteCommand, CancellationToken, Task<ControlPlaneCommandExecutionCommandAcceptedResult>>? WriteCommandExecutionAsyncHandler { get; set; }
    public Func<ControlPlaneCommandExecutionTerminateCommand, CancellationToken, Task<ControlPlaneCommandExecutionCommandAcceptedResult>>? TerminateCommandExecutionAsyncHandler { get; set; }
    public Func<ControlPlaneCommandExecutionResizeCommand, CancellationToken, Task<ControlPlaneCommandExecutionCommandAcceptedResult>>? ResizeCommandExecutionAsyncHandler { get; set; }
    public Func<ControlPlaneFuzzyFileSearchQuery, CancellationToken, Task<ControlPlaneFuzzyFileSearchResult>>? SearchFuzzyFilesAsyncHandler { get; set; }
    public Func<ControlPlaneStartFuzzyFileSearchSessionCommand, CancellationToken, Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult>>? StartFuzzyFileSearchSessionAsyncHandler { get; set; }
    public Func<ControlPlaneUpdateFuzzyFileSearchSessionCommand, CancellationToken, Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult>>? UpdateFuzzyFileSearchSessionAsyncHandler { get; set; }
    public Func<ControlPlaneStopFuzzyFileSearchSessionCommand, CancellationToken, Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult>>? StopFuzzyFileSearchSessionAsyncHandler { get; set; }
    public Func<string, StructuredValue?, CancellationToken, Task<StructuredValue>>? InvokeDiagnosticRpcAsyncHandler { get; set; }

    public string RuntimeName => "CliConsumerFakeRuntime";
    public string? ActiveThreadId { get; set; }
    public bool HasActiveTurn { get; set; }
    public event EventHandler<ControlPlaneConversationStreamEventArgs>? StreamEventReceived;

    public void Emit(ControlPlaneConversationStreamEvent streamEvent)
        => StreamEventReceived?.Invoke(this, new ControlPlaneConversationStreamEventArgs(streamEvent));

    internal static ControlPlaneConversationStreamEvent CreateStreamEvent(
        ControlPlaneConversationStreamEventKind kind,
        string? threadId = null,
        string? turnId = null,
        string? text = null,
        string? status = null,
        string? callId = null,
        string? toolName = null,
        string? message = null,
        bool? willRetry = null,
        string? source = null,
        IReadOnlyList<string>? availableDecisions = null,
        ControlPlaneConversationStreamPayloadKind? payloadKind = null,
        StructuredValue? payload = null,
        string? operationName = null)
        => new()
        {
            Kind = kind,
            Timestamp = DateTimeOffset.Now,
            ThreadId = string.IsNullOrWhiteSpace(threadId) ? null : new ThreadId(threadId),
            TurnId = string.IsNullOrWhiteSpace(turnId) ? null : new TurnId(turnId),
            CallId = string.IsNullOrWhiteSpace(callId) ? null : new CallId(callId),
            ToolName = toolName,
            Text = text,
            Status = status,
            Message = message,
            WillRetry = willRetry,
            Source = source,
            AvailableDecisions = availableDecisions,
            PayloadKind = payloadKind,
            Payload = payload,
            OperationName = operationName,
        };

    public async Task InitializeAsync(
        ControlPlaneInitializeRuntimeCommand options,
        Func<ToolInvocationRequest, CancellationToken, Task<ToolInvocationResult>>? dynamicToolCallHandler,
        CancellationToken cancellationToken)
        {
            InitializedOptions = options;
            if (InitializeAsyncHandler is not null)
            {
                await InitializeAsyncHandler(options, cancellationToken).ConfigureAwait(false);
            }
        }

    public Task<ExecutionRunResult> ExecuteAsync(ExecutionPlan plan, ExecutionRuntimeContext context, CancellationToken cancellationToken)
        => Task.FromException<ExecutionRunResult>(new NotSupportedException("CliConsumerFakeRuntime 不执行 RuntimeStep。"));

    public Task<RuntimeStepResult> ExecuteStepAsync(RuntimeStep step, ExecutionRuntimeContext context, CancellationToken cancellationToken)
        => Task.FromException<RuntimeStepResult>(new NotSupportedException("CliConsumerFakeRuntime 不执行 RuntimeStep。"));

    public Task<ControlPlaneTurnSubmissionResult> SendAsync(ControlPlaneSubmitTurnCommand command, CancellationToken cancellationToken)
    {
        var userInputs = ToAgentUserInputs(command.Inputs);
        var history = ToConversationHistory(command.History);
        var preview = BuildUserInputPreview(userInputs);
        TurnSubmissionCommands.Add(command with
        {
            Inputs = [.. command.Inputs],
            History = [.. command.History],
        });
        SendCalls.Add((preview, history));
        return MapTurnSubmissionResultAsync(
            SendAsyncHandler is null
                ? Task.FromResult(AgentSendResult.Fail("SendAsyncHandler 未配置。"))
                : SendAsyncHandler(preview, history, cancellationToken));
    }

    public Task<ControlPlaneTurnSubmissionResult> RunUserShellCommandAsync(string command, CancellationToken cancellationToken)
    {
        UserShellCommandCalls.Add(command);
        return MapTurnSubmissionResultAsync(
            RunUserShellCommandAsyncHandler is null
                ? Task.FromResult(AgentSendResult.Fail("RunUserShellCommandAsyncHandler 未配置。"))
                : RunUserShellCommandAsyncHandler(command, cancellationToken));
    }

    public Task<ControlPlaneTurnSubmissionResult> SendFollowUpAsync(ControlPlaneSubmitFollowUpCommand command, CancellationToken cancellationToken)
    {
        var mode = ToRuntimeFollowUpMode(command.Mode);
        var userInputs = ToAgentUserInputs(command.Inputs);
        var preview = BuildUserInputPreview(userInputs);
        FollowUpSubmissionCommands.Add(command with
        {
            Inputs = [.. command.Inputs],
        });
        FollowUpCalls.Add((preview, mode, command.CorrelationId));
        return MapTurnSubmissionResultAsync(
            SendFollowUpAsyncHandler is null
                ? Task.FromResult(AgentSendResult.Fail("SendFollowUpAsyncHandler 未配置。"))
                : SendFollowUpAsyncHandler(preview, mode, cancellationToken, command.CorrelationId));
    }

    public Task<ControlPlanePendingFollowUpMutationResult> MutatePendingFollowUpAsync(ControlPlaneMutatePendingFollowUpCommand command, CancellationToken cancellationToken)
    {
        PendingFollowUpMutationCommands.Add(command);
        return Task.FromResult(new ControlPlanePendingFollowUpMutationResult
        {
            Accepted = true,
            Message = "已处理待发送项。",
            CorrelationId = command.CorrelationId,
            Kind = command.Kind,
        });
    }

    public Task InterruptTurnAsync(CancellationToken cancellationToken)
    {
        InterruptCallCount++;
        return InterruptAsyncHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    public Task<bool> RespondToApprovalAsync(ControlPlaneApprovalResolution command, CancellationToken cancellationToken)
    {
        ApprovalResponses.Add(command with
        {
            CallId = new CallId(command.CallId.Value),
            CommandPrefix = [.. command.CommandPrefix],
        });
        return RespondToApprovalAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(true);
    }

    public Task<bool> RespondToPermissionRequestAsync(ControlPlanePermissionGrant command, CancellationToken cancellationToken)
    {
        PermissionResponses.Add(command with
        {
            CallId = new CallId(command.CallId.Value),
            Permissions = command.Permissions.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
        });
        return RespondToPermissionRequestAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(true);
    }

    public Task<bool> RespondToUserInputAsync(ControlPlaneUserInputSubmission command, CancellationToken cancellationToken)
    {
        UserInputResponses.Add(command with
        {
            CallId = new CallId(command.CallId.Value),
            Answers = command.Answers.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.Ordinal),
        });
        return RespondToUserInputAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(true);
    }

    public Task<TianShu.Contracts.Projections.ApprovalQueueProjection?> GetApprovalQueueProjectionAsync(ListPendingApprovals query, CancellationToken cancellationToken)
        => Task.FromResult<TianShu.Contracts.Projections.ApprovalQueueProjection?>(null);

    public Task<IReadOnlyList<UserInputRequest>> ListUserInputRequestsAsync(ListUserInputRequests query, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<UserInputRequest>>([]);

    public async Task<ControlPlaneThreadListResult> ListThreadsAsync(ControlPlaneThreadListQuery query, CancellationToken cancellationToken)
    {
        ThreadListRequestCalls.Add(query);
        ThreadListCalls.Add((query.Limit, query.Archived, !string.IsNullOrWhiteSpace(query.WorkingDirectory)));
        if (ListThreadsRequestAsyncHandler is not null)
        {
            return ToControlPlaneThreadListResult(await ListThreadsRequestAsyncHandler(query, cancellationToken).ConfigureAwait(false));
        }

        return new ControlPlaneThreadListResult
        {
            Threads = ListThreadsAsyncHandler is null
                ? Array.Empty<ControlPlaneThreadSummary>()
                : (await ListThreadsAsyncHandler(query.Limit, query.Archived, !string.IsNullOrWhiteSpace(query.WorkingDirectory), cancellationToken).ConfigureAwait(false))
                    .Select(ToControlPlaneThreadSummary)
                    .Where(static item => item is not null)
                    .Cast<ControlPlaneThreadSummary>()
                    .ToArray(),
        };
    }

    public Task<TianShu.Contracts.Projections.ThreadProjection?> GetThreadProjectionAsync(GetThreadProjection query, CancellationToken cancellationToken)
        => Task.FromResult<TianShu.Contracts.Projections.ThreadProjection?>(null);

    public Task<PlanProjection?> GetPlanProjectionAsync(GetPlanProjection request, CancellationToken cancellationToken)
        => Task.FromResult<PlanProjection?>(null);

    public Task<TeamProjection?> GetTeamProjectionAsync(GetTeamProjection request, CancellationToken cancellationToken)
        => Task.FromResult<TeamProjection?>(null);

    public Task<AgentRosterProjection?> GetAgentRosterProjectionAsync(GetAgentRoster request, CancellationToken cancellationToken)
        => Task.FromResult<AgentRosterProjection?>(
            request.WorkflowId is null
                ? new AgentRosterProjection(
                [
                    new AgentRosterEntry(
                        new AgentId("agent-fake"),
                        new ParticipantRef(new ParticipantId("agent-fake"), ParticipantKind.Agent, "Fake Agent"),
                        "member",
                        0,
                        false),
                ])
                : null);

    public Task<ControlPlaneAgentRosterResult> ListAgentsAsync(ControlPlaneAgentListQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneAgentRosterResult
        {
            Agents =
            [
                new ControlPlaneAgentDescriptor
                {
                    ThreadId = new ThreadId("agent-fake"),
                    AgentNickname = "Fake Agent",
                    AgentRole = "member",
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

    public Task<WorkflowBoardProjection?> GetWorkflowBoardProjectionAsync(GetWorkflowBoard request, CancellationToken cancellationToken)
        => Task.FromResult<WorkflowBoardProjection?>(null);

    public Task<TaskBoardProjection?> GetTaskBoardProjectionAsync(GetTaskBoard request, CancellationToken cancellationToken)
        => Task.FromResult<TaskBoardProjection?>(null);

    public Task<Artifact> PublishArtifactAsync(PublishArtifact request, CancellationToken cancellationToken)
        => Task.FromResult(request.Artifact);

    public Task<Artifact> PromoteArtifactAsync(PromoteArtifact request, CancellationToken cancellationToken)
        => Task.FromResult(new Artifact(
            request.ArtifactId,
            new CollaborationSpaceRef(new CollaborationSpaceId("space-fake"), "space-fake", "Fake Space"),
            "fake-artifact.md",
            ArtifactKind.Document,
            state: ArtifactLifecycleState.Promoted));

    public Task<Artifact> AttachArtifactToTaskAsync(AttachArtifactToTask request, CancellationToken cancellationToken)
        => Task.FromResult(new Artifact(
            request.ArtifactId,
            new CollaborationSpaceRef(new CollaborationSpaceId("space-fake"), "space-fake", "Fake Space"),
            "fake-artifact.md",
            ArtifactKind.Document,
            state: ArtifactLifecycleState.Published));

    public Task<ArtifactProjection?> GetArtifactProjectionAsync(GetArtifactDetail request, CancellationToken cancellationToken)
        => Task.FromResult<ArtifactProjection?>(null);

    public Task<ArtifactCollectionProjection?> GetArtifactCollectionProjectionAsync(ListArtifacts request, CancellationToken cancellationToken)
        => Task.FromResult<ArtifactCollectionProjection?>(null);

    public async Task<IReadOnlyList<ControlPlaneThreadSummary>> ListThreadsAsync(int limit, bool archived, bool matchCurrentCwd, CancellationToken cancellationToken)
    {
        ThreadListCalls.Add((limit, archived, matchCurrentCwd));
        if (ListThreadsAsyncHandler is not null)
        {
            var legacy = await ListThreadsAsyncHandler(limit, archived, matchCurrentCwd, cancellationToken).ConfigureAwait(false);
            return legacy.Select(ToControlPlaneThreadSummary).Where(static item => item is not null).Cast<ControlPlaneThreadSummary>().ToArray();
        }

        var result = await ListThreadsAsync(
                new ControlPlaneThreadListQuery
                {
                    Limit = limit,
                    Archived = archived,
                    WorkingDirectory = matchCurrentCwd ? "D:/Work/TianShu" : null,
                    SortKey = "updated_at",
                },
                cancellationToken)
            .ConfigureAwait(false);
        return result.Threads;
    }

    public async Task<ControlPlaneThreadSummary?> StartNewThreadAsync(CancellationToken cancellationToken)
    {
        StartThreadCalls.Add("start");
        var result = StartNewThreadAsyncHandler is null
            ? null
            : await StartNewThreadAsyncHandler(cancellationToken).ConfigureAwait(false);
        return ToControlPlaneThreadSummary(result);
    }

    public async Task<ControlPlaneThreadSummary?> StartNewThreadAsync(ControlPlaneStartThreadCommand command, CancellationToken cancellationToken)
    {
        StartThreadRequestCalls.Add(command);
        if (StartNewThreadRequestAsyncHandler is not null)
        {
            return ToControlPlaneThreadSummary(await StartNewThreadRequestAsyncHandler(command, cancellationToken).ConfigureAwait(false));
        }

        return await StartNewThreadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneThreadSummary?> ForkThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        ForkThreadCalls.Add(threadId);
        var result = ForkThreadAsyncHandler is null
            ? null
            : await ForkThreadAsyncHandler(threadId, cancellationToken).ConfigureAwait(false);
        return ToControlPlaneThreadSummary(result);
    }

    public async Task<ControlPlaneThreadSummary?> ForkThreadAsync(ControlPlaneForkThreadCommand command, CancellationToken cancellationToken)
        => await ForkThreadAsync(command.ThreadId.Value, cancellationToken).ConfigureAwait(false);

    public Task<bool> ArchiveThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        ArchiveThreadCalls.Add(threadId);
        return ArchiveThreadAsyncHandler?.Invoke(threadId, cancellationToken) ?? Task.FromResult(false);
    }

    public Task<bool> DeleteThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        DeleteThreadCalls.Add(threadId);
        return DeleteThreadAsyncHandler?.Invoke(threadId, cancellationToken) ?? Task.FromResult(false);
    }

    public Task<ControlPlaneClearThreadsResult> ClearThreadsAsync(CancellationToken cancellationToken)
    {
        ClearThreadsCallCount++;
        return ClearThreadsAsyncHandler?.Invoke(cancellationToken) ?? Task.FromResult(new ControlPlaneClearThreadsResult());
    }

    public Task<bool> RenameThreadAsync(string threadId, string name, CancellationToken cancellationToken)
    {
        RenameThreadCalls.Add((threadId, name));
        return RenameThreadAsyncHandler?.Invoke(threadId, name, cancellationToken) ?? Task.FromResult(false);
    }

    public async Task<ControlPlaneThreadSnapshot?> ResumeThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        ResumeThreadCalls.Add(threadId);
        var result = ResumeThreadAsyncHandler is null
            ? null
            : await ResumeThreadAsyncHandler(threadId, cancellationToken).ConfigureAwait(false);
        return ToControlPlaneThreadSnapshot(result);
    }

    public async Task<ControlPlaneThreadSnapshot?> ResumeThreadAsync(ControlPlaneResumeThreadCommand command, CancellationToken cancellationToken)
    {
        ResumeThreadRequestCalls.Add(command);
        if (ResumeThreadRequestAsyncHandler is not null)
        {
            return ToControlPlaneThreadSnapshot(await ResumeThreadRequestAsyncHandler(command, cancellationToken).ConfigureAwait(false));
        }

        return await ResumeThreadAsync(command.ThreadId.Value, cancellationToken).ConfigureAwait(false);
    }

    private static ControlPlaneThreadListResult ToControlPlaneThreadListResult(AgentThreadListResult result)
        => new()
        {
            Threads = result.Data.Select(ToControlPlaneThreadSummary).Where(static item => item is not null).Cast<ControlPlaneThreadSummary>().ToArray(),
            NextCursor = result.NextCursor,
        };

    private static ControlPlaneThreadSummary? ToControlPlaneThreadSummary(AgentThreadInfo? thread)
        => thread is null
            ? null
            : new ControlPlaneThreadSummary
            {
                ThreadId = new ThreadId(thread.ThreadId),
                Preview = thread.Preview,
                Name = thread.Name,
                WorkingDirectory = thread.Cwd,
                Path = thread.Path,
                ModelProvider = thread.ModelProvider,
                Source = thread.Source?.GetThreadSourceKind(),
                ParentThreadId = thread.Source?.SubAgentSource is { ParentThreadId: { Length: > 0 } parentThreadId }
                    ? new ThreadId(parentThreadId)
                    : null,
                LineageDepth = thread.Source?.SubAgentSource?.Depth,
                CliVersion = thread.CliVersion,
                AgentNickname = thread.AgentNickname,
                AgentRole = thread.AgentRole,
                CreatedAt = thread.CreatedAt,
                UpdatedAt = thread.UpdatedAt,
                IsEphemeral = thread.IsEphemeral,
                Status = thread.Status?.Type,
                ActiveFlags = thread.Status?.ActiveFlags ?? Array.Empty<string>(),
                GitSha = thread.GitInfo?.Sha,
                GitBranch = thread.GitInfo?.Branch,
                GitOriginUrl = thread.GitInfo?.OriginUrl,
                SessionConfiguration = ToControlPlaneThreadSessionConfiguration(thread.SessionConfiguration),
            };

    private static ControlPlaneThreadSnapshot? ToControlPlaneThreadSnapshot(AgentThreadResumeResult? result)
        => result is null
            ? null
            : new ControlPlaneThreadSnapshot
            {
                Thread = new ControlPlaneThreadSummary
                {
                    ThreadId = new ThreadId(result.ThreadId),
                    Preview = result.Preview,
                    Name = result.Name,
                    WorkingDirectory = result.Cwd,
                    Path = result.Path,
                    ModelProvider = result.ModelProvider,
                    Source = result.Source?.GetThreadSourceKind(),
                    ParentThreadId = result.Source?.SubAgentSource is { ParentThreadId: { Length: > 0 } parentThreadId }
                        ? new ThreadId(parentThreadId)
                        : null,
                    LineageDepth = result.Source?.SubAgentSource?.Depth,
                    CliVersion = result.CliVersion,
                    AgentNickname = result.AgentNickname,
                    AgentRole = result.AgentRole,
                    CreatedAt = result.CreatedAt,
                    UpdatedAt = result.UpdatedAt,
                    IsEphemeral = result.IsEphemeral,
                    Status = result.Status?.Type,
                    ActiveFlags = result.Status?.ActiveFlags ?? Array.Empty<string>(),
                    GitSha = result.GitInfo?.Sha,
                    GitBranch = result.GitInfo?.Branch,
                    GitOriginUrl = result.GitInfo?.OriginUrl,
                    SessionConfiguration = ToControlPlaneThreadSessionConfiguration(result.SessionConfiguration),
                },
                Messages = result.Messages.Select(ToControlPlaneConversationMessage).ToArray(),
                Turns = result.Turns.Select(ToControlPlaneThreadTurn).ToArray(),
                SeedHistory = result.SeedHistory.Select(ToControlPlaneSeedHistoryItem).ToArray(),
                PendingInputState = result.PendingInputState,
                PendingInteractiveRequests = result.PendingInteractiveRequests.ToArray(),
            };

    private static ControlPlaneThreadSessionConfiguration? ToControlPlaneThreadSessionConfiguration(AgentThreadSessionConfiguration? configuration)
        => configuration is null
            ? null
            : new ControlPlaneThreadSessionConfiguration
            {
                Model = configuration.Model,
                ModelProvider = configuration.ModelProvider,
                ModelProviderId = configuration.ModelProviderId,
                ServiceTier = configuration.ServiceTier?.ToString(),
                ApprovalPolicy = configuration.ApprovalPolicy?.ToString(),
                SandboxPolicy = configuration.SandboxPolicy,
                SandboxPolicyPayload = configuration.SandboxPolicyPayload is null ? null : StructuredValue.FromPlainObject(configuration.SandboxPolicyPayload.ToPlainObject()),
                ReasoningEffort = configuration.ReasoningEffort,
                HistoryLogId = configuration.HistoryLogId,
                HistoryEntryCount = configuration.HistoryEntryCount,
                RolloutPath = configuration.RolloutPath,
                ReasoningSummary = configuration.ReasoningSummary,
                Verbosity = configuration.Verbosity,
                Personality = configuration.Personality,
                AllowLoginShell = configuration.AllowLoginShell,
                ShellEnvironmentPolicy = configuration.ShellEnvironmentPolicy is null ? null : StructuredValue.FromPlainObject(configuration.ShellEnvironmentPolicy.ToPlainObject()),
                ProviderBaseUrl = configuration.ProviderBaseUrl,
                ProviderApiKeyEnvironmentVariable = configuration.ProviderApiKeyEnvironmentVariable,
                ProviderWireApi = configuration.ProviderWireApi,
                ProviderRequestMaxRetries = configuration.ProviderRequestMaxRetries,
                ProviderStreamMaxRetries = configuration.ProviderStreamMaxRetries,
                ProviderStreamIdleTimeoutMs = configuration.ProviderStreamIdleTimeoutMs,
                ProviderWebsocketConnectTimeoutMs = configuration.ProviderWebsocketConnectTimeoutMs,
                ProviderSupportsWebsockets = configuration.ProviderSupportsWebsockets,
                WebSearchMode = configuration.WebSearchMode,
                ServiceName = configuration.ServiceName,
                BaseInstructions = configuration.BaseInstructions,
                DeveloperInstructions = configuration.DeveloperInstructions,
                UserInstructions = configuration.UserInstructions,
                DynamicTools = configuration.DynamicTools?.Select(static item => StructuredValue.FromPlainObject(item.ToPlainObject())).ToArray(),
                CollaborationMode = configuration.CollaborationMode is null ? null : StructuredValue.FromPlainObject(configuration.CollaborationMode.ToPlainObject()),
                PersistExtendedHistory = configuration.PersistExtendedHistory,
                ForkedFromThreadId = string.IsNullOrWhiteSpace(configuration.ForkedFromId) ? null : new ThreadId(configuration.ForkedFromId),
                WorkingDirectory = configuration.Cwd,
                SessionSource = configuration.SessionSource,
                WindowsSandboxLevel = configuration.WindowsSandboxLevel,
                DefaultModeRequestUserInputEnabled = configuration.DefaultModeRequestUserInputEnabled,
            };

    private static ControlPlaneConversationMessage ToControlPlaneConversationMessage(ConversationMessage message)
        => new()
        {
            Role = message.Role switch
            {
                ConversationRole.System => ControlPlaneConversationRole.System,
                ConversationRole.Assistant => ControlPlaneConversationRole.Assistant,
                _ => ControlPlaneConversationRole.User,
            },
            Content = message.Content,
            ContentItems = message.ContentItems.Select(ToControlPlaneInputItem).ToArray(),
            Timestamp = message.Timestamp,
            IsStreaming = message.IsStreaming,
        };

    private static ControlPlaneInputItem ToControlPlaneInputItem(AgentUserInput input)
        => input switch
        {
            TextUserInput text => new ControlPlaneTextInput(
                text.Text,
                text.TextElements.Select(static element => new ControlPlaneTextElement(
                    new ControlPlaneByteRange(element.ByteRange.Start, element.ByteRange.End),
                    element.Placeholder)).ToArray()),
            ImageUserInput image => new ControlPlaneImageInput(image.Url),
            LocalImageUserInput localImage => new ControlPlaneLocalImageInput(localImage.Path),
            SkillUserInput skill => new ControlPlaneSkillInput(skill.Name, skill.Path),
            MentionUserInput mention => new ControlPlaneMentionInput(mention.Name, mention.Path),
            _ => new ControlPlaneTextInput(input.Type),
        };

    private static IReadOnlyList<AgentUserInput> ToAgentUserInputs(IReadOnlyList<ControlPlaneInputItem> inputs)
        => inputs.Select(ToAgentUserInput).ToArray();

    private static AgentUserInput ToAgentUserInput(ControlPlaneInputItem input)
        => input switch
        {
            ControlPlaneTextInput text => new TextUserInput
            {
                Type = text.Type,
                Text = text.Text,
                TextElements = (text.TextElements ?? Array.Empty<ControlPlaneTextElement>())
                    .Select(static element => new AgentTextElement
                    {
                        ByteRange = new AgentByteRange
                        {
                            Start = element.ByteRange.Start,
                            End = element.ByteRange.End,
                        },
                        Placeholder = element.Placeholder,
                    })
                    .ToArray(),
            },
            ControlPlaneImageInput image => new ImageUserInput
            {
                Type = image.Type,
                Url = image.Url,
            },
            ControlPlaneLocalImageInput localImage => new LocalImageUserInput
            {
                Type = localImage.Type,
                Path = localImage.Path,
            },
            ControlPlaneSkillInput skill => new SkillUserInput
            {
                Type = skill.Type,
                Name = skill.Name,
                Path = skill.Path,
            },
            ControlPlaneMentionInput mention => new MentionUserInput
            {
                Type = mention.Type,
                Name = mention.Name,
                Path = mention.Path,
            },
            _ => throw new NotSupportedException($"不支持的控制平面输入类型：{input.GetType().Name}"),
        };

    private static IReadOnlyList<ConversationMessage> ToConversationHistory(IReadOnlyList<ControlPlaneConversationMessage> history)
        => history.Select(static message => new ConversationMessage
        {
            Role = message.Role switch
            {
                ControlPlaneConversationRole.System => ConversationRole.System,
                ControlPlaneConversationRole.Assistant => ConversationRole.Assistant,
                _ => ConversationRole.User,
            },
            Content = message.Content,
            ContentItems = ToAgentUserInputs(message.ContentItems),
            Timestamp = message.Timestamp,
            IsStreaming = message.IsStreaming,
        }).ToArray();

    private static FollowUpMode ToRuntimeFollowUpMode(ControlPlaneFollowUpMode mode)
        => mode switch
        {
            ControlPlaneFollowUpMode.Queue => FollowUpMode.Queue,
            ControlPlaneFollowUpMode.Steer => FollowUpMode.Steer,
            ControlPlaneFollowUpMode.Interrupt => FollowUpMode.Interrupt,
            _ => FollowUpMode.Queue,
        };

    private static async Task<ControlPlaneTurnSubmissionResult> MapTurnSubmissionResultAsync(Task<AgentSendResult> task)
    {
        var result = await task.ConfigureAwait(false);
        return new ControlPlaneTurnSubmissionResult
        {
            Accepted = result.Success,
            Message = result.Message,
            TurnId = string.IsNullOrWhiteSpace(result.TurnId) ? null : new TurnId(result.TurnId),
            TurnStatus = result.TurnStatus,
            CorrelationId = result.CorrelationId,
            RequestedMode = result.RequestedMode switch
            {
                FollowUpMode.Queue => ControlPlaneFollowUpMode.Queue,
                FollowUpMode.Steer => ControlPlaneFollowUpMode.Steer,
                FollowUpMode.Interrupt => ControlPlaneFollowUpMode.Interrupt,
                _ => null,
            },
            EffectiveMode = result.EffectiveMode switch
            {
                FollowUpMode.Queue => ControlPlaneFollowUpMode.Queue,
                FollowUpMode.Steer => ControlPlaneFollowUpMode.Steer,
                FollowUpMode.Interrupt => ControlPlaneFollowUpMode.Interrupt,
                _ => null,
            },
        };
    }

    private static ControlPlaneThreadTurn ToControlPlaneThreadTurn(AgentThreadTurn turn)
        => new()
        {
            Id = turn.Id,
            Status = turn.Status,
            Error = turn.Error is null
                ? null
                : new ControlPlaneThreadTurnError
                {
                    Message = turn.Error.Message,
                    AdditionalDetails = turn.Error.AdditionalDetails,
                },
            Items = turn.Items.Select(ToControlPlaneThreadTurnItem).ToArray(),
        };

    private static ControlPlaneThreadTurnItem ToControlPlaneThreadTurnItem(AgentThreadTurnItem item)
        => new()
        {
            Id = item.Id,
            Type = item.Type,
            Text = item.Text,
            Phase = item.Phase,
            Data = item is GenericThreadTurnItem generic && generic.RawData is not null
                ? StructuredValue.FromPlainObject(generic.RawData.ToPlainObject())
                : null,
        };

    private static ControlPlaneSeedHistoryItem ToControlPlaneSeedHistoryItem(AgentThreadSeedHistoryItem item)
        => new()
        {
            Role = item.Role,
            Content = item.Content,
            Inputs = item.Inputs.Select(ToControlPlaneInputItem).ToArray(),
        };

    internal static ControlPlanePendingInputState? ToControlPlanePendingInputState(PendingInputStatePayload? payload)
        => payload is null
            ? null
            : new ControlPlanePendingInputState(
                payload.Entries.Select(ToControlPlanePendingInputStateEntry).ToArray(),
                payload.InterruptRequestPending,
                payload.SubmitPendingSteersAfterInterrupt,
                payload.QueuedUserMessages?.Select(ToControlPlanePendingInputStateEntry).ToArray(),
                payload.PendingSteers?.Select(ToControlPlanePendingInputStateEntry).ToArray());

    private static ControlPlanePendingInputStateEntry ToControlPlanePendingInputStateEntry(PendingInputStateEntryPayload entry)
        => new(
            entry.CorrelationId,
            entry.RequestedMode,
            entry.EffectiveMode,
            entry.LifecycleState,
            entry.ExpectedTurnId,
            entry.TurnId,
            entry.CompareKey is null
                ? null
                : StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["message"] = entry.CompareKey.Message,
                    ["imageCount"] = entry.CompareKey.ImageCount,
                }),
            entry.PendingBucket,
            entry.Inputs?.Select(ToControlPlaneInputItem).ToArray());

    internal static ControlPlanePendingInteractiveRequest ToControlPlanePendingInteractiveRequest(PendingInteractiveRequestReplay request)
        => new()
        {
            RequestId = request.RequestId,
            RequestIdRaw = request.RequestIdRaw,
            RequestKind = request.RequestKind,
            RequestMethod = request.RequestMethod,
            CallId = request.CallId,
            ThreadId = request.ThreadId,
            TurnId = request.TurnId,
            ToolName = request.ToolName,
            ServerName = request.ServerName,
            Text = request.Text,
            Status = request.Status,
            Phase = request.Phase,
            RequiresApproval = request.RequiresApproval,
            ApprovalKind = request.ApprovalKind,
            AvailableDecisions = request.AvailableDecisions,
            AvailableDecisionOptions = request.AvailableDecisionOptions?.Select(
                static option => new ControlPlaneApprovalDecisionOption(
                    option.Type,
                    option.ExecPolicyAmendment is null ? null : new ControlPlaneExecPolicyAmendment(option.ExecPolicyAmendment.CommandPrefix),
                    option.NetworkPolicyAmendment is null ? null : new ControlPlaneNetworkPolicyAmendment(option.NetworkPolicyAmendment.Host, option.NetworkPolicyAmendment.Action)))
                .ToArray(),
        };

    public Task<ControlPlaneLoadedThreadListResult> ListLoadedThreadsAsync(ControlPlaneLoadedThreadListQuery query, CancellationToken cancellationToken)
    {
        ThreadLoadedListCalls.Add(query);
        return ListLoadedThreadsAsyncHandler?.Invoke(query, cancellationToken) ?? Task.FromResult(new ControlPlaneLoadedThreadListResult());
    }

    public Task<ControlPlaneThreadOperationResult> ReadThreadAsync(ControlPlaneReadThreadQuery query, CancellationToken cancellationToken)
    {
        ThreadReadCalls.Add(query);
        return ReadThreadAsyncHandler?.Invoke(query, cancellationToken) ?? Task.FromResult(new ControlPlaneThreadOperationResult());
    }

    public Task<ControlPlaneThreadCommandAcceptedResult> CompactThreadAsync(ControlPlaneCompactThreadCommand command, CancellationToken cancellationToken)
    {
        CompactThreadCalls.Add((command.ThreadId.Value, command.KeepRecentTurns));
        return CompactThreadAsyncHandler?.Invoke(command.ThreadId.Value, command.KeepRecentTurns, cancellationToken) ?? Task.FromResult(new ControlPlaneThreadCommandAcceptedResult());
    }

    public Task<ControlPlaneThreadCommandAcceptedResult> CleanBackgroundTerminalsAsync(ControlPlaneCleanBackgroundTerminalsCommand command, CancellationToken cancellationToken)
    {
        CleanBackgroundTerminalsCalls.Add(command.ThreadId.Value);
        return CleanBackgroundTerminalsAsyncHandler?.Invoke(command.ThreadId.Value, cancellationToken) ?? Task.FromResult(new ControlPlaneThreadCommandAcceptedResult());
    }

    public Task<ControlPlaneThreadUnsubscribeResult> UnsubscribeThreadAsync(ControlPlaneUnsubscribeThreadCommand command, CancellationToken cancellationToken)
    {
        UnsubscribeThreadCalls.Add(command.ThreadId.Value);
        return UnsubscribeThreadAsyncHandler?.Invoke(command.ThreadId.Value, cancellationToken) ?? Task.FromResult(new ControlPlaneThreadUnsubscribeResult());
    }

    public Task<ControlPlaneThreadElicitationResult> IncrementThreadElicitationAsync(ControlPlaneIncrementThreadElicitationCommand command, CancellationToken cancellationToken)
    {
        return IncrementThreadElicitationAsyncHandler?.Invoke(command.ThreadId.Value, cancellationToken) ?? Task.FromResult(new ControlPlaneThreadElicitationResult());
    }

    public Task<ControlPlaneThreadElicitationResult> DecrementThreadElicitationAsync(ControlPlaneDecrementThreadElicitationCommand command, CancellationToken cancellationToken)
    {
        return DecrementThreadElicitationAsyncHandler?.Invoke(command.ThreadId.Value, cancellationToken) ?? Task.FromResult(new ControlPlaneThreadElicitationResult());
    }

    public Task<ControlPlaneThreadOperationResult> UnarchiveThreadAsync(ControlPlaneUnarchiveThreadCommand command, CancellationToken cancellationToken)
    {
        UnarchiveThreadCalls.Add(command.ThreadId.Value);
        return UnarchiveThreadAsyncHandler?.Invoke(command.ThreadId.Value, cancellationToken) ?? Task.FromResult(new ControlPlaneThreadOperationResult());
    }

    public Task<ControlPlaneThreadOperationResult> UpdateThreadMetadataAsync(ControlPlaneUpdateThreadMetadataCommand command, CancellationToken cancellationToken)
    {
        ThreadMetadataUpdateCalls.Add(command);
        return UpdateThreadMetadataAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(new ControlPlaneThreadOperationResult());
    }

    public Task<ControlPlaneThreadOperationResult> RollbackThreadAsync(ControlPlaneRollbackThreadCommand command, CancellationToken cancellationToken)
    {
        ThreadRollbackCalls.Add(command);
        return RollbackThreadAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(new ControlPlaneThreadOperationResult());
    }

    public Task<ControlPlaneRealtimeCommandAcceptedResult> StartRealtimeAsync(ControlPlaneRealtimeStartCommand command, CancellationToken cancellationToken)
    {
        RealtimeStartCalls.Add(command);
        return StartRealtimeAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());
    }

    public Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeTextAsync(ControlPlaneRealtimeAppendTextCommand command, CancellationToken cancellationToken)
    {
        RealtimeAppendTextCalls.Add(command);
        return AppendRealtimeTextAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());
    }

    public Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeAudioAsync(ControlPlaneRealtimeAppendAudioCommand command, CancellationToken cancellationToken)
    {
        RealtimeAppendAudioCalls.Add(command);
        return AppendRealtimeAudioAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());
    }

    public Task<ControlPlaneRealtimeCommandAcceptedResult> HandoffRealtimeOutputAsync(ControlPlaneRealtimeHandoffOutputCommand command, CancellationToken cancellationToken)
    {
        RealtimeHandoffOutputCalls.Add(command);
        return HandoffRealtimeOutputAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());
    }

    public Task<ControlPlaneRealtimeCommandAcceptedResult> StopRealtimeAsync(ControlPlaneRealtimeStopCommand command, CancellationToken cancellationToken)
    {
        RealtimeStopCalls.Add(command);
        return StopRealtimeAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());
    }

    public Task<ControlPlaneConfigSnapshotResult> ReadConfigAsync(ControlPlaneConfigReadQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneConfigSnapshotResult());

    public Task<ControlPlaneConfigRequirementsResult> ReadConfigRequirementsAsync(ControlPlaneConfigRequirementsQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneConfigRequirementsResult());

    public Task<ControlPlaneModelCatalogResult> ListModelsAsync(ControlPlaneModelCatalogQuery request, CancellationToken cancellationToken)
    {
        ModelListCalls.Add(request);
        return ListModelsAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneModelCatalogResult());
    }

    public Task<CapabilityCatalogSnapshot> GetCapabilityCatalogAsync(
        GetCapabilityCatalog request,
        CancellationToken cancellationToken)
    {
        ProviderCatalogCalls.Add(request);
        return Task.FromResult(new CapabilityCatalogSnapshot());
    }

    public Task<ResolvedEngineBinding> ResolveEngineBindingAsync(
        ResolveEngineBinding request,
        CancellationToken cancellationToken)
    {
        EngineBindingCalls.Add(request);
        return Task.FromResult(new ResolvedEngineBinding(null));
    }

    public Task<ControlPlaneConfigWriteResult> WriteConfigValueAsync(ControlPlaneConfigValueWriteCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneConfigWriteResult());

    public Task<ControlPlaneConfigWriteResult> WriteConfigBatchAsync(ControlPlaneConfigBatchWriteCommand request, CancellationToken cancellationToken)
    {
        ConfigBatchWriteCalls.Add(request);
        return WriteConfigBatchAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneConfigWriteResult());
    }

    public Task<CollaborationSpace> CreateSpaceAsync(CreateCollaborationSpace command, CancellationToken cancellationToken)
        => Task.FromResult(new CollaborationSpace(command.SpaceId, command.Key, command.DisplayName, command.Profile, command.Defaults, command.PolicyRef));

    public Task<CollaborationSpace> ConfigureSpaceAsync(ConfigureCollaborationSpace command, CancellationToken cancellationToken)
        => Task.FromResult(new CollaborationSpace(
            command.SpaceId,
            command.SpaceId.Value,
            command.DisplayName ?? command.SpaceId.Value,
            command.Profile ?? new CollaborationSpaceProfile("fake collaboration"),
            command.Defaults ?? CollaborationDefaultSet.Empty,
            command.PolicyRef));

    public Task<bool> ArchiveSpaceAsync(ArchiveCollaborationSpace command, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<CollaborationSpaceOverviewProjection?> GetSpaceOverviewAsync(GetCollaborationSpaceOverview query, CancellationToken cancellationToken)
        => Task.FromResult<CollaborationSpaceOverviewProjection?>(null);

    public Task<TianShu.Contracts.Projections.CollaborationSpaceProjection?> GetSpaceProjectionAsync(GetCollaborationSpaceProjection query, CancellationToken cancellationToken)
        => Task.FromResult<TianShu.Contracts.Projections.CollaborationSpaceProjection?>(
            new TianShu.Contracts.Projections.CollaborationSpaceProjection(
                new CollaborationSpaceRef(query.SpaceId, query.SpaceId.Value, query.SpaceId.Value),
                ActiveSessionCount: 0,
                ActiveThreadCount: 0,
                IsArchived: false));

    public Task<IReadOnlyList<CollaborationSpaceOverviewProjection>> ListSpacesAsync(ListCollaborationSpaces query, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CollaborationSpaceOverviewProjection>>([]);

    public Task<bool> BindParticipantToSessionAsync(BindParticipantToSession command, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<bool> BindParticipantToWorkflowAsync(BindParticipantToWorkflow command, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<bool> UpdateParticipantRoleAsync(UpdateParticipantRole command, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<ParticipantProjection?> GetParticipantProjectionAsync(GetParticipantProjection query, CancellationToken cancellationToken)
        => Task.FromResult<ParticipantProjection?>(new ParticipantProjection(query.ParticipantId, ParticipantKind.Agent, query.ParticipantId.Value, "member"));

    public Task<TianShu.Contracts.Projections.ParticipantProjection?> GetParticipantViewProjectionAsync(GetParticipantViewProjection query, CancellationToken cancellationToken)
        => Task.FromResult<TianShu.Contracts.Projections.ParticipantProjection?>(
            new TianShu.Contracts.Projections.ParticipantProjection(
                new ParticipantRef(query.ParticipantId, ParticipantKind.Agent, query.ParticipantId.Value),
                ScopeKind: "participant",
                ScopeKey: query.ParticipantId.Value,
                Role: "member",
                IsActive: true));

    public Task<IReadOnlyList<ParticipantProjection>> ListParticipantsInScopeAsync(ListParticipantsInScope query, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ParticipantProjection>>([]);

    public Task<SessionOverviewProjection?> GetSessionOverviewAsync(GetSessionOverview query, CancellationToken cancellationToken)
        => Task.FromResult<SessionOverviewProjection?>(null);

    public Task<IReadOnlyList<SessionOverviewProjection>> ListSessionsAsync(ListSessions query, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SessionOverviewProjection>>([]);

    public Task<ExecutionTrace?> GetExecutionTraceAsync(GetExecutionTrace query, CancellationToken cancellationToken)
    {
        ExecutionTraceCalls.Add(query);
        return Task.FromResult<ExecutionTrace?>(new ExecutionTrace(query.TraceId, new ExecutionId($"exec-{query.TraceId.Value}")));
    }

    public Task<IReadOnlyList<AttemptSummary>> ListAttemptSummariesAsync(ListAttemptSummaries query, CancellationToken cancellationToken)
    {
        AttemptSummaryListCalls.Add(query);
        return Task.FromResult<IReadOnlyList<AttemptSummary>>([]);
    }

    public Task<Account?> GetAccountProfileAsync(GetAccountProfile query, CancellationToken cancellationToken)
        => Task.FromResult<Account?>(null);

    public Task<IReadOnlyList<DeviceBinding>> ListBoundDevicesAsync(ListBoundDevices query, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<DeviceBinding>>([]);

    public Task<IReadOnlyList<MemorySpace>> ListMemorySpacesAsync(ListMemorySpaces query, CancellationToken cancellationToken)
        => ListMemorySpacesAsyncHandler?.Invoke(query, cancellationToken) ?? Task.FromResult<IReadOnlyList<MemorySpace>>([]);

    public Task<MemoryOverlay> ResolveMemoryOverlayAsync(ResolveMemoryOverlay query, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryOverlay());

    public Task<IReadOnlyList<MemoryProviderDescriptor>> ListMemoryProvidersAsync(ListMemoryProviders query, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<MemoryProviderDescriptor>>([]);

    public Task<MemoryQueryResult> FilterMemoryAsync(FilterMemory query, CancellationToken cancellationToken)
    {
        MemoryFilterRequests.Add(query);
        return FilterMemoryAsyncHandler?.Invoke(query, cancellationToken) ?? Task.FromResult(new MemoryQueryResult());
    }

    public Task<MemoryReviewQueryResult> ListMemoryReviewsAsync(ListMemoryReviews query, CancellationToken cancellationToken)
    {
        MemoryReviewListRequests.Add(query);
        return ListMemoryReviewsAsyncHandler?.Invoke(query, cancellationToken) ?? Task.FromResult(new MemoryReviewQueryResult());
    }

    public Task<MemoryMutationResult> AddMemoryAsync(AddMemory command, CancellationToken cancellationToken)
    {
        MemoryAddRequests.Add(command);
        return AddMemoryAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(new MemoryMutationResult(true));
    }

    public Task<IReadOnlyList<MemoryCandidate>> ExtractMemoryAsync(ExtractMemory command, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<MemoryCandidate>>([]);

    public Task<MemoryMutationResult> ImportMemoryAsync(ImportMemory command, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(true));

    public Task<MemoryQueryResult> ExportMemoryAsync(ExportMemory command, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryQueryResult());

    public Task<MemoryMutationResult> BindMemoryProviderAsync(BindMemoryProvider command, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(true));

    public Task<MemoryConsolidationRunResult> RunMemoryConsolidationAsync(RunMemoryConsolidation command, CancellationToken cancellationToken)
    {
        MemoryConsolidationRequests.Add(command);
        return RunMemoryConsolidationAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(new MemoryConsolidationRunResult(0, 0));
    }

    public Task<MemoryMutationResult> ForgetMemoryAsync(ForgetMemory command, CancellationToken cancellationToken)
    {
        MemoryForgetRequests.Add(command);
        return ForgetMemoryAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Forgotten));
    }

    public Task<MemoryMutationResult> DeleteMemoryAsync(DeleteMemory command, CancellationToken cancellationToken)
    {
        MemoryDeleteRequests.Add(command);
        return DeleteMemoryAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Deleted));
    }

    public Task<MemoryMutationResult> ApproveMemoryReviewAsync(ApproveMemoryReview command, CancellationToken cancellationToken)
    {
        MemoryApproveReviewRequests.Add(command);
        return ApproveMemoryReviewAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Active));
    }

    public Task<MemoryMutationResult> DemoteMemoryReviewAsync(DemoteMemoryReview command, CancellationToken cancellationToken)
    {
        MemoryDemoteReviewRequests.Add(command);
        return DemoteMemoryReviewAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Archived));
    }

    public Task<MemoryMutationResult> MergeMemoryReviewAsync(MergeMemoryReview command, CancellationToken cancellationToken)
    {
        MemoryMergeReviewRequests.Add(command);
        return MergeMemoryReviewAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true, command.TargetRecordId, MemoryLifecycleStatus.Active, Effect: MemoryMutationEffect.Superseded));
    }

    public Task<MemoryMutationResult> RestoreMemoryReviewAsync(RestoreMemoryReview command, CancellationToken cancellationToken)
    {
        MemoryRestoreReviewRequests.Add(command);
        return RestoreMemoryReviewAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.PendingReview));
    }

    public Task<MemoryMutationResult> SupersedeMemoryAsync(SupersedeMemory command, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(true));

    public Task<MemoryMutationResult> RecordMemoryFeedbackAsync(RecordMemoryFeedback command, CancellationToken cancellationToken)
    {
        MemoryFeedbackRequests.Add(command);
        return RecordMemoryFeedbackAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Active));
    }

    public Task<MemoryMutationResult> RecordMemoryCitationAsync(RecordMemoryCitation command, CancellationToken cancellationToken)
        => Task.FromResult(new MemoryMutationResult(true));

    public Task<ControlPlaneSkillCatalogResult> ListSkillsAsync(ControlPlaneSkillCatalogQuery request, CancellationToken cancellationToken)
    {
        SkillsListCalls.Add(request);
        return ListSkillsAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneSkillCatalogResult());
    }

    public Task<ControlPlaneSkillConfigWriteResult> WriteSkillsConfigAsync(ControlPlaneSkillConfigWriteCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneSkillConfigWriteResult());

    public Task<ControlPlaneRemoteSkillCatalogResult> ListRemoteSkillsAsync(ControlPlaneRemoteSkillCatalogQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneRemoteSkillCatalogResult());

    public Task<ControlPlaneRemoteSkillExportResult> ExportRemoteSkillAsync(ControlPlaneRemoteSkillExportCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneRemoteSkillExportResult());

    public Task<ControlPlanePluginCatalogResult> ListPluginsAsync(ControlPlanePluginCatalogQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlanePluginCatalogResult());

    public Task<ControlPlanePluginReadResult> ReadPluginAsync(ControlPlanePluginReadQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlanePluginReadResult());

    public Task<ControlPlanePluginInstallResult> InstallPluginAsync(ControlPlanePluginInstallCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlanePluginInstallResult());

    public Task<ControlPlanePluginUninstallResult> UninstallPluginAsync(ControlPlanePluginUninstallCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlanePluginUninstallResult());

    public Task<ControlPlaneAppCatalogResult> ListAppsAsync(ControlPlaneAppCatalogQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneAppCatalogResult());

    public Task<ControlPlaneReviewStartResult> StartReviewAsync(ControlPlaneReviewStartCommand request, CancellationToken cancellationToken)
    {
        ReviewStartCalls.Add(request);
        return StartReviewAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneReviewStartResult());
    }

    public Task<ControlPlaneExperimentalFeatureCatalogResult> ListExperimentalFeaturesAsync(ControlPlaneExperimentalFeatureQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneExperimentalFeatureCatalogResult());

    public Task<ControlPlaneCollaborationModeCatalogResult> ListCollaborationModesAsync(CancellationToken cancellationToken)
    {
        CollaborationModeListCallCount++;
        return ListCollaborationModesAsyncHandler?.Invoke(cancellationToken) ?? Task.FromResult(new ControlPlaneCollaborationModeCatalogResult());
    }

    public Task<ControlPlaneMcpServerCatalogResult> ListMcpServerStatusAsync(ControlPlaneMcpServerStatusQuery request, CancellationToken cancellationToken)
    {
        McpServerStatusCalls.Add(request);
        return ListMcpServerStatusAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneMcpServerCatalogResult());
    }

    public Task<ControlPlaneMcpServerReloadResult> ReloadMcpServersAsync(ControlPlaneMcpServerReloadCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneMcpServerReloadResult());

    public Task<ControlPlaneProviderPackageReloadResult> ReloadProviderPackagesAsync(ControlPlaneProviderPackageReloadCommand request, CancellationToken cancellationToken)
    {
        ProviderPackageReloadCalls.Add(request);
        return ReloadProviderPackagesAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneProviderPackageReloadResult
               {
                   SupportedProtocolAdapterIds = ["openai_responses"],
                   SupportedWireApis = ["openai_responses"],
               });
    }

    public Task<ControlPlaneMcpServerOauthLoginStartResult> StartMcpServerOauthLoginAsync(ControlPlaneMcpServerOauthLoginStartCommand request, CancellationToken cancellationToken)
    {
        McpServerOauthLoginCalls.Add(request);
        return StartMcpServerOauthLoginAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneMcpServerOauthLoginStartResult());
    }

    public Task<ControlPlaneConversationArtifact?> GetConversationSummaryAsync(ControlPlaneConversationArtifactQuery request, CancellationToken cancellationToken)
        => Task.FromResult<ControlPlaneConversationArtifact?>(null);

    public Task<ControlPlaneGitDiffArtifact> GetGitDiffToRemoteAsync(ControlPlaneGitDiffArtifactQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneGitDiffArtifact());
    public Task<ControlPlaneCommandExecutionResult> StartCommandExecutionAsync(ControlPlaneCommandExecutionStartCommand request, CancellationToken cancellationToken)
    {
        CommandExecutionStartCalls.Add(request);
        return StartCommandExecutionAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneCommandExecutionResult());
    }

    public Task<ControlPlaneCommandExecutionCommandAcceptedResult> WriteCommandExecutionAsync(ControlPlaneCommandExecutionWriteCommand request, CancellationToken cancellationToken)
    {
        CommandExecutionWriteCalls.Add(request);
        return WriteCommandExecutionAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneCommandExecutionCommandAcceptedResult());
    }

    public Task<ControlPlaneCommandExecutionCommandAcceptedResult> TerminateCommandExecutionAsync(ControlPlaneCommandExecutionTerminateCommand request, CancellationToken cancellationToken)
    {
        CommandExecutionTerminateCalls.Add(request);
        return TerminateCommandExecutionAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneCommandExecutionCommandAcceptedResult());
    }

    public Task<ControlPlaneCommandExecutionCommandAcceptedResult> ResizeCommandExecutionAsync(ControlPlaneCommandExecutionResizeCommand request, CancellationToken cancellationToken)
    {
        CommandExecutionResizeCalls.Add(request);
        return ResizeCommandExecutionAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneCommandExecutionCommandAcceptedResult());
    }

    public Task<ControlPlaneCodeModeResult> ExecuteCodeModeAsync(ControlPlaneCodeModeExecCommand request, CancellationToken cancellationToken)
        => InvokeCodeModeViaHandler(
            "exec",
            new Dictionary<string, object?>
            {
                ["threadId"] = request.ThreadId.Value,
                ["input"] = request.Input,
                ["yieldTimeMs"] = request.YieldTimeMs,
                ["maxOutputTokens"] = request.MaxOutputTokens,
            },
            cancellationToken);

    public Task<ControlPlaneCodeModeResult> WaitCodeModeAsync(ControlPlaneCodeModeWaitCommand request, CancellationToken cancellationToken)
        => InvokeCodeModeViaHandler(
            "exec_wait",
            new Dictionary<string, object?>
            {
                ["threadId"] = request.ThreadId.Value,
                ["cellId"] = request.CellId,
                ["yieldTimeMs"] = request.YieldTimeMs,
                ["maxTokens"] = request.MaxTokens,
                ["terminate"] = request.Terminate,
            },
            cancellationToken);

    public Task<ControlPlaneAgentThreadRegistrationResult> RegisterAgentThreadAsync(ControlPlaneRegisterAgentThreadCommand request, CancellationToken cancellationToken)
    {
        AgentThreadRegistrationCalls.Add(request);
        return RegisterAgentThreadAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneAgentThreadRegistrationResult());
    }

    public Task<Workflow> CreateWorkflowAsync(CreateWorkflow request, CancellationToken cancellationToken)
        => Task.FromResult(
            new Workflow(
                request.WorkflowId,
                new CollaborationSpaceRef(request.CollaborationSpaceId, request.CollaborationSpaceId.Value, request.CollaborationSpaceId.Value),
                request.DisplayName,
                WorkflowState.Draft,
                request.OwnerParticipant,
                request.ThreadId));

    public Task<PlanProjection> PublishPlanAsync(PublishPlan request, CancellationToken cancellationToken)
        => Task.FromResult(new PlanProjection(request.WorkflowId, request.Plan));

    public Task<TianShu.Contracts.Workflows.Task> CreateTaskAsync(CreateTask request, CancellationToken cancellationToken)
        => Task.FromResult(request.Task);

    public Task<TianShu.Contracts.Workflows.Task?> UpdateTaskStateAsync(UpdateTaskState request, CancellationToken cancellationToken)
        => Task.FromResult<TianShu.Contracts.Workflows.Task?>(null);

    public Task<ControlPlaneJobOperationResult> CreateAgentJobAsync(ControlPlaneCreateJobCommand request, CancellationToken cancellationToken)
    {
        AgentJobCreateCalls.Add(request);
        return CreateAgentJobAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneJobOperationResult());
    }

    public Task<ControlPlaneJobOperationResult> DispatchAgentJobAsync(ControlPlaneDispatchJobCommand request, CancellationToken cancellationToken)
    {
        AgentJobDispatchCalls.Add(request);
        return DispatchAgentJobAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneJobOperationResult());
    }

    public Task<ControlPlaneJobOperationResult> ReportAgentJobItemAsync(ControlPlaneReportJobItemCommand request, CancellationToken cancellationToken)
    {
        AgentJobItemReportCalls.Add(request);
        return ReportAgentJobItemAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneJobOperationResult());
    }

    public Task<ControlPlaneJobOperationResult> ReadAgentJobAsync(ControlPlaneReadJobQuery request, CancellationToken cancellationToken)
    {
        AgentJobReadCalls.Add(request);
        return ReadAgentJobAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneJobOperationResult());
    }

    public Task<ControlPlaneJobListResult> ListAgentJobsAsync(ControlPlaneListJobsQuery request, CancellationToken cancellationToken)
        => ListAgentJobsAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneJobListResult());

    public Task<ControlPlaneFuzzyFileSearchResult> SearchFuzzyFilesAsync(ControlPlaneFuzzyFileSearchQuery request, CancellationToken cancellationToken)
    {
        SearchFuzzyFilesCallCount++;
        return SearchFuzzyFilesAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneFuzzyFileSearchResult());
    }

    public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StartFuzzyFileSearchSessionAsync(ControlPlaneStartFuzzyFileSearchSessionCommand request, CancellationToken cancellationToken)
    {
        StartFuzzyFileSearchSessionCallCount++;
        return StartFuzzyFileSearchSessionAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneFuzzyFileSearchCommandAcceptedResult());
    }

    public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> UpdateFuzzyFileSearchSessionAsync(ControlPlaneUpdateFuzzyFileSearchSessionCommand request, CancellationToken cancellationToken)
    {
        UpdateFuzzyFileSearchSessionCallCount++;
        return UpdateFuzzyFileSearchSessionAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneFuzzyFileSearchCommandAcceptedResult());
    }

    public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StopFuzzyFileSearchSessionAsync(ControlPlaneStopFuzzyFileSearchSessionCommand request, CancellationToken cancellationToken)
    {
        StopFuzzyFileSearchSessionCallCount++;
        return StopFuzzyFileSearchSessionAsyncHandler?.Invoke(request, cancellationToken) ?? Task.FromResult(new ControlPlaneFuzzyFileSearchCommandAcceptedResult());
    }

    public Task<ControlPlaneFeedbackUploadResult> UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneFeedbackUploadResult
        {
            ThreadId = request.ThreadId ?? "feedback-thread",
        });

    public Task<ControlPlaneWindowsSandboxSetupStartResult> StartWindowsSandboxSetupAsync(ControlPlaneWindowsSandboxSetupStartCommand request, CancellationToken cancellationToken)
    {
        WindowsSandboxSetupCalls.Add(request);
        return StartWindowsSandboxSetupAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneWindowsSandboxSetupStartResult());
    }

    private Task<StructuredValue> InvokeViaHandler(string method, object? parameters, CancellationToken cancellationToken)
    {
        var structuredParameters = parameters is null ? null : StructuredValue.FromPlainObject(parameters);
        RpcCalls.Add((method, structuredParameters));
        return InvokeDiagnosticRpcAsyncHandler?.Invoke(method, structuredParameters, cancellationToken)
               ?? Task.FromResult(StructuredValue.FromJsonElement(ReflectionTestHelper.ParseJsonElement("{}")));
    }

    private async Task<ControlPlaneCodeModeResult> InvokeCodeModeViaHandler(string method, object? parameters, CancellationToken cancellationToken)
    {
        var result = await InvokeViaHandler(method, parameters, cancellationToken).ConfigureAwait(false);
        return new ControlPlaneCodeModeResult
        {
            Success = result.TryGetProperty("success", out var successElement) && successElement is not null
                ? successElement.GetBoolean()
                : true,
            Status = result.TryGetProperty("status", out var statusElement) && statusElement is not null
                ? statusElement.GetString() ?? string.Empty
                : string.Empty,
            ThreadId = result.TryGetProperty("threadId", out var threadElement) && threadElement is not null
                ? threadElement.GetString() is { Length: > 0 } threadId
                    ? new ThreadId(threadId)
                    : null
                : null,
            TurnId = result.TryGetProperty("turnId", out var turnElement) && turnElement is not null
                ? turnElement.GetString() is { Length: > 0 } turnId
                    ? new TurnId(turnId)
                    : null
                : null,
            CellId = result.TryGetProperty("cellId", out var cellElement) && cellElement is not null
                ? cellElement.GetString()
                : null,
            Output = result.TryGetProperty("output", out var outputElement) && outputElement is not null
                ? outputElement.GetString() ?? string.Empty
                : string.Empty,
            ContentItems = result.TryGetProperty("contentItems", out var contentItemsElement) && contentItemsElement is not null
                ? contentItemsElement.Items
                    .Select(static item => new ControlPlaneCodeModeOutputItem
                    {
                        Type = item.TryGetProperty("type", out var typeElement) && typeElement is not null
                            ? typeElement.GetString() ?? string.Empty
                            : string.Empty,
                        Text = item.TryGetProperty("text", out var textElement) && textElement is not null
                            ? textElement.GetString()
                            : null,
                        ImageUrl = item.TryGetProperty("imageUrl", out var imageUrlElement) && imageUrlElement is not null
                            ? imageUrlElement.GetString()
                            : null,
                        Detail = item.TryGetProperty("detail", out var detailElement) && detailElement is not null
                            ? detailElement.GetString()
                            : null,
                    })
                    .ToArray()
                : Array.Empty<ControlPlaneCodeModeOutputItem>(),
        };
    }

    public Task<StructuredValue> InvokeDiagnosticRpcAsync(string method, StructuredValue? parameters, CancellationToken cancellationToken)
        => InvokeViaHandler(method, parameters, cancellationToken);

    public Task<ControlPlaneDebugClearMemoriesResult> ClearDebugMemoriesAsync(CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneDebugClearMemoriesResult
        {
            StateDbPath = "D:/TianShu/state/state.db",
            MemoryRootPath = "D:/TianShu/memories",
            RemovedMemoryRoot = true,
        });

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    private static string BuildUserInputPreview(IReadOnlyList<AgentUserInput> userInputs)
        => string.Join(
            Environment.NewLine,
            userInputs
                .Select(static input => input switch
                {
                    TextUserInput text => text.Text,
                    MentionUserInput mention => mention.Name,
                    SkillUserInput skill => skill.Name,
                    LocalImageUserInput image => image.Path,
                    ImageUserInput image => image.Url,
                    _ => null,
                })
                .Where(static text => !string.IsNullOrWhiteSpace(text)));
}
