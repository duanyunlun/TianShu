using System.Reflection;
using Moq;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Identity;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Workflows;
using ControlPlaneParticipantProjection = TianShu.Contracts.Participants.ParticipantProjection;
using ControlPlaneCreateJobCommand = TianShu.Contracts.Workflows.ControlPlaneCreateJobCommand;
using ControlPlaneJobDetails = TianShu.Contracts.Workflows.ControlPlaneJobDetails;
using ControlPlaneJobItemDetails = TianShu.Contracts.Workflows.ControlPlaneJobItemDetails;
using ControlPlaneJobOperationResult = TianShu.Contracts.Workflows.ControlPlaneJobOperationResult;
using ControlPlaneGovernanceSubscription = TianShu.Contracts.Projections.ControlPlaneGovernanceSubscription;
using ControlPlaneReportJobItemCommand = TianShu.Contracts.Workflows.ControlPlaneReportJobItemCommand;
using ControlPlaneReviewStartCommand = TianShu.Contracts.Workflows.ControlPlaneReviewStartCommand;
using ControlPlaneReviewStartResult = TianShu.Contracts.Workflows.ControlPlaneReviewStartResult;
using GetTaskBoard = TianShu.Contracts.Workflows.GetTaskBoard;
using GetWorkflowBoard = TianShu.Contracts.Workflows.GetWorkflowBoard;
using TianShu.Execution.Runtime.Models;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.ControlPlane;
using TianShu.ControlPlane.Abstractions;
using TianShu.ControlPlane.Abstractions.Catalog;
using TianShu.ControlPlane.Abstractions.Identity;
using TianShu.ControlPlane.Abstractions.Memory;
using TianShu.ControlPlane.Abstractions.Subscriptions;
using Task = System.Threading.Tasks.Task;

namespace TianShu.Execution.Integration.Tests;

public sealed class RuntimeControlPlaneAdapterTests
{
    [Fact]
    public void AgentThreadModels_PendingFields_WhenUsedForNorthboundThreadResults_UseControlPlaneTypes()
    {
        var resumePendingInputProperty = typeof(AgentThreadResumeResult).GetProperty(nameof(AgentThreadResumeResult.PendingInputState));
        var resumePendingRequestsProperty = typeof(AgentThreadResumeResult).GetProperty(nameof(AgentThreadResumeResult.PendingInteractiveRequests));
        var detailPendingInputProperty = typeof(AgentThreadDetails).GetProperty(nameof(AgentThreadDetails.PendingInputState));
        var detailPendingRequestsProperty = typeof(AgentThreadDetails).GetProperty(nameof(AgentThreadDetails.PendingInteractiveRequests));

        Assert.NotNull(resumePendingInputProperty);
        Assert.NotNull(resumePendingRequestsProperty);
        Assert.NotNull(detailPendingInputProperty);
        Assert.NotNull(detailPendingRequestsProperty);

        Assert.Equal(typeof(ControlPlanePendingInputState), resumePendingInputProperty!.PropertyType);
        Assert.Equal(typeof(IReadOnlyList<ControlPlanePendingInteractiveRequest>), resumePendingRequestsProperty!.PropertyType);
        Assert.Equal(typeof(ControlPlanePendingInputState), detailPendingInputProperty!.PropertyType);
        Assert.Equal(typeof(IReadOnlyList<ControlPlanePendingInteractiveRequest>), detailPendingRequestsProperty!.PropertyType);
    }

    [Fact]
    public void ControlPlaneAbstractions_ShouldNotExposeLegacyErrorTypes()
    {
        var assembly = typeof(ITianShuControlPlane).Assembly;

        Assert.Null(assembly.GetType("TianShu.ControlPlane.Abstractions.ControlPlaneErrorCode", throwOnError: false, ignoreCase: false));
        Assert.Null(assembly.GetType("TianShu.ControlPlane.Abstractions.ControlPlaneError", throwOnError: false, ignoreCase: false));
        Assert.Null(assembly.GetType("TianShu.ControlPlane.Abstractions.ControlPlaneException", throwOnError: false, ignoreCase: false));
    }

    [Fact]
    public async Task GetSnapshotAsync_ReturnsRuntimeState()
    {
        var runtime = CreateRuntimeMock();
        runtime.SetupGet(static item => item.RuntimeName).Returns("tianshu");
        runtime.SetupGet(static item => item.ActiveThreadId).Returns("thread-1");
        runtime.SetupGet(static item => item.HasActiveTurn).Returns(true);

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        var result = await sut.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal("tianshu", result.RuntimeName);
        Assert.Equal("thread-1", result.ActiveThreadId?.Value);
        Assert.True(result.HasActiveTurn);
        Assert.Equal(typeof(TianShu.Contracts.Primitives.ThreadId), result.ActiveThreadId?.GetType());
    }

    [Fact]
    public async Task SessionQueries_ForwardTypedPayloadsToRuntime()
    {
        var runtime = CreateRuntimeMock();
        GetSessionOverview? capturedOverview = null;
        ListSessions? capturedList = null;
        var space = new CollaborationSpace(
            new CollaborationSpaceId("space-session"),
            "design",
            "Design",
            new CollaborationSpaceProfile("架构设计"),
            CollaborationDefaultSet.Empty);
        var overviewProjection = new SessionOverviewProjection(
            new SessionId("session-1"),
            "Architecture",
            CollaborationSpaceRef.From(space),
            SessionMode.Planning,
            new ThreadId("thread-1"),
            HasActiveTurn: true);

        runtime
            .Setup(item => item.GetSessionOverviewAsync(It.IsAny<GetSessionOverview>(), It.IsAny<CancellationToken>()))
            .Callback<GetSessionOverview, CancellationToken>((query, _) => capturedOverview = query)
            .ReturnsAsync(overviewProjection);
        runtime
            .Setup(item => item.ListSessionsAsync(It.IsAny<ListSessions>(), It.IsAny<CancellationToken>()))
            .Callback<ListSessions, CancellationToken>((query, _) => capturedList = query)
            .ReturnsAsync([overviewProjection]);

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        var overview = await sut.GetSessionOverviewAsync(new GetSessionOverview(new SessionId("session-1")), CancellationToken.None);
        var sessions = await sut.ListSessionsAsync(new ListSessions(new CollaborationSpaceId("space-session"), IncludeClosed: true), CancellationToken.None);

        Assert.Equal("session-1", capturedOverview?.SessionId.Value);
        Assert.Equal("space-session", capturedList?.CollaborationSpaceId?.Value);
        Assert.True(capturedList?.IncludeClosed);
        Assert.Equal("Architecture", overview?.Title);
        Assert.Equal("thread-1", overview?.ActiveThreadId?.Value);
        Assert.Single(sessions);
    }

