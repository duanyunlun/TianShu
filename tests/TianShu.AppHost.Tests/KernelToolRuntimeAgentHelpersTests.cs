using System.Text.Json;
using System.Text.Json.Nodes;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelToolRuntimeAgentHelpersTests
{
    [Fact]
    public void BuildSpawnedAgentSource_ShouldNormalizeNicknameRoleAndIncrementDepth()
    {
        var parentSource = KernelSessionSource.SubAgent(
            KernelSubAgentSource.ThreadSpawn("thread_parent", 2, "Parent", "review"));

        var source = KernelToolRuntimeAgentHelpers.BuildSpawnedAgentSource(
            parentSource,
            "thread_child_parent",
            " worker ",
            " Athena ");

        Assert.Equal("thread_child_parent", source.SubAgentSource?.ParentThreadId);
        Assert.Equal(3, source.SubAgentSource?.Depth);
        Assert.Equal("worker", source.SubAgentSource?.AgentRole);
        Assert.Equal("Athena", source.SubAgentSource?.AgentNickname);
    }

    [Fact]
    public void BuildAgentStatusNode_WhenRecordMissing_ShouldReturnNotFound()
    {
        var status = KernelToolRuntimeAgentHelpers.BuildAgentStatusNode(
            record: null,
            treatArchivedAsNotFound: true,
            hasRunningTurn: false);

        Assert.Equal("\"not_found\"", status!.ToJsonString());
    }

    [Fact]
    public void BuildAgentStatusNode_WhenRunningTurnExists_ShouldReturnRunning()
    {
        var record = new KernelThreadRecord
        {
            Id = "thread_running",
        };

        var status = KernelToolRuntimeAgentHelpers.BuildAgentStatusNode(
            record,
            treatArchivedAsNotFound: true,
            hasRunningTurn: true);

        Assert.Equal("\"running\"", status!.ToJsonString());
    }

    [Fact]
    public void BuildAgentStatusNode_WhenNoTurns_ShouldReturnPendingInit()
    {
        var record = new KernelThreadRecord
        {
            Id = "thread_pending",
        };

        var status = KernelToolRuntimeAgentHelpers.BuildAgentStatusNode(
            record,
            treatArchivedAsNotFound: true,
            hasRunningTurn: false);

        Assert.Equal("\"pending_init\"", status!.ToJsonString());
    }

    [Fact]
    public void BuildAgentStatusNode_WhenLatestTurnIsStaleInProgress_ShouldReturnErrored()
    {
        var record = new KernelThreadRecord
        {
            Id = "thread_stale",
            Turns =
            [
                new KernelTurnRecord
                {
                    Id = "turn_001",
                    Status = " inProgress ",
                    AssistantMessage = "stale output",
                },
            ],
        };

        var status = KernelToolRuntimeAgentHelpers.BuildAgentStatusNode(
            record,
            treatArchivedAsNotFound: false,
            hasRunningTurn: false);

        Assert.Equal("""{"errored":"stale output"}""", status!.ToJsonString());
    }

    [Fact]
    public void BuildAgentStatusNode_WhenCompletedTurnMessageMissing_ShouldFallbackToThreadLastAssistantMessage()
    {
        var record = new KernelThreadRecord
        {
            Id = "thread_completed",
            LastAssistantMessage = "final answer",
            Turns =
            [
                new KernelTurnRecord
                {
                    Id = "turn_002",
                    Status = "completed",
                    AssistantMessage = "  ",
                },
            ],
        };

        var status = KernelToolRuntimeAgentHelpers.BuildAgentStatusNode(
            record,
            treatArchivedAsNotFound: false,
            hasRunningTurn: false);

        Assert.Equal("""{"completed":"final answer"}""", status!.ToJsonString());
    }

    [Fact]
    public void BuildSpawnedAgentSession_ShouldApplyOverridesAndRewriteCollaborationSettings()
    {
        var baseSession = CreateBaseSession(
            collaborationMode: new KernelCollaborationModeState(
                KernelCollaborationModeState.PlanMode,
                new KernelCollaborationModeSettings("base-collab-model", "medium", "base-collab-dev")));
        var context = new KernelSpawnedAgentSessionContext(
            Model: " turn-model ",
            ModelProvider: " azure-openai ",
            ServiceTier: null,
            ApprovalPolicy: KernelApprovalPolicy.OnRequest,
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "danger-full-access" }),
            SandboxMode: " danger-full-access ",
            AllowLoginShell: false,
            ShellEnvironmentPolicy: null,
            Cwd: " d:\\turn ",
            ProviderBaseUrl: " https://turn.example/v1 ",
            ProviderApiKeyEnvironmentVariable: " TURN_API_KEY ",
            ProviderWireApi: " responses ",
            ProviderRequestMaxRetries: 3,
            ProviderStreamMaxRetries: 4,
            ProviderStreamIdleTimeoutMs: 5_000,
            ProviderWebsocketConnectTimeoutMs: 6_000,
            ProviderSupportsWebsockets: true,
            WebSearchMode: " auto ",
            DeveloperInstructions: " turn dev ",
            UserInstructions: "turn user",
            ReasoningSummary: " turn summary ",
            Verbosity: " high ",
            CollaborationMode: new KernelCollaborationModeState(
                KernelCollaborationModeState.PlanMode,
                new KernelCollaborationModeSettings(string.Empty, null, "mode-dev")));
        var sessionSource = KernelSessionSource.SubAgent(
            KernelSubAgentSource.ThreadSpawn("thread_parent", 3, "Athena", "worker"));

        var session = KernelToolRuntimeAgentHelpers.BuildSpawnedAgentSession(
            baseSession,
            context,
            cwdOverride: " d:\\child ",
            modelOverride: " gpt-5.4 ",
            reasoningEffortOverride: " high ",
            sessionSourceOverride: sessionSource,
            developerInstructionsOverride: " role dev ");

        Assert.Equal("gpt-5.4", session.Model);
        Assert.Equal("azure-openai", session.ModelProvider);
        Assert.Equal("d:\\child", session.Cwd);
        Assert.Same(KernelApprovalPolicy.OnRequest, session.ApprovalPolicy);
        Assert.Equal("danger-full-access", session.SandboxMode);
        Assert.False(session.AllowLoginShell);
        Assert.Equal("https://turn.example/v1", session.ProviderBaseUrl);
        Assert.Equal("TURN_API_KEY", session.ProviderApiKeyEnvironmentVariable);
        Assert.Equal("responses", session.ProviderWireApi);
        Assert.Equal(3, session.ProviderRequestMaxRetries);
        Assert.Equal(4, session.ProviderStreamMaxRetries);
        Assert.Equal(5_000, session.ProviderStreamIdleTimeoutMs);
        Assert.Equal(6_000, session.ProviderWebsocketConnectTimeoutMs);
        Assert.True(session.ProviderSupportsWebsockets);
        Assert.Equal("auto", session.WebSearchMode);
        Assert.Equal("role dev", session.DeveloperInstructions);
        Assert.Equal("turn user", session.UserInstructions);
        Assert.Equal("turn summary", session.ReasoningSummary);
        Assert.Equal("high", session.Verbosity);
        Assert.Same(sessionSource, session.SessionSource);
        Assert.NotNull(session.CollaborationMode);
        Assert.Equal(KernelCollaborationModeState.PlanMode, session.CollaborationMode!.Mode);
        Assert.Equal("gpt-5.4", session.CollaborationMode.Settings.Model);
        Assert.Equal("high", session.CollaborationMode.Settings.ReasoningEffort);
        Assert.Equal("role dev", session.CollaborationMode.Settings.DeveloperInstructions);
    }

    [Fact]
    public void BuildSpawnedAgentSession_ShouldFallbackToTurnContextAndBaseSession()
    {
        var baseSession = CreateBaseSession(
            collaborationMode: new KernelCollaborationModeState(
                KernelCollaborationModeState.PlanMode,
                new KernelCollaborationModeSettings(string.Empty, "medium", "base-collab-dev")));
        var context = new KernelSpawnedAgentSessionContext(
            Model: " gpt-turn ",
            ModelProvider: null,
            ServiceTier: null,
            ApprovalPolicy: null,
            SandboxPolicy: null,
            SandboxMode: null,
            AllowLoginShell: true,
            ShellEnvironmentPolicy: null,
            Cwd: " d:\\turn-two ",
            DeveloperInstructions: " turn dev ");

        var session = KernelToolRuntimeAgentHelpers.BuildSpawnedAgentSession(
            baseSession,
            context,
            cwdOverride: null);

        Assert.Equal("gpt-turn", session.Model);
        Assert.Equal(baseSession.ModelProvider, session.ModelProvider);
        Assert.Equal("d:\\turn-two", session.Cwd);
        Assert.Same(baseSession.ApprovalPolicy, session.ApprovalPolicy);
        Assert.Equal(baseSession.SandboxMode, session.SandboxMode);
        Assert.Equal("turn dev", session.DeveloperInstructions);
        Assert.Same(baseSession.SessionSource, session.SessionSource);
        Assert.NotNull(session.CollaborationMode);
        Assert.Equal("gpt-turn", session.CollaborationMode!.Settings.Model);
        Assert.Equal("medium", session.CollaborationMode.Settings.ReasoningEffort);
        Assert.Equal("base-collab-dev", session.CollaborationMode.Settings.DeveloperInstructions);
    }

    [Theory]
    [InlineData(null, 30000)]
    [InlineData(1, 10000)]
    [InlineData(12000, 12000)]
    [InlineData(5000000, 3600000)]
    public void NormalizeWaitTimeoutMs_ShouldClampExpectedValues(int? timeoutMs, int expected)
    {
        var actual = KernelToolRuntimeAgentHelpers.NormalizeWaitTimeoutMs(timeoutMs);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsFinalAgentStatus_ShouldTreatPendingAndRunningAsNonFinal()
    {
        Assert.False(KernelToolRuntimeAgentHelpers.IsFinalAgentStatus(JsonValue.Create("pending_init")));
        Assert.False(KernelToolRuntimeAgentHelpers.IsFinalAgentStatus(JsonValue.Create("running")));
        Assert.True(KernelToolRuntimeAgentHelpers.IsFinalAgentStatus(new JsonObject { ["completed"] = "done" }));
    }

    private static KernelThreadSessionState CreateBaseSession(KernelCollaborationModeState? collaborationMode = null)
    {
        return new KernelThreadSessionState(
            Model: "gpt-base",
            ModelProvider: "openai",
            ServiceTier: null,
            Cwd: @"d:\base",
            ApprovalPolicy: KernelApprovalPolicy.Never,
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "workspace-write" }),
            SandboxMode: "workspace-write",
            AllowLoginShell: true,
            ShellEnvironmentPolicy: null,
            Ephemeral: false,
            ServiceName: "service-base",
            BaseInstructions: "base instructions",
            DeveloperInstructions: "base dev",
            UserInstructions: "base user",
            ReasoningSummary: "base summary",
            Verbosity: "medium",
            Personality: "balanced",
            DynamicTools: null,
            ProviderBaseUrl: "https://base.example/v1",
            ProviderApiKeyEnvironmentVariable: "BASE_API_KEY",
            ProviderWireApi: "responses",
            ProviderRequestMaxRetries: 1,
            ProviderStreamMaxRetries: 2,
            ProviderStreamIdleTimeoutMs: 3_000,
            ProviderWebsocketConnectTimeoutMs: 4_000,
            ProviderSupportsWebsockets: false,
            ProviderHttpFallbackEnabled: true,
            WebSearchMode: "off",
            CollaborationMode: collaborationMode,
            PersistExtendedHistory: true,
            WindowsSandboxLevel: KernelWindowsSandboxLevel.Unelevated,
            DefaultModeRequestUserInputEnabled: true,
            SessionSource: KernelSessionSource.AppServer);
    }
}
