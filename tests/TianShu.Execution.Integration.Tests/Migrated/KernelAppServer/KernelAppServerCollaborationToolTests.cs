using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Reflection;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerCollaborationToolTests
{
    private const string MissingKeyEnvironmentVariable = "TIANSHU_TEST_MISSING_KEY";

    [Fact]
    public async Task ExecuteToolCallAsync_UpdatePlan_ShouldEmitPlanNotification()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            using var args = JsonDocument.Parse("""
                {
                  "explanation": "补齐协议面缺口",
                  "plan": [
                    { "step": "实现 update_plan", "status": "in_progress" },
                    { "step": "补单测", "status": "pending" }
                  ]
                }
                """);

            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_plan_001",
                turnId: "turn_plan_001",
                itemId: "item_plan_001",
                toolName: "update_plan",
                arguments: args.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("Plan updated", result.OutputText);

            var messages = ParseMessages(writer);
            try
            {
                var notification = Assert.Single(messages.Where(static x => IsNotificationMethod(x.RootElement, "turn/plan/updated")));
                var parameters = notification.RootElement.GetProperty("params");
                Assert.Equal("thread_plan_001", parameters.GetProperty("threadId").GetString());
                Assert.Equal("turn_plan_001", parameters.GetProperty("turnId").GetString());
                Assert.Equal("补齐协议面缺口", parameters.GetProperty("explanation").GetString());
                var plan = parameters.GetProperty("plan");
                Assert.Equal(2, plan.GetArrayLength());
                Assert.Equal("实现 update_plan", plan[0].GetProperty("step").GetString());
                Assert.Equal("inProgress", plan[0].GetProperty("status").GetString());
                Assert.Equal("pending", plan[1].GetProperty("status").GetString());

                var toolCalls = messages.Where(static x => IsNotificationMethod(x.RootElement, "item/tool/call")).ToArray();
                Assert.Equal(2, toolCalls.Length);
                Assert.Equal("inProgress", toolCalls[0].RootElement.GetProperty("params").GetProperty("item").GetProperty("status").GetString());
                Assert.Equal("completed", toolCalls[1].RootElement.GetProperty("params").GetProperty("item").GetProperty("status").GetString());
            }
            finally
            {
                DisposeAll(messages);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_UpdatePlan_ShouldRejectPlanMode()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            using var args = JsonDocument.Parse("""
                {
                  "explanation": "补齐协议面缺口",
                  "plan": [
                    { "step": "实现 update_plan", "status": "in_progress" }
                  ]
                }
                """);

            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_plan_002",
                turnId: "turn_plan_002",
                itemId: "item_plan_002",
                toolName: "update_plan",
                arguments: args.RootElement,
                context: CreateTurnContext(
                    root,
                    collaborationMode: new KernelCollaborationModeState(
                        KernelCollaborationModeState.PlanMode,
                        new KernelCollaborationModeSettings("gpt-5", null, null))),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("update_plan is a TODO/checklist tool and is not allowed in Plan mode", result.OutputText);

            var messages = ParseMessages(writer);
            try
            {
                Assert.DoesNotContain(messages, static x => IsNotificationMethod(x.RootElement, "turn/plan/updated"));
            }
            finally
            {
                DisposeAll(messages);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_CollaborationLifecycle_ShouldPreserveRuntimeAndStatusFlow()
    {
        var root = CreateTempDirectory();
        var previousEnv = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
        KernelThreadStore? threadStore = null;

        try
        {
            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_001", root, CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var context = CreateTurnContext(root);

            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "agent_type": "explorer",
                  "fork_context": false,
                  "model": "gpt-5.2",
                  "reasoning_effort": "high"
                }
                """);
            var spawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_001",
                turnId: "turn_parent_001",
                itemId: "item_spawn_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(spawnResult.Success, spawnResult.OutputText);
            using var spawnOutput = JsonDocument.Parse(spawnResult.OutputText);
            var agentId = spawnOutput.RootElement.GetProperty("agent_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(agentId));

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(agentId!, out var childThread));
            Assert.NotNull(childThread);
            Assert.Equal("gpt-5.2", childThread!.Session.Model);
            Assert.Equal("https://example.invalid/v1", childThread!.Session.ProviderBaseUrl);
            Assert.Equal(MissingKeyEnvironmentVariable, childThread.Session.ProviderApiKeyEnvironmentVariable);
            Assert.Equal("responses", childThread.Session.ProviderWireApi);
            Assert.NotNull(childThread.Session.CollaborationMode);
            Assert.Equal("gpt-5.2", childThread.Session.CollaborationMode!.Settings.Model);
            Assert.Equal("high", childThread.Session.CollaborationMode.Settings.ReasoningEffort);

            using var waitArgs = JsonDocument.Parse($$"""
                {
                  "ids": ["{{agentId}}"],
                  "timeout_ms": 10000
                }
                """);
            var initialWaitResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_001",
                turnId: "turn_parent_001",
                itemId: "item_wait_001",
                toolName: "wait",
                arguments: waitArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(initialWaitResult.Success);
            AssertAgentErrored(initialWaitResult.OutputText, agentId!);

            using var closeArgs = JsonDocument.Parse($$"""{ "id": "{{agentId}}" }""");
            var closeResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_001",
                turnId: "turn_parent_001",
                itemId: "item_close_001",
                toolName: "close_agent",
                arguments: closeArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(closeResult.Success, closeResult.OutputText);
            using (var closeOutput = JsonDocument.Parse(closeResult.OutputText))
            {
                Assert.True(closeOutput.RootElement.GetProperty("status").TryGetProperty("errored", out _));
            }

            var closedRecord = await threadStore.GetThreadAsync(agentId!, CancellationToken.None);
            Assert.NotNull(closedRecord);
            Assert.True(closedRecord!.IsArchived);
            Assert.False(threadManager.IsLoaded(agentId!));

            using var resumeArgs = JsonDocument.Parse($$"""{ "id": "{{agentId}}" }""");
            var resumeResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_001",
                turnId: "turn_parent_001",
                itemId: "item_resume_001",
                toolName: "resume_agent",
                arguments: resumeArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(resumeResult.Success, resumeResult.OutputText);
            using (var resumeOutput = JsonDocument.Parse(resumeResult.OutputText))
            {
                Assert.True(resumeOutput.RootElement.GetProperty("status").TryGetProperty("errored", out _));
            }

            var resumedRecord = await threadStore.GetThreadAsync(agentId!, CancellationToken.None);
            Assert.NotNull(resumedRecord);
            Assert.False(resumedRecord!.IsArchived);
            Assert.True(threadManager.IsLoaded(agentId!));

            using var sendArgs = JsonDocument.Parse($$"""
                {
                  "id": "{{agentId}}",
                  "message": "请继续分析",
                  "interrupt": false
                }
                """);
            var sendResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_001",
                turnId: "turn_parent_001",
                itemId: "item_send_001",
                toolName: "send_input",
                arguments: sendArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(sendResult.Success);
            using (var sendOutput = JsonDocument.Parse(sendResult.OutputText))
            {
                Assert.False(string.IsNullOrWhiteSpace(sendOutput.RootElement.GetProperty("submission_id").GetString()));
            }

            using var finalWaitArgs = JsonDocument.Parse($$"""
                {
                  "ids": ["{{agentId}}"],
                  "timeout_ms": 10000
                }
                """);
            var finalWaitResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_001",
                turnId: "turn_parent_001",
                itemId: "item_wait_002",
                toolName: "wait",
                arguments: finalWaitArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(finalWaitResult.Success);
            AssertAgentErrored(finalWaitResult.OutputText, agentId!);

            var messages = ParseMessages(writer);
            try
            {
                var completedToolCalls = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/tool/call"))
                    .Select(x => x.RootElement.GetProperty("params").GetProperty("item"))
                    .Where(static item => string.Equals(item.GetProperty("status").GetString(), "completed", StringComparison.Ordinal))
                    .Select(static item => item.GetProperty("toolName").GetString())
                    .ToArray();

                Assert.Contains("spawn_agent", completedToolCalls);
                Assert.Contains("wait", completedToolCalls);
                Assert.Contains("close_agent", completedToolCalls);
                Assert.Contains("resume_agent", completedToolCalls);
                Assert.Contains("send_input", completedToolCalls);

                var startedCollabItems = messages
                    .Where(static x => IsThreadItemNotification(x.RootElement, "item/started", "collabAgentToolCall"))
                    .ToDictionary(
                        static x => x.RootElement.GetProperty("params").GetProperty("item").GetProperty("id").GetString()!,
                        static x => x.RootElement.GetProperty("params").GetProperty("item"),
                        StringComparer.Ordinal);
                var completedCollabItems = messages
                    .Where(static x => IsThreadItemNotification(x.RootElement, "item/completed", "collabAgentToolCall"))
                    .ToDictionary(
                        static x => x.RootElement.GetProperty("params").GetProperty("item").GetProperty("id").GetString()!,
                        static x => x.RootElement.GetProperty("params").GetProperty("item"),
                        StringComparer.Ordinal);

                Assert.Equal("spawnAgent", startedCollabItems["item_spawn_001"].GetProperty("tool").GetString());
                Assert.Equal("inProgress", startedCollabItems["item_spawn_001"].GetProperty("status").GetString());
                Assert.Equal("请分析当前问题", startedCollabItems["item_spawn_001"].GetProperty("prompt").GetString());
                Assert.Equal("gpt-5.2", startedCollabItems["item_spawn_001"].GetProperty("model").GetString());
                Assert.Equal("high", startedCollabItems["item_spawn_001"].GetProperty("reasoningEffort").GetString());
                Assert.Equal(0, startedCollabItems["item_spawn_001"].GetProperty("receiverThreadIds").GetArrayLength());

                Assert.Equal("spawnAgent", completedCollabItems["item_spawn_001"].GetProperty("tool").GetString());
                Assert.Equal("completed", completedCollabItems["item_spawn_001"].GetProperty("status").GetString());
                Assert.Equal("gpt-5.2", completedCollabItems["item_spawn_001"].GetProperty("model").GetString());
                Assert.Equal("high", completedCollabItems["item_spawn_001"].GetProperty("reasoningEffort").GetString());
                Assert.Equal(1, completedCollabItems["item_spawn_001"].GetProperty("receiverThreadIds").GetArrayLength());
                Assert.Equal("running", completedCollabItems["item_spawn_001"].GetProperty("agentsStates").GetProperty(agentId!).GetProperty("status").GetString());

                Assert.Equal("wait", startedCollabItems["item_wait_001"].GetProperty("tool").GetString());
                Assert.Equal(agentId, startedCollabItems["item_wait_001"].GetProperty("receiverThreadIds")[0].GetString());
                Assert.Equal("failed", completedCollabItems["item_wait_001"].GetProperty("status").GetString());
                Assert.Equal("errored", completedCollabItems["item_wait_001"].GetProperty("agentsStates").GetProperty(agentId!).GetProperty("status").GetString());

                Assert.Equal("closeAgent", startedCollabItems["item_close_001"].GetProperty("tool").GetString());
                Assert.Equal(agentId, startedCollabItems["item_close_001"].GetProperty("receiverThreadIds")[0].GetString());
                Assert.Equal("failed", completedCollabItems["item_close_001"].GetProperty("status").GetString());

                Assert.Equal("resumeAgent", startedCollabItems["item_resume_001"].GetProperty("tool").GetString());
                Assert.Equal(agentId, startedCollabItems["item_resume_001"].GetProperty("receiverThreadIds")[0].GetString());
                Assert.Equal("failed", completedCollabItems["item_resume_001"].GetProperty("status").GetString());

                Assert.Equal("sendInput", startedCollabItems["item_send_001"].GetProperty("tool").GetString());
                Assert.Equal("请继续分析", startedCollabItems["item_send_001"].GetProperty("prompt").GetString());
                Assert.Equal(agentId, startedCollabItems["item_send_001"].GetProperty("receiverThreadIds")[0].GetString());
                Assert.Equal("completed", completedCollabItems["item_send_001"].GetProperty("status").GetString());
                Assert.Equal("running", completedCollabItems["item_send_001"].GetProperty("agentsStates").GetProperty(agentId!).GetProperty("status").GetString());

                Assert.Equal("failed", completedCollabItems["item_wait_002"].GetProperty("status").GetString());
            }
            finally
            {
                DisposeAll(messages);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousEnv);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_WhenRoleConfigPresent_ShouldOverrideRequestedModelReasoningAndDeveloperInstructions()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            var roleConfigPath = Path.Combine(root, "custom-role.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true

                [agents.custom]
                description = "Custom role"
                config_file = "custom-role.toml"
                nickname_candidates = ["Atlas"]
                """);
            await File.WriteAllTextAsync(roleConfigPath, """
                model = "gpt-5.1-codex-max"
                model_reasoning_effort = "high"
                developer_instructions = "Stay focused"
                """);

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_role_001", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore,
                cliConfigFilePath: sessionConfigPath);
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "agent_type": "custom",
                  "fork_context": false,
                  "model": "gpt-5.4",
                  "reasoning_effort": "low"
                }
                """);
            var spawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_role_001",
                turnId: "turn_parent_role_001",
                itemId: "item_spawn_role_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(spawnResult.Success);
            using var spawnOutput = JsonDocument.Parse(spawnResult.OutputText);
            var agentId = spawnOutput.RootElement.GetProperty("agent_id").GetString();
            var nickname = spawnOutput.RootElement.GetProperty("nickname").GetString();
            Assert.False(string.IsNullOrWhiteSpace(agentId));
            Assert.Equal("Atlas", nickname);

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(agentId!, out var childThread));
            Assert.NotNull(childThread);
            Assert.Equal("gpt-5.1-codex-max", childThread!.Session.Model);
            Assert.Equal("high", childThread.Session.CollaborationMode!.Settings.ReasoningEffort);
            Assert.Equal("Stay focused", childThread.Session.DeveloperInstructions);
            Assert.Equal("Atlas", childThread.Session.SessionSource?.SubAgentSource?.AgentNickname);
            Assert.Equal("custom", childThread.Session.SessionSource?.SubAgentSource?.AgentRole);

            var childRecord = await threadStore.GetThreadAsync(agentId!, CancellationToken.None);
            Assert.NotNull(childRecord);
            Assert.Equal("Atlas", childRecord!.AgentNickname);
            Assert.Equal("custom", childRecord!.AgentRole);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_WhenRequestedModelProvidedWithoutReasoning_ShouldUseModelDefaultReasoningEffort()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_model_default_reasoning_001", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore);
            var expectedReasoningEffort = ProviderModelCatalogs.GetDefaultReasoningEffort("gpt-5.4");
            Assert.NotEqual("low", expectedReasoningEffort);

            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "fork_context": false,
                  "model": "gpt-5.4"
                }
                """);
            var spawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_model_default_reasoning_001",
                turnId: "turn_parent_model_default_reasoning_001",
                itemId: "item_spawn_model_default_reasoning_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: CreateTurnContext(
                    root,
                    collaborationMode: KernelCollaborationModeState.CreateDefault("gpt-5", "low")),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(spawnResult.Success);
            using var spawnOutput = JsonDocument.Parse(spawnResult.OutputText);
            var agentId = spawnOutput.RootElement.GetProperty("agent_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(agentId));

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(agentId!, out var childThread));
            Assert.NotNull(childThread);
            Assert.Equal("gpt-5.4", childThread!.Session.Model);
            Assert.Equal(expectedReasoningEffort, childThread.Session.CollaborationMode!.Settings.ReasoningEffort);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_WhenRequestedModelUnknown_ShouldFailWithTianShuStyleError()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_unknown_model_001", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore);
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "fork_context": false,
                  "model": "gpt-5.4-mini"
                }
                """);
            var spawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_unknown_model_001",
                turnId: "turn_parent_unknown_model_001",
                itemId: "item_spawn_unknown_model_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            var availableModels = string.Join(", ", ProviderModelCatalogs.ListModels().Select(static model => model.Model));
            Assert.False(spawnResult.Success);
            Assert.Equal(
                $"Unknown model `gpt-5.4-mini` for spawn_agent. Available models: {availableModels}",
                spawnResult.OutputText);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_WhenRequestedReasoningEffortUnsupported_ShouldFailWithTianShuStyleError()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_invalid_reasoning_001", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore);
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "fork_context": false,
                  "model": "gpt-5.1-codex-mini",
                  "reasoning_effort": "xhigh"
                }
                """);
            var spawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_invalid_reasoning_001",
                turnId: "turn_parent_invalid_reasoning_001",
                itemId: "item_spawn_invalid_reasoning_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.False(spawnResult.Success);
            Assert.Equal(
                "Reasoning effort `xhigh` is not supported for model `gpt-5.1-codex-mini`. Supported reasoning efforts: medium, high",
                spawnResult.OutputText);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_WhenNicknamePoolIsExhausted_ShouldResetPoolAndAppendOrdinalSuffix()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true

                [agents.custom]
                description = "Custom role"
                nickname_candidates = ["Plato"]
                """);

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_nickname_pool_001", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore,
                cliConfigFilePath: sessionConfigPath);
            var context = CreateTurnContext(root);

            async Task<string> SpawnCustomAgentAsync(string turnId, string itemId)
            {
                using var args = JsonDocument.Parse("""
                    {
                      "message": "请分析当前问题",
                      "agent_type": "custom",
                      "fork_context": false
                    }
                    """);
                var result = await server.ExecuteToolCallAsync(
                    threadId: "thread_parent_nickname_pool_001",
                    turnId: turnId,
                    itemId: itemId,
                    toolName: "spawn_agent",
                    arguments: args.RootElement,
                    context: context,
                    toolCallGate: null,
                    cancellationToken: CancellationToken.None);
                Assert.True(result.Success);
                using var output = JsonDocument.Parse(result.OutputText);
                return output.RootElement.GetProperty("agent_id").GetString()!;
            }

            var firstAgentId = await SpawnCustomAgentAsync("turn_nickname_pool_001", "item_spawn_nickname_pool_001");
            var firstRecord = await threadStore.GetThreadAsync(firstAgentId, CancellationToken.None);
            Assert.NotNull(firstRecord);
            Assert.Equal("Plato", firstRecord!.AgentNickname);

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            var releaseNicknameMethod = typeof(AppHostServer).GetMethod(
                "ReleaseSpawnAgentNicknameReservation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(releaseNicknameMethod);
            releaseNicknameMethod!.Invoke(server, [firstAgentId]);
            threadManager.MarkUnloaded(firstAgentId);
            Assert.False(threadManager.IsLoaded(firstAgentId));

            var secondAgentId = await SpawnCustomAgentAsync("turn_nickname_pool_003", "item_spawn_nickname_pool_002");
            var secondRecord = await threadStore.GetThreadAsync(secondAgentId, CancellationToken.None);
            Assert.NotNull(secondRecord);
            Assert.Equal("Plato the 2nd", secondRecord!.AgentNickname);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_WhenRoleNicknameCandidatesConfigured_ShouldUseRoleSpecificNickname()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true

                [agents.custom]
                description = "Custom role"
                nickname_candidates = ["Hermes"]
                """);

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_role_nickname_001", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore,
                cliConfigFilePath: sessionConfigPath);
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "agent_type": "custom",
                  "fork_context": false
                }
                """);
            var spawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_role_nickname_001",
                turnId: "turn_parent_role_nickname_001",
                itemId: "item_spawn_role_nickname_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(spawnResult.Success);
            using var spawnOutput = JsonDocument.Parse(spawnResult.OutputText);
            var agentId = spawnOutput.RootElement.GetProperty("agent_id").GetString();
            var nickname = spawnOutput.RootElement.GetProperty("nickname").GetString();
            Assert.Equal("Hermes", nickname);
            Assert.False(string.IsNullOrWhiteSpace(agentId));

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(agentId!, out var childThread));
            Assert.NotNull(childThread);
            Assert.Equal("Hermes", childThread!.Session.SessionSource.SubAgentSource?.AgentNickname);
            Assert.Equal("custom", childThread.Session.SessionSource.SubAgentSource?.AgentRole);

            var childRecord = await threadStore.GetThreadAsync(agentId!, CancellationToken.None);
            Assert.NotNull(childRecord);
            Assert.Equal("Hermes", childRecord!.AgentNickname);
            Assert.Equal("custom", childRecord.AgentRole);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_WhenAgentTypeOmitted_ShouldUseDefaultRoleConfigForNicknameAndOverridesWithoutPersistingDefaultRole()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            var roleConfigPath = Path.Combine(root, "default-role.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true

                [agents.default]
                description = "Default role"
                config_file = "default-role.toml"
                nickname_candidates = ["Athena"]
                """);
            await File.WriteAllTextAsync(roleConfigPath, """
                model = "gpt-5.1-codex-max"
                model_reasoning_effort = "high"
                developer_instructions = "Stay focused"
                """);

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_default_role_001", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore,
                cliConfigFilePath: sessionConfigPath);
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "fork_context": false,
                  "model": "gpt-5.4",
                  "reasoning_effort": "low"
                }
                """);
            var spawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_default_role_001",
                turnId: "turn_parent_default_role_001",
                itemId: "item_spawn_default_role_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(spawnResult.Success);
            using var spawnOutput = JsonDocument.Parse(spawnResult.OutputText);
            var agentId = spawnOutput.RootElement.GetProperty("agent_id").GetString();
            var nickname = spawnOutput.RootElement.GetProperty("nickname").GetString();
            Assert.Equal("Athena", nickname);
            Assert.False(string.IsNullOrWhiteSpace(agentId));

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(agentId!, out var childThread));
            Assert.NotNull(childThread);
            Assert.Equal("gpt-5.1-codex-max", childThread!.Session.Model);
            Assert.Equal("high", childThread.Session.CollaborationMode!.Settings.ReasoningEffort);
            Assert.Equal("Stay focused", childThread.Session.DeveloperInstructions);
            Assert.Equal("Athena", childThread.Session.SessionSource.SubAgentSource?.AgentNickname);
            Assert.Null(childThread.Session.SessionSource.SubAgentSource?.AgentRole);

            var childRecord = await threadStore.GetThreadAsync(agentId!, CancellationToken.None);
            Assert.NotNull(childRecord);
            Assert.Equal("Athena", childRecord!.AgentNickname);
            Assert.Null(childRecord.AgentRole);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ResumeAgentFromToolAsync_WhenStoredNicknameAndRoleExist_ShouldRestoreSessionSourceMetadata()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true

                [agents.custom]
                description = "Custom role"
                nickname_candidates = ["Hermes"]
                """);

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_resume_nickname_001", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore,
                cliConfigFilePath: sessionConfigPath);
            var context = CreateTurnContext(root);

            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "agent_type": "custom",
                  "fork_context": false
                }
                """);
            var spawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_nickname_001",
                turnId: "turn_parent_resume_nickname_001",
                itemId: "item_spawn_resume_nickname_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(spawnResult.Success);
            using var spawnOutput = JsonDocument.Parse(spawnResult.OutputText);
            var agentId = spawnOutput.RootElement.GetProperty("agent_id").GetString();
            var nickname = spawnOutput.RootElement.GetProperty("nickname").GetString();
            Assert.Equal("Hermes", nickname);
            Assert.False(string.IsNullOrWhiteSpace(agentId));

            using var waitArgs = JsonDocument.Parse($$"""
                {
                  "ids": ["{{agentId}}"],
                  "timeout_ms": 10000
                }
                """);
            var waitResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_nickname_001",
                turnId: "turn_parent_resume_nickname_001",
                itemId: "item_wait_resume_nickname_001",
                toolName: "wait",
                arguments: waitArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(waitResult.Success);

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            var releaseNicknameMethod = typeof(AppHostServer).GetMethod(
                "ReleaseSpawnAgentNicknameReservation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(releaseNicknameMethod);
            releaseNicknameMethod!.Invoke(server, [agentId]);
            threadManager.MarkUnloaded(agentId!);
            Assert.False(threadManager.IsLoaded(agentId!));

            using var resumeArgs = JsonDocument.Parse($$"""{ "id": "{{agentId}}" }""");
            var resumeResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_nickname_001",
                turnId: "turn_parent_resume_nickname_001",
                itemId: "item_resume_nickname_001",
                toolName: "resume_agent",
                arguments: resumeArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(resumeResult.Success);

            Assert.True(threadManager.TryGetThread(agentId!, out var childThread));
            Assert.NotNull(childThread);
            Assert.Equal("Hermes", childThread!.Session.SessionSource.SubAgentSource?.AgentNickname);
            Assert.Equal("custom", childThread.Session.SessionSource.SubAgentSource?.AgentRole);

            var childRecord = await threadStore.GetThreadAsync(agentId!, CancellationToken.None);
            Assert.NotNull(childRecord);
            Assert.Equal("Hermes", childRecord!.AgentNickname);
            Assert.Equal("custom", childRecord.AgentRole);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_ResumeAgent_WhenAgentMissing_ShouldFailWithTianShuStyleMessage()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_resume_missing_001", root, CancellationToken.None);

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);
            using var resumeArgs = JsonDocument.Parse("""{ "id": "thread_missing_resume_001" }""");
            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_missing_001",
                turnId: "turn_parent_resume_missing_001",
                itemId: "item_resume_missing_001",
                toolName: "resume_agent",
                arguments: resumeArgs.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("agent with id thread_missing_resume_001 not found", result.OutputText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_SendInput_WhenAgentMissing_ShouldFailWithTianShuStyleMessage()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_send_missing_001", root, CancellationToken.None);

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);
            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            using var sendArgs = JsonDocument.Parse("""
                {
                  "id": "thread_missing_send_001",
                  "message": "请继续分析",
                  "interrupt": false
                }
                """);
            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_send_missing_001",
                turnId: "turn_parent_send_missing_001",
                itemId: "item_send_missing_001",
                toolName: "send_input",
                arguments: sendArgs.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("agent with id thread_missing_send_001 not found", result.OutputText);
            Assert.Null(await threadStore.GetThreadAsync("thread_missing_send_001", CancellationToken.None));
            Assert.False(threadManager.TryGetThread("thread_missing_send_001", out _));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetAgentStatusNodeAsync_WhenAgentMissing_ShouldReturnNotFound()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_status_missing_001", root, CancellationToken.None);

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);
            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            var status = await InvokeGetAgentStatusNodeAsync(
                server,
                "thread_missing_status_001",
                treatArchivedAsNotFound: true);

            Assert.Equal("\"not_found\"", status?.ToJsonString());
            Assert.Null(await threadStore.GetThreadAsync("thread_missing_status_001", CancellationToken.None));
            Assert.False(threadManager.TryGetThread("thread_missing_status_001", out _));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetAgentStatusNodeAsync_WhenThreadHasNoTurns_ShouldReturnPendingInit()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_pending_init_001", root, CancellationToken.None);

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);
            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            var status = await InvokeGetAgentStatusNodeAsync(
                server,
                "thread_pending_init_001",
                treatArchivedAsNotFound: true);

            Assert.Equal("\"pending_init\"", status?.ToJsonString());
            Assert.False(threadManager.TryGetThread("thread_pending_init_001", out _));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetAgentStatusNodeAsync_WhenThreadStatusIsStaleActiveButLatestTurnInterrupted_ShouldReturnErrored()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            var record = await threadStore.CreateThreadAsync("thread_stale_active_interrupted_001", root, CancellationToken.None);
            record.StatusType = "active";
            record.Turns.Add(new KernelTurnRecord
            {
                Id = "turn_stale_active_interrupted_001",
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
                CompletedAt = DateTimeOffset.UtcNow,
                Status = "interrupted",
            });
            _ = await threadStore.UpsertThreadAsync(record, CancellationToken.None);

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);
            var status = await InvokeGetAgentStatusNodeAsync(
                server,
                "thread_stale_active_interrupted_001",
                treatArchivedAsNotFound: true);

            Assert.Equal("{\"errored\":\"Interrupted\"}", status?.ToJsonString());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetAgentStatusNodeAsync_WhenLatestTurnIsStaleInProgressWithoutLiveTurn_ShouldReturnErrored()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            var record = await threadStore.CreateThreadAsync("thread_stale_in_progress_001", root, CancellationToken.None);
            record.StatusType = "active";
            record.Turns.Add(new KernelTurnRecord
            {
                Id = "turn_stale_in_progress_001",
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
                CompletedAt = DateTimeOffset.UtcNow,
                Status = "inProgress",
            });
            _ = await threadStore.UpsertThreadAsync(record, CancellationToken.None);

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);
            var status = await InvokeGetAgentStatusNodeAsync(
                server,
                "thread_stale_in_progress_001",
                treatArchivedAsNotFound: true);

            Assert.Equal("{\"errored\":\"Interrupted\"}", status?.ToJsonString());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetAgentStatusNodeAsync_WhenActiveTurnOnlyHasCompletedTaskMarker_ShouldReturnCompleted()
    {
        var root = CreateTempDirectory();
        CancellationTokenSource? staleTurnCts = null;
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            var record = await threadStore.CreateThreadAsync("thread_stale_completed_task_marker_001", root, CancellationToken.None);
            record.StatusType = "active";
            record.Turns.Add(new KernelTurnRecord
            {
                Id = "turn_stale_completed_task_marker_001",
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
                CompletedAt = DateTimeOffset.UtcNow,
                Status = "completed",
                AssistantMessage = "child done",
            });
            _ = await threadStore.UpsertThreadAsync(record, CancellationToken.None);

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);
            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            var runtimeThread = threadManager.AttachThread(record, CreateSessionState(root), loaded: true, publishCreated: false);
            runtimeThread.SetActiveTurn("turn_stale_completed_task_marker_001");

            staleTurnCts = new CancellationTokenSource();
            var runningTurns = GetPrivateField<ConcurrentDictionary<string, CancellationTokenSource>>(server, "runningTurns");
            Assert.True(runningTurns.TryAdd("turn_stale_completed_task_marker_001", staleTurnCts));

            var runningTurnTasks = GetPrivateField<ConcurrentDictionary<string, Task>>(server, "runningTurnTasks");
            runningTurnTasks["turn_stale_completed_task_marker_001"] = Task.CompletedTask;

            var status = await InvokeGetAgentStatusNodeAsync(
                server,
                "thread_stale_completed_task_marker_001",
                treatArchivedAsNotFound: true);

            Assert.Equal("{\"completed\":\"child done\"}", status?.ToJsonString());
            Assert.True(string.IsNullOrWhiteSpace(runtimeThread.ActiveTurnId));
            Assert.False(runningTurns.ContainsKey("turn_stale_completed_task_marker_001"));
            Assert.False(runningTurnTasks.ContainsKey("turn_stale_completed_task_marker_001"));
            staleTurnCts = null;
        }
        finally
        {
            staleTurnCts?.Dispose();
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ResumeAgentFromToolAsync_WhenParentTurnOverridesRuntimeState_ShouldRebuildSessionFromCurrentTurn()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_resume_runtime_001", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore);
            var initialContext = CreateTurnContext(root, developerInstructions: "Parent developer baseline");
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "agent_type": "explorer",
                  "fork_context": false
                }
                """);
            var spawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_runtime_001",
                turnId: "turn_parent_resume_runtime_001",
                itemId: "item_spawn_resume_runtime_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: initialContext,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(spawnResult.Success, spawnResult.OutputText);

            using var spawnOutput = JsonDocument.Parse(spawnResult.OutputText);
            var agentId = spawnOutput.RootElement.GetProperty("agent_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(agentId));

            using var waitArgs = JsonDocument.Parse($$"""
                {
                  "ids": ["{{agentId}}"],
                  "timeout_ms": 10000
                }
                """);
            var waitResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_runtime_001",
                turnId: "turn_parent_resume_runtime_002",
                itemId: "item_wait_resume_runtime_001",
                toolName: "wait",
                arguments: waitArgs.RootElement,
                context: initialContext,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(waitResult.Success, waitResult.OutputText);

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(agentId!, out var childThreadBeforeResume));
            Assert.NotNull(childThreadBeforeResume);
            var baseInstructionsBeforeResume = childThreadBeforeResume!.Session.BaseInstructions;

            using var closeArgs = JsonDocument.Parse($$"""{ "id": "{{agentId}}" }""");
            var closeResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_runtime_001",
                turnId: "turn_parent_resume_runtime_003",
                itemId: "item_close_resume_runtime_001",
                toolName: "close_agent",
                arguments: closeArgs.RootElement,
                context: initialContext,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(closeResult.Success, closeResult.OutputText);

            var resumedCwd = Path.Combine(root, "resume-cwd");
            Directory.CreateDirectory(resumedCwd);
            var resumedCollaborationMode = KernelCollaborationModeState.CreateDefault(
                "gpt-5.1-codex-mini",
                "high",
                "Resume developer instructions");
            var resumedContext = CreateTurnContext(
                root,
                model: "gpt-5.1-codex-mini",
                modelProvider: "ollama",
                approvalPolicy: "on-request",
                sandboxMode: "danger-full-access",
                cwd: resumedCwd,
                providerBaseUrl: "https://resume.invalid/v1",
                providerApiKeyEnvironmentVariable: "TIANSHU_RESUME_RUNTIME_KEY",
                providerWireApi: "responses",
                developerInstructions: "Resume developer instructions",
                reasoningSummary: "detailed",
                verbosity: "high",
                collaborationMode: resumedCollaborationMode);

            using var resumeArgs = JsonDocument.Parse($$"""{ "id": "{{agentId}}" }""");
            var resumeResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_runtime_001",
                turnId: "turn_parent_resume_runtime_004",
                itemId: "item_resume_runtime_001",
                toolName: "resume_agent",
                arguments: resumeArgs.RootElement,
                context: resumedContext,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(resumeResult.Success, resumeResult.OutputText);

            Assert.True(threadManager.TryGetThread(agentId!, out var childThreadAfterResume));
            Assert.NotNull(childThreadAfterResume);
            Assert.Equal("gpt-5.1-codex-mini", childThreadAfterResume!.Session.Model);
            Assert.Equal("ollama", childThreadAfterResume.Session.ModelProvider);
            Assert.Equal("on-request", KernelApprovalPolicyHelpers.NormalizeScalar(childThreadAfterResume.Session.ApprovalPolicy));
            Assert.Equal("danger-full-access", childThreadAfterResume.Session.SandboxMode);
            Assert.Equal(resumedCwd, childThreadAfterResume.Session.Cwd);
            Assert.Equal("https://resume.invalid/v1", childThreadAfterResume.Session.ProviderBaseUrl);
            Assert.Equal("TIANSHU_RESUME_RUNTIME_KEY", childThreadAfterResume.Session.ProviderApiKeyEnvironmentVariable);
            Assert.Equal("responses", childThreadAfterResume.Session.ProviderWireApi);
            Assert.Equal("Resume developer instructions", childThreadAfterResume.Session.DeveloperInstructions);
            Assert.Equal("detailed", childThreadAfterResume.Session.ReasoningSummary);
            Assert.Equal("high", childThreadAfterResume.Session.Verbosity);
            Assert.Equal(baseInstructionsBeforeResume, childThreadAfterResume.Session.BaseInstructions);
            Assert.NotNull(childThreadAfterResume.Session.CollaborationMode);
            Assert.Equal("gpt-5.1-codex-mini", childThreadAfterResume.Session.CollaborationMode!.Settings.Model);
            Assert.Equal("high", childThreadAfterResume.Session.CollaborationMode.Settings.ReasoningEffort);
            Assert.Equal("Resume developer instructions", childThreadAfterResume.Session.CollaborationMode.Settings.DeveloperInstructions);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_Wait_WhenTimeoutIsNonPositive_ShouldFailWithTianShuStyleMessage()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_wait_timeout_001", root, CancellationToken.None);

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);
            using var waitArgs = JsonDocument.Parse("""
                {
                  "ids": ["thread_wait_timeout_001"],
                  "timeout_ms": 0
                }
                """);
            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_wait_timeout_001",
                turnId: "turn_parent_wait_timeout_001",
                itemId: "item_wait_timeout_001",
                toolName: "wait",
                arguments: waitArgs.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("timeout_ms must be greater than zero", result.OutputText);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveConfiguredSpawnAgentPositiveInt_WhenValueMissing_ShouldReturnTianShuDefaults()
    {
        var emptyConfig = new Dictionary<string, object?>(StringComparer.Ordinal);
        var maxThreads = KernelSpawnAgentGuardAppHostRuntime.ResolveConfiguredSpawnAgentPositiveInt(
            emptyConfig,
            ["agents", "max_threads"],
            6,
            "agents.max_threads");
        var maxDepth = KernelSpawnAgentGuardAppHostRuntime.ResolveConfiguredSpawnAgentPositiveInt(
            emptyConfig,
            ["agents", "max_depth"],
            1,
            "agents.max_depth");

        Assert.Equal(6, maxThreads);
        Assert.Equal(1, maxDepth);
    }

    [Fact]
    public void ResolveConfiguredSpawnAgentPositiveInt_WhenLegacyCamelCaseValuePresent_ShouldStillResolve()
    {
        var config = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["agents"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["maxThreads"] = 4,
                ["maxDepth"] = 2,
            },
        };

        var maxThreads = KernelSpawnAgentGuardAppHostRuntime.ResolveConfiguredSpawnAgentPositiveInt(
            config,
            ["agents", "max_threads"],
            6,
            "agents.max_threads");
        var maxDepth = KernelSpawnAgentGuardAppHostRuntime.ResolveConfiguredSpawnAgentPositiveInt(
            config,
            ["agents", "max_depth"],
            1,
            "agents.max_depth");

        Assert.Equal(4, maxThreads);
        Assert.Equal(2, maxDepth);
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_WhenConfiguredMaxThreadsLimitReached_ShouldFailWithTianShuStyleMessage()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true

                [agents]
                max_threads = 1
                """);

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_max_threads_001", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore,
                cliConfigFilePath: sessionConfigPath);
            var context = CreateTurnContext(root);
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "agent_type": "explorer",
                  "fork_context": false
                }
                """);

            var firstSpawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_001",
                turnId: "turn_parent_max_threads_001",
                itemId: "item_spawn_max_threads_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(firstSpawnResult.Success);

            var secondSpawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_001",
                turnId: "turn_parent_max_threads_002",
                itemId: "item_spawn_max_threads_002",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.False(secondSpawnResult.Success);
            Assert.Equal("agent thread limit reached (max 1)", secondSpawnResult.OutputText);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_WhenServersShareStorePath_ShouldShareMaxThreadsLimit()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? firstThreadStore = null;
        KernelThreadStore? secondThreadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            var storePath = Path.Combine(root, "threads.json");
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true

                [agents]
                max_threads = 1
                """);

            firstThreadStore = new KernelThreadStore(storePath);
            await firstThreadStore.InitializeAsync(CancellationToken.None);
            _ = await firstThreadStore.CreateThreadAsync("thread_parent_max_threads_shared_001", root, CancellationToken.None);

            secondThreadStore = new KernelThreadStore(storePath);
            await secondThreadStore.InitializeAsync(CancellationToken.None);

            var firstServer = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                firstThreadStore,
                cliConfigFilePath: sessionConfigPath);
            var secondServer = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                secondThreadStore,
                cliConfigFilePath: sessionConfigPath);
            var context = CreateTurnContext(root);
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "agent_type": "explorer",
                  "fork_context": false
                }
                """);

            var firstSpawnResult = await firstServer.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_shared_001",
                turnId: "turn_parent_max_threads_shared_001",
                itemId: "item_spawn_max_threads_shared_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(firstSpawnResult.Success);

            using var firstSpawnOutput = JsonDocument.Parse(firstSpawnResult.OutputText);
            var firstAgentId = firstSpawnOutput.RootElement.GetProperty("agent_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(firstAgentId));

            var secondSpawnResult = await secondServer.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_shared_001",
                turnId: "turn_parent_max_threads_shared_002",
                itemId: "item_spawn_max_threads_shared_002",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.False(secondSpawnResult.Success);
            Assert.Equal("agent thread limit reached (max 1)", secondSpawnResult.OutputText);

            using var waitArgs = JsonDocument.Parse($$"""
                {
                  "ids": ["{{firstAgentId}}"],
                  "timeout_ms": 10000
                }
                """);
            var waitResult = await firstServer.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_shared_001",
                turnId: "turn_parent_max_threads_shared_003",
                itemId: "item_wait_max_threads_shared_001",
                toolName: "wait",
                arguments: waitArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(waitResult.Success, waitResult.OutputText);

            using var closeArgs = JsonDocument.Parse($$"""{ "id": "{{firstAgentId}}" }""");
            var closeResult = await firstServer.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_shared_001",
                turnId: "turn_parent_max_threads_shared_004",
                itemId: "item_close_max_threads_shared_001",
                toolName: "close_agent",
                arguments: closeArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(closeResult.Success, closeResult.OutputText);

            var thirdSpawnResult = await secondServer.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_shared_001",
                turnId: "turn_parent_max_threads_shared_005",
                itemId: "item_spawn_max_threads_shared_003",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(thirdSpawnResult.Success, thirdSpawnResult.OutputText);
        }
        finally
        {
            if (secondThreadStore is not null)
            {
                await secondThreadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            if (firstThreadStore is not null)
            {
                await firstThreadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_WhenConfiguredMaxDepthLimitReached_ShouldFailWithTianShuStyleMessage()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true

                [agents]
                max_depth = 1
                """);

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_max_depth_001", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore,
                cliConfigFilePath: sessionConfigPath);
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "agent_type": "explorer",
                  "fork_context": false
                }
                """);

            var parentContext = CreateTurnContext(root);
            var firstSpawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_max_depth_001",
                turnId: "turn_parent_max_depth_001",
                itemId: "item_spawn_max_depth_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: parentContext,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(firstSpawnResult.Success);

            using var firstSpawnOutput = JsonDocument.Parse(firstSpawnResult.OutputText);
            var firstAgentId = firstSpawnOutput.RootElement.GetProperty("agent_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(firstAgentId));

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(firstAgentId!, out var firstChildThread));
            Assert.NotNull(firstChildThread);

            var childContext = CreateTurnContext(root, sessionSource: firstChildThread!.Session.SessionSource);
            var secondSpawnResult = await server.ExecuteToolCallAsync(
                threadId: firstAgentId!,
                turnId: "turn_parent_max_depth_002",
                itemId: "item_spawn_max_depth_002",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: childContext,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.False(secondSpawnResult.Success);
            Assert.Equal("Agent depth limit reached. Solve the task yourself.", secondSpawnResult.OutputText);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CloseAgentFromToolAsync_WhenConfiguredMaxThreadsLimitExists_ShouldReleaseSlotForNextSpawn()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true

                [agents]
                max_threads = 1
                """);

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_max_threads_002", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore,
                cliConfigFilePath: sessionConfigPath);
            var context = CreateTurnContext(root);
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "agent_type": "explorer",
                  "fork_context": false
                }
                """);

            var firstSpawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_002",
                turnId: "turn_parent_max_threads_003",
                itemId: "item_spawn_max_threads_003",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(firstSpawnResult.Success);
            using var firstSpawnOutput = JsonDocument.Parse(firstSpawnResult.OutputText);
            var firstAgentId = firstSpawnOutput.RootElement.GetProperty("agent_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(firstAgentId));

            using var waitArgs = JsonDocument.Parse($$"""
                {
                  "ids": ["{{firstAgentId}}"],
                  "timeout_ms": 10000
                }
                """);
            var waitResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_002",
                turnId: "turn_parent_max_threads_004",
                itemId: "item_wait_max_threads_001",
                toolName: "wait",
                arguments: waitArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(waitResult.Success);

            using var closeArgs = JsonDocument.Parse($$"""{ "id": "{{firstAgentId}}" }""");
            var closeResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_002",
                turnId: "turn_parent_max_threads_005",
                itemId: "item_close_max_threads_001",
                toolName: "close_agent",
                arguments: closeArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(closeResult.Success);

            var secondSpawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_002",
                turnId: "turn_parent_max_threads_006",
                itemId: "item_spawn_max_threads_004",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(secondSpawnResult.Success);
            using var secondSpawnOutput = JsonDocument.Parse(secondSpawnResult.OutputText);
            var secondAgentId = secondSpawnOutput.RootElement.GetProperty("agent_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(secondAgentId));
            Assert.NotEqual(firstAgentId, secondAgentId);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ResumeAgentFromToolAsync_WhenConfiguredMaxDepthLimitReached_ShouldFailWithTianShuStyleMessage()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true

                [agents]
                max_depth = 1
                """);

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_resume_depth_001", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore,
                cliConfigFilePath: sessionConfigPath);
            var parentContext = CreateTurnContext(root);
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "agent_type": "explorer",
                  "fork_context": false
                }
                """);

            var firstSpawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_depth_001",
                turnId: "turn_parent_resume_depth_001",
                itemId: "item_spawn_resume_depth_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: parentContext,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(firstSpawnResult.Success);
            using var firstSpawnOutput = JsonDocument.Parse(firstSpawnResult.OutputText);
            var firstAgentId = firstSpawnOutput.RootElement.GetProperty("agent_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(firstAgentId));

            var secondSpawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_depth_001",
                turnId: "turn_parent_resume_depth_002",
                itemId: "item_spawn_resume_depth_002",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: parentContext,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(secondSpawnResult.Success);
            using var secondSpawnOutput = JsonDocument.Parse(secondSpawnResult.OutputText);
            var secondAgentId = secondSpawnOutput.RootElement.GetProperty("agent_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(secondAgentId));

            using var waitArgs = JsonDocument.Parse($$"""
                {
                  "ids": ["{{secondAgentId}}"],
                  "timeout_ms": 10000
                }
                """);
            var waitResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_depth_001",
                turnId: "turn_parent_resume_depth_003",
                itemId: "item_wait_resume_depth_001",
                toolName: "wait",
                arguments: waitArgs.RootElement,
                context: parentContext,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(waitResult.Success);

            using var closeArgs = JsonDocument.Parse($$"""{ "id": "{{secondAgentId}}" }""");
            var closeResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_depth_001",
                turnId: "turn_parent_resume_depth_004",
                itemId: "item_close_resume_depth_001",
                toolName: "close_agent",
                arguments: closeArgs.RootElement,
                context: parentContext,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(closeResult.Success, closeResult.OutputText);

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(firstAgentId!, out var firstChildThread));
            Assert.NotNull(firstChildThread);

            var childContext = CreateTurnContext(root, sessionSource: firstChildThread!.Session.SessionSource);
            using var resumeArgs = JsonDocument.Parse($$"""{ "id": "{{secondAgentId}}" }""");
            var resumeResult = await server.ExecuteToolCallAsync(
                threadId: firstAgentId!,
                turnId: "turn_parent_resume_depth_005",
                itemId: "item_resume_depth_001",
                toolName: "resume_agent",
                arguments: resumeArgs.RootElement,
                context: childContext,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.False(resumeResult.Success);
            Assert.Equal("Agent depth limit reached. Solve the task yourself.", resumeResult.OutputText);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ResumeAgentFromToolAsync_WhenConfiguredMaxThreadsLimitReached_ShouldFailWithTianShuStyleMessage()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true

                [agents]
                max_threads = 1
                """);

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_max_threads_003", root, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore,
                cliConfigFilePath: sessionConfigPath);
            var context = CreateTurnContext(root);
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "agent_type": "explorer",
                  "fork_context": false
                }
                """);

            var firstSpawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_003",
                turnId: "turn_parent_max_threads_007",
                itemId: "item_spawn_max_threads_005",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(firstSpawnResult.Success);
            using var firstSpawnOutput = JsonDocument.Parse(firstSpawnResult.OutputText);
            var firstAgentId = firstSpawnOutput.RootElement.GetProperty("agent_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(firstAgentId));

            using var waitArgs = JsonDocument.Parse($$"""
                {
                  "ids": ["{{firstAgentId}}"],
                  "timeout_ms": 10000
                }
                """);
            var waitResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_003",
                turnId: "turn_parent_max_threads_008",
                itemId: "item_wait_max_threads_002",
                toolName: "wait",
                arguments: waitArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(waitResult.Success);

            using var closeArgs = JsonDocument.Parse($$"""{ "id": "{{firstAgentId}}" }""");
            var closeResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_003",
                turnId: "turn_parent_max_threads_009",
                itemId: "item_close_max_threads_002",
                toolName: "close_agent",
                arguments: closeArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(closeResult.Success);

            var secondSpawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_003",
                turnId: "turn_parent_max_threads_010",
                itemId: "item_spawn_max_threads_006",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(secondSpawnResult.Success);

            using var resumeArgs = JsonDocument.Parse($$"""{ "id": "{{firstAgentId}}" }""");
            var resumeResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_max_threads_003",
                turnId: "turn_parent_max_threads_011",
                itemId: "item_resume_max_threads_001",
                toolName: "resume_agent",
                arguments: resumeArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.False(resumeResult.Success);
            Assert.Equal("agent thread limit reached (max 1)", resumeResult.OutputText);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ResumeAgentFromToolAsync_WhenResumeFails_ShouldReleaseReservedSlotForNextSpawn()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true

                [agents]
                max_threads = 1
                """);

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_resume_failure_001", root, CancellationToken.None);

            var resumableRecord = CreateThreadRecord("thread_resume_failure_missing_rollout_001", root, agentNickname: "Hermes");
            resumableRecord.AgentRole = "explorer";
            resumableRecord.IsArchived = true;
            _ = await threadStore.UpsertThreadAsync(resumableRecord, CancellationToken.None);

            var server = new AppHostServer(
                new StringReader(string.Empty),
                new StringWriter(),
                threadStore,
                cliConfigFilePath: sessionConfigPath);
            var context = CreateTurnContext(root);
            using var resumeArgs = JsonDocument.Parse("""{ "id": "thread_resume_failure_missing_rollout_001" }""");
            var resumeResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_failure_001",
                turnId: "turn_parent_resume_failure_001",
                itemId: "item_resume_failure_001",
                toolName: "resume_agent",
                arguments: resumeArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.False(resumeResult.Success);
            Assert.Contains(
                "no archived rollout found for thread id thread_resume_failure_missing_rollout_001",
                resumeResult.OutputText,
                StringComparison.Ordinal);

            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "agent_type": "explorer",
                  "fork_context": false
                }
                """);
            var spawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_resume_failure_001",
                turnId: "turn_parent_resume_failure_002",
                itemId: "item_spawn_after_resume_failure_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: context,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(spawnResult.Success, spawnResult.OutputText);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TurnStart_WhenSpawnAgentRolesConfigured_ShouldExposeRoleAwareAgentTypeDescription()
    {
        var root = CreateTempDirectory();
        var previousOpenAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            var roleConfigPath = Path.Combine(root, "custom-role.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                model = "gpt-5"

                [features]
                multi_agent = true

                [agents.custom]
                description = "Custom role"
                config_file = "custom-role.toml"
                """);
            await File.WriteAllTextAsync(roleConfigPath, """
                model = "gpt-5.1-codex-max"
                model_reasoning_effort = "high"
                developer_instructions = "Stay focused"
                """);

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_role_description_001", root, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler(
            [
                BuildAssistantMessageStream("resp-role-description-001", "DONE"),
            ]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId = "thread_role_description_001",
                    input = new[]
                    {
                        new { text = "hello" },
                    },
                },
            });

            var writer = new StringWriter();
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var server = new AppHostServer(
                reader,
                writer,
                threadStore,
                cliConfigFilePath: sessionConfigPath,
                httpClient: httpClient);
            await server.RunAsync(CancellationToken.None);
            await WaitUntilAsync(
                () => Task.FromResult(writer.ToString().Contains("\"method\":\"turn/completed\"", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(5));

            Assert.Single(handler.RequestBodies);
            using var request = JsonDocument.Parse(handler.RequestBodies[0]);
            var description = ExtractToolParameterDescription(request.RootElement, "spawn_agent", "agent_type");
            Assert.False(string.IsNullOrWhiteSpace(description));
            Assert.Contains("custom: {", description, StringComparison.Ordinal);
            Assert.Contains("Custom role", description, StringComparison.Ordinal);
            Assert.Contains("gpt-5.1-codex-max", description, StringComparison.Ordinal);
            Assert.Contains("high", description, StringComparison.Ordinal);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previousOpenAiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_WhenForkContextEnabled_ShouldCarryParentLiveTurnIntoChildThread()
    {
        var root = CreateTempDirectory();
        var previousEnv = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, null);
        KernelThreadStore? threadStore = null;

        try
        {
            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            var parentRecord = await threadStore.CreateThreadAsync("thread_parent_live_fork_001", root, CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");

            var buildDefaultThreadSessionMethod = typeof(AppHostServer).GetMethod(
                "BuildDefaultThreadSession",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(buildDefaultThreadSessionMethod);

            var parentSession = Assert.IsType<KernelThreadSessionState>(
                buildDefaultThreadSessionMethod!.Invoke(server, [parentRecord]));
            var parentRuntime = threadManager.AttachThread(parentRecord, parentSession, loaded: true, publishCreated: false);

            const string parentTurnId = "turn_parent_live_fork_001";
            parentRuntime.SetActiveTurn(parentTurnId);

            var seedTrackedTurnUserMessageMethod = typeof(AppHostServer).GetMethod(
                "SeedTrackedTurnUserMessage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(seedTrackedTurnUserMessageMethod);
            seedTrackedTurnUserMessageMethod!.Invoke(server, [parentRecord.Id, parentTurnId, "父线程实时问题", null!]);

            var tryTrackTurnNotificationMethod = typeof(AppHostServer).GetMethod(
                "TryTrackTurnNotification",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(string), typeof(JsonElement)],
                modifiers: null);
            Assert.NotNull(tryTrackTurnNotificationMethod);
            var liveAgentMessageNotification = JsonSerializer.SerializeToElement(new
            {
                threadId = parentRecord.Id,
                turnId = parentTurnId,
                item = new
                {
                    id = "item_parent_agent_message_001",
                    type = "agentMessage",
                    text = "父线程实时回答",
                },
            });
            tryTrackTurnNotificationMethod!.Invoke(server, ["item/completed", liveAgentMessageNotification]);

            var spawnAgentMethod = typeof(AppHostServer).GetMethod(
                "SpawnAgentFromToolAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(spawnAgentMethod);

            var request = new KernelSpawnAgentRequest(
                Message: "子代理任务",
                Items: null,
                AgentType: "explorer",
                ForkContext: true,
                Model: null,
                ReasoningEffort: null);
            var invocation = spawnAgentMethod!.Invoke(
                server,
                [
                    parentRecord.Id,
                    CreateTurnContext(root),
                    request,
                    CancellationToken.None,
                ]);

            var task = Assert.IsAssignableFrom<Task<KernelSpawnAgentResponse>>(invocation);
            var response = await task;

            var childRecord = await threadStore.GetThreadAsync(response.AgentId, CancellationToken.None);
            Assert.NotNull(childRecord);

            var forkedTurn = Assert.Single(childRecord!.Turns.Where(static turn => string.Equals(turn.Id, parentTurnId, StringComparison.Ordinal)));
            Assert.Equal("inProgress", forkedTurn.Status);

            var userMessageItem = Assert.Single(forkedTurn.Items.Where(static item => string.Equals(item.Type, "userMessage", StringComparison.Ordinal)));
            Assert.Equal("父线程实时问题", userMessageItem.Payload.GetProperty("content")[0].GetProperty("text").GetString());

            var agentMessageItem = Assert.Single(forkedTurn.Items.Where(static item => string.Equals(item.Type, "agentMessage", StringComparison.Ordinal)));
            Assert.Equal("父线程实时回答", agentMessageItem.Payload.GetProperty("text").GetString());
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousEnv);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_WhenItemsProvided_ShouldSendStructuredCurrentUserMessageToChildRequest()
    {
        var root = CreateTempDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, "test-key");

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_items_spawn_001", root, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler(
            [
                BuildAssistantMessageStream("resp-child-items-spawn-001", "CHILD"),
            ]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore, httpClient: httpClient);
            using var args = JsonDocument.Parse(
                """
                {
                  "items": [
                    { "type": "mention", "name": "worker-1", "path": "app://worker-1" },
                    { "type": "text", "text": "请继续处理" },
                    { "type": "local_image", "path": "D:/images/demo.png" }
                  ]
                }
                """);

            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_items_spawn_001",
                turnId: "turn_parent_items_spawn_001",
                itemId: "item_spawn_items_001",
                toolName: "spawn_agent",
                arguments: args.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);
            await WaitUntilAsync(
                () => Task.FromResult(handler.RequestBodies.Count > 0),
                TimeSpan.FromSeconds(5));

            using var request = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
            var input = request.RootElement.GetProperty("input").EnumerateArray().ToArray();
            var currentUser = input
                .Last(item => item.TryGetProperty("role", out var roleElement)
                    && string.Equals(roleElement.GetString(), "user", StringComparison.Ordinal));
            var content = currentUser.GetProperty("content");

            Assert.Equal(3, content.GetArrayLength());
            Assert.Equal("[mention:$worker-1](app://worker-1)", content[0].GetProperty("text").GetString());
            Assert.Equal("请继续处理", content[1].GetProperty("text").GetString());
            Assert.Equal("[local_image:D:/images/demo.png]", content[2].GetProperty("text").GetString());
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SendInputToAgentFromToolAsync_WhenItemsProvidedAndAgentIdle_ShouldSendStructuredCurrentUserMessageToChildRequest()
    {
        var root = CreateTempDirectory();
        var previousApiKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, "test-key");

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_child_items_send_001", root, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler(
            [
                BuildAssistantMessageStream("resp-child-items-send-001", "CHILD"),
            ]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore, httpClient: httpClient);
            using var args = JsonDocument.Parse(
                """
                {
                  "id": "thread_child_items_send_001",
                  "items": [
                    { "type": "mention", "name": "worker-1", "path": "app://worker-1" },
                    { "type": "text", "text": "新的结构化任务" },
                    { "type": "local_image", "path": "D:/images/demo.png" }
                  ]
                }
                """);

            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_items_send_001",
                turnId: "turn_parent_items_send_001",
                itemId: "item_send_items_001",
                toolName: "send_input",
                arguments: args.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);
            await WaitUntilAsync(
                () => Task.FromResult(handler.RequestBodies.Count > 0),
                TimeSpan.FromSeconds(5));

            using var request = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
            var input = request.RootElement.GetProperty("input").EnumerateArray().ToArray();
            var currentUser = input
                .Last(item => item.TryGetProperty("role", out var roleElement)
                    && string.Equals(roleElement.GetString(), "user", StringComparison.Ordinal));
            var content = currentUser.GetProperty("content");

            Assert.Equal(3, content.GetArrayLength());
            Assert.Equal("[mention:$worker-1](app://worker-1)", content[0].GetProperty("text").GetString());
            Assert.Equal("新的结构化任务", content[1].GetProperty("text").GetString());
            Assert.Equal("[local_image:D:/images/demo.png]", content[2].GetProperty("text").GetString());
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_WhenForkContextEnabled_ShouldPreserveParentSpawnCallAndInjectSyntheticOutputIntoChildRequest()
    {
        const string forkedSpawnAgentOutputMessage = "You are the newly spawned agent. The prior conversation history was forked from your parent agent. Treat the next user message as your new task, and use the forked history only as background context.";

        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        var previousOpenAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        KernelThreadStore? threadStore = null;

        try
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, "test-key");
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            var parentRecord = await threadStore.CreateThreadAsync("thread_parent_fork_output_001", root, CancellationToken.None);

            var handler = new CapturingSequencedSseHandler(
            [
                BuildAssistantMessageStream("resp-child-fork-output-001", "child done"),
            ]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore, httpClient: httpClient);
            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");

            var buildDefaultThreadSessionMethod = typeof(AppHostServer).GetMethod(
                "BuildDefaultThreadSession",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(buildDefaultThreadSessionMethod);

            var parentSession = Assert.IsType<KernelThreadSessionState>(
                buildDefaultThreadSessionMethod!.Invoke(server, [parentRecord]));
            var parentRuntime = threadManager.AttachThread(parentRecord, parentSession, loaded: true, publishCreated: false);

            const string parentTurnId = "turn_parent_fork_output_001";
            const string parentSpawnCallId = "call_spawn_fork_output_001";
            parentRuntime.SetActiveTurn(parentTurnId);

            var seedTrackedTurnUserMessageMethod = typeof(AppHostServer).GetMethod(
                "SeedTrackedTurnUserMessage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(seedTrackedTurnUserMessageMethod);
            seedTrackedTurnUserMessageMethod!.Invoke(server, [parentRecord.Id, parentTurnId, "父线程实时问题", null!]);

            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "子代理任务",
                  "fork_context": true
                }
                """);
            var tryTrackTurnNotificationMethod = typeof(AppHostServer).GetMethod(
                "TryTrackTurnNotification",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(string), typeof(JsonElement)],
                modifiers: null);
            Assert.NotNull(tryTrackTurnNotificationMethod);
            var parentFunctionCallNotification = JsonSerializer.SerializeToElement(new
            {
                threadId = parentRecord.Id,
                turnId = parentTurnId,
                item = new
                {
                    id = parentSpawnCallId,
                    type = "function_call",
                    name = "spawn_agent",
                    arguments = spawnArgs.RootElement.GetRawText(),
                    call_id = parentSpawnCallId,
                },
            });
            tryTrackTurnNotificationMethod!.Invoke(server, ["item/completed", parentFunctionCallNotification]);

            var spawnResult = await server.ExecuteToolCallAsync(
                threadId: parentRecord.Id,
                turnId: parentTurnId,
                itemId: "item_spawn_fork_output_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None,
                externalCallId: parentSpawnCallId);

            Assert.True(spawnResult.Success);
            await WaitUntilAsync(
                () => Task.FromResult(handler.RequestBodies.Count > 0),
                TimeSpan.FromSeconds(5));

            Assert.Single(handler.RequestBodies);
            using var request = JsonDocument.Parse(handler.RequestBodies[0]);
            var inputItems = request.RootElement.GetProperty("input").EnumerateArray().ToArray();
            var parentFunctionCallIndex = Array.FindIndex(
                inputItems,
                item => item.TryGetProperty("type", out var typeElement)
                    && string.Equals(typeElement.GetString(), "function_call", StringComparison.Ordinal)
                    && item.TryGetProperty("call_id", out var callIdElement)
                    && string.Equals(callIdElement.GetString(), parentSpawnCallId, StringComparison.Ordinal));
            var functionCallOutputIndex = Array.FindIndex(
                inputItems,
                item => item.TryGetProperty("type", out var typeElement)
                    && string.Equals(typeElement.GetString(), "function_call_output", StringComparison.Ordinal)
                    && item.TryGetProperty("call_id", out var callIdElement)
                    && string.Equals(callIdElement.GetString(), parentSpawnCallId, StringComparison.Ordinal));
            var childUserIndex = Array.FindIndex(
                inputItems,
                static item => item.TryGetProperty("role", out var roleElement)
                    && string.Equals(roleElement.GetString(), "user", StringComparison.Ordinal)
                    && item.TryGetProperty("content", out var contentElement)
                    && contentElement.ValueKind == JsonValueKind.Array
                    && contentElement.EnumerateArray().Any(content =>
                        content.TryGetProperty("text", out var textElement)
                        && string.Equals(textElement.GetString(), "子代理任务", StringComparison.Ordinal)));

            Assert.True(parentFunctionCallIndex >= 0);
            Assert.True(functionCallOutputIndex > parentFunctionCallIndex);
            Assert.True(childUserIndex > functionCallOutputIndex);

            var functionCall = inputItems[parentFunctionCallIndex];
            Assert.Equal("spawn_agent", functionCall.GetProperty("name").GetString());
            Assert.Equal(spawnArgs.RootElement.GetRawText(), functionCall.GetProperty("arguments").GetString());

            var functionCallOutput = inputItems[functionCallOutputIndex];
            var outputElement = functionCallOutput.GetProperty("output");
            Assert.Equal(JsonValueKind.String, outputElement.ValueKind);
            Assert.Equal(forkedSpawnAgentOutputMessage, outputElement.GetString());
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previousOpenAiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SpawnAgentFromToolAsync_ShouldInjectSubagentNotificationIntoParentNextTurnWithoutWait()
    {
        var root = CreateTempDirectory();
        var previousMissingKey = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        var previousOpenAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, "test-key");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        KernelThreadStore? threadStore = null;

        try
        {
            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_notify_001", root, CancellationToken.None);
            _ = await threadStore.AppendCompletedTurnAsync(
                "thread_parent_notify_001",
                "turn_parent_notify_seed_001",
                "父线程上一轮问题",
                "父线程上一轮回答",
                "completed",
                CancellationToken.None);

            var handler = new CapturingSequencedSseHandler(
            [
                BuildAssistantMessageStream("resp-child-notify-001", "CHILD DONE"),
                BuildAssistantMessageStream("resp-parent-notify-001", "PARENT DONE"),
            ]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore, httpClient: httpClient);
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "子代理任务",
                  "agent_type": "explorer",
                  "fork_context": false
                }
                """);
            var spawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_notify_001",
                turnId: "turn_parent_notify_001",
                itemId: "item_spawn_notify_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(spawnResult.Success);
            using var spawnOutput = JsonDocument.Parse(spawnResult.OutputText);
            var agentId = spawnOutput.RootElement.GetProperty("agent_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(agentId));

            await WaitUntilAsync(async () =>
            {
                var parentRecord = await threadStore.GetThreadAsync("thread_parent_notify_001", CancellationToken.None);
                return parentRecord is not null
                    && parentRecord.SeedHistory.Any(item => (item.Content ?? string.Empty).Contains(agentId!, StringComparison.Ordinal));
            }, TimeSpan.FromSeconds(5));

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId = "thread_parent_notify_001",
                    input = new[]
                    {
                        new { text = "继续处理" },
                    },
                },
            });

            var writer = new StringWriter();
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var parentServer = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);
            await parentServer.RunAsync(CancellationToken.None);
            await WaitUntilAsync(
                () => Task.FromResult(writer.ToString().Contains("\"method\":\"turn/completed\"", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(5));

            Assert.Equal(2, handler.RequestCount);
            using var parentRequest = JsonDocument.Parse(handler.RequestBodies[1]);
            var relevantMessages = ExtractResponsesMessageSequence(parentRequest.RootElement.GetProperty("input"))
                .Where(static entry =>
                    entry.Text.Contains("父线程上一轮问题", StringComparison.Ordinal)
                    || entry.Text.Contains("父线程上一轮回答", StringComparison.Ordinal)
                    || entry.Text.Contains("<subagent_notification>", StringComparison.Ordinal)
                    || entry.Text.Contains("继续处理", StringComparison.Ordinal))
                .ToArray();

            Assert.True(
                relevantMessages.Length == 4,
                string.Join(
                    " | ",
                    relevantMessages.Select(static entry => $"{entry.Role}:{entry.Text}")));
            Assert.Equal(("user", "父线程上一轮问题"), relevantMessages[0]);
            Assert.Equal(("assistant", "父线程上一轮回答"), relevantMessages[1]);
            Assert.Equal("user", relevantMessages[2].Role);
            Assert.Contains("<subagent_notification>", relevantMessages[2].Text, StringComparison.Ordinal);
            Assert.Contains(agentId, relevantMessages[2].Text, StringComparison.Ordinal);
            Assert.Contains("\"completed\":\"CHILD DONE\"", relevantMessages[2].Text, StringComparison.Ordinal);
            Assert.Equal(("user", "继续处理"), relevantMessages[3]);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousMissingKey);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previousOpenAiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task MaybeStartSubagentCompletionWatcher_WhenChildMissing_ShouldAppendNotFoundNotificationToParentSeedHistory()
    {
        var root = CreateTempDirectory();
        KernelThreadStore? threadStore = null;

        try
        {
            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_notify_missing_001", root, CancellationToken.None);

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);
            const string childThreadId = "thread_missing_child_notify_001";
            InvokeMaybeStartSubagentCompletionWatcher(
                server,
                childThreadId,
                KernelSessionSource.SubAgent(
                    KernelSubAgentSource.ThreadSpawn(
                        "thread_parent_notify_missing_001",
                        1,
                        agentNickname: null,
                        agentRole: "explorer")));

            await WaitUntilAsync(async () =>
            {
                var parentRecord = await threadStore.GetThreadAsync("thread_parent_notify_missing_001", CancellationToken.None);
                return parentRecord is not null
                    && parentRecord.SeedHistory.Any(item => (item.Content ?? string.Empty).Contains(childThreadId, StringComparison.Ordinal));
            }, TimeSpan.FromSeconds(5));

            var parent = await threadStore.GetThreadAsync("thread_parent_notify_missing_001", CancellationToken.None);
            Assert.NotNull(parent);
            var notification = Assert.Single(parent!.SeedHistory.Where(
                static item => KernelSubagentNotificationUtilities.IsNotificationHistoryItem(item)));
            Assert.Contains($"\"agent_id\":\"{childThreadId}\"", notification.Content, StringComparison.Ordinal);
            Assert.Contains("\"status\":\"not_found\"", notification.Content, StringComparison.Ordinal);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public void BuildTurnRequestContext_WhenLoadedThreadSpawnSubagentsExist_ShouldAttachSortedEnvironmentContextSubagents()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);
            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");

            threadManager.AttachThread(
                CreateThreadRecord("thread_child_b", root),
                CreateSessionState(
                    root,
                    KernelSessionSource.SubAgent(KernelSubAgentSource.ThreadSpawn(
                        "thread_parent_subagents_001",
                        depth: 1,
                        agentNickname: "Boreas"))),
                loaded: true,
                publishCreated: false);
            threadManager.AttachThread(
                CreateThreadRecord("thread_child_a", root, agentNickname: "Atlas"),
                CreateSessionState(
                    root,
                    KernelSessionSource.SubAgent(KernelSubAgentSource.ThreadSpawn(
                        "thread_parent_subagents_001",
                        depth: 1,
                        agentNickname: "ShouldBeIgnored"))),
                loaded: true,
                publishCreated: false);
            threadManager.AttachThread(
                CreateThreadRecord("thread_child_review", root, agentNickname: "Reviewer"),
                CreateSessionState(root, KernelSessionSource.SubAgent(KernelSubAgentSource.Review)),
                loaded: true,
                publishCreated: false);
            threadManager.AttachThread(
                CreateThreadRecord("thread_child_other_parent", root, agentNickname: "OtherParent"),
                CreateSessionState(
                    root,
                    KernelSessionSource.SubAgent(KernelSubAgentSource.ThreadSpawn(
                        "thread_parent_subagents_999",
                        depth: 1,
                        agentNickname: "OtherParent"))),
                loaded: true,
                publishCreated: false);
            threadManager.AttachThread(
                CreateThreadRecord("thread_child_unloaded", root, agentNickname: "Dormant"),
                CreateSessionState(
                    root,
                    KernelSessionSource.SubAgent(KernelSubAgentSource.ThreadSpawn(
                        "thread_parent_subagents_001",
                        depth: 1,
                        agentNickname: "Dormant"))),
                loaded: false,
                publishCreated: false);

            var method = typeof(AppHostServer).GetMethod(
                "BuildTurnRequestContext",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(string), typeof(KernelThreadSessionState)],
                modifiers: null);
            Assert.NotNull(method);

            var context = Assert.IsType<TurnRequestContext>(method!.Invoke(
                server,
                ["thread_parent_subagents_001", CreateSessionState(root)]));

            Assert.Equal(
                "- thread_child_a: Atlas" + Environment.NewLine + "- thread_child_b: Boreas",
                context.EnvironmentContextSubagents);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveContextualUserMessages_WhenEnvironmentContextSubagentsPresent_ShouldSerializeTianShuStyleXmlBlock()
    {
        var root = CreateTempDirectory();
        try
        {
            var context = CreateTurnContext(root) with
            {
                EnvironmentContextSubagents = "- thread_child_a: Atlas\n- thread_child_b",
            };

            var messages = Assert.IsAssignableFrom<IReadOnlyList<string>>(
                KernelTurnExecutionRuntimeHelpers.ResolveContextualUserMessages(context));
            var serialized = Assert.Single(messages);

            Assert.Equal(
                """
                <environment_context>
                  <subagents>
                    - thread_child_a: Atlas
                    - thread_child_b
                  </subagents>
                </environment_context>
                """.ReplaceLineEndings(Environment.NewLine),
                serialized);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_ToolSearch_ShouldSearchTurnDynamicTools()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var dynamicTools = KernelDynamicToolResolver.Parse(JsonSerializer.SerializeToElement(new object[]
            {
                new
                {
                    name = "mcp__codex_apps__calendar__create_event",
                    server = "dynamic",
                    connectorName = "Calendar",
                    connectorDescription = "Calendar tools.",
                    description = "Create a calendar event.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            title = new { type = "string" },
                            attendees = new { type = "array" },
                        },
                    },
                },
                new
                {
                    name = "drive_search_files",
                    server = "dynamic",
                    description = "Search drive files by keyword.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            keyword = new { type = "string" },
                        },
                    },
                },
            }));
            using var args = JsonDocument.Parse("""
                {
                  "query": "calendar event",
                  "limit": 1
                }
                """);

            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_search_001",
                turnId: "turn_search_001",
                itemId: "item_search_001",
                toolName: "tool_search",
                arguments: args.RootElement,
                context: CreateTurnContext(root, dynamicTools),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);
            using var output = JsonDocument.Parse(result.OutputText);
            var tools = output.RootElement.GetProperty("tools");
            Assert.Single(tools.EnumerateArray());
            Assert.Equal("namespace", tools[0].GetProperty("type").GetString());
            Assert.Equal("mcp__codex_apps__calendar", tools[0].GetProperty("name").GetString());
            Assert.Equal("create_event", tools[0].GetProperty("tools")[0].GetProperty("name").GetString());

            var messages = ParseMessages(writer);
            try
            {
                var completed = messages
                    .Where(static x => IsNotificationMethod(x.RootElement, "item/tool/call"))
                    .Select(x => x.RootElement.GetProperty("params").GetProperty("item"))
                    .Single(static item => string.Equals(item.GetProperty("status").GetString(), "completed", StringComparison.Ordinal));
                Assert.Equal("tool_search", completed.GetProperty("toolName").GetString());
            }
            finally
            {
                DisposeAll(messages);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task ExecuteToolCallAsync_ReportAgentJobResult_ShouldRecordAssignedWorkerResult()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var manager = GetPrivateField<KernelAgentOrchestrationManager>(server, "agentOrchestrationManager");
            _ = await manager.CreateJobAsync(
                jobId: "job_csv_001",
                name: "csv batch",
                instruction: "process csv",
                inputHeadersJson: "[]",
                inputCsvPath: Path.Combine(root, "input.csv"),
                outputCsvPath: Path.Combine(root, "output.csv"),
                autoExport: true,
                items: new[]
                {
                    (ItemId: (string?)"item_001", SourceId: (string?)"row_001", RowJson: "{\"value\":\"alpha\"}")
                },
                cancellationToken: CancellationToken.None);
            _ = await manager.DispatchAsync("job_csv_001", new[] { "thread_worker_001" }, CancellationToken.None);

            using var args = JsonDocument.Parse("""
                {
                  "job_id": "job_csv_001",
                  "item_id": "item_001",
                  "result": {
                    "summary": "done"
                  }
                }
                """);

            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_worker_001",
                turnId: "turn_report_001",
                itemId: "item_report_001",
                toolName: "report_agent_job_result",
                arguments: args.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);
            using var output = JsonDocument.Parse(result.OutputText);
            Assert.True(output.RootElement.GetProperty("accepted").GetBoolean());

            var snapshot = await manager.ReadJobAsync("job_csv_001", CancellationToken.None);
            var item = Assert.Single(snapshot.Items);
            Assert.Equal("running", item.Status);
            Assert.Contains("done", item.ResultJson, StringComparison.Ordinal);
            Assert.NotNull(item.ReportedAt);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_ReportAgentJobResult_ShouldRejectWrongWorker()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var manager = GetPrivateField<KernelAgentOrchestrationManager>(server, "agentOrchestrationManager");
            _ = await manager.CreateJobAsync(
                jobId: "job_csv_002",
                name: "csv batch",
                instruction: "process csv",
                inputHeadersJson: "[]",
                inputCsvPath: Path.Combine(root, "input.csv"),
                outputCsvPath: Path.Combine(root, "output.csv"),
                autoExport: true,
                items: new[]
                {
                    (ItemId: (string?)"item_001", SourceId: (string?)"row_001", RowJson: "{\"value\":\"alpha\"}")
                },
                cancellationToken: CancellationToken.None);
            _ = await manager.DispatchAsync("job_csv_002", new[] { "thread_worker_001" }, CancellationToken.None);

            using var args = JsonDocument.Parse("""
                {
                  "job_id": "job_csv_002",
                  "item_id": "item_001",
                  "result": {
                    "summary": "done"
                  }
                }
                """);

            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_worker_999",
                turnId: "turn_report_002",
                itemId: "item_report_002",
                toolName: "report_agent_job_result",
                arguments: args.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);
            using var output = JsonDocument.Parse(result.OutputText);
            Assert.False(output.RootElement.GetProperty("accepted").GetBoolean());

            var snapshot = await manager.ReadJobAsync("job_csv_002", CancellationToken.None);
            var item = Assert.Single(snapshot.Items);
            Assert.Equal("running", item.Status);
            Assert.Null(item.ResultJson);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RecoverRunningAgentJobWorkersAsync_WhenWorkerReachedFinalState_ShouldFinalizeReportedItem()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_worker_recovery_001", root, CancellationToken.None);
            _ = await threadStore.AppendCompletedTurnAsync(
                "thread_worker_recovery_001",
                "turn_worker_recovery_001",
                "处理任务",
                "WORKER_DONE",
                "completed",
                CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var manager = GetPrivateField<KernelAgentOrchestrationManager>(server, "agentOrchestrationManager");

            _ = await manager.CreateJobAsync(
                jobId: "job_recovery_001",
                name: "recovery batch",
                instruction: "process csv",
                inputHeadersJson: "[]",
                inputCsvPath: Path.Combine(root, "input.csv"),
                outputCsvPath: Path.Combine(root, "output.csv"),
                autoExport: true,
                items: new[]
                {
                    (ItemId: (string?)"item_001", SourceId: (string?)"row_001", RowJson: "{\"value\":\"alpha\"}")
                },
                cancellationToken: CancellationToken.None);
            _ = await manager.DispatchAsync("job_recovery_001", new[] { "thread_worker_recovery_001" }, CancellationToken.None);
            _ = await manager.RecordItemResultAsync(
                "job_recovery_001",
                "item_001",
                "thread_worker_recovery_001",
                """{"summary":"done:item_001"}""",
                CancellationToken.None);

            var snapshotBefore = await manager.ReadJobAsync("job_recovery_001", CancellationToken.None);
            var activeWorkers = new Dictionary<string, KernelAgentJobActiveWorker>(StringComparer.Ordinal);

            var progressed = await InvokeRecoverRunningAgentJobWorkersAsync(
                server,
                "job_recovery_001",
                snapshotBefore.Items,
                activeWorkers,
                TimeSpan.FromMinutes(30));

            Assert.True(progressed);
            Assert.Empty(activeWorkers);

            var snapshotAfter = await manager.ReadJobAsync("job_recovery_001", CancellationToken.None);
            var item = Assert.Single(snapshotAfter.Items);
            Assert.Equal("completed", item.Status);
            Assert.Contains("done:item_001", item.ResultJson, StringComparison.Ordinal);
            Assert.Null(item.AssignedThreadId);
            Assert.NotNull(item.ReportedAt);
            Assert.NotNull(item.CompletedAt);
            Assert.Equal("completed", snapshotAfter.Job.Status);

            var workerRecord = await threadStore.GetThreadAsync("thread_worker_recovery_001", CancellationToken.None);
            Assert.NotNull(workerRecord);
            Assert.True(workerRecord!.IsArchived, JsonSerializer.Serialize(workerRecord));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RecoverRunningAgentJobWorkersAsync_WhenRunningItemIsStale_ShouldFailItemAndArchiveWorker()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_worker_stale_001", root, CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var manager = GetPrivateField<KernelAgentOrchestrationManager>(server, "agentOrchestrationManager");

            _ = await manager.CreateJobAsync(
                jobId: "job_stale_001",
                name: "stale batch",
                instruction: "process csv",
                inputHeadersJson: "[]",
                inputCsvPath: Path.Combine(root, "input.csv"),
                outputCsvPath: Path.Combine(root, "output.csv"),
                autoExport: true,
                items: new[]
                {
                    (ItemId: (string?)"item_001", SourceId: (string?)"row_001", RowJson: "{\"value\":\"alpha\"}")
                },
                cancellationToken: CancellationToken.None,
                maxRuntimeSeconds: 1);
            var assignment = await manager.AssignItemAsync("job_stale_001", "item_001", "thread_worker_stale_001", CancellationToken.None);
            Assert.NotNull(assignment);

            var snapshotBefore = await manager.ReadJobAsync("job_stale_001", CancellationToken.None);
            var staleItem = Assert.Single(snapshotBefore.Items) with
            {
                UpdatedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10),
            };
            await threadStore.StateStore.UpsertAgentJobItemAsync(staleItem, CancellationToken.None);
            await threadStore.StateStore.UpsertAgentJobAsync(
                snapshotBefore.Job with
                {
                    Status = "running",
                    UpdatedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10),
                    StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10),
                },
                CancellationToken.None);

            var activeWorkers = new Dictionary<string, KernelAgentJobActiveWorker>(StringComparer.Ordinal)
            {
                ["thread_worker_stale_001"] = new("thread_worker_stale_001", "item_001", DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10)),
            };

            var progressed = await InvokeRecoverRunningAgentJobWorkersAsync(
                server,
                "job_stale_001",
                [staleItem],
                activeWorkers,
                TimeSpan.FromSeconds(1));

            Assert.True(progressed);
            Assert.Empty(activeWorkers);

            var snapshotAfter = await manager.ReadJobAsync("job_stale_001", CancellationToken.None);
            var item = Assert.Single(snapshotAfter.Items);
            Assert.Equal("failed", item.Status);
            Assert.Contains("worker exceeded max runtime of 1s", item.LastError, StringComparison.Ordinal);
            Assert.Null(item.AssignedThreadId);
            Assert.NotNull(item.CompletedAt);
            Assert.Equal("completed", snapshotAfter.Job.Status);

            var workerRecord = await threadStore.GetThreadAsync("thread_worker_stale_001", CancellationToken.None);
            Assert.NotNull(workerRecord);
            Assert.True(workerRecord!.IsArchived, JsonSerializer.Serialize(workerRecord));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RecoverRunningAgentJobWorkersAsync_WhenAssignedThreadIdMissing_ShouldFailItem()
    {
        var root = CreateTempDirectory();
        try
        {
            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var manager = GetPrivateField<KernelAgentOrchestrationManager>(server, "agentOrchestrationManager");

            var created = await manager.CreateJobAsync(
                jobId: "job_missing_thread_001",
                name: "broken batch",
                instruction: "process csv",
                inputHeadersJson: "[]",
                inputCsvPath: Path.Combine(root, "input.csv"),
                outputCsvPath: Path.Combine(root, "output.csv"),
                autoExport: true,
                items: new[]
                {
                    (ItemId: (string?)"item_001", SourceId: (string?)"row_001", RowJson: "{\"value\":\"alpha\"}")
                },
                cancellationToken: CancellationToken.None);

            var brokenItem = Assert.Single(created.Items) with
            {
                Status = "running",
                AttemptCount = 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await threadStore.StateStore.UpsertAgentJobItemAsync(brokenItem, CancellationToken.None);
            await threadStore.StateStore.UpsertAgentJobAsync(
                created.Job with
                {
                    Status = "running",
                    UpdatedAt = DateTimeOffset.UtcNow,
                    StartedAt = DateTimeOffset.UtcNow,
                },
                CancellationToken.None);

            var activeWorkers = new Dictionary<string, KernelAgentJobActiveWorker>(StringComparer.Ordinal);

            var progressed = await InvokeRecoverRunningAgentJobWorkersAsync(
                server,
                "job_missing_thread_001",
                [brokenItem],
                activeWorkers,
                TimeSpan.FromMinutes(30));

            Assert.True(progressed);
            Assert.Empty(activeWorkers);

            var snapshotAfter = await manager.ReadJobAsync("job_missing_thread_001", CancellationToken.None);
            var item = Assert.Single(snapshotAfter.Items);
            Assert.Equal("failed", item.Status);
            Assert.Equal("running item is missing assigned_thread_id", item.LastError);
            Assert.Null(item.AssignedThreadId);
            Assert.Equal("completed", snapshotAfter.Job.Status);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void NormalizeSpawnAgentsOnCsvConcurrency_WhenAgentThreadLimitConfigured_ShouldCapRequestedValue()
    {
        var cappedRequested = KernelSpawnAgentsOnCsvRuntimeHelpers.NormalizeSpawnAgentsOnCsvConcurrency(16, 6);
        var cappedDefault = KernelSpawnAgentsOnCsvRuntimeHelpers.NormalizeSpawnAgentsOnCsvConcurrency(null, 6);

        Assert.Equal(6, cappedRequested);
        Assert.Equal(6, cappedDefault);
    }

    [Fact]
    public async Task ExecuteToolCallAsync_SpawnAgentsOnCsv_WhenDepthLimitReached_ShouldFailWithTianShuStyleMessage()
    {
        var root = CreateTempDirectory();
        var previousEnv = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, "test-key");
        KernelThreadStore? threadStore = null;

        try
        {
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true
                agent_jobs = true

                [agents]
                max_depth = 1
                """);

            var inputPath = Path.Combine(root, "input.csv");
            await File.WriteAllTextAsync(inputPath, "id,name\nrow-1,Alpha\n");

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_csv_depth_001", root, CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(
                new StringReader(string.Empty),
                writer,
                threadStore,
                cliConfigFilePath: sessionConfigPath);
            using var spawnArgs = JsonDocument.Parse("""
                {
                  "message": "请分析当前问题",
                  "agent_type": "explorer",
                  "fork_context": false
                }
                """);

            var parentContext = CreateTurnContext(root);
            var spawnResult = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_csv_depth_001",
                turnId: "turn_parent_csv_depth_001",
                itemId: "item_spawn_csv_depth_001",
                toolName: "spawn_agent",
                arguments: spawnArgs.RootElement,
                context: parentContext,
                toolCallGate: null,
                cancellationToken: CancellationToken.None);
            Assert.True(spawnResult.Success);

            using var spawnOutput = JsonDocument.Parse(spawnResult.OutputText);
            var childThreadId = spawnOutput.RootElement.GetProperty("agent_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(childThreadId));

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(childThreadId!, out var childThread));
            Assert.NotNull(childThread);

            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                csv_path = inputPath,
                instruction = "Analyze {name}",
                max_workers = 1,
            }));
            var result = await server.ExecuteToolCallAsync(
                threadId: childThreadId!,
                turnId: "turn_parent_csv_depth_002",
                itemId: "item_csv_depth_001",
                toolName: "spawn_agents_on_csv",
                arguments: args.RootElement,
                context: CreateTurnContext(root, sessionSource: childThread!.Session.SessionSource),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(
                "failed to process csv job: agent depth limit reached; this session cannot spawn more subagents",
                result.OutputText);

            var failedJob = await ReadLatestAgentJobAsync(threadStore);
            Assert.NotNull(failedJob);
            Assert.Equal("failed", failedJob!.Status);
            Assert.Equal(
                "agent depth limit reached; this session cannot spawn more subagents",
                failedJob.LastError);
            Assert.Null(failedJob.StartedAt);
            Assert.NotNull(failedJob.CompletedAt);

            var failedJobItems = await threadStore.StateStore.ListAgentJobItemsAsync(failedJob.Id, CancellationToken.None);
            var failedJobItem = Assert.Single(failedJobItems);
            Assert.Equal("row-1", failedJobItem.ItemId);
            Assert.Equal("pending", failedJobItem.Status);
            Assert.Equal(0, failedJobItem.AttemptCount);
            Assert.Null(failedJobItem.AssignedThreadId);
            Assert.Null(failedJobItem.LastError);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousEnv);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_SpawnAgentsOnCsv_WhenSpawnSlotTemporarilyUnavailable_ShouldRetryPendingItem()
    {
        var root = CreateTempDirectory();
        var previousEnv = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, "test-key");

        try
        {
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true
                agent_jobs = true

                [agents]
                max_threads = 1
                """);

            var inputPath = Path.Combine(root, "input.csv");
            await File.WriteAllTextAsync(inputPath, "id,name\nrow-1,Alpha\n");

            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_csv_retry_001", root, CancellationToken.None);

            var writer = new StringWriter();
            var handler = new AgentJobWorkerSseHandler([AgentJobWorkerMode.ReportCompleted]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            var server = new AppHostServer(
                new StringReader(string.Empty),
                writer,
                threadStore,
                cliConfigFilePath: sessionConfigPath,
                httpClient: httpClient);

            var guardRuntime = GetPrivateField<object>(server, "spawnAgentGuardAppHostRuntime");
            var reserveMethod = guardRuntime.GetType().GetMethod(
                "ReserveSpawnAgentSlot",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(reserveMethod);
            var reservation = reserveMethod!.Invoke(guardRuntime, [1]);
            Assert.NotNull(reservation);

            var commitMethod = reservation!.GetType().GetMethod("Commit", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(commitMethod);
            commitMethod!.Invoke(reservation, ["occupied-thread"]);

            var releaseMethod = guardRuntime.GetType().GetMethod(
                "ReleaseSpawnedAgentThread",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(releaseMethod);

            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                csv_path = inputPath,
                instruction = "Analyze {name}",
                max_workers = 1,
            }));
            var jobTask = server.ExecuteToolCallAsync(
                threadId: "thread_parent_csv_retry_001",
                turnId: "turn_csv_retry_001",
                itemId: "item_csv_retry_001",
                toolName: "spawn_agents_on_csv",
                arguments: args.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            await Task.Delay(600);
            Assert.False(jobTask.IsCompleted);

            releaseMethod!.Invoke(guardRuntime, ["occupied-thread"]);

            var result = await jobTask;
            Assert.True(result.Success);
            using var output = JsonDocument.Parse(result.OutputText);
            Assert.Equal("completed", output.RootElement.GetProperty("status").GetString());
            Assert.Equal(1, output.RootElement.GetProperty("completed_items").GetInt32());
            Assert.Equal(0, output.RootElement.GetProperty("failed_items").GetInt32());
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousEnv);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_SpawnAgentsOnCsv_WhenConfiguredJobMaxRuntimeSecondsPresent_ShouldUseConfiguredDefault()
    {
        var root = CreateTempDirectory();
        var previousEnv = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, "test-key");

        try
        {
            var sessionConfigPath = Path.Combine(root, "session-config.toml");
            await File.WriteAllTextAsync(sessionConfigPath, """
                [features]
                multi_agent = true
                agent_jobs = true

                [agents]
                job_max_runtime_seconds = 1
                """);

            var inputPath = Path.Combine(root, "input.csv");
            await File.WriteAllTextAsync(inputPath, "id,name\nrow-1,Alpha\n");

            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_csv_runtime_001", root, CancellationToken.None);

            var writer = new StringWriter();
            var handler = new AgentJobWorkerSseHandler([AgentJobWorkerMode.ReportCompleted]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            var server = new AppHostServer(
                new StringReader(string.Empty),
                writer,
                threadStore,
                cliConfigFilePath: sessionConfigPath,
                httpClient: httpClient);

            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                csv_path = inputPath,
                instruction = "Analyze {name}",
                max_workers = 1,
            }));
            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_csv_runtime_001",
                turnId: "turn_csv_runtime_001",
                itemId: "item_csv_runtime_001",
                toolName: "spawn_agents_on_csv",
                arguments: args.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);
            using var output = JsonDocument.Parse(result.OutputText);
            var jobId = output.RootElement.GetProperty("job_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(jobId));

            var job = await threadStore.StateStore.GetAgentJobAsync(jobId!, CancellationToken.None);
            Assert.NotNull(job);
            Assert.Equal(1, job!.MaxRuntimeSeconds);
        }
        finally
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousEnv);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_SpawnAgentsOnCsv_ShouldRunWorkersAndExportDefaultCsv()
    {
        var root = CreateTempDirectory();
        var previousEnv = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, "test-key");

        try
        {
            var inputPath = Path.Combine(root, "input.csv");
            await File.WriteAllTextAsync(inputPath, "id,name,path\nalpha,Alpha,/repo/a.cs\nalpha,Beta,/repo/b.cs\n");

            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_csv_001", root, CancellationToken.None);

            var writer = new StringWriter();
            var handler = new AgentJobWorkerSseHandler(
            [
                AgentJobWorkerMode.ReportCompleted,
                AgentJobWorkerMode.ReportCompleted,
            ]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore, httpClient: httpClient);

            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                csv_path = inputPath,
                instruction = "Analyze {name} at {path}. {{literal}}",
                id_column = "id",
                max_workers = 1,
            }));
            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_csv_001",
                turnId: "turn_csv_001",
                itemId: "item_csv_001",
                toolName: "spawn_agents_on_csv",
                arguments: args.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);
            using var output = JsonDocument.Parse(result.OutputText);
            var jobId = output.RootElement.GetProperty("job_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(jobId));
            Assert.Equal("completed", output.RootElement.GetProperty("status").GetString());
            Assert.Equal(2, output.RootElement.GetProperty("total_items").GetInt32());
            Assert.Equal(2, output.RootElement.GetProperty("completed_items").GetInt32());
            Assert.Equal(0, output.RootElement.GetProperty("failed_items").GetInt32());

            var outputCsvPath = output.RootElement.GetProperty("output_csv_path").GetString();
            Assert.NotNull(outputCsvPath);
            Assert.EndsWith($"input.agent-job-{jobId![..8]}.csv", outputCsvPath, StringComparison.OrdinalIgnoreCase);
            var outputCsv = await File.ReadAllTextAsync(outputCsvPath!);
            Assert.Contains("job_id,item_id,row_index,source_id,status,attempt_count,last_error,result_json,reported_at,completed_at", outputCsv, StringComparison.Ordinal);
            Assert.Contains("alpha-2", outputCsv, StringComparison.Ordinal);
            Assert.Contains("completed", outputCsv, StringComparison.Ordinal);
            Assert.Equal(4, handler.RequestCount);
            Assert.Contains(handler.RequestBodies, static body => body.Contains("Analyze Alpha at /repo/a.cs. {literal}", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousEnv);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_SpawnAgentsOnCsv_ShouldEmitTianShuStyleAgentJobProgressCommentary()
    {
        var root = CreateTempDirectory();
        var previousEnv = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, "test-key");
        KernelThreadStore? threadStore = null;

        try
        {
            var inputPath = Path.Combine(root, "input.csv");
            await File.WriteAllTextAsync(inputPath, "id,name\nrow-1,Alpha\n");

            threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_csv_progress_001", root, CancellationToken.None);

            var writer = new StringWriter();
            var handler = new AgentJobWorkerSseHandler([AgentJobWorkerMode.ReportCompleted]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore, httpClient: httpClient);

            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                csv_path = inputPath,
                instruction = "Analyze {name}",
                max_workers = 1,
            }));
            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_csv_progress_001",
                turnId: "turn_csv_progress_001",
                itemId: "item_csv_progress_001",
                toolName: "spawn_agents_on_csv",
                arguments: args.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);

            using var parsedOutput = JsonDocument.Parse(result.OutputText);
            var jobId = parsedOutput.RootElement.GetProperty("job_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(jobId));

            var notifications = ParseMessages(writer);
            try
            {
                var progressNotifications = notifications
                    .Where(static document => IsThreadItemNotification(document.RootElement, "item/agentMessage/delta", "agentMessage"))
                    .Where(document =>
                    {
                        var delta = document.RootElement.GetProperty("params").GetProperty("delta").GetString();
                        return delta is not null && delta.StartsWith("agent_job_progress:", StringComparison.Ordinal);
                    })
                    .ToArray();

                Assert.NotEmpty(progressNotifications);
                Assert.DoesNotContain(notifications, static document => IsNotificationMethod(document.RootElement, "agent/job/progress"));

                using var payload = JsonDocument.Parse(progressNotifications[0].RootElement.GetProperty("params").GetProperty("delta").GetString()!["agent_job_progress:".Length..]);
                Assert.Equal(jobId, payload.RootElement.GetProperty("job_id").GetString());
                Assert.True(payload.RootElement.GetProperty("total_items").GetInt32() >= 1);
            }
            finally
            {
                DisposeAll(notifications);
            }
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousEnv);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_SpawnAgentsOnCsv_ShouldFailWhenWorkerDoesNotReport()
    {
        var root = CreateTempDirectory();
        var previousEnv = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, "test-key");

        try
        {
            var inputPath = Path.Combine(root, "input.csv");
            var outputPath = Path.Combine(root, "result.csv");
            await File.WriteAllTextAsync(inputPath, "id,name\nrow-1,Alpha\n");

            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_csv_002", root, CancellationToken.None);

            var writer = new StringWriter();
            var handler = new AgentJobWorkerSseHandler([AgentJobWorkerMode.NoReport]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore, httpClient: httpClient);

            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                csv_path = inputPath,
                instruction = "Analyze {name}",
                output_csv_path = outputPath,
                max_workers = 1,
            }));
            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_csv_002",
                turnId: "turn_csv_002",
                itemId: "item_csv_002",
                toolName: "spawn_agents_on_csv",
                arguments: args.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);
            using var output = JsonDocument.Parse(result.OutputText);
            Assert.Equal("completed", output.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, output.RootElement.GetProperty("completed_items").GetInt32());
            Assert.Equal(1, output.RootElement.GetProperty("failed_items").GetInt32());
            Assert.Equal("row-1", output.RootElement.GetProperty("failed_item_errors")[0].GetProperty("item_id").GetString());
            Assert.Contains(
                "worker finished without calling report_agent_job_result",
                output.RootElement.GetProperty("failed_item_errors")[0].GetProperty("last_error").GetString(),
                StringComparison.Ordinal);
            Assert.Equal(1, handler.RequestCount);

            var outputCsv = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("failed", outputCsv, StringComparison.Ordinal);
            Assert.Contains("worker finished without calling report_agent_job_result", outputCsv, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousEnv);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_SpawnAgentsOnCsv_StopShouldCancelJobAndLeavePendingItemsUnprocessed()
    {
        var root = CreateTempDirectory();
        var previousEnv = Environment.GetEnvironmentVariable(MissingKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, "test-key");

        try
        {
            var inputPath = Path.Combine(root, "input.csv");
            await File.WriteAllTextAsync(inputPath, "id,name\nrow-1,Alpha\nrow-2,Beta\n");

            var threadStore = new KernelThreadStore(Path.Combine(root, "threads.json"));
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync("thread_parent_csv_003", root, CancellationToken.None);

            var writer = new StringWriter();
            var handler = new AgentJobWorkerSseHandler(
            [
                AgentJobWorkerMode.ReportAndStop,
                AgentJobWorkerMode.ReportCompleted,
            ]);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore, httpClient: httpClient);

            using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                csv_path = inputPath,
                instruction = "Analyze {name}",
                max_workers = 1,
            }));
            var result = await server.ExecuteToolCallAsync(
                threadId: "thread_parent_csv_003",
                turnId: "turn_csv_003",
                itemId: "item_csv_003",
                toolName: "spawn_agents_on_csv",
                arguments: args.RootElement,
                context: CreateTurnContext(root),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);
            using var output = JsonDocument.Parse(result.OutputText);
            Assert.Equal("cancelled", output.RootElement.GetProperty("status").GetString());
            Assert.Equal(2, output.RootElement.GetProperty("total_items").GetInt32());
            Assert.Equal(1, output.RootElement.GetProperty("completed_items").GetInt32());
            Assert.Equal(0, output.RootElement.GetProperty("failed_items").GetInt32());
            Assert.Contains("cancelled by worker request", output.RootElement.GetProperty("job_error").GetString(), StringComparison.Ordinal);
            Assert.Equal(2, handler.RequestCount);

            var outputCsvPath = output.RootElement.GetProperty("output_csv_path").GetString();
            Assert.NotNull(outputCsvPath);
            var lines = (await File.ReadAllLinesAsync(outputCsvPath!))
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
            Assert.Contains(lines, static line => line.Contains("row-1", StringComparison.Ordinal) && line.Contains(",completed,", StringComparison.Ordinal));
            Assert.Contains(lines, static line => line.Contains("row-2", StringComparison.Ordinal) && line.Contains(",pending,", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(MissingKeyEnvironmentVariable, previousEnv);
            DeleteDirectory(root);
        }
    }

    private enum AgentJobWorkerMode
    {
        NoReport,
        ReportCompleted,
        ReportAndStop,
    }

    private sealed class AgentJobWorkerSseHandler : HttpMessageHandler
    {
        private readonly Queue<AgentJobWorkerMode> reportModes;
        private bool awaitingAssistantAfterTool;

        public AgentJobWorkerSseHandler(IEnumerable<AgentJobWorkerMode> reportModes)
        {
            this.reportModes = new Queue<AgentJobWorkerMode>(reportModes);
        }

        public int RequestCount { get; private set; }

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(body);

            string stream;
            if (awaitingAssistantAfterTool)
            {
                awaitingAssistantAfterTool = false;
                stream = BuildSseStream(
                    JsonSerializer.Serialize(new
                    {
                        type = "response.created",
                        response = new { id = $"resp-{RequestCount}" },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        type = "response.output_text.delta",
                        delta = "OK",
                    }),
                    JsonSerializer.Serialize(new
                    {
                        type = "response.output_item.done",
                        item = new
                        {
                            type = "message",
                            role = "assistant",
                            content = new object[]
                            {
                                new { type = "output_text", text = "OK" },
                            },
                        },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        type = "response.completed",
                        response = new { id = $"resp-{RequestCount}" },
                    }));
            }
            else if (reportModes.Count > 0)
            {
                var mode = reportModes.Dequeue();
                if (mode == AgentJobWorkerMode.NoReport)
                {
                    stream = BuildNoReportStream();
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(stream, Encoding.UTF8, "text/event-stream"),
                    };
                }

                var (jobId, itemId) = ExtractJobIdentifiers(body);
                awaitingAssistantAfterTool = true;
                var functionCallArgs = JsonSerializer.Serialize(new
                {
                    job_id = jobId,
                    item_id = itemId,
                    result = new
                    {
                        summary = $"done:{itemId}",
                    },
                    stop = mode == AgentJobWorkerMode.ReportAndStop,
                });
                stream = BuildSseStream(
                    JsonSerializer.Serialize(new
                    {
                        type = "response.created",
                        response = new { id = $"resp-{RequestCount}" },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        type = "response.output_item.done",
                        item = new
                        {
                            type = "function_call",
                            name = "report_agent_job_result",
                            arguments = functionCallArgs,
                            call_id = $"call-{RequestCount}",
                        },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        type = "response.completed",
                        response = new { id = $"resp-{RequestCount}" },
                    }));
            }
            else
            {
                stream = BuildNoReportStream();
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(stream, Encoding.UTF8, "text/event-stream"),
            };
        }

        private static (string JobId, string ItemId) ExtractJobIdentifiers(string requestBody)
        {
            using var document = JsonDocument.Parse(requestBody);
            var input = document.RootElement.GetProperty("input");
            var message = input[input.GetArrayLength() - 1];
            var text = message.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
            return (ExtractPromptValue(text, "Job ID:"), ExtractPromptValue(text, "Item ID:"));
        }

        private static string ExtractPromptValue(string prompt, string marker)
        {
            var start = prompt.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Prompt marker not found: {marker}");
            start += marker.Length;
            var end = prompt.IndexOf('\n', start);
            if (end < 0)
            {
                end = prompt.Length;
            }

            return prompt[start..end].Trim();
        }

        private string BuildNoReportStream()
        {
            return BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = $"resp-{RequestCount}" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "NO_REPORT",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "NO_REPORT" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = $"resp-{RequestCount}" },
                }));
        }
    }

    private sealed class CapturingSequencedSseHandler(IEnumerable<string> streams) : HttpMessageHandler
    {
        private readonly Queue<string> streams = new(streams);

        public int RequestCount { get; private set; }

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            Assert.True(this.streams.Count > 0, "No SSE stream configured for request.");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(this.streams.Dequeue(), Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    private static string BuildSseStream(params string[] jsonEvents)
    {
        var builder = new StringBuilder();
        foreach (var jsonEvent in jsonEvents)
        {
            builder.Append("data: ");
            builder.AppendLine(jsonEvent);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildAssistantMessageStream(string responseId, string assistantText)
    {
        return BuildSseStream(
            JsonSerializer.Serialize(new
            {
                type = "response.created",
                response = new { id = responseId },
            }),
            JsonSerializer.Serialize(new
            {
                type = "response.output_text.delta",
                delta = assistantText,
            }),
            JsonSerializer.Serialize(new
            {
                type = "response.output_item.done",
                item = new
                {
                    type = "message",
                    role = "assistant",
                    content = new object[]
                    {
                        new { type = "output_text", text = assistantText },
                    },
                },
            }),
            JsonSerializer.Serialize(new
            {
                type = "response.completed",
                response = new { id = responseId },
            }));
    }

    private static IReadOnlyList<(string Role, string Text)> ExtractResponsesMessageSequence(JsonElement input)
    {
        var messages = new List<(string Role, string Text)>();
        foreach (var item in input.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeElement)
                || !string.Equals(typeElement.GetString(), "message", StringComparison.Ordinal)
                || !item.TryGetProperty("role", out var roleElement)
                || !item.TryGetProperty("content", out var contentElement)
                || contentElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var role = roleElement.GetString() ?? string.Empty;
            foreach (var content in contentElement.EnumerateArray())
            {
                if (!content.TryGetProperty("text", out var textElement)
                    || textElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                messages.Add((role, textElement.GetString() ?? string.Empty));
            }
        }

        return messages;
    }

    private static string? ExtractToolParameterDescription(
        JsonElement request,
        string toolName,
        string parameterName)
    {
        if (!request.TryGetProperty("tools", out var toolsElement)
            || toolsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var tool in toolsElement.EnumerateArray())
        {
            if (!tool.TryGetProperty("type", out var typeElement)
                || !string.Equals(typeElement.GetString(), "function", StringComparison.Ordinal)
                || !tool.TryGetProperty("name", out var nameElement)
                || !string.Equals(nameElement.GetString(), toolName, StringComparison.Ordinal)
                || !tool.TryGetProperty("parameters", out var parametersElement)
                || parametersElement.ValueKind != JsonValueKind.Object
                || !parametersElement.TryGetProperty("properties", out var propertiesElement)
                || propertiesElement.ValueKind != JsonValueKind.Object
                || !propertiesElement.TryGetProperty(parameterName, out var parameterElement)
                || parameterElement.ValueKind != JsonValueKind.Object
                || !parameterElement.TryGetProperty("description", out var descriptionElement)
                || descriptionElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            return descriptionElement.GetString();
        }

        return null;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await predicate().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new TimeoutException("Condition was not satisfied within the allotted time.");
    }

    private static TurnRequestContext CreateTurnContext(
        string root,
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools = null,
        KernelCollaborationModeState? collaborationMode = null,
        KernelSessionSource? sessionSource = null,
        string? model = null,
        string? modelProvider = null,
        string? approvalPolicy = null,
        string? sandboxMode = null,
        string? cwd = null,
        string? providerBaseUrl = null,
        string? providerApiKeyEnvironmentVariable = null,
        string? providerWireApi = null,
        string? developerInstructions = null,
        string? reasoningSummary = null,
        string? verbosity = null)
        => new(
            Model: model ?? "gpt-5",
            ModelProvider: modelProvider ?? "openai",
            ServiceTier: null,
            ApprovalPolicy: approvalPolicy ?? "never",
            SandboxPolicy: null,
            SandboxMode: sandboxMode ?? "workspaceWrite",
            Cwd: cwd ?? root,
            ProviderBaseUrl: providerBaseUrl ?? "https://example.invalid/v1",
            ProviderApiKeyEnvironmentVariable: providerApiKeyEnvironmentVariable ?? MissingKeyEnvironmentVariable,
            ProviderWireApi: providerWireApi ?? "responses",
            IsReview: false,
            ReviewDisplayText: null,
            DynamicTools: dynamicTools,
            DeveloperInstructions: developerInstructions,
            ReasoningSummary: reasoningSummary,
            Verbosity: verbosity,
            CollaborationMode: collaborationMode,
            SessionSource: sessionSource);

    private static KernelThreadSessionState CreateSessionState(
        string root,
        KernelSessionSource? sessionSource = null)
    {
        var sandboxPolicy = JsonSerializer.SerializeToElement(new
        {
            type = "workspaceWrite",
            writableRoots = Array.Empty<string>(),
            readOnlyAccess = new { type = "fullAccess" },
            networkAccess = false,
            excludeTmpdirEnvVar = false,
            excludeSlashTmp = false,
        });

        return new KernelThreadSessionState(
            Model: "gpt-5",
            ModelProvider: "openai",
            ServiceTier: null,
            Cwd: root,
            ApprovalPolicy: "never",
            SandboxPolicy: sandboxPolicy,
            SandboxMode: "workspaceWrite",
            Ephemeral: false,
            ServiceName: null,
            BaseInstructions: "base",
            DeveloperInstructions: "dev",
            Personality: null,
            DynamicTools: null,
            ProviderBaseUrl: "https://example.invalid/v1",
            ProviderApiKeyEnvironmentVariable: MissingKeyEnvironmentVariable,
            ProviderWireApi: "responses",
            SessionSource: sessionSource ?? KernelSessionSource.AppServer);
    }

    private static KernelThreadRecord CreateThreadRecord(
        string threadId,
        string root,
        string? agentNickname = null)
    {
        return new KernelThreadRecord
        {
            Id = threadId,
            Cwd = root,
            AgentNickname = agentNickname,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            StatusType = "idle",
            ActiveFlags = [],
            Turns = [],
        };
    }

    private static async Task<KernelAgentJobRecord?> ReadLatestAgentJobAsync(KernelThreadStore threadStore)
    {
        await threadStore.StateStore.InitializeAsync(CancellationToken.None);
        using var connection = KernelNativeSqliteConnection.Open(threadStore.StateStore.DatabasePath);
        var row = connection.QuerySingle(
            "SELECT id, name, status, instruction, output_schema_json, input_headers_json, input_csv_path, output_csv_path, auto_export, max_runtime_seconds, created_at_unix_ms, updated_at_unix_ms, started_at_unix_ms, completed_at_unix_ms, last_error FROM agent_jobs ORDER BY created_at_unix_ms DESC LIMIT 1;");
        return row is null
            ? null
            : new KernelAgentJobRecord(
                Id: row.GetString(0) ?? string.Empty,
                Name: row.GetString(1) ?? string.Empty,
                Status: row.GetString(2) ?? string.Empty,
                Instruction: row.GetString(3) ?? string.Empty,
                OutputSchemaJson: row.GetString(4),
                InputHeadersJson: row.GetString(5) ?? string.Empty,
                InputCsvPath: row.GetString(6) ?? string.Empty,
                OutputCsvPath: row.GetString(7) ?? string.Empty,
                AutoExport: row.GetInt64(8) != 0,
                MaxRuntimeSeconds: row.IsNull(9) ? null : (int)row.GetInt64(9),
                CreatedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(10)),
                UpdatedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(11)),
                StartedAt: row.IsNull(12) ? null : DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(12)),
                CompletedAt: row.IsNull(13) ? null : DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(13)),
                LastError: row.GetString(14));
    }

    private static void AssertAgentErrored(string outputText, string agentId)
    {
        using var output = JsonDocument.Parse(outputText);
        Assert.False(output.RootElement.GetProperty("timed_out").GetBoolean());
        var status = output.RootElement.GetProperty("status");
        Assert.True(status.TryGetProperty(agentId, out var agentStatus));
        Assert.True(agentStatus.TryGetProperty("errored", out var error));
        Assert.Contains(MissingKeyEnvironmentVariable, error.GetString(), StringComparison.Ordinal);
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(instance)!;
    }

    private static async Task<bool> InvokeRecoverRunningAgentJobWorkersAsync(
        AppHostServer server,
        string jobId,
        IReadOnlyList<KernelAgentJobItemRecord> items,
        Dictionary<string, KernelAgentJobActiveWorker> activeWorkers,
        TimeSpan runtimeTimeout)
    {
        var runtime = GetPrivateField<object>(server, "spawnAgentsOnCsvAppHostRuntime");
        var method = runtime.GetType().GetMethod(
            "RecoverRunningAgentJobWorkersAsync",
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var invocation = method!.Invoke(
            runtime,
            [
                jobId,
                items,
                activeWorkers,
                runtimeTimeout,
                CancellationToken.None,
            ]);

        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;

        var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(resultProperty);
        return Assert.IsType<bool>(resultProperty!.GetValue(task));
    }

    private static async Task<System.Text.Json.Nodes.JsonNode?> InvokeGetAgentStatusNodeAsync(
        AppHostServer server,
        string agentId,
        bool treatArchivedAsNotFound)
    {
        var runtime = GetPrivateField<object>(server, "toolRuntimeAppHostRuntime");
        var method = runtime.GetType().GetMethod(
            "GetAgentStatusNodeAsync",
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var invocation = method!.Invoke(
            runtime,
            [
                agentId,
                treatArchivedAsNotFound,
                CancellationToken.None,
            ]);

        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;

        var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(resultProperty);
        return (System.Text.Json.Nodes.JsonNode?)resultProperty!.GetValue(task);
    }

    private static void InvokeMaybeStartSubagentCompletionWatcher(
        AppHostServer server,
        string childThreadId,
        KernelSessionSource sessionSource)
    {
        var method = typeof(AppHostServer).GetMethod(
            "MaybeStartSubagentCompletionWatcher",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        _ = method!.Invoke(server, [childThreadId, sessionSource]);
    }

    private static JsonDocument[] ParseMessages(StringWriter writer)
    {
        return writer
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();
    }

    private static bool IsNotificationMethod(JsonElement json, string method)
    {
        return json.TryGetProperty("method", out var methodElement)
               && methodElement.ValueKind == JsonValueKind.String
               && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal)
               && !json.TryGetProperty("id", out _);
    }

    private static void DisposeAll(IEnumerable<JsonDocument> documents)
    {
        foreach (var document in documents)
        {
            document.Dispose();
        }
    }

    private static bool IsThreadItemNotification(JsonElement json, string method, string itemType)
    {
        if (!IsNotificationMethod(json, method)
            || !json.TryGetProperty("params", out var parameters)
            || !parameters.TryGetProperty("item", out var item)
            || !item.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return string.Equals(typeElement.GetString(), itemType, StringComparison.Ordinal);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShuKernelTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

}