    [Fact]
    public async Task SubmitTurnAsync_MapsInputsHistoryAndEnvelopeToRuntime()
    {
        var runtime = CreateRuntimeMock();
        ControlPlaneSubmitTurnCommand? capturedCommand = null;
        runtime
            .Setup(static item => item.SendAsync(
                It.IsAny<ControlPlaneSubmitTurnCommand>(),
                It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneSubmitTurnCommand, CancellationToken>((command, _) => capturedCommand = command)
            .ReturnsAsync(new ControlPlaneTurnSubmissionResult
            {
                Accepted = true,
                Message = "accepted",
                TurnId = new TurnId("turn-1"),
                TurnStatus = "running",
                CorrelationId = "corr-1",
                RequestedMode = ControlPlaneFollowUpMode.Queue,
                EffectiveMode = ControlPlaneFollowUpMode.Queue,
            });

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var command = new ControlPlaneSubmitTurnCommand
        {
            Envelope = new InteractionEnvelope(
                new InteractionEnvelopeId("interaction-runtime-1"),
                new InteractionSource(InteractionSourceKind.Host, "cli"),
                [new TextInteractionItem("请帮我分析")],
                new InteractionTarget(ThreadId: new ThreadId("thread-runtime-1"))),
            Inputs =
            [
                new ControlPlaneTextInput(
                    "请帮我分析",
                    [new ControlPlaneTextElement(new ControlPlaneByteRange(0, 6), "prompt")]),
                new ControlPlaneImageInput("https://example.com/a.png"),
                new ControlPlaneLocalImageInput(@"D:\tmp\b.png"),
                new ControlPlaneSkillInput("csharp", @"C:\skills\csharp\SKILL.md"),
                new ControlPlaneMentionInput("repo", "app://repo"),
            ],
            History =
            [
                new ControlPlaneConversationMessage
                {
                    Role = ControlPlaneConversationRole.Assistant,
                    Content = "上一轮回复",
                    ContentItems = [new ControlPlaneTextInput("上一轮回复")],
                    Timestamp = new DateTimeOffset(2026, 4, 7, 10, 0, 0, TimeSpan.Zero),
                    IsStreaming = true,
                },
            ],
        };

        var result = await sut.SubmitTurnAsync(command, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("accepted", result.Message);
        Assert.Equal("turn-1", result.TurnId?.Value);
        Assert.Equal("running", result.TurnStatus);
        Assert.Equal("corr-1", result.CorrelationId);
        Assert.Equal(ControlPlaneFollowUpMode.Queue, result.RequestedMode);
        Assert.Equal(ControlPlaneFollowUpMode.Queue, result.EffectiveMode);

        var captured = Assert.IsType<ControlPlaneSubmitTurnCommand>(capturedCommand);
        var inputs = Assert.IsAssignableFrom<IReadOnlyList<ControlPlaneInputItem>>(captured.Inputs);
        Assert.Collection(
            inputs,
            item =>
            {
                var text = Assert.IsType<ControlPlaneTextInput>(item);
                Assert.Equal("text", text.Type);
                Assert.Equal("请帮我分析", text.Text);
                var element = Assert.Single(text.TextElements);
                Assert.Equal(0, element.ByteRange.Start);
                Assert.Equal(6, element.ByteRange.End);
                Assert.Equal("prompt", element.Placeholder);
            },
            item =>
            {
                var image = Assert.IsType<ControlPlaneImageInput>(item);
                Assert.Equal("https://example.com/a.png", image.Url);
            },
            item =>
            {
                var image = Assert.IsType<ControlPlaneLocalImageInput>(item);
                Assert.Equal(@"D:\tmp\b.png", image.Path);
            },
            item =>
            {
                var skill = Assert.IsType<ControlPlaneSkillInput>(item);
                Assert.Equal("csharp", skill.Name);
                Assert.Equal(@"C:\skills\csharp\SKILL.md", skill.Path);
            },
            item =>
            {
                var mention = Assert.IsType<ControlPlaneMentionInput>(item);
                Assert.Equal("repo", mention.Name);
                Assert.Equal("app://repo", mention.Path);
            });

        var history = Assert.IsAssignableFrom<IReadOnlyList<ControlPlaneConversationMessage>>(captured.History);
        var message = Assert.Single(history);
        Assert.Equal(ControlPlaneConversationRole.Assistant, message.Role);
        Assert.Equal("上一轮回复", message.Content);
        Assert.True(message.IsStreaming);
        Assert.Equal(new DateTimeOffset(2026, 4, 7, 10, 0, 0, TimeSpan.Zero), message.Timestamp);
        var contentItem = Assert.Single(message.ContentItems);
        Assert.IsType<ControlPlaneTextInput>(contentItem);
        Assert.Equal("interaction-runtime-1", captured.Envelope?.Id.Value);
        Assert.Equal("cli", captured.Envelope?.Source.Surface);
        Assert.Equal("thread-runtime-1", captured.Envelope?.Target?.ThreadId?.Value);
    }

    [Fact]
    public async Task SubmitFollowUpAsync_MapsEnvelopeModeAndCorrelationToRuntime()
    {
        var runtime = CreateRuntimeMock();
        ControlPlaneSubmitFollowUpCommand? capturedCommand = null;
        runtime
            .Setup(static item => item.SendFollowUpAsync(
                It.IsAny<ControlPlaneSubmitFollowUpCommand>(),
                It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneSubmitFollowUpCommand, CancellationToken>((command, _) => capturedCommand = command)
            .ReturnsAsync(new ControlPlaneTurnSubmissionResult
            {
                Accepted = true,
                Message = "queued",
                TurnId = new TurnId("turn-followup-1"),
                TurnStatus = "queued",
                CorrelationId = "corr-followup-1",
                RequestedMode = ControlPlaneFollowUpMode.Steer,
                EffectiveMode = ControlPlaneFollowUpMode.Steer,
            });

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var command = new ControlPlaneSubmitFollowUpCommand
        {
            Envelope = new InteractionEnvelope(
                new InteractionEnvelopeId("interaction-followup-runtime-1"),
                new InteractionSource(InteractionSourceKind.Host, "sidecar"),
                [new TextInteractionItem("继续")]),
            Inputs = [new ControlPlaneTextInput("继续")],
            Mode = ControlPlaneFollowUpMode.Steer,
            CorrelationId = "corr-followup-1",
        };

        var result = await sut.SubmitFollowUpAsync(command, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("turn-followup-1", result.TurnId?.Value);
        Assert.Equal("corr-followup-1", result.CorrelationId);
        Assert.Equal(ControlPlaneFollowUpMode.Steer, capturedCommand?.Mode);
        Assert.Equal("interaction-followup-runtime-1", capturedCommand?.Envelope?.Id.Value);
        Assert.Equal("sidecar", capturedCommand?.Envelope?.Source.Surface);
        Assert.IsType<ControlPlaneTextInput>(Assert.Single(capturedCommand!.Inputs));
    }

    [Fact]
    public async Task MutatePendingFollowUpAsync_ForwardsMutationCommandToRuntime()
    {
        var runtime = CreateRuntimeMock();
        ControlPlaneMutatePendingFollowUpCommand? capturedCommand = null;
        runtime
            .Setup(static item => item.MutatePendingFollowUpAsync(
                It.IsAny<ControlPlaneMutatePendingFollowUpCommand>(),
                It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneMutatePendingFollowUpCommand, CancellationToken>((command, _) => capturedCommand = command)
            .ReturnsAsync(new ControlPlanePendingFollowUpMutationResult
            {
                Accepted = true,
                Message = "已将待发送项转为引导。",
                ThreadId = new ThreadId("thread-1"),
                TurnId = new TurnId("turn-1"),
                CorrelationId = "corr-1",
                Kind = ControlPlanePendingFollowUpMutationKind.PromoteToSteer,
            });

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var result = await sut.MutatePendingFollowUpAsync(
            new ControlPlaneMutatePendingFollowUpCommand
            {
                ThreadId = new ThreadId("thread-1"),
                CorrelationId = "corr-1",
                Kind = ControlPlanePendingFollowUpMutationKind.PromoteToSteer,
            },
            CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("thread-1", result.ThreadId?.Value);
        Assert.Equal("turn-1", result.TurnId?.Value);
        Assert.Equal("corr-1", result.CorrelationId);
        Assert.Equal(ControlPlanePendingFollowUpMutationKind.PromoteToSteer, result.Kind);
        Assert.Equal("thread-1", capturedCommand?.ThreadId?.Value);
        Assert.Equal("corr-1", capturedCommand?.CorrelationId);
        Assert.Equal(ControlPlanePendingFollowUpMutationKind.PromoteToSteer, capturedCommand?.Kind);
    }

    [Fact]
    public async Task ListThreadsAsync_MapsThreadProjection()
    {
        var runtime = CreateRuntimeMock();
        ControlPlaneThreadListQuery? capturedRequest = null;
        runtime
            .Setup(static item => item.ListThreadsAsync(It.IsAny<ControlPlaneThreadListQuery>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneThreadListQuery, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new ControlPlaneThreadListResult
            {
                Threads =
                [
                    new ControlPlaneThreadSummary
                    {
                        ThreadId = new ThreadId("thread-1"),
                        Preview = "实现控制平面",
                        Name = "控制平面迁移",
                        WorkingDirectory = @"D:\repo",
                        Path = @"D:\repo\.tianshu\threads\thread-1.jsonl",
                        ModelProvider = "openai",
                        Source = "subAgentThreadSpawn",
                        CliVersion = "0.1.0",
                        AgentNickname = "planner",
                        AgentRole = "architect",
                        CreatedAt = new DateTimeOffset(2026, 4, 6, 9, 0, 0, TimeSpan.Zero),
                        UpdatedAt = new DateTimeOffset(2026, 4, 7, 9, 0, 0, TimeSpan.Zero),
                        IsEphemeral = true,
                        Status = "running",
                        ActiveFlags = ["streaming", "subagent"],
                        GitSha = "abc123",
                        GitBranch = "newkernel",
                        GitOriginUrl = "https://example.com/tianshu.git",
                        SessionConfiguration = new ControlPlaneThreadSessionConfiguration
                        {
                            Model = "gpt-5",
                            ModelProvider = "openai",
                            ServiceTier = "fast",
                            ApprovalPolicy = "on-request",
                            ReasoningEffort = "high",
                            ReasoningSummary = "verbose",
                            Verbosity = "high",
                            Personality = "pragmatic",
                            ForkedFromThreadId = new ThreadId("thread-0"),
                            WorkingDirectory = @"D:\repo",
                            SessionSource = "cli",
                        },
                    },
                ],
                NextCursor = "cursor-2",
            });

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var result = await sut.ListThreadsAsync(
            new ControlPlaneThreadListQuery
            {
                Limit = 10,
                Cursor = "cursor-1",
                Archived = true,
                WorkingDirectory = @"D:\repo",
                SortKey = "updated_at",
                ModelProviders = ["openai", "azure"],
                SourceKinds =
                [
                    ControlPlaneThreadSourceKind.Cli,
                    ControlPlaneThreadSourceKind.SubAgentThreadSpawn,
                    ControlPlaneThreadSourceKind.Unknown,
                ],
                SearchTerm = "control-plane",
            },
            CancellationToken.None);

        var request = Assert.IsType<ControlPlaneThreadListQuery>(capturedRequest);
        Assert.Equal(10, request.Limit);
        Assert.Equal("cursor-1", request.Cursor);
        Assert.True(request.Archived);
        Assert.Equal(@"D:\repo", request.WorkingDirectory);
        Assert.Equal("updated_at", request.SortKey);
        Assert.Equal(["openai", "azure"], request.ModelProviders);
        Assert.Equal(
            new[]
            {
                ControlPlaneThreadSourceKind.Cli,
                ControlPlaneThreadSourceKind.SubAgentThreadSpawn,
                ControlPlaneThreadSourceKind.Unknown,
            },
            request.SourceKinds);
        Assert.Equal("control-plane", request.SearchTerm);

        Assert.Equal("cursor-2", result.NextCursor);
        var thread = Assert.Single(result.Threads);
        Assert.Equal("thread-1", thread.ThreadId.Value);
        Assert.Equal("实现控制平面", thread.Preview);
        Assert.Equal("控制平面迁移", thread.Name);
        Assert.Equal(@"D:\repo", thread.WorkingDirectory);
        Assert.Equal(@"D:\repo\.tianshu\threads\thread-1.jsonl", thread.Path);
        Assert.Equal("openai", thread.ModelProvider);
        Assert.Equal(ControlPlaneThreadSourceKind.SubAgentThreadSpawn, thread.Source);
        Assert.Equal("running", thread.Status);
        Assert.Equal(["streaming", "subagent"], thread.ActiveFlags);
        Assert.Equal("abc123", thread.GitSha);
        Assert.Equal("newkernel", thread.GitBranch);
        Assert.Equal("https://example.com/tianshu.git", thread.GitOriginUrl);
        Assert.NotNull(thread.SessionConfiguration);
        Assert.Equal("gpt-5", thread.SessionConfiguration!.Model);
        Assert.Equal("openai", thread.SessionConfiguration.ModelProvider);
        Assert.Equal("fast", thread.SessionConfiguration.ServiceTier);
        Assert.Equal("on-request", thread.SessionConfiguration.ApprovalPolicy);
        Assert.Equal("high", thread.SessionConfiguration.ReasoningEffort);
        Assert.Equal("verbose", thread.SessionConfiguration.ReasoningSummary);
        Assert.Equal("high", thread.SessionConfiguration.Verbosity);
        Assert.Equal("pragmatic", thread.SessionConfiguration.Personality);
        Assert.Equal("thread-0", thread.SessionConfiguration.ForkedFromThreadId?.Value);
        Assert.Equal(@"D:\repo", thread.SessionConfiguration.WorkingDirectory);
        Assert.Equal(ControlPlaneSessionSource.Cli, thread.SessionConfiguration.SessionSource);
    }

    [Fact]
    public async Task StartThreadAsync_MapsTypedCommandToRuntime()
    {
        var runtime = CreateRuntimeMock();
        ControlPlaneStartThreadCommand? capturedRequest = null;
        runtime
            .Setup(static item => item.StartNewThreadAsync(It.IsAny<ControlPlaneStartThreadCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneStartThreadCommand, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new ControlPlaneThreadSummary
            {
                ThreadId = new ThreadId("thread-start-1"),
                Preview = "控制平面 start",
                Name = "控制平面 start",
                WorkingDirectory = @"D:\repo",
                UpdatedAt = new DateTimeOffset(2026, 4, 7, 10, 30, 0, TimeSpan.Zero),
            });

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var result = await sut.StartThreadAsync(
            new ControlPlaneStartThreadCommand
            {
                Model = "gpt-5",
                ModelProvider = "anthropic",
                ServiceTier = "fast",
                WorkingDirectory = @"D:\repo",
                ApprovalPolicy = "on-request",
                SandboxMode = "workspace-write",
                Configuration = new Dictionary<string, ControlPlaneStructuredValue>(StringComparer.Ordinal)
                {
                    ["env"] = ControlPlaneStructuredValue.FromString("dev"),
                },
                ServiceName = "worker",
                BaseInstructions = "base",
                DeveloperInstructions = "developer",
                Personality = "friendly",
                Ephemeral = true,
                DynamicTools =
                [
                    new ControlPlaneDynamicToolSpec
                    {
                        Name = "task_lookup",
                        Description = "查询任务",
                        InputSchema = ControlPlaneStructuredValue.FromObject(new Dictionary<string, ControlPlaneStructuredValue>(StringComparer.Ordinal)
                        {
                            ["type"] = ControlPlaneStructuredValue.FromString("object"),
                        }),
                    },
                ],
                MockExperimentalField = "experimental-start",
                PersistExtendedHistory = true,
                ExperimentalRawEvents = true,
            },
            CancellationToken.None);

        var request = Assert.IsType<ControlPlaneStartThreadCommand>(capturedRequest);
        Assert.Equal("gpt-5", request.Model);
        Assert.Equal("anthropic", request.ModelProvider);
        Assert.Equal("fast", request.ServiceTier);
        Assert.Equal(@"D:\repo", request.WorkingDirectory);
        Assert.Equal("on-request", request.ApprovalPolicy);
        Assert.Equal("workspace-write", request.SandboxMode);
        Assert.Equal("dev", request.Configuration?["env"].StringValue);
        Assert.Equal("worker", request.ServiceName);
        Assert.Equal("base", request.BaseInstructions);
        Assert.Equal("developer", request.DeveloperInstructions);
        Assert.Equal("friendly", request.Personality);
        Assert.True(request.Ephemeral);
        var dynamicTool = Assert.Single(request.DynamicTools!);
        Assert.Equal("task_lookup", dynamicTool.Name);
        Assert.Equal("查询任务", dynamicTool.Description);
        Assert.Equal("object", dynamicTool.InputSchema.Properties["type"].StringValue);
        Assert.Equal("experimental-start", request.MockExperimentalField);
        Assert.True(request.PersistExtendedHistory);
        Assert.True(request.ExperimentalRawEvents);

        Assert.NotNull(result);
        Assert.Equal("thread-start-1", result!.ThreadId.Value);
        Assert.Equal("控制平面 start", result.Preview);
    }

    [Fact]
    public async Task ResumeThreadAsync_MapsTypedCommandAndSnapshot()
    {
        var runtime = CreateRuntimeMock();
        ControlPlaneResumeThreadCommand? capturedRequest = null;
        runtime
            .Setup(static item => item.ResumeThreadAsync(It.IsAny<ControlPlaneResumeThreadCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneResumeThreadCommand, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new ControlPlaneThreadSnapshot
            {
                Thread = new ControlPlaneThreadSummary
                {
                    ThreadId = new ThreadId("thread-resume-1"),
                    Preview = "控制平面 resume",
                    Name = "控制平面 resume",
                    WorkingDirectory = @"D:\repo",
                    UpdatedAt = new DateTimeOffset(2026, 4, 7, 11, 0, 0, TimeSpan.Zero),
                },
                Turns =
                [
                    new ControlPlaneThreadTurn
                    {
                        Id = "turn-1",
                        Status = "completed",
                        Items =
                        [
                            new ControlPlaneThreadTurnItem
                            {
                                Id = "item-1",
                                Type = "assistant_message",
                                Text = "resume output",
                                Phase = "completed",
                                Data = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                                {
                                    ["text"] = StructuredValue.FromString("resume output"),
                                }),
                            },
                        ],
                    },
                ],
                SeedHistory =
                [
                    new ControlPlaneSeedHistoryItem
                    {
                        Role = "user",
                        Content = "resume",
                        Inputs = [new ControlPlaneTextInput("resume")],
                    },
                ],
                Messages =
                [
                    new ControlPlaneConversationMessage
                    {
                        Role = ControlPlaneConversationRole.Assistant,
                        Content = "恢复完成",
                        ContentItems = [new ControlPlaneTextInput("恢复完成")],
                    },
                ],
                PendingInputState = new ControlPlanePendingInputState(
                    Entries:
                    [
                        new ControlPlanePendingInputStateEntry(
                            "corr-resume-1",
                            "Queue",
                            "Queue",
                            "awaiting_commit",
                            "turn-1",
                            null,
                            StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["message"] = "后续消息",
                                ["imageCount"] = 0,
                            }),
                            "QueuedUserMessage",
                            [new ControlPlaneTextInput("后续消息")]),
                    ],
                    InterruptRequestPending: false),
                PendingInteractiveRequests =
                [
                    new ControlPlanePendingInteractiveRequest
                    {
                        RequestId = 1001,
                        RequestKind = "approval_requested",
                        RequestMethod = "item/tool/requestApproval",
                        CallId = "approval-resume-1",
                        ThreadId = "thread-resume-1",
                        TurnId = "turn-1",
                        ToolName = "shell",
                        Text = "需要批准 shell",
                        Status = "awaitingApproval",
                        Phase = "request_approval",
                        RequiresApproval = true,
                        ApprovalKind = "shell_command",
                        AvailableDecisions = ["accept", "decline"],
                        AvailableDecisionOptions =
                        [
                            new ControlPlaneApprovalDecisionOption(
                                "acceptWithExecpolicyAmendment",
                                new ControlPlaneExecPolicyAmendment(["git", "status"])),
                        ],
                    },
                ],
            });

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var result = await sut.ResumeThreadAsync(
            new ControlPlaneResumeThreadCommand
            {
                ThreadId = new ThreadId("thread-resume-1"),
                History =
                [
                    ControlPlaneStructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "message",
                        ["role"] = "assistant",
                        ["content"] = new object?[]
                        {
                            new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["type"] = "output_text",
                                ["text"] = "恢复上下文",
                            },
                        },
                    }),
                ],
                ModelProvider = "openai",
                ApprovalPolicy = "on-request",
                Personality = "friendly",
                PersistExtendedHistory = true,
            },
            CancellationToken.None);

        var request = Assert.IsType<ControlPlaneResumeThreadCommand>(capturedRequest);
        Assert.Equal("thread-resume-1", request.ThreadId.Value);
        Assert.Equal("openai", request.ModelProvider);
        Assert.Equal("on-request", request.ApprovalPolicy);
        Assert.Equal("friendly", request.Personality);
        Assert.True(request.PersistExtendedHistory);
        var history = Assert.Single(request.History!);
        Assert.Equal("message", history.Properties["type"].StringValue);
        Assert.Equal("assistant", history.Properties["role"].StringValue);
        var content = Assert.Single(history.Properties["content"].Items);
        Assert.Equal("output_text", content.Properties["type"].StringValue);
        Assert.Equal("恢复上下文", content.Properties["text"].StringValue);

        Assert.NotNull(result);
        Assert.Equal("thread-resume-1", result!.Thread.ThreadId.Value);
        Assert.Equal("控制平面 resume", result.Thread.Name);
        var turn = Assert.Single(result.Turns);
        Assert.Equal("turn-1", turn.Id);
        var turnItem = Assert.Single(turn.Items);
        Assert.Equal("assistant_message", turnItem.Type);
        Assert.Equal("resume output", turnItem.Text);
        Assert.Equal("completed", turnItem.Phase);
        Assert.NotNull(turnItem.Data);
        Assert.Equal("resume output", turnItem.Data!.Properties["text"].StringValue);
        var seedHistory = Assert.Single(result.SeedHistory);
        Assert.Equal("user", seedHistory.Role);
        Assert.Equal("resume", seedHistory.Content);
        var pendingInputState = Assert.IsType<ControlPlanePendingInputState>(result.PendingInputState);
        var entry = Assert.Single(pendingInputState.Entries);
        Assert.Equal("corr-resume-1", entry.CorrelationId);
        Assert.Equal("后续消息", entry.CompareKey?.Properties["message"].StringValue);
        var pendingRequest = Assert.Single(result.PendingInteractiveRequests);
        Assert.Equal("approval_requested", pendingRequest.RequestKind);
        Assert.Equal("shell_command", pendingRequest.ApprovalKind);
        var option = Assert.Single(pendingRequest.AvailableDecisionOptions!);
        Assert.Equal("acceptWithExecpolicyAmendment", option.Type);
        Assert.Equal(["git", "status"], option.ExecPolicyAmendment?.CommandPrefix);
    }

    [Fact]
    public async Task ExtendedConversationOperations_MapTypedCommandsToRuntime()
    {
        var runtime = CreateRuntimeMock();
        ControlPlaneLoadedThreadListQuery? loadedListQuery = null;
        ControlPlaneReadThreadQuery? readThreadQuery = null;
        ControlPlaneCompactThreadCommand? compactThreadCommand = null;
        ControlPlaneCleanBackgroundTerminalsCommand? cleanBackgroundCommand = null;
        ControlPlaneUnsubscribeThreadCommand? unsubscribeCommand = null;
        ControlPlaneIncrementThreadElicitationCommand? incrementCommand = null;
        ControlPlaneDecrementThreadElicitationCommand? decrementCommand = null;
        ControlPlaneUnarchiveThreadCommand? unarchiveCommand = null;
        ControlPlaneUpdateThreadMetadataCommand? updateMetadataCommand = null;
        ControlPlaneRollbackThreadCommand? rollbackCommand = null;
        ControlPlaneRealtimeStartCommand? realtimeStartCommand = null;
        ControlPlaneRealtimeAppendTextCommand? realtimeAppendTextCommand = null;
        ControlPlaneRealtimeAppendAudioCommand? realtimeAppendAudioCommand = null;
        ControlPlaneRealtimeHandoffOutputCommand? realtimeHandoffCommand = null;
        ControlPlaneRealtimeStopCommand? realtimeStopCommand = null;

        runtime
            .Setup(static item => item.ListLoadedThreadsAsync(It.IsAny<ControlPlaneLoadedThreadListQuery>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneLoadedThreadListQuery, CancellationToken>((query, _) => loadedListQuery = query)
            .ReturnsAsync(new ControlPlaneLoadedThreadListResult
            {
                ThreadIds = [new ThreadId("thread-loaded-1")],
                NextCursor = "loaded-cursor-1",
            });
        runtime
            .Setup(static item => item.ReadThreadAsync(It.IsAny<ControlPlaneReadThreadQuery>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneReadThreadQuery, CancellationToken>((query, _) => readThreadQuery = query)
            .ReturnsAsync(new ControlPlaneThreadOperationResult
            {
                Thread = new ControlPlaneThreadDetail
                {
                    ThreadId = new ThreadId("thread-read-1"),
                    Preview = "read preview",
                    UpdatedAt = new DateTimeOffset(2026, 4, 9, 8, 0, 0, TimeSpan.Zero),
                },
            });
        runtime
            .Setup(static item => item.CompactThreadAsync(It.IsAny<ControlPlaneCompactThreadCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneCompactThreadCommand, CancellationToken>((command, _) => compactThreadCommand = command)
            .ReturnsAsync(new ControlPlaneThreadCommandAcceptedResult());
        runtime
            .Setup(static item => item.CleanBackgroundTerminalsAsync(It.IsAny<ControlPlaneCleanBackgroundTerminalsCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneCleanBackgroundTerminalsCommand, CancellationToken>((command, _) => cleanBackgroundCommand = command)
            .ReturnsAsync(new ControlPlaneThreadCommandAcceptedResult());
        runtime
            .Setup(static item => item.UnsubscribeThreadAsync(It.IsAny<ControlPlaneUnsubscribeThreadCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneUnsubscribeThreadCommand, CancellationToken>((command, _) => unsubscribeCommand = command)
            .ReturnsAsync(new ControlPlaneThreadUnsubscribeResult
            {
                Status = "unsubscribed",
            });
        runtime
            .Setup(static item => item.IncrementThreadElicitationAsync(It.IsAny<ControlPlaneIncrementThreadElicitationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneIncrementThreadElicitationCommand, CancellationToken>((command, _) => incrementCommand = command)
            .ReturnsAsync(new ControlPlaneThreadElicitationResult
            {
                Count = 2,
                Paused = true,
            });
        runtime
            .Setup(static item => item.DecrementThreadElicitationAsync(It.IsAny<ControlPlaneDecrementThreadElicitationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneDecrementThreadElicitationCommand, CancellationToken>((command, _) => decrementCommand = command)
            .ReturnsAsync(new ControlPlaneThreadElicitationResult
            {
                Count = 1,
                Paused = false,
            });
        runtime
            .Setup(static item => item.UnarchiveThreadAsync(It.IsAny<ControlPlaneUnarchiveThreadCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneUnarchiveThreadCommand, CancellationToken>((command, _) => unarchiveCommand = command)
            .ReturnsAsync(new ControlPlaneThreadOperationResult
            {
                Thread = new ControlPlaneThreadDetail
                {
                    ThreadId = new ThreadId("thread-unarchive-1"),
                    Preview = "unarchived",
                    UpdatedAt = new DateTimeOffset(2026, 4, 9, 8, 1, 0, TimeSpan.Zero),
                },
            });
        runtime
            .Setup(static item => item.UpdateThreadMetadataAsync(It.IsAny<ControlPlaneUpdateThreadMetadataCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneUpdateThreadMetadataCommand, CancellationToken>((command, _) => updateMetadataCommand = command)
            .ReturnsAsync(new ControlPlaneThreadOperationResult
            {
                Thread = new ControlPlaneThreadDetail
                {
                    ThreadId = new ThreadId("thread-metadata-1"),
                    Preview = "metadata updated",
                    GitSha = "abc123",
                    GitBranch = "main",
                    UpdatedAt = new DateTimeOffset(2026, 4, 9, 8, 2, 0, TimeSpan.Zero),
                },
            });
        runtime
            .Setup(static item => item.RollbackThreadAsync(It.IsAny<ControlPlaneRollbackThreadCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneRollbackThreadCommand, CancellationToken>((command, _) => rollbackCommand = command)
            .ReturnsAsync(new ControlPlaneThreadOperationResult
            {
                Thread = new ControlPlaneThreadDetail
                {
                    ThreadId = new ThreadId("thread-rollback-1"),
                    Preview = "rolled back",
                    UpdatedAt = new DateTimeOffset(2026, 4, 9, 8, 3, 0, TimeSpan.Zero),
                },
            });
        runtime
            .Setup(static item => item.StartRealtimeAsync(It.IsAny<ControlPlaneRealtimeStartCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneRealtimeStartCommand, CancellationToken>((command, _) => realtimeStartCommand = command)
            .ReturnsAsync(new ControlPlaneRealtimeCommandAcceptedResult());
        runtime
            .Setup(static item => item.AppendRealtimeTextAsync(It.IsAny<ControlPlaneRealtimeAppendTextCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneRealtimeAppendTextCommand, CancellationToken>((command, _) => realtimeAppendTextCommand = command)
            .ReturnsAsync(new ControlPlaneRealtimeCommandAcceptedResult());
        runtime
            .Setup(static item => item.AppendRealtimeAudioAsync(It.IsAny<ControlPlaneRealtimeAppendAudioCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneRealtimeAppendAudioCommand, CancellationToken>((command, _) => realtimeAppendAudioCommand = command)
            .ReturnsAsync(new ControlPlaneRealtimeCommandAcceptedResult());
        runtime
            .Setup(static item => item.HandoffRealtimeOutputAsync(It.IsAny<ControlPlaneRealtimeHandoffOutputCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneRealtimeHandoffOutputCommand, CancellationToken>((command, _) => realtimeHandoffCommand = command)
            .ReturnsAsync(new ControlPlaneRealtimeCommandAcceptedResult());
        runtime
            .Setup(static item => item.StopRealtimeAsync(It.IsAny<ControlPlaneRealtimeStopCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneRealtimeStopCommand, CancellationToken>((command, _) => realtimeStopCommand = command)
            .ReturnsAsync(new ControlPlaneRealtimeCommandAcceptedResult());

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        var loadedList = await sut.ListLoadedThreadsAsync(
            new ControlPlaneLoadedThreadListQuery
            {
                Limit = 5,
                Cursor = "loaded-cursor-0",
            },
            CancellationToken.None);
        var readThread = await sut.ReadThreadAsync(
            new ControlPlaneReadThreadQuery
            {
                ThreadId = new ThreadId("thread-read-1"),
                IncludeTurns = true,
            },
            CancellationToken.None);
        var compactResult = await sut.CompactThreadAsync(
            new ControlPlaneCompactThreadCommand
            {
                ThreadId = new ThreadId("thread-compact-1"),
                KeepRecentTurns = 3,
            },
            CancellationToken.None);
        var cleanResult = await sut.CleanBackgroundTerminalsAsync(
            new ControlPlaneCleanBackgroundTerminalsCommand
            {
                ThreadId = new ThreadId("thread-clean-1"),
            },
            CancellationToken.None);
        var unsubscribeResult = await sut.UnsubscribeThreadAsync(
            new ControlPlaneUnsubscribeThreadCommand
            {
                ThreadId = new ThreadId("thread-unsub-1"),
            },
            CancellationToken.None);
        var incrementResult = await sut.IncrementThreadElicitationAsync(
            new ControlPlaneIncrementThreadElicitationCommand
            {
                ThreadId = new ThreadId("thread-elicit-1"),
            },
            CancellationToken.None);
        var decrementResult = await sut.DecrementThreadElicitationAsync(
            new ControlPlaneDecrementThreadElicitationCommand
            {
                ThreadId = new ThreadId("thread-elicit-1"),
            },
            CancellationToken.None);
        var unarchiveResult = await sut.UnarchiveThreadAsync(
            new ControlPlaneUnarchiveThreadCommand
            {
                ThreadId = new ThreadId("thread-unarchive-1"),
            },
            CancellationToken.None);
        var metadataResult = await sut.UpdateThreadMetadataAsync(
            new ControlPlaneUpdateThreadMetadataCommand
            {
                ThreadId = new ThreadId("thread-metadata-1"),
                HasGitSha = true,
                GitSha = "abc123",
                HasGitBranch = true,
                GitBranch = "main",
            },
            CancellationToken.None);
        var rollbackResult = await sut.RollbackThreadAsync(
            new ControlPlaneRollbackThreadCommand
            {
                ThreadId = new ThreadId("thread-rollback-1"),
                NumTurns = 2,
            },
            CancellationToken.None);
        var realtimeStartResult = await sut.StartRealtimeAsync(
            new ControlPlaneRealtimeStartCommand
            {
                ThreadId = new ThreadId("thread-rt-1"),
                SessionId = "session-rt-1",
                Prompt = "start",
            },
            CancellationToken.None);
        var realtimeAppendTextResult = await sut.AppendRealtimeTextAsync(
            new ControlPlaneRealtimeAppendTextCommand
            {
                ThreadId = new ThreadId("thread-rt-1"),
                SessionId = "session-rt-1",
                Text = "hello",
            },
            CancellationToken.None);
        var realtimeAppendAudioResult = await sut.AppendRealtimeAudioAsync(
            new ControlPlaneRealtimeAppendAudioCommand
            {
                ThreadId = new ThreadId("thread-rt-1"),
                SessionId = "session-rt-1",
                Audio = new ControlPlaneRealtimeAudioInput
                {
                    Data = "AQID",
                    SampleRate = 24000,
                },
            },
            CancellationToken.None);
        var realtimeHandoffResult = await sut.HandoffRealtimeOutputAsync(
            new ControlPlaneRealtimeHandoffOutputCommand
            {
                ThreadId = new ThreadId("thread-rt-1"),
                SessionId = "session-rt-1",
                HandoffId = "call-rt-1",
                Output = "done",
            },
            CancellationToken.None);
        var realtimeStopResult = await sut.StopRealtimeAsync(
            new ControlPlaneRealtimeStopCommand
            {
                ThreadId = new ThreadId("thread-rt-1"),
                SessionId = "session-rt-1",
            },
            CancellationToken.None);

        Assert.Equal("loaded-cursor-1", loadedList.NextCursor);
        Assert.Equal("thread-loaded-1", Assert.Single(loadedList.ThreadIds).Value);
        Assert.Equal("loaded-cursor-0", loadedListQuery?.Cursor);
        Assert.Equal(5, loadedListQuery?.Limit);
        Assert.Equal("thread-read-1", readThread.Thread?.ThreadId.Value);
        Assert.Equal("thread-read-1", readThreadQuery?.ThreadId.Value);
        Assert.True(readThreadQuery?.IncludeTurns);
        Assert.Equal("thread-compact-1", compactThreadCommand?.ThreadId.Value);
        Assert.Equal(3, compactThreadCommand?.KeepRecentTurns);
        Assert.Equal("thread-clean-1", cleanBackgroundCommand?.ThreadId.Value);
        Assert.Equal("unsubscribed", unsubscribeResult.Status);
        Assert.Equal("thread-unsub-1", unsubscribeCommand?.ThreadId.Value);
        Assert.Equal((ulong)2, incrementResult.Count);
        Assert.True(incrementResult.Paused);
        Assert.Equal("thread-elicit-1", incrementCommand?.ThreadId.Value);
        Assert.Equal((ulong)1, decrementResult.Count);
        Assert.False(decrementResult.Paused);
        Assert.Equal("thread-elicit-1", decrementCommand?.ThreadId.Value);
        Assert.Equal("thread-unarchive-1", unarchiveResult.Thread?.ThreadId.Value);
        Assert.Equal("thread-unarchive-1", unarchiveCommand?.ThreadId.Value);
        Assert.Equal("abc123", metadataResult.Thread?.GitSha);
        Assert.Equal("main", metadataResult.Thread?.GitBranch);
        Assert.Equal("thread-metadata-1", updateMetadataCommand?.ThreadId.Value);
        Assert.True(updateMetadataCommand?.HasGitSha);
        Assert.Equal("abc123", updateMetadataCommand?.GitSha);
        Assert.Equal("thread-rollback-1", rollbackResult.Thread?.ThreadId.Value);
        Assert.Equal(2, rollbackCommand?.NumTurns);
        Assert.NotNull(compactResult);
        Assert.NotNull(cleanResult);
        Assert.NotNull(realtimeStartResult);
        Assert.NotNull(realtimeAppendTextResult);
        Assert.NotNull(realtimeAppendAudioResult);
        Assert.NotNull(realtimeHandoffResult);
        Assert.NotNull(realtimeStopResult);
        Assert.Equal("thread-rt-1", realtimeStartCommand?.ThreadId.Value);
        Assert.Equal("session-rt-1", realtimeStartCommand?.SessionId);
        Assert.Equal("start", realtimeStartCommand?.Prompt);
        Assert.Equal("hello", realtimeAppendTextCommand?.Text);
        Assert.Equal("AQID", realtimeAppendAudioCommand?.Audio.Data);
        Assert.Equal("call-rt-1", realtimeHandoffCommand?.HandoffId);
        Assert.Equal("done", realtimeHandoffCommand?.Output);
        Assert.Equal("session-rt-1", realtimeStopCommand?.SessionId);
    }

    [Fact]
    public async Task CreateJobAsync_MapsStructuredValuesAndReturnsProjection()
    {
        var runtime = CreateRuntimeMock();
        ControlPlaneCreateJobCommand? capturedRequest = null;
        runtime
            .Setup(static item => item.CreateAgentJobAsync(It.IsAny<ControlPlaneCreateJobCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneCreateJobCommand, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new ControlPlaneJobOperationResult
            {
                Job = new ControlPlaneJobDetails
                {
                    Id = new JobId("job-1"),
                    Name = "批量分析",
                    Status = "queued",
                    Instruction = "请逐条分析",
                },
                Items =
                [
                    new ControlPlaneJobItemDetails
                    {
                        ItemId = new JobItemId("item-1"),
                        SourceId = "source-1",
                        ThreadId = new ThreadId("thread-1"),
                        AssignedThreadId = new ThreadId("thread-2"),
                        Status = "running",
                        LastError = "none",
                        Result = ControlPlaneStructuredValue.FromObject(new Dictionary<string, ControlPlaneStructuredValue>(StringComparer.Ordinal)
                        {
                            ["score"] = ControlPlaneStructuredValue.FromNumber("0.98"),
                            ["passed"] = ControlPlaneStructuredValue.FromBoolean(true),
                        }),
                    },
                ],
            });

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var command = new ControlPlaneCreateJobCommand
        {
            JobId = new JobId("job-1"),
            Name = "批量分析",
            Instruction = "请逐条分析",
            InputHeaders = ControlPlaneStructuredValue.FromObject(new Dictionary<string, ControlPlaneStructuredValue>(StringComparer.Ordinal)
            {
                ["name"] = ControlPlaneStructuredValue.FromString("标题"),
            }),
            InputCsvPath = @"D:\input.csv",
            OutputCsvPath = @"D:\output.csv",
            AutoExport = true,
            OutputSchema = ControlPlaneStructuredValue.FromObject(new Dictionary<string, ControlPlaneStructuredValue>(StringComparer.Ordinal)
            {
                ["score"] = ControlPlaneStructuredValue.FromString("number"),
                ["passed"] = ControlPlaneStructuredValue.FromString("boolean"),
            }),
            Items =
            [
                ControlPlaneStructuredValue.FromObject(new Dictionary<string, ControlPlaneStructuredValue>(StringComparer.Ordinal)
                {
                    ["id"] = ControlPlaneStructuredValue.FromString("1"),
                    ["priority"] = ControlPlaneStructuredValue.FromNumber("10"),
                    ["enabled"] = ControlPlaneStructuredValue.FromBoolean(true),
                }),
            ],
        };

        var result = await sut.CreateJobAsync(command, CancellationToken.None);

        var request = Assert.IsType<ControlPlaneCreateJobCommand>(capturedRequest);
        Assert.Equal("job-1", request.JobId?.Value);
        Assert.Equal("批量分析", request.Name);
        Assert.Equal("请逐条分析", request.Instruction);
        Assert.Equal(@"D:\input.csv", request.InputCsvPath);
        Assert.Equal(@"D:\output.csv", request.OutputCsvPath);
        Assert.True(request.AutoExport);
        Assert.Equal(ControlPlaneStructuredValueKind.Object, request.InputHeaders?.Kind);
        Assert.Equal("标题", request.InputHeaders?.Properties["name"].StringValue);
        Assert.Equal("number", request.OutputSchema?.Properties["score"].StringValue);
        Assert.Equal("boolean", request.OutputSchema?.Properties["passed"].StringValue);
        var item = Assert.Single(request.Items);
        Assert.Equal(ControlPlaneStructuredValueKind.Object, item.Kind);
        Assert.Equal("1", item.Properties["id"].StringValue);
        Assert.Equal("10", item.Properties["priority"].NumberValue);
        Assert.True(item.Properties["enabled"].BooleanValue ?? false);

        Assert.NotNull(result.Job);
        Assert.Equal("job-1", result.Job!.Id.Value);
        Assert.Equal("批量分析", result.Job.Name);
        Assert.Equal("queued", result.Job.Status);
        Assert.Equal("请逐条分析", result.Job.Instruction);
        var jobItem = Assert.Single(result.Items);
        Assert.Equal("item-1", jobItem.ItemId.Value);
        Assert.Equal("source-1", jobItem.SourceId);
        Assert.Equal("thread-1", jobItem.ThreadId?.Value);
        Assert.Equal("thread-2", jobItem.AssignedThreadId?.Value);
        Assert.Equal("running", jobItem.Status);
        Assert.Equal("none", jobItem.LastError);
        Assert.NotNull(jobItem.Result);
        Assert.Equal(typeof(TianShu.Contracts.Primitives.StructuredValue), jobItem.Result!.GetType());
        Assert.Equal(ControlPlaneStructuredValueKind.Object, jobItem.Result!.Kind);
        Assert.Equal("0.98", jobItem.Result.Properties["score"].NumberValue);
        Assert.True(jobItem.Result.Properties["passed"].BooleanValue ?? false);
    }

    [Fact]
    public async Task ReportJobItemAsync_MapsTypedPayloadsToRuntime()
    {
        var runtime = CreateRuntimeMock();
        ControlPlaneReportJobItemCommand? capturedRequest = null;
        runtime
            .Setup(static item => item.ReportAgentJobItemAsync(It.IsAny<ControlPlaneReportJobItemCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneReportJobItemCommand, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new ControlPlaneJobOperationResult
            {
                Job = new ControlPlaneJobDetails
                {
                    Id = new JobId("job-2"),
                    Status = "running",
                },
                Item = new ControlPlaneJobItemDetails
                {
                    ItemId = new JobItemId("item-2"),
                    Status = "completed",
                    LastError = "none",
                    Result = ControlPlaneStructuredValue.FromObject(new Dictionary<string, ControlPlaneStructuredValue>(StringComparer.Ordinal)
                    {
                        ["score"] = ControlPlaneStructuredValue.FromNumber("99"),
                    }),
                },
            });

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var command = new ControlPlaneReportJobItemCommand
        {
            JobId = new JobId("job-2"),
            ItemId = new JobItemId("item-2"),
            Status = "completed",
            Result = ControlPlaneStructuredValue.FromObject(new Dictionary<string, ControlPlaneStructuredValue>(StringComparer.Ordinal)
            {
                ["score"] = ControlPlaneStructuredValue.FromNumber("99"),
            }),
            LastError = "none",
        };

        var result = await sut.ReportJobItemAsync(command, CancellationToken.None);

        var request = Assert.IsType<ControlPlaneReportJobItemCommand>(capturedRequest);
        Assert.Equal("job-2", request.JobId.Value);
        Assert.Equal("item-2", request.ItemId.Value);
        Assert.Equal("completed", request.Status);
        Assert.Equal("99", request.Result?.Properties["score"].NumberValue);
        Assert.Equal("none", request.LastError);
        Assert.Equal("item-2", result.Item?.ItemId.Value);
        Assert.Equal("completed", result.Item?.Status);
        Assert.Equal("99", result.Item?.Result?.Properties["score"].NumberValue);
    }

    [Fact]
    public void ITianShuControlPlane_ExposesExtendedFacades()
    {
        var runtime = CreateRuntimeMock();
        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        Assert.Same(sut, sut.Collaboration);
        Assert.Same(sut, sut.Sessions);
        Assert.Same(sut, sut.Conversations);
        Assert.Same(sut, sut.Workflows);
        Assert.Same(sut, sut.Agents);
        Assert.Same(sut, sut.Governance);
        Assert.Same(sut, sut.Catalog);
        Assert.Same(sut, sut.Artifacts);
        Assert.Same(sut, sut.Diagnostics);
        Assert.Same(sut, sut.Identity);
        Assert.Same(sut, sut.Memory);
        Assert.Same(sut, sut.Subscriptions);
    }

    [Fact]
    public void ITianShuControlPlane_UsesCollaborationFacadeForParticipantScope()
    {
        var properties = typeof(ITianShuControlPlane)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .ToArray();

        Assert.Contains("Collaboration", properties);
        Assert.Contains("Diagnostics", properties);
        Assert.Contains("Identity", properties);
        Assert.Contains("Memory", properties);
        Assert.DoesNotContain("Participants", properties);
    }

    [Fact]
    public async Task DiagnosticsQueries_ForwardTypedPayloadsToRuntime()
    {
        var runtime = CreateRuntimeMock();
        GetExecutionTrace? capturedTraceQuery = null;
        ListAttemptSummaries? capturedAttemptQuery = null;

        runtime
            .Setup(item => item.GetExecutionTraceAsync(It.IsAny<GetExecutionTrace>(), It.IsAny<CancellationToken>()))
            .Callback<GetExecutionTrace, CancellationToken>((query, _) => capturedTraceQuery = query)
            .ReturnsAsync(new ExecutionTrace(
                new ExecutionTraceId("trace-runtime-001"),
                new ExecutionId("execution-runtime-001"),
                attempts:
                [
                    new AttemptSummary(new ExecutionId("execution-runtime-001"), 1, true, DateTimeOffset.UtcNow),
                ]));
        runtime
            .Setup(item => item.ListAttemptSummariesAsync(It.IsAny<ListAttemptSummaries>(), It.IsAny<CancellationToken>()))
            .Callback<ListAttemptSummaries, CancellationToken>((query, _) => capturedAttemptQuery = query)
            .ReturnsAsync([
                new AttemptSummary(new ExecutionId("execution-runtime-001"), 2, false, DateTimeOffset.UtcNow)
            ]);

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        var trace = await sut.Diagnostics.GetExecutionTraceAsync(new GetExecutionTrace(new ExecutionTraceId("trace-runtime-001")), CancellationToken.None);
        var attempts = await sut.Diagnostics.ListAttemptSummariesAsync(new ListAttemptSummaries(new ExecutionId("execution-runtime-001")), CancellationToken.None);

        Assert.Equal("trace-runtime-001", capturedTraceQuery?.TraceId.Value);
        Assert.Equal("execution-runtime-001", capturedAttemptQuery?.ExecutionId.Value);
        Assert.NotNull(trace);
        Assert.Single(trace.Attempts);
        Assert.Single(attempts);
    }

    [Fact]
    public async Task IdentityAndMemoryQueries_ForwardTypedPayloadsToRuntime()
    {
        var runtime = CreateRuntimeMock();
        GetAccountProfile? capturedAccountQuery = null;
        ListBoundDevices? capturedDevicesQuery = null;
        ListMemorySpaces? capturedSpacesQuery = null;
        ResolveMemoryOverlay? capturedOverlayQuery = null;
        var accountId = new AccountId("local-account:semi");

        runtime
            .Setup(item => item.GetAccountProfileAsync(It.IsAny<GetAccountProfile>(), It.IsAny<CancellationToken>()))
            .Callback<GetAccountProfile, CancellationToken>((query, _) => capturedAccountQuery = query)
            .ReturnsAsync(new Account(accountId, "Example"));
        runtime
            .Setup(item => item.ListBoundDevicesAsync(It.IsAny<ListBoundDevices>(), It.IsAny<CancellationToken>()))
            .Callback<ListBoundDevices, CancellationToken>((query, _) => capturedDevicesQuery = query)
            .ReturnsAsync([
                new DeviceBinding(new DeviceId("device:semi"), accountId, "Example-PC", "Windows")
            ]);
        runtime
            .Setup(item => item.ListMemorySpacesAsync(It.IsAny<ListMemorySpaces>(), It.IsAny<CancellationToken>()))
            .Callback<ListMemorySpaces, CancellationToken>((query, _) => capturedSpacesQuery = query)
            .ReturnsAsync([
                new MemorySpace(new MemorySpaceId("memory:user:semi"), MemoryScopeKind.User, accountId.Value, "User Memory", true)
            ]);
        runtime
            .Setup(item => item.ResolveMemoryOverlayAsync(It.IsAny<ResolveMemoryOverlay>(), It.IsAny<CancellationToken>()))
            .Callback<ResolveMemoryOverlay, CancellationToken>((query, _) => capturedOverlayQuery = query)
            .ReturnsAsync(new MemoryOverlay(
                Facts:
                [
                    new FactMemoryRecord("identity.account_id", StructuredValue.FromString(accountId.Value), new MemorySpaceId("memory:user:semi")),
                ],
                HabitProfile: new HabitProfile(accountId, ["shell_command"], "high"),
                MergeDecision: MemoryMergeDecision.Applied));

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        var account = await sut.GetAccountProfileAsync(new GetAccountProfile(accountId), CancellationToken.None);
        var devices = await sut.ListBoundDevicesAsync(new ListBoundDevices(accountId), CancellationToken.None);
        var spaces = await sut.ListMemorySpacesAsync(new ListMemorySpaces(MemoryScopeKind.User), CancellationToken.None);
        var overlay = await sut.ResolveMemoryOverlayAsync(new ResolveMemoryOverlay(new MemorySpaceId("memory:user:semi")), CancellationToken.None);

        Assert.Equal(accountId.Value, capturedAccountQuery?.AccountId.Value);
        Assert.Equal(accountId.Value, capturedDevicesQuery?.AccountId.Value);
        Assert.Equal(MemoryScopeKind.User, capturedSpacesQuery?.ScopeKind);
        Assert.Equal("memory:user:semi", capturedOverlayQuery?.MemorySpaceId?.Value);
        Assert.Equal("Example", account?.DisplayName);
        Assert.Single(devices);
        Assert.Single(spaces);
        Assert.Single(overlay.Facts);
        Assert.Equal("high", overlay.HabitProfile?.PreferredVerbosity);
    }

    [Fact]
    public async Task IdentityAndMemoryCommands_ForwardTypedPayloadsToRuntime()
    {
        var runtime = CreateRuntimeMock();
        ListMemoryProviders? capturedProvidersQuery = null;
        FilterMemory? capturedFilterQuery = null;
        ListMemoryReviews? capturedReviewListQuery = null;
        AddMemory? capturedAddCommand = null;
        ExtractMemory? capturedExtractCommand = null;
        ImportMemory? capturedImportCommand = null;
        ExportMemory? capturedExportCommand = null;
        BindMemoryProvider? capturedBindCommand = null;
        ForgetMemory? capturedForgetCommand = null;
        DeleteMemory? capturedDeleteCommand = null;
        ApproveMemoryReview? capturedReviewApproveCommand = null;
        DemoteMemoryReview? capturedReviewDemoteCommand = null;
        MergeMemoryReview? capturedReviewMergeCommand = null;
        RestoreMemoryReview? capturedReviewRestoreCommand = null;
        RecordMemoryFeedback? capturedFeedbackCommand = null;
        RecordMemoryCitation? capturedCitationCommand = null;
        var memorySpaceId = new MemorySpaceId("memory:user:semi");
        var memoryRecordId = new MemoryRecordId("memory-record:semi");
        var targetMemoryRecordId = new MemoryRecordId("memory-record:target");

        runtime
            .Setup(item => item.ListMemoryProvidersAsync(It.IsAny<ListMemoryProviders>(), It.IsAny<CancellationToken>()))
            .Callback<ListMemoryProviders, CancellationToken>((query, _) => capturedProvidersQuery = query)
            .ReturnsAsync([
                new MemoryProviderDescriptor(
                    "provider-local",
                    "Local Provider",
                    "1.0",
                    MemoryProviderCapability.Add | MemoryProviderCapability.Filter,
                    [MemoryScopeKind.User])
            ]);
        runtime
            .Setup(item => item.FilterMemoryAsync(It.IsAny<FilterMemory>(), It.IsAny<CancellationToken>()))
            .Callback<FilterMemory, CancellationToken>((query, _) => capturedFilterQuery = query)
            .ReturnsAsync(new MemoryQueryResult([
                new FactMemoryRecord("preference.shell", StructuredValue.FromString("pwsh"), memorySpaceId, id: memoryRecordId)
            ]));
        runtime
            .Setup(item => item.ListMemoryReviewsAsync(It.IsAny<ListMemoryReviews>(), It.IsAny<CancellationToken>()))
            .Callback<ListMemoryReviews, CancellationToken>((query, _) => capturedReviewListQuery = query)
            .ReturnsAsync(new MemoryReviewQueryResult([
                new MemoryReviewItem(new FactMemoryRecord("preference.shell", StructuredValue.FromString("pwsh"), memorySpaceId, id: memoryRecordId))
            ]));
        runtime
            .Setup(item => item.AddMemoryAsync(It.IsAny<AddMemory>(), It.IsAny<CancellationToken>()))
            .Callback<AddMemory, CancellationToken>((command, _) => capturedAddCommand = command)
            .ReturnsAsync(new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.Active, Effect: MemoryMutationEffect.Upserted));
        runtime
            .Setup(item => item.ExtractMemoryAsync(It.IsAny<ExtractMemory>(), It.IsAny<CancellationToken>()))
            .Callback<ExtractMemory, CancellationToken>((command, _) => capturedExtractCommand = command)
            .ReturnsAsync([
                new MemoryCandidate("preference.shell", StructuredValue.FromString("pwsh"), memorySpaceId, extractionReason: "typed-forward")
            ]);
        runtime
            .Setup(item => item.ImportMemoryAsync(It.IsAny<ImportMemory>(), It.IsAny<CancellationToken>()))
            .Callback<ImportMemory, CancellationToken>((command, _) => capturedImportCommand = command)
            .ReturnsAsync(new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.Active, Effect: MemoryMutationEffect.Upserted));
        runtime
            .Setup(item => item.ExportMemoryAsync(It.IsAny<ExportMemory>(), It.IsAny<CancellationToken>()))
            .Callback<ExportMemory, CancellationToken>((command, _) => capturedExportCommand = command)
            .ReturnsAsync(new MemoryQueryResult([
                new FactMemoryRecord("preference.shell", StructuredValue.FromString("pwsh"), memorySpaceId, id: memoryRecordId)
            ]));
        runtime
            .Setup(item => item.BindMemoryProviderAsync(It.IsAny<BindMemoryProvider>(), It.IsAny<CancellationToken>()))
            .Callback<BindMemoryProvider, CancellationToken>((command, _) => capturedBindCommand = command)
            .ReturnsAsync(new MemoryMutationResult(true));
        runtime
            .Setup(item => item.ForgetMemoryAsync(It.IsAny<ForgetMemory>(), It.IsAny<CancellationToken>()))
            .Callback<ForgetMemory, CancellationToken>((command, _) => capturedForgetCommand = command)
            .ReturnsAsync(new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.Forgotten, Effect: MemoryMutationEffect.LifecycleChanged));
        runtime
            .Setup(item => item.DeleteMemoryAsync(It.IsAny<DeleteMemory>(), It.IsAny<CancellationToken>()))
            .Callback<DeleteMemory, CancellationToken>((command, _) => capturedDeleteCommand = command)
            .ReturnsAsync(new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.Deleted, Effect: MemoryMutationEffect.LifecycleChanged));
        runtime
            .Setup(item => item.ApproveMemoryReviewAsync(It.IsAny<ApproveMemoryReview>(), It.IsAny<CancellationToken>()))
            .Callback<ApproveMemoryReview, CancellationToken>((command, _) => capturedReviewApproveCommand = command)
            .ReturnsAsync(new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.Active, Effect: MemoryMutationEffect.LifecycleChanged));
        runtime
            .Setup(item => item.DemoteMemoryReviewAsync(It.IsAny<DemoteMemoryReview>(), It.IsAny<CancellationToken>()))
            .Callback<DemoteMemoryReview, CancellationToken>((command, _) => capturedReviewDemoteCommand = command)
            .ReturnsAsync(new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.Archived, Effect: MemoryMutationEffect.LifecycleChanged));
        runtime
            .Setup(item => item.MergeMemoryReviewAsync(It.IsAny<MergeMemoryReview>(), It.IsAny<CancellationToken>()))
            .Callback<MergeMemoryReview, CancellationToken>((command, _) => capturedReviewMergeCommand = command)
            .ReturnsAsync(new MemoryMutationResult(true, targetMemoryRecordId, MemoryLifecycleStatus.Active, Effect: MemoryMutationEffect.Superseded));
        runtime
            .Setup(item => item.RestoreMemoryReviewAsync(It.IsAny<RestoreMemoryReview>(), It.IsAny<CancellationToken>()))
            .Callback<RestoreMemoryReview, CancellationToken>((command, _) => capturedReviewRestoreCommand = command)
            .ReturnsAsync(new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.PendingReview, Effect: MemoryMutationEffect.LifecycleChanged));
        runtime
            .Setup(item => item.RecordMemoryFeedbackAsync(It.IsAny<RecordMemoryFeedback>(), It.IsAny<CancellationToken>()))
            .Callback<RecordMemoryFeedback, CancellationToken>((command, _) => capturedFeedbackCommand = command)
            .ReturnsAsync(new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.Active));
        runtime
            .Setup(item => item.RecordMemoryCitationAsync(It.IsAny<RecordMemoryCitation>(), It.IsAny<CancellationToken>()))
            .Callback<RecordMemoryCitation, CancellationToken>((command, _) => capturedCitationCommand = command)
            .ReturnsAsync(new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.Active));

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var citation = new MemoryCitation([
            new MemoryCitationEntry(memoryRecordId, memorySpaceId, "preference.shell")
        ]);

        var providers = await sut.ListMemoryProvidersAsync(new ListMemoryProviders(MemoryScopeKind.User), CancellationToken.None);
        var filterResult = await sut.FilterMemoryAsync(new FilterMemory(MemorySpaceId: memorySpaceId), CancellationToken.None);
        var reviewListResult = await sut.ListMemoryReviewsAsync(new ListMemoryReviews(memorySpaceId), CancellationToken.None);
        var addResult = await sut.AddMemoryAsync(new AddMemory(memorySpaceId, "preference.shell", StructuredValue.FromString("pwsh")), CancellationToken.None);
        var extractResult = await sut.ExtractMemoryAsync(
            new ExtractMemory(memorySpaceId, new MemorySourceRef(MemorySourceKind.Conversation, "turn-001"), StructuredValue.FromString("记住我更喜欢 pwsh")),
            CancellationToken.None);
        var importResult = await sut.ImportMemoryAsync(
            new ImportMemory(memorySpaceId, new MemorySourceRef(MemorySourceKind.File, "memory.json")),
            CancellationToken.None);
        var exportResult = await sut.ExportMemoryAsync(new ExportMemory(memorySpaceId), CancellationToken.None);
        var bindResult = await sut.BindMemoryProviderAsync(
            new BindMemoryProvider(
                "provider-local",
                memorySpaceId,
                MemoryProviderBindingMode.ReadWrite,
                MemoryProviderCapability.Add | MemoryProviderCapability.Filter),
            CancellationToken.None);
        var forgetResult = await sut.ForgetMemoryAsync(new ForgetMemory(memoryRecordId), CancellationToken.None);
        var deleteResult = await sut.DeleteMemoryAsync(new DeleteMemory(memoryRecordId, Reason: "cleanup"), CancellationToken.None);
        var reviewApproveResult = await sut.ApproveMemoryReviewAsync(
            new ApproveMemoryReview(memoryRecordId, memorySpaceId, "preference.shell", "accepted"),
            CancellationToken.None);
        var reviewDemoteResult = await sut.DemoteMemoryReviewAsync(
            new DemoteMemoryReview(memoryRecordId, memorySpaceId, "preference.shell", "low-confidence"),
            CancellationToken.None);
        var reviewMergeResult = await sut.MergeMemoryReviewAsync(
            new MergeMemoryReview(memoryRecordId, targetMemoryRecordId, memorySpaceId, "duplicate"),
            CancellationToken.None);
        var reviewRestoreResult = await sut.RestoreMemoryReviewAsync(
            new RestoreMemoryReview(memoryRecordId, memorySpaceId, "preference.shell", "retry"),
            CancellationToken.None);
        var feedbackResult = await sut.RecordMemoryFeedbackAsync(
            new RecordMemoryFeedback(memoryRecordId, MemoryMergeDecision.Applied, "accepted"),
            CancellationToken.None);
        var citationResult = await sut.RecordMemoryCitationAsync(new RecordMemoryCitation(citation), CancellationToken.None);

        Assert.Equal(MemoryScopeKind.User, capturedProvidersQuery?.ScopeKind);
        Assert.Equal(memorySpaceId.Value, capturedFilterQuery?.MemorySpaceId?.Value);
        Assert.Equal(memorySpaceId.Value, capturedReviewListQuery?.MemorySpaceId?.Value);
        Assert.Equal("preference.shell", capturedAddCommand?.Key);
        Assert.Equal(memorySpaceId.Value, capturedExtractCommand?.MemorySpaceId.Value);
        Assert.Equal(memorySpaceId.Value, capturedImportCommand?.MemorySpaceId.Value);
        Assert.Equal(memorySpaceId.Value, capturedExportCommand?.MemorySpaceId.Value);
        Assert.Equal("provider-local", capturedBindCommand?.ProviderId);
        Assert.Equal(memoryRecordId, capturedForgetCommand?.MemoryRecordId);
        Assert.Equal("cleanup", capturedDeleteCommand?.Reason);
        Assert.Equal(memoryRecordId, capturedReviewApproveCommand?.MemoryRecordId);
        Assert.Equal("accepted", capturedReviewApproveCommand?.Reason);
        Assert.Equal(memoryRecordId, capturedReviewDemoteCommand?.MemoryRecordId);
        Assert.Equal("low-confidence", capturedReviewDemoteCommand?.Reason);
        Assert.Equal(memoryRecordId, capturedReviewMergeCommand?.ReviewRecordId);
        Assert.Equal(targetMemoryRecordId, capturedReviewMergeCommand?.TargetRecordId);
        Assert.Equal(memoryRecordId, capturedReviewRestoreCommand?.MemoryRecordId);
        Assert.Equal("retry", capturedReviewRestoreCommand?.Reason);
        Assert.Equal("accepted", capturedFeedbackCommand?.Feedback);
        Assert.Equal(memoryRecordId, Assert.Single(capturedCitationCommand?.Citation.Entries ?? []).MemoryRecordId);
        Assert.Single(providers);
        Assert.Single(filterResult.Records);
        Assert.Single(reviewListResult.Items);
        Assert.Equal(memoryRecordId, addResult.RecordId);
        Assert.Single(extractResult);
        Assert.True(importResult.Success);
        Assert.Single(exportResult.Records);
        Assert.True(bindResult.Success);
        Assert.Equal(MemoryLifecycleStatus.Forgotten, forgetResult.LifecycleStatus);
        Assert.Equal(MemoryLifecycleStatus.Deleted, deleteResult.LifecycleStatus);
        Assert.Equal(MemoryLifecycleStatus.Active, reviewApproveResult.LifecycleStatus);
        Assert.Equal(MemoryLifecycleStatus.Archived, reviewDemoteResult.LifecycleStatus);
        Assert.Equal(targetMemoryRecordId, reviewMergeResult.RecordId);
        Assert.Equal(MemoryLifecycleStatus.PendingReview, reviewRestoreResult.LifecycleStatus);
        Assert.True(feedbackResult.Success);
        Assert.True(citationResult.Success);
    }

    [Fact]
    public async Task CollaborationCommands_MapTypedPayloadsToRuntime()
    {
        var runtime = CreateRuntimeMock();
        CreateCollaborationSpace? capturedCreate = null;
        ConfigureCollaborationSpace? capturedConfigure = null;
        ArchiveCollaborationSpace? capturedArchive = null;
        GetCollaborationSpaceOverview? capturedOverview = null;
        GetCollaborationSpaceProjection? capturedSpaceProjection = null;
        ListCollaborationSpaces? capturedList = null;
        BindParticipantToSession? capturedBindSession = null;
        BindParticipantToWorkflow? capturedBindWorkflow = null;
        UpdateParticipantRole? capturedUpdateRole = null;
        GetParticipantProjection? capturedParticipantProjection = null;
        GetParticipantViewProjection? capturedParticipantViewProjection = null;
        ListParticipantsInScope? capturedParticipantsInScope = null;

        var createdSpace = new CollaborationSpace(
            new CollaborationSpaceId("space-created"),
            "design",
            "Design",
            new CollaborationSpaceProfile("设计"),
            CollaborationDefaultSet.Empty);
        var configuredSpace = new CollaborationSpace(
            createdSpace.Id,
            createdSpace.Key,
            "Design Updated",
            createdSpace.Profile,
            createdSpace.Defaults,
            createdSpace.PolicyRef,
            createdSpace.IsArchived);
        var overviewProjection = new CollaborationSpaceOverviewProjection(createdSpace.Id, createdSpace.Key, createdSpace.DisplayName, false);
        var collaborationViewProjection = new TianShu.Contracts.Projections.CollaborationSpaceProjection(
            new CollaborationSpaceRef(createdSpace.Id, createdSpace.Key, createdSpace.DisplayName),
            ActiveSessionCount: 0,
            ActiveThreadCount: 0,
            IsArchived: false);
        var participantProjection = new ControlPlaneParticipantProjection(new ParticipantId("participant-1"), ParticipantKind.Agent, "Planner", "owner");
        var participantViewProjection = new TianShu.Contracts.Projections.ParticipantProjection(
            new ParticipantRef(new ParticipantId("participant-1"), ParticipantKind.Agent, "Planner"),
            ScopeKind: "participant",
            ScopeKey: "participant-1",
            Role: "owner",
            IsActive: true);

        runtime
            .Setup(item => item.CreateSpaceAsync(It.IsAny<CreateCollaborationSpace>(), It.IsAny<CancellationToken>()))
            .Callback<CreateCollaborationSpace, CancellationToken>((command, _) => capturedCreate = command)
            .ReturnsAsync(createdSpace);
        runtime
            .Setup(item => item.ConfigureSpaceAsync(It.IsAny<ConfigureCollaborationSpace>(), It.IsAny<CancellationToken>()))
            .Callback<ConfigureCollaborationSpace, CancellationToken>((command, _) => capturedConfigure = command)
            .ReturnsAsync(configuredSpace);
        runtime
            .Setup(item => item.ArchiveSpaceAsync(It.IsAny<ArchiveCollaborationSpace>(), It.IsAny<CancellationToken>()))
            .Callback<ArchiveCollaborationSpace, CancellationToken>((command, _) => capturedArchive = command)
            .ReturnsAsync(true);
        runtime
            .Setup(item => item.GetSpaceOverviewAsync(It.IsAny<GetCollaborationSpaceOverview>(), It.IsAny<CancellationToken>()))
            .Callback<GetCollaborationSpaceOverview, CancellationToken>((query, _) => capturedOverview = query)
            .ReturnsAsync(overviewProjection);
        runtime
            .Setup(item => item.GetSpaceProjectionAsync(It.IsAny<GetCollaborationSpaceProjection>(), It.IsAny<CancellationToken>()))
            .Callback<GetCollaborationSpaceProjection, CancellationToken>((query, _) => capturedSpaceProjection = query)
            .ReturnsAsync(collaborationViewProjection);
        runtime
            .Setup(item => item.ListSpacesAsync(It.IsAny<ListCollaborationSpaces>(), It.IsAny<CancellationToken>()))
            .Callback<ListCollaborationSpaces, CancellationToken>((query, _) => capturedList = query)
            .ReturnsAsync([overviewProjection]);
        runtime
            .Setup(item => item.BindParticipantToSessionAsync(It.IsAny<BindParticipantToSession>(), It.IsAny<CancellationToken>()))
            .Callback<BindParticipantToSession, CancellationToken>((command, _) => capturedBindSession = command)
            .ReturnsAsync(true);
        runtime
            .Setup(item => item.BindParticipantToWorkflowAsync(It.IsAny<BindParticipantToWorkflow>(), It.IsAny<CancellationToken>()))
            .Callback<BindParticipantToWorkflow, CancellationToken>((command, _) => capturedBindWorkflow = command)
            .ReturnsAsync(true);
        runtime
            .Setup(item => item.UpdateParticipantRoleAsync(It.IsAny<UpdateParticipantRole>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateParticipantRole, CancellationToken>((command, _) => capturedUpdateRole = command)
            .ReturnsAsync(true);
        runtime
            .Setup(item => item.GetParticipantProjectionAsync(It.IsAny<GetParticipantProjection>(), It.IsAny<CancellationToken>()))
            .Callback<GetParticipantProjection, CancellationToken>((query, _) => capturedParticipantProjection = query)
            .ReturnsAsync(participantProjection);
        runtime
            .Setup(item => item.GetParticipantViewProjectionAsync(It.IsAny<GetParticipantViewProjection>(), It.IsAny<CancellationToken>()))
            .Callback<GetParticipantViewProjection, CancellationToken>((query, _) => capturedParticipantViewProjection = query)
            .ReturnsAsync(participantViewProjection);
        runtime
            .Setup(item => item.ListParticipantsInScopeAsync(It.IsAny<ListParticipantsInScope>(), It.IsAny<CancellationToken>()))
            .Callback<ListParticipantsInScope, CancellationToken>((query, _) => capturedParticipantsInScope = query)
            .ReturnsAsync([participantProjection]);

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        var created = await sut.CreateSpaceAsync(
            new CreateCollaborationSpace(
                new CollaborationSpaceId("space-created"),
                "design",
                "Design",
                new CollaborationSpaceProfile("设计"),
                CollaborationDefaultSet.Empty),
            CancellationToken.None);
        var configured = await sut.ConfigureSpaceAsync(
            new ConfigureCollaborationSpace(new CollaborationSpaceId("space-created"), DisplayName: "Design Updated"),
            CancellationToken.None);
        var archived = await sut.ArchiveSpaceAsync(new ArchiveCollaborationSpace(new CollaborationSpaceId("space-created")), CancellationToken.None);
        var overview = await sut.GetSpaceOverviewAsync(new GetCollaborationSpaceOverview(new CollaborationSpaceId("space-created")), CancellationToken.None);
        var projection = await sut.GetSpaceProjectionAsync(new GetCollaborationSpaceProjection(new CollaborationSpaceId("space-created")), CancellationToken.None);
        var spaces = await sut.ListSpacesAsync(new ListCollaborationSpaces(IncludeArchived: true), CancellationToken.None);
        var sessionBound = await sut.BindParticipantToSessionAsync(new BindParticipantToSession(new SessionId("session-1"), new ParticipantId("participant-1")), CancellationToken.None);
        var workflowBound = await sut.BindParticipantToWorkflowAsync(new BindParticipantToWorkflow(new WorkflowId("workflow-1"), new ParticipantId("participant-1")), CancellationToken.None);
        var roleUpdated = await sut.UpdateParticipantRoleAsync(new UpdateParticipantRole(new ParticipantId("participant-1"), "owner"), CancellationToken.None);
        var participant = await sut.GetParticipantProjectionAsync(new GetParticipantProjection(new ParticipantId("participant-1")), CancellationToken.None);
        var participantView = await sut.GetParticipantViewProjectionAsync(new GetParticipantViewProjection(new ParticipantId("participant-1")), CancellationToken.None);
        var participants = await sut.ListParticipantsInScopeAsync(new ListParticipantsInScope(new CollaborationSpaceId("space-created")), CancellationToken.None);

        Assert.Equal("space-created", capturedCreate?.SpaceId.Value);
        Assert.Equal("Design Updated", capturedConfigure?.DisplayName);
        Assert.Equal("space-created", capturedArchive?.SpaceId.Value);
        Assert.Equal("space-created", capturedOverview?.SpaceId.Value);
        Assert.Equal("space-created", capturedSpaceProjection?.SpaceId.Value);
        Assert.True(capturedList?.IncludeArchived);
        Assert.Equal("session-1", capturedBindSession?.SessionId.Value);
        Assert.Equal("workflow-1", capturedBindWorkflow?.WorkflowId.Value);
        Assert.Equal("owner", capturedUpdateRole?.Role);
        Assert.Equal("participant-1", capturedParticipantProjection?.ParticipantId.Value);
        Assert.Equal("participant-1", capturedParticipantViewProjection?.ParticipantId.Value);
        Assert.Equal("space-created", capturedParticipantsInScope?.CollaborationSpaceId.Value);
        Assert.Equal("Design", created.DisplayName);
        Assert.Equal("Design Updated", configured.DisplayName);
        Assert.True(archived);
        Assert.Equal("Design", overview?.DisplayName);
        Assert.Equal("Design", projection?.CollaborationSpace.DisplayName);
        Assert.Single(spaces);
        Assert.True(sessionBound);
        Assert.True(workflowBound);
        Assert.True(roleUpdated);
        Assert.Equal("Planner", participant?.DisplayName);
        Assert.Equal("owner", participantView?.Role);
        Assert.Single(participants);
    }

    [Fact]
    public async Task GovernanceCommands_MapTypedPayloadsToRuntime()
    {
        var runtime = CreateRuntimeMock();
        ControlPlaneApprovalResolution? capturedApprovalResolution = null;
        ControlPlanePermissionGrant? capturedPermissionGrant = null;
        ControlPlaneUserInputSubmission? capturedUserInputSubmission = null;
        runtime
            .Setup(static item => item.RespondToApprovalAsync(It.IsAny<ControlPlaneApprovalResolution>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneApprovalResolution, CancellationToken>((command, _) => capturedApprovalResolution = command)
            .ReturnsAsync(true);
        runtime
            .Setup(static item => item.RespondToPermissionRequestAsync(It.IsAny<ControlPlanePermissionGrant>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlanePermissionGrant, CancellationToken>((command, _) => capturedPermissionGrant = command)
            .ReturnsAsync(true);
        runtime
            .Setup(static item => item.RespondToUserInputAsync(It.IsAny<ControlPlaneUserInputSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneUserInputSubmission, CancellationToken>((command, _) => capturedUserInputSubmission = command)
            .ReturnsAsync(true);

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var approvalAccepted = await sut.ResolveApprovalAsync(
            new ControlPlaneApprovalResolution
            {
                Envelope = new InteractionEnvelope(
                    new InteractionEnvelopeId("interaction-approval-runtime-1"),
                    new InteractionSource(InteractionSourceKind.Host, "sidecar"),
                    [new StructuredInteractionItem("approval_response", StructuredValue.FromString("approve"))]),
                CallId = new CallId("approval-call-1"),
                Decision = ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment,
                Note = "allow git status",
                CommandPrefix = ["git", "status"],
            },
            CancellationToken.None);
        var permissionAccepted = await sut.ResolvePermissionRequestAsync(
            new ControlPlanePermissionGrant
            {
                Envelope = new InteractionEnvelope(
                    new InteractionEnvelopeId("interaction-permission-runtime-1"),
                    new InteractionSource(InteractionSourceKind.Host, "sidecar"),
                    [new StructuredInteractionItem("permission_response", StructuredValue.FromString("session"))]),
                CallId = new CallId("permission-call-1"),
                Permissions = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["network"] = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["http"] = StructuredValue.FromBoolean(true),
                        ["host"] = StructuredValue.FromString("api.openai.com"),
                    }),
                },
                Scope = ControlPlanePermissionScope.Session,
            },
            CancellationToken.None);
        var userInputAccepted = await sut.SubmitUserInputAsync(
            new ControlPlaneUserInputSubmission
            {
                Envelope = new InteractionEnvelope(
                    new InteractionEnvelopeId("interaction-userinput-runtime-1"),
                    new InteractionSource(InteractionSourceKind.Host, "sidecar"),
                    [new StructuredInteractionItem("user_input_submission", StructuredValue.FromString("A"))]),
                CallId = new CallId("input-call-1"),
                Answers = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["choice"] = StructuredValue.FromString("A"),
                    ["count"] = StructuredValue.FromNumber("2"),
                },
            },
            CancellationToken.None);

        Assert.True(approvalAccepted);
        Assert.True(permissionAccepted);
        Assert.True(userInputAccepted);

        var approvalResolution = Assert.IsType<ControlPlaneApprovalResolution>(capturedApprovalResolution);
        Assert.Equal("approval-call-1", approvalResolution.CallId.Value);
        Assert.Equal("interaction-approval-runtime-1", approvalResolution.Envelope?.Id.Value);
        Assert.Equal(ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment, approvalResolution.Decision);
        Assert.Equal("allow git status", approvalResolution.Note);
        Assert.Equal(["git", "status"], approvalResolution.CommandPrefix);

        var permissionGrant = Assert.IsType<ControlPlanePermissionGrant>(capturedPermissionGrant);
        Assert.Equal("permission-call-1", permissionGrant.CallId.Value);
        Assert.Equal("interaction-permission-runtime-1", permissionGrant.Envelope?.Id.Value);
        Assert.Equal(ControlPlanePermissionScope.Session, permissionGrant.Scope);
        var permissionPayload = Assert.IsType<StructuredValue>(permissionGrant.Permissions["network"]);
        Assert.Equal(StructuredValueKind.Object, permissionPayload.Kind);
        Assert.True(permissionPayload.Properties["http"].BooleanValue ?? false);
        Assert.Equal("api.openai.com", permissionPayload.Properties["host"].StringValue);

        var userInputSubmission = Assert.IsType<ControlPlaneUserInputSubmission>(capturedUserInputSubmission);
        Assert.Equal("input-call-1", userInputSubmission.CallId.Value);
        Assert.Equal("interaction-userinput-runtime-1", userInputSubmission.Envelope?.Id.Value);
        Assert.Equal("A", userInputSubmission.Answers["choice"].StringValue);
        Assert.Equal("2", userInputSubmission.Answers["count"].NumberValue);
    }

    [Fact]
    public async Task ReadConfigAsync_AndListModelsAsync_MapCatalogPayloads()
    {
        var runtime = CreateRuntimeMock();
        runtime
            .Setup(static item => item.ReadConfigAsync(It.IsAny<ControlPlaneConfigReadQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneConfigSnapshotResult
            {
                Config = StructuredValue.FromPlainObject(new Dictionary<string, object?>
                {
                    ["model"] = "gpt-5",
                    ["provider"] = "openai",
                }),
                Origins = new Dictionary<string, ControlPlaneConfigOrigin>(StringComparer.Ordinal)
                {
                    ["model"] = new ControlPlaneConfigOrigin
                    {
                        Type = "workspace",
                        File = @"D:\repo\.tianshu\tianshu.toml",
                        Version = "v1",
                    },
                },
                Fields =
                [
                    new ControlPlaneConfigField
                    {
                        KeyPath = "model",
                        ValueKind = "string",
                        ValueText = "gpt-5",
                        Value = StructuredValue.FromString("gpt-5"),
                        SourceType = "workspace",
                        SourcePath = @"D:\repo\.tianshu\tianshu.toml",
                        SourceText = "工作区配置",
                    },
                ],
                Layers =
                [
                    new ControlPlaneConfigLayer
                    {
                        Name = StructuredValue.FromPlainObject(new Dictionary<string, object?>
                        {
                            ["type"] = "workspace",
                        }),
                        Version = "v1",
                        Config = StructuredValue.FromPlainObject(new Dictionary<string, object?>
                        {
                            ["model"] = "gpt-5",
                        }),
                    },
                ],
            });
        runtime
            .Setup(static item => item.ListModelsAsync(It.IsAny<ControlPlaneModelCatalogQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneModelCatalogResult
            {
                NextCursor = "model-cursor-2",
                Items =
                [
                    new ControlPlaneModelCatalogItem
                    {
                        Id = "catalog-1",
                        Model = "gpt-5",
                        DisplayName = "GPT-5",
                        DefaultReasoningEffort = "medium",
                        SupportedReasoningEfforts = ["low", "medium", "high"],
                        InputModalities = ["text", "image"],
                        SupportsPersonality = true,
                        Hidden = false,
                        IsDefault = true,
                        SupportsParallelToolCalls = true,
                        SupportsReasoningSummaries = true,
                        DefaultReasoningSummary = "auto",
                        SupportsVerbosity = true,
                        DefaultVerbosity = "medium",
                        PreferWebsocketTransport = true,
                        Description = "旗舰模型",
                    },
                ],
            });

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        var config = await sut.ReadConfigAsync(
            new ControlPlaneConfigReadQuery
            {
                WorkingDirectory = @"D:\repo",
                IncludeLayers = true,
            },
            CancellationToken.None);
        var models = await sut.ListModelsAsync(
            new ControlPlaneModelCatalogQuery
            {
                Limit = 20,
                Cursor = "model-cursor-1",
                IncludeHidden = true,
            },
            CancellationToken.None);

        Assert.Equal("gpt-5", config.Config?.Properties["model"].StringValue);
        Assert.Equal("openai", config.Config?.Properties["provider"].StringValue);
        Assert.Equal("workspace", config.Origins["model"].Type);
        Assert.Equal("gpt-5", config.Fields[0].Value?.StringValue);
        Assert.Equal("workspace", config.Layers[0].Name?.Properties["type"].StringValue);
        Assert.Equal("gpt-5", config.Layers[0].Config?.Properties["model"].StringValue);

        Assert.Equal("model-cursor-2", models.NextCursor);
        var model = Assert.Single(models.Items);
        Assert.Equal("catalog-1", model.Id);
        Assert.Equal("gpt-5", model.Model);
        Assert.Equal(["low", "medium", "high"], model.SupportedReasoningEfforts);
        Assert.Equal(["text", "image"], model.InputModalities);
        Assert.True(model.SupportsPersonality);
        Assert.True(model.IsDefault);
        Assert.True(model.SupportsParallelToolCalls);
        Assert.True(model.SupportsReasoningSummaries);
        Assert.Equal("auto", model.DefaultReasoningSummary);
        Assert.True(model.SupportsVerbosity);
        Assert.Equal("medium", model.DefaultVerbosity);
        Assert.True(model.PreferWebsocketTransport);
    }

    [Fact]
    public async Task UninstallPluginAsync_AndReloadMcpServersAsync_MapCatalogCommandResults()
    {
        var runtime = CreateRuntimeMock();
        runtime
            .Setup(static item => item.UninstallPluginAsync(It.IsAny<ControlPlanePluginUninstallCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlanePluginUninstallResult());
        runtime
            .Setup(static item => item.ReloadMcpServersAsync(It.IsAny<ControlPlaneMcpServerReloadCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneMcpServerReloadResult());

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        var uninstall = await sut.UninstallPluginAsync(
            new ControlPlanePluginUninstallCommand
            {
                PluginId = "demo-plugin",
            },
            CancellationToken.None);
        var reload = await sut.ReloadMcpServersAsync(CancellationToken.None);

        Assert.NotNull(uninstall);
        Assert.NotNull(reload);
    }

    [Fact]
    public async Task CatalogAndWorkflowFormalEntries_MapRemoteSkillsAndReviewStart()
    {
        var runtime = CreateRuntimeMock();
        runtime
            .Setup(static item => item.ListRemoteSkillsAsync(It.IsAny<ControlPlaneRemoteSkillCatalogQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneRemoteSkillCatalogResult());
        runtime
            .Setup(static item => item.ExportRemoteSkillAsync(It.IsAny<ControlPlaneRemoteSkillExportCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneRemoteSkillExportResult());
        runtime
            .Setup(static item => item.StartReviewAsync(It.IsAny<ControlPlaneReviewStartCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneReviewStartResult());
        runtime
            .Setup(static item => item.StartMcpServerOauthLoginAsync(It.IsAny<ControlPlaneMcpServerOauthLoginStartCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneMcpServerOauthLoginStartResult());

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        var remoteList = await sut.ListRemoteSkillsAsync(new ControlPlaneRemoteSkillCatalogQuery(), CancellationToken.None);
        var remoteExport = await sut.ExportRemoteSkillAsync(new ControlPlaneRemoteSkillExportCommand(), CancellationToken.None);
        var oauthLogin = await sut.StartMcpServerOauthLoginAsync(new ControlPlaneMcpServerOauthLoginStartCommand(), CancellationToken.None);
        var review = await sut.StartReviewAsync(new ControlPlaneReviewStartCommand(), CancellationToken.None);

        Assert.NotNull(remoteList);
        Assert.NotNull(remoteExport);
        Assert.NotNull(oauthLogin);
        Assert.NotNull(review);
    }

    [Fact]
    public async Task UploadFeedbackAsync_MapsGovernanceFeedbackCommand()
    {
        var runtime = CreateRuntimeMock();
        runtime
            .As<IGovernanceControlPlaneClient>()
            .Setup(static item => item.UploadFeedbackAsync(It.IsAny<ControlPlaneFeedbackUploadCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneFeedbackUploadResult());
        runtime
            .As<IDiagnosticsControlPlaneClient>()
            .Setup(static item => item.UploadFeedbackAsync(It.IsAny<ControlPlaneFeedbackUploadCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneFeedbackUploadResult());

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        var result = await sut.UploadFeedbackAsync(new ControlPlaneFeedbackUploadCommand(), CancellationToken.None);
        var diagnosticsResult = await sut.Diagnostics.UploadFeedbackAsync(new ControlPlaneFeedbackUploadCommand(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(diagnosticsResult);
    }

    [Fact]
    public async Task GetCapabilityCatalogAsync_AndResolveEngineBindingAsync_UseContractsCatalogTypes()
    {
        var runtime = CreateRuntimeMock();
        runtime
            .Setup(static item => item.GetCapabilityCatalogAsync(It.IsAny<GetCapabilityCatalog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityCatalogSnapshot(
                activeProviderKey: "demo",
                activeModel: "gpt-5.4",
                providers:
                [
                    new ProviderProfile(
                        key: "demo",
                        displayName: "Demo Provider",
                        transportFamily: "responses",
                        transportModes: ["responses.http", "responses.websocket"],
                        supportedCapabilities:
                        [
                            new CapabilityDescriptor("websocket_transport", true),
                        ],
                        models:
                        [
                            new ModelProfile(
                                key: "gpt-5.4",
                                model: "gpt-5.4",
                                displayName: "GPT-5.4",
                                description: "演示模型",
                                isDefault: true,
                                defaultReasoningEffort: "medium",
                                supportedReasoningEfforts: ["low", "medium", "high"],
                                inputModalities: ["text", "image"],
                                supportsPersonality: true,
                                supportsParallelToolCalls: true,
                                supportsReasoningSummaries: true,
                                defaultReasoningSummary: "auto",
                                supportsVerbosity: true,
                                defaultVerbosity: "medium",
                                preferWebsocketTransport: true,
                                supportedCapabilities:
                                [
                                    new CapabilityDescriptor(
                                        "reasoning_effort",
                                        true,
                                        ["low", "medium", "high"],
                                        "medium"),
                                ]),
                        ],
                        baseUrl: "https://example.invalid/v1",
                        apiKeyEnvironmentVariable: "DEMO_API_KEY",
                        supportsWebsockets: true),
                ]));
        runtime
            .Setup(static item => item.ResolveEngineBindingAsync(It.IsAny<ResolveEngineBinding>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedEngineBinding(
                binding: new EngineBinding(
                    engineKey: "kernel",
                    providerKey: "demo",
                    modelKey: "gpt-5.4",
                    model: "gpt-5.4",
                    transportFamily: "responses",
                    baseUrl: "https://example.invalid/v1",
                    apiKeyEnvironmentVariable: "DEMO_API_KEY",
                    supportsWebsockets: true,
                    reasoning: new CatalogReasoningProfile("high", "auto", "medium"),
                    streaming: new CatalogStreamingPreference("responses.websocket", preferWebsocketTransport: true, useWebsocketTransport: true),
                    fallbackPlan:
                    [
                        new EngineBindingCandidate(
                            providerKey: "openai",
                            modelKey: "gpt-5.4",
                            model: "gpt-5.4",
                            transportFamily: "responses",
                            transportMode: "responses.http",
                            selectionReason: "fallback",
                            supportsWebsockets: false),
                    ]),
                candidates:
                [
                    new EngineBindingCandidate(
                        providerKey: "demo",
                        modelKey: "gpt-5.4",
                        model: "gpt-5.4",
                        transportFamily: "responses",
                        transportMode: "responses.websocket",
                        selectionReason: "selected",
                        isSelected: true,
                        supportsWebsockets: true),
                    new EngineBindingCandidate(
                        providerKey: "openai",
                        modelKey: "gpt-5.4",
                        model: "gpt-5.4",
                        transportFamily: "responses",
                        transportMode: "responses.http",
                        selectionReason: "fallback",
                        supportsWebsockets: false),
                ]));

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        var providers = await sut.GetCapabilityCatalogAsync(
            new GetCapabilityCatalog(
                workspacePath: @"D:\repo",
                includeHiddenModels: true,
                modelLimit: 80),
            CancellationToken.None);
        var binding = await sut.ResolveEngineBindingAsync(
            new ResolveEngineBinding(
                WorkspacePath: @"D:\repo",
                PreferredProviderKey: "demo",
                PreferredModelKey: "gpt-5.4",
                ReasoningEffort: "high",
                ReasoningSummary: "auto",
                Verbosity: "medium",
                PreferWebsocketTransport: true),
            CancellationToken.None);

        Assert.Equal("demo", providers.ActiveProviderKey);
        Assert.Equal("gpt-5.4", providers.ActiveModel);
        var provider = Assert.Single(providers.Providers);
        Assert.Equal("demo", provider.Key);
        Assert.Equal("Demo Provider", provider.DisplayName);
        Assert.Equal("responses", provider.TransportFamily);
        Assert.Equal("https://example.invalid/v1", provider.BaseUrl);
        Assert.Equal("DEMO_API_KEY", provider.ApiKeyEnvironmentVariable);
        Assert.True(provider.SupportsWebsockets);
        Assert.Equal(["responses.http", "responses.websocket"], provider.TransportModes);
        Assert.Single(provider.SupportedCapabilities);
        var providerModel = Assert.Single(provider.Models);
        Assert.Equal("gpt-5.4", providerModel.Key);
        Assert.Equal("gpt-5.4", providerModel.Model);
        Assert.True(providerModel.SupportsPersonality);
        Assert.True(providerModel.SupportsParallelToolCalls);
        Assert.True(providerModel.SupportsReasoningSummaries);
        Assert.Equal("auto", providerModel.DefaultReasoningSummary);
        Assert.True(providerModel.SupportsVerbosity);
        Assert.Equal("medium", providerModel.DefaultVerbosity);
        Assert.True(providerModel.PreferWebsocketTransport);

        Assert.NotNull(binding.Binding);
        var resolved = binding.Binding!;
        Assert.Equal("demo", resolved.ProviderKey);
        Assert.Equal("gpt-5.4", resolved.Model);
        Assert.Equal("responses", resolved.TransportFamily);
        Assert.Equal("https://example.invalid/v1", resolved.BaseUrl);
        Assert.Equal("DEMO_API_KEY", resolved.ApiKeyEnvironmentVariable);
        Assert.True(resolved.SupportsWebsockets);
        Assert.Equal("high", resolved.Reasoning.Effort);
        Assert.Equal("auto", resolved.Reasoning.Summary);
        Assert.Equal("medium", resolved.Reasoning.Verbosity);
        Assert.Equal("responses.websocket", resolved.Streaming.TransportMode);
        Assert.True(resolved.Streaming.PreferWebsocketTransport);
        Assert.True(resolved.Streaming.UseWebsocketTransport);
        Assert.Single(resolved.FallbackPlan);
        Assert.Equal("openai", resolved.FallbackPlan[0].ProviderKey);
        Assert.Equal(2, binding.Candidates.Count);
        Assert.True(binding.Candidates[0].IsSelected);
    }

    [Fact]
    public async Task ListAgentsAsync_AndRegisterAgentThreadAsync_MapAgentDescriptors()
    {
        var runtime = CreateRuntimeMock();
        runtime
            .Setup(static item => item.ListLoadedThreadsAsync(It.IsAny<ControlPlaneLoadedThreadListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneLoadedThreadListResult
            {
                ThreadIds = [new ThreadId("thread-agent-1"), new ThreadId("thread-root-1")],
                NextCursor = "loaded-2",
            });
        runtime
            .Setup(static item => item.ReadThreadAsync(
                It.Is<ControlPlaneReadThreadQuery>(request => request.ThreadId == new ThreadId("thread-agent-1") && !request.IncludeTurns),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneThreadOperationResult
            {
                Thread = new ControlPlaneThreadDetail
                {
                    ThreadId = new ThreadId("thread-agent-1"),
                    Preview = "agent preview",
                    AgentNickname = "Hermes",
                    AgentRole = "reviewer",
                    Source = ControlPlaneThreadSourceKind.SubAgentThreadSpawn,
                    ParentThreadId = new ThreadId("thread-root-1"),
                    LineageDepth = 2,
                    UpdatedAt = new DateTimeOffset(2026, 4, 8, 2, 0, 0, TimeSpan.Zero),
                    Status = "running",
                    ActiveFlags = ["streaming"],
                },
            });
        runtime
            .Setup(static item => item.ReadThreadAsync(
                It.Is<ControlPlaneReadThreadQuery>(request => request.ThreadId == new ThreadId("thread-root-1") && !request.IncludeTurns),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneThreadOperationResult
            {
                Thread = new ControlPlaneThreadDetail
                {
                    ThreadId = new ThreadId("thread-root-1"),
                    Preview = "root preview",
                    UpdatedAt = new DateTimeOffset(2026, 4, 8, 2, 1, 0, TimeSpan.Zero),
                },
            });
        runtime
            .Setup(static item => item.ListAgentsAsync(It.IsAny<ControlPlaneAgentListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneAgentRosterResult
            {
                Agents =
                [
                    new ControlPlaneAgentDescriptor
                    {
                        ThreadId = new ThreadId("thread-agent-1"),
                        Preview = "agent preview",
                        AgentNickname = "Hermes",
                        AgentRole = "reviewer",
                        Status = "running",
                        ActiveFlags = ["streaming"],
                        Lineage = new ControlPlaneAgentLineage
                        {
                            ParentThreadId = new ThreadId("thread-root-1"),
                            Depth = 2,
                        },
                    },
                ],
                NextCursor = "loaded-2",
            });
        runtime
            .Setup(static item => item.GetAgentRosterProjectionAsync(It.IsAny<GetAgentRoster>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRosterProjection(
            [
                new AgentRosterEntry(
                    new AgentId("thread-agent-1"),
                    new ParticipantRef(new ParticipantId("thread-agent-1"), ParticipantKind.Agent, "Hermes"),
                    "reviewer",
                    2,
                    true),
            ]));
        runtime
            .Setup(static item => item.RegisterAgentThreadAsync(It.IsAny<ControlPlaneRegisterAgentThreadCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneAgentThreadRegistrationResult
            {
                Agent = new ControlPlaneAgentDescriptor
                {
                    ThreadId = new ThreadId("thread-agent-2"),
                    Preview = "registered",
                    AgentNickname = "Athena",
                    AgentRole = "architect",
                    UpdatedAt = new DateTimeOffset(2026, 4, 8, 2, 2, 0, TimeSpan.Zero),
                    Lineage = new ControlPlaneAgentLineage
                    {
                        ParentThreadId = new ThreadId("thread-root-1"),
                        Depth = 1,
                    },
                },
            });

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var listResult = await sut.ListAgentsAsync(new ControlPlaneAgentListQuery(), CancellationToken.None);
        var rosterProjection = await sut.GetAgentRosterProjectionAsync(new GetAgentRoster(), CancellationToken.None);
        var registerResult = await sut.RegisterAgentThreadAsync(
            new ControlPlaneRegisterAgentThreadCommand
            {
                ThreadId = new ThreadId("thread-agent-2"),
                AgentNickname = "Athena",
                AgentRole = "architect",
            },
            CancellationToken.None);

        Assert.Equal("loaded-2", listResult.NextCursor);
        var agent = Assert.Single(listResult.Agents);
        Assert.Equal("thread-agent-1", agent.ThreadId.Value);
        Assert.Equal("Hermes", agent.AgentNickname);
        Assert.Equal("reviewer", agent.AgentRole);
        Assert.Equal("thread-root-1", agent.Lineage?.ParentThreadId?.Value);
        Assert.Equal(2, agent.Lineage?.Depth);
        Assert.Equal("running", agent.Status);
        Assert.NotNull(rosterProjection);
        var rosterEntry = Assert.Single(rosterProjection!.Items);
        Assert.Equal("thread-agent-1", rosterEntry.AgentId.Value);
        Assert.Equal("Hermes", rosterEntry.Participant.DisplayName);
        Assert.Equal("reviewer", rosterEntry.Role);
        Assert.True(rosterEntry.IsBusy);

        Assert.NotNull(registerResult.Agent);
        Assert.Equal("thread-agent-2", registerResult.Agent!.ThreadId.Value);
        Assert.Equal("Athena", registerResult.Agent.AgentNickname);
        Assert.Equal("architect", registerResult.Agent.AgentRole);
    }

    [Fact]
    public async Task ThreadWorkflowArtifactAndGovernanceProjectionQueries_WhenRuntimeBackendsNotMaterialized_ReturnNullOrEmpty()
    {
        var runtime = CreateRuntimeMock();
        runtime
            .Setup(static item => item.GetThreadProjectionAsync(It.IsAny<GetThreadProjection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TianShu.Contracts.Projections.ThreadProjection?)null);
        runtime
            .Setup(static item => item.GetWorkflowBoardProjectionAsync(It.IsAny<GetWorkflowBoard>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowBoardProjection?)null);
        runtime
            .Setup(static item => item.GetTaskBoardProjectionAsync(It.IsAny<GetTaskBoard>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskBoardProjection?)null);
        runtime
            .Setup(static item => item.GetPlanProjectionAsync(It.IsAny<GetPlanProjection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanProjection?)null);
        runtime
            .Setup(static item => item.GetTeamProjectionAsync(It.IsAny<GetTeamProjection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TeamProjection?)null);
        runtime
            .Setup(static item => item.GetApprovalQueueProjectionAsync(It.IsAny<ListPendingApprovals>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApprovalQueueProjection?)null);
        runtime
            .Setup(static item => item.ListUserInputRequestsAsync(It.IsAny<ListUserInputRequests>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserInputRequest>());
        runtime
            .Setup(static item => item.GetArtifactProjectionAsync(It.IsAny<GetArtifactDetail>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArtifactProjection?)null);
        runtime
            .Setup(static item => item.GetArtifactCollectionProjectionAsync(It.IsAny<ListArtifacts>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArtifactCollectionProjection?)null);
        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        var thread = await sut.GetThreadProjectionAsync(
            new GetThreadProjection(new ThreadId("thread-runtime-null")),
            CancellationToken.None);
        var workflowBoard = await sut.GetWorkflowBoardProjectionAsync(
            new GetWorkflowBoard(new WorkflowId("workflow-runtime-null")),
            CancellationToken.None);
        var taskBoard = await sut.GetTaskBoardProjectionAsync(
            new GetTaskBoard(new WorkflowId("workflow-runtime-null")),
            CancellationToken.None);
        var plan = await sut.GetPlanProjectionAsync(
            new GetPlanProjection(new WorkflowId("workflow-runtime-null")),
            CancellationToken.None);
        var team = await sut.GetTeamProjectionAsync(
            new GetTeamProjection(new TeamId("team-runtime-null")),
            CancellationToken.None);
        var approvalQueue = await sut.GetApprovalQueueProjectionAsync(
            new ListPendingApprovals(),
            CancellationToken.None);
        var userInputRequests = await sut.ListUserInputRequestsAsync(
            new ListUserInputRequests(),
            CancellationToken.None);
        var artifact = await sut.GetArtifactProjectionAsync(
            new GetArtifactDetail(new ArtifactId("artifact-runtime-null")),
            CancellationToken.None);
        var collection = await sut.GetArtifactCollectionProjectionAsync(
            new ListArtifacts(new CollaborationSpaceId("space-runtime-null"), null),
            CancellationToken.None);

        Assert.Null(thread);
        Assert.Null(workflowBoard);
        Assert.Null(taskBoard);
        Assert.Null(plan);
        Assert.Null(team);
        Assert.Null(approvalQueue);
        Assert.Empty(userInputRequests);
        Assert.Null(artifact);
        Assert.Null(collection);
    }

    [Fact]
    public async Task WorkflowWriteCommands_ShouldMapIntoRuntime()
    {
        var runtime = CreateRuntimeMock();
        CreateWorkflow? capturedCreateWorkflow = null;
        PublishPlan? capturedPublishPlan = null;
        CreateTask? capturedCreateTask = null;
        UpdateTaskState? capturedUpdateTaskState = null;
        var owner = new ParticipantRef(new ParticipantId("agent-runtime-owner"), ParticipantKind.Agent, "Runtime Owner");

        runtime
            .Setup(static item => item.CreateWorkflowAsync(It.IsAny<CreateWorkflow>(), It.IsAny<CancellationToken>()))
            .Callback<CreateWorkflow, CancellationToken>((command, _) => capturedCreateWorkflow = command)
            .ReturnsAsync((CreateWorkflow command, CancellationToken _) => new Workflow(
                command.WorkflowId,
                new CollaborationSpaceRef(command.CollaborationSpaceId, command.CollaborationSpaceId.Value, "Runtime Space"),
                command.DisplayName,
                WorkflowState.Draft,
                command.OwnerParticipant,
                command.ThreadId));
        runtime
            .Setup(static item => item.PublishPlanAsync(It.IsAny<PublishPlan>(), It.IsAny<CancellationToken>()))
            .Callback<PublishPlan, CancellationToken>((command, _) => capturedPublishPlan = command)
            .ReturnsAsync((PublishPlan command, CancellationToken _) => new PlanProjection(command.WorkflowId, command.Plan));
        runtime
            .Setup(static item => item.CreateTaskAsync(It.IsAny<CreateTask>(), It.IsAny<CancellationToken>()))
            .Callback<CreateTask, CancellationToken>((command, _) => capturedCreateTask = command)
            .ReturnsAsync((CreateTask command, CancellationToken _) => command.Task);
        runtime
            .Setup(static item => item.UpdateTaskStateAsync(It.IsAny<UpdateTaskState>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateTaskState, CancellationToken>((command, _) => capturedUpdateTaskState = command)
            .ReturnsAsync((UpdateTaskState command, CancellationToken _) => new TianShu.Contracts.Workflows.Task(
                command.TaskId,
                new WorkflowId("workflow-runtime-write-001"),
                "Workflow Task",
                command.State,
                command.OwnerParticipant));

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var workflow = await sut.CreateWorkflowAsync(
            new CreateWorkflow(
                new WorkflowId("workflow-runtime-write-001"),
                new CollaborationSpaceId("space-runtime-write-001"),
                "Runtime Workflow Write",
                owner,
                new ThreadId("thread-runtime-write-001")),
            CancellationToken.None);
        var plan = await sut.PublishPlanAsync(
            new PublishPlan(
                workflow.Id,
                new Plan(
                    "Runtime Write Plan",
                    [
                        new PlanStep(0, "Map workflow writes"),
                    ])),
            CancellationToken.None);
        var task = await sut.CreateTaskAsync(
            new CreateTask(
                new TianShu.Contracts.Workflows.Task(
                    new TaskId("task-runtime-write-001"),
                    workflow.Id,
                    "Workflow Task",
                    TaskState.Todo,
                    owner)),
            CancellationToken.None);
        var updatedTask = await sut.UpdateTaskStateAsync(
            new UpdateTaskState(task.Id, TaskState.InProgress, owner),
            CancellationToken.None);

        Assert.NotNull(capturedCreateWorkflow);
        Assert.Equal("workflow-runtime-write-001", capturedCreateWorkflow!.WorkflowId.Value);
        Assert.NotNull(capturedPublishPlan);
        Assert.Equal("Runtime Write Plan", capturedPublishPlan!.Plan.Title);
        Assert.NotNull(capturedCreateTask);
        Assert.Equal("task-runtime-write-001", capturedCreateTask!.Task.Id.Value);
        Assert.NotNull(capturedUpdateTaskState);
        Assert.Equal(TaskState.InProgress, capturedUpdateTaskState!.State);
        Assert.Equal("Runtime Workflow Write", workflow.DisplayName);
        Assert.Equal("Runtime Write Plan", plan.Plan.Title);
        Assert.Equal("Workflow Task", task.Title);
        Assert.NotNull(updatedTask);
        Assert.Equal(TaskState.InProgress, updatedTask!.State);
    }

    [Fact]
    public async Task GetConversationSummaryAsync_AndGetGitDiffToRemoteAsync_MapArtifacts()
    {
        var runtime = CreateRuntimeMock();
        runtime
            .Setup(static item => item.GetConversationSummaryAsync(It.IsAny<ControlPlaneConversationArtifactQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneConversationArtifact
            {
                ConversationId = "conv-1",
                Path = @"D:\repo\.tianshu\threads\conv-1.jsonl",
                Preview = "artifact preview",
                Timestamp = "2026-04-08T02:03:00Z",
                UpdatedAt = "2026-04-08T02:04:00Z",
                ModelProvider = "openai",
                WorkingDirectory = @"D:\repo",
                CliVersion = "0.1.0",
                Source = "cli",
                GitSha = "abc123",
                GitBranch = "newkernel",
                GitOriginUrl = "https://example.com/tianshu.git",
            });
        runtime
            .Setup(static item => item.GetGitDiffToRemoteAsync(It.IsAny<ControlPlaneGitDiffArtifactQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneGitDiffArtifact
            {
                HasChanges = true,
                Diff = "diff --git a/src b/src",
            });

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var summary = await sut.GetConversationSummaryAsync(
            new ControlPlaneConversationArtifactQuery
            {
                ThreadId = new ThreadId("thread-artifact-1"),
            },
            CancellationToken.None);
        var diff = await sut.GetGitDiffToRemoteAsync(
            new ControlPlaneGitDiffArtifactQuery
            {
                ThreadId = new ThreadId("thread-artifact-1"),
            },
            CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal("conv-1", summary!.ConversationId);
        Assert.Equal("abc123", summary.GitSha);
        Assert.Equal("newkernel", summary.GitBranch);
        Assert.True(diff.HasChanges);
        Assert.Equal("diff --git a/src b/src", diff.Diff);
    }

    [Fact]
    public async Task ArtifactWriteCommands_ShouldMapIntoRuntime()
    {
        var runtime = CreateRuntimeMock();
        PublishArtifact? capturedPublish = null;
        PromoteArtifact? capturedPromote = null;
        AttachArtifactToTask? capturedAttach = null;
        var artifact = new Artifact(
            new ArtifactId("artifact-runtime-write"),
            new CollaborationSpaceRef(new CollaborationSpaceId("space-runtime-write"), "space-runtime-write", "Runtime Space"),
            "runtime-artifact.md",
            ArtifactKind.Document,
            new ParticipantRef(new ParticipantId("agent-runtime-artifact"), ParticipantKind.Agent, "Runtime Agent"));

        runtime
            .Setup(static item => item.PublishArtifactAsync(It.IsAny<PublishArtifact>(), It.IsAny<CancellationToken>()))
            .Callback<PublishArtifact, CancellationToken>((command, _) => capturedPublish = command)
            .ReturnsAsync((PublishArtifact command, CancellationToken _) => command.Artifact);
        runtime
            .Setup(static item => item.PromoteArtifactAsync(It.IsAny<PromoteArtifact>(), It.IsAny<CancellationToken>()))
            .Callback<PromoteArtifact, CancellationToken>((command, _) => capturedPromote = command)
            .ReturnsAsync((PromoteArtifact command, CancellationToken _) => new Artifact(
                command.ArtifactId,
                artifact.CollaborationSpace,
                artifact.Name,
                artifact.Kind,
                artifact.ProducedByParticipant,
                state: ArtifactLifecycleState.Promoted));
        runtime
            .Setup(static item => item.AttachArtifactToTaskAsync(It.IsAny<AttachArtifactToTask>(), It.IsAny<CancellationToken>()))
            .Callback<AttachArtifactToTask, CancellationToken>((command, _) => capturedAttach = command)
            .ReturnsAsync((AttachArtifactToTask command, CancellationToken _) => new Artifact(
                command.ArtifactId,
                artifact.CollaborationSpace,
                artifact.Name,
                artifact.Kind,
                artifact.ProducedByParticipant,
                state: ArtifactLifecycleState.Published));

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var published = await sut.PublishArtifactAsync(new PublishArtifact(artifact), CancellationToken.None);
        var promoted = await sut.PromoteArtifactAsync(new PromoteArtifact(artifact.Id, "stable"), CancellationToken.None);
        var attached = await sut.AttachArtifactToTaskAsync(new AttachArtifactToTask(artifact.Id, new TaskId("task-runtime-write")), CancellationToken.None);

        Assert.Equal(artifact.Id, capturedPublish?.Artifact.Id);
        Assert.Equal("stable", capturedPromote?.TargetChannel);
        Assert.Equal("task-runtime-write", capturedAttach?.TaskId.Value);
        Assert.Equal(ArtifactLifecycleState.Draft, published.State);
        Assert.Equal(ArtifactLifecycleState.Promoted, promoted.State);
        Assert.Equal(ArtifactLifecycleState.Published, attached.State);
    }

    [Fact]
    public async Task SearchFuzzyFilesAsync_AndSessionCommands_MapConversationContracts()
    {
        var runtime = CreateRuntimeMock();
        ControlPlaneFuzzyFileSearchQuery? capturedQuery = null;
        ControlPlaneStartFuzzyFileSearchSessionCommand? capturedStart = null;
        ControlPlaneUpdateFuzzyFileSearchSessionCommand? capturedUpdate = null;
        ControlPlaneStopFuzzyFileSearchSessionCommand? capturedStop = null;
        runtime
            .Setup(static item => item.SearchFuzzyFilesAsync(It.IsAny<ControlPlaneFuzzyFileSearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneFuzzyFileSearchQuery, CancellationToken>((query, _) => capturedQuery = query)
            .ReturnsAsync(new ControlPlaneFuzzyFileSearchResult
            {
                Files =
                [
                    new ControlPlaneFuzzyFileSearchFile
                    {
                        Path = @"src\TianShu.Execution.Runtime\TianShuExecutionRuntime.cs",
                        FileName = "TianShuExecutionRuntime.cs",
                    },
                ],
            });
        runtime
            .Setup(static item => item.StartFuzzyFileSearchSessionAsync(It.IsAny<ControlPlaneStartFuzzyFileSearchSessionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneStartFuzzyFileSearchSessionCommand, CancellationToken>((command, _) => capturedStart = command)
            .ReturnsAsync(new ControlPlaneFuzzyFileSearchCommandAcceptedResult());
        runtime
            .Setup(static item => item.UpdateFuzzyFileSearchSessionAsync(It.IsAny<ControlPlaneUpdateFuzzyFileSearchSessionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneUpdateFuzzyFileSearchSessionCommand, CancellationToken>((command, _) => capturedUpdate = command)
            .ReturnsAsync(new ControlPlaneFuzzyFileSearchCommandAcceptedResult());
        runtime
            .Setup(static item => item.StopFuzzyFileSearchSessionAsync(It.IsAny<ControlPlaneStopFuzzyFileSearchSessionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ControlPlaneStopFuzzyFileSearchSessionCommand, CancellationToken>((command, _) => capturedStop = command)
            .ReturnsAsync(new ControlPlaneFuzzyFileSearchCommandAcceptedResult());

        var sut = new RuntimeControlPlaneAdapter(runtime.Object);
        var result = await sut.SearchFuzzyFilesAsync(
            new ControlPlaneFuzzyFileSearchQuery
            {
                Query = "TianShuExecutionRuntime",
                WorkingDirectory = @"D:\repo",
                Limit = 8,
                Roots = ["src", "tests"],
            },
            CancellationToken.None);
        await sut.StartFuzzyFileSearchSessionAsync(
            new ControlPlaneStartFuzzyFileSearchSessionCommand
            {
                SessionId = "session-1",
                Roots = ["src"],
            },
            CancellationToken.None);
        await sut.UpdateFuzzyFileSearchSessionAsync(
            new ControlPlaneUpdateFuzzyFileSearchSessionCommand
            {
                SessionId = "session-1",
                Query = "Kernel",
            },
            CancellationToken.None);
        await sut.StopFuzzyFileSearchSessionAsync(
            new ControlPlaneStopFuzzyFileSearchSessionCommand
            {
                SessionId = "session-1",
            },
            CancellationToken.None);

        var file = Assert.Single(result.Files);
        Assert.Equal(@"src\TianShu.Execution.Runtime\TianShuExecutionRuntime.cs", file.Path);
        Assert.Equal("TianShuExecutionRuntime.cs", file.FileName);

        Assert.NotNull(capturedQuery);
        Assert.Equal("TianShuExecutionRuntime", capturedQuery!.Query);
        Assert.Equal(@"D:\repo", capturedQuery.WorkingDirectory);
        Assert.Equal(8, capturedQuery.Limit);
        Assert.Equal(["src", "tests"], capturedQuery.Roots);

        Assert.NotNull(capturedStart);
        Assert.Equal("session-1", capturedStart!.SessionId);
        Assert.Equal(["src"], capturedStart.Roots);

        Assert.NotNull(capturedUpdate);
        Assert.Equal("session-1", capturedUpdate!.SessionId);
        Assert.Equal("Kernel", capturedUpdate.Query);

        Assert.NotNull(capturedStop);
        Assert.Equal("session-1", capturedStop!.SessionId);
    }

    [Fact]
    public async Task SubscribeGovernanceAsync_MapsTypedProjectionEvent()
    {
        var runtime = CreateRuntimeMock();
        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        await using var enumerator = sut.SubscribeGovernanceAsync(
            new ControlPlaneGovernanceSubscription
            {
                ThreadId = new ThreadId("thread-governance-1"),
            },
            CancellationToken.None).GetAsyncEnumerator();

        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        runtime.Raise(
            static item => item.StreamEventReceived += null,
            new ControlPlaneConversationStreamEventArgs(new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.ApprovalRequested,
                ThreadId = new ThreadId("thread-governance-1"),
                TurnId = new TurnId("turn-governance-1"),
                CallId = new CallId("approval-governance-1"),
                ToolName = "shell",
                ApprovalKind = "shell_command",
                RequiresApproval = true,
                PayloadKind = ControlPlaneConversationStreamPayloadKind.ApprovalRequest,
                Payload = StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["toolName"] = "shell",
                    ["approvalKind"] = "shell_command",
                    ["summary"] = "需要批准 shell",
                    ["availableDecisions"] = new object?[] { "accept", "decline" },
                }),
            }));

        Assert.True(await moveNextTask);
        var streamEvent = enumerator.Current;
        Assert.Equal("ApprovalRequested", streamEvent.Kind);
        Assert.Equal("thread-governance-1", streamEvent.ThreadId?.Value);
        Assert.Equal("turn-governance-1", streamEvent.TurnId?.Value);
        Assert.Equal("approval-governance-1", streamEvent.CallId?.Value);
        Assert.Equal("shell", streamEvent.ToolName);
        Assert.Equal("shell_command", streamEvent.ApprovalKind);
        Assert.True(streamEvent.RequiresApproval);
        Assert.NotNull(streamEvent.Payload);
        Assert.Equal("shell", streamEvent.Payload!.Properties["toolName"].StringValue);
        Assert.Equal("shell_command", streamEvent.Payload.Properties["approvalKind"].StringValue);
        Assert.Equal("需要批准 shell", streamEvent.Payload.Properties["summary"].StringValue);
        Assert.Equal(
            ["accept", "decline"],
            streamEvent.Payload.Properties["availableDecisions"].Items.Select(static item => item.StringValue!).ToArray());
    }

    [Fact]
    public async Task SubscribeStreamAsync_MapsTypedConversationStreamEvent()
    {
        var runtime = CreateRuntimeMock();
        var sut = new RuntimeControlPlaneAdapter(runtime.Object);

        await using var enumerator = sut.SubscribeStreamAsync(
            new ThreadId("thread-stream-1"),
            CancellationToken.None).GetAsyncEnumerator();

        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        runtime.Raise(
            static item => item.StreamEventReceived += null,
            new ControlPlaneConversationStreamEventArgs(new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.AssistantTextDelta,
                ThreadId = new ThreadId("thread-stream-1"),
                TurnId = new TurnId("turn-stream-1"),
                Text = "stream relay ok",
            }));

        Assert.True(await moveNextTask);
        var streamEvent = enumerator.Current;
        Assert.Equal(ControlPlaneConversationStreamEventKind.AssistantTextDelta, streamEvent.Kind);
        Assert.Equal("thread-stream-1", streamEvent.ThreadId?.Value);
        Assert.Equal("turn-stream-1", streamEvent.TurnId?.Value);
        Assert.Equal("stream relay ok", streamEvent.Text);
    }

    private static Mock<IExecutionRuntime> CreateRuntimeMock()
    {
        var runtime = new Mock<IExecutionRuntime>(MockBehavior.Strict);
        runtime.Setup(static item => item.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return runtime;
    }
}
