using System.Linq;
using System.Reflection;
using System.Text.Json;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Events;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;

namespace TianShu.Execution.Integration.Tests;

public sealed class SendCommandProtocolTests
{
    private static readonly Assembly ProbeAssembly = ReflectionTestHelper.LoadRequiredAssembly("TianShu.Cli");

    [Fact]
    public void ProbeEventRecord_FromStreamEvent_PreservesTypedApprovalAndPermissionPayloads()
    {
        var eventRecordType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.ProbeEventRecord");
        var approvalEvent = new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ApprovalRequested,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            CallId = new CallId("approval-1"),
            ToolName = "shell_command",
            ApprovalKind = "command_execution",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ApprovalRequest,
            Payload = ToStructuredValue(
                new ApprovalRequestPayload(
                    "shell_command",
                    "command_execution",
                    ["accept", "decline"],
                    "需要确认命令执行。",
                    [new ApprovalMetadataFieldPayload("cwd", "string", "D:/Work/TianShu")])),
        };

        var approvalRecord = ReflectionTestHelper.InvokeStaticMethod(eventRecordType, "FromStreamEvent", approvalEvent);
        Assert.NotNull(approvalRecord);

        var approvalRequest = ReflectionTestHelper.GetProperty(approvalRecord!, "ApprovalRequest");
        Assert.NotNull(approvalRequest);
        Assert.Equal("需要确认命令执行。", ReflectionTestHelper.GetProperty(approvalRequest!, "Summary"));
        var metadataFields = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(approvalRequest, "MetadataFields"));
        Assert.Single(metadataFields.Cast<object>());

        var permissionEvent = new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.PermissionRequested,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId("turn-1"),
            CallId = new CallId("permission-1"),
            ToolName = "shell_command",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.PermissionRequest,
            Payload = ToStructuredValue(
                new PermissionRequestPayload(
                    "需要更高权限。",
                    [new PermissionFieldPayload("network", "json", """{"enabled":true}""")],
                    """{"network":{"enabled":true}}""",
                    "等待权限确认。")),
        };

        var permissionRecord = ReflectionTestHelper.InvokeStaticMethod(eventRecordType, "FromStreamEvent", permissionEvent);
        Assert.NotNull(permissionRecord);

        var permissionRequest = ReflectionTestHelper.GetProperty(permissionRecord!, "PermissionRequest");
        Assert.NotNull(permissionRequest);
        Assert.Equal("等待权限确认。", ReflectionTestHelper.GetProperty(permissionRequest!, "Summary"));
    }

    [Fact]
    public void ProbeEventRecord_FromStreamEvent_NestsLegacyJsonUnderDiagnostics()
    {
        var eventRecordType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.ProbeEventRecord");
        var streamEvent = new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.PlanUpdated,
            Diagnostics = new ControlPlaneConversationStreamDiagnostics
            {
                DataJson = """{"steps":[{"step":"检查日志"}]}""",
                MetadataJson = """{"method":"turn/plan/updated"}""",
                RawJson = """{"method":"turn/plan/updated","params":{"steps":[{"step":"检查日志"}]}}""",
            },
        };

        var record = ReflectionTestHelper.InvokeStaticMethod(eventRecordType, "FromStreamEvent", streamEvent);
        Assert.NotNull(record);

        var diagnostics = ReflectionTestHelper.GetProperty(record!, "Diagnostics");
        Assert.NotNull(diagnostics);
        Assert.Equal(streamEvent.Diagnostics!.DataJson, ReflectionTestHelper.GetProperty(diagnostics!, "DataJson"));
        Assert.Equal(streamEvent.Diagnostics.MetadataJson, ReflectionTestHelper.GetProperty(diagnostics!, "MetadataJson"));
        Assert.Equal(streamEvent.Diagnostics.RawJson, ReflectionTestHelper.GetProperty(diagnostics!, "RawJson"));

        var json = JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("diagnostics", out var diagnosticsElement));
        Assert.Equal(streamEvent.Diagnostics.RawJson, diagnosticsElement.GetProperty("rawJson").GetString());
        Assert.False(document.RootElement.TryGetProperty("rawJson", out _));
        Assert.False(document.RootElement.TryGetProperty("dataJson", out _));
        Assert.False(document.RootElement.TryGetProperty("metadataJson", out _));
    }

    [Fact]
    public void ProbeEventRecord_FromStreamEvent_PreservesFollowUpCorrelationPayloads()
    {
        var eventRecordType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.ProbeEventRecord");
        var correlationId = Guid.NewGuid().ToString("N");
        var pendingFollowUpEvent = new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated,
            ThreadId = new ThreadId("thread-followup-1"),
            TurnId = new TurnId("turn-followup-1"),
            PayloadKind = ControlPlaneConversationStreamPayloadKind.PendingFollowUp,
            Payload = ToStructuredValue(
                new PendingFollowUpLifecyclePayload(
                    correlationId,
                    "Steer",
                    "Steer",
                    "awaiting_commit",
                    "turn-followup-1",
                    "turn-followup-1",
                    new PendingFollowUpCompareKeyPayload("中途引导", 0))),
        };

        var pendingFollowUpRecord = ReflectionTestHelper.InvokeStaticMethod(eventRecordType, "FromStreamEvent", pendingFollowUpEvent);
        Assert.NotNull(pendingFollowUpRecord);

        var pendingFollowUp = ReflectionTestHelper.GetProperty(pendingFollowUpRecord!, "PendingFollowUp");
        Assert.NotNull(pendingFollowUp);
        Assert.Equal(correlationId, ReflectionTestHelper.GetProperty(pendingFollowUp!, "CorrelationId"));
        Assert.Equal("awaiting_commit", ReflectionTestHelper.GetProperty(pendingFollowUp, "LifecycleState"));

        var pendingInputStateEvent = new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated,
            ThreadId = new ThreadId("thread-followup-1"),
            TurnId = new TurnId("turn-followup-1"),
            PayloadKind = ControlPlaneConversationStreamPayloadKind.PendingInputState,
            Payload = ToStructuredValue(
                new PendingInputStatePayload(
                    new[]
                    {
                        new PendingInputStateEntryPayload(
                            correlationId,
                            "Steer",
                            "Steer",
                            "awaiting_commit",
                            "turn-followup-1",
                            "turn-followup-1",
                            new PendingFollowUpCompareKeyPayload("中途引导", 0),
                            "PendingSteer"),
                    },
                    InterruptRequestPending: false,
                    SubmitPendingSteersAfterInterrupt: true)),
        };

        var pendingInputStateRecord = ReflectionTestHelper.InvokeStaticMethod(eventRecordType, "FromStreamEvent", pendingInputStateEvent);
        Assert.NotNull(pendingInputStateRecord);

        var pendingInputState = ReflectionTestHelper.GetProperty(pendingInputStateRecord!, "PendingInputState");
        Assert.NotNull(pendingInputState);
        Assert.False((bool)ReflectionTestHelper.GetProperty(pendingInputState!, "InterruptRequestPending")!);
        Assert.True((bool)ReflectionTestHelper.GetProperty(pendingInputState, "SubmitPendingSteersAfterInterrupt")!);
        var entries = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(pendingInputState, "Entries"));
        var entry = Assert.Single(entries.Cast<object>());
        Assert.Equal(correlationId, ReflectionTestHelper.GetProperty(entry, "CorrelationId"));
        Assert.Equal("PendingSteer", ReflectionTestHelper.GetProperty(entry, "PendingBucket"));

        var committedEvent = new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.UserMessageCommitted,
            ThreadId = new ThreadId("thread-followup-1"),
            TurnId = new TurnId("turn-followup-1"),
            PayloadKind = ControlPlaneConversationStreamPayloadKind.CommittedUserMessage,
            Payload = ToStructuredValue(
                new CommittedUserMessagePayload(
                    "item-followup-1",
                    "中途引导",
                    0,
                    correlationId)),
        };

        var committedRecord = ReflectionTestHelper.InvokeStaticMethod(eventRecordType, "FromStreamEvent", committedEvent);
        Assert.NotNull(committedRecord);

        var committedUserMessage = ReflectionTestHelper.GetProperty(committedRecord!, "CommittedUserMessage");
        Assert.NotNull(committedUserMessage);
        Assert.Equal(correlationId, ReflectionTestHelper.GetProperty(committedUserMessage!, "CorrelationId"));
        Assert.Equal("中途引导", ReflectionTestHelper.GetProperty(committedUserMessage, "Text"));

        var pendingFollowUpJson = JsonSerializer.Serialize(pendingFollowUpRecord, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var pendingFollowUpDocument = JsonDocument.Parse(pendingFollowUpJson);
        Assert.Equal(correlationId, pendingFollowUpDocument.RootElement.GetProperty("pendingFollowUp").GetProperty("correlationId").GetString());

        var pendingInputStateJson = JsonSerializer.Serialize(pendingInputStateRecord, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var pendingInputStateDocument = JsonDocument.Parse(pendingInputStateJson);
        Assert.Equal(correlationId, pendingInputStateDocument.RootElement.GetProperty("pendingInputState").GetProperty("entries")[0].GetProperty("correlationId").GetString());

        var committedJson = JsonSerializer.Serialize(committedRecord, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var committedDocument = JsonDocument.Parse(committedJson);
        Assert.Equal(correlationId, committedDocument.RootElement.GetProperty("committedUserMessage").GetProperty("correlationId").GetString());
    }

    [Fact]
    public void ProbeEventRecord_FromStreamEvent_PreservesAgentJobProgressPayload()
    {
        var eventRecordType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.ProbeEventRecord");
        var streamEvent = new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.AgentJobProgress,
            ThreadId = new ThreadId("thread-agent-job-1"),
            TurnId = new TurnId("turn-agent-job-1"),
            Text = "agent_job_progress:{\"job_id\":\"job-1\"}",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.AgentJobProgress,
            Payload = ToStructuredValue(
                new AgentJobProgressPayload(
                    "job-1",
                    5,
                    1,
                    1,
                    3,
                    0,
                    42)),
        };

        var record = ReflectionTestHelper.InvokeStaticMethod(eventRecordType, "FromStreamEvent", streamEvent);
        Assert.NotNull(record);

        var progress = ReflectionTestHelper.GetProperty(record!, "AgentJobProgress");
        Assert.NotNull(progress);
        Assert.Equal("job-1", ReflectionTestHelper.GetProperty(progress!, "JobId"));
        Assert.Equal(5, ReflectionTestHelper.GetProperty(progress, "TotalItems"));
        Assert.Equal(3, ReflectionTestHelper.GetProperty(progress, "CompletedItems"));
        Assert.Equal(42, ReflectionTestHelper.GetProperty(progress, "EtaSeconds"));

        var json = JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        Assert.Equal("job-1", document.RootElement.GetProperty("agentJobProgress").GetProperty("jobId").GetString());
        Assert.Equal(5, document.RootElement.GetProperty("agentJobProgress").GetProperty("totalItems").GetInt32());
    }

    private static StructuredValue ToStructuredValue<TPayload>(TPayload payload)
        where TPayload : class
        => StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    [Fact]
    public void Parse_WhenCollaborationModeProvided_SetsOption_AndHelpMentionsFlag()
    {
        var optionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandOptions");
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            optionsType,
            "Parse",
            (object)new[] { "--message", "hello", "--collaboration-mode", "plan" });
        Assert.NotNull(parseResult);

        var options = ReflectionTestHelper.GetProperty(parseResult!, "Options");
        Assert.NotNull(options);
        Assert.Equal("plan", ReflectionTestHelper.GetProperty(options!, "CollaborationMode"));

        var helpText = Assert.IsType<string>(ReflectionTestHelper.InvokeStaticMethod(optionsType, "GetHelpText"));
        Assert.Contains("--collaboration-mode <mode>", helpText, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenDynamicToolsJsonProvided_SetsOption_AndHelpMentionsFlags()
    {
        var optionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandOptions");
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            optionsType,
            "Parse",
            (object)new[]
            {
                "--message",
                "hello",
                "--dynamic-tools-json",
                """[{"name":"mcp__calendar__find_events","description":"搜索日历事件。","inputSchema":{"type":"object"}}]""",
            });
        Assert.NotNull(parseResult);

        var options = ReflectionTestHelper.GetProperty(parseResult!, "Options");
        Assert.NotNull(options);

        var dynamicTools = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(options!, "DynamicTools"));
        var tool = Assert.Single(dynamicTools.Cast<object>());
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(tool, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("mcp__calendar__find_events", document.RootElement.GetProperty("name").GetString());

        var helpText = Assert.IsType<string>(ReflectionTestHelper.InvokeStaticMethod(optionsType, "GetHelpText"));
        Assert.Contains("--dynamic-tools-json <json>", helpText, StringComparison.Ordinal);
        Assert.Contains("--dynamic-tools-file <path>", helpText, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenConfigOverrideAndConfigFileProvided_SetsTypedOptions()
    {
        var optionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandOptions");
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            optionsType,
            "Parse",
            (object)new[]
            {
                "--message",
                "hello",
                "--config",
                "web_search=live",
                "-c",
                "model=gpt-5-mini",
                "--config-file",
                ".\\custom-config.toml",
            });
        Assert.NotNull(parseResult);

        var options = ReflectionTestHelper.GetProperty(parseResult!, "Options");
        Assert.NotNull(options);
        var configFilePath = Assert.IsType<string>(ReflectionTestHelper.GetProperty(options!, "ConfigFilePath"));
        Assert.EndsWith("custom-config.toml", configFilePath, StringComparison.OrdinalIgnoreCase);

        var configOverrides = Assert.IsAssignableFrom<System.Collections.IDictionary>(ReflectionTestHelper.GetProperty(options, "ConfigOverrides"));
        Assert.Equal("live", configOverrides["web_search"]);
        Assert.Equal("gpt-5-mini", configOverrides["model"]);

        var helpText = Assert.IsType<string>(ReflectionTestHelper.InvokeStaticMethod(optionsType, "GetHelpText"));
        Assert.Contains("--config-file <path>", helpText, StringComparison.Ordinal);
        Assert.Contains("-c <key=value>", helpText, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenPermissionsJsonProvided_SetsOption()
    {
        using var tempDir = new TempDir();
        var permissionsPath = Path.Combine(tempDir.Path, "permissions.json");
        File.WriteAllText(permissionsPath, "{\"permissions\":{}}");

        var optionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandOptions");
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            optionsType,
            "Parse",
            (object)new[] { "--message", "hello", "--permissions-json", permissionsPath });
        Assert.NotNull(parseResult);

        var options = ReflectionTestHelper.GetProperty(parseResult!, "Options");
        Assert.NotNull(options);
        Assert.Equal(Path.GetFullPath(permissionsPath), ReflectionTestHelper.GetProperty(options!, "PermissionsJsonPath"));
    }

    [Fact]
    public void Parse_WhenApprovalDecisionProvided_SetsOption_AndHelpMentionsFlag()
    {
        var optionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandOptions");
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            optionsType,
            "Parse",
            (object)new[] { "--message", "hello", "--approval-decision", "always" });
        Assert.NotNull(parseResult);

        var options = ReflectionTestHelper.GetProperty(parseResult!, "Options");
        Assert.NotNull(options);
        Assert.Equal("ApproveAndRemember", ReflectionTestHelper.GetProperty(options!, "ApprovalDecision")?.ToString());

        var helpText = Assert.IsType<string>(ReflectionTestHelper.InvokeStaticMethod(optionsType, "GetHelpText"));
        Assert.Contains("--approval-decision <value>", helpText, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRuntimeOptions_CopiesCollaborationModeToRuntimeOptions()
    {
        var optionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandOptions");
        var runnerType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandRunner");
        var runner = Activator.CreateInstance(runnerType);
        Assert.NotNull(runner);

        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            optionsType,
            "Parse",
            (object)new[] { "--message", "hello", "--collaboration-mode", "plan" });
        var options = ReflectionTestHelper.GetProperty(parseResult!, "Options");
        Assert.NotNull(options);

        var runtimeOptions = ReflectionTestHelper.InvokeMethod(runner!, "BuildRuntimeOptions", options!);
        Assert.NotNull(runtimeOptions);
        Assert.Equal("plan", ReflectionTestHelper.GetProperty(runtimeOptions!, "CollaborationMode"));
    }

    [Fact]
    public void BuildRuntimeOptions_CopiesDynamicToolsToRuntimeOptions()
    {
        var optionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandOptions");
        var runnerType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandRunner");
        var runner = Activator.CreateInstance(runnerType);
        Assert.NotNull(runner);

        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            optionsType,
            "Parse",
            (object)new[]
            {
                "--message",
                "hello",
                "--dynamic-tools-json",
                """[{"name":"mcp__calendar__find_events","description":"搜索日历事件。","inputSchema":{"type":"object"}}]""",
            });
        var options = ReflectionTestHelper.GetProperty(parseResult!, "Options");
        Assert.NotNull(options);

        var runtimeOptions = ReflectionTestHelper.InvokeMethod(runner!, "BuildRuntimeOptions", options!);
        Assert.NotNull(runtimeOptions);

        var dynamicTools = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(runtimeOptions!, "DynamicTools"));
        var tool = Assert.Single(dynamicTools.Cast<object>());
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(tool, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("mcp__calendar__find_events", document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void Parse_WhenTurnTimeoutProvided_SetsOption_AndBuildRuntimeOptionsCopiesTimeout()
    {
        var optionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandOptions");
        var runnerType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandRunner");
        var runner = Activator.CreateInstance(runnerType);
        Assert.NotNull(runner);

        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            optionsType,
            "Parse",
            (object)new[] { "--message", "hello", "--turn-timeout-seconds", "900" });
        var options = ReflectionTestHelper.GetProperty(parseResult!, "Options");
        Assert.NotNull(options);
        Assert.Equal(900, ReflectionTestHelper.GetProperty(options!, "TurnTimeoutSeconds"));

        var helpText = Assert.IsType<string>(ReflectionTestHelper.InvokeStaticMethod(optionsType, "GetHelpText"));
        Assert.Contains("--turn-timeout-seconds <n>", helpText, StringComparison.Ordinal);

        var runtimeOptions = ReflectionTestHelper.InvokeMethod(runner!, "BuildRuntimeOptions", options!);
        Assert.NotNull(runtimeOptions);
        Assert.Equal(TimeSpan.FromSeconds(900), ReflectionTestHelper.GetProperty(runtimeOptions!, "TurnTimeout"));
    }

    [Fact]
    public async Task BuildTerminalResultAsync_WritesCollaborationModeIntoSummaryJson()
    {
        var optionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandOptions");
        var runnerType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandRunner");
        var exitCodeType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandExitCode");
        var eventRecordType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.ProbeEventRecord");

        using var tempDir = new TempDir();
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            optionsType,
            "Parse",
            (object)new[]
            {
                "--message",
                "hello",
                "--artifacts",
                tempDir.Path,
                "--collaboration-mode",
                "plan",
            });
        var probeOptions = ReflectionTestHelper.GetProperty(parseResult!, "Options");
        Assert.NotNull(probeOptions);

        var runtimeOptions = new ControlPlaneInitializeRuntimeCommand
        {
            WorkingDirectory = tempDir.Path,
            ConfigFilePath = Path.Combine(tempDir.Path, "config.toml"),
            Model = "gpt-5-codex",
        };
        ReflectionTestHelper.SetProperty(runtimeOptions, "CollaborationMode", "plan");

        var runner = Activator.CreateInstance(runnerType);
        Assert.NotNull(runner);

        var emptyEvents = Array.CreateInstance(eventRecordType, 0);
        var exitCode = Enum.Parse(exitCodeType, "Success");

        var task = ReflectionTestHelper.InvokeMethod(
            runner!,
            "BuildTerminalResultAsync",
            probeOptions!,
            DateTimeOffset.UtcNow,
            emptyEvents,
            "assistant text",
            exitCode,
            runtimeOptions,
            null,
            null,
            null,
            null,
            "completed",
            false,
            false,
            false,
            false,
            false,
            null,
            null,
            CancellationToken.None,
            false,
            false,
            false,
            false,
            false,
            false,
            null,
            null);

        var runResult = await ReflectionTestHelper.AwaitTaskResultAsync(task);
        Assert.NotNull(runResult);

        var summaryJson = Assert.IsType<string>(ReflectionTestHelper.GetProperty(runResult!, "SummaryJson"));
        using var document = JsonDocument.Parse(summaryJson);
        Assert.Equal("plan", document.RootElement.GetProperty("collaborationMode").GetString());
    }

    [Fact]
    public void ProbeResolvedOptions_FromRuntimeOptions_IncludesCollaborationMode()
    {
        var resolvedOptionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.ProbeResolvedOptions");
        var runtimeOptions = new ControlPlaneInitializeRuntimeCommand
        {
            WorkingDirectory = Path.GetTempPath(),
            ConfigFilePath = Path.Combine(Path.GetTempPath(), "config.toml"),
        };
        ReflectionTestHelper.SetProperty(runtimeOptions, "CollaborationMode", "plan");

        var resolved = ReflectionTestHelper.InvokeStaticMethod(
            resolvedOptionsType,
            "FromRuntimeOptions",
            runtimeOptions,
            null,
            "kernel.csproj",
            Path.Combine(Path.GetTempPath(), "artifacts"),
            false,
            null,
            null);
        Assert.NotNull(resolved);
        Assert.Equal("plan", ReflectionTestHelper.GetProperty(resolved!, "CollaborationMode"));
    }

    [Fact]
    public void ProbeResolvedOptions_FromRuntimeOptions_IncludesDynamicTools()
    {
        var resolvedOptionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.ProbeResolvedOptions");
        var runtimeOptions = new ControlPlaneInitializeRuntimeCommand
        {
            WorkingDirectory = Path.GetTempPath(),
            ConfigFilePath = Path.Combine(Path.GetTempPath(), "config.toml"),
            DynamicTools =
            [
                new ControlPlaneDynamicToolSpec
                {
                    Name = "mcp__calendar__find_events",
                    Description = "搜索日历事件。",
                    InputSchema = StructuredValue.FromJsonElement(
                        ReflectionTestHelper.ParseJsonElement("""{"type":"object"}""")),
                },
            ],
        };

        var resolved = ReflectionTestHelper.InvokeStaticMethod(
            resolvedOptionsType,
            "FromRuntimeOptions",
            runtimeOptions,
            null,
            "kernel.csproj",
            Path.Combine(Path.GetTempPath(), "artifacts"),
            false,
            null,
            null);
        Assert.NotNull(resolved);

        var dynamicTools = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(resolved!, "DynamicTools"));
        var tool = Assert.Single(dynamicTools.Cast<object>());
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(tool, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("mcp__calendar__find_events", document.RootElement.GetProperty("name").GetString());
    }
    [Fact]
    public void Parse_WhenMessageMissing_ReturnsError()
    {
        var optionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandOptions");
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(optionsType, "Parse", (object)new[] { "--json" });
        Assert.NotNull(parseResult);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(parseResult!, "ErrorMessage"));
        Assert.Contains("缺少必填参数：--message <text>", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenUnknownArgumentProvided_ReturnsError()
    {
        var optionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandOptions");
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(optionsType, "Parse", (object)new[] { "--message", "hello", "--unknown", "value" });
        Assert.NotNull(parseResult);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(parseResult!, "ErrorMessage"));
        Assert.Contains("不支持的参数：--unknown", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenResumeOptionsConflict_ReturnsError()
    {
        var optionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandOptions");
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(optionsType, "Parse", (object)new[] { "--message", "hello", "--resume-thread-id", "thread-1", "--resume-latest" });
        Assert.NotNull(parseResult);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(parseResult!, "ErrorMessage"));
        Assert.Contains("--resume-thread-id 与 --resume-latest 不能同时使用", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_WhenMessageValueLooksLikeFlag_ReturnsError()
    {
        var optionsType = ReflectionTestHelper.GetRequiredType(ProbeAssembly, "TianShu.Cli.SendCommandOptions");
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(optionsType, "Parse", (object)new[] { "--message", "--json" });
        Assert.NotNull(parseResult);

        var errorMessage = Assert.IsType<string>(ReflectionTestHelper.GetProperty(parseResult!, "ErrorMessage"));
        Assert.Contains("参数 --message 缺少值。", errorMessage, StringComparison.Ordinal);
    }}

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tianshu-runtime-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for test temp directories.
        }
    }
}






