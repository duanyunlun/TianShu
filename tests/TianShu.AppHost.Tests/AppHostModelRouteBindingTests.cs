using System.Reflection;
using System.Text.Json;
using TianShu.AppHost.Configuration;
using TianShu.Contracts.Orchestration;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Execution.Runtime;
using TianShu.Provider.Abstractions;
using TianShu.RuntimeComposition;

namespace TianShu.AppHost.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostModelRouteBindingTests
{
    [Fact]
    public async Task BuildTurnRequestContext_WhenPlanMode_UsesPlanningRouteAndPersistsDecision()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var storePath = Path.Combine(root, "threads.json");
        using var tianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(tianShuHome);
            File.WriteAllText(Path.Combine(tianShuHome, "tianshu.toml"), CreateRouteConfig(includeCodingRoute: true));

            var store = new KernelThreadStore(storePath);
            await store.InitializeAsync(CancellationToken.None);
            _ = await store.CreateThreadAsync("thread-plan-route-001", root, CancellationToken.None);
            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                store);
            var session = CreateSession(
                root,
                new KernelCollaborationModeState(
                    KernelCollaborationModeState.PlanMode,
                    new KernelCollaborationModeSettings("session-model", "low", "plan dev")));

            var context = await InvokeBuildTurnRequestContextAsync(server, "thread-plan-route-001", session);

            Assert.Equal("plan-model", context.Model);
            Assert.Equal("plan-provider", context.ModelProvider);
            Assert.Equal(ProviderWireApi.OpenAiChatCompletions, context.ProviderWireApi);
            Assert.Equal("https://plan.example.invalid/v1", context.ProviderBaseUrl);
            Assert.Equal("PLAN_KEY", context.ProviderApiKeyEnvironmentVariable);
            Assert.Equal("workbench", context.ModelRouteSetId);
            Assert.Equal("planning", context.ModelRouteKind);
            Assert.StartsWith("model-route-", context.ModelRouteDiagnosticsCorrelationId, StringComparison.Ordinal);
            Assert.NotNull(context.CollaborationMode);
            Assert.Equal(KernelCollaborationModeState.PlanMode, context.CollaborationMode!.Mode);
            Assert.Equal("plan-model", context.CollaborationMode.Settings.Model);
            Assert.Equal("high", context.CollaborationMode.Settings.ReasoningEffort);
            Assert.Equal("plan dev", context.CollaborationMode.Settings.DeveloperInstructions);
            Assert.Equal("planning", context.StageId);
            Assert.StartsWith("decision-turn-thread-plan-route-001-", context.StageDecisionId, StringComparison.Ordinal);
            Assert.StartsWith("ctx-turn-thread-plan-route-001-", context.ContextPackageId, StringComparison.Ordinal);
            Assert.StartsWith("execution-decision-turn-thread-plan-route-001-", context.ExecutionRequestId, StringComparison.Ordinal);
            Assert.Equal("planning", context.DispatchBinding);
            Assert.Equal(StageExecutorDispatcher.DefaultModelTurnImplementationId, context.DispatchImplementationId);
            Assert.Equal(StageExecutorDispatchKind.ModelTurn.ToString(), context.DispatchKind);
            Assert.NotNull(context.ExecutionDispatchContext);
            Assert.Equal(context.ExecutionRequestId, context.ExecutionDispatchContext!.ExecutionId);

            var stored = await store.GetThreadAsync("thread-plan-route-001", CancellationToken.None);
            var orchestration = stored!.SessionState?.Orchestration;
            Assert.NotNull(orchestration);
            Assert.Equal("planning", orchestration!.CurrentStageId);
            Assert.Equal(context.StageDecisionId, orchestration.LastDecision?.DecisionId);
            Assert.Equal(context.ContextPackageId, orchestration.LastContextPackage?.PackageId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task BuildReviewTurnRequestContext_WhenRouteSetHasOnlyReviewRoute_UsesReviewStageDecision()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var storePath = Path.Combine(root, "threads.json");
        using var tianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(tianShuHome);
            File.WriteAllText(Path.Combine(tianShuHome, "tianshu.toml"), CreateRouteConfig(includeCodingRoute: false));

            var store = new KernelThreadStore(storePath);
            await store.InitializeAsync(CancellationToken.None);
            _ = await store.CreateThreadAsync("thread-review-route-001", root, CancellationToken.None);
            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                store);
            var session = CreateSession(root);

            var context = await InvokeBuildReviewTurnRequestContextAsync(server, "thread-review-route-001", session);

            Assert.Equal("review-model", context.Model);
            Assert.Equal("review-provider", context.ModelProvider);
            Assert.Equal(ProviderWireApi.OpenAiChatCompletions, context.ProviderWireApi);
            Assert.Equal("https://review.example.invalid/v1", context.ProviderBaseUrl);
            Assert.Equal("REVIEW_KEY", context.ProviderApiKeyEnvironmentVariable);
            Assert.Equal("workbench", context.ModelRouteSetId);
            Assert.Equal("review", context.ModelRouteKind);
            Assert.True(context.IsReview);
            Assert.Null(context.DynamicTools);
            Assert.NotNull(context.OutputSchema);
            Assert.Equal("review", context.StageId);

            var stored = await store.GetThreadAsync("thread-review-route-001", CancellationToken.None);
            var orchestration = stored!.SessionState?.Orchestration;
            Assert.NotNull(orchestration);
            Assert.Equal("review", orchestration!.CurrentStageId);
            Assert.Equal("requested-stage", orchestration.LastDecision?.ReasonCode);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task BuildTurnRequestContext_WhenRouteSetLivesInUserModule_LoadsDefaultStageRoute()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var modelRouteSetDirectory = Path.Combine(tianShuHome, "modules", "model", "route-sets");
        var protocolRuleDirectory = Path.Combine(tianShuHome, "modules", "model", "protocol-rules");
        var providerInstanceDirectory = Path.Combine(tianShuHome, "modules", "model", "provider-instances");
        var storePath = Path.Combine(root, "threads.json");
        using var tianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(modelRouteSetDirectory);
            Directory.CreateDirectory(protocolRuleDirectory);
            Directory.CreateDirectory(providerInstanceDirectory);
            File.WriteAllText(Path.Combine(tianShuHome, "tianshu.toml"), """
                model_route_set = "default"
                model_protocol_rule_set = "default"
                provider_instances = "default"
                """);
            File.WriteAllText(Path.Combine(modelRouteSetDirectory, "default.toml"), """
                [model_route_sets.default]
                display_name = "Default"

                [[model_route_sets.default.routes]]
                kind = "default"
                candidates = [
                  { provider = "module-provider", model = "module-default-model" },
                ]
                """);
            File.WriteAllText(Path.Combine(protocolRuleDirectory, "default.toml"), """
                [model_protocol_rule_sets.default]
                display_name = "Default"
                rules = [
                  { match = "module-*", protocols = ["anthropic_messages", "openai_chat_completions"] },
                ]
                """);
            File.WriteAllText(Path.Combine(providerInstanceDirectory, "default.toml"), """
                [providers.module-provider]
                base_url = "https://module.example.invalid/v1"
                api_key_env = "MODULE_KEY"
                """);

            var store = new KernelThreadStore(storePath);
            await store.InitializeAsync(CancellationToken.None);
            _ = await store.CreateThreadAsync("thread-default-route-001", root, CancellationToken.None);
            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                store);
            var session = CreateSession(root, modelRouteSetId: "default");

            var context = await InvokeBuildTurnRequestContextAsync(server, "thread-default-route-001", session);

            Assert.Equal("module-default-model", context.Model);
            Assert.Equal("module-provider", context.ModelProvider);
            Assert.Equal(ProviderWireApi.AnthropicMessages, context.ProviderWireApi);
            Assert.Equal("https://module.example.invalid/v1", context.ProviderBaseUrl);
            Assert.Equal("MODULE_KEY", context.ProviderApiKeyEnvironmentVariable);
            Assert.Equal("default", context.ModelRouteSetId);
            Assert.Equal("default", context.ModelRouteKind);
            Assert.Equal("default", context.StageId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ApplyOrchestrationAndModelRoute_WhenRequestedStageIsConfiguredExtension_UsesExtensionRoute()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var storePath = Path.Combine(root, "threads.json");
        using var tianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(tianShuHome);
            File.WriteAllText(Path.Combine(tianShuHome, "tianshu.toml"), CreateExtensionStageRouteConfig());

            var routingRuntime = CreateRoutingRuntime(
                new KernelThreadStore(storePath),
                Path.Combine(tianShuHome, "tianshu.toml"));
            var session = CreateSession(root);
            var context = new TurnRequestContext(
                Model: "session-model",
                ModelProvider: "fallback-provider",
                ServiceTier: null,
                ApprovalPolicy: KernelApprovalPolicy.Untrusted,
                SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly" }),
                SandboxMode: "read-only",
                Cwd: root,
                ProviderBaseUrl: "https://fallback.example.invalid/v1",
                ProviderApiKeyEnvironmentVariable: "FALLBACK_KEY",
                ProviderWireApi: ProviderWireApi.OpenAiChatCompletions,
                ModelRouteSetId: "workbench");

            var routed = routingRuntime.ApplyOrchestrationAndModelRoute(
                "thread-extension-route-001",
                session,
                context,
                "triage");

            Assert.Equal("triage-model", routed.Model);
            Assert.Equal("triage-provider", routed.ModelProvider);
            Assert.Equal("https://triage.example.invalid/v1", routed.ProviderBaseUrl);
            Assert.Equal("TRIAGE_KEY", routed.ProviderApiKeyEnvironmentVariable);
            Assert.Equal("triage", routed.ModelRouteKind);
            Assert.Equal("triage", routed.StageId);
            Assert.StartsWith("decision-turn-thread-extension-route-001-", routed.StageDecisionId, StringComparison.Ordinal);
            Assert.StartsWith("ctx-turn-thread-extension-route-001-", routed.ContextPackageId, StringComparison.Ordinal);
            Assert.StartsWith("execution-decision-turn-thread-extension-route-001-", routed.ExecutionRequestId, StringComparison.Ordinal);
            Assert.Equal("triage.executor", routed.DispatchBinding);
            Assert.Equal(StageExecutorDispatcher.DefaultModelTurnImplementationId, routed.DispatchImplementationId);
            Assert.Equal(StageExecutorDispatchKind.ModelTurn.ToString(), routed.DispatchKind);
            Assert.NotNull(routed.ExecutionDispatchContext);
            Assert.Equal(routed.ExecutionRequestId, routed.ExecutionDispatchContext!.ExecutionId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ApplyOrchestrationAndModelRoute_WhenRouteResultHasNoBaseUrl_DoesNotInheritContextBaseUrl()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var storePath = Path.Combine(root, "threads.json");
        using var tianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(tianShuHome);
            File.WriteAllText(Path.Combine(tianShuHome, "tianshu.toml"), CreateExtensionStageRouteConfig(includeProviderBaseUrl: false));

            var routingRuntime = CreateRoutingRuntime(
                new KernelThreadStore(storePath),
                Path.Combine(tianShuHome, "tianshu.toml"));
            var session = CreateSession(root);
            var context = new TurnRequestContext(
                Model: "session-model",
                ModelProvider: "fallback-provider",
                ServiceTier: null,
                ApprovalPolicy: KernelApprovalPolicy.Untrusted,
                SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly" }),
                SandboxMode: "read-only",
                Cwd: root,
                ProviderBaseUrl: "https://fallback.example.invalid/v1",
                ProviderApiKeyEnvironmentVariable: "FALLBACK_KEY",
                ProviderWireApi: ProviderWireApi.OpenAiChatCompletions,
                ModelRouteSetId: "workbench");

            var routed = routingRuntime.ApplyOrchestrationAndModelRoute(
                "thread-extension-no-base-url-001",
                session,
                context,
                "triage");

            Assert.Equal("triage-model", routed.Model);
            Assert.Equal("triage-provider", routed.ModelProvider);
            Assert.Null(routed.ProviderBaseUrl);
            Assert.Equal("TRIAGE_KEY", routed.ProviderApiKeyEnvironmentVariable);
            Assert.Equal(ProviderWireApi.OpenAiChatCompletions, routed.ProviderWireApi);
            Assert.Equal("triage", routed.ModelRouteKind);
            Assert.Equal("triage", routed.StageId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ApplyOrchestrationAndModelRouteAsync_WhenStageRegistryIsInvalid_FailsBeforeStateCommit()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var storePath = Path.Combine(root, "threads.json");
        using var tianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(tianShuHome);
            File.WriteAllText(Path.Combine(tianShuHome, "tianshu.toml"), CreateInvalidStageRegistryConfig());

            var store = new KernelThreadStore(storePath);
            await store.InitializeAsync(CancellationToken.None);
            _ = await store.CreateThreadAsync("thread-invalid-registry-001", root, CancellationToken.None);
            var routingRuntime = CreateRoutingRuntime(
                store,
                Path.Combine(tianShuHome, "tianshu.toml"));
            var session = CreateSession(root);
            var context = CreateFallbackTurnContext(root);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                routingRuntime.ApplyOrchestrationAndModelRouteAsync(
                    "thread-invalid-registry-001",
                    session,
                    context,
                    "triage",
                    CancellationToken.None));

            Assert.Contains("Stage Registry 无效", error.Message, StringComparison.Ordinal);
            Assert.Contains("extension_stage_lifecycle_order_missing", error.Message, StringComparison.Ordinal);
            var stored = await store.GetThreadAsync("thread-invalid-registry-001", CancellationToken.None);
            Assert.Null(stored!.SessionState?.Orchestration);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ApplyOrchestrationAndModelRouteAsync_WhenExtensionStageRouteMissing_FailsWithoutDefaultRouteFallback()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var storePath = Path.Combine(root, "threads.json");
        using var tianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(tianShuHome);
            File.WriteAllText(Path.Combine(tianShuHome, "tianshu.toml"), CreateExtensionStageRouteConfig(includeTriageRoute: false));

            var store = new KernelThreadStore(storePath);
            await store.InitializeAsync(CancellationToken.None);
            _ = await store.CreateThreadAsync("thread-missing-extension-route-001", root, CancellationToken.None);
            var routingRuntime = CreateRoutingRuntime(
                store,
                Path.Combine(tianShuHome, "tianshu.toml"));
            var session = CreateSession(root);
            var context = CreateFallbackTurnContext(root);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                routingRuntime.ApplyOrchestrationAndModelRouteAsync(
                    "thread-missing-extension-route-001",
                    session,
                    context,
                    "triage",
                    CancellationToken.None));

            Assert.Contains("model_route_not_found", error.Message, StringComparison.Ordinal);
            Assert.Contains("triage", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("default-model", error.Message, StringComparison.Ordinal);
            var stored = await store.GetThreadAsync("thread-missing-extension-route-001", CancellationToken.None);
            Assert.Null(stored!.SessionState?.Orchestration);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task AppendStageCheckpoint_WhenTurnCompletes_PersistsStageCheckpoint()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var storePath = Path.Combine(root, "threads.json");
        using var tianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(tianShuHome);
            File.WriteAllText(Path.Combine(tianShuHome, "tianshu.toml"), CreateRouteConfig(includeCodingRoute: true));

            var store = new KernelThreadStore(storePath);
            await store.InitializeAsync(CancellationToken.None);
            _ = await store.CreateThreadAsync("thread-checkpoint-route-001", root, CancellationToken.None);
            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                store);
            var session = CreateSession(root);
            var context = await InvokeBuildTurnRequestContextAsync(server, "thread-checkpoint-route-001", session);

            var startedAt = DateTimeOffset.Parse("2026-06-09T00:00:00Z");
            var completedAt = startedAt.AddMinutes(1);
            await InvokeTryCommitTerminalTurnProjectionAsync(
                server,
                "thread-checkpoint-route-001",
                "turn-checkpoint-001",
                context,
                "实现完成",
                "completed",
                startedAt,
                completedAt);

            var stored = await store.GetThreadAsync("thread-checkpoint-route-001", CancellationToken.None);
            var checkpoint = Assert.Single(stored!.SessionState!.Orchestration!.Checkpoints);
            Assert.Equal(context.StageId, checkpoint.StageId);
            Assert.Equal(StageExecutionState.Completed, checkpoint.State);
            Assert.Equal("实现完成", checkpoint.Summary);
            Assert.Equal(context.ModelRouteSetId, checkpoint.ModelRouteSetId);
            Assert.Equal(context.ModelRouteKind, checkpoint.ModelRouteKind);
            Assert.Equal(
                context.ExecutionRequestId,
                checkpoint.Diagnostics?.Properties["executionRequestId"].StringValue);
            Assert.Equal(
                context.DispatchBinding,
                checkpoint.Diagnostics?.Properties["executorBinding"].StringValue);
            Assert.Equal(
                context.DispatchImplementationId,
                checkpoint.Diagnostics?.Properties["executorImplementationId"].StringValue);
            Assert.Equal(
                context.DispatchKind,
                checkpoint.Diagnostics?.Properties["executorDispatchKind"].StringValue);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task AppendStageCheckpoint_WhenExecutionDispatchContextIsMissing_FailsWithoutRebuild()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var storePath = Path.Combine(root, "threads.json");
        using var tianShuHomeScope = new EnvironmentVariableScope("TIANSHU_HOME", tianShuHome);

        try
        {
            Directory.CreateDirectory(tianShuHome);
            File.WriteAllText(Path.Combine(tianShuHome, "tianshu.toml"), CreateRouteConfig(includeCodingRoute: true));

            var store = new KernelThreadStore(storePath);
            await store.InitializeAsync(CancellationToken.None);
            _ = await store.CreateThreadAsync("thread-checkpoint-missing-runtime-001", root, CancellationToken.None);
            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                store);
            var session = CreateSession(root);
            var context = await InvokeBuildTurnRequestContextAsync(server, "thread-checkpoint-missing-runtime-001", session);
            var missingRuntimeContext = context with
            {
                ExecutionDispatchContext = null,
            };
            var startedAt = DateTimeOffset.Parse("2026-06-09T00:00:00Z");
            var completedAt = startedAt.AddMinutes(1);

            var error = await Assert.ThrowsAsync<ArgumentException>(() => InvokeTryCommitTerminalTurnProjectionAsync(
                server,
                "thread-checkpoint-missing-runtime-001",
                "turn-checkpoint-missing-runtime-001",
                missingRuntimeContext,
                "实现完成",
                "completed",
                startedAt,
                completedAt));

            Assert.Equal("turnContext.ExecutionDispatchContext", error.ParamName);
            var stored = await store.GetThreadAsync("thread-checkpoint-missing-runtime-001", CancellationToken.None);
            Assert.Empty(stored!.SessionState!.Orchestration!.Checkpoints);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task<TurnRequestContext> InvokeBuildTurnRequestContextAsync(
        AppHostServer server,
        string threadId,
        KernelThreadSessionState session)
    {
        var method = typeof(AppHostServer).GetMethod(
            "BuildTurnRequestContextAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(string), typeof(KernelThreadSessionState), typeof(CancellationToken)],
            modifiers: null);
        Assert.NotNull(method);

        var result = method!.Invoke(server, [threadId, session, CancellationToken.None]);
        var task = Assert.IsAssignableFrom<Task<TurnRequestContext>>(result);
        return await task.ConfigureAwait(false);
    }

    private static async Task<TurnRequestContext> InvokeBuildReviewTurnRequestContextAsync(
        AppHostServer server,
        string threadId,
        KernelThreadSessionState session)
    {
        var method = typeof(AppHostServer).GetMethod(
            "BuildReviewTurnRequestContextAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(string), typeof(KernelThreadSessionState), typeof(string), typeof(string), typeof(CancellationToken)],
            modifiers: null);
        Assert.NotNull(method);

        var result = method!.Invoke(server, [threadId, session, null, "Review current diff.", CancellationToken.None]);
        var task = Assert.IsAssignableFrom<Task<TurnRequestContext>>(result);
        return await task.ConfigureAwait(false);
    }

    private static async Task InvokeTryCommitTerminalTurnProjectionAsync(
        AppHostServer server,
        string threadId,
        string turnId,
        TurnRequestContext context,
        string assistantText,
        string status,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        var runtime = GetTurnExecutionRuntime(server);
        await runtime.TryCommitTerminalTurnProjectionAsync(
            threadId,
            turnId,
            context,
            reviewOutputText: null,
            reviewFailureMessage: null,
            effectiveUserText: "请完成任务",
            finalAssistantText: assistantText,
            finalTurnStatus: status,
            finalTurnError: null,
            turnStartedAt: startedAt,
            turnCompletedAt: completedAt).ConfigureAwait(false);
    }

    private static KernelTurnExecutionAppHostRuntime GetTurnExecutionRuntime(AppHostServer server)
    {
        var field = typeof(AppHostServer).GetField(
            "turnExecutionAppHostRuntime",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        return Assert.IsAssignableFrom<KernelTurnExecutionAppHostRuntime>(field!.GetValue(server));
    }

    private static AppHostCoreLoopRoutingRuntime CreateRoutingRuntime(KernelThreadStore store, string userConfigPath)
        => new(
            store,
            cwd => KernelConfigSnapshotUtilities.BuildConfigReadSnapshot(
                    includeLayers: false,
                    cwd,
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    userConfigPath)
                .Config,
            Normalize,
            BuildStageCorrelationId);

    private static string BuildStageCorrelationId(string threadId)
        => $"turn-{threadId}-{Guid.NewGuid():N}";

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static KernelThreadSessionState CreateSession(
        string cwd,
        KernelCollaborationModeState? collaborationMode = null,
        string modelRouteSetId = "workbench")
        => new(
            Model: "session-model",
            ModelProvider: "coding-provider",
            ServiceTier: null,
            Cwd: cwd,
            ApprovalPolicy: KernelApprovalPolicy.Untrusted,
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly" }),
            SandboxMode: "read-only",
            AllowLoginShell: false,
            ShellEnvironmentPolicy: new KernelShellEnvironmentPolicy(KernelShellEnvironmentPolicyInherit.Core),
            DynamicTools: [],
            ProviderBaseUrl: "https://coding.example.invalid/v1",
            ProviderApiKeyEnvironmentVariable: "CODING_KEY",
            ProviderWireApi: ProviderWireApi.OpenAiChatCompletions,
            ProviderSupportsWebsockets: false,
            CollaborationMode: collaborationMode,
            PersistExtendedHistory: true,
            WindowsSandboxLevel: KernelWindowsSandboxLevel.Unelevated,
            DefaultModeRequestUserInputEnabled: true,
            ModelRouteSetId: modelRouteSetId);

    private static TurnRequestContext CreateFallbackTurnContext(string cwd)
        => new(
            Model: "session-model",
            ModelProvider: "fallback-provider",
            ServiceTier: null,
            ApprovalPolicy: KernelApprovalPolicy.Untrusted,
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly" }),
            SandboxMode: "read-only",
            Cwd: cwd,
            ProviderBaseUrl: "https://fallback.example.invalid/v1",
            ProviderApiKeyEnvironmentVariable: "FALLBACK_KEY",
            ProviderWireApi: ProviderWireApi.OpenAiChatCompletions,
            ModelRouteSetId: "workbench");

    private static string CreateRouteConfig(bool includeCodingRoute)
    {
        var codingRoute = includeCodingRoute
            ? """

              [[model_route_sets.workbench.routes]]
              kind = "coding"
              candidates = [
                { provider = "coding-provider", model = "coding-model", protocol = "openai_chat_completions" },
              ]
              """
            : string.Empty;

        return $$"""
               model_route_set = "workbench"

               [providers.plan-provider]
               protocol = "openai_chat_completions"
               base_url = "https://plan.example.invalid/v1"
               api_key_env = "PLAN_KEY"

               [providers.plan-provider.reasoning]
               effort = "high"

               [providers.coding-provider]
               protocol = "openai_chat_completions"
               base_url = "https://coding.example.invalid/v1"
               api_key_env = "CODING_KEY"

               [providers.review-provider]
               protocol = "openai_chat_completions"
               base_url = "https://review.example.invalid/v1"
               api_key_env = "REVIEW_KEY"

               [model_route_sets.workbench]
               display_name = "Workbench"

               [[model_route_sets.workbench.routes]]
               kind = "default"
               candidates = [
                 { provider = "coding-provider", model = "default-model", protocol = "openai_chat_completions" },
               ]

               [[model_route_sets.workbench.routes]]
               kind = "planning"
               candidates = [
                 { provider = "plan-provider", model = "plan-model", protocol = "openai_chat_completions" },
               ]
               {{codingRoute}}

               [[model_route_sets.workbench.routes]]
               kind = "review"
               candidates = [
                 { provider = "review-provider", model = "review-model", protocol = "openai_chat_completions" },
               ]
               """;
    }

    private static string CreateExtensionStageRouteConfig(bool includeProviderBaseUrl = true, bool includeTriageRoute = true)
    {
        var providerBaseUrl = includeProviderBaseUrl
            ? """
              base_url = "https://triage.example.invalid/v1"
              """
            : string.Empty;
        var triageRoute = includeTriageRoute
            ? """

              [[model_route_sets.workbench.routes]]
              kind = "triage"
              candidates = [
                { provider = "triage-provider", model = "triage-model", protocol = "openai_chat_completions" },
              ]
              """
            : string.Empty;

        return $$"""
           model_route_set = "workbench"

           [providers.triage-provider]
           protocol = "openai_chat_completions"
           {{providerBaseUrl}}
           api_key_env = "TRIAGE_KEY"

           [model_route_sets.workbench]
           display_name = "Workbench"

           [[model_route_sets.workbench.routes]]
           kind = "default"
           candidates = [
             { provider = "triage-provider", model = "default-model", protocol = "openai_chat_completions" },
           ]
           {{triageRoute}}

           [[stage_registry.stages]]
           id = "triage"
           display_name = "Triage"
           lifecycle_order = 0
           model_route_kind = "triage"
           allowed_previous = ["default"]
           allowed_next = ["planning"]
           context_projection_mode = "summary"
           executor_binding = "triage.executor"
           """;
    }

    private static string CreateInvalidStageRegistryConfig()
        => """
           model_route_set = "workbench"

           [providers.triage-provider]
           protocol = "openai_chat_completions"
           base_url = "https://triage.example.invalid/v1"
           api_key_env = "TRIAGE_KEY"

           [model_route_sets.workbench]
           display_name = "Workbench"

           [[model_route_sets.workbench.routes]]
           kind = "default"
           candidates = [
             { provider = "triage-provider", model = "default-model", protocol = "openai_chat_completions" },
           ]

           [[model_route_sets.workbench.routes]]
           kind = "triage"
           candidates = [
             { provider = "triage-provider", model = "triage-model", protocol = "openai_chat_completions" },
           ]

           [[stage_registry.stages]]
           id = "triage"
           display_name = "Triage"
           model_route_kind = "triage"
           allowed_previous = ["default"]
           """;

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShuAppHostModelRouteTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 测试临时目录清理失败不影响断言结果。
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string name;
        private readonly string? previous;

        public EnvironmentVariableScope(string name, string? value)
        {
            this.name = name;
            previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable(name, previous);
    }
}
