using System.Text.Json;
using System.Text.Json.Nodes;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Contracts.Tools;
using TianShu.Provider.Abstractions;
using TianShu.Provider.OpenAI;
using TianShu.Tools.Collaboration;
using TianShu.Tools.Fanout;
using TianShu.Tools.Interaction;
using TianShu.Tools.Search;

namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelCollaborationToolHandlerTests
{
    [Fact]
    public async Task UpdatePlanProvider_ShouldInvokeRuntimeServices()
    {
        KernelPlanUpdateRequest? captured = null;
        var handler = CreateCollaborationRuntimeHandler("update_plan");
        using var args = JsonDocument.Parse("""
            {
              "explanation": "同步任务进度",
              "plan": [
                { "step": "实现工具", "status": "in_progress" },
                { "step": "补测试", "status": "pending" }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(new KernelToolRuntimeServices(
                UpdatePlan: (request, _) =>
                {
                    captured = request;
                    return Task.CompletedTask;
                })),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Plan updated", result.OutputText);
        Assert.NotNull(captured);
        Assert.Equal("同步任务进度", captured!.Explanation);
        Assert.Collection(
            captured.Plan,
            step =>
            {
                Assert.Equal("实现工具", step.Step);
                Assert.Equal("in_progress", step.Status);
            },
            step =>
            {
                Assert.Equal("补测试", step.Step);
                Assert.Equal("pending", step.Status);
            });
    }

    [Fact]
    public async Task UpdatePlanProvider_ShouldRejectEmptyPlan()
    {
        var handler = CreateCollaborationRuntimeHandler("update_plan");
        using var args = JsonDocument.Parse("""{ "plan": [] }""");

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(new KernelToolRuntimeServices(UpdatePlan: (_, _) => Task.CompletedTask)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("plan must contain at least one step", result.OutputText);
    }

    [Fact]
    public async Task UpdatePlanProvider_ShouldRejectPlanMode()
    {
        var handler = CreateCollaborationRuntimeHandler("update_plan");
        using var args = JsonDocument.Parse("""
            {
              "plan": [
                { "step": "实现工具", "status": "in_progress" }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(
                new KernelToolRuntimeServices(UpdatePlan: (_, _) => Task.CompletedTask),
                collaborationMode: new KernelCollaborationModeState(
                    "plan",
                    new KernelCollaborationModeSettings("gpt-5", null, null))),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("update_plan is a TODO/checklist tool and is not allowed in Plan mode", result.OutputText);
    }

    [Fact]
    public async Task RequestUserInputProvider_ShouldRejectDefaultMode()
    {
        var handler = CreateInteractionRuntimeHandler("request_user_input");
        using var args = JsonDocument.Parse("""
            {
              "questions": [
                {
                  "id": "confirm_path",
                  "header": "????",
                  "question": "?????",
                  "options": [
                    { "label": "??????", "description": "????????" },
                    { "label": "??", "description": "???????" }
                  ]
                }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(
                collaborationMode: new KernelCollaborationModeState(
                    "default",
                    new KernelCollaborationModeSettings("gpt-5", null, null))),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("request_user_input is unavailable in Default mode", result.OutputText);
    }

    [Fact]
    public async Task RequestUserInputProvider_ShouldSerializeResponseInPlanMode()
    {
        KernelRequestUserInputRequest? captured = null;
        var handler = CreateInteractionRuntimeHandler("request_user_input");
        using var args = JsonDocument.Parse("""
            {
              "questions": [
                {
                  "id": "confirm_path",
                  "header": "????",
                  "question": "?????",
                  "options": [
                    { "label": "??????", "description": "????????" },
                    { "label": "??", "description": "???????" }
                  ]
                }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(
                collaborationMode: new KernelCollaborationModeState(
                    "plan",
                    new KernelCollaborationModeSettings("gpt-5", null, null)),
                userInputRequester: (request, _) =>
                {
                    captured = request;
                    return Task.FromResult<KernelRequestUserInputResponse>(
                        new(new Dictionary<string, KernelRequestUserInputAnswer>(StringComparer.Ordinal)
                        {
                            ["confirm_path"] = new KernelRequestUserInputAnswer(new[] { "yes" }),
                        }));
                }),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal("turn_001_call", captured!.ItemId);
        Assert.Single(captured.Questions);
        Assert.Equal("confirm_path", captured.Questions[0].Id);
        Assert.True(captured.Questions[0].IsOther);

        using var output = JsonDocument.Parse(result.OutputText);
        Assert.Equal("yes", output.RootElement.GetProperty("answers").GetProperty("confirm_path").GetProperty("answers")[0].GetString());
    }

    [Fact]
    public async Task RequestUserInputProvider_ShouldAllowDefaultModeWhenFeatureEnabled()
    {
        KernelRequestUserInputRequest? captured = null;
        var handler = CreateInteractionRuntimeHandler("request_user_input");
        using var args = JsonDocument.Parse("""
            {
              "questions": [
                {
                  "id": "confirm_path",
                  "header": "确认",
                  "question": "是否继续？",
                  "options": [
                    { "label": "继续", "description": "按当前方案继续执行。" },
                    { "label": "停止", "description": "立即停止当前操作。" }
                  ]
                }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(
                collaborationMode: new KernelCollaborationModeState(
                    "default",
                    new KernelCollaborationModeSettings("gpt-5", null, null)),
                defaultModeRequestUserInputEnabled: true,
                userInputRequester: (request, _) =>
                {
                    captured = request;
                    return Task.FromResult<KernelRequestUserInputResponse>(
                        new(new Dictionary<string, KernelRequestUserInputAnswer>(StringComparer.Ordinal)
                        {
                            ["confirm_path"] = new KernelRequestUserInputAnswer(new[] { "继续" }),
                        }));
                }),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.True(captured!.Questions[0].IsOther);
        using var output = JsonDocument.Parse(result.OutputText);
        Assert.Equal("继续", output.RootElement.GetProperty("answers").GetProperty("confirm_path").GetProperty("answers")[0].GetString());
    }

    [Fact]
    public async Task RequestUserInputProvider_ShouldRejectQuestionsWithoutOptions()
    {
        var handler = CreateInteractionRuntimeHandler("request_user_input");
        using var args = JsonDocument.Parse("""
            {
              "questions": [
                {
                  "id": "notes",
                  "header": "补充说明",
                  "question": "请直接输入补充说明"
                }
              ]
            }
            """);

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(
                collaborationMode: new KernelCollaborationModeState(
                    "plan",
                    new KernelCollaborationModeSettings("gpt-5", null, null))),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("request_user_input requires non-empty options for every question", result.OutputText);
    }

    [Fact]
    public async Task SpawnAgentProvider_ShouldSerializeResponse()
    {
        KernelSpawnAgentRequest? captured = null;
        var handler = CreateCollaborationRuntimeHandler("spawn_agent");
        using var args = JsonDocument.Parse("""
            {
              "message": "调查这个问题",
              "agent_type": "explorer",
              "fork_context": true,
              "model": "gpt-5.4-mini",
              "reasoning_effort": "high"
            }
            """);

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(new KernelToolRuntimeServices(
                SpawnAgent: (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new KernelSpawnAgentResponse("agent_001", "Einstein"));
                })),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal("调查这个问题", captured!.Message);
        Assert.Equal("explorer", captured.AgentType);
        Assert.True(captured.ForkContext);
        Assert.Equal("gpt-5.4-mini", captured.Model);
        Assert.Equal("high", captured.ReasoningEffort);

        using var output = JsonDocument.Parse(result.OutputText);
        Assert.Equal("agent_001", output.RootElement.GetProperty("agent_id").GetString());
        Assert.Equal("Einstein", output.RootElement.GetProperty("nickname").GetString());
    }

    [Fact]
    public async Task SendInputProvider_ShouldSerializeSubmissionId()
    {
        KernelSendInputRequest? captured = null;
        var handler = CreateCollaborationRuntimeHandler("send_input");
        using var args = JsonDocument.Parse("""
            {
              "id": "agent_001",
              "message": "继续",
              "interrupt": true
            }
            """);

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(new KernelToolRuntimeServices(
                SendInputToAgent: (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new KernelSendInputResponse("submission_001"));
                })),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal("agent_001", captured!.Id);
        Assert.Equal("继续", captured.Message);
        Assert.True(captured.Interrupt);

        using var output = JsonDocument.Parse(result.OutputText);
        Assert.Equal("submission_001", output.RootElement.GetProperty("submission_id").GetString());
    }

    [Fact]
    public async Task ResumeAgentProvider_ShouldSerializeStatus()
    {
        var handler = CreateCollaborationRuntimeHandler("resume_agent");
        using var args = JsonDocument.Parse("""{ "id": "agent_001" }""");

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(new KernelToolRuntimeServices(
                ResumeAgent: (_, _) => Task.FromResult<JsonNode?>(JsonNode.Parse(@"{""completed"":""done""}")))),
            CancellationToken.None);

        Assert.True(result.Success);
        using var output = JsonDocument.Parse(result.OutputText);
        Assert.Equal("done", output.RootElement.GetProperty("status").GetProperty("completed").GetString());
    }

    [Fact]
    public async Task WaitProvider_ShouldSerializeStatusesAndTimeoutFlag()
    {
        var handler = CreateCollaborationRuntimeHandler("wait");
        using var args = JsonDocument.Parse("""
            {
              "ids": ["agent_001", "agent_002"],
              "timeout_ms": 2500
            }
            """);

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(new KernelToolRuntimeServices(
                WaitOnAgents: (ids, timeoutMs, _) =>
                {
                    Assert.Equal(new[] { "agent_001", "agent_002" }, ids);
                    Assert.Equal(2500, timeoutMs);
                    return Task.FromResult(new KernelWaitAgentsResponse(
                        new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
                        {
                            ["agent_001"] = JsonValue.Create("running"),
                            ["agent_002"] = JsonNode.Parse(@"{""completed"":""done""}"),
                        },
                        TimedOut: false));
                })),
            CancellationToken.None);

        Assert.True(result.Success);
        using var output = JsonDocument.Parse(result.OutputText);
        var status = output.RootElement.GetProperty("status");
        Assert.Equal("running", status.GetProperty("agent_001").GetString());
        Assert.Equal("done", status.GetProperty("agent_002").GetProperty("completed").GetString());
        Assert.False(output.RootElement.GetProperty("timed_out").GetBoolean());
    }

    [Fact]
    public async Task CloseAgentProvider_ShouldSerializeStatus()
    {
        var handler = CreateCollaborationRuntimeHandler("close_agent");
        using var args = JsonDocument.Parse("""{ "id": "agent_001" }""");

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(new KernelToolRuntimeServices(
                CloseAgent: (_, _) => Task.FromResult<JsonNode?>(JsonNode.Parse(@"{""errored"":""boom""}")))),
            CancellationToken.None);

        Assert.True(result.Success);
        using var output = JsonDocument.Parse(result.OutputText);
        Assert.Equal("boom", output.RootElement.GetProperty("status").GetProperty("errored").GetString());
    }

    [Fact]
    public async Task SearchToolProvider_ShouldGroupConnectorToolsIntoNamespaceDeferredTools()
    {
        var handler = CreateSearchRuntimeHandler("tool_search");
        using var args = JsonDocument.Parse("""
            {
              "query": "calendar event",
              "limit": 2
            }
            """);
        var dynamicTools = KernelDynamicToolResolver.Parse(JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                name = "mcp__codex_apps__calendar__create_event",
                server = "dynamic",
                connectorName = "Calendar",
                connectorDescription = "Calendar tools.",
                description = "Create a calendar event with title and attendees.",
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
                name = "mcp__codex_apps__calendar__delete_event",
                server = "dynamic",
                connectorName = "Calendar",
                connectorDescription = "Calendar tools.",
                description = "Delete a calendar event.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        event_id = new { type = "string" },
                    },
                },
            },
        }));

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(dynamicTools: dynamicTools),
            CancellationToken.None);

        Assert.True(result.Success);
        using var output = JsonDocument.Parse(result.OutputText);
        var tools = output.RootElement.GetProperty("tools");
        Assert.Single(tools.EnumerateArray());

        var toolNamespace = tools[0];
        Assert.Equal("namespace", toolNamespace.GetProperty("type").GetString());
        Assert.Equal("mcp__codex_apps__calendar", toolNamespace.GetProperty("name").GetString());
        Assert.Equal("Calendar tools.", toolNamespace.GetProperty("description").GetString());

        var deferredTools = toolNamespace.GetProperty("tools");
        Assert.Equal(2, deferredTools.GetArrayLength());
        Assert.All(deferredTools.EnumerateArray().ToArray(), static tool =>
        {
            Assert.Equal("function", tool.GetProperty("type").GetString());
            Assert.True(tool.GetProperty("defer_loading").GetBoolean());
        });
        var names = deferredTools.EnumerateArray()
            .Select(static tool => tool.GetProperty("name").GetString())
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "create_event", "delete_event" }, names);
    }

    [Fact]
    public async Task SearchToolProvider_ShouldReturnTopLevelDeferredFunctionWhenNoNamespace()
    {
        var handler = CreateSearchRuntimeHandler("tool_search");
        using var args = JsonDocument.Parse("""{ "query": "calendar create", "limit": 1 }""");
        var dynamicTools = KernelDynamicToolResolver.Parse(JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                name = "calendar_create_event",
                server = "dynamic",
                description = "Create a calendar event.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string" },
                    },
                },
            },
        }));

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(dynamicTools: dynamicTools),
            CancellationToken.None);

        Assert.True(result.Success);
        using var output = JsonDocument.Parse(result.OutputText);
        var tools = output.RootElement.GetProperty("tools");
        Assert.Single(tools.EnumerateArray());
        Assert.Equal("function", tools[0].GetProperty("type").GetString());
        Assert.Equal("calendar_create_event", tools[0].GetProperty("name").GetString());
        Assert.True(tools[0].GetProperty("defer_loading").GetBoolean());
    }

    [Fact]
    public async Task SearchToolProvider_ShouldRejectEmptyQuery()
    {
        var handler = CreateSearchRuntimeHandler("tool_search");
        using var args = JsonDocument.Parse("""{ "query": "   " }""");

        var result = await handler.ExecuteAsync(args.RootElement, CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("query must not be empty", result.OutputText);
    }

    [Fact]
    public void ToolSuggestProvider_BuildResponsesSpec_ShouldDescribeDiscoverableConnectors()
    {
        var spec = KernelToolDiscoveryRuntimeSupport.BuildSuggestProviderToolDefinition(
        [
            new KernelToolSuggestConnectorInfo(
                "connector_2128aebfecb84f64a069897515042a44",
                "Google Calendar",
                "Plan events and schedules.",
                "https://chatgpt.com/apps/google-calendar/connector_2128aebfecb84f64a069897515042a44"),
            new KernelToolSuggestConnectorInfo(
                "connector_68df038e0ba48191908c8434991bbac2",
                "Gmail",
                "Find and summarize email threads.",
                "https://chatgpt.com/apps/gmail/connector_68df038e0ba48191908c8434991bbac2"),
        ]);

        using var json = CompileProviderTool(spec);
        var description = json.RootElement.GetProperty("description").GetString();
        Assert.Contains("Google Calendar", description, StringComparison.Ordinal);
        Assert.Contains("Gmail", description, StringComparison.Ordinal);
        Assert.Contains("DO NOT explore or recommend tools that are not on this list.", description, StringComparison.Ordinal);

        var parameters = json.RootElement.GetProperty("parameters");
        var toolIdDescription = parameters.GetProperty("properties").GetProperty("tool_id").GetProperty("description").GetString();
        Assert.Contains("connector_2128aebfecb84f64a069897515042a44", toolIdDescription, StringComparison.Ordinal);
        Assert.Contains("connector_68df038e0ba48191908c8434991bbac2", toolIdDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolSuggestProvider_ShouldRejectInvalidArguments()
    {
        var handler = CreateSearchRuntimeHandler("tool_suggest");

        using var emptyReasonArgs = JsonDocument.Parse(
            """{"tool_type":"connector","action_type":"install","tool_id":"connector-1","suggest_reason":"   "}""");
        var emptyReason = await handler.ExecuteAsync(emptyReasonArgs.RootElement, CreateContext(), CancellationToken.None);
        Assert.False(emptyReason.Success);
        Assert.Equal("suggest_reason must not be empty", emptyReason.OutputText);

        using var pluginArgs = JsonDocument.Parse(
            """{"tool_type":"plugin","action_type":"enable","tool_id":"plugin-1","suggest_reason":"需要插件"}""");
        var pluginResult = await handler.ExecuteAsync(pluginArgs.RootElement, CreateContext(), CancellationToken.None);
        Assert.False(pluginResult.Success);
        Assert.Equal("plugin tool suggestions are not currently available", pluginResult.OutputText);

        using var invalidActionArgs = JsonDocument.Parse(
            """{"tool_type":"connector","action_type":"enable","tool_id":"connector-1","suggest_reason":"需要能力"}""");
        var invalidAction = await handler.ExecuteAsync(invalidActionArgs.RootElement, CreateContext(), CancellationToken.None);
        Assert.False(invalidAction.Success);
        Assert.Equal("connector tool suggestions currently support only action_type=\"install\"", invalidAction.OutputText);
    }

    [Fact]
    public async Task ToolSuggestProvider_ShouldRequestElicitation_AndMarkCompletedAfterRefresh()
    {
        var handler = CreateSearchRuntimeHandler("tool_suggest");
        McpServerElicitationRequest? captured = null;
        var connector = new KernelToolSuggestConnectorInfo(
            "connector_2128aebfecb84f64a069897515042a44",
            "Google Calendar",
            "Plan events and schedules.",
            "https://chatgpt.com/apps/google-calendar/connector_2128aebfecb84f64a069897515042a44");

        using var args = JsonDocument.Parse(
            """{"tool_type":"connector","action_type":"install","tool_id":"connector_2128aebfecb84f64a069897515042a44","suggest_reason":"Plan and reference events from your calendar"}""");

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(
                new KernelToolRuntimeServices(
                    ListToolSuggestDiscoverableConnectors: _ => Task.FromResult<IReadOnlyList<KernelToolSuggestConnectorInfo>>([connector]),
                    RefreshOpenAiAppsToolSnapshot: _ => Task.FromResult<KernelOpenAiAppsToolSnapshot?>(new KernelOpenAiAppsToolSnapshot(
                        null,
                        [connector]))),
                mcpRequester: (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new McpServerElicitationResponse("accept", null));
                }),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal("codex_apps", captured!.ServerName);
        Assert.Equal("form", captured.Mode);
        Assert.Contains("Google Calendar could help with this request.", captured.Message, StringComparison.Ordinal);
        Assert.Contains("Open the installation link to install it", captured.Message, StringComparison.Ordinal);

        Assert.True(captured.Meta.HasValue);
        using var metaJson = JsonDocument.Parse(captured.Meta.Value.GetRawText());
        Assert.Equal("tool_suggestion", metaJson.RootElement.GetProperty("codex_approval_kind").GetString());
        Assert.Equal("connector", metaJson.RootElement.GetProperty("tool_type").GetString());
        Assert.Equal("install", metaJson.RootElement.GetProperty("suggest_type").GetString());
        Assert.Equal(connector.Id, metaJson.RootElement.GetProperty("tool_id").GetString());
        Assert.Equal(connector.Name, metaJson.RootElement.GetProperty("tool_name").GetString());
        Assert.Equal(connector.InstallUrl, metaJson.RootElement.GetProperty("install_url").GetString());

        using var output = JsonDocument.Parse(result.OutputText);
        Assert.True(output.RootElement.GetProperty("completed").GetBoolean());
        Assert.True(output.RootElement.GetProperty("user_confirmed").GetBoolean());
        Assert.Equal(connector.Id, output.RootElement.GetProperty("tool_id").GetString());
    }

    [Fact]
    public async Task ToolSuggestProvider_ShouldReturnUncompletedWhenUserDeclines()
    {
        var handler = CreateSearchRuntimeHandler("tool_suggest");
        var connector = new KernelToolSuggestConnectorInfo(
            "connector_68df038e0ba48191908c8434991bbac2",
            "Gmail",
            "Find and summarize email threads.",
            "https://chatgpt.com/apps/gmail/connector_68df038e0ba48191908c8434991bbac2");

        using var args = JsonDocument.Parse(
            """{"tool_type":"connector","action_type":"install","tool_id":"connector_68df038e0ba48191908c8434991bbac2","suggest_reason":"Find and reference emails from your inbox"}""");

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(
                new KernelToolRuntimeServices(
                    ListToolSuggestDiscoverableConnectors: _ => Task.FromResult<IReadOnlyList<KernelToolSuggestConnectorInfo>>([connector]),
                    RefreshOpenAiAppsToolSnapshot: _ => throw new InvalidOperationException("should not refresh on decline")),
                mcpRequester: (_, _) => Task.FromResult(new McpServerElicitationResponse("decline", null))),
            CancellationToken.None);

        Assert.True(result.Success);
        using var output = JsonDocument.Parse(result.OutputText);
        Assert.False(output.RootElement.GetProperty("completed").GetBoolean());
        Assert.False(output.RootElement.GetProperty("user_confirmed").GetBoolean());
        Assert.Equal(connector.Name, output.RootElement.GetProperty("tool_name").GetString());
    }

    [Fact]
    public async Task SpawnAgentsOnCsvProvider_ShouldInvokeRuntimeServices()
    {
        var handler = CreateFanoutRuntimeHandler("spawn_agents_on_csv");
        KernelSpawnAgentsOnCsvRequest? captured = null;
        using var args = JsonDocument.Parse("""
            {
              "csv_path": "input.csv",
              "instruction": "处理 {name}",
              "id_column": "id",
              "output_csv_path": "out.csv",
              "max_concurrency": 3,
              "max_workers": 1,
              "max_runtime_seconds": 120,
              "output_schema": {
                "type": "object"
              }
            }
            """);

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(new KernelToolRuntimeServices(
                SpawnAgentsOnCsv: (request, _) =>
                {
                    captured = request;
                    return Task.FromResult(new KernelSpawnAgentsOnCsvResponse(
                        JobId: "job_001",
                        Status: "completed",
                        OutputCsvPath: "out.csv",
                        TotalItems: 2,
                        CompletedItems: 2,
                        FailedItems: 0,
                        JobError: null,
                        FailedItemErrors: null));
                })),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal("input.csv", captured!.CsvPath);
        Assert.Equal("处理 {name}", captured.Instruction);
        Assert.Equal("id", captured.IdColumn);
        Assert.Equal("out.csv", captured.OutputCsvPath);
        Assert.Equal(3, captured.MaxConcurrency);
        Assert.Equal(1, captured.MaxWorkers);
        Assert.Equal(120, captured.MaxRuntimeSeconds);
        Assert.True(captured.OutputSchema.HasValue);
        Assert.Equal("object", captured.OutputSchema.Value.GetProperty("type").GetString());

        using var output = JsonDocument.Parse(result.OutputText);
        Assert.Equal("job_001", output.RootElement.GetProperty("job_id").GetString());
        Assert.Equal("completed", output.RootElement.GetProperty("status").GetString());
        Assert.Equal("out.csv", output.RootElement.GetProperty("output_csv_path").GetString());
        Assert.Equal(2, output.RootElement.GetProperty("completed_items").GetInt32());
    }

    [Fact]
    public async Task SpawnAgentsOnCsvProvider_ShouldRejectNonObjectOutputSchema()
    {
        var handler = CreateFanoutRuntimeHandler("spawn_agents_on_csv");
        using var args = JsonDocument.Parse("""{ "csv_path": "input.csv", "instruction": "处理", "output_schema": 1 }""");

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(new KernelToolRuntimeServices(
                SpawnAgentsOnCsv: (_, _) => throw new InvalidOperationException("should not be called"))),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("output_schema must be a JSON object", result.OutputText);
    }
    [Fact]
    public async Task ReportAgentJobResultProvider_ShouldInvokeRuntimeServices()
    {
        var handler = CreateFanoutRuntimeHandler("report_agent_job_result");
        string? capturedJobId = null;
        string? capturedItemId = null;
        JsonElement capturedResult = default;
        var capturedStop = false;
        using var args = JsonDocument.Parse("""
            {
              "job_id": "job_001",
              "item_id": "item_001",
              "result": {
                "summary": "done"
              },
              "stop": true
            }
            """);

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(new KernelToolRuntimeServices(
                ReportAgentJobResult: (jobId, itemId, payload, stop, _) =>
                {
                    capturedJobId = jobId;
                    capturedItemId = itemId;
                    capturedResult = payload;
                    capturedStop = stop;
                    return Task.FromResult(true);
                })),
            CancellationToken.None);

        Assert.True(result.Success);
        using var output = JsonDocument.Parse(result.OutputText);
        Assert.True(output.RootElement.GetProperty("accepted").GetBoolean());
        Assert.Equal("job_001", capturedJobId);
        Assert.Equal("item_001", capturedItemId);
        Assert.Equal("done", capturedResult.GetProperty("summary").GetString());
        Assert.True(capturedStop);
    }

    [Fact]
    public async Task ReportAgentJobResultProvider_ShouldRejectNonObjectResult()
    {
        var handler = CreateFanoutRuntimeHandler("report_agent_job_result");
        using var args = JsonDocument.Parse("""{ "job_id": "job_001", "item_id": "item_001", "result": 1 }""");

        var result = await handler.ExecuteAsync(
            args.RootElement,
            CreateContext(new KernelToolRuntimeServices(ReportAgentJobResult: (_, _, _, _, _) => Task.FromResult(true))),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("result must be a JSON object", result.OutputText);
    }

    private static KernelToolCallContext CreateContext(
        KernelToolRuntimeServices? runtimeServices = null,
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools = null,
        KernelCollaborationModeState? collaborationMode = null,
        bool defaultModeRequestUserInputEnabled = false,
        Func<KernelRequestUserInputRequest, CancellationToken, Task<KernelRequestUserInputResponse>>? userInputRequester = null,
        Func<McpServerElicitationRequest, CancellationToken, Task<McpServerElicitationResponse>>? mcpRequester = null)
        => new(
            ThreadId: "thread_001",
            TurnId: "turn_001",
            Cwd: Environment.CurrentDirectory,
            McpServerElicitationRequester: mcpRequester,
            RuntimeServices: runtimeServices,
            DynamicTools: dynamicTools,
            ItemId: "turn_001_call",
            CollaborationMode: collaborationMode,
            DefaultModeRequestUserInputEnabled: defaultModeRequestUserInputEnabled,
            UserInputRequester: userInputRequester);

    private static JsonDocument CompileProviderTool(ProviderResponsesToolDefinition definition)
    {
        var tools = new OpenAiResponsesToolSurfaceBuilder().Build(
            new ProviderResponsesToolSurfaceBuilderContext([definition]));
        var tool = Assert.Single(tools);
        return JsonDocument.Parse(JsonSerializer.Serialize(tool));
    }

    private static IKernelToolHandler CreateSearchRuntimeHandler(string toolKey)
        => new KernelContractToolHandlerAdapter(
            new SearchToolProvider().CreateHandler(toolKey, new TianShuToolActivationContext()));

    private static IKernelToolHandler CreateCollaborationRuntimeHandler(string toolKey)
        => new KernelContractToolHandlerAdapter(
            new CollaborationToolProvider().CreateHandler(toolKey, new TianShuToolActivationContext()));

    private static IKernelToolHandler CreateInteractionRuntimeHandler(string toolKey)
        => new KernelContractToolHandlerAdapter(
            new InteractionToolProvider().CreateHandler(toolKey, new TianShuToolActivationContext()));

    private static IKernelToolHandler CreateFanoutRuntimeHandler(string toolKey)
        => new KernelContractToolHandlerAdapter(
            new FanoutToolProvider().CreateHandler(toolKey, new TianShuToolActivationContext()));
}


