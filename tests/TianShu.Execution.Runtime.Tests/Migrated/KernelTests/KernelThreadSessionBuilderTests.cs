using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelThreadSessionBuilderTests
{
    private static readonly JsonSerializerOptions ProtocolJsonOptions = CreateProtocolJsonOptions();

    [Fact]
    public void KernelThreadSessionBuilder_ShouldBuildDefaultSessionFromRecord()
    {
        var record = new KernelThreadRecord
        {
            Id = "thread_builder_001",
            Cwd = "D:/Repo",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            StatusType = "idle",
            ActiveFlags = [],
            Turns = [],
        };

        var session = KernelThreadSessionBuilder
            .FromRecord(record, "gpt-5", "openai", "on-request")
            .Build();

        Assert.Equal("gpt-5", session.Model);
        Assert.Equal("openai", session.ModelProvider);
        Assert.Equal("on-request", session.ApprovalPolicy);
        Assert.Equal("D:/Repo", session.Cwd);
        Assert.Equal("workspaceWrite", session.SandboxMode);
        Assert.False(session.Ephemeral);
        Assert.Equal(KernelSessionSource.VsCode, session.SessionSource);
    }

    [Fact]
    public void KernelSessionSource_MemoryConsolidation_ShouldProjectToGenericSubAgentSourceKind()
    {
        var source = KernelSessionSource.SubAgent(KernelSubAgentSource.MemoryConsolidation);

        Assert.Equal(KernelThreadSourceKind.SubAgent, source.GetThreadSourceKind());
        Assert.True(KernelThreadSourceKind.SubAgent.Matches(source));
        Assert.False(KernelThreadSourceKind.SubAgentOther.Matches(source));
    }

    [Fact]
    public void KernelThreadSessionBuilder_WhenConfigSnapshotSessionSourceMissing_ShouldPreserveVsCodeFallback()
    {
        var record = new KernelThreadRecord
        {
            Id = "thread_builder_missing_source_001",
            Cwd = "D:/Repo",
        };

        var snapshot = new KernelThreadConfigSnapshot(
            Model: "gpt-5",
            ModelProviderId: "openai",
            ServiceTier: null,
            ApprovalPolicy: "on-request",
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "workspaceWrite" }),
            SandboxMode: "workspaceWrite",
            Cwd: "D:/Repo",
            Ephemeral: false,
            AllowLoginShell: true,
            ShellEnvironmentPolicy: KernelShellEnvironmentPolicy.Default,
            ProviderBaseUrl: null,
            ProviderApiKeyEnvironmentVariable: null,
            ProviderWireApi: null,
            ProviderRequestMaxRetries: null,
            ProviderStreamMaxRetries: null,
            ProviderStreamIdleTimeoutMs: null,
            ProviderWebsocketConnectTimeoutMs: null,
            ProviderSupportsWebsockets: null,
            ProviderHttpFallbackEnabled: false,
            WebSearchMode: null,
            ServiceName: null,
            BaseInstructions: null,
            DeveloperInstructions: null,
            UserInstructions: null,
            ReasoningEffort: null,
            ReasoningSummary: null,
            Verbosity: null,
            Personality: null,
            DynamicTools: null,
            CollaborationMode: null,
            PersistExtendedHistory: false,
            SessionSource: null!);

        var session = KernelThreadSessionBuilder
            .FromRecord(record, "gpt-5", "openai", "on-request")
            .ApplyConfigSnapshot(snapshot)
            .Build();

        Assert.Equal(KernelSessionSource.VsCode, session.SessionSource);
    }

    [Fact]
    public void KernelThreadSessionBuilder_ShouldPreserveModelCatalogSnapshot()
    {
        var snapshot = new KernelThreadConfigSnapshot(
            Model: "gpt-5",
            ModelProviderId: "openai",
            ServiceTier: null,
            ApprovalPolicy: "on-request",
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "workspaceWrite" }),
            SandboxMode: "workspaceWrite",
            Cwd: "D:/Repo",
            Ephemeral: false,
            AllowLoginShell: true,
            ShellEnvironmentPolicy: KernelShellEnvironmentPolicy.Default,
            ProviderBaseUrl: null,
            ProviderApiKeyEnvironmentVariable: null,
            ProviderWireApi: "responses",
            ProviderRequestMaxRetries: null,
            ProviderStreamMaxRetries: null,
            ProviderStreamIdleTimeoutMs: null,
            ProviderWebsocketConnectTimeoutMs: null,
            ProviderSupportsWebsockets: null,
            ProviderHttpFallbackEnabled: false,
            WebSearchMode: null,
            ServiceName: null,
            BaseInstructions: null,
            DeveloperInstructions: null,
            UserInstructions: null,
            ReasoningEffort: null,
            ReasoningSummary: null,
            Verbosity: null,
            Personality: null,
            DynamicTools: null,
            CollaborationMode: KernelCollaborationModeState.CreateDefault("gpt-5"),
            PersistExtendedHistory: false,
            SessionSource: KernelSessionSource.AppServer,
            ModelRouteSetId: "workbench");

        var session = KernelThreadSessionBuilder
            .FromRecord(new KernelThreadRecord { Id = "thread_builder_catalog_001", Cwd = "D:/Repo" }, "gpt-5", "openai", "on-request")
            .ApplyConfigSnapshot(snapshot)
            .Build();
        var persisted = KernelThreadConfigSnapshotFactory.FromSession(session);

        Assert.Equal("workbench", session.ModelRouteSetId);
        Assert.Equal("workbench", persisted.ModelRouteSetId);
        Assert.Equal("workbench", persisted.DeepClone().ModelRouteSetId);
    }

    [Fact]
    public void KernelThreadSessionBuilder_ShouldApplyThreadStartAndTurnOverrides()
    {
        var startParams = JsonSerializer.SerializeToElement(new
        {
            cwd = "D:/Repo/Sub",
            model = "gpt-4.1-mini",
            modelProvider = "openai-custom",
            approvalPolicy = "never",
            serviceTier = "flex",
            sandbox = new
            {
                type = "readOnly",
                networkAccess = false,
            },
            ephemeral = true,
            personality = "friendly",
        });

        var started = KernelThreadSessionBuilder
            .FromRecord(new KernelThreadRecord { Id = "thread_builder_002", Cwd = "D:/Repo" }, "gpt-5", "openai", "on-request")
            .ApplyThreadStart(startParams)
            .Build();

        Assert.Equal("gpt-4.1-mini", started.Model);
        Assert.Equal("openai-custom", started.ModelProvider);
        Assert.Equal("never", started.ApprovalPolicy);
        Assert.Equal("flex", started.ServiceTier);
        Assert.Equal("D:/Repo/Sub", started.Cwd);
        Assert.Equal("readOnly", started.SandboxMode);
        Assert.True(started.Ephemeral);
        Assert.Equal("friendly", started.Personality);
        Assert.Null(started.WebSearchMode);
        Assert.Null(started.ReasoningSummary);
        Assert.Null(started.Verbosity);
        Assert.Null(started.UserInstructions);
        Assert.NotNull(started.CollaborationMode);
        Assert.Equal("default", started.CollaborationMode!.Mode);
        Assert.Equal("gpt-4.1-mini", started.CollaborationMode.Settings.Model);

        var turnParams = JsonSerializer.SerializeToElement(new
        {
            approvalPolicy = "on-failure",
            personality = "pragmatic",
            summary = "detailed",
            verbosity = "medium",
            effort = "medium",
            providerBaseUrl = "https://example.invalid/v1",
            providerApiKeyEnvironmentVariable = "OPENAI_API_KEY",
            providerWireApi = "responses",
            providerRequestMaxRetries = 2,
            providerStreamMaxRetries = 3,
            providerStreamIdleTimeoutMs = 45000,
            providerWebsocketConnectTimeoutMs = 15000,
            providerSupportsWebsockets = true,
            collaborationMode = new
            {
                mode = "plan",
                settings = new
                {
                    model = "gpt-5",
                    developer_instructions = "?????????",
                },
            },
        });

        var overridden = KernelThreadSessionBuilder
            .FromSession(started)
            .ApplyTurnOverrides(turnParams)
            .Build();

        Assert.Equal("gpt-5", overridden.Model);
        Assert.Equal("on-failure", overridden.ApprovalPolicy);
        Assert.Equal("pragmatic", overridden.Personality);
        Assert.Equal("detailed", overridden.ReasoningSummary);
        Assert.Equal("medium", overridden.Verbosity);
        Assert.Equal("https://example.invalid/v1", overridden.ProviderBaseUrl);
        Assert.Equal("OPENAI_API_KEY", overridden.ProviderApiKeyEnvironmentVariable);
        Assert.Equal("responses", overridden.ProviderWireApi);
        Assert.Equal(2, overridden.ProviderRequestMaxRetries);
        Assert.Equal(3, overridden.ProviderStreamMaxRetries);
        Assert.Equal(45000, overridden.ProviderStreamIdleTimeoutMs);
        Assert.Equal(15000, overridden.ProviderWebsocketConnectTimeoutMs);
        Assert.True(overridden.ProviderSupportsWebsockets);
        Assert.Equal("readOnly", overridden.SandboxMode);
        Assert.True(overridden.Ephemeral);
        Assert.NotNull(overridden.CollaborationMode);
        Assert.Equal("plan", overridden.CollaborationMode!.Mode);
        Assert.Equal("gpt-5", overridden.CollaborationMode.Settings.Model);
        Assert.Equal("medium", overridden.CollaborationMode.Settings.ReasoningEffort);
        Assert.Equal("?????????", overridden.CollaborationMode.Settings.DeveloperInstructions);
    }

    [Theory]
    [InlineData("chat-completions")]
    [InlineData("unknown")]
    public void KernelThreadSessionBuilder_WhenProviderWireApiUnsupported_Throws(string wireApi)
    {
        var turnParams = JsonSerializer.SerializeToElement(new
        {
            providerWireApi = wireApi,
        });

        var builder = KernelThreadSessionBuilder
            .FromRecord(new KernelThreadRecord { Id = "thread_builder_invalid_default_protocol_001", Cwd = "D:/Repo" }, "gpt-5", "openai", "on-request");

        var exception = Assert.Throws<InvalidOperationException>(() => builder.ApplyTurnOverrides(turnParams));
        Assert.Contains("responses", exception.Message, StringComparison.Ordinal);
        Assert.Contains(wireApi, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelThreadSessionBuilder_ShouldPreserveGranularApprovalPolicyJsonAndExplicitServiceTierClear()
    {
        var startParams = JsonSerializer.SerializeToElement(new
        {
            approvalPolicy = new
            {
                granular = new
                {
                    sandbox_approval = true,
                    rules = false,
                    skill_approval = true,
                    request_permissions = false,
                    mcp_elicitations = true,
                },
            },
            serviceTier = (string?)null,
        });

        var started = KernelThreadSessionBuilder
            .FromRecord(new KernelThreadRecord { Id = "thread_builder_004", Cwd = "D:/Repo" }, "gpt-5", "openai", "on-request")
            .ApplyThreadStart(startParams)
            .Build();

        Assert.Null(started.ServiceTier);
        Assert.Equal("granular", KernelApprovalPolicyHelpers.NormalizeScalar(started.ApprovalPolicy));
        Assert.True(KernelApprovalPolicyHelpers.IsGranular(started.ApprovalPolicy));
        Assert.True(KernelApprovalPolicyHelpers.TryGetGranularFlag(
            started.ApprovalPolicy,
            "sandbox_approval",
            "sandboxApproval",
            out var sandboxApproval));
        Assert.True(sandboxApproval);
        Assert.True(KernelApprovalPolicyHelpers.TryGetGranularFlag(
            started.ApprovalPolicy,
            "request_permissions",
            "requestPermissions",
            out var requestPermissions));
        Assert.False(requestPermissions);

        var payload = KernelApprovalPolicyHelpers.ToPayloadValue(started.ApprovalPolicy);
        Assert.NotNull(payload);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var granular = document.RootElement.GetProperty("granular");
        Assert.True(granular.GetProperty("sandbox_approval").GetBoolean());
        Assert.False(granular.GetProperty("request_permissions").GetBoolean());
        Assert.True(granular.GetProperty("mcp_elicitations").GetBoolean());
    }

    [Fact]
    public void KernelThreadSessionBuilder_ShouldKeepTopLevelDeveloperInstructionsWhenModeUsesBuiltInPrompt()
    {
        var session = new KernelThreadSessionState(
            Model: "gpt-5",
            ModelProvider: "openai",
            ServiceTier: null,
            Cwd: "D:/Repo",
            ApprovalPolicy: "on-request",
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "workspaceWrite" }),
            SandboxMode: "workspaceWrite",
            DeveloperInstructions: "top-level developer instructions",
            CollaborationMode: new KernelCollaborationModeState(
                "default",
                new KernelCollaborationModeSettings("gpt-5", null, "custom mode instructions")));

        var overridden = KernelThreadSessionBuilder
            .FromSession(session)
            .ApplyTurnOverrides(JsonSerializer.SerializeToElement(new
            {
                collaborationMode = new
                {
                    mode = "plan",
                    settings = new
                    {
                        developer_instructions = (string?)null,
                    },
                },
            }))
            .Build();

        Assert.Equal("top-level developer instructions", overridden.DeveloperInstructions);
        Assert.NotNull(overridden.CollaborationMode);
        Assert.Equal("plan", overridden.CollaborationMode!.Mode);
        Assert.Equal("gpt-5", overridden.CollaborationMode.Settings.Model);
        Assert.Null(overridden.CollaborationMode.Settings.DeveloperInstructions);
    }

    [Fact]
    public void KernelThreadSessionBuilder_ShouldPreserveExistingPromptDefaultsWhenLaterSnapshotOmitsThem()
    {
        var record = new KernelThreadRecord
        {
            Id = "thread_builder_003",
            Cwd = "D:/Repo",
        };

        var configuredSnapshot = new KernelThreadConfigSnapshot(
            Model: "gpt-5",
            ModelProviderId: "openai",
            ServiceTier: null,
            ApprovalPolicy: "on-request",
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "workspaceWrite" }),
            SandboxMode: "workspaceWrite",
            Cwd: "D:/Repo",
            Ephemeral: false,
            AllowLoginShell: true,
            ShellEnvironmentPolicy: KernelShellEnvironmentPolicy.Default,
            ProviderBaseUrl: null,
            ProviderApiKeyEnvironmentVariable: null,
            ProviderWireApi: null,
            ProviderRequestMaxRetries: null,
            ProviderStreamMaxRetries: null,
            ProviderStreamIdleTimeoutMs: null,
            ProviderWebsocketConnectTimeoutMs: null,
            ProviderSupportsWebsockets: null,
            ProviderHttpFallbackEnabled: true,
            WebSearchMode: null,
            ServiceName: null,
            BaseInstructions: "configured base prompt",
            DeveloperInstructions: "configured developer prompt",
            UserInstructions: "configured user prompt",
            ReasoningEffort: null,
            ReasoningSummary: "auto",
            Verbosity: "high",
            Personality: null,
            DynamicTools: null,
            CollaborationMode: KernelCollaborationModeState.CreateDefault("gpt-5"),
            PersistExtendedHistory: false,
            SessionSource: "configToml");

        var persistedSnapshot = configuredSnapshot with
        {
            BaseInstructions = null,
            DeveloperInstructions = null,
            UserInstructions = null,
            SessionSource = "appServer",
        };

        var session = KernelThreadSessionBuilder
            .FromRecord(record, "gpt-5", "openai", "on-request")
            .ApplyConfigSnapshot(configuredSnapshot)
            .ApplyConfigSnapshot(persistedSnapshot)
            .Build();

        Assert.Equal("configured base prompt", session.BaseInstructions);
        Assert.Equal("configured developer prompt", session.DeveloperInstructions);
        Assert.Equal("configured user prompt", session.UserInstructions);
        Assert.Equal("auto", session.ReasoningSummary);
        Assert.Equal("high", session.Verbosity);
        Assert.True(session.ProviderHttpFallbackEnabled);
    }

    [Fact]
    public void KernelThreadSessionBuilder_ShouldPreserveProviderHttpFallbackWhenRebuildingFromSession()
    {
        var session = new KernelThreadSessionState(
            Model: "gpt-5",
            ModelProvider: "openai",
            ServiceTier: null,
            Cwd: "D:/Repo",
            ApprovalPolicy: "on-request",
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "workspaceWrite" }),
            SandboxMode: "workspaceWrite",
            ProviderWireApi: "responses",
            ProviderSupportsWebsockets: true,
            ProviderHttpFallbackEnabled: true);

        var rebuilt = KernelThreadSessionBuilder
            .FromSession(session)
            .Build();

        Assert.True(rebuilt.ProviderHttpFallbackEnabled);
    }

    [Fact]
    public void KernelThreadConfigSnapshotFactory_ShouldNotPersistProviderHttpFallbackState()
    {
        var session = new KernelThreadSessionState(
            Model: "gpt-5",
            ModelProvider: "openai",
            ServiceTier: null,
            Cwd: "D:/Repo",
            ApprovalPolicy: "on-request",
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "workspaceWrite" }),
            SandboxMode: "workspaceWrite",
            ProviderWireApi: "responses",
            ProviderSupportsWebsockets: true,
            ProviderHttpFallbackEnabled: true);

        var snapshot = KernelThreadConfigSnapshotFactory.FromSession(session);

        Assert.False(snapshot.ProviderHttpFallbackEnabled);
    }

    [Fact]
    public void KernelThreadSessionBuilder_ShouldApplyTypedThreadStartRequestDynamicToolsAndSandbox()
    {
        const string json = """
        {
          "model": "gpt-5-mini",
          "sandbox": {
            "type": "readOnly",
            "networkAccess": false
          },
          "dynamicTools": [
            {
              "name": "mcp__demo__lookup",
              "tool_name": "lookup",
              "namespace": "mcp__demo",
              "description": "lookup data",
              "server": "demo"
            }
          ],
          "config": {
            "profile": "default",
            "features": {
              "planner": true
            }
          }
        }
        """;

        var request = JsonSerializer.Deserialize<KernelThreadStartRequest>(json, ProtocolJsonOptions);
        Assert.NotNull(request);
        Assert.NotNull(request!.Sandbox);
        Assert.Equal("readOnly", request.Sandbox!.Type);
        Assert.NotNull(request.Config);
        Assert.Equal("default", request.Config!.ToJsonElement().GetProperty("profile").GetString());
        Assert.Single(request.DynamicTools!);
        Assert.Equal("lookup", request.DynamicTools![0].ShortName);

        var session = KernelThreadSessionBuilder
            .FromRecord(new KernelThreadRecord { Id = "thread_builder_typed_001", Cwd = "D:/Repo" }, "gpt-5", "openai", "on-request")
            .ApplyThreadStart(request)
            .Build();

        Assert.Equal("gpt-5-mini", session.Model);
        Assert.Equal("readOnly", session.SandboxMode);
        Assert.Single(session.DynamicTools!);
        Assert.Equal("mcp__demo__lookup", session.DynamicTools![0].FullName);
        Assert.Equal("mcp__demo", session.DynamicTools[0].Namespace);
    }

    [Fact]
    public void KernelThreadProtocolRequests_ShouldDeserializeTypedConfigPayload()
    {
        const string configJson = """
        {
          "model": "gpt-5",
          "config": {
            "profile": "default",
            "features": {
              "planner": true
            }
          }
        }
        """;

        var start = JsonSerializer.Deserialize<KernelThreadStartRequest>(configJson, ProtocolJsonOptions);
        var resume = JsonSerializer.Deserialize<KernelThreadResumeRequest>("""{"threadId":"thread_resume_cfg_001","config":{"profile":"review"}}""", ProtocolJsonOptions);
        var fork = JsonSerializer.Deserialize<KernelThreadForkRequest>("""{"threadId":"thread_fork_cfg_001","config":{"profile":"plan"}}""", ProtocolJsonOptions);

        Assert.NotNull(start?.Config);
        Assert.Equal("default", start!.Config!.ToJsonElement().GetProperty("profile").GetString());
        Assert.NotNull(resume?.Config);
        Assert.Equal("review", resume!.Config!.ToJsonElement().GetProperty("profile").GetString());
        Assert.NotNull(fork?.Config);
        Assert.Equal("plan", fork!.Config!.ToJsonElement().GetProperty("profile").GetString());
    }

    [Fact]
    public void KernelThreadSessionBuilder_ShouldApplyTypedTurnRequestCollaborationOverride()
    {
        const string json = """
        {
          "sandboxPolicy": "readOnly",
          "summary": "brief",
          "effort": "medium",
          "collaborationMode": {
            "mode": "plan",
            "settings": {
              "model": "gpt-5-mini",
              "reasoningEffort": "high",
              "developer_instructions": null
            }
          }
        }
        """;

        var request = JsonSerializer.Deserialize<KernelTurnStartRequest>(json, ProtocolJsonOptions);
        Assert.NotNull(request);
        Assert.NotNull(request!.SandboxPolicy);
        Assert.Equal("readOnly", request.SandboxPolicy!.Type);
        Assert.NotNull(request.CollaborationMode);
        Assert.Equal("plan", request.CollaborationMode!.Mode);
        Assert.Equal("gpt-5-mini", request.CollaborationMode.Model);
        Assert.Equal("high", request.CollaborationMode.ReasoningEffort);
        Assert.True(request.CollaborationMode.DeveloperInstructions.IsSpecified);
        Assert.Null(request.CollaborationMode.DeveloperInstructions.Value);

        var session = new KernelThreadSessionState(
            Model: "gpt-5",
            ModelProvider: "openai",
            ServiceTier: null,
            Cwd: "D:/Repo",
            ApprovalPolicy: "on-request",
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "workspaceWrite" }),
            SandboxMode: "workspaceWrite",
            DeveloperInstructions: "keep me unless explicitly cleared",
            CollaborationMode: KernelCollaborationModeState.CreateDefault("gpt-5", "low", "keep me unless explicitly cleared"));

        var overridden = KernelThreadSessionBuilder
            .FromSession(session)
            .ApplyTurnOverrides(request)
            .Build();

        Assert.Equal("gpt-5-mini", overridden.Model);
        Assert.Equal("readOnly", overridden.SandboxMode);
        Assert.Equal("brief", overridden.ReasoningSummary);
        Assert.Equal("high", overridden.CollaborationMode!.Settings.ReasoningEffort);
        Assert.Equal("plan", overridden.CollaborationMode.Mode);
        Assert.Equal("gpt-5-mini", overridden.CollaborationMode.Settings.Model);
        Assert.Null(overridden.CollaborationMode.Settings.DeveloperInstructions);
    }

    [Fact]
    public void KernelTurnStartRequest_ShouldDeserializeMinimalInteractionEnvelopePayload()
    {
        const string json = """
        {
          "threadId": "thread-envelope-typed-001",
          "interactionEnvelope": {
            "id": "interaction-envelope-typed-001",
            "sourceKind": 0,
            "surface": "cli",
            "createdAtUnixMs": 1746200000000
          }
        }
        """;

        var request = JsonSerializer.Deserialize<KernelTurnStartRequest>(json, ProtocolJsonOptions);
        Assert.NotNull(request);
        Assert.NotNull(request!.InteractionEnvelope);

        var interactionEnvelope = request.InteractionEnvelope!.ToContract();
        Assert.NotNull(interactionEnvelope);
        Assert.Equal("interaction-envelope-typed-001", interactionEnvelope!.Id.Value);
        Assert.Equal(TianShu.Contracts.Interactions.InteractionSourceKind.Host, interactionEnvelope.SourceKind);
        Assert.Equal("cli", interactionEnvelope.Surface);
        Assert.Equal(1_746_200_000_000, interactionEnvelope.CreatedAt.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void KernelThreadSessionBuilder_RawThreadStartPath_ShouldMatchTypedRequestPath()
    {
        const string json = """
        {
          "cwd": "D:/Repo/Sub",
          "model": "gpt-4.1-mini",
          "modelProvider": "openai-custom",
          "approvalPolicy": {
            "granular": {
              "sandbox_approval": true,
              "rules": false,
              "skill_approval": true,
              "request_permissions": false,
              "mcp_elicitations": true
            }
          },
          "serviceTier": "flex",
          "sandbox": {
            "type": "readOnly",
            "networkAccess": false
          },
          "ephemeral": true,
          "serviceName": "demo",
          "baseInstructions": "base prompt",
          "developerInstructions": "developer prompt",
          "personality": "friendly",
          "dynamicTools": [
            {
              "name": "mcp__demo__lookup",
              "tool_name": "lookup",
              "namespace": "mcp__demo",
              "description": "lookup data",
              "server": "demo"
            }
          ],
          "persistExtendedHistory": true
        }
        """;

        using var document = JsonDocument.Parse(json);
        var typedRequest = JsonSerializer.Deserialize<KernelThreadStartRequest>(json, ProtocolJsonOptions);
        Assert.NotNull(typedRequest);

        var record = new KernelThreadRecord { Id = "thread_builder_raw_start_001", Cwd = "D:/Repo" };
        var rawSession = KernelThreadSessionBuilder
            .FromRecord(record, "gpt-5", "openai", "on-request")
            .ApplyThreadStart(document.RootElement.Clone())
            .Build();
        var typedSession = KernelThreadSessionBuilder
            .FromRecord(record, "gpt-5", "openai", "on-request")
            .ApplyThreadStart(typedRequest!)
            .Build();

        Assert.Equal(SerializeComparableSession(typedSession), SerializeComparableSession(rawSession));
    }

    [Fact]
    public void KernelThreadSessionBuilder_RawTurnOverridePath_ShouldMatchTypedRequestPath()
    {
        const string json = """
        {
          "model": "gpt-5-mini",
          "serviceTier": "fast",
          "cwd": "D:/Repo/Review",
          "approvalPolicy": "never",
          "sandboxPolicy": {
            "type": "danger-full-access"
          },
          "summary": "brief",
          "verbosity": "medium",
          "effort": "high",
          "personality": "pragmatic",
          "collaborationMode": {
            "mode": "plan",
            "settings": {
              "model": "gpt-5",
              "reasoningEffort": "medium",
              "developer_instructions": null
            }
          }
        }
        """;

        using var document = JsonDocument.Parse(json);
        var typedRequest = JsonSerializer.Deserialize<KernelTurnStartRequest>(json, ProtocolJsonOptions);
        Assert.NotNull(typedRequest);

        var seedSession = new KernelThreadSessionState(
            Model: "gpt-4.1",
            ModelProvider: "openai",
            ServiceTier: null,
            Cwd: "D:/Repo",
            ApprovalPolicy: "on-request",
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "workspaceWrite" }),
            SandboxMode: "workspaceWrite",
            Personality: "pragmatic",
            DeveloperInstructions: "keep me",
            CollaborationMode: KernelCollaborationModeState.CreateDefault("gpt-4.1", "low", "keep me"));

        var rawSession = KernelThreadSessionBuilder
            .FromSession(seedSession)
            .ApplyTurnOverrides(document.RootElement.Clone())
            .Build();
        var typedSession = KernelThreadSessionBuilder
            .FromSession(seedSession)
            .ApplyTurnOverrides(typedRequest!)
            .Build();

        Assert.Equal(SerializeComparableSession(typedSession), SerializeComparableSession(rawSession));
    }

    private static JsonSerializerOptions CreateProtocolJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new KernelOptionalJsonConverterFactory());
        return options;
    }

    private static string SerializeComparableSession(KernelThreadSessionState session)
    {
        return JsonSerializer.Serialize(new
        {
            session.Model,
            session.ModelProvider,
            ServiceTier = session.ServiceTier?.ToString(),
            ApprovalPolicy = JsonSerializer.Serialize(KernelApprovalPolicyHelpers.ToPayloadValue(session.ApprovalPolicy)),
            SandboxType = session.SandboxPolicy.ValueKind == JsonValueKind.Object
                && session.SandboxPolicy.TryGetProperty("type", out var sandboxType)
                    ? sandboxType.GetString()
                    : null,
            session.SandboxMode,
            session.Cwd,
            session.Ephemeral,
            session.ServiceName,
            session.BaseInstructions,
            session.DeveloperInstructions,
            session.Personality,
            session.ReasoningSummary,
            session.PersistExtendedHistory,
            DynamicTools = session.DynamicTools?.Select(static tool => new
            {
                tool.FullName,
                tool.ShortName,
                tool.Namespace,
                tool.Server,
            }).ToArray(),
            CollaborationMode = session.CollaborationMode is null
                ? null
                : new
                {
                    session.CollaborationMode.Mode,
                    session.CollaborationMode.Settings.Model,
                    session.CollaborationMode.Settings.ReasoningEffort,
                    session.CollaborationMode.Settings.DeveloperInstructions,
                },
        });
    }
}
