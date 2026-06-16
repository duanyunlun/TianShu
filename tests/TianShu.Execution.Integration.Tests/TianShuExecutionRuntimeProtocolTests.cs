using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Tools;
using TianShu.Contracts.Workflows;
using TianShu.Execution.Runtime.Models;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Events;
using Task = System.Threading.Tasks.Task;

namespace TianShu.Execution.Integration.Tests;

public sealed class TianShuExecutionRuntimeProtocolTests
{
    [Fact]
    public void BuildInitializeParams_EnablesExperimentalApi()
    {
        var runtime = new TianShuExecutionRuntime();

        var payload = ReflectionTestHelper.InvokeMethod(runtime, "BuildInitializeParams");
        Assert.NotNull(payload);

        var json = JsonSerializer.SerializeToElement(payload);
        Assert.True(json.GetProperty("capabilities").GetProperty("experimentalApi").GetBoolean());
    }

    [Fact]
    public void BuildInitializeParams_OptsOutDuplicateAgentMessageNotifications()
    {
        var runtime = new TianShuExecutionRuntime();

        var payload = ReflectionTestHelper.InvokeMethod(runtime, "BuildInitializeParams");
        Assert.NotNull(payload);

        var json = JsonSerializer.SerializeToElement(payload);
        var optOutMethods = json.GetProperty("capabilities").GetProperty("optOutNotificationMethods").EnumerateArray().Select(static item => item.GetString()).ToArray();
        Assert.Empty(optOutMethods);
    }

    [Fact]
    public void BuildTurnStartParams_WhenCollaborationModeConfigured_UsesCodexShape()
    {
        var runtime = new TianShuExecutionRuntime();
        var options = new ExecutionRuntimeOptions
        {
            Model = "gpt-5-codex",
        };

        ReflectionTestHelper.SetProperty(options, "CollaborationMode", "plan");
        ReflectionTestHelper.SetField(runtime, "options", options);

        var payload = ReflectionTestHelper.InvokeMethod(runtime, "BuildTurnStartParams", "thread-123", "hello world", Array.Empty<ConversationMessage>());
        Assert.NotNull(payload);

        var json = JsonSerializer.SerializeToElement(payload);
        var collaborationMode = json.GetProperty("collaborationMode");
        Assert.Equal("plan", collaborationMode.GetProperty("mode").GetString());

        var settings = collaborationMode.GetProperty("settings");
        Assert.Equal("gpt-5-codex", settings.GetProperty("model").GetString());
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("reasoning_effort").ValueKind);
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("developer_instructions").ValueKind);
    }

    [Fact]
    public void BuildTurnStartParams_WhenInteractionEnvelopeProvided_UsesMinimalEnvelopePayload()
    {
        var runtime = new TianShuExecutionRuntime();
        var interactionEnvelope = new InteractionEnvelopeRef(
            new InteractionEnvelopeId("interaction_runtime_protocol_001"),
            InteractionSourceKind.Host,
            "cli",
            DateTimeOffset.FromUnixTimeMilliseconds(1_746_200_000_000));

        var payload = ReflectionTestHelper.InvokeMethod(
            runtime,
            "BuildTurnStartParams",
            "thread-123",
            (IReadOnlyList<AgentUserInput>)
            [
                new TextUserInput
                {
                    Type = "text",
                    Text = "hello",
                },
            ],
            Array.Empty<ConversationMessage>(),
            interactionEnvelope);
        Assert.NotNull(payload);

        var json = JsonSerializer.SerializeToElement(payload);
        var envelopeJson = json.GetProperty("interactionEnvelope");
        Assert.Equal("interaction_runtime_protocol_001", envelopeJson.GetProperty("id").GetString());
        Assert.Equal((int)InteractionSourceKind.Host, envelopeJson.GetProperty("sourceKind").GetInt32());
        Assert.Equal("cli", envelopeJson.GetProperty("surface").GetString());
    }

    [Fact]
    public async Task HandleToolRequestUserInputAsync_WritesCodexAnswerEnvelope()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var requestJson =
            """
            {
              "method": "item/tool/requestUserInput",
              "id": 42,
              "params": {
                "threadId": "thread-1",
                "turnId": "turn-1",
                "itemId": "input-1",
                "questions": [
                  {
                    "id": "choice",
                    "header": "Pick one",
                    "question": "Pick one",
                    "isOther": true,
                    "isSecret": false,
                    "options": null
                  },
                  {
                    "id": "notes",
                    "header": "Notes",
                    "question": "Notes",
                    "isOther": true,
                    "isSecret": false,
                    "options": null
                  }
                ]
              }
            }
            """;

        var pendingTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(requestJson),
            CancellationToken.None);

        ControlPlaneConversationStreamEvent? userInputEvent = null;
        for (var attempt = 0; attempt < 20 && userInputEvent is null; attempt++)
        {
            userInputEvent = events.LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.UserInputRequested);
            if (userInputEvent is null)
            {
                await Task.Delay(10);
            }
        }

        Assert.NotNull(userInputEvent);
        Assert.Equal("thread-1", userInputEvent!.ThreadId?.Value);
        Assert.Equal("turn-1", userInputEvent.TurnId?.Value);
        var userInputPayload = GetUserInputRequestPayload(userInputEvent);
        var questions = ReadStructuredItems(userInputPayload, "questions");
        Assert.Equal(2, questions.Count);
        Assert.Equal("choice", ReadStructuredString(questions[0], "id"));
        Assert.Equal("Pick one", ReadStructuredString(questions[0], "header"));
        Assert.True(ReadStructuredBoolean(questions[0], "isOther"));
        AssertStructuredNull(ReadStructuredValue(questions[0], "options"));
        Assert.Equal("Notes", ReadStructuredString(questions[1], "header"));
        Assert.True(ReadStructuredBoolean(questions[1], "isOther"));
        AssertStructuredNull(ReadStructuredValue(questions[1], "options"));
        Assert.Equal("item/tool/requestUserInput", userInputEvent.SourceMethod);
        Assert.Null(userInputEvent.Diagnostics);

        var startedEvent = events
            .FirstOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted && item.CallId?.Value == "input-1");
        Assert.NotNull(startedEvent);
        Assert.Equal("requestUserInput", startedEvent!.ToolName);
        Assert.Equal("input-1", startedEvent.CallId?.Value);
        var startedPayload = GetToolCallPayload(startedEvent);
        Assert.Equal("requestUserInput", ReadStructuredString(startedPayload, "toolName"));
        Assert.Equal("input-1", ReadStructuredString(startedPayload, "callId"));
        Assert.Equal("awaitingUserInput", ReadStructuredString(startedPayload, "status"));
        Assert.Equal("request_user_input", ReadStructuredString(startedPayload, "phase"));
        Assert.Contains("Pick one", ReadStructuredString(startedPayload, "inputText"), StringComparison.Ordinal);
        Assert.Contains("Notes", ReadStructuredString(startedPayload, "inputText"), StringComparison.Ordinal);
        Assert.Equal("item/tool/requestUserInput", startedEvent.SourceMethod);

        var responded = false;
        for (var attempt = 0; attempt < 20 && !responded; attempt++)
        {
            responded = await runtime.RespondToUserInputAsync(
                new ControlPlaneUserInputSubmission
                {
                    CallId = new CallId("input-1"),
                    Answers = new Dictionary<string, StructuredValue>
                    {
                        ["choice"] = StructuredValue.FromString("Option A"),
                        ["notes"] = StructuredValue.FromArray(
                            new[]
                            {
                                StructuredValue.FromString("first"),
                                StructuredValue.FromString("second"),
                            }),
                    },
                },
                CancellationToken.None);

            if (!responded)
            {
                await Task.Delay(10);
            }
        }

        Assert.True(responded);
        await ReflectionTestHelper.AwaitTaskResultAsync(pendingTask);

        await writer.FlushAsync();
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        Assert.False(string.IsNullOrWhiteSpace(payload));

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal(42, root.GetProperty("id").GetInt32());

        var answers = root.GetProperty("result").GetProperty("answers");
        Assert.Equal("Option A", answers.GetProperty("choice").GetProperty("answers")[0].GetString());
        Assert.Equal(2, answers.GetProperty("notes").GetProperty("answers").GetArrayLength());
        Assert.Equal("first", answers.GetProperty("notes").GetProperty("answers")[0].GetString());
        Assert.Equal("second", answers.GetProperty("notes").GetProperty("answers")[1].GetString());

        var completedEvent = events
            .LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted && item.CallId?.Value == "input-1");
        Assert.NotNull(completedEvent);
        Assert.Equal("requestUserInput", completedEvent!.ToolName);
        var completedPayload = GetToolCallPayload(completedEvent);
        Assert.Equal("requestUserInput", ReadStructuredString(completedPayload, "toolName"));
        Assert.Equal("completed", ReadStructuredString(completedPayload, "status"));
        Assert.Equal("request_user_input", ReadStructuredString(completedPayload, "phase"));
        using var answerSummary = JsonDocument.Parse(ReadStructuredString(completedPayload, "outputText")!);
        Assert.Equal("Option A", answerSummary.RootElement.GetProperty("choice").GetString());
        Assert.Equal("first", answerSummary.RootElement.GetProperty("notes")[0].GetString());
        Assert.Equal("second", answerSummary.RootElement.GetProperty("notes")[1].GetString());
        Assert.Equal("item/tool/requestUserInput", completedEvent.SourceMethod);
    }

    [Fact]
    public async Task HandlePermissionRequestApprovalAsync_WritesPermissionsEnvelope()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var requestJson =
            """
            {
              "method": "item/permissions/requestApproval",
              "id": 43,
              "params": {
                "threadId": "thread-1",
                "turnId": "turn-1",
                "itemId": "permission-1",
                "reason": "Need broader access",
                "permissions": {
                  "network": {
                    "enabled": true
                  },
                  "file_system": {
                    "write": [
                      "src"
                    ]
                  }
                }
              }
            }
            """;

        var pendingTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(requestJson),
            CancellationToken.None);

        ControlPlaneConversationStreamEvent? permissionEvent = null;
        for (var attempt = 0; attempt < 20 && permissionEvent is null; attempt++)
        {
            permissionEvent = events.LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.PermissionRequested);
            if (permissionEvent is null)
            {
                await Task.Delay(10);
            }
        }

        Assert.NotNull(permissionEvent);
        Assert.Equal("thread-1", permissionEvent!.ThreadId?.Value);
        Assert.Equal("turn-1", permissionEvent.TurnId?.Value);
        var permissionPayload = GetPermissionRequestPayload(permissionEvent);
        Assert.Equal("Need broader access", ReadStructuredString(permissionPayload, "reason"));
        using var permissionsJson = JsonDocument.Parse(ReadStructuredString(permissionPayload, "permissionsJson")!);
        Assert.True(permissionsJson.RootElement.GetProperty("network").GetProperty("enabled").GetBoolean());
        Assert.Equal("src", permissionsJson.RootElement.GetProperty("file_system").GetProperty("write")[0].GetString());
        Assert.Collection(
            ReadStructuredItems(permissionPayload, "fields"),
            field =>
            {
                Assert.Equal("network", ReadStructuredString(field, "key"));
                Assert.Equal("json", ReadStructuredString(field, "valueType"));
                using var valueJson = JsonDocument.Parse(ReadStructuredString(field, "valueText")!);
                Assert.True(valueJson.RootElement.GetProperty("enabled").GetBoolean());
            },
            field =>
            {
                Assert.Equal("file_system", ReadStructuredString(field, "key"));
                Assert.Equal("json", ReadStructuredString(field, "valueType"));
                using var valueJson = JsonDocument.Parse(ReadStructuredString(field, "valueText")!);
                Assert.Equal("src", valueJson.RootElement.GetProperty("write")[0].GetString());
            });
        Assert.Equal("item/permissions/requestApproval", permissionEvent.SourceMethod);
        Assert.Null(permissionEvent.Diagnostics);

        var startedEvent = events
            .FirstOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted && item.CallId?.Value == "permission-1");
        Assert.NotNull(startedEvent);
        Assert.Equal("request_permissions", startedEvent!.ToolName);
        Assert.Equal("permission-1", startedEvent.CallId?.Value);
        var startedPayload = GetToolCallPayload(startedEvent);
        Assert.Equal("request_permissions", ReadStructuredString(startedPayload, "toolName"));
        Assert.Equal("permission-1", ReadStructuredString(startedPayload, "callId"));
        Assert.Equal("awaitingPermission", ReadStructuredString(startedPayload, "status"));
        Assert.Equal("request_permission", ReadStructuredString(startedPayload, "phase"));
        Assert.Equal("item/permissions/requestApproval", startedEvent.SourceMethod);

        var responded = false;
        for (var attempt = 0; attempt < 20 && !responded; attempt++)
        {
            responded = await runtime.RespondToPermissionRequestAsync(
                new ControlPlanePermissionGrant
                {
                    CallId = new CallId("permission-1"),
                    Permissions = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["network"] = StructuredValue.FromPlainObject(new Dictionary<string, object?> { ["enabled"] = true }),
                        ["file_system"] = StructuredValue.FromPlainObject(new Dictionary<string, object?> { ["write"] = new[] { "src" } }),
                    },
                    Scope = ControlPlanePermissionScope.Session,
                },
                CancellationToken.None);

            if (!responded)
            {
                await Task.Delay(10);
            }
        }

        Assert.True(responded);
        await ReflectionTestHelper.AwaitTaskResultAsync(pendingTask);

        await writer.FlushAsync();
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        Assert.False(string.IsNullOrWhiteSpace(payload));

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal(43, root.GetProperty("id").GetInt32());

        var result = root.GetProperty("result");
        Assert.Equal("session", result.GetProperty("scope").GetString());
        Assert.True(result.GetProperty("permissions").GetProperty("network").GetProperty("enabled").GetBoolean());
        Assert.Equal("src", result.GetProperty("permissions").GetProperty("file_system").GetProperty("write")[0].GetString());

        var completedEvent = events
            .LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted && item.CallId?.Value == "permission-1");
        Assert.NotNull(completedEvent);
        Assert.Equal("request_permissions", completedEvent!.ToolName);
        Assert.Equal("completed", completedEvent.Status);
        var completedPayload = GetToolCallPayload(completedEvent);
        Assert.Equal("completed", ReadStructuredString(completedPayload, "status"));
        Assert.Equal("request_permission", ReadStructuredString(completedPayload, "phase"));
        Assert.Contains("scope=session", ReadStructuredString(completedPayload, "outputText"), StringComparison.Ordinal);
        Assert.Equal("item/permissions/requestApproval", completedEvent.SourceMethod);
    }

    [Fact]
    public async Task HandleMcpServerElicitationRequestAsync_WhenApproved_WritesElicitationEnvelope()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);

        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var requestJson =
            """
            {
              "method": "mcpServer/elicitation/request",
              "id": 44,
              "params": {
                "threadId": "thread-1",
                "turnId": "turn-1",
                "serverName": "codex_apps",
                "mode": "form",
                "message": "Google Calendar could help with this request.",
                "_meta": {
                  "codex_approval_kind": "tool_suggestion",
                  "tool_name": "Google Calendar",
                  "install_url": "https://chatgpt.com/apps/google-calendar/connector_2128aebfecb84f64a069897515042a44"
                }
              }
            }
            """;

        var pendingTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(requestJson),
            CancellationToken.None);

        ControlPlaneConversationStreamEvent? approvalEvent = null;
        for (var attempt = 0; attempt < 20 && approvalEvent is null; attempt++)
        {
            approvalEvent = events.LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ApprovalRequested);
            if (approvalEvent is null)
            {
                await Task.Delay(10);
            }
        }

        Assert.NotNull(approvalEvent);
        Assert.Equal("tool_suggest", approvalEvent!.ToolName);
        Assert.Equal("mcpServer/elicitation/request", approvalEvent.Message);
        Assert.Contains("Google Calendar could help with this request.", approvalEvent.Text, StringComparison.Ordinal);
        var approvalPayload = GetApprovalRequestPayload(approvalEvent);
        Assert.Equal("tool_suggestion", ReadStructuredString(approvalPayload, "approvalKind"));
        Assert.Equal("tool_suggest", ReadStructuredString(approvalPayload, "toolName"));
        Assert.Equal("mcpServer/elicitation/request", approvalEvent.SourceMethod);
        Assert.Null(approvalEvent.Diagnostics);

        var responded = await runtime.RespondToApprovalAsync(
            new ControlPlaneApprovalResolution
            {
                CallId = new CallId(approvalEvent.CallId!.Value),
                Decision = ControlPlaneApprovalDecision.Approve,
                Note = "已确认",
            },
            CancellationToken.None);
        Assert.True(responded);

        await ReflectionTestHelper.AwaitTaskResultAsync(pendingTask);

        await writer.FlushAsync();
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        Assert.False(string.IsNullOrWhiteSpace(payload));

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal(44, root.GetProperty("id").GetInt32());
        var result = root.GetProperty("result");
        Assert.Equal("accept", result.GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Object, result.GetProperty("content").ValueKind);

        var startedEvent = events
            .FirstOrDefault(item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted && item.CallId?.Value == approvalEvent.CallId?.Value);
        Assert.NotNull(startedEvent);
        Assert.Equal("mcpServer/elicitation/request", startedEvent!.SourceMethod);

        var completedEvent = events
            .LastOrDefault(item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted && item.CallId?.Value == approvalEvent.CallId?.Value);
        Assert.NotNull(completedEvent);
        Assert.Equal("mcpServer/elicitation/request", completedEvent!.SourceMethod);
    }

    [Fact]
    public async Task HandleMcpServerElicitationRequestAsync_WhenFormRequestArrives_UsesUserInputFlow_AndPreservesContent()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);

        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var requestJson =
            """
            {
              "method": "mcpServer/elicitation/request",
              "id": 45,
              "params": {
                "threadId": "thread-form-1",
                "turnId": "turn-form-1",
                "serverName": "calendar_mcp",
                "elicitationId": "elicitation-form-001",
                "mode": "form",
                "message": "Provide calendar details.",
                "requestedSchema": {
                  "type": "object",
                  "properties": {
                    "calendarId": {
                      "type": "string",
                      "title": "Calendar Id",
                      "description": "Target calendar id"
                    },
                    "visibility": {
                      "type": "string",
                      "enum": ["public", "private"]
                    }
                  },
                  "required": ["calendarId"]
                }
              }
            }
            """;

        var pendingTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(requestJson),
            CancellationToken.None);

        ControlPlaneConversationStreamEvent? inputEvent = null;
        for (var attempt = 0; attempt < 20 && inputEvent is null; attempt++)
        {
            inputEvent = events.LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.UserInputRequested);
            if (inputEvent is null)
            {
                await Task.Delay(10);
            }
        }

        Assert.NotNull(inputEvent);
        Assert.Equal("calendar_mcp", inputEvent!.ToolName);
        Assert.Equal("mcpServer/elicitation/request", inputEvent.Message);
        var inputPayload = GetUserInputRequestPayload(inputEvent);
        Assert.Equal("form", ReadStructuredString(inputPayload, "mode"));
        Assert.Equal("calendar_mcp", ReadStructuredString(inputPayload, "serverName"));
        Assert.Equal("elicitation-form-001", ReadStructuredString(inputPayload, "elicitationId"));
        Assert.Equal("mcpServer/elicitation/request", inputEvent.SourceMethod);
        Assert.Equal("object", ReadStructuredString(inputPayload, "requestedSchema", "type"));
        Assert.Collection(
            ReadStructuredItems(inputPayload, "questions"),
            question =>
            {
                Assert.Equal("calendarId", ReadStructuredString(question, "id"));
                Assert.Equal("Calendar Id", ReadStructuredString(question, "header"));
                Assert.Contains("必填", ReadStructuredString(question, "prompt"), StringComparison.Ordinal);
            },
            question =>
            {
                Assert.Equal("visibility", ReadStructuredString(question, "id"));
                Assert.Equal("visibility", ReadStructuredString(question, "header"));
                Assert.Collection(
                    ReadStructuredItems(question, "options"),
                    option => Assert.Equal("public", ReadStructuredString(option, "label")),
                    option => Assert.Equal("private", ReadStructuredString(option, "label")));
            });
        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.ApprovalRequested);

        var responded = await runtime.RespondToUserInputAsync(
            new ControlPlaneUserInputSubmission
            {
                CallId = new CallId(inputEvent.CallId!.Value),
                Answers = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["calendarId"] = StructuredValue.FromString("primary"),
                    ["visibility"] = StructuredValue.FromString("private"),
                },
            },
            CancellationToken.None);
        Assert.True(responded);

        await ReflectionTestHelper.AwaitTaskResultAsync(pendingTask);

        await writer.FlushAsync();
        stream.Position = 0;
        using var document = JsonDocument.Parse(await new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true).ReadToEndAsync());
        var result = document.RootElement.GetProperty("result");
        Assert.Equal("accept", result.GetProperty("action").GetString());
        Assert.Equal("primary", result.GetProperty("content").GetProperty("calendarId").GetString());
        Assert.Equal("private", result.GetProperty("content").GetProperty("visibility").GetString());

        var startedEvent = events
            .FirstOrDefault(item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted && item.CallId?.Value == inputEvent.CallId?.Value);
        Assert.NotNull(startedEvent);
        Assert.Equal("mcpServer/elicitation/request", startedEvent!.SourceMethod);

        var completedEvent = events
            .LastOrDefault(item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted && item.CallId?.Value == inputEvent.CallId?.Value);
        Assert.NotNull(completedEvent);
        Assert.Equal("mcpServer/elicitation/request", completedEvent!.SourceMethod);
    }

    [Fact]
    public async Task HandleMcpServerElicitationRequestAsync_WhenUrlRequestIsDeclined_WritesDeclineWithoutContent()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);

        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var requestJson =
            """
            {
              "method": "mcpServer/elicitation/request",
              "id": 46,
              "params": {
                "threadId": "thread-url-1",
                "turnId": "turn-url-1",
                "serverName": "browser_mcp",
                "elicitationId": "elicitation-url-001",
                "mode": "url",
                "message": "Open the verification URL.",
                "url": "https://example.com/verify"
              }
            }
            """;

        var pendingTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(requestJson),
            CancellationToken.None);

        ControlPlaneConversationStreamEvent? inputEvent = null;
        for (var attempt = 0; attempt < 20 && inputEvent is null; attempt++)
        {
            inputEvent = events.LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.UserInputRequested);
            if (inputEvent is null)
            {
                await Task.Delay(10);
            }
        }

        Assert.NotNull(inputEvent);
        var inputPayload = GetUserInputRequestPayload(inputEvent!);
        Assert.Equal("url", ReadStructuredString(inputPayload, "mode"));
        Assert.Equal("https://example.com/verify", ReadStructuredString(inputPayload, "url"));
        Assert.Single(ReadStructuredItems(inputPayload, "questions"));
        Assert.Equal("content", ReadStructuredString(ReadStructuredItems(inputPayload, "questions")[0], "id"));

        var responded = await runtime.RespondToUserInputAsync(
            new ControlPlaneUserInputSubmission
            {
                CallId = new CallId(inputEvent.CallId!.Value),
                Answers = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["_action"] = StructuredValue.FromString("decline"),
                },
            },
            CancellationToken.None);
        Assert.True(responded);

        await ReflectionTestHelper.AwaitTaskResultAsync(pendingTask);

        await writer.FlushAsync();
        stream.Position = 0;
        using var document = JsonDocument.Parse(await new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true).ReadToEndAsync());
        var result = document.RootElement.GetProperty("result");
        Assert.Equal("decline", result.GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("content").ValueKind);
    }

    [Fact]
    public async Task SendFollowUpAsync_WhenModeIsQueue_WaitsForActiveTurnCompletionBeforeStartingNextTurn()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-queue-1", activeTurnId: "turn-active-1");
        using var streamHandle = stream;
        using var writerHandle = writer;

        var pendingTask = runtime.SendFollowUpAsync("继续处理", FollowUpMode.Queue, CancellationToken.None);
        await Task.Delay(100);
        Assert.Equal(0, stream.Length);

        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn-active-1", true, "completed", "上一轮已完成", null, """{"status":"completed"}""");
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"turn":{"id":"turn_queue_2","status":"inProgress"}}""");

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.Equal("turn_queue_2", result.TurnId);
        Assert.Equal("inProgress", result.TurnStatus);

        var requests = await ReadRequestDocumentsAsync(stream);
        Assert.Single(requests);
        Assert.Equal("turn/start", requests[0].RootElement.GetProperty("method").GetString());
        var parameters = requests[0].RootElement.GetProperty("params");
        Assert.Equal("thread-queue-1", parameters.GetProperty("threadId").GetString());
        var input = parameters.GetProperty("input");
        Assert.Single(input.EnumerateArray());
        Assert.Equal("继续处理", input[0].GetProperty("text").GetString());
        requests[0].Dispose();
    }

    [Fact]
    public async Task SendAsync_WhenActiveThreadMissing_LazilyStartsThreadBeforeTurnStart()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());
        ReflectionTestHelper.SetField(runtime, "activeThreadId", null);
        ReflectionTestHelper.SetField(runtime, "activeTurnId", null);

        var pendingTask = runtime.SendAsync("首次发送", Array.Empty<ConversationMessage>(), CancellationToken.None);
        var threadStartRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, threadStartRequestId, """{"thread":{"id":"thread-lazy-1"}}""");
        var turnStartRequestId = await WaitForPendingResponseIdAsync(runtime, threadStartRequestId);
        CompletePendingResponse(runtime, turnStartRequestId, """{"turn":{"id":"turn_lazy_1","status":"inProgress"}}""");
        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn_lazy_1", true, "completed", "已完成", null, """{"status":"completed"}""");

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.True(
            string.Equals(result.Message, "已完成", StringComparison.Ordinal)
            || string.Equals(result.Message, "消息已发送，turnId=turn_lazy_1，请关注流式事件。", StringComparison.Ordinal),
            $"Unexpected send result message: {result.Message}");
        Assert.Equal("turn_lazy_1", result.TurnId);

        var requests = await ReadRequestDocumentsAsync(stream);
        Assert.Equal(2, requests.Count);
        Assert.Equal("thread/start", requests[0].RootElement.GetProperty("method").GetString());
        Assert.Equal("turn/start", requests[1].RootElement.GetProperty("method").GetString());
        var parameters = requests[1].RootElement.GetProperty("params");
        Assert.Equal("thread-lazy-1", parameters.GetProperty("threadId").GetString());
        var input = parameters.GetProperty("input");
        Assert.Single(input.EnumerateArray());
        Assert.Equal("首次发送", input[0].GetProperty("text").GetString());
        foreach (var request in requests)
        {
            request.Dispose();
        }
    }

    [Fact]
    public async Task SendAsync_WhenActiveThreadMissing_EmitsInfoForLazilyCreatedThread()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());
        ReflectionTestHelper.SetField(runtime, "activeThreadId", null);
        ReflectionTestHelper.SetField(runtime, "activeTurnId", null);

        var pendingTask = runtime.SendAsync("首次发送", Array.Empty<ConversationMessage>(), CancellationToken.None);
        var threadStartRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, threadStartRequestId, """{"thread":{"id":"thread-lazy-info-1"}}""");
        var turnStartRequestId = await WaitForPendingResponseIdAsync(runtime, threadStartRequestId);
        CompletePendingResponse(runtime, turnStartRequestId, """{"turn":{"id":"turn_lazy_info_1","status":"inProgress"}}""");
        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn_lazy_info_1", true, "completed", "已完成", null, """{"status":"completed"}""");

        var result = await pendingTask;
        Assert.True(result.Success);

        Assert.Contains(events, static streamEvent =>
            streamEvent.Kind == ControlPlaneConversationStreamEventKind.Info
            && string.Equals(streamEvent.ThreadId?.Value, "thread-lazy-info-1", StringComparison.Ordinal)
            && string.Equals(streamEvent.Message, "已按需建立线程：thread-lazy-info-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_WhenStructuredUserInputsProvided_WritesTypedInputItems()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-typed-send-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", null);

        var pendingTask = runtime.SendAsync(
            new AgentUserInput[]
            {
                new MentionUserInput
                {
                    Type = "mention",
                    Name = "worker-1",
                    Path = "app://worker-1",
                },
                new TextUserInput
                {
                    Type = "text",
                    Text = "请继续处理 typed input",
                },
                new LocalImageUserInput
                {
                    Type = "localImage",
                    Path = "D:/images/demo.png",
                },
            },
            Array.Empty<ConversationMessage>(),
            CancellationToken.None);
        var turnStartRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, turnStartRequestId, """{"turn":{"id":"turn_typed_send_1","status":"inProgress"}}""");
        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn_typed_send_1", true, "completed", "typed ok", null, """{"status":"completed"}""");

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.True(
            string.Equals(result.Message, "typed ok", StringComparison.Ordinal)
            || string.Equals(result.Message, "消息已发送，turnId=turn_typed_send_1，请关注流式事件。", StringComparison.Ordinal),
            $"Unexpected send result message: {result.Message}");
        Assert.Equal("turn_typed_send_1", result.TurnId);

        var requests = await ReadRequestDocumentsAsync(stream);
        var request = Assert.Single(requests);
        Assert.Equal("turn/start", request.RootElement.GetProperty("method").GetString());
        var input = request.RootElement.GetProperty("params").GetProperty("input");
        Assert.Equal(3, input.GetArrayLength());
        Assert.Equal("mention", input[0].GetProperty("type").GetString());
        Assert.Equal("worker-1", input[0].GetProperty("name").GetString());
        Assert.Equal("text", input[1].GetProperty("type").GetString());
        Assert.Equal("请继续处理 typed input", input[1].GetProperty("text").GetString());
        Assert.Equal("local_image", input[2].GetProperty("type").GetString());
        Assert.Equal("D:/images/demo.png", input[2].GetProperty("path").GetString());
        request.Dispose();
    }

    [Fact]
    public async Task StartNewThreadAsync_WhenDynamicToolsConfigured_WritesDynamicToolsIntoThreadStartRequest()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());
        ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
        {
            WorkingDirectory = "D:/dynamic-tools/workspace",
            DynamicTools =
            [
                new ControlPlaneDynamicToolSpec
                {
                    Name = "mcp__calendar__find_events",
                    Description = "搜索日历事件。",
                    InputSchema = StructuredValue.FromJsonElement(
                        ReflectionTestHelper.ParseJsonElement(
                            """
                            {
                              "type": "object",
                              "required": ["query"]
                            }
                            """)),
                },
            ],
        });

        var pendingTask = runtime.StartNewThreadAsync(CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"thread":{"id":"thread-dynamic-tools-1","preview":"dynamic tools"}}""");

        var thread = await pendingTask;
        Assert.NotNull(thread);
        Assert.Equal("thread-dynamic-tools-1", thread!.ThreadId.Value);

        var requests = await ReadRequestDocumentsAsync(stream);
        var request = Assert.Single(requests);
        Assert.Equal("thread/start", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("D:/dynamic-tools/workspace", parameters.GetProperty("cwd").GetString());
        var dynamicTools = parameters.GetProperty("dynamicTools");
        var tool = Assert.Single(dynamicTools.EnumerateArray());
        Assert.Equal("mcp__calendar__find_events", tool.GetProperty("name").GetString());
        Assert.Equal("搜索日历事件。", tool.GetProperty("description").GetString());
        Assert.Equal("query", tool.GetProperty("inputSchema").GetProperty("required")[0].GetString());
        request.Dispose();
    }

    [Fact]
    public async Task StartNewThreadAsync_WhenDefaultRequestProvided_UsesTianShuDefaultThreadFlags()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.StartNewThreadAsync(CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"thread":{"id":"thread-default-flags-001","preview":"default flags"}}""");

        var thread = await pendingTask;
        Assert.NotNull(thread);
        Assert.Equal("thread-default-flags-001", thread!.ThreadId.Value);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/start", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.False(parameters.GetProperty("persistExtendedHistory").GetBoolean());
        Assert.False(parameters.TryGetProperty("experimentalRawEvents", out _));
    }

    [Fact]
    public async Task StartNewThreadAsync_WhenTypedRequestProvided_WritesTypedThreadStartPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());
        ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
        {
            WorkingDirectory = "D:/fallback/workspace",
            Model = "fallback-model",
            ModelProvider = "fallback-provider",
            ServiceTier = "flex",
            ApprovalPolicy = "on-request",
            SandboxMode = "workspace-write",
            SessionSource = ControlPlaneSessionSource.Cli,
            DynamicTools =
            [
                new ControlPlaneDynamicToolSpec
                {
                    Name = "fallback_tool",
                    Description = "fallback tool",
                    InputSchema = StructuredValue.FromJsonElement(
                        ReflectionTestHelper.ParseJsonElement("""{"type":"object"}""")),
                },
            ],
        });

        var pendingTask = runtime.StartNewThreadAsync(
            new ControlPlaneStartThreadCommand
            {
                Model = "gpt-5.4",
                ModelProvider = "openai",
                ServiceTier = "fast",
                WorkingDirectory = "D:/typed/thread-start",
                ApprovalPolicy = "never",
                SandboxMode = "danger-full-access",
                Configuration = new Dictionary<string, ControlPlaneStructuredValue>(StringComparer.Ordinal)
                {
                    ["notify"] = ControlPlaneStructuredValue.FromBoolean(true),
                    ["max_turns"] = ControlPlaneStructuredValue.FromNumber("3"),
                },
                ServiceName = "tianshu-vs",
                BaseInstructions = "保持输出精炼。",
                DeveloperInstructions = "所有对外接口必须 typed-first。",
                Personality = "pragmatic",
                Ephemeral = true,
                MockExperimentalField = "mock-value",
                DynamicTools =
                [
                    new ControlPlaneDynamicToolSpec
                    {
                        Name = "thread_start_tool",
                        Description = "typed start tool",
                        InputSchema = ControlPlaneStructuredValue.FromObject(new Dictionary<string, ControlPlaneStructuredValue>(StringComparer.Ordinal)
                        {
                            ["type"] = ControlPlaneStructuredValue.FromString("object"),
                        }),
                    },
                ],
                PersistExtendedHistory = false,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"thread":{"id":"thread-start-typed-001","preview":"typed start"}}""");

        var thread = await pendingTask;
        Assert.NotNull(thread);
        Assert.Equal("thread-start-typed-001", thread!.ThreadId.Value);

        var requests = await ReadRequestDocumentsAsync(stream);
        var request = Assert.Single(requests);
        Assert.Equal("thread/start", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("gpt-5.4", parameters.GetProperty("model").GetString());
        Assert.Equal("openai", parameters.GetProperty("modelProvider").GetString());
        Assert.Equal("fast", parameters.GetProperty("serviceTier").GetString());
        Assert.Equal("D:/typed/thread-start", parameters.GetProperty("cwd").GetString());
        Assert.Equal("never", parameters.GetProperty("approvalPolicy").GetString());
        Assert.Equal("danger-full-access", parameters.GetProperty("sandbox").GetString());
        Assert.Equal("tianshu-vs", parameters.GetProperty("serviceName").GetString());
        Assert.Equal("保持输出精炼。", parameters.GetProperty("baseInstructions").GetString());
        Assert.Equal("所有对外接口必须 typed-first。", parameters.GetProperty("developerInstructions").GetString());
        Assert.Equal("pragmatic", parameters.GetProperty("personality").GetString());
        Assert.Equal("cli", parameters.GetProperty("sessionSource").GetString());
        Assert.True(parameters.GetProperty("ephemeral").GetBoolean());
        Assert.Equal("mock-value", parameters.GetProperty("mockExperimentalField").GetString());
        Assert.False(parameters.GetProperty("persistExtendedHistory").GetBoolean());
        var config = parameters.GetProperty("config");
        Assert.True(config.GetProperty("notify").GetBoolean());
        Assert.Equal(3, config.GetProperty("max_turns").GetInt32());
        var dynamicTools = parameters.GetProperty("dynamicTools");
        var tool = Assert.Single(dynamicTools.EnumerateArray());
        Assert.Equal("thread_start_tool", tool.GetProperty("name").GetString());
        request.Dispose();
    }

    [Fact]
    public async Task StartNewThreadAsync_WhenExperimentalRawEventsRequested_WritesTypedThreadStartExperimentalFlag()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.StartNewThreadAsync(
            new ControlPlaneStartThreadCommand
            {
                ExperimentalRawEvents = true,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"thread":{"id":"thread-raw-events-001","preview":"experimental raw"}}""");

        var thread = await pendingTask;
        Assert.NotNull(thread);

        using var request = await ReadSingleRequestAsync(stream);
        var parameters = request.RootElement.GetProperty("params");
        Assert.True(parameters.GetProperty("experimentalRawEvents").GetBoolean());
    }

    [Fact]
    public async Task StartNewThreadAsync_WhenApprovalPolicyProvided_WritesTypedApprovalScalar()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.StartNewThreadAsync(
            new ControlPlaneStartThreadCommand
            {
                ApprovalPolicy = "on-request",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"thread":{"id":"thread-start-granular-001","preview":"granular start"}}""");

        var thread = await pendingTask;
        Assert.NotNull(thread);

        using var request = await ReadSingleRequestAsync(stream);
        var approvalPolicy = request.RootElement.GetProperty("params").GetProperty("approvalPolicy");
        Assert.Equal(JsonValueKind.String, approvalPolicy.ValueKind);
        Assert.Equal("on-request", approvalPolicy.GetString());
    }

    [Fact]
    public async Task StartNewThreadAsync_WhenResponseCarriesSessionConfiguration_PopulatesTypedSnapshot()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.StartNewThreadAsync(CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-session-config-001",
                "preview": "typed session config"
              },
              "model": "gpt-5.4",
              "modelProvider": "openai",
              "modelRouteSetId": "workbench",
              "serviceTier": "fast",
              "approvalPolicy": "never",
              "sandbox": "danger-full-access",
              "reasoningEffort": "high",
              "historyLogId": "history-log-001",
              "historyEntryCount": 42,
              "rolloutPath": "D:/sessions/thread-session-config-001.jsonl",
              "forkedFromId": "thread-parent-001"
            }
            """);

        var thread = await pendingTask;
        Assert.NotNull(thread);
        Assert.NotNull(thread!.SessionConfiguration);
        Assert.Equal("gpt-5.4", thread.SessionConfiguration!.Model);
        Assert.Equal("openai", thread.SessionConfiguration.ModelProvider);
        Assert.Equal("workbench", thread.SessionConfiguration.ModelRouteSetId);
        Assert.Equal("fast", thread.SessionConfiguration.ServiceTier);
        Assert.Equal("never", thread.SessionConfiguration.ApprovalPolicy);
        Assert.Equal("danger-full-access", thread.SessionConfiguration.SandboxPolicy);
        Assert.Equal("high", thread.SessionConfiguration.ReasoningEffort);
        Assert.Equal("history-log-001", thread.SessionConfiguration.HistoryLogId);
        Assert.Equal(42, thread.SessionConfiguration.HistoryEntryCount);
        Assert.Equal("D:/sessions/thread-session-config-001.jsonl", thread.SessionConfiguration.RolloutPath);
        Assert.Equal("thread-parent-001", thread.SessionConfiguration.ForkedFromThreadId!.Value);
    }

    [Fact]
    public async Task ForkThreadAsync_WhenTypedRequestProvided_WritesTypedThreadForkPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());
        ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
        {
            WorkingDirectory = "D:/fallback/fork",
            Model = "fallback-model",
            ModelProvider = "fallback-provider",
            ServiceTier = "flex",
            ApprovalPolicy = "on-request",
            SandboxMode = "workspace-write",
            SessionSource = ControlPlaneSessionSource.Cli,
        });

        var pendingTask = runtime.ForkThreadAsync(
            new ControlPlaneForkThreadCommand
            {
                ThreadId = new ThreadId("thread-source-001"),
                Path = "D:/sessions/thread-source-001.jsonl",
                Model = "claude-3-7-sonnet",
                ModelProvider = "anthropic",
                ServiceTier = "fast",
                WorkingDirectory = "D:/typed/thread-fork",
                ApprovalPolicy = "never",
                SandboxMode = "read-only",
                Configuration = new Dictionary<string, ControlPlaneStructuredValue>(StringComparer.Ordinal)
                {
                    ["feature_flag"] = ControlPlaneStructuredValue.FromBoolean(true),
                },
                BaseInstructions = "fork base",
                DeveloperInstructions = "fork developer",
                Ephemeral = true,
                PersistExtendedHistory = false,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"thread":{"id":"thread-forked-001","preview":"forked thread"}}""");

        var thread = await pendingTask;
        Assert.NotNull(thread);
        Assert.Equal("thread-forked-001", thread!.ThreadId.Value);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/fork", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-source-001", parameters.GetProperty("threadId").GetString());
        Assert.Equal("D:/sessions/thread-source-001.jsonl", parameters.GetProperty("path").GetString());
        Assert.Equal("claude-3-7-sonnet", parameters.GetProperty("model").GetString());
        Assert.Equal("anthropic", parameters.GetProperty("modelProvider").GetString());
        Assert.Equal("fast", parameters.GetProperty("serviceTier").GetString());
        Assert.Equal("D:/typed/thread-fork", parameters.GetProperty("cwd").GetString());
        Assert.Equal("never", parameters.GetProperty("approvalPolicy").GetString());
        Assert.Equal("read-only", parameters.GetProperty("sandbox").GetString());
        Assert.Equal("fork base", parameters.GetProperty("baseInstructions").GetString());
        Assert.Equal("fork developer", parameters.GetProperty("developerInstructions").GetString());
        Assert.Equal("cli", parameters.GetProperty("sessionSource").GetString());
        Assert.True(parameters.GetProperty("ephemeral").GetBoolean());
        Assert.False(parameters.GetProperty("persistExtendedHistory").GetBoolean());
        Assert.True(parameters.GetProperty("config").GetProperty("feature_flag").GetBoolean());
    }

    [Fact]
    public async Task ForkThreadAsync_WhenApprovalPolicyProvided_UsesCommandOverrideAndOptionFallbacks()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());
        ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
        {
            ServiceTier = AgentServiceTierOverride.FromValue(AgentServiceTier.Fast),
            ApprovalPolicy = AgentApprovalPolicy.OnRequest,
        });

        var pendingTask = runtime.ForkThreadAsync(
            new ControlPlaneForkThreadCommand
            {
                ThreadId = new ThreadId("thread-fork-granular-001"),
                ApprovalPolicy = "never",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"thread":{"id":"thread-fork-granular-001","preview":"granular fork"}}""");

        var thread = await pendingTask;
        Assert.NotNull(thread);

        using var request = await ReadSingleRequestAsync(stream);
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("fast", parameters.GetProperty("serviceTier").GetString());
        var approvalPolicy = parameters.GetProperty("approvalPolicy");
        Assert.Equal("never", approvalPolicy.GetString());
    }

    [Fact]
    public async Task ResumeThreadAsync_WhenResponseCarriesSessionConfiguration_PopulatesTypedSnapshot()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ResumeThreadAsync("thread-resume-session-001", CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-resume-session-001",
                "preview": "resume session"
              },
              "model": "claude-3-7-sonnet",
              "modelProviderId": "anthropic",
              "serviceTier": "fast",
              "approvalPolicy": "on-request",
              "sandboxPolicy": "workspace-write",
              "reasoningEffort": "medium",
              "historyLogId": "history-log-resume-001",
              "historyEntryCount": 7,
              "rolloutPath": "D:/sessions/thread-resume-session-001.jsonl",
              "forkedFromId": "thread-origin-001"
            }
            """);

        var session = await pendingTask;
        Assert.NotNull(session);
        Assert.NotNull(session!.Thread.SessionConfiguration);
        Assert.Equal("claude-3-7-sonnet", session.Thread.SessionConfiguration!.Model);
        Assert.Equal("anthropic", session.Thread.SessionConfiguration.ModelProvider);
        Assert.Equal("fast", session.Thread.SessionConfiguration.ServiceTier);
        Assert.Equal("on-request", session.Thread.SessionConfiguration.ApprovalPolicy);
        Assert.Equal("workspace-write", session.Thread.SessionConfiguration.SandboxPolicy);
        Assert.Equal("medium", session.Thread.SessionConfiguration.ReasoningEffort);
        Assert.Equal("history-log-resume-001", session.Thread.SessionConfiguration.HistoryLogId);
        Assert.Equal(7, session.Thread.SessionConfiguration.HistoryEntryCount);
        Assert.Equal("D:/sessions/thread-resume-session-001.jsonl", session.Thread.SessionConfiguration.RolloutPath);
        Assert.Equal("thread-origin-001", session.Thread.SessionConfiguration.ForkedFromThreadId!.Value);
    }

    [Fact]
    public async Task CreateThreadAsync_WhenResponseOmitsSessionConfiguration_ParsesTopLevelTypedSnapshot()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.StartNewThreadAsync(CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-top-level-session-001",
                "preview": "top-level session only",
                "path": "D:/sessions/top-level-session-001.jsonl"
              },
              "model": "gpt-5.4",
              "modelProvider": "openai",
              "serviceTier": "fast",
              "approvalPolicy": "never",
              "sandbox": {
                "type": "danger-full-access"
              },
              "reasoningEffort": "high",
              "cwd": "D:/snapshot/cwd",
              "ephemeral": true,
              "allowLoginShell": true,
              "serviceName": "snapshot-service",
              "baseInstructions": "snapshot base",
              "developerInstructions": "snapshot developer",
              "dynamicTools": [
                {
                  "fullName": "mcp__demo__lookup"
                }
              ],
              "collaborationMode": {
                "mode": "plan"
              },
              "sessionSource": {
                "subAgent": "review"
              },
              "defaultModeRequestUserInputEnabled": true
            }
            """);

        var thread = await pendingTask;
        Assert.NotNull(thread);
        Assert.NotNull(thread!.SessionConfiguration);
        Assert.Equal("gpt-5.4", thread.SessionConfiguration!.Model);
        Assert.Equal("openai", thread.SessionConfiguration.ModelProvider);
        Assert.Equal("fast", thread.SessionConfiguration.ServiceTier);
        Assert.Equal("never", thread.SessionConfiguration.ApprovalPolicy);
        Assert.Equal("danger-full-access", thread.SessionConfiguration.SandboxPolicy);
        Assert.Equal("high", thread.SessionConfiguration.ReasoningEffort);
        Assert.Equal("D:/sessions/top-level-session-001.jsonl", thread.SessionConfiguration.RolloutPath);
        Assert.Equal("D:/snapshot/cwd", thread.SessionConfiguration.WorkingDirectory);
        Assert.True(thread.IsEphemeral);
        Assert.True(thread.SessionConfiguration.AllowLoginShell ?? false);
        Assert.Equal("snapshot-service", thread.SessionConfiguration.ServiceName);
        Assert.Equal("snapshot base", thread.SessionConfiguration.BaseInstructions);
        Assert.Equal("snapshot developer", thread.SessionConfiguration.DeveloperInstructions);
        Assert.Equal("mcp__demo__lookup", Assert.Single(thread.SessionConfiguration.DynamicTools!).Properties["fullName"].StringValue);
        Assert.Equal("plan", thread.SessionConfiguration.CollaborationMode!.Properties["mode"].StringValue);
        Assert.Equal(
            ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.Review),
            thread.SessionConfiguration.SessionSource);
        Assert.True(thread.SessionConfiguration.DefaultModeRequestUserInputEnabled ?? false);
    }

    [Fact]
    public async Task ResumeThreadAsync_WhenResponseCarriesTypedReplayMessages_PopulatesAuthoritativeMessages()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ResumeThreadAsync("thread-resume-messages-001", CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-resume-messages-001",
                "preview": "resume replay messages",
                "seedHistory": [
                  {
                    "role": "user",
                    "content": "legacy fallback"
                  }
                ],
                "turns": [
                  {
                    "id": "turn-resume-messages-001",
                    "status": "completed",
                    "items": [
                      {
                        "id": "assistant-item-001",
                        "type": "assistant_message",
                        "text": "legacy assistant reply"
                      }
                    ]
                  }
                ]
              },
              "messages": [
                {
                  "role": "user",
                  "content": "[mention:$worker-1](app://worker-1)\n请继续处理",
                  "inputs": [
                    {
                      "type": "mention",
                      "name": "$worker-1",
                      "path": "app://worker-1"
                    },
                    {
                      "type": "text",
                      "text": "请继续处理"
                    }
                  ]
                },
                {
                  "role": "assistant",
                  "content": "我会先补齐 typed replay。"
                }
              ]
            }
            """);

        var session = await pendingTask;
        Assert.NotNull(session);
        Assert.Collection(
            session!.Messages,
            message =>
            {
                Assert.Equal(ControlPlaneConversationRole.User, message.Role);
                Assert.Equal("[mention:$worker-1](app://worker-1)\n请继续处理", message.Content);
                Assert.Collection(
                    message.ContentItems,
                    input =>
                    {
                        var mention = Assert.IsType<ControlPlaneMentionInput>(input);
                        Assert.Equal("$worker-1", mention.Name);
                        Assert.Equal("app://worker-1", mention.Path);
                    },
                    input =>
                    {
                        var text = Assert.IsType<ControlPlaneTextInput>(input);
                        Assert.Equal("请继续处理", text.Text);
                    });
            },
            message =>
            {
                Assert.Equal(ControlPlaneConversationRole.Assistant, message.Role);
                Assert.Equal("我会先补齐 typed replay。", message.Content);
            });
    }

    [Fact]
    public async Task SendAsync_WhenTurnStartResponseArrives_DoesNotEmitTurnStartedBeforeStartedNotification()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-start-semantics-1", activeTurnId: null);
        using var streamHandle = stream;
        using var writerHandle = writer;

        var pendingTask = runtime.SendAsync("测试 accepted 语义", Array.Empty<ConversationMessage>(), CancellationToken.None);
        var turnStartRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, turnStartRequestId, """{"turn":{"id":"turn_start_semantics_1","status":"inProgress"}}""");

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (string.Equals(
                    ReflectionTestHelper.GetField(runtime, "submittedTurnId") as string,
                    "turn_start_semantics_1",
                    StringComparison.Ordinal))
            {
                break;
            }

            await Task.Delay(10);
        }

        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.TurnStarted);
        Assert.Null(ReflectionTestHelper.GetField(runtime, "activeTurnId"));
        Assert.Equal("turn_start_semantics_1", ReflectionTestHelper.GetField(runtime, "submittedTurnId"));
        Assert.True(runtime.HasActiveTurn);

        const string turnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-start-semantics-1","turn":{"id":"turn_start_semantics_1","status":"inProgress"}}}
        """;
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(turnStarted), turnStarted);

        var startedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.TurnStarted));
        Assert.Equal("thread-start-semantics-1", startedEvent.ThreadId?.Value);
        Assert.Equal("turn_start_semantics_1", startedEvent.TurnId?.Value);
        Assert.Null(startedEvent.PayloadKind);
        Assert.Null(startedEvent.Payload);
        Assert.Equal("turn_start_semantics_1", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
        Assert.Null(ReflectionTestHelper.GetField(runtime, "submittedTurnId"));

        const string turnCompleted = """
        {"method":"turn/completed","params":{"threadId":"thread-start-semantics-1","turn":{"id":"turn_start_semantics_1","status":"completed","items":[]}}}
        """;
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(turnCompleted), turnCompleted);

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.Equal("turn_start_semantics_1", result.TurnId);
    }

    [Theory]
    [InlineData("interrupted", "回合已中断。")]
    [InlineData("failed", "回合执行失败。")]
    public async Task SendAsync_WhenTurnCompletedNotificationReportsTerminalFailureStatus_ReturnsFailedResult(string turnStatus, string expectedMessage)
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-terminal-status-1", activeTurnId: null);
        using var streamHandle = stream;
        using var writerHandle = writer;

        var pendingTask = runtime.SendAsync("测试 terminal 状态", Array.Empty<ConversationMessage>(), CancellationToken.None);
        var turnStartRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, turnStartRequestId, """{"turn":{"id":"turn_terminal_status_1","status":"inProgress"}}""");
        SpinWait.SpinUntil(
            () => string.Equals((string?)ReflectionTestHelper.GetField(runtime, "submittedTurnId"), "turn_terminal_status_1", StringComparison.Ordinal),
            TimeSpan.FromSeconds(1));

        var turnCompleted = $@"{{""method"":""turn/completed"",""params"":{{""threadId"":""thread-terminal-status-1"",""turn"":{{""id"":""turn_terminal_status_1"",""status"":""{turnStatus}"",""items"":[]}}}}}}";
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(turnCompleted), turnCompleted);

        var result = await pendingTask;
        Assert.False(result.Success);
        Assert.Equal(expectedMessage, result.Message);
        Assert.Equal("turn_terminal_status_1", result.TurnId);
        Assert.Equal(turnStatus, result.TurnStatus);
    }

    [Fact]
    public async Task SendAsync_WhenTurnCompletedNotificationCarriesError_ReturnsSpecificFailureMessage()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-terminal-error-1", activeTurnId: null);
        using var streamHandle = stream;
        using var writerHandle = writer;

        var pendingTask = runtime.SendAsync("测试 terminal error", Array.Empty<ConversationMessage>(), CancellationToken.None);
        var turnStartRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, turnStartRequestId, """{"turn":{"id":"turn_terminal_error_1","status":"inProgress"}}""");
        SpinWait.SpinUntil(
            () => string.Equals((string?)ReflectionTestHelper.GetField(runtime, "submittedTurnId"), "turn_terminal_error_1", StringComparison.Ordinal),
            TimeSpan.FromSeconds(1));

        const string turnCompleted = """
        {
          "method": "turn/completed",
          "params": {
            "threadId": "thread-terminal-error-1",
            "turn": {
              "id": "turn_terminal_error_1",
              "status": "failed",
              "items": [],
              "error": {
                "message": "工具执行失败：git apply 退出码 1",
                "additionalDetails": "stderr: patch does not apply"
              }
            }
          }
        }
        """;
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(turnCompleted), turnCompleted);

        var result = await pendingTask;
        Assert.False(result.Success);
        Assert.Equal("工具执行失败：git apply 退出码 1", result.Message);
        Assert.Equal("turn_terminal_error_1", result.TurnId);
        Assert.Equal("failed", result.TurnStatus);
    }

    [Fact]
    public async Task ListThreadsAsync_WhenThreadSummaryContainsTypedMetadata_PreservesNameAndStatus()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: null, activeTurnId: null);
        using var streamHandle = stream;
        using var writerHandle = writer;

        var pendingTask = runtime.ListThreadsAsync(limit: 5, archived: false, matchCurrentCwd: false, CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """
        {"data":[{"id":"thread-summary-001","preview":"最近预览","name":"配置 GUI 线程","cwd":"D:/Work/TianShu","path":"D:/Work/TianShu/.tianshu-cli/threads/thread-summary-001","modelProvider":"openai-compatible","source":"appServer","cliVersion":"0.1.0","agentNickname":"Halley","agentRole":"architect","createdAt":1774160000,"updatedAt":1774160300,"ephemeral":true,"status":{"type":"idle","activeFlags":["ready"]},"gitInfo":{"sha":"abc123","branch":"main","originUrl":"https://example.com/repo.git"}}],"nextCursor":null}
        """);

        var threads = await pendingTask;
        var thread = Assert.Single(threads);
        Assert.Equal("thread-summary-001", thread.ThreadId.Value);
        Assert.Equal("配置 GUI 线程", thread.Name);
        Assert.Equal("最近预览", thread.Preview);
        Assert.Equal("openai-compatible", thread.ModelProvider);
        Assert.Equal("appServer", thread.Source);
        Assert.Equal("Halley", thread.AgentNickname);
        Assert.Equal("architect", thread.AgentRole);
        Assert.True(thread.IsEphemeral);
        Assert.NotNull(thread.CreatedAt);
        Assert.NotNull(thread.Status);
        Assert.Equal("idle", thread.Status);
        Assert.Equal("ready", Assert.Single(thread.ActiveFlags));
        Assert.Equal("abc123", thread.GitSha);
        Assert.Equal("main", thread.GitBranch);
    }

    [Fact]
    public async Task ListThreadsAsync_WithTypedRequest_WritesExpectedMethodAndParsesNextCursor()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: null, activeTurnId: null);
        using var streamHandle = stream;
        using var writerHandle = writer;

        var pendingTask = runtime.ListThreadsAsync(
            new ControlPlaneThreadListQuery
            {
                Limit = 2,
                Cursor = "dGhyZWFkX2E=",
                Archived = true,
                WorkingDirectory = "D:/Work/TianShu",
                SortKey = "updated_at",
                ModelProviders = ["anthropic", "openai", "anthropic"],
                SourceKinds =
                [
                    ControlPlaneThreadSourceKind.SubAgentReview,
                    ControlPlaneThreadSourceKind.AppServer,
                    ControlPlaneThreadSourceKind.SubAgentReview,
                ],
                SearchTerm = "配置",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """
        {"data":[{"id":"thread-summary-002","preview":"配置预览","name":"配置线程","cwd":"D:/Work/TianShu","updatedAt":1774160400}],"nextCursor":"dGhyZWFkX3N1bW1hcnlfMDAy"}
        """);

        var result = await pendingTask;
        var thread = Assert.Single(result.Threads);
        Assert.Equal("thread-summary-002", thread.ThreadId.Value);
        Assert.Equal("配置线程", thread.Name);
        Assert.Equal("dGhyZWFkX3N1bW1hcnlfMDAy", result.NextCursor);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/list", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal(2, parameters.GetProperty("limit").GetInt32());
        Assert.Equal("dGhyZWFkX2E=", parameters.GetProperty("cursor").GetString());
        Assert.True(parameters.GetProperty("archived").GetBoolean());
        Assert.Equal("D:/Work/TianShu", parameters.GetProperty("cwd").GetString());
        Assert.Equal("updated_at", parameters.GetProperty("sortKey").GetString());
        Assert.Equal(
            new string?[] { "anthropic", "openai" },
            parameters.GetProperty("modelProviders").EnumerateArray().Select(static item => item.GetString()).ToArray());
        Assert.Equal(
            new string?[] { "subAgentReview", "appServer" },
            parameters.GetProperty("sourceKinds").EnumerateArray().Select(static item => item.GetString()).ToArray());
        Assert.Equal("配置", parameters.GetProperty("searchTerm").GetString());
    }

    [Fact]
    public async Task GetSessionOverviewAsync_WhenThreadReadContainsSessionState_ReturnsFormalProjection()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: null, activeTurnId: null);
        using var streamHandle = stream;
        using var writerHandle = writer;

        var pendingTask = runtime.GetSessionOverviewAsync(
            new GetSessionOverview(new SessionId("thread-session-overview-001")),
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """
        {
          "thread": {
            "id": "thread-session-overview-001",
            "preview": "overview",
            "sessionState": {
              "sessionId": "thread-session-overview-001",
              "title": "架构收口",
              "collaborationSpaceId": "space-session-001",
              "collaborationSpaceKey": "tianshu-runtime",
              "collaborationSpaceDisplayName": "TianShu Runtime",
              "sessionMode": "planning",
              "isClosed": false,
              "activeThreadId": "thread-session-overview-001",
              "hasActiveTurn": true
            },
            "turns": []
          }
        }
        """);

        var overview = await pendingTask;
        Assert.NotNull(overview);
        Assert.Equal("thread-session-overview-001", overview!.SessionId.Value);
        Assert.Equal("架构收口", overview.Title);
        Assert.Equal("space-session-001", overview.CollaborationSpace.Id.Value);
        Assert.Equal("tianshu-runtime", overview.CollaborationSpace.Key);
        Assert.Equal("TianShu Runtime", overview.CollaborationSpace.DisplayName);
        Assert.Equal(SessionMode.Planning, overview.Mode);
        Assert.Equal("thread-session-overview-001", overview.ActiveThreadId?.Value);
        Assert.True(overview.HasActiveTurn);
        Assert.False(overview.IsClosed);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/read", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-session-overview-001", parameters.GetProperty("threadId").GetString());
        Assert.False(parameters.GetProperty("includeTurns").GetBoolean());
    }

    [Fact]
    public async Task ListSessionsAsync_WhenIncludeClosedTrue_QueriesActiveAndArchivedPages()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: null, activeTurnId: null);
        using var streamHandle = stream;
        using var writerHandle = writer;

        var pendingTask = runtime.ListSessionsAsync(
            new ListSessions(new CollaborationSpaceId("space-session-002"), IncludeClosed: true),
            CancellationToken.None);
        var activeRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, activeRequestId, """
        {
          "data": [
            {
              "id": "thread-session-active-001",
              "preview": "active",
              "updatedAt": 1774160400,
              "turns": [],
              "sessionState": {
                "sessionId": "thread-session-active-001",
                "title": "活跃会话",
                "collaborationSpaceId": "space-session-002",
                "collaborationSpaceKey": "tianshu-runtime",
                "collaborationSpaceDisplayName": "TianShu Runtime",
                "sessionMode": "interactive",
                "isClosed": false,
                "activeThreadId": "thread-session-active-001",
                "hasActiveTurn": false
              }
            }
          ],
          "nextCursor": null
        }
        """);

        var archivedRequestId = await WaitForPendingResponseIdAsync(runtime, activeRequestId);
        CompletePendingResponse(runtime, archivedRequestId, """
        {
          "data": [
            {
              "id": "thread-session-closed-001",
              "preview": "closed",
              "updatedAt": 1774160300,
              "turns": [],
              "sessionState": {
                "sessionId": "thread-session-closed-001",
                "title": "已关闭会话",
                "collaborationSpaceId": "space-session-002",
                "collaborationSpaceKey": "tianshu-runtime",
                "collaborationSpaceDisplayName": "TianShu Runtime",
                "sessionMode": "review",
                "isClosed": true,
                "activeThreadId": null,
                "hasActiveTurn": false
              }
            },
            {
              "id": "thread-session-other-space-001",
              "preview": "other",
              "updatedAt": 1774160200,
              "turns": [],
              "sessionState": {
                "sessionId": "thread-session-other-space-001",
                "title": "其他空间",
                "collaborationSpaceId": "space-other",
                "collaborationSpaceKey": "other-space",
                "collaborationSpaceDisplayName": "Other Space",
                "sessionMode": "automation",
                "isClosed": true,
                "activeThreadId": null,
                "hasActiveTurn": false
              }
            }
          ],
          "nextCursor": null
        }
        """);

        var sessions = await pendingTask;
        Assert.Equal(2, sessions.Count);

        var active = Assert.Single(sessions.Where(static item => !item.IsClosed));
        Assert.Equal("thread-session-active-001", active.SessionId.Value);
        Assert.Equal(SessionMode.Interactive, active.Mode);
        Assert.Equal("space-session-002", active.CollaborationSpace.Id.Value);

        var closed = Assert.Single(sessions.Where(static item => item.IsClosed));
        Assert.Equal("thread-session-closed-001", closed.SessionId.Value);
        Assert.Equal(SessionMode.Review, closed.Mode);
        Assert.Null(closed.ActiveThreadId);

        var requests = await ReadRequestDocumentsAsync(stream);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, static document => Assert.Equal("thread/list", document.RootElement.GetProperty("method").GetString()));
        Assert.False(requests[0].RootElement.GetProperty("params").GetProperty("archived").GetBoolean());
        Assert.True(requests[1].RootElement.GetProperty("params").GetProperty("archived").GetBoolean());
        Assert.Equal(200, requests[0].RootElement.GetProperty("params").GetProperty("limit").GetInt32());
        Assert.Equal("updated_at", requests[0].RootElement.GetProperty("params").GetProperty("sortKey").GetString());

        foreach (var request in requests)
        {
            request.Dispose();
        }
    }

    [Fact]
    public async Task SendFollowUpAsync_WhenModeIsQueue_EmitsTypedProviderTransportFieldsOnDeferredTurnStart()
    {
        var runtime = new TianShuExecutionRuntime();
        ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
        {
            ProviderApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY",
            ProviderBaseUrl = "https://api.anthropic.com/v1",
            ProviderWireApi = "messages",
            ProviderRequestMaxRetries = 0,
            ProviderStreamMaxRetries = 2,
            ProviderStreamIdleTimeoutMs = 30000,
            ProviderWebsocketConnectTimeoutMs = 15000,
            ProviderSupportsWebsockets = true,
        });

        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-queue-provider-1", activeTurnId: "turn-active-provider-1");
        using var streamHandle = stream;
        using var writerHandle = writer;

        var pendingTask = runtime.SendFollowUpAsync("继续处理", FollowUpMode.Queue, CancellationToken.None);
        await Task.Delay(100);
        Assert.Equal(0, stream.Length);

        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn-active-provider-1", true, "completed", "上一轮已完成", null, """{"status":"completed"}""");
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"turn":{"id":"turn_queue_provider_2","status":"inProgress"}}""");

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.Equal("turn_queue_provider_2", result.TurnId);

        var requests = await ReadRequestDocumentsAsync(stream);
        Assert.Single(requests);
        Assert.Equal("turn/start", requests[0].RootElement.GetProperty("method").GetString());
        var parameters = requests[0].RootElement.GetProperty("params");
        Assert.Equal("ANTHROPIC_API_KEY", parameters.GetProperty("providerApiKeyEnvironmentVariable").GetString());
        Assert.Equal("https://api.anthropic.com/v1", parameters.GetProperty("providerBaseUrl").GetString());
        Assert.Equal("messages", parameters.GetProperty("providerWireApi").GetString());
        Assert.Equal(0, parameters.GetProperty("providerRequestMaxRetries").GetInt32());
        Assert.Equal(2, parameters.GetProperty("providerStreamMaxRetries").GetInt32());
        Assert.Equal(30000, parameters.GetProperty("providerStreamIdleTimeoutMs").GetInt64());
        Assert.Equal(15000, parameters.GetProperty("providerWebsocketConnectTimeoutMs").GetInt64());
        Assert.True(parameters.GetProperty("providerSupportsWebsockets").GetBoolean());
        requests[0].Dispose();
    }

    [Fact]
    public async Task SendAsync_WhenOptionsContainProviderTransportOverrides_EmitsTypedProviderTransportFieldsOnTurnStart()
    {
        var runtime = new TianShuExecutionRuntime();
        ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
        {
            ProviderApiKeyEnvironmentVariable = "GAP015_TEST_API_KEY",
            ProviderBaseUrl = "http://127.0.0.1:63512/v1",
            ProviderWireApi = "responses",
            ProviderRequestMaxRetries = 0,
            ProviderStreamMaxRetries = 0,
            ProviderStreamIdleTimeoutMs = 30000,
            ProviderWebsocketConnectTimeoutMs = 12000,
            ProviderSupportsWebsockets = true,
        });

        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-transport-overrides-1", activeTurnId: null);
        using var streamHandle = stream;
        using var writerHandle = writer;

        var pendingTask = runtime.SendAsync("验证 transport overrides", Array.Empty<ConversationMessage>(), CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"turn":{"id":"turn_transport_overrides_1","status":"inProgress"}}""");
        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn_transport_overrides_1", true, "completed", "已完成", null, """{"status":"completed"}""");

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.Equal("turn_transport_overrides_1", result.TurnId);

        var requests = await ReadRequestDocumentsAsync(stream);
        Assert.Single(requests);
        Assert.Equal("turn/start", requests[0].RootElement.GetProperty("method").GetString());
        var parameters = requests[0].RootElement.GetProperty("params");
        Assert.Equal("GAP015_TEST_API_KEY", parameters.GetProperty("providerApiKeyEnvironmentVariable").GetString());
        Assert.Equal("http://127.0.0.1:63512/v1", parameters.GetProperty("providerBaseUrl").GetString());
        Assert.Equal("responses", parameters.GetProperty("providerWireApi").GetString());
        Assert.Equal(0, parameters.GetProperty("providerRequestMaxRetries").GetInt32());
        Assert.Equal(0, parameters.GetProperty("providerStreamMaxRetries").GetInt32());
        Assert.Equal(30000, parameters.GetProperty("providerStreamIdleTimeoutMs").GetInt64());
        Assert.Equal(12000, parameters.GetProperty("providerWebsocketConnectTimeoutMs").GetInt64());
        Assert.True(parameters.GetProperty("providerSupportsWebsockets").GetBoolean());
        requests[0].Dispose();
    }

    [Fact]
    public void BuildTurnStartParams_WhenOptionsContainTypedOverrides_WritesSupportedTurnFields()
    {
        var runtime = new TianShuExecutionRuntime();
        ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
        {
            Model = "gpt-5",
            WorkingDirectory = "D:/typed-turn",
            ApprovalPolicy = AgentApprovalPolicy.Parse("never"),
            SandboxMode = "danger-full-access",
            ServiceTier = AgentServiceTierOverride.FromValue("flex"),
            ProviderBaseUrl = "https://example.invalid/v1",
            ProviderApiKeyEnvironmentVariable = "OPENAI_API_KEY",
            ProviderWireApi = "responses",
            ProviderRequestMaxRetries = 1,
            ProviderStreamMaxRetries = 2,
            ProviderStreamIdleTimeoutMs = 45000,
            ProviderWebsocketConnectTimeoutMs = 15000,
            ProviderSupportsWebsockets = true,
            ModelReasoningSummary = "detailed",
            ModelVerbosity = "high",
            CollaborationMode = "plan",
        });

        var payload = ReflectionTestHelper.InvokeMethod(
            runtime,
            "BuildTurnStartParams",
            "thread-typed-turn-1",
            "hello world",
            Array.Empty<ConversationMessage>());
        Assert.NotNull(payload);

        var json = JsonSerializer.SerializeToElement(payload);
        Assert.Equal("gpt-5", json.GetProperty("model").GetString());
        Assert.Equal("flex", json.GetProperty("serviceTier").GetString());
        Assert.Equal("D:/typed-turn", json.GetProperty("cwd").GetString());
        Assert.Equal("danger-full-access", json.GetProperty("sandboxPolicy").GetString());
        Assert.Equal("https://example.invalid/v1", json.GetProperty("providerBaseUrl").GetString());
        Assert.Equal("OPENAI_API_KEY", json.GetProperty("providerApiKeyEnvironmentVariable").GetString());
        Assert.Equal("responses", json.GetProperty("providerWireApi").GetString());
        Assert.Equal(1, json.GetProperty("providerRequestMaxRetries").GetInt32());
        Assert.Equal(2, json.GetProperty("providerStreamMaxRetries").GetInt32());
        Assert.Equal(45000, json.GetProperty("providerStreamIdleTimeoutMs").GetInt64());
        Assert.Equal(15000, json.GetProperty("providerWebsocketConnectTimeoutMs").GetInt64());
        Assert.True(json.GetProperty("providerSupportsWebsockets").GetBoolean());
        Assert.Equal("detailed", json.GetProperty("summary").GetString());
        Assert.Equal("high", json.GetProperty("verbosity").GetString());
        Assert.Equal("plan", json.GetProperty("collaborationMode").GetProperty("mode").GetString());
    }

    [Fact]
    public void BuildTurnStartParams_WhenMemoryHabitVerbosityConfigured_UsesIdentityMemoryDecision()
    {
        var previous = Environment.GetEnvironmentVariable("TIANSHU_MEMORY_PREFERRED_VERBOSITY");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_MEMORY_PREFERRED_VERBOSITY", "medium");
            var runtime = new TianShuExecutionRuntime();
            ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
            {
                Model = "gpt-5",
            });

            var payload = ReflectionTestHelper.InvokeMethod(
                runtime,
                "BuildTurnStartParams",
                "thread-memory-habit-1",
                "hello world",
                Array.Empty<ConversationMessage>());
            Assert.NotNull(payload);

            var json = JsonSerializer.SerializeToElement(payload);
            Assert.Equal("medium", json.GetProperty("verbosity").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_MEMORY_PREFERRED_VERBOSITY", previous);
        }
    }

    [Fact]
    public void BuildTurnStartParams_WhenOptionsContainOutputSchema_WritesOutputSchema()
    {
        var runtime = new TianShuExecutionRuntime();
        using var schemaDocument = JsonDocument.Parse("""{"type":"object","properties":{"answer":{"type":"string"}}}""");
        ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
        {
            OutputSchema = StructuredValue.FromJsonElement(schemaDocument.RootElement),
        });

        var payload = ReflectionTestHelper.InvokeMethod(
            runtime,
            "BuildTurnStartParams",
            "thread-output-schema-1",
            "hello world",
            Array.Empty<ConversationMessage>());
        Assert.NotNull(payload);

        var json = JsonSerializer.SerializeToElement(payload);
        Assert.Equal("object", json.GetProperty("outputSchema").GetProperty("type").GetString());
        Assert.Equal("string", json.GetProperty("outputSchema").GetProperty("properties").GetProperty("answer").GetProperty("type").GetString());
    }

    [Fact]
    public async Task SendFollowUpAsync_WhenModeIsSteer_WritesTurnSteerRequest()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-steer-1", activeTurnId: "turn-active-2");
        using var streamHandle = stream;
        using var writerHandle = writer;

        var pendingTask = runtime.SendFollowUpAsync("请转向新的方向", FollowUpMode.Steer, CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"turnId":"turn_steer_3","turn":{"status":"inProgress"}}""");

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.Equal("turn_steer_3", result.TurnId);

        var requests = await ReadRequestDocumentsAsync(stream);
        Assert.Single(requests);
        Assert.Equal("turn/steer", requests[0].RootElement.GetProperty("method").GetString());
        var parameters = requests[0].RootElement.GetProperty("params");
        Assert.Equal("thread-steer-1", parameters.GetProperty("threadId").GetString());
        Assert.Equal("turn-active-2", parameters.GetProperty("expectedTurnId").GetString());
        Assert.Equal("请转向新的方向", parameters.GetProperty("text").GetString());
        requests[0].Dispose();
    }

    [Fact]
    public async Task SendFollowUpAsync_WhenModeIsSteerWithoutActiveTurn_FailsWithoutStartingTurn()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: null, activeTurnId: null);
        using var streamHandle = stream;
        using var writerHandle = writer;

        var result = await runtime.SendFollowUpAsync("请转向新的方向", FollowUpMode.Steer, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("当前没有可引导的活动回合。", result.Message);
        Assert.Equal(FollowUpMode.Steer, result.RequestedMode);
        Assert.Equal(FollowUpMode.Steer, result.EffectiveMode);

        var requests = await ReadRequestDocumentsAsync(stream);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task SendFollowUpAsync_WhenSteerCommittedMessageArrives_EmitsCorrelationLifecycleAndCommittedCorrelationId()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-steer-correlation-1", activeTurnId: "turn-active-correlation-1");
        using var streamHandle = stream;
        using var writerHandle = writer;

        const string correlationId = "corr-steer-001";
        var pendingTask = runtime.SendFollowUpAsync("请转向新的方向", FollowUpMode.Steer, CancellationToken.None, correlationId);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"turnId":"turn_steer_correlation_2","turn":{"status":"inProgress"}}""");

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(FollowUpMode.Steer, result.RequestedMode);
        Assert.Equal(FollowUpMode.Steer, result.EffectiveMode);

        var awaitingCommit = Assert.Single(events.Where(static item =>
            item.Kind == ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated
            && string.Equals(item.Status, "awaiting_commit", StringComparison.Ordinal)));
        Assert.Equal("thread-steer-correlation-1", awaitingCommit.ThreadId?.Value);
        Assert.Equal("turn_steer_correlation_2", awaitingCommit.TurnId?.Value);
        var awaitingCommitState = GetPendingInputStatePayload(awaitingCommit);
        var awaitingCommitEntry = Assert.Single(ReadStructuredItems(awaitingCommitState, "pendingSteers"));
        Assert.Equal(correlationId, ReadStructuredString(awaitingCommitEntry, "correlationId"));
        Assert.Equal("turn-active-correlation-1", ReadStructuredString(awaitingCommitEntry, "expectedTurnId"));
        Assert.Equal("turn_steer_correlation_2", ReadStructuredString(awaitingCommitEntry, "turnId"));
        Assert.Equal("awaiting_commit", ReadStructuredString(awaitingCommitEntry, "lifecycleState"));
        Assert.Equal("PendingSteer", ReadStructuredString(awaitingCommitEntry, "pendingBucket"));
        Assert.Null(ReadStructuredValue(awaitingCommitEntry, "compareKey"));

        const string committedNotification = """
        {"method":"item/completed","params":{"threadId":"thread-steer-correlation-1","turnId":"turn_steer_correlation_2","item":{"id":"user-msg-steer-1","type":"user_message","status":"completed","content":[{"type":"input_text","text":"请转向新的方向"}]}}}
        """;
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(committedNotification), committedNotification);

        var committedLifecycle = Assert.Single(events.Where(static item =>
            item.Kind == ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated
            && string.Equals(item.Status, "committed", StringComparison.Ordinal)));
        Assert.Equal("thread-steer-correlation-1", committedLifecycle.ThreadId?.Value);
        Assert.Equal("turn_steer_correlation_2", committedLifecycle.TurnId?.Value);
        var committedLifecycleState = GetPendingInputStatePayload(committedLifecycle);
        Assert.Empty(ReadStructuredItems(committedLifecycleState, "entries"));
        Assert.Empty(ReadStructuredItems(committedLifecycleState, "queuedUserMessages"));
        Assert.Empty(ReadStructuredItems(committedLifecycleState, "pendingSteers"));

        var committedUserMessage = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.UserMessageCommitted));
        var committedUserMessagePayload = GetCommittedUserMessagePayload(committedUserMessage);
        Assert.Equal(correlationId, ReadStructuredString(committedUserMessagePayload, "correlationId"));
    }

    [Fact]
    public async Task SendFollowUpAsync_WhenQueueCommittedMessageArrives_EmitsCorrelationLifecycleAndCommittedCorrelationId()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-queue-correlation-1", activeTurnId: "turn-active-queue-correlation-1");
        using var streamHandle = stream;
        using var writerHandle = writer;

        const string correlationId = "corr-queue-001";
        var pendingTask = runtime.SendFollowUpAsync("继续处理", FollowUpMode.Queue, CancellationToken.None, correlationId);

        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn-active-queue-correlation-1", true, "completed", "上一轮已完成", null, """{"status":"completed"}""");
        var startRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, startRequestId, """{"turn":{"id":"turn_queue_correlation_2","status":"inProgress"}}""");

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(FollowUpMode.Queue, result.RequestedMode);
        Assert.Equal(FollowUpMode.Queue, result.EffectiveMode);

        var awaitingCommit = Assert.Single(events.Where(static item =>
            item.Kind == ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated
            && string.Equals(item.Status, "awaiting_commit", StringComparison.Ordinal)));
        Assert.Equal("thread-queue-correlation-1", awaitingCommit.ThreadId?.Value);
        Assert.Equal("turn_queue_correlation_2", awaitingCommit.TurnId?.Value);
        var awaitingCommitState = GetPendingInputStatePayload(awaitingCommit);
        var awaitingCommitEntry = Assert.Single(ReadStructuredItems(awaitingCommitState, "queuedUserMessages"));
        Assert.Equal(correlationId, ReadStructuredString(awaitingCommitEntry, "correlationId"));
        Assert.Equal("turn-active-queue-correlation-1", ReadStructuredString(awaitingCommitEntry, "expectedTurnId"));
        Assert.Equal("turn_queue_correlation_2", ReadStructuredString(awaitingCommitEntry, "turnId"));
        Assert.Equal("Queue", ReadStructuredString(awaitingCommitEntry, "requestedMode"));
        Assert.Equal("Queue", ReadStructuredString(awaitingCommitEntry, "effectiveMode"));
        Assert.Equal("awaiting_commit", ReadStructuredString(awaitingCommitEntry, "lifecycleState"));

        const string committedNotification = """
        {"method":"item/completed","params":{"threadId":"thread-queue-correlation-1","turnId":"turn_queue_correlation_2","item":{"id":"user-msg-queue-1","type":"user_message","status":"completed","content":[{"type":"input_text","text":"继续处理"}]}}}
        """;
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(committedNotification), committedNotification);

        var committedLifecycle = Assert.Single(events.Where(static item =>
            item.Kind == ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated
            && string.Equals(item.Status, "committed", StringComparison.Ordinal)));
        Assert.Equal("thread-queue-correlation-1", committedLifecycle.ThreadId?.Value);
        Assert.Equal("turn_queue_correlation_2", committedLifecycle.TurnId?.Value);
        var committedLifecycleState = GetPendingInputStatePayload(committedLifecycle);
        Assert.Empty(ReadStructuredItems(committedLifecycleState, "entries"));
        Assert.Empty(ReadStructuredItems(committedLifecycleState, "queuedUserMessages"));
        Assert.Empty(ReadStructuredItems(committedLifecycleState, "pendingSteers"));

        var committedUserMessage = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.UserMessageCommitted));
        var committedUserMessagePayload = GetCommittedUserMessagePayload(committedUserMessage);
        Assert.Equal(correlationId, ReadStructuredString(committedUserMessagePayload, "correlationId"));
    }

    [Fact]
    public void TryConsumePendingFollowUpCorrelation_WhenDifferentThreadHasEarlierPendingCommit_ConsumesMatchingThreadQueue()
    {
        var runtime = new TianShuExecutionRuntime();

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "EnqueuePendingFollowUpCommit",
            "thread-queue-a",
            "corr-thread-a",
            new AgentUserInput[] { new TextUserInput { Type = "text", Text = "线程A待提交" } },
            "线程A待提交",
            "turn-a-1",
            "turn-a-2",
            FollowUpMode.Queue,
            FollowUpMode.Queue,
            0);
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "EnqueuePendingFollowUpCommit",
            "thread-queue-b",
            "corr-thread-b",
            new AgentUserInput[] { new TextUserInput { Type = "text", Text = "线程B待提交" } },
            "线程B待提交",
            "turn-b-1",
            "turn-b-2",
            FollowUpMode.Queue,
            FollowUpMode.Queue,
            0);

        var correlationB = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryConsumePendingFollowUpCorrelation",
            new AgentUserInput[] { new TextUserInput { Type = "text", Text = "线程B待提交" } },
            "turn-b-2",
            "thread-queue-b");
        Assert.Equal("corr-thread-b", Assert.IsType<string>(correlationB));

        var correlationA = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryConsumePendingFollowUpCorrelation",
            new AgentUserInput[] { new TextUserInput { Type = "text", Text = "线程A待提交" } },
            "turn-a-2",
            "thread-queue-a");
        Assert.Equal("corr-thread-a", Assert.IsType<string>(correlationA));
    }

    [Fact]
    public void TryConsumePendingFollowUpCorrelation_WhenCommittedInputsMissing_DoesNotFallbackToCompareMessage()
    {
        var runtime = new TianShuExecutionRuntime();

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "EnqueuePendingFollowUpCommit",
            "thread-queue-missing-inputs",
            "corr-missing-inputs",
            "只剩 compareKey 的排队消息",
            "turn-missing-inputs-1",
            "turn-missing-inputs-2",
            FollowUpMode.Queue,
            FollowUpMode.Queue);

        var correlation = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryConsumePendingFollowUpCorrelation",
            Array.Empty<AgentUserInput>(),
            "turn-missing-inputs-2",
            "thread-queue-missing-inputs");

        Assert.Null(correlation);
    }

    [Fact]
    public async Task DeleteThreadAsync_WhenThreadHasPendingFollowUpCommit_RemovesThreadScopedCommitQueue()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-delete-commit-1", activeTurnId: null);
        using var streamHandle = stream;
        using var writerHandle = writer;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "EnqueuePendingFollowUpCommit",
            "thread-delete-commit-1",
            "corr-delete-commit-1",
            "待删除线程的待提交消息",
            "turn-delete-commit-0",
            "turn-delete-commit-1",
            FollowUpMode.Queue,
            FollowUpMode.Queue);

        var pendingTask = runtime.DeleteThreadAsync("thread-delete-commit-1", CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, "{}");

        var deleted = await pendingTask;
        Assert.True(deleted);

        var correlation = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryConsumePendingFollowUpCorrelation",
            new AgentUserInput[] { new TextUserInput { Type = "text", Text = "待删除线程的待提交消息" } },
            "turn-delete-commit-1",
            "thread-delete-commit-1");
        Assert.Null(correlation);
    }

    [Fact]
    public async Task SendFollowUpAsync_WhenMultipleQueueFollowUpsAreAccepted_DispatchesOneTurnPerCompletion()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-queue-owner-1", activeTurnId: "turn-active-queue-owner-1");
        using var streamHandle = stream;
        using var writerHandle = writer;

        var firstTask = runtime.SendFollowUpAsync("第一条排队", FollowUpMode.Queue, CancellationToken.None, "corr-queue-owner-1");
        var secondTask = runtime.SendFollowUpAsync("第二条排队", FollowUpMode.Queue, CancellationToken.None, "corr-queue-owner-2");

        await Task.Delay(50);
        var queuedLifecycle = events
            .Where(static item =>
                item.Kind == ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated
                && string.Equals(item.Status, "queued", StringComparison.Ordinal))
            .ToArray();
        Assert.Collection(
            queuedLifecycle,
            payload =>
            {
                var state = GetPendingInputStatePayload(payload);
                Assert.Equal(
                    "corr-queue-owner-1",
                    ReadStructuredString(Assert.Single(ReadStructuredItems(state, "queuedUserMessages")), "correlationId"));
            },
            payload =>
            {
                var state = GetPendingInputStatePayload(payload);
                Assert.Collection(
                    ReadStructuredItems(state, "queuedUserMessages"),
                    entry => Assert.Equal("corr-queue-owner-1", ReadStructuredString(entry, "correlationId")),
                    entry => Assert.Equal("corr-queue-owner-2", ReadStructuredString(entry, "correlationId")));
            });

        var firstQueuedEvent = queuedLifecycle[0];
        var firstQueuedState = GetPendingInputStatePayload(firstQueuedEvent);
        Assert.False(ReadStructuredBoolean(firstQueuedState, "interruptRequestPending"));
        Assert.False(ReadStructuredBoolean(firstQueuedState, "submitPendingSteersAfterInterrupt"));
        Assert.Empty(ReadStructuredItems(firstQueuedState, "entries"));
        Assert.Collection(
            ReadStructuredItems(firstQueuedState, "queuedUserMessages"),
            entry =>
            {
                Assert.Equal("corr-queue-owner-1", ReadStructuredString(entry, "correlationId"));
                Assert.Equal("Queue", ReadStructuredString(entry, "requestedMode"));
                Assert.Equal("queued", ReadStructuredString(entry, "lifecycleState"));
                Assert.Equal("QueuedUserMessage", ReadStructuredString(entry, "pendingBucket"));
            });

        var secondQueuedEvent = queuedLifecycle.Last();
        var secondQueuedState = GetPendingInputStatePayload(secondQueuedEvent);
        Assert.False(ReadStructuredBoolean(secondQueuedState, "interruptRequestPending"));
        Assert.False(ReadStructuredBoolean(secondQueuedState, "submitPendingSteersAfterInterrupt"));
        Assert.Empty(ReadStructuredItems(secondQueuedState, "entries"));
        Assert.Collection(
            ReadStructuredItems(secondQueuedState, "queuedUserMessages"),
            entry =>
            {
                Assert.Equal("corr-queue-owner-1", ReadStructuredString(entry, "correlationId"));
                Assert.Equal("QueuedUserMessage", ReadStructuredString(entry, "pendingBucket"));
            },
            entry =>
            {
                Assert.Equal("corr-queue-owner-2", ReadStructuredString(entry, "correlationId"));
                Assert.Equal("QueuedUserMessage", ReadStructuredString(entry, "pendingBucket"));
            });

        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn-active-queue-owner-1", true, "completed", "上一轮已完成", null, """{"status":"completed"}""");
        var firstStartRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, firstStartRequestId, """{"turn":{"id":"turn_queue_owner_2","status":"inProgress"}}""");

        var firstResult = await firstTask;
        Assert.True(firstResult.Success);
        Assert.Equal("turn_queue_owner_2", firstResult.TurnId);
        stream.SetLength(0);
        stream.Position = 0;
        await Task.Delay(50);
        var queuedBeforeCompletion = await ReadRequestDocumentsAsync(stream);
        Assert.Empty(queuedBeforeCompletion);

        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn_queue_owner_2", true, "completed", "第一条已完成", null, """{"status":"completed"}""");
        var secondStartRequestId = await WaitForPendingResponseIdAsync(runtime, firstStartRequestId);
        CompletePendingResponse(runtime, secondStartRequestId, """{"turn":{"id":"turn_queue_owner_3","status":"inProgress"}}""");

        var secondResult = await secondTask;
        Assert.True(secondResult.Success);
        Assert.Equal("turn_queue_owner_3", secondResult.TurnId);
    }

    [Fact]
    public async Task SendFollowUpAsync_WhenModeIsInterrupt_WaitsForInterruptCompletionBeforeStartingNextTurn()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-interrupt-1", activeTurnId: "turn-active-3");
        using var streamHandle = stream;
        using var writerHandle = writer;

        var pendingTask = runtime.SendFollowUpAsync("重新开始这轮", FollowUpMode.Interrupt, CancellationToken.None);
        var interruptRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, interruptRequestId, "{}");

        await Task.Delay(100);
        var interruptOnlyRequests = await ReadRequestDocumentsAsync(stream);
        Assert.Single(interruptOnlyRequests);
        Assert.Equal("turn/interrupt", interruptOnlyRequests[0].RootElement.GetProperty("method").GetString());
        interruptOnlyRequests[0].Dispose();
        stream.SetLength(0);
        stream.Position = 0;

        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn-active-3", true, "interrupted", null, null, """{"status":"interrupted"}""");
        var startRequestId = await WaitForPendingResponseIdAsync(runtime, interruptRequestId);
        CompletePendingResponse(runtime, startRequestId, """{"turn":{"id":"turn_restart_4","status":"inProgress"}}""");

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.Equal("turn_restart_4", result.TurnId);

        var interruptRequestedEvent = Assert.Single(events.Where(static item =>
            item.Kind == ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated
            && string.Equals(item.Status, "interrupt_requested", StringComparison.Ordinal)));
        var interruptRequestedState = GetPendingInputStatePayload(interruptRequestedEvent);
        Assert.True(ReadStructuredBoolean(interruptRequestedState, "interruptRequestPending"));
        Assert.False(ReadStructuredBoolean(interruptRequestedState, "submitPendingSteersAfterInterrupt"));
        Assert.Collection(
            ReadStructuredItems(interruptRequestedState, "entries"),
            entry =>
            {
                Assert.Equal("Interrupt", ReadStructuredString(entry, "requestedMode"));
                Assert.Equal("interrupt_requested", ReadStructuredString(entry, "lifecycleState"));
                Assert.Equal("QueuedUserMessage", ReadStructuredString(entry, "pendingBucket"));
            });

        var awaitingCommitEvent = Assert.Single(events.Where(static item =>
            item.Kind == ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated
            && string.Equals(item.Status, "awaiting_commit", StringComparison.Ordinal)));
        var awaitingCommitState = GetPendingInputStatePayload(awaitingCommitEvent);
        Assert.False(ReadStructuredBoolean(awaitingCommitState, "interruptRequestPending"));
        Assert.False(ReadStructuredBoolean(awaitingCommitState, "submitPendingSteersAfterInterrupt"));
        Assert.Empty(ReadStructuredItems(awaitingCommitState, "entries"));
        Assert.Collection(
            ReadStructuredItems(awaitingCommitState, "queuedUserMessages"),
            entry =>
            {
                Assert.Equal("Interrupt", ReadStructuredString(entry, "requestedMode"));
                Assert.Equal("awaiting_commit", ReadStructuredString(entry, "lifecycleState"));
                Assert.Equal("QueuedUserMessage", ReadStructuredString(entry, "pendingBucket"));
            });

        var requests = await ReadRequestDocumentsAsync(stream);
        Assert.Single(requests);
        Assert.Equal("turn/start", requests[0].RootElement.GetProperty("method").GetString());
        var restartParameters = requests[0].RootElement.GetProperty("params");
        Assert.Equal("thread-interrupt-1", restartParameters.GetProperty("threadId").GetString());
        var restartInput = restartParameters.GetProperty("input");
        Assert.Single(restartInput.EnumerateArray());
        Assert.Equal("重新开始这轮", restartInput[0].GetProperty("text").GetString());
        foreach (var request in requests)
        {
            request.Dispose();
        }
    }

    [Fact]
    public async Task SendFollowUpAsync_WhenInterruptLifecycleSucceeds_EmitsRequestedAndCompletedPendingFollowUpEvents()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-interrupt-lifecycle-1", activeTurnId: "turn-active-lifecycle-1");
        using var streamHandle = stream;
        using var writerHandle = writer;

        const string correlationId = "corr-interrupt-001";
        var pendingTask = runtime.SendFollowUpAsync("重新开始这轮", FollowUpMode.Interrupt, CancellationToken.None, correlationId);
        var interruptRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, interruptRequestId, "{}");
        await Task.Delay(100);

        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn-active-lifecycle-1", true, "interrupted", null, null, """{"status":"interrupted"}""");
        var startRequestId = await WaitForPendingResponseIdAsync(runtime, interruptRequestId);
        CompletePendingResponse(runtime, startRequestId, """{"turn":{"id":"turn_restart_lifecycle_2","status":"inProgress"}}""");

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(FollowUpMode.Interrupt, result.RequestedMode);
        Assert.Equal(FollowUpMode.Queue, result.EffectiveMode);

        var lifecycleEvents = events
            .Where(static item =>
                item.Kind == ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated
                && item.Status is not null
                && item.Status.StartsWith("interrupt_", StringComparison.Ordinal))
            .ToArray();

        Assert.Collection(
            lifecycleEvents,
            streamEvent =>
            {
                Assert.Equal("interrupt_requested", streamEvent.Status);
                Assert.Equal("thread-interrupt-lifecycle-1", streamEvent.ThreadId?.Value);
                Assert.Equal("turn-active-lifecycle-1", streamEvent.TurnId?.Value);
                var state = GetPendingInputStatePayload(streamEvent);
                var interruptEntry = Assert.Single(ReadStructuredItems(state, "entries"));
                Assert.Equal(correlationId, ReadStructuredString(interruptEntry, "correlationId"));
                Assert.Equal("interrupt_requested", ReadStructuredString(interruptEntry, "lifecycleState"));
                Assert.Equal("turn-active-lifecycle-1", ReadStructuredString(interruptEntry, "expectedTurnId"));
                Assert.Equal("turn-active-lifecycle-1", ReadStructuredString(interruptEntry, "turnId"));
            },
            streamEvent =>
            {
                Assert.Equal("interrupt_completed", streamEvent.Status);
                Assert.Equal("thread-interrupt-lifecycle-1", streamEvent.ThreadId?.Value);
                Assert.Equal("turn-active-lifecycle-1", streamEvent.TurnId?.Value);
                var state = GetPendingInputStatePayload(streamEvent);
                var interruptEntry = Assert.Single(ReadStructuredItems(state, "entries"));
                Assert.Equal(correlationId, ReadStructuredString(interruptEntry, "correlationId"));
                Assert.Equal("interrupt_completed", ReadStructuredString(interruptEntry, "lifecycleState"));
                Assert.Equal("turn-active-lifecycle-1", ReadStructuredString(interruptEntry, "expectedTurnId"));
                Assert.Equal("turn-active-lifecycle-1", ReadStructuredString(interruptEntry, "turnId"));
            });
    }

    [Fact]
    public async Task SendFollowUpAsync_WhenInterruptRestartCommittedMessageArrives_EmitsCorrelationLifecycleAndCommittedCorrelationId()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-interrupt-correlation-1", activeTurnId: "turn-active-interrupt-correlation-1");
        using var streamHandle = stream;
        using var writerHandle = writer;

        const string correlationId = "corr-interrupt-committed-001";
        var pendingTask = runtime.SendFollowUpAsync("重新开始这轮", FollowUpMode.Interrupt, CancellationToken.None, correlationId);
        var interruptRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, interruptRequestId, "{}");
        await Task.Delay(100);

        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn-active-interrupt-correlation-1", true, "interrupted", null, null, """{"status":"interrupted"}""");
        var startRequestId = await WaitForPendingResponseIdAsync(runtime, interruptRequestId);
        CompletePendingResponse(runtime, startRequestId, """{"turn":{"id":"turn_interrupt_correlation_2","status":"inProgress"}}""");

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(FollowUpMode.Interrupt, result.RequestedMode);
        Assert.Equal(FollowUpMode.Queue, result.EffectiveMode);

        var awaitingCommit = Assert.Single(events.Where(static item =>
            item.Kind == ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated
            && string.Equals(item.Status, "awaiting_commit", StringComparison.Ordinal)));
        Assert.Equal("thread-interrupt-correlation-1", awaitingCommit.ThreadId?.Value);
        Assert.Equal("turn_interrupt_correlation_2", awaitingCommit.TurnId?.Value);
        var awaitingCommitState = GetPendingInputStatePayload(awaitingCommit);
        var awaitingCommitEntry = Assert.Single(ReadStructuredItems(awaitingCommitState, "queuedUserMessages"));
        Assert.Equal(correlationId, ReadStructuredString(awaitingCommitEntry, "correlationId"));
        Assert.Equal("turn-active-interrupt-correlation-1", ReadStructuredString(awaitingCommitEntry, "expectedTurnId"));
        Assert.Equal("turn_interrupt_correlation_2", ReadStructuredString(awaitingCommitEntry, "turnId"));
        Assert.Equal("Interrupt", ReadStructuredString(awaitingCommitEntry, "requestedMode"));
        Assert.Equal("Queue", ReadStructuredString(awaitingCommitEntry, "effectiveMode"));
        Assert.Equal("awaiting_commit", ReadStructuredString(awaitingCommitEntry, "lifecycleState"));

        const string committedNotification = """
        {"method":"item/completed","params":{"threadId":"thread-interrupt-correlation-1","turnId":"turn_interrupt_correlation_2","item":{"id":"user-msg-interrupt-1","type":"user_message","status":"completed","content":[{"type":"input_text","text":"重新开始这轮"}]}}}
        """;
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(committedNotification), committedNotification);

        var committedLifecycle = Assert.Single(events.Where(static item =>
            item.Kind == ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated
            && string.Equals(item.Status, "committed", StringComparison.Ordinal)));
        Assert.Equal("thread-interrupt-correlation-1", committedLifecycle.ThreadId?.Value);
        Assert.Equal("turn_interrupt_correlation_2", committedLifecycle.TurnId?.Value);
        var committedLifecycleState = GetPendingInputStatePayload(committedLifecycle);
        Assert.Empty(ReadStructuredItems(committedLifecycleState, "entries"));
        Assert.Empty(ReadStructuredItems(committedLifecycleState, "queuedUserMessages"));
        Assert.Empty(ReadStructuredItems(committedLifecycleState, "pendingSteers"));

        var committedUserMessage = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.UserMessageCommitted));
        var committedUserMessagePayload = GetCommittedUserMessagePayload(committedUserMessage);
        Assert.Equal(correlationId, ReadStructuredString(committedUserMessagePayload, "correlationId"));
    }

    [Fact]
    public async Task SendFollowUpAsync_WhenInterruptFollowUpHasQueuedSuccessor_WaitsForReplacementTurnCompletionBeforeDispatchingSuccessor()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-interrupt-owner-1", activeTurnId: "turn-active-interrupt-owner-1");
        using var streamHandle = stream;
        using var writerHandle = writer;

        var interruptTask = runtime.SendFollowUpAsync("先中断当前回合", FollowUpMode.Interrupt, CancellationToken.None, "corr-interrupt-owner-1");
        var queuedTask = runtime.SendFollowUpAsync("中断后再排队处理", FollowUpMode.Queue, CancellationToken.None, "corr-interrupt-owner-2");

        var interruptRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, interruptRequestId, "{}");

        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn-active-interrupt-owner-1", true, "interrupted", null, null, """{"status":"interrupted"}""");
        var restartRequestId = await WaitForPendingResponseIdAsync(runtime, interruptRequestId);
        CompletePendingResponse(runtime, restartRequestId, """{"turn":{"id":"turn_interrupt_owner_2","status":"inProgress"}}""");

        var interruptResult = await interruptTask;
        Assert.True(interruptResult.Success);
        Assert.Equal("turn_interrupt_owner_2", interruptResult.TurnId);
        stream.SetLength(0);
        stream.Position = 0;
        await Task.Delay(50);
        var queuedBeforeReplacementCompletion = await ReadRequestDocumentsAsync(stream);
        Assert.Empty(queuedBeforeReplacementCompletion);

        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn_interrupt_owner_2", true, "completed", "中断后的回合已完成", null, """{"status":"completed"}""");
        var queuedStartRequestId = await WaitForPendingResponseIdAsync(runtime, interruptRequestId, restartRequestId);
        CompletePendingResponse(runtime, queuedStartRequestId, """{"turn":{"id":"turn_interrupt_owner_3","status":"inProgress"}}""");

        var queuedResult = await queuedTask;
        Assert.True(queuedResult.Success);
        Assert.Equal("turn_interrupt_owner_3", queuedResult.TurnId);
    }

    [Fact]
    public async Task SendFollowUpAsync_WhenInterruptRequestFails_EmitsFailedPendingFollowUpEvent()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-interrupt-failure-1", activeTurnId: "turn-active-failure-1");
        using var streamHandle = stream;
        using var writerHandle = writer;

        const string correlationId = "corr-interrupt-failure-001";
        var pendingTask = runtime.SendFollowUpAsync("重新开始这轮", FollowUpMode.Interrupt, CancellationToken.None, correlationId);
        var interruptRequestId = await WaitForPendingResponseIdAsync(runtime);

        var handled = (bool)ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryResolveRpcResponse",
            ReflectionTestHelper.ParseJsonElement($"{{\"id\":{interruptRequestId},\"error\":{{\"message\":\"boom\"}}}}"))!;

        Assert.True(handled);

        var result = await pendingTask;
        Assert.False(result.Success);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal(FollowUpMode.Interrupt, result.RequestedMode);
        Assert.Equal(FollowUpMode.Interrupt, result.EffectiveMode);
        Assert.Contains("中断当前回合失败", result.Message, StringComparison.Ordinal);

        var lifecycleEvents = events
            .Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated)
            .Select(static item => item.Status)
            .Where(static state => !string.IsNullOrWhiteSpace(state))
            .ToArray();

        Assert.Collection(
            lifecycleEvents,
            state => Assert.Equal("queued", state),
            state => Assert.Equal("interrupt_requested", state),
            state => Assert.Equal("interrupt_failed", state));
    }

    [Fact]
    public async Task InterruptAsync_WritesTurnInterruptRequest_WhenActiveTurnExists()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-interrupt-2", activeTurnId: "turn-active-4");
        using var streamHandle = stream;
        using var writerHandle = writer;

        var pendingTask = runtime.InterruptAsync(CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, "{}" );
        await pendingTask;

        var requests = await ReadRequestDocumentsAsync(stream);
        Assert.Single(requests);
        Assert.Equal("turn/interrupt", requests[0].RootElement.GetProperty("method").GetString());
        var parameters = requests[0].RootElement.GetProperty("params");
        Assert.Equal("thread-interrupt-2", parameters.GetProperty("threadId").GetString());
        Assert.Equal("turn-active-4", parameters.GetProperty("turnId").GetString());
        requests[0].Dispose();
    }


    [Fact]
    public async Task InvokeDiagnosticRpcAsync_WritesRequestedMethod_AndReturnsResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.InvokeDiagnosticRpcAsync("model/list", StructuredJson("""{"limit":2}"""), CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """
            {
              "data": [
                {
                  "id": "gpt-5-codex"
                }
              ]
            }
            """);

        var result = await pendingTask;
        Assert.Equal("gpt-5-codex", result.GetProperty("data").Items[0].GetProperty("id").GetString());

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("model/list", request.RootElement.GetProperty("method").GetString());
        Assert.Equal(2, request.RootElement.GetProperty("params").GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task InvokeDiagnosticRpcAsync_WhenParametersAreNull_WritesEmptyObject()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.InvokeDiagnosticRpcAsync("collaborationmode/list", null, CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"data":[]}""");
        await pendingTask;

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("collaborationmode/list", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal(JsonValueKind.Object, parameters.ValueKind);
        Assert.False(parameters.EnumerateObject().Any());
    }

    [Theory]
    [InlineData("plugin/list")]
    [InlineData("plugin/read")]
    [InlineData("mcpserverstatus/list")]
    [InlineData("configRequirements/read")]
    [InlineData("windowsSandbox/setupStart")]
    [InlineData("command/exec/write")]
    public async Task InvokeDiagnosticRpcAsync_CanReachKernelOnlyMethods(string method)
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.InvokeDiagnosticRpcAsync(method, StructuredJson("""{"marker":"ok"}"""), CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"ok":true}""");

        var result = await pendingTask;
        Assert.True(result.GetProperty("ok").GetBoolean());

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal(method, request.RootElement.GetProperty("method").GetString());
        Assert.Equal("ok", request.RootElement.GetProperty("params").GetProperty("marker").GetString());
    }

    [Fact]
    public async Task ReadConfigAsync_WritesExpectedMethod_AndParsesTypedConfigResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ReadConfigAsync(
            new ControlPlaneConfigReadQuery
            {
                WorkingDirectory = "D:/Work/TianShu",
                IncludeLayers = true,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "config": {
                "model": "gpt-5.4",
                "default_permissions": "trusted",
                "personality": "pragmatic",
                "plan_mode_reasoning_effort": "medium",
                "experimental_instructions_file": "D:/legacy/instructions.md",
                "plugins": {
                  "demo_plugin": {
                    "enabled": false
                  }
                },
                "project_root_markers": [".git", ".hg"],
                "windows_wsl_setup_acknowledged": true,
                "zsh_path": "C:/Program Files/Git/usr/bin/zsh.exe",
                "apps": {
                  "_default": {
                    "enabled": true,
                    "default_tools_enabled": true
                  },
                  "demo": {
                    "enabled": true,
                    "isAccessible": true,
                    "default_tools_enabled": true,
                    "default_tools_approval_mode": "prompt",
                    "tools": {
                      "search": {
                        "enabled": true,
                        "approval_mode": "auto"
                      }
                    }
                  }
                },
                "audio": {
                  "microphone": "default",
                  "speaker": "headphones"
                },
                "feedback": {
                  "enabled": false
                },
                "file_opener": "vscode",
                "ghost_snapshot": {
                  "disable_warnings": true,
                  "ignore_large_untracked_dirs": 128,
                  "ignore_large_untracked_files": 64
                },
                "history": {
                  "persistence": "save-all",
                  "max_bytes": 2048
                },
                "memories": {
                  "no_memories_if_mcp_or_web_search": true,
                  "generate_memories": false,
                  "use_memories": false,
                  "max_raw_memories_for_consolidation": 512,
                  "max_unused_days": 21,
                  "max_rollout_age_days": 42,
                  "max_rollouts_per_startup": 9,
                  "min_rollout_idle_hours": 24,
                  "extract_model": "gpt-5-mini",
                  "consolidation_model": "gpt-5"
                },
                "features": {
                  "js_repl": true,
                  "plugins": false
                },
                "providers": {
                  "demo": {
                    "name": "Demo Provider",
                    "base_url": "https://example.invalid/v1",
                    "default_protocol": "responses",
                    "supports_websockets": true
                  }
                },
                "profiles": {
                  "default": {
                    "sandbox_mode": "workspace-write",
                    "experimental_instructions_file": "D:/legacy/profile-instructions.md",
                    "plan_mode_reasoning_effort": "high",
                    "features": {
                      "code_mode": true
                    },
                    "windows": {
                      "sandbox": "unelevated"
                    }
                  }
                },
                "shell_environment_policy": {
                  "inherit": "core",
                  "ignore_default_excludes": false,
                  "exclude": ["*_SECRET"],
                  "set": {
                    "FOO": "BAR"
                  },
                  "include_only": ["PATH"],
                  "experimental_use_profile": true
                },
                "windows": {
                  "sandbox": "elevated"
                },
                "mcp_servers": {
                  "docs": {
                    "url": "https://example.invalid/mcp",
                    "enabled": true,
                    "enabled_tools": ["search"],
                    "startup_timeout_sec": 12.5
                  }
                },
                "notice": {
                  "hide_full_access_warning": true,
                  "hide_world_writable_warning": false,
                  "hide_rate_limit_model_nudge": true,
                  "hide_gpt5_1_migration_prompt": true,
                  "hide_gpt-5.1-codex-max_migration_prompt": false,
                  "model_migrations": {
                    "gpt-5.1": "gpt-5.4"
                  }
                },
                "otel": {
                  "log_user_prompt": true,
                  "environment": "test",
                  "exporter": {
                    "otlp-http": {
                      "endpoint": "https://otel.invalid/http",
                      "headers": {
                        "Authorization": "Bearer test"
                      },
                      "protocol": "json",
                      "tls": {
                        "ca_certificate": "D:/certs/ca.pem"
                      }
                    }
                  },
                  "trace_exporter": "none",
                  "metrics_exporter": "statsig"
                },
                "projects": {
                  "D:/Work/TianShu/Test": {
                    "trust_level": "trusted"
                  }
                },
                "legacy_only_key": "legacy-value",
                "sandbox_workspace_write": {
                  "network_access": true,
                  "writable_roots": ["D:/Work/TianShu"]
                },
                "permissions": {
                  "trusted": {
                    "filesystem": {
                      "/workspace": "write",
                      "/repo": {
                        ".": "read"
                      }
                    },
                    "network": {
                      "enabled": true,
                      "allowed_domains": ["example.invalid"]
                    }
                  }
                },
                "skills": {
                  "bundled": {
                    "enabled": false
                  },
                  "config": [
                    {
                      "path": "D:/Work/TianShu/.tianshu/skills/demo-search",
                      "enabled": true
                    }
                  ]
                },
                "tui": {
                  "notifications": ["toast"],
                  "notification_method": "auto",
                  "animations": false,
                  "show_tooltips": true,
                  "alternate_screen": "never",
                  "status_line": ["model", "cwd"],
                  "theme": "ansi",
                  "model_availability_nux": {
                    "gpt-5.4": 2
                  }
                },
                "agents": {
                  "max_threads": 4,
                  "max_depth": 6,
                  "job_max_runtime_seconds": 180,
                  "researcher": {
                    "description": "Research role",
                    "config_file": "./agents/researcher.toml",
                    "nickname_candidates": ["Hypatia", "Noether"]
                  }
                }
              },
              "origins": {
                "model": {
                  "name": {
                    "type": "project",
                    "file": "D:/Work/TianShu/.tianshu/tianshu.toml"
                  },
                  "version": "origin-v1"
                },
                "sandbox_workspace_write": {
                  "name": {
                    "type": "project",
                    "file": "D:/Work/TianShu/.tianshu/tianshu.toml"
                  }
                }
              },
              "layers": [
                {
                  "name": {
                    "type": "project"
                  },
                  "version": "v1",
                  "disabledReason": "readonly",
                  "config": {
                    "model": "gpt-5.4"
                  }
                }
              ]
            }
            """);

        var result = await pendingTask;
        Assert.NotNull(result.Config);

        using var configDocument = JsonDocument.Parse(JsonSerializer.Serialize(result.Config!.ToPlainObject()));
        var config = configDocument.RootElement;

        Assert.Equal("gpt-5.4", config.GetProperty("model").GetString());
        Assert.Equal("trusted", config.GetProperty("default_permissions").GetString());
        Assert.Equal("pragmatic", config.GetProperty("personality").GetString());
        Assert.Equal("medium", config.GetProperty("plan_mode_reasoning_effort").GetString());
        Assert.Equal("D:/legacy/instructions.md", config.GetProperty("experimental_instructions_file").GetString());
        Assert.False(config.GetProperty("plugins").GetProperty("demo_plugin").GetProperty("enabled").GetBoolean());
        Assert.Equal(".git", config.GetProperty("project_root_markers")[0].GetString());
        Assert.Equal(".hg", config.GetProperty("project_root_markers")[1].GetString());
        Assert.True(config.GetProperty("windows_wsl_setup_acknowledged").GetBoolean());
        Assert.Equal("C:/Program Files/Git/usr/bin/zsh.exe", config.GetProperty("zsh_path").GetString());
        Assert.Equal("save-all", config.GetProperty("history").GetProperty("persistence").GetString());
        Assert.Equal(2048, config.GetProperty("history").GetProperty("max_bytes").GetInt32());
        Assert.True(config.GetProperty("features").GetProperty("js_repl").GetBoolean());
        Assert.False(config.GetProperty("features").GetProperty("plugins").GetBoolean());
        Assert.Equal("Demo Provider", config.GetProperty("providers").GetProperty("demo").GetProperty("name").GetString());
        Assert.Equal("https://example.invalid/v1", config.GetProperty("providers").GetProperty("demo").GetProperty("base_url").GetString());
        Assert.Equal("responses", config.GetProperty("providers").GetProperty("demo").GetProperty("default_protocol").GetString());
        Assert.True(config.GetProperty("providers").GetProperty("demo").GetProperty("supports_websockets").GetBoolean());
        Assert.Equal("workspace-write", config.GetProperty("profiles").GetProperty("default").GetProperty("sandbox_mode").GetString());
        Assert.Equal("D:/legacy/profile-instructions.md", config.GetProperty("profiles").GetProperty("default").GetProperty("experimental_instructions_file").GetString());
        Assert.Equal("high", config.GetProperty("profiles").GetProperty("default").GetProperty("plan_mode_reasoning_effort").GetString());
        Assert.True(config.GetProperty("profiles").GetProperty("default").GetProperty("features").GetProperty("code_mode").GetBoolean());
        Assert.Equal("unelevated", config.GetProperty("profiles").GetProperty("default").GetProperty("windows").GetProperty("sandbox").GetString());
        Assert.Equal("core", config.GetProperty("shell_environment_policy").GetProperty("inherit").GetString());
        Assert.False(config.GetProperty("shell_environment_policy").GetProperty("ignore_default_excludes").GetBoolean());
        Assert.Equal("*_SECRET", Assert.Single(config.GetProperty("shell_environment_policy").GetProperty("exclude").EnumerateArray()).GetString());
        Assert.Equal("BAR", config.GetProperty("shell_environment_policy").GetProperty("set").GetProperty("FOO").GetString());
        Assert.Equal("PATH", Assert.Single(config.GetProperty("shell_environment_policy").GetProperty("include_only").EnumerateArray()).GetString());
        Assert.True(config.GetProperty("shell_environment_policy").GetProperty("experimental_use_profile").GetBoolean());
        Assert.Equal("elevated", config.GetProperty("windows").GetProperty("sandbox").GetString());
        Assert.Equal("https://example.invalid/mcp", config.GetProperty("mcp_servers").GetProperty("docs").GetProperty("url").GetString());
        Assert.True(config.GetProperty("mcp_servers").GetProperty("docs").GetProperty("enabled").GetBoolean());
        Assert.Equal("search", Assert.Single(config.GetProperty("mcp_servers").GetProperty("docs").GetProperty("enabled_tools").EnumerateArray()).GetString());
        Assert.Equal(12.5d, config.GetProperty("mcp_servers").GetProperty("docs").GetProperty("startup_timeout_sec").GetDouble());
        Assert.True(config.GetProperty("apps").GetProperty("_default").GetProperty("default_tools_enabled").GetBoolean());
        Assert.True(config.GetProperty("apps").GetProperty("demo").GetProperty("enabled").GetBoolean());
        Assert.True(config.GetProperty("apps").GetProperty("demo").GetProperty("isAccessible").GetBoolean());
        Assert.Equal("prompt", config.GetProperty("apps").GetProperty("demo").GetProperty("default_tools_approval_mode").GetString());
        Assert.True(config.GetProperty("apps").GetProperty("demo").GetProperty("tools").GetProperty("search").GetProperty("enabled").GetBoolean());
        Assert.Equal("auto", config.GetProperty("apps").GetProperty("demo").GetProperty("tools").GetProperty("search").GetProperty("approval_mode").GetString());
        Assert.Equal("default", config.GetProperty("audio").GetProperty("microphone").GetString());
        Assert.Equal("headphones", config.GetProperty("audio").GetProperty("speaker").GetString());
        Assert.False(config.GetProperty("feedback").GetProperty("enabled").GetBoolean());
        Assert.Equal("vscode", config.GetProperty("file_opener").GetString());
        Assert.True(config.GetProperty("ghost_snapshot").GetProperty("disable_warnings").GetBoolean());
        Assert.Equal(128, config.GetProperty("ghost_snapshot").GetProperty("ignore_large_untracked_dirs").GetInt32());
        Assert.Equal(64, config.GetProperty("ghost_snapshot").GetProperty("ignore_large_untracked_files").GetInt32());
        Assert.True(config.GetProperty("memories").GetProperty("no_memories_if_mcp_or_web_search").GetBoolean());
        Assert.False(config.GetProperty("memories").GetProperty("generate_memories").GetBoolean());
        Assert.False(config.GetProperty("memories").GetProperty("use_memories").GetBoolean());
        Assert.Equal(512, config.GetProperty("memories").GetProperty("max_raw_memories_for_consolidation").GetInt32());
        Assert.Equal(21, config.GetProperty("memories").GetProperty("max_unused_days").GetInt32());
        Assert.Equal(42, config.GetProperty("memories").GetProperty("max_rollout_age_days").GetInt32());
        Assert.Equal(9, config.GetProperty("memories").GetProperty("max_rollouts_per_startup").GetInt32());
        Assert.Equal(24, config.GetProperty("memories").GetProperty("min_rollout_idle_hours").GetInt32());
        Assert.Equal("gpt-5-mini", config.GetProperty("memories").GetProperty("extract_model").GetString());
        Assert.Equal("gpt-5", config.GetProperty("memories").GetProperty("consolidation_model").GetString());
        Assert.True(config.GetProperty("notice").GetProperty("hide_full_access_warning").GetBoolean());
        Assert.False(config.GetProperty("notice").GetProperty("hide_world_writable_warning").GetBoolean());
        Assert.True(config.GetProperty("notice").GetProperty("hide_rate_limit_model_nudge").GetBoolean());
        Assert.True(config.GetProperty("notice").GetProperty("hide_gpt5_1_migration_prompt").GetBoolean());
        Assert.False(config.GetProperty("notice").GetProperty("hide_gpt-5.1-codex-max_migration_prompt").GetBoolean());
        Assert.Equal("gpt-5.4", config.GetProperty("notice").GetProperty("model_migrations").GetProperty("gpt-5.1").GetString());
        Assert.True(config.GetProperty("otel").GetProperty("log_user_prompt").GetBoolean());
        Assert.Equal("test", config.GetProperty("otel").GetProperty("environment").GetString());
        Assert.Equal("https://otel.invalid/http", config.GetProperty("otel").GetProperty("exporter").GetProperty("otlp-http").GetProperty("endpoint").GetString());
        Assert.Equal("Bearer test", config.GetProperty("otel").GetProperty("exporter").GetProperty("otlp-http").GetProperty("headers").GetProperty("Authorization").GetString());
        Assert.Equal("json", config.GetProperty("otel").GetProperty("exporter").GetProperty("otlp-http").GetProperty("protocol").GetString());
        Assert.Equal("D:/certs/ca.pem", config.GetProperty("otel").GetProperty("exporter").GetProperty("otlp-http").GetProperty("tls").GetProperty("ca_certificate").GetString());
        Assert.Equal("none", config.GetProperty("otel").GetProperty("trace_exporter").GetString());
        Assert.Equal("statsig", config.GetProperty("otel").GetProperty("metrics_exporter").GetString());
        Assert.Equal("trusted", config.GetProperty("projects").GetProperty("D:/Work/TianShu/Test").GetProperty("trust_level").GetString());
        Assert.False(config.GetProperty("skills").GetProperty("bundled").GetProperty("enabled").GetBoolean());
        Assert.Equal("D:/Work/TianShu/.tianshu/skills/demo-search", config.GetProperty("skills").GetProperty("config")[0].GetProperty("path").GetString());
        Assert.True(config.GetProperty("skills").GetProperty("config")[0].GetProperty("enabled").GetBoolean());
        Assert.True(config.GetProperty("sandbox_workspace_write").GetProperty("network_access").GetBoolean());
        Assert.Equal("D:/Work/TianShu", Assert.Single(config.GetProperty("sandbox_workspace_write").GetProperty("writable_roots").EnumerateArray()).GetString());
        Assert.Equal("write", config.GetProperty("permissions").GetProperty("trusted").GetProperty("filesystem").GetProperty("/workspace").GetString());
        Assert.Equal("read", config.GetProperty("permissions").GetProperty("trusted").GetProperty("filesystem").GetProperty("/repo").GetProperty(".").GetString());
        Assert.True(config.GetProperty("permissions").GetProperty("trusted").GetProperty("network").GetProperty("enabled").GetBoolean());
        Assert.Equal("example.invalid", Assert.Single(config.GetProperty("permissions").GetProperty("trusted").GetProperty("network").GetProperty("allowed_domains").EnumerateArray()).GetString());
        Assert.Equal("toast", Assert.Single(config.GetProperty("tui").GetProperty("notifications").EnumerateArray()).GetString());
        Assert.Equal("auto", config.GetProperty("tui").GetProperty("notification_method").GetString());
        Assert.False(config.GetProperty("tui").GetProperty("animations").GetBoolean());
        Assert.True(config.GetProperty("tui").GetProperty("show_tooltips").GetBoolean());
        Assert.Equal("never", config.GetProperty("tui").GetProperty("alternate_screen").GetString());
        Assert.Equal("model", config.GetProperty("tui").GetProperty("status_line")[0].GetString());
        Assert.Equal("cwd", config.GetProperty("tui").GetProperty("status_line")[1].GetString());
        Assert.Equal("ansi", config.GetProperty("tui").GetProperty("theme").GetString());
        Assert.Equal(2, config.GetProperty("tui").GetProperty("model_availability_nux").GetProperty("gpt-5.4").GetInt32());
        Assert.Equal(4, config.GetProperty("agents").GetProperty("max_threads").GetInt32());
        Assert.Equal(6, config.GetProperty("agents").GetProperty("max_depth").GetInt32());
        Assert.Equal(180, config.GetProperty("agents").GetProperty("job_max_runtime_seconds").GetInt32());
        Assert.Equal("Research role", config.GetProperty("agents").GetProperty("researcher").GetProperty("description").GetString());
        Assert.Equal("./agents/researcher.toml", config.GetProperty("agents").GetProperty("researcher").GetProperty("config_file").GetString());
        Assert.Equal("Hypatia", config.GetProperty("agents").GetProperty("researcher").GetProperty("nickname_candidates")[0].GetString());
        Assert.Equal("Noether", config.GetProperty("agents").GetProperty("researcher").GetProperty("nickname_candidates")[1].GetString());
        Assert.True(result.Origins.ContainsKey("model"));
        Assert.Equal("origin-v1", result.Origins["model"].Version);
        Assert.Single(result.Fields);
        Assert.Equal("legacy_only_key", result.Fields[0].KeyPath);
        Assert.Equal("legacy-value", result.Fields[0].ValueText);
        Assert.Single(result.Layers);
        Assert.Equal("v1", result.Layers[0].Version);
        Assert.Equal("readonly", result.Layers[0].DisabledReason);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("config/read", request.RootElement.GetProperty("method").GetString());
        Assert.Equal("D:/Work/TianShu", request.RootElement.GetProperty("params").GetProperty("cwd").GetString());
        Assert.True(request.RootElement.GetProperty("params").GetProperty("includeLayers").GetBoolean());
    }

    [Fact]
    public async Task ListModelsAsync_WritesExpectedMethod_AndParsesExtendedCapabilities()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ListModelsAsync(
            new ControlPlaneModelCatalogQuery
            {
                Limit = 20,
                Cursor = "cursor-1",
                IncludeHidden = true,
                RequireEndpoint = true,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "nextCursor": "cursor-2",
              "data": [
                {
                  "id": "gpt-5.4",
                  "model": "gpt-5.4",
                  "displayName": "GPT-5.4",
                  "defaultReasoningEffort": "medium",
                  "supportedReasoningEfforts": [
                    { "reasoningEffort": "low", "description": "Fast" },
                    { "reasoningEffort": "high", "description": "Deep" }
                  ],
                  "inputModalities": ["text", "image"],
                  "supportsPersonality": false,
                  "hidden": true,
                  "supportsParallelToolCalls": true,
                  "supportsReasoningSummaries": true,
                  "defaultReasoningSummary": "auto",
                  "supportsVerbosity": true,
                  "defaultVerbosity": "medium",
                  "preferWebsocketTransport": true,
                  "isDefault": false,
                  "description": "旗舰模型",
                  "availabilityNux": {
                    "message": "需要先开通。"
                  },
                  "upgrade": "gpt-5.5",
                  "upgradeInfo": {
                    "migrationMarkdown": "升级说明"
                  }
                }
              ]
            }
            """);

        var result = await pendingTask;

        Assert.Equal("cursor-2", result.NextCursor);
        var model = Assert.Single(result.Items);
        Assert.Equal("gpt-5.4", model.Id);
        Assert.Equal("gpt-5.4", model.Model);
        Assert.True(model.Hidden);
        Assert.True(model.SupportsParallelToolCalls);
        Assert.True(model.SupportsReasoningSummaries);
        Assert.Equal("auto", model.DefaultReasoningSummary);
        Assert.True(model.SupportsVerbosity);
        Assert.Equal("medium", model.DefaultVerbosity);
        Assert.True(model.PreferWebsocketTransport);
        Assert.Equal("需要先开通。", model.AvailabilityNuxMessage);
        Assert.Equal("gpt-5.5", model.UpgradeModel);
        Assert.Equal("升级说明", model.UpgradeMigrationMarkdown);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("model/list", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal(20, parameters.GetProperty("limit").GetInt32());
        Assert.Equal("cursor-1", parameters.GetProperty("cursor").GetString());
        Assert.True(parameters.GetProperty("includeHidden").GetBoolean());
        Assert.True(parameters.GetProperty("requireEndpoint").GetBoolean());
    }

    [Fact]
    public async Task ReadConfigRequirementsAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ReadConfigRequirementsAsync(
            new ControlPlaneConfigRequirementsQuery
            {
                WorkingDirectory = "D:/Work/TianShu",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "requirements": {
                "allowedApprovalPolicies": ["never"],
                "featureRequirements": {
                  "tool_search": true
                },
                "network": {
                  "enabled": true,
                  "httpPort": 18080
                }
              }
            }
            """);

        var result = await pendingTask;
        Assert.True(result.IsDefined);
        Assert.Equal("never", Assert.Single(result.AllowedApprovalPolicies));
        Assert.True(result.FeatureRequirements["tool_search"]);
        Assert.NotNull(result.Network);
        Assert.True(result.Network!.Enabled);
        Assert.Equal((ushort)18080, result.Network.HttpPort);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("configRequirements/read", request.RootElement.GetProperty("method").GetString());
        Assert.Equal("D:/Work/TianShu", request.RootElement.GetProperty("params").GetProperty("cwd").GetString());
    }

    [Fact]
    public async Task WriteConfigValueAsync_WritesExpectedMethod_AndSerializesTypedValue()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-config-write-1", activeTurnId: null);

        var pendingTask = runtime.WriteConfigValueAsync(
            new ControlPlaneConfigValueWriteCommand
            {
                KeyPath = "shell_environment_policy.inherit",
                Value = StructuredValue.FromBoolean(false),
                MergeStrategy = "upsert",
                WorkingDirectory = "D:/workspace",
                FilePath = "D:/workspace/config.override.toml",
                ExpectedVersion = "v1",
            },
            CancellationToken.None);

        var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("config/value/write", request.RootElement.GetProperty("method").GetString());
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("shell_environment_policy.inherit", parameters.GetProperty("keyPath").GetString());
        Assert.False(parameters.GetProperty("value").GetBoolean());
        Assert.Equal("upsert", parameters.GetProperty("mergeStrategy").GetString());
        Assert.Equal("D:/workspace", parameters.GetProperty("cwd").GetString());
        Assert.Equal("D:/workspace/config.override.toml", parameters.GetProperty("filePath").GetString());
        Assert.Equal("v1", parameters.GetProperty("expectedVersion").GetString());

        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "status": "okOverridden",
              "version": "v2",
              "filePath": "D:/workspace/config.override.toml",
              "overriddenMetadata": {
                "message": "Overridden by project config: D:/workspace/.tianshu/tianshu.toml",
                "overridingLayer": {
                  "name": {
                    "type": "project",
                    "dotTianShuFolder": "D:/workspace/.tianshu"
                  },
                  "version": "layer-v1"
                },
                "effectiveValue": false
              }
            }
            """);

        var result = await pendingTask;
        await writer.FlushAsync();
        Assert.Equal("okOverridden", result.Status);
        Assert.Equal("v2", result.Version);
        Assert.Equal("D:/workspace/config.override.toml", result.FilePath);
        Assert.True(result.IsOverridden);
        Assert.NotNull(result.OverriddenMetadata);
        Assert.Equal("Overridden by project config: D:/workspace/.tianshu/tianshu.toml", result.OverriddenMetadata!.Message);
        Assert.NotNull(result.OverriddenMetadata.OverridingLayer);
        Assert.Equal("project", result.OverriddenMetadata.OverridingLayer!.Type);
        Assert.Equal("D:/workspace/.tianshu", result.OverriddenMetadata.OverridingLayer.DotTianShuFolder);
        Assert.NotNull(result.OverriddenMetadata.EffectiveValue);
        Assert.Equal(StructuredValueKind.Boolean, result.OverriddenMetadata.EffectiveValue!.Kind);
        Assert.False(result.OverriddenMetadata.EffectiveValue.BooleanValue);
    }

    [Fact]
    public async Task WriteConfigBatchAsync_WritesExpectedMethod_AndSerializesTypedItems()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-config-batch-write-1", activeTurnId: null);

        var pendingTask = runtime.WriteConfigBatchAsync(
            new ControlPlaneConfigBatchWriteCommand
            {
                Items =
                [
                    new ControlPlaneConfigWriteItem
                    {
                        KeyPath = "profiles.default.model",
                        Value = StructuredValue.FromString("gpt-5"),
                        MergeStrategy = "upsert",
                    },
                    new ControlPlaneConfigWriteItem
                    {
                        KeyPath = "shell_environment_policy.inherit",
                        Value = StructuredValue.FromBoolean(false),
                    },
                ],
                WorkingDirectory = "D:/workspace",
                FilePath = "D:/workspace/config.override.toml",
                ExpectedVersion = "v2",
                ReloadUserConfig = true,
            },
            CancellationToken.None);

        var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("config/batchWrite", request.RootElement.GetProperty("method").GetString());
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("D:/workspace", parameters.GetProperty("cwd").GetString());
        Assert.Equal("D:/workspace/config.override.toml", parameters.GetProperty("filePath").GetString());
        Assert.Equal("v2", parameters.GetProperty("expectedVersion").GetString());
        Assert.True(parameters.GetProperty("reloadUserConfig").GetBoolean());
        var items = parameters.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("profiles.default.model", items[0].GetProperty("keyPath").GetString());
        Assert.Equal("gpt-5", items[0].GetProperty("value").GetString());
        Assert.Equal("upsert", items[0].GetProperty("mergeStrategy").GetString());
        Assert.Equal("shell_environment_policy.inherit", items[1].GetProperty("keyPath").GetString());
        Assert.False(items[1].GetProperty("value").GetBoolean());
        Assert.Equal("replace", items[1].GetProperty("mergeStrategy").GetString());

        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "status": "okOverridden",
              "version": "v3",
              "filePath": "D:/workspace/config.override.toml",
              "overriddenMetadata": {
                "message": "Overridden by session flags",
                "overridingLayer": {
                  "name": {
                    "type": "sessionFlags"
                  },
                  "version": "layer-v2"
                },
                "effectiveValue": {
                  "inherit": false
                }
              }
            }
            """);

        var result = await pendingTask;
        await writer.FlushAsync();
        Assert.Equal("okOverridden", result.Status);
        Assert.Equal("v3", result.Version);
        Assert.Equal("D:/workspace/config.override.toml", result.FilePath);
        Assert.True(result.IsOverridden);
        Assert.NotNull(result.OverriddenMetadata);
        Assert.Equal("Overridden by session flags", result.OverriddenMetadata!.Message);
        Assert.NotNull(result.OverriddenMetadata.OverridingLayer);
        Assert.Equal("sessionFlags", result.OverriddenMetadata.OverridingLayer!.Type);
    }

    [Fact]
    public async Task WriteConfigBatchAsync_WhenReloadingActiveProfileModel_UpdatesRuntimeThreadDefaults()
    {
        var runtime = new TianShuExecutionRuntime();
        ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
        {
            Model = "old-model",
            ModelProvider = "anthropic",
            ProfileName = "work",
            WorkingDirectory = "D:/workspace",
        });
        var (stream, _) = CreateConnectedRuntime(runtime, activeThreadId: null, activeTurnId: null);

        var writeTask = runtime.WriteConfigBatchAsync(
            new ControlPlaneConfigBatchWriteCommand
            {
                Items =
                [
                    new ControlPlaneConfigWriteItem
                    {
                        KeyPath = "profiles.work.model",
                        Value = StructuredValue.FromString("gpt-5.5-mini"),
                    },
                ],
                WorkingDirectory = "D:/workspace",
                ReloadUserConfig = true,
            },
            CancellationToken.None);
        var writeRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, writeRequestId, """{"status":"ok"}""");
        _ = await writeTask;

        var startTask = runtime.StartNewThreadAsync(CancellationToken.None);
        var startRequestId = await WaitForPendingResponseIdAsync(runtime, writeRequestId);
        CompletePendingResponse(runtime, startRequestId, """{"thread":{"id":"thread-reloaded-model-001","preview":"new model"}}""");
        _ = await startTask;

        var requests = await ReadRequestDocumentsAsync(stream);
        var startRequest = Assert.Single(requests, static request => request.RootElement.GetProperty("method").GetString() == "thread/start");
        var parameters = startRequest.RootElement.GetProperty("params");
        Assert.Equal("gpt-5.5-mini", parameters.GetProperty("model").GetString());
        Assert.Equal("anthropic", parameters.GetProperty("modelProvider").GetString());
    }

    [Fact]
    public async Task ListSkillsAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ListSkillsAsync(
            new ControlPlaneSkillCatalogQuery
            {
                WorkingDirectories = ["D:/Work/TianShu"],
                ForceReload = true,
                ExtraRootsByWorkingDirectory =
                [
                    new ControlPlaneSkillsExtraRootsForWorkingDirectory
                    {
                        WorkingDirectory = "D:/Work/TianShu",
                        ExtraUserRoots = ["D:/Work/TianShu/.tianshu-extra-skills"],
                    },
                ],
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "data": [
                {
                  "cwd": "D:/Work/TianShu",
                  "skills": [
                    {
                      "name": "demo-search",
                      "description": "search skill",
                      "shortDescription": "search summary",
                      "interface": {
                        "displayName": "Demo Search",
                        "shortDescription": "search ui",
                        "iconSmall": "D:/Work/TianShu/.tianshu/skills/demo-search/assets/small.png",
                        "brandColor": "#336699",
                        "defaultPrompt": "find things"
                      },
                      "dependencies": {
                        "tools": [
                          {
                            "type": "env_var",
                            "value": "GITHUB_TOKEN",
                            "description": "GitHub token"
                          }
                        ]
                      },
                      "pathToSkillsMd": "D:/Work/TianShu/.tianshu/skills/demo-search/SKILL.md",
                      "path": "D:/Work/TianShu/.tianshu/skills/demo-search/SKILL.md",
                      "scope": "repo",
                      "enabled": true
                    }
                  ],
                  "errors": [
                    {
                      "path": "D:/Work/TianShu/.tianshu-extra-skills/broken",
                      "message": "manifest 缺少 name"
                    }
                  ]
                }
              ]
            }
            """);

        var result = await pendingTask;
        var entry = Assert.Single(result.Entries);
        Assert.Equal("D:/Work/TianShu", entry.WorkingDirectory);
        var skill = Assert.Single(entry.Skills);
        Assert.Equal("demo-search", skill.Name);
        Assert.Equal("search summary", skill.ShortDescription);
        Assert.Equal("repo", skill.Scope);
        Assert.True(skill.Enabled);
        Assert.Equal("D:/Work/TianShu/.tianshu/skills/demo-search/SKILL.md", skill.Path);
        Assert.Equal("Demo Search", ReadStructuredString(skill.Interface, "displayName"));
        Assert.Equal("search ui", ReadStructuredString(skill.Interface, "shortDescription"));
        Assert.Equal("GITHUB_TOKEN", ReadStructuredString(skill.Dependencies, "tools", 0, "value"));
        var error = Assert.Single(entry.Errors);
        Assert.Equal("manifest 缺少 name", error.Message);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("skills/list", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("D:/Work/TianShu", parameters.GetProperty("cwds")[0].GetString());
        Assert.True(parameters.GetProperty("forceReload").GetBoolean());
        var extraRoots = parameters.GetProperty("perCwdExtraUserRoots")[0];
        Assert.Equal("D:/Work/TianShu", extraRoots.GetProperty("cwd").GetString());
        Assert.Equal("D:/Work/TianShu/.tianshu-extra-skills", extraRoots.GetProperty("extraUserRoots")[0].GetString());
    }

    [Fact]
    public async Task WriteSkillsConfigAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.WriteSkillsConfigAsync(
            new ControlPlaneSkillConfigWriteCommand
            {
                Path = "D:/Work/TianShu/.tianshu/skills/demo-search",
                Enabled = false,
                WorkingDirectory = "D:/Work/TianShu",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"effectiveEnabled":false}""");

        var result = await pendingTask;
        Assert.False(result.EffectiveEnabled);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("skills/config/write", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("D:/Work/TianShu/.tianshu/skills/demo-search", parameters.GetProperty("path").GetString());
        Assert.False(parameters.GetProperty("enabled").GetBoolean());
        Assert.Equal("D:/Work/TianShu", parameters.GetProperty("cwd").GetString());
    }

    [Fact]
    public async Task ListRemoteSkillsAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ListRemoteSkillsAsync(
            new ControlPlaneRemoteSkillCatalogQuery
            {
                HazelnutScope = "org",
                ProductSurface = "cli",
                Enabled = true,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "data": [
                {
                  "hazelnutId": "skill_remote_001",
                  "name": "Remote Search",
                  "description": "remote skill",
                  "hazelnutScope": "org"
                }
              ],
              "nextCursor": "cursor_remote_001"
            }
            """);

        var result = await pendingTask;
        var item = Assert.Single(result.Items);
        Assert.Equal("skill_remote_001", item.Id);
        Assert.Equal("Remote Search", item.Name);
        Assert.Equal("remote skill", item.Description);
        Assert.Equal("org", item.HazelnutScope);
        Assert.Equal("cursor_remote_001", result.NextCursor);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("skills/remote/list", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("org", parameters.GetProperty("hazelnutScope").GetString());
        Assert.Equal("cli", parameters.GetProperty("productSurface").GetString());
        Assert.True(parameters.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task ExportRemoteSkillAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ExportRemoteSkillAsync(
            new ControlPlaneRemoteSkillExportCommand
            {
                HazelnutId = "skill_remote_001",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"id":"skill_remote_001","path":"D:/Exports/skill_remote_001"}""");

        var result = await pendingTask;
        Assert.Equal("skill_remote_001", result.Id);
        Assert.Equal("D:/Exports/skill_remote_001", result.Path);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("skills/remote/export", request.RootElement.GetProperty("method").GetString());
        Assert.Equal("skill_remote_001", request.RootElement.GetProperty("params").GetProperty("hazelnutId").GetString());
    }

    [Fact]
    public async Task ListPluginsAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ListPluginsAsync(
            new ControlPlanePluginCatalogQuery
            {
                WorkingDirectories = ["D:/Work/TianShu"],
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "marketplaces": [
                {
                  "name": "debug",
                  "marketplacePath": "D:/marketplace/marketplace.json",
                  "plugins": [
                    {
                      "id": "demo-plugin@debug",
                      "name": "demo-plugin",
                      "source": {
                        "type": "local",
                        "path": "D:/marketplace/demo-plugin"
                      },
                      "installed": true,
                      "enabled": true,
                      "installPolicy": "AVAILABLE",
                      "authPolicy": "ON_INSTALL"
                    }
                  ]
                }
              ],
              "remoteSyncError": "sync timeout"
            }
            """);

        var result = await pendingTask;
        var marketplace = Assert.Single(result.Marketplaces);
        Assert.Equal("debug", marketplace.Name);
        Assert.Equal("D:/marketplace/marketplace.json", marketplace.Path);
        var plugin = Assert.Single(marketplace.Plugins);
        Assert.Equal("demo-plugin", plugin.Name);
        Assert.True(plugin.Enabled);
        Assert.Equal("D:/marketplace/demo-plugin", ReadStructuredString(plugin.Source, "path"));
        Assert.Equal("sync timeout", result.RemoteSyncError);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("plugin/list", request.RootElement.GetProperty("method").GetString());
        Assert.Equal("D:/Work/TianShu", request.RootElement.GetProperty("params").GetProperty("cwds")[0].GetString());
    }

    [Fact]
    public async Task ListPluginsAsync_WithForceRemoteSync_WritesExpectedPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ListPluginsAsync(
            new ControlPlanePluginCatalogQuery
            {
                WorkingDirectories = ["D:/Work/TianShu"],
                ForceRemoteSync = true,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"marketplaces":[],"remoteSyncError":"forceRemoteSync not implemented"}""");

        var result = await pendingTask;
        Assert.Equal("forceRemoteSync not implemented", result.RemoteSyncError);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("plugin/list", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("D:/Work/TianShu", parameters.GetProperty("cwds")[0].GetString());
        Assert.True(parameters.GetProperty("forceRemoteSync").GetBoolean());
    }

    [Fact]
    public async Task UninstallPluginAsync_WritesExpectedMethodAndPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.UninstallPluginAsync(
            new ControlPlanePluginUninstallCommand
            {
                PluginId = "sample@debug",
                WorkingDirectory = "D:/Work/TianShu",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{}""");

        _ = await pendingTask;

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("plugin/uninstall", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("sample@debug", parameters.GetProperty("pluginId").GetString());
        Assert.Equal("D:/Work/TianShu", parameters.GetProperty("cwd").GetString());
    }

    [Fact]
    public async Task ReadPluginAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ReadPluginAsync(
            new ControlPlanePluginReadQuery
            {
                MarketplacePath = "D:/marketplace/marketplace.json",
                PluginName = "demo-plugin",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "plugin": {
                "marketplaceName": "debug",
                "marketplacePath": "D:/marketplace/marketplace.json",
                "summary": {
                  "id": "demo-plugin@debug",
                  "name": "demo-plugin",
                  "source": {
                    "type": "local",
                    "path": "D:/marketplace/demo-plugin"
                  },
                  "installed": true,
                  "enabled": true,
                  "installPolicy": "AVAILABLE",
                  "authPolicy": "ON_INSTALL"
                },
                "description": "demo description",
                "skills": [
                  {
                    "name": "demo-plugin:search",
                    "description": "search skill",
                    "path": "D:/marketplace/demo-plugin/skills/search"
                  }
                ],
                "apps": [
                  {
                    "id": "connector_example",
                    "name": "connector_example",
                    "installUrl": "https://chatgpt.com/apps/connector_example/connector_example"
                  }
                ],
                "mcpServers": [
                  "demo"
                ]
              }
            }
            """);

        var result = await pendingTask;
        Assert.NotNull(result.Plugin);
        Assert.Equal("debug", result.Plugin!.MarketplaceName);
        Assert.Equal("demo-plugin", result.Plugin.Summary.Name);
        Assert.Equal("demo-plugin@debug", result.Plugin.Summary.Id);
        Assert.Equal("D:/marketplace/demo-plugin", ReadStructuredString(result.Plugin.Summary.Source, "path"));
        Assert.Equal("demo description", result.Plugin.Description);
        Assert.Equal("demo-plugin:search", Assert.Single(result.Plugin.Skills).Name);
        Assert.Equal("connector_example", Assert.Single(result.Plugin.Apps).Id);
        Assert.Equal("demo", Assert.Single(result.Plugin.McpServers));

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("plugin/read", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("D:/marketplace/marketplace.json", parameters.GetProperty("marketplacePath").GetString());
        Assert.Equal("demo-plugin", parameters.GetProperty("pluginName").GetString());
    }

    [Fact]
    public async Task InstallPluginAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.InstallPluginAsync(
            new ControlPlanePluginInstallCommand
            {
                MarketplacePath = "D:/marketplace/marketplace.json",
                PluginName = "demo-plugin",
                WorkingDirectory = "D:/Work/TianShu",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "authPolicy": "ON_INSTALL",
              "appsNeedingAuth": [
                {
                  "id": "connector_example",
                  "name": "connector_example",
                  "installUrl": "https://chatgpt.com/apps/connector_example/connector_example"
                }
              ]
            }
            """);

        var result = await pendingTask;
        Assert.Equal("ON_INSTALL", result.AuthPolicy);
        var app = Assert.Single(result.AppsNeedingAuth);
        Assert.Equal("connector_example", app.Id);
        Assert.Equal("https://chatgpt.com/apps/connector_example/connector_example", app.InstallUrl);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("plugin/install", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("D:/marketplace/marketplace.json", parameters.GetProperty("marketplacePath").GetString());
        Assert.Equal("demo-plugin", parameters.GetProperty("pluginName").GetString());
        Assert.Equal("D:/Work/TianShu", parameters.GetProperty("cwd").GetString());
    }

    [Fact]
    public async Task UploadFeedbackAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.UploadFeedbackAsync(
            new ControlPlaneFeedbackUploadCommand
            {
                Classification = "bug",
                IncludeLogs = true,
                ThreadId = "thread_feedback_runtime",
                Reason = "details",
                ExtraLogFiles = ["D:/logs/a.log", "D:/logs/b.log"],
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"threadId":"feedback_runtime_001"}""");

        var result = await pendingTask;
        Assert.Equal("feedback_runtime_001", result.ThreadId);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("feedback/upload", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("bug", parameters.GetProperty("classification").GetString());
        Assert.True(parameters.GetProperty("includeLogs").GetBoolean());
        Assert.Equal("thread_feedback_runtime", parameters.GetProperty("threadId").GetString());
        Assert.Equal("details", parameters.GetProperty("reason").GetString());
        var extraLogFiles = parameters.GetProperty("extraLogFiles").EnumerateArray().Select(static item => item.GetString()).ToArray();
        Assert.Collection(
            extraLogFiles,
            item => Assert.Equal("D:/logs/a.log", item),
            item => Assert.Equal("D:/logs/b.log", item));
    }

    [Fact]
    public async Task StartCommandExecutionAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.StartCommandExecutionAsync(
            new ControlPlaneCommandExecutionStartCommand
            {
                WorkingDirectory = @"D:\Work\TianShu",
                CommandArgs = ["cmd.exe", "/c", "echo hello"],
                ProcessId = "proc_cmd_001",
                Tty = true,
                Size = new ControlPlaneCommandExecutionTerminalSize
                {
                    Rows = 24,
                    Cols = 80,
                },
                StreamStdin = true,
                StreamStdoutStderr = true,
                Background = true,
                DisableTimeout = true,
                TimeoutMs = 5000,
                DisableOutputCap = true,
                OutputBytesCap = 4096,
                ThreadId = new ThreadId("thread_cmd_001"),
                TurnId = new TurnId("turn_cmd_001"),
                ItemId = "item_cmd_001",
                ApprovalPolicy = "on-request",
                Approved = true,
                Login = true,
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["DEMO"] = "1",
                },
                Sandbox = StructuredValue.FromPlainObject(
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["mode"] = "workspace-write",
                    }),
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"started":true,"processId":"proc_cmd_001","pid":4321}""");

        var result = await pendingTask;
        Assert.True(result.Started);
        Assert.Equal("proc_cmd_001", result.ProcessId);
        Assert.Equal(4321, result.Pid);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("command/exec", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal(@"D:\Work\TianShu", parameters.GetProperty("cwd").GetString());
        var command = parameters.GetProperty("command").EnumerateArray().Select(static item => item.GetString()).ToArray();
        Assert.Collection(
            command,
            item => Assert.Equal("cmd.exe", item),
            item => Assert.Equal("/c", item),
            item => Assert.Equal("echo hello", item));
        Assert.Equal("proc_cmd_001", parameters.GetProperty("processId").GetString());
        Assert.True(parameters.GetProperty("tty").GetBoolean());
        Assert.Equal(24, parameters.GetProperty("size").GetProperty("rows").GetInt32());
        Assert.Equal(80, parameters.GetProperty("size").GetProperty("cols").GetInt32());
        Assert.True(parameters.GetProperty("streamStdin").GetBoolean());
        Assert.True(parameters.GetProperty("streamStdoutStderr").GetBoolean());
        Assert.True(parameters.GetProperty("background").GetBoolean());
        Assert.True(parameters.GetProperty("disableTimeout").GetBoolean());
        Assert.Equal(5000, parameters.GetProperty("timeoutMs").GetInt32());
        Assert.True(parameters.GetProperty("disableOutputCap").GetBoolean());
        Assert.Equal(4096, parameters.GetProperty("outputBytesCap").GetInt32());
        Assert.Equal("thread_cmd_001", parameters.GetProperty("threadId").GetString());
        Assert.Equal("turn_cmd_001", parameters.GetProperty("turnId").GetString());
        Assert.Equal("item_cmd_001", parameters.GetProperty("itemId").GetString());
        Assert.Equal("on-request", parameters.GetProperty("approvalPolicy").GetString());
        Assert.True(parameters.GetProperty("approved").GetBoolean());
        Assert.True(parameters.GetProperty("login").GetBoolean());
        Assert.Equal("1", parameters.GetProperty("env").GetProperty("DEMO").GetString());
        Assert.Equal("workspace-write", parameters.GetProperty("sandbox").GetProperty("mode").GetString());
    }

    [Fact]
    public async Task WriteCommandExecutionAsync_WritesExpectedMethod()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.WriteCommandExecutionAsync(
            new ControlPlaneCommandExecutionWriteCommand
            {
                ProcessId = "proc_cmd_002",
                DeltaBase64 = "aGVsbG8=",
                CloseStdin = true,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, "{}");

        _ = await pendingTask;

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("command/exec/write", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("proc_cmd_002", parameters.GetProperty("processId").GetString());
        Assert.Equal("aGVsbG8=", parameters.GetProperty("deltaBase64").GetString());
        Assert.True(parameters.GetProperty("closeStdin").GetBoolean());
    }

    [Fact]
    public async Task TerminateCommandExecutionAsync_WritesExpectedMethod()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.TerminateCommandExecutionAsync(
            new ControlPlaneCommandExecutionTerminateCommand
            {
                ProcessId = "proc_cmd_003",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, "{}");

        _ = await pendingTask;

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("command/exec/terminate", request.RootElement.GetProperty("method").GetString());
        Assert.Equal("proc_cmd_003", request.RootElement.GetProperty("params").GetProperty("processId").GetString());
    }

    [Fact]
    public async Task ResizeCommandExecutionAsync_WritesExpectedMethod()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ResizeCommandExecutionAsync(
            new ControlPlaneCommandExecutionResizeCommand
            {
                ProcessId = "proc_cmd_004",
                Size = new ControlPlaneCommandExecutionTerminalSize
                {
                    Rows = 40,
                    Cols = 120,
                },
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, "{}");

        _ = await pendingTask;

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("command/exec/resize", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("proc_cmd_004", parameters.GetProperty("processId").GetString());
        Assert.Equal(40, parameters.GetProperty("size").GetProperty("rows").GetInt32());
        Assert.Equal(120, parameters.GetProperty("size").GetProperty("cols").GetInt32());
    }

    [Fact]
    public async Task StartWindowsSandboxSetupAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.StartWindowsSandboxSetupAsync(
            new ControlPlaneWindowsSandboxSetupStartCommand
            {
                Mode = WindowsSandboxSetupMode.Elevated,
                WorkingDirectory = @"D:\Work\TianShu",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{"started":true}""");

        var result = await pendingTask;
        Assert.True(result.Started);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("windowsSandbox/setupStart", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("elevated", parameters.GetProperty("mode").GetString());
        Assert.Equal(@"D:\Work\TianShu", parameters.GetProperty("cwd").GetString());
    }

    [Fact]
    public async Task SearchFuzzyFilesAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.SearchFuzzyFilesAsync(
            new ControlPlaneFuzzyFileSearchQuery
            {
                Query = "TianShuExecutionRuntime",
                WorkingDirectory = @"D:\Work\TianShu",
                Limit = 7,
                Roots = ["src", "tests"],
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "files": [
                {
                  "path": "src/TianShu.Execution.Runtime/TianShuExecutionRuntime.cs",
                  "fileName": "TianShuExecutionRuntime.cs"
                }
              ]
            }
            """);

        var result = await pendingTask;
        var file = Assert.Single(result.Files);
        Assert.Equal("src/TianShu.Execution.Runtime/TianShuExecutionRuntime.cs", file.Path);
        Assert.Equal("TianShuExecutionRuntime.cs", file.FileName);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("fuzzyFileSearch", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("TianShuExecutionRuntime", parameters.GetProperty("query").GetString());
        Assert.Equal(@"D:\Work\TianShu", parameters.GetProperty("cwd").GetString());
        Assert.Equal(7, parameters.GetProperty("limit").GetInt32());
        Assert.Equal(
            ["src", "tests"],
            parameters.GetProperty("roots").EnumerateArray().Select(static item => item.GetString()).ToArray());
    }

    [Fact]
    public async Task StartFuzzyFileSearchSessionAsync_WritesExpectedMethod()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.StartFuzzyFileSearchSessionAsync(
            new ControlPlaneStartFuzzyFileSearchSessionCommand
            {
                SessionId = "session-fuzzy-001",
                Roots = ["src"],
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, "{}");

        var result = await pendingTask;
        Assert.NotNull(result);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("fuzzyFileSearch/sessionStart", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("session-fuzzy-001", parameters.GetProperty("sessionId").GetString());
        Assert.Equal(["src"], parameters.GetProperty("roots").EnumerateArray().Select(static item => item.GetString()).ToArray());
    }

    [Fact]
    public async Task UpdateFuzzyFileSearchSessionAsync_WritesExpectedMethod()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.UpdateFuzzyFileSearchSessionAsync(
            new ControlPlaneUpdateFuzzyFileSearchSessionCommand
            {
                SessionId = "session-fuzzy-001",
                Query = "Kernel",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, "{}");

        var result = await pendingTask;
        Assert.NotNull(result);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("fuzzyFileSearch/sessionUpdate", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("session-fuzzy-001", parameters.GetProperty("sessionId").GetString());
        Assert.Equal("Kernel", parameters.GetProperty("query").GetString());
    }

    [Fact]
    public async Task StopFuzzyFileSearchSessionAsync_WritesExpectedMethod()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.StopFuzzyFileSearchSessionAsync(
            new ControlPlaneStopFuzzyFileSearchSessionCommand
            {
                SessionId = "session-fuzzy-001",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, "{}");

        var result = await pendingTask;
        Assert.NotNull(result);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("fuzzyFileSearch/sessionStop", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("session-fuzzy-001", parameters.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task RegisterAgentThreadAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.RegisterAgentThreadAsync(
            new ControlPlaneRegisterAgentThreadCommand
            {
                ThreadId = new ThreadId("thread_agent_001"),
                AgentNickname = "demo-agent",
                AgentRole = "reviewer",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread_agent_001",
                "preview": "agent registered",
                "cwd": "D:/Work/TianShu",
                "updatedAt": 1711094400,
                "agentNickname": "demo-agent",
                "agentRole": "reviewer"
              }
            }
            """);

        var result = await pendingTask;
        Assert.Equal("thread_agent_001", result.Agent?.ThreadId.Value);
        Assert.Equal("demo-agent", result.Agent?.AgentNickname);
        Assert.Equal("reviewer", result.Agent?.AgentRole);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("agent/thread/register", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread_agent_001", parameters.GetProperty("threadId").GetString());
        Assert.Equal("demo-agent", parameters.GetProperty("agentNickname").GetString());
        Assert.Equal("reviewer", parameters.GetProperty("agentRole").GetString());
    }

    [Fact]
    public async Task CreateAgentJobAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.CreateAgentJobAsync(
            new ControlPlaneCreateJobCommand
            {
                JobId = new JobId("job_protocol_001"),
                Name = "demo-job",
                Instruction = "analyze items",
                InputHeaders = ControlPlaneStructuredValue.FromPlainObject(new Dictionary<string, object?> { ["columns"] = new[] { "title" } }),
                InputCsvPath = "D:/data/input.csv",
                OutputCsvPath = "D:/data/output.csv",
                AutoExport = false,
                OutputSchema = ControlPlaneStructuredValue.FromPlainObject(new Dictionary<string, object?> { ["type"] = "object" }),
                Items =
                [
                    ControlPlaneStructuredValue.FromPlainObject(new Dictionary<string, object?> { ["itemId"] = "item-1", ["sourceId"] = "src-1" }),
                ],
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "job": {
                "id": "job_protocol_001",
                "name": "demo-job",
                "status": "pending",
                "instruction": "analyze items"
              },
              "items": [
                {
                  "itemId": "item-1",
                  "sourceId": "src-1",
                  "assignedThreadId": "thread-a",
                  "status": "pending"
                }
              ]
            }
            """);

        var result = await pendingTask;
        Assert.Equal("job_protocol_001", result.Job?.Id.Value);
        Assert.Equal("pending", result.Job?.Status);
        var item = Assert.Single(result.Items);
        Assert.Equal("item-1", item.ItemId.Value);
        Assert.Equal("thread-a", item.AssignedThreadId?.Value);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("agent/job/create", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("job_protocol_001", parameters.GetProperty("jobId").GetString());
        Assert.Equal("demo-job", parameters.GetProperty("name").GetString());
        Assert.Equal("analyze items", parameters.GetProperty("instruction").GetString());
        Assert.Equal("title", parameters.GetProperty("inputHeaders").GetProperty("columns")[0].GetString());
        Assert.Equal("D:/data/input.csv", parameters.GetProperty("inputCsvPath").GetString());
        Assert.Equal("D:/data/output.csv", parameters.GetProperty("outputCsvPath").GetString());
        Assert.False(parameters.GetProperty("autoExport").GetBoolean());
        Assert.Equal("object", parameters.GetProperty("outputSchema").GetProperty("type").GetString());
        Assert.Equal("item-1", parameters.GetProperty("items")[0].GetProperty("itemId").GetString());
    }

    [Fact]
    public async Task DispatchAgentJobAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.DispatchAgentJobAsync(
            new ControlPlaneDispatchJobCommand
            {
                JobId = new JobId("job_protocol_002"),
                ThreadIds = [new ThreadId("thread-a"), new ThreadId("THREAD-A"), new ThreadId("thread-b")],
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "items": [
                {
                  "itemId": "item-1",
                  "assignedThreadId": "thread-a",
                  "status": "running"
                },
                {
                  "itemId": "item-2",
                  "assignedThreadId": "thread-b",
                  "status": "running"
                }
              ]
            }
            """);

        var result = await pendingTask;
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("thread-a", result.Items[0].AssignedThreadId?.Value);
        Assert.Equal("thread-b", result.Items[1].AssignedThreadId?.Value);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("agent/job/dispatch", request.RootElement.GetProperty("method").GetString());
        var threadIds = request.RootElement.GetProperty("params").GetProperty("threadIds").EnumerateArray().Select(static item => item.GetString()).ToArray();
        Assert.Equal(new[] { "thread-a", "thread-b" }, threadIds);
    }

    [Fact]
    public async Task ReportAgentJobItemAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ReportAgentJobItemAsync(
            new ControlPlaneReportJobItemCommand
            {
                JobId = new JobId("job_protocol_003"),
                ItemId = new JobItemId("item-1"),
                Status = "completed",
                Result = ControlPlaneStructuredValue.FromPlainObject(new Dictionary<string, object?> { ["score"] = 99 }),
                LastError = "none",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "job": {
                "id": "job_protocol_003",
                "name": "demo-job",
                "status": "running",
                "instruction": "analyze items"
              },
              "item": {
                "itemId": "item-1",
                "status": "completed",
                "lastError": "none",
                "resultJson": "{\"score\":99}"
              }
            }
            """);

        var result = await pendingTask;
        Assert.Equal("job_protocol_003", result.Job?.Id.Value);
        Assert.Equal("item-1", result.Item?.ItemId.Value);
        Assert.Equal("completed", result.Item?.Status);
        Assert.NotNull(result.Item?.Result);
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Item.Result.ToPlainObject())))
        {
            Assert.Equal(99, document.RootElement.GetProperty("score").GetInt32());
        }

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("agent/job/item/report", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("job_protocol_003", parameters.GetProperty("jobId").GetString());
        Assert.Equal("item-1", parameters.GetProperty("itemId").GetString());
        Assert.Equal("completed", parameters.GetProperty("status").GetString());
        Assert.Equal(99, parameters.GetProperty("result").GetProperty("score").GetInt32());
        Assert.Equal("none", parameters.GetProperty("lastError").GetString());
    }

    [Fact]
    public async Task ReadAgentJobAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ReadAgentJobAsync(
            new ControlPlaneReadJobQuery
            {
                JobId = new JobId("job_protocol_004"),
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "job": {
                "id": "job_protocol_004",
                "name": "demo-job",
                "status": "completed",
                "instruction": "analyze items"
              },
              "items": [
                {
                  "itemId": "item-1",
                  "assignedThreadId": "thread-a",
                  "status": "completed",
                  "resultJson": "{\"score\":99}"
                }
              ]
            }
            """);

        var result = await pendingTask;
        Assert.Equal("job_protocol_004", result.Job?.Id.Value);
        var item = Assert.Single(result.Items);
        Assert.Equal("item-1", item.ItemId.Value);
        Assert.Equal("thread-a", item.AssignedThreadId?.Value);
        Assert.NotNull(item.Result);
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(item.Result.ToPlainObject())))
        {
            Assert.Equal(99, document.RootElement.GetProperty("score").GetInt32());
        }

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("agent/job/read", request.RootElement.GetProperty("method").GetString());
        Assert.Equal("job_protocol_004", request.RootElement.GetProperty("params").GetProperty("jobId").GetString());
    }

    [Fact]
    public async Task ListAppsAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ListAppsAsync(
            new ControlPlaneAppCatalogQuery
            {
                Limit = 10,
                Cursor = "cursor_app_001",
                ThreadId = new ThreadId("thread_app_001"),
                ForceRefetch = true,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "data": [
                {
                  "id": "connector_example",
                  "name": "Example Connector",
                  "description": "demo connector",
                  "logoUrl": "https://example.com/logo-light.png",
                  "logoUrlDark": "https://example.com/logo-dark.png",
                  "distributionChannel": "directory",
                  "branding": {
                    "category": "productivity",
                    "developer": "TianShu",
                    "isDiscoverableApp": true,
                    "privacyPolicy": "https://example.com/privacy",
                    "termsOfService": "https://example.com/terms",
                    "website": "https://example.com"
                  },
                  "appMetadata": {
                    "categories": ["productivity"],
                    "developer": "TianShu",
                    "firstPartyRequiresInstall": false,
                    "firstPartyType": "third_party",
                    "review": {
                      "status": "approved"
                    },
                    "screenshots": [
                      {
                        "fileId": "file_app_001",
                        "url": "https://example.com/screenshot.png",
                        "userPrompt": "show me the UI"
                      }
                    ],
                    "seoDescription": "connector seo",
                    "showInComposerWhenUnlinked": true,
                    "subCategories": ["search"],
                    "version": "1.0.0",
                    "versionId": "ver_app_001",
                    "versionNotes": "initial release"
                  },
                  "labels": {
                    "tier": "beta"
                  },
                  "installUrl": "https://chatgpt.com/apps/connector_example/connector_example",
                  "isAccessible": true,
                  "isEnabled": false,
                  "pluginDisplayNames": ["demo-plugin"]
                }
              ],
              "nextCursor": "cursor_app_002"
            }
            """);

        var result = await pendingTask;
        Assert.Equal("cursor_app_002", result.NextCursor);
        var app = Assert.Single(result.Items);
        Assert.Equal("connector_example", app.Id);
        Assert.Equal("Example Connector", app.Name);
        Assert.Equal("directory", app.DistributionChannel);
        Assert.Equal("beta", Assert.Single(app.Labels).Value);
        Assert.True(app.IsAccessible);
        Assert.False(app.IsEnabled);
        Assert.Equal("demo-plugin", Assert.Single(app.PluginDisplayNames));
        Assert.NotNull(app.Branding);
        Assert.Equal("productivity", ReadStructuredString(app.Branding, "category"));
        Assert.True(ReadStructuredBoolean(app.Branding, "isDiscoverableApp"));
        Assert.NotNull(app.Metadata);
        Assert.Equal("approved", ReadStructuredString(app.Metadata, "review", "status"));
        Assert.Equal("show me the UI", ReadStructuredString(app.Metadata, "screenshots", 0, "userPrompt"));

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("app/list", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal(10, parameters.GetProperty("limit").GetInt32());
        Assert.Equal("cursor_app_001", parameters.GetProperty("cursor").GetString());
        Assert.Equal("thread_app_001", parameters.GetProperty("threadId").GetString());
        Assert.True(parameters.GetProperty("forceRefetch").GetBoolean());
    }

    [Fact]
    public async Task GetGitDiffToRemoteAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.GetGitDiffToRemoteAsync(
            new ControlPlaneGitDiffArtifactQuery
            {
                ThreadId = new ThreadId("thread_diff_001"),
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "diff": "diff --git a/foo.txt b/foo.txt\n+new line\n",
              "hasChanges": true
            }
            """);

        var result = await pendingTask;
        Assert.True(result.HasChanges);
        Assert.Contains("diff --git", result.Diff, StringComparison.Ordinal);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("artifact/gitdifftoremote/read", request.RootElement.GetProperty("method").GetString());
        Assert.Equal("thread_diff_001", request.RootElement.GetProperty("params").GetProperty("threadId").GetString());
    }

    [Fact]
    public async Task StartReviewAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.StartReviewAsync(
            new ControlPlaneReviewStartCommand
            {
                ThreadId = "thread-review-001",
                Delivery = "detached",
                Target = new ControlPlaneReviewCommitTarget
                {
                    Sha = "abc123def",
                    Title = "修复命令链路",
                },
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "reviewThreadId": "review-thread-001",
              "turn": {
                "id": "turn-review-001",
                "status": "inProgress",
                "items": [
                  {
                    "id": "review-item-001",
                    "type": "assistant_message",
                    "text": "请审查提交 abc123def。"
                  }
                ]
              }
            }
            """);

        var result = await pendingTask;
        Assert.Equal("review-thread-001", result.ReviewThreadId);
        Assert.NotNull(result.Turn);
        Assert.Equal("turn-review-001", result.Turn!.Id);
        Assert.Equal("inProgress", result.Turn.Status);
        Assert.Equal("请审查提交 abc123def。", result.Turn.DisplayText);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("review/start", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-review-001", parameters.GetProperty("threadId").GetString());
        Assert.Equal("detached", parameters.GetProperty("delivery").GetString());
        var target = parameters.GetProperty("target");
        Assert.Equal("commit", target.GetProperty("type").GetString());
        Assert.Equal("abc123def", target.GetProperty("sha").GetString());
        Assert.Equal("修复命令链路", target.GetProperty("title").GetString());
    }

    [Fact]
    public async Task StartReviewAsync_RegistersSubmittedTurnId_ForInterruptBeforeTurnStarted()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var reviewTask = runtime.StartReviewAsync(
            new ControlPlaneReviewStartCommand
            {
                ThreadId = "thread-review-interrupt-001",
                Delivery = "detached",
                Target = new ControlPlaneReviewUncommittedChangesTarget(),
            },
            CancellationToken.None);
        var reviewRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            reviewRequestId,
            """
            {
              "reviewThreadId": "review-thread-interrupt-001",
              "turn": {
                "id": "turn-review-interrupt-001",
                "status": "inProgress"
              }
            }
            """);

        var reviewResult = await reviewTask;
        Assert.Equal("review-thread-interrupt-001", reviewResult.ReviewThreadId);
        Assert.Equal("thread-review-interrupt-001", ReflectionTestHelper.GetField(runtime, "activeThreadId"));
        Assert.Null(ReflectionTestHelper.GetField(runtime, "activeTurnId"));
        Assert.Equal("turn-review-interrupt-001", ReflectionTestHelper.GetField(runtime, "submittedTurnId"));
        Assert.True(runtime.HasActiveTurn);

        var interruptTask = runtime.InterruptAsync(CancellationToken.None);
        var interruptRequestId = await WaitForPendingResponseIdAsync(runtime, reviewRequestId);
        CompletePendingResponse(runtime, interruptRequestId, "{}");
        await interruptTask;

        var requests = await ReadRequestDocumentsAsync(stream);
        Assert.Equal(2, requests.Count);
        Assert.Equal("review/start", requests[0].RootElement.GetProperty("method").GetString());
        Assert.Equal("turn/interrupt", requests[1].RootElement.GetProperty("method").GetString());
        var interruptParameters = requests[1].RootElement.GetProperty("params");
        Assert.Equal("thread-review-interrupt-001", interruptParameters.GetProperty("threadId").GetString());
        Assert.Equal("turn-review-interrupt-001", interruptParameters.GetProperty("turnId").GetString());

        foreach (var request in requests)
        {
            request.Dispose();
        }
    }

    [Fact]
    public async Task StartReviewAsync_WhenTurnStartedNotificationArrives_AdoptsActiveTurnAndClearsSubmittedTurnId()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var reviewTask = runtime.StartReviewAsync(
            new ControlPlaneReviewStartCommand
            {
                ThreadId = "thread-review-started-001",
                Delivery = "detached",
                Target = new ControlPlaneReviewUncommittedChangesTarget(),
            },
            CancellationToken.None);
        var reviewRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            reviewRequestId,
            """
            {
              "reviewThreadId": "review-thread-started-001",
              "turn": {
                "id": "turn-review-started-001",
                "status": "inProgress"
              }
            }
            """);

        await reviewTask;
        Assert.Equal("turn-review-started-001", ReflectionTestHelper.GetField(runtime, "submittedTurnId"));

        const string turnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-review-started-001","turn":{"id":"turn-review-started-001","status":"inProgress"}}}
        """;
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(turnStarted), turnStarted);

        var startedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.TurnStarted));
        Assert.Equal("thread-review-started-001", startedEvent.ThreadId?.Value);
        Assert.Equal("turn-review-started-001", startedEvent.TurnId?.Value);
        Assert.Null(startedEvent.PayloadKind);
        Assert.Null(startedEvent.Payload);
        Assert.Equal("turn-review-started-001", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
        Assert.Null(ReflectionTestHelper.GetField(runtime, "submittedTurnId"));
    }

    [Fact]
    public async Task SendFollowUpAsync_WhenModeIsInterrupt_UsesSubmittedTurnIdBeforeReviewStartedNotification()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: "thread-review-followup-001", activeTurnId: null);
        using var streamHandle = stream;
        using var writerHandle = writer;
        ReflectionTestHelper.SetField(runtime, "submittedTurnId", "turn-review-followup-001");

        var pendingTask = runtime.SendFollowUpAsync("中断并重新开始 review", FollowUpMode.Interrupt, CancellationToken.None);
        var interruptRequestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, interruptRequestId, "{}");

        await Task.Delay(100);
        var interruptOnlyRequests = await ReadRequestDocumentsAsync(stream);
        Assert.Single(interruptOnlyRequests);
        Assert.Equal("turn/interrupt", interruptOnlyRequests[0].RootElement.GetProperty("method").GetString());
        var interruptParameters = interruptOnlyRequests[0].RootElement.GetProperty("params");
        Assert.Equal("thread-review-followup-001", interruptParameters.GetProperty("threadId").GetString());
        Assert.Equal("turn-review-followup-001", interruptParameters.GetProperty("turnId").GetString());
        interruptOnlyRequests[0].Dispose();
        stream.SetLength(0);
        stream.Position = 0;

        ReflectionTestHelper.InvokeMethod(runtime, "CompleteTurn", "turn-review-followup-001", true, "interrupted", null, null, """{"status":"interrupted"}""");
        var startRequestId = await WaitForPendingResponseIdAsync(runtime, interruptRequestId);
        CompletePendingResponse(runtime, startRequestId, """{"turn":{"id":"turn-review-followup-restart-001","status":"inProgress"}}""");

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.Equal("turn-review-followup-restart-001", result.TurnId);

        var requests = await ReadRequestDocumentsAsync(stream);
        Assert.Single(requests);
        Assert.Equal("turn/start", requests[0].RootElement.GetProperty("method").GetString());
        requests[0].Dispose();
    }

    [Fact]
    public async Task ListExperimentalFeaturesAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ListExperimentalFeaturesAsync(
            new ControlPlaneExperimentalFeatureQuery
            {
                Limit = 7,
                Cursor = "cursor_14",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "data": [
                {
                  "name": "tool_search",
                  "stage": "beta",
                  "displayName": "Tool Search",
                  "description": "搜索工具能力",
                  "announcement": "Enabled for testing",
                  "enabled": true,
                  "defaultEnabled": false
                }
              ],
              "nextCursor": "cursor_15"
            }
            """);

        var result = await pendingTask;
        var item = Assert.Single(result.Items);
        Assert.Equal("tool_search", item.Name);
        Assert.Equal("beta", item.Stage);
        Assert.Equal("Tool Search", item.DisplayName);
        Assert.Equal("搜索工具能力", item.Description);
        Assert.True(item.Enabled);
        Assert.False(item.DefaultEnabled);
        Assert.Equal("cursor_15", result.NextCursor);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("experimentalfeature/list", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal(7, parameters.GetProperty("limit").GetInt32());
        Assert.Equal("cursor_14", parameters.GetProperty("cursor").GetString());
    }

    [Fact]
    public async Task ListCollaborationModesAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ListCollaborationModesAsync(CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "data": [
                {
                  "name": "plan",
                  "mode": "plan",
                  "model": "gpt-5.4",
                  "reasoningEffort": "high"
                }
              ]
            }
            """);

        var result = await pendingTask;
        var item = Assert.Single(result.Items);
        Assert.Equal("plan", item.Name);
        Assert.Equal("plan", item.Mode);
        Assert.Equal("gpt-5.4", item.Model);
        Assert.Equal("high", item.ReasoningEffort);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("collaborationmode/list", request.RootElement.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Object, request.RootElement.GetProperty("params").ValueKind);
        Assert.Empty(request.RootElement.GetProperty("params").EnumerateObject());
    }

    [Fact]
    public async Task ListMcpServerStatusAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ListMcpServerStatusAsync(
            new ControlPlaneMcpServerStatusQuery
            {
                Limit = 2,
                Cursor = "cursor_mcp_01",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "data": [
                {
                  "name": "demo",
                  "authStatus": "authorized",
                  "tools": {
                    "search": {},
                    "read": {}
                  },
                  "resources": [
                    {
                      "uri": "resource://demo/index"
                    }
                  ],
                  "resourceTemplates": [
                    {
                      "uriTemplate": "resource://demo/{id}"
                    }
                  ]
                }
              ],
              "nextCursor": "cursor_mcp_02"
            }
            """);

        var result = await pendingTask;
        var item = Assert.Single(result.Items);
        Assert.Equal("demo", item.Name);
        Assert.Equal("authorized", item.AuthStatus);
        Assert.Equal(["search", "read"], item.ToolNames);
        Assert.Equal("resource://demo/index", Assert.Single(item.ResourceUris));
        Assert.Equal("resource://demo/{id}", Assert.Single(item.ResourceTemplateUris));
        Assert.Equal("cursor_mcp_02", result.NextCursor);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("mcpserverstatus/list", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal(2, parameters.GetProperty("limit").GetInt32());
        Assert.Equal("cursor_mcp_01", parameters.GetProperty("cursor").GetString());
    }

    [Fact]
    public async Task ReloadMcpServersAsync_WritesExpectedMethod()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ReloadMcpServersAsync(new ControlPlaneMcpServerReloadCommand(), CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, "{}");

        _ = await pendingTask;

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("config/mcpserver/reload", request.RootElement.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Object, request.RootElement.GetProperty("params").ValueKind);
        Assert.Empty(request.RootElement.GetProperty("params").EnumerateObject());
    }

    [Fact]
    public async Task ReloadProviderPackagesAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ReloadProviderPackagesAsync(new ControlPlaneProviderPackageReloadCommand(), CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "loadedAssemblyCount": 2,
              "issueCount": 1,
              "supportedProtocolAdapterIds": ["openai_responses", "demo_adapter"],
              "supportedWireApis": ["responses"],
              "issues": ["demo issue"]
            }
            """);

        var result = await pendingTask;

        Assert.Equal(2, result.LoadedAssemblyCount);
        Assert.Equal(1, result.IssueCount);
        Assert.Equal(["openai_responses", "demo_adapter"], result.SupportedProtocolAdapterIds);
        Assert.Equal(["responses"], result.SupportedWireApis);
        Assert.Equal(["demo issue"], result.Issues);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("config/provider/reload", request.RootElement.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Object, request.RootElement.GetProperty("params").ValueKind);
        Assert.Empty(request.RootElement.GetProperty("params").EnumerateObject());
    }

    [Fact]
    public async Task StartMcpServerOauthLoginAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.StartMcpServerOauthLoginAsync(
            new ControlPlaneMcpServerOauthLoginStartCommand
            {
                Name = "demo",
                TimeoutSecs = 15,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "authorizationUrl": "https://example.com/oauth/demo"
            }
            """);

        var result = await pendingTask;
        Assert.Equal("https://example.com/oauth/demo", result.AuthorizationUrl);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("mcpServer/oauth/login", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("demo", parameters.GetProperty("name").GetString());
        Assert.Equal(15, parameters.GetProperty("timeoutSecs").GetInt32());
    }

    [Fact]
    public async Task GetConversationSummaryAsync_WritesExpectedMethod_AndParsesTypedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.GetConversationSummaryAsync(
            new ControlPlaneConversationArtifactQuery
            {
                ThreadId = new ThreadId("thread-summary-001"),
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "summary": {
                "conversationId": "thread-summary-001",
                "path": "Test/cli-acceptance-artifacts/summary.json",
                "preview": "会话摘要验证通过。",
                "timestamp": "2026-03-21T14:20:00Z",
                "updatedAt": "2026-03-21T14:21:00Z",
                "modelProvider": "openai-compatible",
                "cwd": "D:/Work/TianShu",
                "cliVersion": "0.1.0-dev",
                "source": "rollout",
                "gitInfo": {
                  "sha": "abc123",
                  "branch": "main",
                  "originUrl": "https://example.invalid/repo.git"
                }
              }
            }
            """);

        var result = await pendingTask;
        Assert.NotNull(result);
        Assert.Equal("thread-summary-001", result!.ConversationId);
        Assert.Equal("Test/cli-acceptance-artifacts/summary.json", result.Path);
        Assert.Equal("会话摘要验证通过。", result.Preview);
        Assert.Equal("rollout", result.Source);
        Assert.Equal("abc123", result.GitSha);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("artifact/conversationsummary/read", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-summary-001", parameters.GetProperty("threadId").GetString());
        Assert.False(parameters.TryGetProperty("conversationId", out _));
    }

    [Fact]
    public async Task ResumeThreadAsync_WritesExpectedMethod_AndHydratesPendingInputStateFromThreadPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ResumeThreadAsync("thread-resume-001", CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-resume-001",
                "preview": "协议测试线程恢复",
                "cwd": "D:/Work/TianShu",
                "path": "D:/sessions/thread-resume-001.jsonl",
                "ephemeral": true,
                "seedHistory": [
                  {
                    "role": "user",
                    "content": "继续完善 resume typed 链路"
                  }
                ],
                "pendingInputState": {
                  "interruptRequestPending": true,
                  "submitPendingSteersAfterInterrupt": true,
                  "queuedUserMessages": [
                    {
                      "correlationId": "corr-resume-interrupt-001",
                      "requestedMode": "Interrupt",
                      "effectiveMode": "Interrupt",
                      "lifecycleState": "interrupt_requested",
                      "expectedTurnId": "turn-expected-001",
                      "turnId": "turn-interrupt-001",
                      "pendingBucket": "QueuedUserMessage",
                      "compareKey": {
                        "message": "恢复中的中断请求",
                        "imageCount": 0
                      }
                    }
                  ],
                  "entries": [],
                  "pendingSteers": [
                    {
                      "correlationId": "corr-resume-001",
                      "requestedMode": "Steer",
                      "effectiveMode": "Steer",
                      "lifecycleState": "awaiting_commit",
                      "expectedTurnId": "turn-expected-001",
                      "turnId": null,
                      "pendingBucket": "PendingSteer",
                      "compareKey": {
                        "message": "恢复后的引导消息",
                        "imageCount": 0
                      }
                    }
                  ]
                },
                "pendingInteractiveRequests": [
                  {
                    "requestId": 901,
                    "requestKind": "approval_requested",
                    "requestMethod": "item/tool/requestApproval",
                    "callId": "approval-resume-001",
                    "threadId": "thread-resume-001",
                    "turnId": "turn-resume-001",
                    "toolName": "browser",
                    "text": "browser | open https://example.com | mcp_tool_call_requires_approval",
                    "status": "awaitingApproval",
                    "phase": "request_approval",
                    "requiresApproval": true,
                    "availableDecisions": ["accept", "acceptForSession", "decline"],
                    "approvalRequest": {
                      "toolName": "browser",
                      "availableDecisions": ["accept", "acceptForSession", "decline"],
                      "summary": "browser | open https://example.com | mcp_tool_call_requires_approval",
                      "metadataFields": []
                    }
                  },
                  {
                    "requestId": 902,
                    "requestKind": "permission_requested",
                    "requestMethod": "item/permissions/requestApproval",
                    "callId": "permission-resume-001",
                    "threadId": "thread-resume-001",
                    "turnId": "turn-resume-001",
                    "toolName": "request_permissions",
                    "text": "Need broader access | {\"network\":{\"enabled\":true}}",
                    "status": "awaitingPermission",
                    "phase": "request_permission",
                    "permissionRequest": {
                      "reason": "Need broader access",
                      "fields": [],
                      "permissionsJson": "{\"network\":{\"enabled\":true}}",
                      "summary": "Need broader access | {\"network\":{\"enabled\":true}}"
                    }
                  },
                  {
                    "requestId": 903,
                    "requestKind": "request_user_input",
                    "requestMethod": "item/tool/requestUserInput",
                    "callId": "input-resume-001",
                    "threadId": "thread-resume-001",
                    "turnId": "turn-resume-001",
                    "toolName": "requestUserInput",
                    "text": "- 选择配置文件",
                    "status": "awaitingUserInput",
                    "phase": "request_user_input",
                    "userInputRequest": {
                      "summary": "- 选择配置文件",
                      "questions": [
                        {
                          "id": "config_path",
                          "header": "选择配置文件",
                          "prompt": "请选择配置文件",
                          "isSecret": false,
                          "isOther": true,
                          "options": null
                        }
                      ]
                    }
                  }
                ],
                "turns": [
                  {
                    "id": "turn-resume-001",
                    "status": "completed",
                    "items": [
                      {
                        "id": "assistant-item-001",
                        "type": "assistant_message",
                        "text": "已改为优先回放 typed turn history。"
                      }
                    ]
                  }
                ]
              }
            }
            """);

        var result = await pendingTask;
        Assert.NotNull(result);
        Assert.Equal("thread-resume-001", result!.Thread.ThreadId.Value);
        Assert.Equal("D:/sessions/thread-resume-001.jsonl", result.Thread.Path);
        Assert.True(result.Thread.IsEphemeral);
        Assert.NotNull(result.PendingInputState);
        Assert.True(result.PendingInputState!.InterruptRequestPending);
        Assert.True(result.PendingInputState.SubmitPendingSteersAfterInterrupt);
        Assert.Equal(
            "corr-resume-interrupt-001",
            Assert.Single(result.PendingInputState.QueuedUserMessages ?? Array.Empty<ControlPlanePendingInputStateEntry>()).CorrelationId);
        var pendingEntry = Assert.Single(result.PendingInputState.PendingSteers ?? Array.Empty<ControlPlanePendingInputStateEntry>());
        Assert.Equal("corr-resume-001", pendingEntry.CorrelationId);
        Assert.Equal("PendingSteer", pendingEntry.PendingBucket);
        Assert.Null(pendingEntry.CompareKey);
        Assert.Empty(pendingEntry.Inputs ?? Array.Empty<ControlPlaneInputItem>());
        Assert.Equal("继续完善 resume typed 链路", Assert.Single(result.SeedHistory).Content);
        var resumedTurn = Assert.Single(result.Turns);
        Assert.Equal("turn-resume-001", resumedTurn.Id);
        var resumedItem = Assert.Single(resumedTurn.Items);
        Assert.Equal("assistant_message", resumedItem.Type);
        Assert.Equal("已改为优先回放 typed turn history。", resumedItem.Text);
        Assert.Equal(3, result.PendingInteractiveRequests.Count);
        Assert.Contains(
            result.PendingInteractiveRequests,
            static request => request.RequestId == 901 && string.Equals(request.CallId, "approval-resume-001", StringComparison.Ordinal));
        Assert.Contains(
            result.PendingInteractiveRequests,
            static request => request.RequestId == 902 && string.Equals(request.CallId, "permission-resume-001", StringComparison.Ordinal));
        Assert.Contains(
            result.PendingInteractiveRequests,
            static request => request.RequestId == 903 && string.Equals(request.CallId, "input-resume-001", StringComparison.Ordinal));
        var hydratedSnapshot = Assert.IsType<PendingInputStatePayload>(
            ReflectionTestHelper.InvokeMethod(runtime, "GetPendingInputStateSnapshot", "thread-resume-001"));
        Assert.True(hydratedSnapshot.SubmitPendingSteersAfterInterrupt);
        Assert.Contains(
            hydratedSnapshot.QueuedUserMessages ?? Array.Empty<PendingInputStateEntryPayload>(),
            static entry => string.Equals(entry.CorrelationId, "corr-resume-interrupt-001", StringComparison.Ordinal));
        Assert.Equal(
            "corr-resume-001",
            Assert.Single(hydratedSnapshot.PendingSteers ?? Array.Empty<PendingInputStateEntryPayload>()).CorrelationId);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/resume", request.RootElement.GetProperty("method").GetString());
        var resumeRequestId = request.RootElement.GetProperty("id").GetInt64();
        Assert.Equal("thread-resume-001", request.RootElement.GetProperty("params").GetProperty("threadId").GetString());

        stream.SetLength(0);
        stream.Position = 0;

        var permissionResponseTask = runtime.RespondToPermissionRequestAsync(
            new ControlPlanePermissionGrant
            {
                CallId = new CallId("permission-resume-001"),
                Permissions = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["network"] = StructuredValue.FromObject(new Dictionary<string, StructuredValue>
                    {
                        ["enabled"] = StructuredValue.FromBoolean(true),
                    }),
                },
                Scope = ControlPlanePermissionScope.Session,
            },
            CancellationToken.None);
        var permissionRequestId = await WaitForPendingResponseIdAsync(runtime, resumeRequestId);
        CompletePendingResponse(runtime, permissionRequestId, """{"ok":true}""");
        Assert.True(await permissionResponseTask);
        using (var permissionRequest = await ReadSingleRequestAsync(stream))
        {
            Assert.Equal("serverRequest/respond", permissionRequest.RootElement.GetProperty("method").GetString());
            var parameters = permissionRequest.RootElement.GetProperty("params");
            Assert.Equal(902, parameters.GetProperty("requestId").GetInt64());
            Assert.Equal("permission-resume-001", parameters.GetProperty("callId").GetString());
            Assert.Equal("permission_requested", parameters.GetProperty("requestKind").GetString());
            Assert.Equal("session", parameters.GetProperty("result").GetProperty("scope").GetString());
        }

        stream.SetLength(0);
        stream.Position = 0;

        var userInputResponseTask = runtime.RespondToUserInputAsync(
            new ControlPlaneUserInputSubmission
            {
                CallId = new CallId("input-resume-001"),
                Answers = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["config_path"] = StructuredValue.FromString(".tianshu/tianshu.toml"),
                },
            },
            CancellationToken.None);
        var userInputRequestId = await WaitForPendingResponseIdAsync(runtime, resumeRequestId, permissionRequestId);
        CompletePendingResponse(runtime, userInputRequestId, """{"ok":true}""");
        Assert.True(await userInputResponseTask);
        using (var userInputRequest = await ReadSingleRequestAsync(stream))
        {
            Assert.Equal("serverRequest/respond", userInputRequest.RootElement.GetProperty("method").GetString());
            var parameters = userInputRequest.RootElement.GetProperty("params");
            Assert.Equal(903, parameters.GetProperty("requestId").GetInt64());
            Assert.Equal("input-resume-001", parameters.GetProperty("callId").GetString());
            Assert.Equal("request_user_input", parameters.GetProperty("requestKind").GetString());
            Assert.Equal(".tianshu/tianshu.toml", parameters.GetProperty("result").GetProperty("answers").GetProperty("config_path").GetProperty("answers")[0].GetString());
        }

        stream.SetLength(0);
        stream.Position = 0;

        var approvalResponseTask = runtime.RespondToApprovalAsync(
            new ControlPlaneApprovalResolution
            {
                CallId = new CallId("approval-resume-001"),
                Decision = ControlPlaneApprovalDecision.ApproveForSession,
                Note = "恢复后继续批准",
            },
            CancellationToken.None);
        var approvalRequestId = await WaitForPendingResponseIdAsync(runtime, resumeRequestId, permissionRequestId, userInputRequestId);
        CompletePendingResponse(runtime, approvalRequestId, """{"ok":true}""");
        Assert.True(await approvalResponseTask);
        using (var approvalRequest = await ReadSingleRequestAsync(stream))
        {
            Assert.Equal("serverRequest/respond", approvalRequest.RootElement.GetProperty("method").GetString());
            var parameters = approvalRequest.RootElement.GetProperty("params");
            Assert.Equal(901, parameters.GetProperty("requestId").GetInt64());
            Assert.Equal("approval-resume-001", parameters.GetProperty("callId").GetString());
            Assert.Equal("approval_requested", parameters.GetProperty("requestKind").GetString());
            var decision = parameters.GetProperty("result").GetProperty("decision");
            var decisionType = decision.ValueKind == JsonValueKind.String
                ? decision.GetString()
                : decision.GetProperty("type").GetString();
            Assert.Equal("acceptForSession", decisionType);
        }
    }

    [Fact]
    public async Task ResumeThreadAsync_WhenTypedRequestProvided_WritesTypedThreadResumePayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());
        ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
        {
            WorkingDirectory = "D:/fallback/resume",
            Model = "fallback-model",
            ModelProvider = "fallback-provider",
            ServiceTier = "flex",
            ApprovalPolicy = "on-request",
            SandboxMode = "workspace-write",
            SessionSource = ControlPlaneSessionSource.Cli,
        });

        var pendingTask = runtime.ResumeThreadAsync(
            new ControlPlaneResumeThreadCommand
            {
                ThreadId = new ThreadId("thread-resume-typed-001"),
                Path = "D:/sessions/thread-resume-typed-001.jsonl",
                History =
                [
                    ControlPlaneStructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "message",
                        ["role"] = "user",
                        ["content"] = new object?[]
                        {
                            new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["type"] = "input_text",
                                ["text"] = "resume from typed history",
                            },
                        },
                    }),
                ],
                Model = "gpt-5.4",
                ModelProvider = "openai",
                ServiceTier = "fast",
                WorkingDirectory = "D:/typed/thread-resume",
                ApprovalPolicy = "never",
                SandboxMode = "danger-full-access",
                Configuration = new Dictionary<string, ControlPlaneStructuredValue>(StringComparer.Ordinal)
                {
                    ["notify"] = ControlPlaneStructuredValue.FromBoolean(true),
                },
                BaseInstructions = "resume base",
                DeveloperInstructions = "resume developer",
                Personality = "friendly",
                PersistExtendedHistory = false,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-resume-typed-001",
                "preview": "typed resume",
                "cwd": "D:/typed/thread-resume",
                "path": "D:/sessions/thread-resume-typed-001.jsonl",
                "ephemeral": false,
                "seedHistory": [],
                "pendingInteractiveRequests": [],
                "turns": []
              }
            }
            """);

        var result = await pendingTask;
        Assert.NotNull(result);
        Assert.Equal("D:/sessions/thread-resume-typed-001.jsonl", result!.Thread.Path);
        Assert.False(result.Thread.IsEphemeral);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/resume", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-resume-typed-001", parameters.GetProperty("threadId").GetString());
        Assert.Equal("D:/sessions/thread-resume-typed-001.jsonl", parameters.GetProperty("path").GetString());
        Assert.Equal("gpt-5.4", parameters.GetProperty("model").GetString());
        Assert.Equal("openai", parameters.GetProperty("modelProvider").GetString());
        Assert.Equal("fast", parameters.GetProperty("serviceTier").GetString());
        Assert.Equal("D:/typed/thread-resume", parameters.GetProperty("cwd").GetString());
        Assert.Equal("never", parameters.GetProperty("approvalPolicy").GetString());
        Assert.Equal("danger-full-access", parameters.GetProperty("sandbox").GetString());
        Assert.Equal("resume base", parameters.GetProperty("baseInstructions").GetString());
        Assert.Equal("resume developer", parameters.GetProperty("developerInstructions").GetString());
        Assert.Equal("friendly", parameters.GetProperty("personality").GetString());
        Assert.Equal("cli", parameters.GetProperty("sessionSource").GetString());
        Assert.False(parameters.GetProperty("persistExtendedHistory").GetBoolean());
        var config = parameters.GetProperty("config");
        Assert.True(config.GetProperty("notify").GetBoolean());
        var history = parameters.GetProperty("history");
        var historyItem = Assert.Single(history.EnumerateArray());
        Assert.Equal("message", historyItem.GetProperty("type").GetString());
        Assert.Equal("user", historyItem.GetProperty("role").GetString());
    }

    [Fact]
    public async Task ResumeThreadAsync_WhenServiceTierOmitted_UsesOptionFallback()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());
        ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
        {
            ServiceTier = AgentServiceTierOverride.FromValue(AgentServiceTier.Flex),
        });

        var pendingTask = runtime.ResumeThreadAsync(
            new ControlPlaneResumeThreadCommand
            {
                ThreadId = new ThreadId("thread-resume-clear-tier-001"),
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-resume-clear-tier-001",
                "preview": "resume clear tier",
                "cwd": "D:/typed/thread-resume",
                "ephemeral": false,
                "seedHistory": [],
                "pendingInteractiveRequests": [],
                "turns": []
              }
            }
            """);

        var result = await pendingTask;
        Assert.NotNull(result);

        using var request = await ReadSingleRequestAsync(stream);
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("flex", parameters.GetProperty("serviceTier").GetString());
    }

    [Fact]
    public async Task ResumeThreadAsync_WhenPendingInputStateCarriesExplicitFlags_PreservesThem()
    {
        var result = await ResumeThreadWithPendingInputStateAsync(
            "thread-explicit-flags-001",
            """
            {
              "entries": [],
              "interruptRequestPending": true,
              "submitPendingSteersAfterInterrupt": true,
              "pendingSteers": [
                {
                  "correlationId": "corr-explicit-flags-001",
                  "requestedMode": "Queue",
                  "effectiveMode": "Queue",
                  "lifecycleState": "awaiting_commit",
                  "expectedTurnId": "turn-explicit-flags-001",
                  "turnId": null,
                  "pendingBucket": "PendingSteer"
                }
              ]
            }
            """);

        var pendingState = Assert.IsType<ControlPlanePendingInputState>(result!.PendingInputState);
        Assert.True(pendingState.InterruptRequestPending);
        Assert.True(pendingState.SubmitPendingSteersAfterInterrupt);
        Assert.Empty(pendingState.Entries);
        Assert.Empty(pendingState.QueuedUserMessages ?? Array.Empty<ControlPlanePendingInputStateEntry>());
        Assert.Equal(
            "corr-explicit-flags-001",
            Assert.Single(pendingState.PendingSteers ?? Array.Empty<ControlPlanePendingInputStateEntry>()).CorrelationId);
    }

    [Fact]
    public async Task ResumeThreadAsync_WhenLatestTurnIsNonTerminal_ShouldRestoreActiveTurn()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, _) = CreateConnectedRuntime(runtime, activeThreadId: null, activeTurnId: null);

        var pendingTask = runtime.ResumeThreadAsync("thread-resume-active-001", CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);

        using (var request = await ReadSingleRequestAsync(stream))
        {
            Assert.Equal("thread/resume", request.RootElement.GetProperty("method").GetString());
            Assert.Equal("thread-resume-active-001", request.RootElement.GetProperty("params").GetProperty("threadId").GetString());
            Assert.Equal(requestId, request.RootElement.GetProperty("id").GetInt64());
        }

        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-resume-active-001",
                "preview": "恢复中的线程",
                "cwd": "D:/Work/TianShu",
                "pendingInteractiveRequests": [],
                "seedHistory": [],
                "turns": [
                  {
                    "id": "turn-resume-active-001",
                    "status": "in_progress",
                    "items": []
                  }
                ]
              }
            }
            """);

        var result = await pendingTask;
        Assert.NotNull(result);
        Assert.True(runtime.HasActiveTurn);
        Assert.Equal("thread-resume-active-001", runtime.ActiveThreadId);
        Assert.Equal("turn-resume-active-001", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
    }

    [Fact]
    public async Task ResumeThreadAsync_WhenThreadStatusIsIdle_DoesNotRestoreStaleActiveTurn()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, _) = CreateConnectedRuntime(runtime, activeThreadId: null, activeTurnId: null);

        var pendingTask = runtime.ResumeThreadAsync("thread-resume-idle-001", CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);

        using (var request = await ReadSingleRequestAsync(stream))
        {
            Assert.Equal("thread/resume", request.RootElement.GetProperty("method").GetString());
            Assert.Equal("thread-resume-idle-001", request.RootElement.GetProperty("params").GetProperty("threadId").GetString());
            Assert.Equal(requestId, request.RootElement.GetProperty("id").GetInt64());
        }

        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-resume-idle-001",
                "preview": "已空闲线程",
                "cwd": "D:/Work/TianShu",
                "status": {
                  "type": "idle",
                  "activeFlags": []
                },
                "pendingInteractiveRequests": [],
                "seedHistory": [],
                "turns": [
                  {
                    "id": "turn-resume-idle-001",
                    "status": "in_progress",
                    "items": []
                  }
                ]
              }
            }
            """);

        var result = await pendingTask;
        Assert.NotNull(result);
        Assert.False(runtime.HasActiveTurn);
        Assert.Equal("thread-resume-idle-001", runtime.ActiveThreadId);
        Assert.Null(ReflectionTestHelper.GetField(runtime, "activeTurnId"));
    }

    [Fact]
    public void UpdatePendingInputStateSnapshot_WhenStructuredInputsProvided_PreservesTypedInputs()
    {
        var runtime = new TianShuExecutionRuntime();
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-pending-inputs-001");

        var inputs = new AgentUserInput[]
        {
            new TextUserInput
            {
                Type = "text",
                Text = "请保留这条补录文本",
            },
            new LocalImageUserInput
            {
                Type = "localImage",
                Path = "D:/images/reference.png",
            },
        };

        var updated = Assert.IsType<PendingInputStatePayload>(
            ReflectionTestHelper.InvokeMethod(
                runtime,
                "UpdatePendingInputStateSnapshot",
                "corr-pending-inputs-001",
                FollowUpMode.Queue,
                FollowUpMode.Queue,
                "awaiting_commit",
                "turn-pending-inputs-old",
                "turn-pending-inputs-new",
                "请保留这条补录文本",
                1,
                inputs));

        var entry = Assert.Single(updated.QueuedUserMessages ?? Array.Empty<PendingInputStateEntryPayload>());
        Assert.Equal("corr-pending-inputs-001", entry.CorrelationId);
        Assert.Collection(
            entry.Inputs ?? Array.Empty<AgentUserInput>(),
            input =>
            {
                var textInput = Assert.IsType<TextUserInput>(input);
                Assert.Equal("请保留这条补录文本", textInput.Text);
            },
            input =>
            {
                var imageInput = Assert.IsType<LocalImageUserInput>(input);
                Assert.Equal("D:/images/reference.png", imageInput.Path);
            });
    }

    [Fact]
    public async Task ResumeThreadAsync_WhenPendingInputStateExplicitlyClearsSubmitFlag_DoesNotDeriveItFromPendingSteers()
    {
        var result = await ResumeThreadWithPendingInputStateAsync(
            "thread-explicit-flags-false-001",
            """
            {
              "entries": [
                {
                  "correlationId": "corr-explicit-flags-false-interrupt-001",
                  "requestedMode": "Interrupt",
                  "effectiveMode": "Interrupt",
                  "lifecycleState": "interrupt_requested",
                  "expectedTurnId": "turn-explicit-flags-false-001",
                  "turnId": "turn-explicit-flags-false-001",
                  "pendingBucket": "QueuedUserMessage"
                }
              ],
              "interruptRequestPending": true,
              "submitPendingSteersAfterInterrupt": false,
              "pendingSteers": [
                {
                  "correlationId": "corr-explicit-flags-false-steer-001",
                  "requestedMode": "Steer",
                  "effectiveMode": "Steer",
                  "lifecycleState": "awaiting_commit",
                  "expectedTurnId": "turn-explicit-flags-false-001",
                  "turnId": null,
                  "pendingBucket": "PendingSteer"
                }
              ]
            }
            """);

        var pendingState = Assert.IsType<ControlPlanePendingInputState>(result!.PendingInputState);
        Assert.True(pendingState.InterruptRequestPending);
        Assert.False(pendingState.SubmitPendingSteersAfterInterrupt);
        var interruptEntry = Assert.Single(pendingState.Entries);
        Assert.Equal("corr-explicit-flags-false-interrupt-001", interruptEntry.CorrelationId);
        Assert.Equal("QueuedUserMessage", interruptEntry.PendingBucket);
        Assert.Equal(
            "corr-explicit-flags-false-steer-001",
            Assert.Single(pendingState.PendingSteers ?? Array.Empty<ControlPlanePendingInputStateEntry>()).CorrelationId);
    }

    [Fact]
    public async Task ResumeThreadAsync_WhenPendingInputStateExplicitlyClearsInterruptFlag_DoesNotDeriveItFromEntries()
    {
        var result = await ResumeThreadWithPendingInputStateAsync(
            "thread-explicit-interrupt-false-001",
            """
            {
              "entries": [
                {
                  "correlationId": "corr-explicit-interrupt-false-001",
                  "requestedMode": "Interrupt",
                  "effectiveMode": "Interrupt",
                  "lifecycleState": "interrupt_requested",
                  "expectedTurnId": "turn-explicit-interrupt-false-001",
                  "turnId": "turn-explicit-interrupt-false-001",
                  "pendingBucket": "QueuedUserMessage"
                }
              ],
              "interruptRequestPending": false,
              "submitPendingSteersAfterInterrupt": true,
              "pendingSteers": [
                {
                  "correlationId": "corr-explicit-interrupt-false-steer-001",
                  "requestedMode": "Steer",
                  "effectiveMode": "Steer",
                  "lifecycleState": "awaiting_commit",
                  "expectedTurnId": "turn-explicit-interrupt-false-001",
                  "turnId": null,
                  "pendingBucket": "PendingSteer"
                }
              ]
            }
            """);

        var pendingState = Assert.IsType<ControlPlanePendingInputState>(result!.PendingInputState);
        Assert.False(pendingState.InterruptRequestPending);
        Assert.True(pendingState.SubmitPendingSteersAfterInterrupt);
        var interruptEntry = Assert.Single(pendingState.Entries);
        Assert.Equal("corr-explicit-interrupt-false-001", interruptEntry.CorrelationId);
        Assert.Equal("QueuedUserMessage", interruptEntry.PendingBucket);
        Assert.Equal(
            "corr-explicit-interrupt-false-steer-001",
            Assert.Single(pendingState.PendingSteers ?? Array.Empty<ControlPlanePendingInputStateEntry>()).CorrelationId);
    }

    [Fact]
    public async Task ResumeThreadAsync_WhenLegacyPendingInputEntriesOmitAuthoritativeBuckets_DoesNotPromoteThem()
    {
        var result = await ResumeThreadWithPendingInputStateAsync(
            "thread-legacy-promote-001",
            """
            {
              "entries": [
                {
                  "correlationId": "corr-legacy-interrupt-001",
                  "requestedMode": "Interrupt",
                  "effectiveMode": "Interrupt",
                  "lifecycleState": "interrupt_requested",
                  "expectedTurnId": "turn-legacy-001",
                  "turnId": "turn-legacy-interrupt-001",
                  "pendingBucket": "QueuedUserMessage"
                },
                {
                  "correlationId": "corr-legacy-queue-001",
                  "requestedMode": "Queue",
                  "effectiveMode": "Queue",
                  "lifecycleState": "queued",
                  "expectedTurnId": "turn-legacy-001",
                  "turnId": null,
                  "pendingBucket": "QueuedUserMessage"
                },
                {
                  "correlationId": "corr-legacy-steer-001",
                  "requestedMode": "Steer",
                  "effectiveMode": "Steer",
                  "lifecycleState": "awaiting_commit",
                  "expectedTurnId": "turn-legacy-001",
                  "turnId": null,
                  "pendingBucket": "PendingSteer"
                }
              ],
              "interruptRequestPending": true,
              "submitPendingSteersAfterInterrupt": true
            }
            """);

        var pendingState = Assert.IsType<ControlPlanePendingInputState>(result!.PendingInputState);
        Assert.True(pendingState.InterruptRequestPending);
        Assert.False(pendingState.SubmitPendingSteersAfterInterrupt);
        Assert.Equal("corr-legacy-interrupt-001", Assert.Single(pendingState.Entries).CorrelationId);
        Assert.Empty(pendingState.QueuedUserMessages ?? Array.Empty<ControlPlanePendingInputStateEntry>());
        Assert.Empty(pendingState.PendingSteers ?? Array.Empty<ControlPlanePendingInputStateEntry>());
    }

    [Fact]
    public async Task ReadThreadAsync_WhenPendingInputStateCarriesExplicitQueues_PreservesTopLevelBuckets()
    {
        var result = await ReadThreadWithPendingInputStateAsync(
            "thread-explicit-queues-001",
            """
            {
              "entries": [
                {
                  "correlationId": "corr-explicit-queues-interrupt-001",
                  "requestedMode": "Interrupt",
                  "effectiveMode": "Interrupt",
                  "lifecycleState": "interrupt_requested",
                  "expectedTurnId": "turn-explicit-queues-001",
                  "turnId": "turn-explicit-queues-001",
                  "pendingBucket": "QueuedUserMessage"
                }
              ],
              "interruptRequestPending": true,
              "submitPendingSteersAfterInterrupt": true,
              "queuedUserMessages": [
                {
                  "correlationId": "corr-explicit-queues-queue-001",
                  "requestedMode": "Queue",
                  "effectiveMode": "Queue",
                  "lifecycleState": "awaiting_commit",
                  "expectedTurnId": "turn-explicit-queues-001",
                  "turnId": null,
                  "pendingBucket": "QueuedUserMessage"
                }
              ],
              "pendingSteers": [
                {
                  "correlationId": "corr-explicit-queues-steer-001",
                  "requestedMode": "Steer",
                  "effectiveMode": "Steer",
                  "lifecycleState": "awaiting_commit",
                  "expectedTurnId": "turn-explicit-queues-001",
                  "turnId": null,
                  "pendingBucket": "PendingSteer"
                }
              ]
            }
            """);

        var pendingState = Assert.IsType<ControlPlanePendingInputState>(result.Thread.PendingInputState);
        Assert.True(pendingState.InterruptRequestPending);
        Assert.True(pendingState.SubmitPendingSteersAfterInterrupt);
        Assert.Equal(
            "corr-explicit-queues-queue-001",
            Assert.Single(pendingState.QueuedUserMessages ?? Array.Empty<ControlPlanePendingInputStateEntry>()).CorrelationId);
        Assert.Equal(
            "corr-explicit-queues-steer-001",
            Assert.Single(pendingState.PendingSteers ?? Array.Empty<ControlPlanePendingInputStateEntry>()).CorrelationId);
        Assert.Equal("corr-explicit-queues-interrupt-001", Assert.Single(pendingState.Entries).CorrelationId);
    }

    [Fact]
    public async Task ReadThreadAsync_WhenPendingInputStateExplicitlyClearsTopLevelCollections_DoesNotRehydrateThem()
    {
        var result = await ReadThreadWithPendingInputStateAsync(
            "thread-explicit-empty-001",
            """
            {
              "entries": [
                {
                  "correlationId": "corr-explicit-empty-queue-001",
                  "requestedMode": "Queue",
                  "effectiveMode": "Queue",
                  "lifecycleState": "awaiting_commit",
                  "expectedTurnId": "turn-explicit-empty-001",
                  "turnId": null,
                  "pendingBucket": "QueuedUserMessage"
                },
                {
                  "correlationId": "corr-explicit-empty-steer-001",
                  "requestedMode": "Steer",
                  "effectiveMode": "Steer",
                  "lifecycleState": "awaiting_commit",
                  "expectedTurnId": "turn-explicit-empty-001",
                  "turnId": null,
                  "pendingBucket": "PendingSteer"
                }
              ],
              "interruptRequestPending": false,
              "submitPendingSteersAfterInterrupt": false,
              "queuedUserMessages": [],
              "pendingSteers": []
            }
            """);

        var pendingState = Assert.IsType<ControlPlanePendingInputState>(result.Thread.PendingInputState);
        Assert.Empty(pendingState.Entries);
        Assert.Empty(pendingState.QueuedUserMessages ?? Array.Empty<ControlPlanePendingInputStateEntry>());
        Assert.Empty(pendingState.PendingSteers ?? Array.Empty<ControlPlanePendingInputStateEntry>());
    }

    [Fact]
    public async Task ReadThreadAsync_WhenLegacyInterruptAwaitingCommitEntryArrives_DoesNotPromoteQueuedMessages()
    {
        var result = await ReadThreadWithPendingInputStateAsync(
            "thread-legacy-interrupt-awaiting-001",
            """
            {
              "entries": [
                {
                  "correlationId": "corr-legacy-interrupt-awaiting-001",
                  "requestedMode": "Interrupt",
                  "effectiveMode": "Queue",
                  "lifecycleState": "awaiting_commit",
                  "expectedTurnId": "turn-legacy-interrupt-awaiting-001",
                  "turnId": "turn-legacy-interrupt-awaiting-002",
                  "pendingBucket": "QueuedUserMessage"
                }
              ],
              "interruptRequestPending": false,
              "submitPendingSteersAfterInterrupt": false
            }
            """);

        var pendingState = Assert.IsType<ControlPlanePendingInputState>(result.Thread.PendingInputState);
        Assert.Empty(pendingState.Entries);
        Assert.Empty(pendingState.QueuedUserMessages ?? Array.Empty<ControlPlanePendingInputStateEntry>());
        Assert.Empty(pendingState.PendingSteers ?? Array.Empty<ControlPlanePendingInputStateEntry>());
    }

    [Fact]
    public void UpdatePendingInputStateSnapshot_WhenStateCarriesExplicitTopLevelCollections_IgnoresStaleEntriesProjection()
    {
        var runtime = new TianShuExecutionRuntime();
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-explicit-owner-001");
        var states = Assert.IsType<Dictionary<string, PendingInputStatePayload>>(
            ReflectionTestHelper.GetField(runtime, "pendingInputStatesByThread"));
        states["thread-explicit-owner-001"] = new PendingInputStatePayload(
            Entries:
            [
                new PendingInputStateEntryPayload(
                    "corr-explicit-owner-interrupt-001",
                    "Interrupt",
                    "Interrupt",
                    "interrupt_requested",
                    "turn-explicit-owner-old",
                    "turn-explicit-owner-old",
                    new PendingFollowUpCompareKeyPayload("interrupt entry", 0),
                    "QueuedUserMessage"),
                new PendingInputStateEntryPayload(
                    "corr-explicit-owner-stale-queue-001",
                    "Queue",
                    "Queue",
                    "awaiting_commit",
                    "turn-explicit-owner-old",
                    "turn-explicit-owner-new",
                    new PendingFollowUpCompareKeyPayload("stale queue projection", 0),
                    "QueuedUserMessage"),
            ],
            InterruptRequestPending: true,
            SubmitPendingSteersAfterInterrupt: true,
            QueuedUserMessages: Array.Empty<PendingInputStateEntryPayload>(),
            PendingSteers:
            [
                new PendingInputStateEntryPayload(
                    "corr-explicit-owner-steer-001",
                    "Steer",
                    "Steer",
                    "awaiting_commit",
                    "turn-explicit-owner-old",
                    "turn-explicit-owner-new",
                    new PendingFollowUpCompareKeyPayload("authoritative steer entry", 0),
                    "PendingSteer"),
            ]);

        var updated = Assert.IsType<PendingInputStatePayload>(
            ReflectionTestHelper.InvokeMethod(
                runtime,
                "UpdatePendingInputStateSnapshot",
                "corr-explicit-owner-live-queue-001",
                FollowUpMode.Queue,
                FollowUpMode.Queue,
                "awaiting_commit",
                "turn-explicit-owner-old",
                "turn-explicit-owner-new",
                "authoritative queued entry",
                0));

        Assert.True(updated.InterruptRequestPending);
        Assert.True(updated.SubmitPendingSteersAfterInterrupt);
        Assert.Equal(
            "corr-explicit-owner-live-queue-001",
            Assert.Single(updated.QueuedUserMessages ?? Array.Empty<PendingInputStateEntryPayload>()).CorrelationId);
        Assert.Equal(
            "corr-explicit-owner-steer-001",
            Assert.Single(updated.PendingSteers ?? Array.Empty<PendingInputStateEntryPayload>()).CorrelationId);
        var entry = Assert.Single(updated.Entries);
        Assert.Equal("corr-explicit-owner-interrupt-001", entry.CorrelationId);
        Assert.DoesNotContain(
            updated.Entries,
            static entry => string.Equals(entry.CorrelationId, "corr-explicit-owner-stale-queue-001", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdatePendingInputStateSnapshot_WhenStateExplicitlyClearsTopLevelCollections_DoesNotRehydrateStaleEntriesProjection()
    {
        var runtime = new TianShuExecutionRuntime();
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-explicit-owner-empty-001");
        var states = Assert.IsType<Dictionary<string, PendingInputStatePayload>>(
            ReflectionTestHelper.GetField(runtime, "pendingInputStatesByThread"));
        states["thread-explicit-owner-empty-001"] = new PendingInputStatePayload(
            Entries:
            [
                new PendingInputStateEntryPayload(
                    "corr-explicit-owner-empty-queue-001",
                    "Queue",
                    "Queue",
                    "awaiting_commit",
                    "turn-explicit-owner-empty-old",
                    "turn-explicit-owner-empty-new",
                    new PendingFollowUpCompareKeyPayload("stale queue projection", 0),
                    "QueuedUserMessage"),
            ],
            InterruptRequestPending: false,
            SubmitPendingSteersAfterInterrupt: false,
            QueuedUserMessages: Array.Empty<PendingInputStateEntryPayload>(),
            PendingSteers: Array.Empty<PendingInputStateEntryPayload>());

        var updated = Assert.IsType<PendingInputStatePayload>(
            ReflectionTestHelper.InvokeMethod(
                runtime,
                "UpdatePendingInputStateSnapshot",
                "corr-explicit-owner-empty-interrupt-001",
                FollowUpMode.Interrupt,
                FollowUpMode.Interrupt,
                "interrupt_requested",
                "turn-explicit-owner-empty-old",
                "turn-explicit-owner-empty-old",
                "authoritative interrupt entry",
                0));

        Assert.True(updated.InterruptRequestPending);
        Assert.Empty(updated.QueuedUserMessages ?? Array.Empty<PendingInputStateEntryPayload>());
        Assert.Empty(updated.PendingSteers ?? Array.Empty<PendingInputStateEntryPayload>());
        Assert.Collection(
            updated.Entries,
            entry => Assert.Equal("corr-explicit-owner-empty-interrupt-001", entry.CorrelationId));
        Assert.DoesNotContain(
            updated.Entries,
            static entry => string.Equals(entry.CorrelationId, "corr-explicit-owner-empty-queue-001", StringComparison.Ordinal));
    }

    [Fact]
    public void HandleTurnCompletedNotification_WhenInterruptedConsumesRuntimeOwnedPendingSteers()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-interrupted-reducer-001");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-interrupted-reducer-001");
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "SetPendingInputStateSnapshot",
            "thread-interrupted-reducer-001",
            new PendingInputStatePayload(
            [
                new PendingInputStateEntryPayload(
                    "corr-interrupted-reducer-interrupt-001",
                    "Interrupt",
                    "Interrupt",
                    "interrupt_requested",
                    "turn-interrupted-reducer-001",
                    "turn-interrupted-reducer-001",
                    new PendingFollowUpCompareKeyPayload("中断当前回合", 0),
                    "QueuedUserMessage"),
            ],
                InterruptRequestPending: true,
                SubmitPendingSteersAfterInterrupt: true,
                QueuedUserMessages: null,
                PendingSteers:
            [
                new PendingInputStateEntryPayload(
                    "corr-interrupted-reducer-steer-001",
                    "Steer",
                    "Steer",
                    "awaiting_commit",
                    "turn-interrupted-reducer-001",
                    null,
                    new PendingFollowUpCompareKeyPayload("中断后立刻继续这个方向", 0),
                    "PendingSteer",
                    new AgentUserInput[] { new TextUserInput { Type = "text", Text = "中断后立刻继续这个方向" } }),
            ]));

        const string notification = """
        {"method":"turn/completed","params":{"threadId":"thread-interrupted-reducer-001","turn":{"id":"turn-interrupted-reducer-001","status":"interrupted"}}}
        """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notification),
            notification);

        var completedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.TurnCompleted));
        Assert.Equal("thread-interrupted-reducer-001", completedEvent.ThreadId?.Value);
        Assert.Equal("turn-interrupted-reducer-001", completedEvent.TurnId?.Value);
        Assert.Equal("interrupted", completedEvent.Status);

        var committedLifecycle = Assert.Single(events.Where(static item =>
            item.Kind == ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated
            && string.Equals(item.Status, "committed", StringComparison.Ordinal)));
        var committedLifecycleState = GetPendingInputStatePayload(committedLifecycle);
        Assert.Empty(ReadStructuredItems(committedLifecycleState, "pendingSteers"));

        var committedUserMessage = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.UserMessageCommitted));
        Assert.Equal("pending-follow-up-corr-interrupted-reducer-steer-001", committedUserMessage.ItemId);
        Assert.Equal("中断后立刻继续这个方向", committedUserMessage.Text);
        var committedUserMessagePayload = GetCommittedUserMessagePayload(committedUserMessage);
        Assert.Equal("corr-interrupted-reducer-steer-001", ReadStructuredString(committedUserMessagePayload, "correlationId"));
        Assert.Equal("中断后立刻继续这个方向", ReadStructuredString(committedUserMessagePayload, "text"));

        var reducedSnapshot = Assert.IsType<PendingInputStatePayload>(
            ReflectionTestHelper.InvokeMethod(
                runtime,
                "GetPendingInputStateSnapshot",
                "thread-interrupted-reducer-001"));
        Assert.False(reducedSnapshot.InterruptRequestPending);
        Assert.False(reducedSnapshot.SubmitPendingSteersAfterInterrupt);
        Assert.Empty(reducedSnapshot.Entries);
    }

    [Fact]
    public void HandleTurnCompletedNotification_WhenInterruptLifecycleReachedCompleted_KeepsResubmitFlagUntilReducerConsumesEntries()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-interrupted-reducer-completed-001");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-interrupted-reducer-completed-001");

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "RaisePendingFollowUpLifecycle",
            "corr-interrupted-reducer-steer-completed-001",
            FollowUpMode.Steer,
            FollowUpMode.Steer,
            "awaiting_commit",
            "turn-interrupted-reducer-completed-001",
            "turn-interrupted-reducer-completed-001",
            "中断后继续这个方向",
            0);
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "RaisePendingFollowUpLifecycle",
            "corr-interrupted-reducer-interrupt-completed-001",
            FollowUpMode.Interrupt,
            FollowUpMode.Interrupt,
            "queued",
            null,
            null,
            "中断当前回合",
            0);
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "RaisePendingFollowUpLifecycle",
            "corr-interrupted-reducer-interrupt-completed-001",
            FollowUpMode.Interrupt,
            FollowUpMode.Interrupt,
            "interrupt_requested",
            "turn-interrupted-reducer-completed-001",
            "turn-interrupted-reducer-completed-001",
            "中断当前回合",
            0);
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "RaisePendingFollowUpLifecycle",
            "corr-interrupted-reducer-interrupt-completed-001",
            FollowUpMode.Interrupt,
            FollowUpMode.Interrupt,
            "interrupt_completed",
            "turn-interrupted-reducer-completed-001",
            "turn-interrupted-reducer-completed-001",
            "中断当前回合",
            0);

        var snapshotBeforeCompleted = Assert.IsType<PendingInputStatePayload>(
            ReflectionTestHelper.InvokeMethod(
                runtime,
                "GetPendingInputStateSnapshot",
                "thread-interrupted-reducer-completed-001"));
        Assert.False(snapshotBeforeCompleted.InterruptRequestPending);
        Assert.True(snapshotBeforeCompleted.SubmitPendingSteersAfterInterrupt);
        Assert.Equal(
            "corr-interrupted-reducer-steer-completed-001",
            Assert.Single(snapshotBeforeCompleted.PendingSteers ?? Array.Empty<PendingInputStateEntryPayload>()).CorrelationId);
        var pendingEntry = Assert.Single(snapshotBeforeCompleted.Entries);
        Assert.Equal("Interrupt", pendingEntry.RequestedMode);
        Assert.Equal("interrupt_completed", pendingEntry.LifecycleState);
        Assert.Equal("QueuedUserMessage", pendingEntry.PendingBucket);

        const string notification = """
        {"method":"turn/completed","params":{"threadId":"thread-interrupted-reducer-completed-001","turn":{"id":"turn-interrupted-reducer-completed-001","status":"interrupted"}}}
        """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notification),
            notification);

        var completedEvent = events.Last(item => item.Kind == ControlPlaneConversationStreamEventKind.TurnCompleted);
        Assert.Equal("thread-interrupted-reducer-completed-001", completedEvent.ThreadId?.Value);
        Assert.Equal("turn-interrupted-reducer-completed-001", completedEvent.TurnId?.Value);
        Assert.Equal("interrupted", completedEvent.Status);

        var reducedSnapshot = Assert.IsType<PendingInputStatePayload>(
            ReflectionTestHelper.InvokeMethod(
                runtime,
                "GetPendingInputStateSnapshot",
                "thread-interrupted-reducer-completed-001"));
        Assert.False(reducedSnapshot.InterruptRequestPending);
        Assert.False(reducedSnapshot.SubmitPendingSteersAfterInterrupt);
        Assert.Empty(reducedSnapshot.Entries);
    }

    [Fact]
    public void HandleTurnCompletedNotification_WhenInterruptedOnlyConsumesEntriesOwnedByCompletedTurn()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-interrupted-owner-001");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-interrupted-owner-new");
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "SetPendingInputStateSnapshot",
            "thread-interrupted-owner-001",
            new PendingInputStatePayload(
                Entries:
                [
                    new PendingInputStateEntryPayload(
                        "corr-interrupted-owner-interrupt-001",
                        "Interrupt",
                        "Interrupt",
                        "interrupt_completed",
                        "turn-interrupted-owner-old",
                        "turn-interrupted-owner-old",
                        new PendingFollowUpCompareKeyPayload("中断当前回合", 0),
                        "QueuedUserMessage"),
                ],
                InterruptRequestPending: false,
                SubmitPendingSteersAfterInterrupt: true,
                QueuedUserMessages:
                [
                    new PendingInputStateEntryPayload(
                        "corr-interrupted-owner-queue-001",
                        "Queue",
                        "Queue",
                        "awaiting_commit",
                        "turn-interrupted-owner-old",
                        "turn-interrupted-owner-new",
                        new PendingFollowUpCompareKeyPayload("在新回合排队发送这个补充", 0),
                        "QueuedUserMessage"),
                ],
                PendingSteers:
                [
                    new PendingInputStateEntryPayload(
                        "corr-interrupted-owner-steer-001",
                        "Steer",
                        "Steer",
                        "awaiting_commit",
                        "turn-interrupted-owner-old",
                        "turn-interrupted-owner-new",
                        new PendingFollowUpCompareKeyPayload("在新回合继续这个方向", 0),
                        "PendingSteer"),
                ]));

        const string notification = """
        {"method":"turn/completed","params":{"threadId":"thread-interrupted-owner-001","turn":{"id":"turn-interrupted-owner-old","status":"interrupted"}}}
        """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notification),
            notification);

        var completedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.TurnCompleted));
        Assert.Equal("thread-interrupted-owner-001", completedEvent.ThreadId?.Value);
        Assert.Equal("turn-interrupted-owner-old", completedEvent.TurnId?.Value);
        Assert.Equal("interrupted", completedEvent.Status);

        Assert.True(runtime.HasActiveTurn);
        Assert.Equal("turn-interrupted-owner-new", ReflectionTestHelper.GetField(runtime, "activeTurnId"));

        var reducedSnapshot = Assert.IsType<PendingInputStatePayload>(
            ReflectionTestHelper.InvokeMethod(
                runtime,
                "GetPendingInputStateSnapshot",
                "thread-interrupted-owner-001"));
        Assert.False(reducedSnapshot.InterruptRequestPending);
        Assert.False(reducedSnapshot.SubmitPendingSteersAfterInterrupt);
        Assert.Empty(reducedSnapshot.Entries);
        Assert.Equal(
            "corr-interrupted-owner-steer-001",
            Assert.Single(reducedSnapshot.PendingSteers ?? Array.Empty<PendingInputStateEntryPayload>()).CorrelationId);
        Assert.Equal(
            "corr-interrupted-owner-queue-001",
            Assert.Single(reducedSnapshot.QueuedUserMessages ?? Array.Empty<PendingInputStateEntryPayload>()).CorrelationId);
    }

    [Fact]
    public void ReducePendingInputStateForCompletedTurn_WhenStateCarriesExplicitTopLevelCollections_IgnoresStaleEntriesProjection()
    {
        var runtime = new TianShuExecutionRuntime();
        var states = Assert.IsType<Dictionary<string, PendingInputStatePayload>>(
            ReflectionTestHelper.GetField(runtime, "pendingInputStatesByThread"));
        states["thread-explicit-reduce-001"] = new PendingInputStatePayload(
            Entries:
            [
                new PendingInputStateEntryPayload(
                    "corr-explicit-reduce-interrupt-001",
                    "Interrupt",
                    "Interrupt",
                    "interrupt_completed",
                    "turn-explicit-reduce-old",
                    "turn-explicit-reduce-old",
                    new PendingFollowUpCompareKeyPayload("interrupt entry", 0),
                    "QueuedUserMessage"),
                new PendingInputStateEntryPayload(
                    "corr-explicit-reduce-stale-queue-001",
                    "Queue",
                    "Queue",
                    "awaiting_commit",
                    "turn-explicit-reduce-old",
                    "turn-explicit-reduce-new",
                    new PendingFollowUpCompareKeyPayload("stale queue projection", 0),
                    "QueuedUserMessage"),
            ],
            InterruptRequestPending: false,
            SubmitPendingSteersAfterInterrupt: true,
            QueuedUserMessages:
            [
                new PendingInputStateEntryPayload(
                    "corr-explicit-reduce-live-queue-001",
                    "Queue",
                    "Queue",
                    "awaiting_commit",
                    "turn-explicit-reduce-old",
                    "turn-explicit-reduce-new",
                    new PendingFollowUpCompareKeyPayload("authoritative queued entry", 0),
                    "QueuedUserMessage"),
            ],
            PendingSteers:
            [
                new PendingInputStateEntryPayload(
                    "corr-explicit-reduce-steer-001",
                    "Steer",
                    "Steer",
                    "awaiting_commit",
                    "turn-explicit-reduce-old",
                    "turn-explicit-reduce-old",
                    new PendingFollowUpCompareKeyPayload("authoritative steer entry", 0),
                    "PendingSteer"),
            ]);

        var reduced = Assert.IsType<PendingInputStatePayload>(
            ReflectionTestHelper.InvokeMethod(
                runtime,
                "ReducePendingInputStateForCompletedTurn",
                "thread-explicit-reduce-001",
                "turn-explicit-reduce-old",
                "interrupted"));

        Assert.False(reduced.InterruptRequestPending);
        Assert.False(reduced.SubmitPendingSteersAfterInterrupt);
        Assert.Equal(
            "corr-explicit-reduce-live-queue-001",
            Assert.Single(reduced.QueuedUserMessages ?? Array.Empty<PendingInputStateEntryPayload>()).CorrelationId);
        Assert.Empty(reduced.PendingSteers ?? Array.Empty<PendingInputStateEntryPayload>());
        Assert.Empty(reduced.Entries);
        Assert.DoesNotContain(
            reduced.Entries,
            static entry => string.Equals(entry.CorrelationId, "corr-explicit-reduce-stale-queue-001", StringComparison.Ordinal));

        var snapshot = Assert.IsType<PendingInputStatePayload>(
            ReflectionTestHelper.InvokeMethod(
                runtime,
                "GetPendingInputStateSnapshot",
                "thread-explicit-reduce-001"));
        Assert.Equal(
            "corr-explicit-reduce-live-queue-001",
            Assert.Single(snapshot.QueuedUserMessages ?? Array.Empty<PendingInputStateEntryPayload>()).CorrelationId);
        Assert.Empty(snapshot.Entries);
    }

    [Fact]
    public void ReducePendingInputStateForCompletedTurn_WhenStateExplicitlyClearsTopLevelCollections_DoesNotRehydrateStaleEntriesProjection()
    {
        var runtime = new TianShuExecutionRuntime();
        var states = Assert.IsType<Dictionary<string, PendingInputStatePayload>>(
            ReflectionTestHelper.GetField(runtime, "pendingInputStatesByThread"));
        states["thread-explicit-reduce-empty-001"] = new PendingInputStatePayload(
            Entries:
            [
                new PendingInputStateEntryPayload(
                    "corr-explicit-reduce-empty-interrupt-001",
                    "Interrupt",
                    "Interrupt",
                    "interrupt_completed",
                    "turn-explicit-reduce-empty-old",
                    "turn-explicit-reduce-empty-old",
                    new PendingFollowUpCompareKeyPayload("interrupt entry", 0),
                    "QueuedUserMessage"),
                new PendingInputStateEntryPayload(
                    "corr-explicit-reduce-empty-queue-001",
                    "Queue",
                    "Queue",
                    "awaiting_commit",
                    "turn-explicit-reduce-empty-old",
                    "turn-explicit-reduce-empty-new",
                    new PendingFollowUpCompareKeyPayload("stale queue projection", 0),
                    "QueuedUserMessage"),
            ],
            InterruptRequestPending: false,
            SubmitPendingSteersAfterInterrupt: false,
            QueuedUserMessages: Array.Empty<PendingInputStateEntryPayload>(),
            PendingSteers: Array.Empty<PendingInputStateEntryPayload>());

        var reduced = Assert.IsType<PendingInputStatePayload>(
            ReflectionTestHelper.InvokeMethod(
                runtime,
                "ReducePendingInputStateForCompletedTurn",
                "thread-explicit-reduce-empty-001",
                "turn-explicit-reduce-empty-old",
                "interrupted"));

        Assert.False(reduced.InterruptRequestPending);
        Assert.Empty(reduced.Entries);
        Assert.Empty(reduced.QueuedUserMessages ?? Array.Empty<PendingInputStateEntryPayload>());
        Assert.Empty(reduced.PendingSteers ?? Array.Empty<PendingInputStateEntryPayload>());
    }

    [Fact]
    public async Task ReadThreadAsync_WritesExpectedMethod_AndParsesThreadPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ReadThreadAsync(
            new ControlPlaneReadThreadQuery
            {
                ThreadId = new ThreadId("thread-read-001"),
                IncludeTurns = true,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-read-001",
                "cwd": "D:/Work/TianShu",
                "preview": "协议测试线程读取",
                "sessionConfiguration": {
                  "model": "gpt-5.4",
                  "modelProvider": "openai",
                  "modelProviderId": "openai",
                  "approvalPolicy": "never",
                  "sandboxPolicy": "danger-full-access",
                  "sandboxPolicyPayload": {
                    "type": "danger-full-access"
                  },
                  "reasoningEffort": "high",
                  "serviceName": "read-thread",
                  "developerInstructions": "keep typed state",
                  "dynamicTools": [
                    {
                      "fullName": "mcp__demo__lookup"
                    }
                  ],
                  "sessionSource": {
                    "subAgent": {
                      "thread_spawn": {
                        "parent_thread_id": "thread-parent-typed-001",
                        "depth": 2,
                        "agent_nickname": "reviewer",
                        "agent_role": "review"
                      }
                    }
                  }
                },
                "gitInfo": {
                  "branch": "main"
                },
                "pendingInputState": {
                  "submitPendingSteersAfterInterrupt": false,
                  "queuedUserMessages": [
                    {
                      "correlationId": "corr-read-001",
                      "requestedMode": "Interrupt",
                      "effectiveMode": "Interrupt",
                      "lifecycleState": "interrupt_requested",
                      "expectedTurnId": null,
                      "turnId": "turn-read-001",
                      "pendingBucket": "QueuedUserMessage",
                      "compareKey": {
                        "message": "读取后的待处理中断",
                        "imageCount": 1
                      }
                    }
                  ],
                  "interruptRequestPending": true
                },
                "turns": [
                  {
                    "id": "turn_read_1",
                    "status": "completed",
                    "items": [
                      {
                        "id": "item_read_1",
                        "type": "assistant_message",
                        "text": "ok"
                      }
                    ]
                  }
                ]
              }
            }
            """);

        var result = await pendingTask;
        Assert.NotNull(result.Thread);
        Assert.Equal("thread-read-001", result.Thread!.ThreadId.Value);
        Assert.Equal("main", result.Thread.GitBranch);
        Assert.NotNull(result.Thread.PendingInputState);
        Assert.False(result.Thread.PendingInputState!.SubmitPendingSteersAfterInterrupt);
        var pendingEntry = Assert.Single(result.Thread.PendingInputState.QueuedUserMessages ?? Array.Empty<ControlPlanePendingInputStateEntry>());
        Assert.Equal("corr-read-001", pendingEntry.CorrelationId);
        Assert.Equal("interrupt_requested", pendingEntry.LifecycleState);
        Assert.Equal("QueuedUserMessage", pendingEntry.PendingBucket);
        Assert.Null(pendingEntry.CompareKey);
        Assert.Empty(pendingEntry.Inputs ?? Array.Empty<ControlPlaneInputItem>());
        var hydratedSnapshot = Assert.IsType<PendingInputStatePayload>(
            ReflectionTestHelper.InvokeMethod(runtime, "GetPendingInputStateSnapshot", "thread-read-001"));
        Assert.False(hydratedSnapshot.SubmitPendingSteersAfterInterrupt);
        Assert.Equal(
            "corr-read-001",
            Assert.Single(hydratedSnapshot.QueuedUserMessages ?? Array.Empty<PendingInputStateEntryPayload>()).CorrelationId);
        Assert.NotNull(result.Thread.SessionConfiguration);
        Assert.Equal("gpt-5.4", result.Thread.SessionConfiguration!.Model);
        Assert.Equal("openai", result.Thread.SessionConfiguration.ModelProviderId);
        Assert.Equal("danger-full-access", result.Thread.SessionConfiguration.SandboxPolicy);
        Assert.Equal("danger-full-access", result.Thread.SessionConfiguration.SandboxPolicyPayload!.Properties["type"].StringValue);
        Assert.Equal("read-thread", result.Thread.SessionConfiguration.ServiceName);
        Assert.Equal("keep typed state", result.Thread.SessionConfiguration.DeveloperInstructions);
        Assert.Equal("mcp__demo__lookup", Assert.Single(result.Thread.SessionConfiguration.DynamicTools!).Properties["fullName"].StringValue);
        Assert.Equal(
            ControlPlaneSessionSource.SubAgent(
                ControlPlaneSubAgentSource.ThreadSpawn(
                    "thread-parent-typed-001",
                    2,
                    "reviewer",
                    "review")),
            result.Thread.SessionConfiguration.SessionSource);
        var turn = Assert.Single(result.Thread.Turns);
        var item = Assert.Single(turn.Items);
        Assert.Equal("assistant_message", item.Type);
        Assert.Equal("ok", item.Text);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/read", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-read-001", parameters.GetProperty("threadId").GetString());
        Assert.True(parameters.GetProperty("includeTurns").GetBoolean());
    }

    [Fact]
    public void AgentThreadSourceKindMatches_WhenKindIsGenericSubAgent_MatchesConcreteSubAgentSources()
    {
        Assert.True(ControlPlaneThreadSourceKind.SubAgent.Matches(ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.Review)));
        Assert.True(ControlPlaneThreadSourceKind.SubAgent.Matches(ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.ThreadSpawn("thread-parent-001", 2))));
        Assert.True(ControlPlaneThreadSourceKind.SubAgent.Matches(ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.MemoryConsolidation)));
        Assert.True(ControlPlaneThreadSourceKind.SubAgent.Matches(ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.Other("handoff"))));
        Assert.False(ControlPlaneThreadSourceKind.SubAgent.Matches(ControlPlaneSessionSource.Cli));
    }

    [Fact]
    public void AgentThreadSourceKindMatches_WhenKindIsSubAgentOther_MatchesOtherBuckets()
    {
        Assert.True(ControlPlaneThreadSourceKind.SubAgentOther.Matches(ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.Other("handoff"))));
        Assert.False(ControlPlaneThreadSourceKind.SubAgentOther.Matches(ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.MemoryConsolidation)));
        Assert.False(ControlPlaneThreadSourceKind.SubAgentOther.Matches(ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.Review)));
    }

    [Fact]
    public void AgentSessionSource_MemoryConsolidation_ShouldProjectToGenericSubAgentSourceKind()
    {
        var source = ControlPlaneSessionSource.SubAgent(ControlPlaneSubAgentSource.MemoryConsolidation);

        Assert.Equal(ControlPlaneThreadSourceKind.SubAgent, source.GetThreadSourceKind());
        Assert.True(ControlPlaneThreadSourceKind.SubAgent.Matches(source));
        Assert.False(ControlPlaneThreadSourceKind.SubAgentOther.Matches(source));
    }

    [Fact]
    public async Task ReadThreadAsync_WhenRichThreadItemsPresent_ParsesTypedUnionItems()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ReadThreadAsync(
            new ControlPlaneReadThreadQuery
            {
                ThreadId = new ThreadId("thread-read-rich-001"),
                IncludeTurns = true,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-read-rich-001",
                "preview": "rich thread",
                "turns": [
                  {
                    "id": "turn-rich-001",
                    "status": "completed",
                    "items": [
                      {
                        "id": "user-item-001",
                        "type": "userMessage",
                        "content": [
                          {
                            "type": "text",
                            "text": "请继续处理 rich item parser",
                            "textElements": [
                              {
                                "byteRange": {
                                  "start": 0,
                                  "end": 2
                                },
                                "placeholder": "<note>"
                              }
                            ]
                          },
                          {
                            "type": "mention",
                            "name": "worker-1",
                            "path": "app://worker-1"
                          }
                        ]
                      },
                      {
                        "id": "reasoning-item-001",
                        "type": "reasoning",
                        "summary": [
                          "先解析 typed items"
                        ],
                        "content": [
                          "再更新回放逻辑"
                        ]
                      },
                      {
                        "id": "command-item-001",
                        "type": "commandExecution",
                        "command": "git status",
                        "commandActions": [
                          {
                            "type": "unknown",
                            "command": "git status"
                          }
                        ],
                        "cwd": "D:/Work/TianShu",
                        "status": "completed",
                        "aggregatedOutput": "M src/Execution/TianShu.Execution.Runtime/TianShuExecutionRuntime.cs",
                        "exitCode": 0,
                        "durationMs": 18,
                        "processId": "pty-1"
                      },
                      {
                        "id": "file-change-item-001",
                        "type": "fileChange",
                        "status": "completed",
                        "changes": [
                          {
                            "path": "src/Execution/TianShu.Execution.Runtime/TianShuExecutionRuntime.cs",
                            "kind": "update",
                            "diff": "@@ -1 +1 @@"
                          }
                        ]
                      },
                      {
                        "id": "mcp-item-001",
                        "type": "mcpToolCall",
                        "server": "filesystem",
                        "tool": "read_file",
                        "arguments": {
                          "path": "README.md"
                        },
                        "status": "completed",
                        "result": {
                          "content": [
                            "README CONTENT"
                          ],
                          "structuredContent": {
                            "lineCount": 12
                          }
                        },
                        "durationMs": 27
                      },
                      {
                        "id": "dynamic-item-001",
                        "type": "dynamicToolCall",
                        "tool": "capture_note",
                        "arguments": {
                          "title": "rich"
                        },
                        "contentItems": [
                          {
                            "type": "inputText",
                            "text": "captured"
                          }
                        ],
                        "status": "completed",
                        "success": true,
                        "durationMs": 9
                      },
                      {
                        "id": "web-search-item-001",
                        "type": "webSearch",
                        "query": "tianshu thread item union",
                        "action": {
                          "type": "search",
                          "queries": [
                            "tianshu thread item union"
                          ]
                        }
                      }
                    ]
                  }
                ]
              }
            }
            """);

        var result = await pendingTask;
        Assert.NotNull(result.Thread);
        var turn = Assert.Single(result.Thread!.Turns);
        Assert.Collection(
            turn.Items,
            item =>
            {
                Assert.Equal("userMessage", item.Type);
                Assert.Equal(
                    $"请继续处理 rich item parser{Environment.NewLine}worker-1",
                    item.Text);
            },
            item =>
            {
                Assert.Equal("reasoning", item.Type);
                Assert.Equal("先解析 typed items", item.Text);
            },
            item =>
            {
                Assert.Equal("commandExecution", item.Type);
                Assert.Equal(
                    "M src/Execution/TianShu.Execution.Runtime/TianShuExecutionRuntime.cs",
                    item.Text);
            },
            item =>
            {
                Assert.Equal("fileChange", item.Type);
                Assert.Equal("src/Execution/TianShu.Execution.Runtime/TianShuExecutionRuntime.cs", item.Text);
            },
            item =>
            {
                Assert.Equal("mcpToolCall", item.Type);
                Assert.Equal("README CONTENT", item.Text);
            },
            item =>
            {
                Assert.Equal("dynamicToolCall", item.Type);
                Assert.Equal("captured", item.Text);
            },
            item =>
            {
                Assert.Equal("webSearch", item.Type);
                Assert.Equal("tianshu thread item union", item.Text);
            });
    }

    [Fact]
    public async Task RaisePendingFollowUpLifecycle_WhenKernelPersistenceEnabled_WritesPendingInputUpdateRequest()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(
            runtime,
            activeThreadId: "thread-persist-001",
            activeTurnId: "turn-active-persist-001",
            enablePendingInputPersistence: true);

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "RaisePendingFollowUpLifecycle",
            "corr-persist-001",
            FollowUpMode.Steer,
            FollowUpMode.Steer,
            "awaiting_commit",
            "turn-expected-persist-001",
            null,
            "持久化引导消息",
            0);

        var requestId = await WaitForPendingResponseIdAsync(runtime);
        using var request = await ReadSingleRequestEventuallyAsync(stream);
        Assert.Equal("tianshu/thread/pending_input/update", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-persist-001", parameters.GetProperty("threadId").GetString());
        var pendingInputState = parameters.GetProperty("pendingInputState");
        Assert.False(pendingInputState.GetProperty("interruptRequestPending").GetBoolean());
        Assert.False(pendingInputState.GetProperty("submitPendingSteersAfterInterrupt").GetBoolean());
        var entry = pendingInputState.GetProperty("pendingSteers")[0];
        Assert.Equal("corr-persist-001", entry.GetProperty("correlationId").GetString());
        Assert.Equal("awaiting_commit", entry.GetProperty("lifecycleState").GetString());
        Assert.Equal("PendingSteer", entry.GetProperty("pendingBucket").GetString());
        Assert.False(entry.TryGetProperty("compareKey", out _));

        CompletePendingResponse(
            runtime,
            requestId,
            """{}""");

        await writer.FlushAsync();
    }

    [Fact]
    public async Task UnarchiveThreadAsync_WritesExpectedMethod_AndParsesThreadPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.UnarchiveThreadAsync(
            new ControlPlaneUnarchiveThreadCommand
            {
                ThreadId = new ThreadId("thread-unarchive-001"),
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-unarchive-001",
                "preview": "协议测试取消归档"
              }
            }
            """);

        var result = await pendingTask;
        Assert.NotNull(result.Thread);
        Assert.Equal("thread-unarchive-001", result.Thread!.ThreadId.Value);
        Assert.Equal("协议测试取消归档", result.Thread.Preview);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/unarchive", request.RootElement.GetProperty("method").GetString());
        Assert.Equal("thread-unarchive-001", request.RootElement.GetProperty("params").GetProperty("threadId").GetString());
    }

    [Fact]
    public async Task UpdateThreadMetadataAsync_WritesExpectedMethod_AndPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.UpdateThreadMetadataAsync(
            new ControlPlaneUpdateThreadMetadataCommand
            {
                ThreadId = new ThreadId("thread-metadata-001"),
                HasGitSha = true,
                GitSha = "abc123",
                HasGitBranch = true,
                GitBranch = null,
                HasGitOriginUrl = true,
                GitOriginUrl = "https://example.invalid/repo.git",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-metadata-001",
                "gitInfo": {
                  "sha": "abc123",
                  "branch": null,
                  "originUrl": "https://example.invalid/repo.git"
                }
              }
            }
            """);

        var result = await pendingTask;
        Assert.NotNull(result.Thread);
        Assert.Equal("abc123", result.Thread!.GitSha);
        Assert.Null(result.Thread.GitBranch);
        Assert.Equal("https://example.invalid/repo.git", result.Thread.GitOriginUrl);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/metadata/update", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-metadata-001", parameters.GetProperty("threadId").GetString());
        var gitInfo = parameters.GetProperty("gitInfo");
        Assert.Equal("abc123", gitInfo.GetProperty("sha").GetString());
        Assert.Equal(JsonValueKind.Null, gitInfo.GetProperty("branch").ValueKind);
        Assert.Equal("https://example.invalid/repo.git", gitInfo.GetProperty("originUrl").GetString());
    }

    [Fact]
    public async Task RollbackThreadAsync_WritesExpectedMethod_AndParsesThreadPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-rollback-001");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn_rollback_active");
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "EnqueuePendingFollowUpCommit",
            "thread-rollback-001",
            "corr-rollback-001",
            "回滚前待提交消息",
            "turn_rollback_active",
            "turn_rollback_pending",
            FollowUpMode.Queue,
            FollowUpMode.Queue);
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "UpdatePendingInputStateSnapshot",
            "corr-rollback-001",
            FollowUpMode.Queue,
            FollowUpMode.Queue,
            "awaiting_commit",
            "turn_rollback_active",
            "turn_rollback_pending",
            "回滚前待提交消息",
            0);
        Assert.IsType<ConcurrentDictionary<string, long>>(
            ReflectionTestHelper.GetField(runtime, "pendingInputPersistenceVersionsByThread")).Clear();
        var pendingApprovalRequests = Assert.IsType<ConcurrentDictionary<string, TaskCompletionSource<ApprovalResponse>>>(
            ReflectionTestHelper.GetField(runtime, "pendingApprovalRequests"));
        var pendingPermissionRequests = Assert.IsType<ConcurrentDictionary<string, TaskCompletionSource<PermissionGrantResponse>>>(
            ReflectionTestHelper.GetField(runtime, "pendingPermissionRequests"));
        var pendingUserInputRequests = Assert.IsType<ConcurrentDictionary<string, TaskCompletionSource<UserInputSubmission>>>(
            ReflectionTestHelper.GetField(runtime, "pendingUserInputRequests"));
        var approvalCompletion = new TaskCompletionSource<ApprovalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var permissionCompletion = new TaskCompletionSource<PermissionGrantResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var userInputCompletion = new TaskCompletionSource<UserInputSubmission>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingApprovalRequests["approval-call-rollback-001"] = approvalCompletion;
        pendingPermissionRequests["permission-call-rollback-001"] = permissionCompletion;
        pendingUserInputRequests["input-call-rollback-001"] = userInputCompletion;
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "TrackPendingInteractiveServerRequest",
            701L,
            "approval-call-rollback-001",
            "thread-rollback-001",
            "turn_rollback_active",
            "approval_requested",
            "shell",
            "item/tool/requestApproval");
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "TrackPendingInteractiveServerRequest",
            702L,
            "permission-call-rollback-001",
            "thread-rollback-001",
            "turn_rollback_active",
            "permission_requested",
            "shell",
            "item/permissions/requestApproval");
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "TrackPendingInteractiveServerRequest",
            703L,
            "input-call-rollback-001",
            "thread-rollback-001",
            "turn_rollback_active",
            "request_user_input",
            "shell",
            "item/tool/requestUserInput");
        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.RollbackThreadAsync(
            new ControlPlaneRollbackThreadCommand
            {
                ThreadId = new ThreadId("thread-rollback-001"),
                NumTurns = 2,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-rollback-001",
                "turns": [
                  {
                    "id": "turn_rollback_1",
                    "status": "completed"
                  }
                ]
              }
            }
            """);

        var result = await pendingTask;
        Assert.NotNull(result.Thread);
        Assert.Equal("thread-rollback-001", result.Thread!.ThreadId.Value);
        Assert.Single(result.Thread.Turns);
        Assert.NotNull(result.Thread.PendingInputState);
        Assert.Empty(result.Thread.PendingInputState!.Entries);

        var pendingCommit = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryConsumePendingFollowUpCorrelation",
            new AgentUserInput[] { new TextUserInput { Type = "text", Text = "回滚前待提交消息" } },
            "turn_rollback_pending",
            "thread-rollback-001");
        Assert.Null(pendingCommit);
        Assert.True(approvalCompletion.Task.IsCanceled);
        Assert.True(permissionCompletion.Task.IsCanceled);
        Assert.True(userInputCompletion.Task.IsCanceled);
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingInteractiveServerRequestsByRequestId"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingInteractiveServerRequestIdsByCallId"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingInteractiveReplayContextsByCallId"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingApprovalRequests"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingPermissionRequests"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingUserInputRequests"))!.Cast<object>());

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/rollback", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-rollback-001", parameters.GetProperty("threadId").GetString());
        Assert.Equal(2, parameters.GetProperty("numTurns").GetInt32());
    }

    [Fact]
    public async Task ListLoadedThreadsAsync_WritesExpectedMethod_AndParsesResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ListLoadedThreadsAsync(
            new ControlPlaneLoadedThreadListQuery
            {
                Limit = 1,
                Cursor = "thread_a",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "data": ["thread_b"],
              "nextCursor": "thread_b"
            }
            """);

        var result = await pendingTask;
        Assert.Equal(["thread_b"], result.ThreadIds.Select(static item => item.Value));
        Assert.Equal("thread_b", result.NextCursor);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/loaded/list", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal(1, parameters.GetProperty("limit").GetInt32());
        Assert.Equal("thread_a", parameters.GetProperty("cursor").GetString());
    }

    [Fact]
    public async Task CompactThreadAsync_WritesExpectedMethod_AndPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.CompactThreadAsync(
            new ControlPlaneCompactThreadCommand
            {
                ThreadId = new ThreadId("thread-compact-001"),
                KeepRecentTurns = 6,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{}""");

        _ = await pendingTask;

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/compact/start", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-compact-001", parameters.GetProperty("threadId").GetString());
        Assert.Equal(6, parameters.GetProperty("keepRecentTurns").GetInt32());
    }

    [Fact]
    public async Task CleanBackgroundTerminalsAsync_WritesExpectedMethod_AndPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.CleanBackgroundTerminalsAsync(
            new ControlPlaneCleanBackgroundTerminalsCommand
            {
                ThreadId = new ThreadId("thread-clean-001"),
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{}""");

        _ = await pendingTask;

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/backgroundTerminals/clean", request.RootElement.GetProperty("method").GetString());
        Assert.Equal("thread-clean-001", request.RootElement.GetProperty("params").GetProperty("threadId").GetString());
    }

    [Fact]
    public async Task RunUserShellCommandAsync_WritesExpectedMethod_AndParsesResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-shell-001");

        var pendingTask = runtime.RunUserShellCommandAsync("Get-Location", CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "turnId": "turn-shell-001",
              "itemId": "cmd-shell-001",
              "turnStatus": "completed",
              "itemStatus": "completed",
              "exitCode": 0,
              "stdout": "D:\\GitRepos\\Personal\\TianShu",
              "stderr": "",
              "reusedActiveTurn": false
            }
            """);

        var result = await pendingTask;
        Assert.True(result.Accepted);
        Assert.Equal("turn-shell-001", result.TurnId);
        Assert.Equal("completed", result.TurnStatus);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("tianshu/userShell/run", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-shell-001", parameters.GetProperty("threadId").GetString());
        Assert.Equal("Get-Location", parameters.GetProperty("command").GetString());
    }

    [Fact]
    public async Task UnsubscribeThreadAsync_WritesExpectedMethod_AndParsesStatus()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.UnsubscribeThreadAsync(
            new ControlPlaneUnsubscribeThreadCommand
            {
                ThreadId = new ThreadId("thread-unsubscribe-001"),
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "status": "notSubscribed"
            }
            """);

        var result = await pendingTask;
        Assert.Equal("notSubscribed", result.Status);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/unsubscribe", request.RootElement.GetProperty("method").GetString());
        Assert.Equal("thread-unsubscribe-001", request.RootElement.GetProperty("params").GetProperty("threadId").GetString());
    }

    [Fact]
    public async Task HandoffRealtimeOutputAsync_WritesExpectedMethod_AndPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.HandoffRealtimeOutputAsync(
            new ControlPlaneRealtimeHandoffOutputCommand
            {
                ThreadId = new ThreadId("thread-rt-handoff-1"),
                SessionId = "session-rt-handoff-1",
                HandoffId = "call-rt-handoff-1",
                Output = "delegated result",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{}""");
        await pendingTask;

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/realtime/handoffOutput", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-rt-handoff-1", parameters.GetProperty("threadId").GetString());
        Assert.Equal("session-rt-handoff-1", parameters.GetProperty("sessionId").GetString());
        Assert.Equal("call-rt-handoff-1", parameters.GetProperty("handoffId").GetString());
        Assert.Equal("delegated result", parameters.GetProperty("output").GetString());
    }

    [Fact]
    public async Task StartRealtimeAsync_WritesExpectedMethod_AndPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.StartRealtimeAsync(
            new ControlPlaneRealtimeStartCommand
            {
                ThreadId = new ThreadId("thread-rt-start-1"),
                SessionId = "session-rt-start-1",
                Prompt = "realtime prompt",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{}""");
        await pendingTask;

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/realtime/start", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-rt-start-1", parameters.GetProperty("threadId").GetString());
        Assert.Equal("session-rt-start-1", parameters.GetProperty("sessionId").GetString());
        Assert.Equal("realtime prompt", parameters.GetProperty("prompt").GetString());
    }

    [Fact]
    public async Task AppendRealtimeAudioAsync_WritesExpectedMethod_AndPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.AppendRealtimeAudioAsync(
            new ControlPlaneRealtimeAppendAudioCommand
            {
                ThreadId = new ThreadId("thread-rt-audio-1"),
                SessionId = "session-rt-audio-1",
                Audio = new ControlPlaneRealtimeAudioInput
                {
                    Data = "AQIDBA==",
                    SampleRate = 24000,
                    NumChannels = 1,
                },
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, """{}""");
        await pendingTask;

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("thread/realtime/appendAudio", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-rt-audio-1", parameters.GetProperty("threadId").GetString());
        Assert.Equal("session-rt-audio-1", parameters.GetProperty("sessionId").GetString());
        var audio = parameters.GetProperty("audio");
        Assert.Equal("AQIDBA==", audio.GetProperty("data").GetString());
        Assert.Equal(24000, audio.GetProperty("sampleRate").GetInt32());
        Assert.Equal(1, audio.GetProperty("numChannels").GetInt32());
    }

    [Fact]
    public async Task ExecuteCodeModeAsync_WritesExecMethod_AndParsesRunningResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ExecuteCodeModeAsync(
            new ControlPlaneCodeModeExecCommand
            {
                ThreadId = new ThreadId("thread-code-exec-001"),
                Input = "print('hi')",
                YieldTimeMs = 250,
                MaxOutputTokens = 64,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "success": true,
              "turnId": "turn-code-exec-001",
              "output": "partial output",
              "contentItems": [
                {
                  "type": "input_text",
                  "text": "Script running with cell ID cell-code-exec-001\npartial output"
                },
                {
                  "type": "input_text",
                  "text": "partial output"
                }
              ]
            }
            """);

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.Equal("running", result.Status);
        Assert.Equal(new ThreadId("thread-code-exec-001"), result.ThreadId);
        Assert.Equal(new TurnId("turn-code-exec-001"), result.TurnId);
        Assert.Equal("cell-code-exec-001", result.CellId);
        Assert.Equal("partial output", result.Output);
        Assert.Equal(2, result.ContentItems.Count);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("exec", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-code-exec-001", parameters.GetProperty("threadId").GetString());
        Assert.Equal("print('hi')", parameters.GetProperty("input").GetString());
        Assert.Equal(250, parameters.GetProperty("yieldTimeMs").GetInt32());
        Assert.Equal(64, parameters.GetProperty("maxOutputTokens").GetInt32());
    }

    [Fact]
    public async Task WaitCodeModeAsync_WritesExecWaitMethod_AndParsesTerminatedResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.WaitCodeModeAsync(
            new ControlPlaneCodeModeWaitCommand
            {
                ThreadId = new ThreadId("thread-code-wait-001"),
                CellId = "cell-code-wait-001",
                YieldTimeMs = 400,
                MaxTokens = 32,
                Terminate = true,
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "success": true,
              "contentItems": [
                {
                  "type": "input_text",
                  "text": "Script terminated"
                },
                {
                  "type": "input_text",
                  "text": "terminated by user"
                }
              ]
            }
            """);

        var result = await pendingTask;
        Assert.True(result.Success);
        Assert.Equal("terminated", result.Status);
        Assert.Equal(new ThreadId("thread-code-wait-001"), result.ThreadId);
        Assert.Equal("cell-code-wait-001", result.CellId);
        Assert.Equal(string.Empty, result.Output);
        Assert.Equal(2, result.ContentItems.Count);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("exec_wait", request.RootElement.GetProperty("method").GetString());
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("thread-code-wait-001", parameters.GetProperty("threadId").GetString());
        Assert.Equal("cell-code-wait-001", parameters.GetProperty("cellId").GetString());
        Assert.Equal(400, parameters.GetProperty("yieldTimeMs").GetInt32());
        Assert.Equal(32, parameters.GetProperty("maxTokens").GetInt32());
        Assert.True(parameters.GetProperty("terminate").GetBoolean());
    }

    [Fact]
    public async Task InvokeDiagnosticRpcAsync_WhenMethodIsBlank_ThrowsArgumentException()
    {
        var runtime = new TianShuExecutionRuntime();
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => runtime.InvokeDiagnosticRpcAsync("   ", null, CancellationToken.None));
        Assert.Equal("method", exception.ParamName);
    }

    [Fact]
    public async Task InvokeDiagnosticRpcAsync_WhenRuntimeIsNotInitialized_ThrowsInvalidOperationException()
    {
        var runtime = new TianShuExecutionRuntime();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.InvokeDiagnosticRpcAsync("model/list", null, CancellationToken.None));
        Assert.Contains("尚未初始化", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeDiagnosticRpcAsync_WhenResponseContainsError_ThrowsAppServerRpcException()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.InvokeDiagnosticRpcAsync("model/list", null, CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        var handled = (bool)ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryResolveRpcResponse",
            ReflectionTestHelper.ParseJsonElement($"{{\"id\":{requestId},\"error\":{{\"message\":\"boom\"}}}}"))!;

        Assert.True(handled);

        var exception = await Assert.ThrowsAsync<AppServerRpcException>(() => pendingTask);
        Assert.Equal(-32603, exception.Code);
        Assert.Equal("boom", exception.RpcMessage);
        Assert.Null(exception.ErrorData);
        Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingResponses"))!.Cast<object>());
    }

    [Fact]
    public async Task InvokeDiagnosticRpcAsync_WhenResponseContainsStructuredError_PreservesErrorData()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.InvokeDiagnosticRpcAsync("thread/start", null, CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        var handled = (bool)ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryResolveRpcResponse",
            ReflectionTestHelper.ParseJsonElement(
                $$"""
                {
                  "id": {{requestId}},
                  "error": {
                    "code": -32600,
                    "message": "failed to load configuration: boom",
                    "data": {
                      "reason": "cloudRequirements",
                      "detail": "boom",
                      "requirements": {
                        "allowedApprovalPolicies": ["never"]
                      }
                    }
                  }
                }
                """))!;

        Assert.True(handled);

        var exception = await Assert.ThrowsAsync<AppServerRpcException>(() => pendingTask);
        Assert.Equal(-32600, exception.Code);
        Assert.Equal("failed to load configuration: boom", exception.RpcMessage);
        Assert.NotNull(exception.ErrorData);
        var errorData = exception.ErrorData!;
        Assert.Equal("cloudRequirements", errorData.GetProperty("reason").GetString());
        Assert.Equal("boom", errorData.GetProperty("detail").GetString());
        Assert.Equal("never", errorData.GetProperty("requirements").GetProperty("allowedApprovalPolicies").Items[0].GetString());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingResponses"))!.Cast<object>());
    }

    [Fact]
    public async Task InvokeDiagnosticRpcAsync_WhenNoResponseArrives_ThrowsTimeoutException_And_CleansPendingRequest()
    {
        var runtime = new TianShuExecutionRuntime();
        var options = new ExecutionRuntimeOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(20),
        };

        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "options", options);
        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => runtime.InvokeDiagnosticRpcAsync("model/list", null, CancellationToken.None));
        Assert.Contains("RPC 超时", exception.Message, StringComparison.Ordinal);
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingResponses"))!.Cast<object>());
    }

    [Fact]
    public async Task WriteJsonLineAsync_WhenConcurrentCallsShareStdin_SerializesOutboundWrites()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new BlockingConcurrentWriteStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var firstTask = Assert.IsAssignableFrom<Task>(
            ReflectionTestHelper.InvokeMethod(
                runtime,
                "WriteJsonLineAsync",
                new Dictionary<string, object?> { ["method"] = "turn/start" },
                CancellationToken.None));
        await stream.FirstWriteStarted;

        var secondTask = Assert.IsAssignableFrom<Task>(
            ReflectionTestHelper.InvokeMethod(
                runtime,
                "WriteJsonLineAsync",
                new Dictionary<string, object?> { ["method"] = "tianshu/thread/pending_input/update" },
                CancellationToken.None));

        await Task.Delay(50);
        Assert.False(stream.ConcurrentWriteObserved);

        stream.ReleaseWrites();
        await Task.WhenAll(firstTask, secondTask);

        var requests = await ReadRequestDocumentsAsync(stream);
        Assert.Equal(2, requests.Count);
        Assert.Equal("turn/start", requests[0].RootElement.GetProperty("method").GetString());
        Assert.Equal("tianshu/thread/pending_input/update", requests[1].RootElement.GetProperty("method").GetString());
        foreach (var request in requests)
        {
            request.Dispose();
        }
    }

    [Fact]
    public async Task ResumeThreadAsync_WhenPendingInteractiveRequestUsesStringRequestId_PreservesRawIdAndReplaysServerRespond()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());

        var pendingTask = runtime.ResumeThreadAsync("thread-resume-string-001", CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            """
            {
              "thread": {
                "id": "thread-resume-string-001",
                "preview": "协议测试线程恢复（字符串 requestId）",
                "cwd": "D:/Work/TianShu",
                "path": "D:/sessions/thread-resume-string-001.jsonl",
                "ephemeral": true,
                "pendingInteractiveRequests": [
                  {
                    "requestId": "permission-resume-string-001",
                    "requestKind": "permission_requested",
                    "requestMethod": "item/permissions/requestApproval",
                    "callId": "permission-call-resume-string-001",
                    "threadId": "thread-resume-string-001",
                    "turnId": "turn-resume-string-001",
                    "toolName": "request_permissions",
                    "text": "Need broader access | {\"network\":{\"enabled\":true}}",
                    "status": "awaitingPermission",
                    "phase": "request_permission",
                    "permissionRequest": {
                      "reason": "Need broader access",
                      "fields": [],
                      "permissionsJson": "{\"network\":{\"enabled\":true}}",
                      "summary": "Need broader access | {\"network\":{\"enabled\":true}}"
                    }
                  }
                ],
                "turns": []
              }
            }
            """);

        var result = await pendingTask;
        var pendingRequest = Assert.Single(result!.PendingInteractiveRequests);
        Assert.Equal(0, pendingRequest.RequestId);
        Assert.Equal("permission-resume-string-001", pendingRequest.RequestIdRaw);
        Assert.Equal("permission-call-resume-string-001", pendingRequest.CallId);

        stream.SetLength(0);
        stream.Position = 0;

        var responseTask = runtime.RespondToPermissionRequestAsync(
            new ControlPlanePermissionGrant
            {
                CallId = new CallId("permission-call-resume-string-001"),
                Permissions = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["network"] = StructuredValue.FromObject(new Dictionary<string, StructuredValue>
                    {
                        ["enabled"] = StructuredValue.FromBoolean(true),
                    }),
                },
                Scope = ControlPlanePermissionScope.Session,
            },
            CancellationToken.None);
        var responseRequestId = await WaitForPendingResponseIdAsync(runtime, requestId);
        CompletePendingResponse(runtime, responseRequestId, """{"ok":true}""");
        Assert.True(await responseTask);

        using var replayRequest = await ReadSingleRequestAsync(stream);
        Assert.Equal("serverRequest/respond", replayRequest.RootElement.GetProperty("method").GetString());
        var parameters = replayRequest.RootElement.GetProperty("params");
        Assert.Equal("permission-resume-string-001", parameters.GetProperty("requestId").GetString());
        Assert.Equal("permission-call-resume-string-001", parameters.GetProperty("callId").GetString());
        Assert.Equal("permission_requested", parameters.GetProperty("requestKind").GetString());
        Assert.Equal("session", parameters.GetProperty("result").GetProperty("scope").GetString());
    }

    [Fact]
    public async Task HandleToolRequestApprovalAsync_WhenResponded_RemovesPendingRequest_And_WritesDecision()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-approval-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-approval-1");
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/tool/requestApproval",
                  "id": 7,
                  "params": {
                    "threadId": "thread-approval-1",
                    "turnId": "turn-approval-1",
                    "itemId": "approval-item-1",
                    "callId": "approval-call-1",
                    "toolName": "shell",
                    "command": ["cmd.exe", "/c", "echo hi"]
                  }
                }
                """),
            CancellationToken.None);

        var responded = false;
        for (var attempt = 0; attempt < 20 && !responded; attempt++)
        {
            responded = await runtime.RespondToApprovalAsync(
                new ControlPlaneApprovalResolution
                {
                    CallId = new CallId("approval-call-1"),
                    Decision = ControlPlaneApprovalDecision.Decline,
                    Note = "需要更多上下文",
                },
                CancellationToken.None);
            if (!responded)
            {
                await Task.Delay(10);
            }
        }

        Assert.True(responded);
        await ReflectionTestHelper.AwaitTaskResultAsync(handlerTask);
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingApprovalRequests"))!.Cast<object>());

        using var response = await ReadSingleRequestAsync(stream);
        Assert.Equal(7, response.RootElement.GetProperty("id").GetInt32());
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("decline", result.GetProperty("decision").GetString());
        Assert.Equal("需要更多上下文", result.GetProperty("reason").GetString());

        var startedEvent = events
            .FirstOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted && item.CallId?.Value == "approval-call-1");
        Assert.NotNull(startedEvent);
        Assert.Equal("shell", startedEvent!.ToolName);
        Assert.Equal("approval-call-1", startedEvent.CallId?.Value);
        var startedPayload = GetToolCallPayload(startedEvent);
        Assert.Equal("shell", ReadStructuredString(startedPayload, "toolName"));
        Assert.Equal("awaitingApproval", ReadStructuredString(startedPayload, "status"));
        Assert.Equal("request_approval", ReadStructuredString(startedPayload, "phase"));
        Assert.Equal("item/tool/requestApproval", startedEvent.SourceMethod);

        var completedEvent = events
            .LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted && item.CallId?.Value == "approval-call-1");
        Assert.NotNull(completedEvent);
        Assert.Equal("shell", completedEvent!.ToolName);
        Assert.Equal("declined", completedEvent.Status);
        var completedPayload = GetToolCallPayload(completedEvent);
        Assert.Equal("declined", ReadStructuredString(completedPayload, "status"));
        Assert.Equal("request_approval", ReadStructuredString(completedPayload, "phase"));
        Assert.Contains("decline", ReadStructuredString(completedPayload, "outputText"), StringComparison.Ordinal);
        Assert.Equal("item/tool/requestApproval", completedEvent.SourceMethod);
    }

    [Fact]
    public async Task HandleCommandExecutionRequestApprovalAsync_WhenSkillMetadataPresent_PopulatesStructuredMetadata()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-approval-skill-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-approval-skill-1");

        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/commandExecution/requestApproval",
                  "id": 8,
                  "params": {
                    "threadId": "thread-approval-skill-1",
                    "turnId": "turn-approval-skill-1",
                    "itemId": "approval-skill-item-1",
                    "command": "D:/repo/.tianshu/skills/test/scripts/hello.cmd",
                    "reason": "manual review required",
                    "skillMetadata": {
                      "pathToSkillsMd": "D:/repo/.tianshu/skills/test/SKILL.md"
                    }
                  }
                }
                """),
            CancellationToken.None);

        ControlPlaneConversationStreamEvent? approvalEvent = null;
        for (var attempt = 0; attempt < 20 && approvalEvent is null; attempt++)
        {
            approvalEvent = events.LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ApprovalRequested);
            if (approvalEvent is null)
            {
                await Task.Delay(10);
            }
        }

        Assert.NotNull(approvalEvent);
        Assert.Equal("commandExecution", approvalEvent!.ToolName);
        Assert.Equal("item/commandExecution/requestApproval", approvalEvent.Message);
        var approvalPayload = GetApprovalRequestPayload(approvalEvent);
        var metadataFields = ReadStructuredItems(approvalPayload, "metadataFields");
        var pathField = Assert.Single(metadataFields);
        Assert.Equal("pathToSkillsMd", ReadStructuredString(pathField, "key"));
        Assert.Equal("string", ReadStructuredString(pathField, "valueType"));
        Assert.Equal("D:/repo/.tianshu/skills/test/SKILL.md", ReadStructuredString(pathField, "valueText"));
        Assert.Null(approvalEvent.Diagnostics);

        var responded = await runtime.RespondToApprovalAsync(
            new ControlPlaneApprovalResolution
            {
                CallId = new CallId(approvalEvent.CallId!.Value),
                Decision = ControlPlaneApprovalDecision.Approve,
                Note = "允许",
            },
            CancellationToken.None);
        Assert.True(responded);

        await ReflectionTestHelper.AwaitTaskResultAsync(handlerTask);

        using var response = await ReadSingleRequestAsync(stream);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("accept", result.GetProperty("decision").GetString());
        Assert.Equal("允许", result.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task HandleCommandExecutionRequestApprovalAsync_WhenTypedDecisionOptionsPresent_EmitsTypedOptionsAndWritesCodexDecisionPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-approval-options-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-approval-options-1");

        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/commandExecution/requestApproval",
                  "id": 18,
                  "params": {
                    "threadId": "thread-approval-options-1",
                    "turnId": "turn-approval-options-1",
                    "approvalId": "approval-options-1",
                    "command": "git add note.txt",
                    "reason": "manual review required",
                    "availableDecisions": [
                      "accept",
                      {
                        "acceptWithExecpolicyAmendment": {
                          "execpolicy_amendment": ["git", "add"]
                        }
                      },
                      {
                        "applyNetworkPolicyAmendment": {
                          "network_policy_amendment": {
                            "host": "example.com",
                            "action": "allow"
                          }
                        }
                      },
                      "cancel"
                    ],
                    "proposedExecpolicyAmendment": ["git", "add"],
                    "proposedNetworkPolicyAmendments": [
                      {
                        "host": "example.com",
                        "action": "allow"
                      }
                    ]
                  }
                }
                """),
            CancellationToken.None);

        ControlPlaneConversationStreamEvent? approvalEvent = null;
        for (var attempt = 0; attempt < 20 && approvalEvent is null; attempt++)
        {
            approvalEvent = events.LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ApprovalRequested);
            if (approvalEvent is null)
            {
                await Task.Delay(10);
            }
        }

        Assert.NotNull(approvalEvent);
        var approvalPayload = GetApprovalRequestPayload(approvalEvent!);
        Assert.NotNull(approvalEvent.AvailableDecisionOptions);
        Assert.Equal(4, approvalEvent.AvailableDecisionOptions!.Count);

        var execPolicyOption = Assert.Single(
            approvalEvent.AvailableDecisionOptions,
            static option => string.Equals(option.Type, "acceptWithExecpolicyAmendment", StringComparison.Ordinal));
        Assert.NotNull(execPolicyOption.ExecPolicyAmendment);
        Assert.Equal(["git", "add"], execPolicyOption.ExecPolicyAmendment!.CommandPrefix);

        var networkOption = Assert.Single(
            approvalEvent.AvailableDecisionOptions,
            static option => string.Equals(option.Type, "applyNetworkPolicyAmendment", StringComparison.Ordinal));
        Assert.NotNull(networkOption.NetworkPolicyAmendment);
        Assert.Equal("example.com", networkOption.NetworkPolicyAmendment!.Host);
        Assert.Equal("allow", networkOption.NetworkPolicyAmendment.Action);

        var proposedExecPolicy = ReadStructuredValue(approvalPayload, "proposedExecPolicyAmendment");
        Assert.NotNull(proposedExecPolicy);
        Assert.Equal(
            ["git", "add"],
            ReadStructuredItems(proposedExecPolicy, "commandPrefix")
                .Select(static item => item.GetString())
                .ToArray());
        var proposedNetwork = Assert.Single(ReadStructuredItems(approvalPayload, "proposedNetworkPolicyAmendments"));
        Assert.Equal("example.com", ReadStructuredString(proposedNetwork, "host"));
        Assert.Equal("allow", ReadStructuredString(proposedNetwork, "action"));

        var responded = await runtime.RespondToApprovalAsync(
            new ControlPlaneApprovalResolution
            {
                CallId = new CallId("approval-options-1"),
                Decision = ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment,
                CommandPrefix = ["git", "add"],
                Note = "remember it",
            },
            CancellationToken.None);
        Assert.True(responded);

        await ReflectionTestHelper.AwaitTaskResultAsync(handlerTask);

        using var response = await ReadSingleRequestAsync(stream);
        var result = response.RootElement.GetProperty("result");
        var decision = result.GetProperty("decision").GetProperty("acceptWithExecpolicyAmendment");
        var amendment = decision.GetProperty("execpolicy_amendment");
        Assert.Equal(2, amendment.GetArrayLength());
        Assert.Equal("git", amendment[0].GetString());
        Assert.Equal("add", amendment[1].GetString());
        Assert.Equal("remember it", result.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RespondToApprovalAsync_WhenNetworkAmendmentPayloadMissing_ShouldSendDecline()
    {
        await using var runtime = new TianShuExecutionRuntime();
        var (stream, _) = CreateConnectedRuntime(runtime, activeThreadId: "thread-approval-missing-network", activeTurnId: null);

        var responseTask = runtime.RespondToApprovalAsync(
            new ControlPlaneApprovalResolution
            {
                CallId = new CallId("approval-options-missing-network"),
                Decision = ControlPlaneApprovalDecision.ApplyNetworkPolicyAmendment,
                Note = "missing payload",
            },
            CancellationToken.None);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("turn/approval/respond", request.RootElement.GetProperty("method").GetString());

        var result = request.RootElement.GetProperty("params");
        Assert.Equal("approval-options-missing-network", result.GetProperty("callId").GetString());
        Assert.Equal("decline", result.GetProperty("decision").GetString());
        Assert.Equal("missing payload", result.GetProperty("reason").GetString());

        CompletePendingResponse(runtime, request.RootElement.GetProperty("id").GetInt64(), """{"ok":true}""");
        Assert.True(await responseTask);
    }

    public static IEnumerable<object[]> TypedApprovalServerRequestCases()
    {
        yield return new object[]
        {
            """
            {
              "method": "item/commandExecution/requestApproval",
              "id": 11,
              "params": {
                "threadId": "thread-approval-typed",
                "turnId": "turn-approval-typed",
                "approvalId": "approval-command-1",
                "command": "dotnet test",
                "reason": "manual review required"
              }
            }
            """,
            "approval-command-1",
            "commandExecution",
            "item/commandExecution/requestApproval",
        };

        yield return new object[]
        {
            """
            {
              "method": "item/fileChange/requestApproval",
              "id": 11,
              "params": {
                "threadId": "thread-approval-typed",
                "turnId": "turn-approval-typed",
                "itemId": "approval-file-1",
                "reason": "manual review required"
              }
            }
            """,
            "approval-file-1",
            "fileChange",
            "item/fileChange/requestApproval",
        };
    }

    [Theory]
    [MemberData(nameof(TypedApprovalServerRequestCases))]
    public async Task TryHandleServerRequestAsync_WhenTypedApprovalRequestArrives_WaitsForDecision(
        string requestJson,
        string callId,
        string expectedToolName,
        string expectedSourceMethod)
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-approval-typed");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-approval-typed");
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(requestJson),
            CancellationToken.None);

        var responded = false;
        for (var attempt = 0; attempt < 20 && !responded; attempt++)
        {
            responded = await runtime.RespondToApprovalAsync(
                new ControlPlaneApprovalResolution
                {
                    CallId = new CallId(callId),
                    Decision = ControlPlaneApprovalDecision.Decline,
                    Note = "manual review required",
                },
                CancellationToken.None);
            if (!responded)
            {
                await Task.Delay(10);
            }
        }

        Assert.True(responded);
        Assert.True(Assert.IsType<bool>(await ReflectionTestHelper.AwaitTaskResultAsync(handlerTask)));
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingApprovalRequests"))!.Cast<object>());

        using var response = await ReadSingleRequestAsync(stream);
        Assert.Equal(11, response.RootElement.GetProperty("id").GetInt32());
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("decline", result.GetProperty("decision").GetString());
        Assert.Equal("manual review required", result.GetProperty("reason").GetString());

        var startedEvent = events
            .FirstOrDefault(item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted && item.CallId?.Value == callId);
        Assert.NotNull(startedEvent);
        Assert.Equal(expectedToolName, startedEvent!.ToolName);
        var startedPayload = GetToolCallPayload(startedEvent);
        Assert.Equal(expectedToolName, ReadStructuredString(startedPayload, "toolName"));
        Assert.Equal("awaitingApproval", ReadStructuredString(startedPayload, "status"));
        Assert.Equal("request_approval", ReadStructuredString(startedPayload, "phase"));
        Assert.Equal(expectedSourceMethod, startedEvent.SourceMethod);

        var completedEvent = events
            .LastOrDefault(item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted && item.CallId?.Value == callId);
        Assert.NotNull(completedEvent);
        Assert.Equal(expectedToolName, completedEvent!.ToolName);
        var completedPayload = GetToolCallPayload(completedEvent);
        Assert.Equal(expectedToolName, ReadStructuredString(completedPayload, "toolName"));
        Assert.Equal("declined", ReadStructuredString(completedPayload, "status"));
        Assert.Equal("request_approval", ReadStructuredString(completedPayload, "phase"));
        Assert.Contains("decline", ReadStructuredString(completedPayload, "outputText"), StringComparison.Ordinal);
        Assert.Equal(expectedSourceMethod, completedEvent.SourceMethod);
    }

    public static IEnumerable<object[]> LegacyApprovalRequestCases()
    {
        yield return new object[]
        {
            """
            {
              "method": "execCommandApproval",
              "id": 21,
              "params": {
                "conversationId": "thread-approval-legacy",
                "callId": "approval-exec-1",
                "command": ["cmd.exe", "/c", "dir"],
                "cwd": "D:/repo",
                "parsedCmd": [],
                "reason": "manual review required"
              }
            }
            """,
            "approval-exec-1",
            "commandExecution",
            "denied",
            "execCommandApproval",
        };

        yield return new object[]
        {
            """
            {
              "method": "applyPatchApproval",
              "id": 22,
              "params": {
                "conversationId": "thread-approval-legacy",
                "callId": "approval-patch-1",
                "fileChanges": {},
                "reason": "manual review required",
                "grantRoot": "src"
              }
            }
            """,
            "approval-patch-1",
            "fileChange",
            "denied",
            "applyPatchApproval",
        };
    }

    [Theory]
    [MemberData(nameof(LegacyApprovalRequestCases))]
    public async Task TryHandleServerRequestAsync_WhenLegacyApprovalRequestArrives_MapsIntoApprovalFlow(
        string requestJson,
        string callId,
        string expectedToolName,
        string expectedDecision,
        string expectedSourceMethod)
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(requestJson),
            CancellationToken.None);

        ControlPlaneConversationStreamEvent? approvalEvent = null;
        for (var attempt = 0; attempt < 20 && approvalEvent is null; attempt++)
        {
            approvalEvent = events.LastOrDefault(item => item.Kind == ControlPlaneConversationStreamEventKind.ApprovalRequested && item.CallId?.Value == callId);
            if (approvalEvent is null)
            {
                await Task.Delay(10);
            }
        }

        Assert.NotNull(approvalEvent);
        Assert.Equal(expectedToolName, approvalEvent!.ToolName);
        var approvalPayload = GetApprovalRequestPayload(approvalEvent);
        Assert.Equal(expectedToolName, ReadStructuredString(approvalPayload, "toolName"));
        AssertStructuredNull(ReadStructuredValue(approvalPayload, "approvalKind"));
        Assert.True(await runtime.RespondToApprovalAsync(
            new ControlPlaneApprovalResolution
            {
                CallId = new CallId(callId),
                Decision = ControlPlaneApprovalDecision.Decline,
                Note = "manual review required",
            },
            CancellationToken.None));

        Assert.True(Assert.IsType<bool>(await ReflectionTestHelper.AwaitTaskResultAsync(handlerTask)));
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingApprovalRequests"))!.Cast<object>());

        using var response = await ReadSingleRequestAsync(stream);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal(expectedDecision, result.GetProperty("decision").GetString());
        Assert.Equal("manual review required", result.GetProperty("reason").GetString());

        var startedEvent = events
            .FirstOrDefault(item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted && item.CallId?.Value == callId);
        Assert.NotNull(startedEvent);
        Assert.Equal(expectedSourceMethod, startedEvent!.SourceMethod);

        var completedEvent = events
            .LastOrDefault(item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted && item.CallId?.Value == callId);
        Assert.NotNull(completedEvent);
        Assert.Equal(expectedSourceMethod, completedEvent!.SourceMethod);
    }

    [Fact]
    public async Task HandleDynamicToolCallAsync_WhenHandlerConfigured_WritesHandlerResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-tool-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-tool-1");
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);
        ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
        {
            DynamicToolCallHandler = static (request, _) =>
            {
                var path = request.Input.GetProperty("path").GetString();
                return Task.FromResult(new ToolInvocationResult(
                    request.CallId,
                    request.ToolKey,
                    new[]
                    {
                        new ToolStreamItem(
                            "content",
                            StructuredValue.FromPlainObject(new Dictionary<string, object?>
                            {
                                ["type"] = "inputText",
                                ["text"] = $"handler:{request.ToolKey}:{path}",
                            })),
                    }));
            },
        });

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/tool/call",
                  "id": 21,
                  "params": {
                    "threadId": "thread-tool-1",
                    "turnId": "turn-tool-1",
                    "callId": "tool-call-1",
                    "tool": "open_file",
                    "arguments": {
                      "path": "README.md"
                    }
                  }
                }
                """),
            CancellationToken.None);

        await ReflectionTestHelper.AwaitTaskResultAsync(handlerTask);

        using var response = await ReadSingleRequestAsync(stream);
        Assert.Equal(21, response.RootElement.GetProperty("id").GetInt32());
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal("handler:open_file:README.md", result.GetProperty("contentItems")[0].GetProperty("text").GetString());

        var startedEvent = events
            .FirstOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted && item.CallId?.Value == "tool-call-1");
        Assert.NotNull(startedEvent);
        Assert.Equal("open_file", startedEvent!.ToolName);
        var startedPayload = GetToolCallPayload(startedEvent);
        Assert.Equal("open_file", ReadStructuredString(startedPayload, "toolName"));
        Assert.Equal("started", ReadStructuredString(startedPayload, "status"));
        Assert.Equal("tool_call", ReadStructuredString(startedPayload, "phase"));
        using var inputDocument = JsonDocument.Parse(ReadStructuredString(startedPayload, "inputText")!);
        Assert.Equal("README.md", inputDocument.RootElement.GetProperty("path").GetString());
        Assert.Equal("item/tool/call", startedEvent.SourceMethod);

        var completedEvent = events
            .LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted && item.CallId?.Value == "tool-call-1");
        Assert.NotNull(completedEvent);
        Assert.Equal("open_file", completedEvent!.ToolName);
        Assert.Equal("completed", completedEvent.Status);
        var completedPayload = GetToolCallPayload(completedEvent);
        Assert.Equal("completed", ReadStructuredString(completedPayload, "status"));
        Assert.Equal("tool_call", ReadStructuredString(completedPayload, "phase"));
        Assert.Equal("handler:open_file:README.md", ReadStructuredString(completedPayload, "outputText"));
        Assert.Equal("item/tool/call", completedEvent.SourceMethod);
    }

    [Fact]
    public async Task HandleDynamicToolCallAsync_WhenHandlerReturnsImageContent_WritesImageUrlResult()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-tool-image-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-tool-image-1");
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);
        ReflectionTestHelper.SetField(runtime, "options", new ExecutionRuntimeOptions
        {
            DynamicToolCallHandler = static (request, _) =>
                Task.FromResult(new ToolInvocationResult(
                    request.CallId,
                    request.ToolKey,
                    new[]
                    {
                        new ToolStreamItem(
                            "content",
                            StructuredValue.FromPlainObject(new Dictionary<string, object?>
                            {
                                ["type"] = "inputImage",
                                ["imageUrl"] = "https://example.com/image.png",
                            })),
                    })),
        });

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/tool/call",
                  "id": 22,
                  "params": {
                    "threadId": "thread-tool-image-1",
                    "turnId": "turn-tool-image-1",
                    "callId": "tool-call-image-1",
                    "tool": "render_preview",
                    "arguments": {
                      "path": "README.md"
                    }
                  }
                }
                """),
            CancellationToken.None);

        await ReflectionTestHelper.AwaitTaskResultAsync(handlerTask);

        using var response = await ReadSingleRequestAsync(stream);
        Assert.Equal(22, response.RootElement.GetProperty("id").GetInt32());
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("success").GetBoolean());
        var contentItem = Assert.Single(result.GetProperty("contentItems").EnumerateArray());
        Assert.Equal("inputImage", contentItem.GetProperty("type").GetString());
        Assert.Equal("https://example.com/image.png", contentItem.GetProperty("image_url").GetString());
        Assert.False(contentItem.TryGetProperty("text", out _));

        var completedEvent = events
            .LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted && item.CallId?.Value == "tool-call-image-1");
        Assert.NotNull(completedEvent);
        var completedPayload = GetToolCallPayload(completedEvent!);
        Assert.Equal("https://example.com/image.png", ReadStructuredString(completedPayload, "outputText"));
    }

    [Fact]
    public async Task HandleDynamicToolCallAsync_WhenHandlerMissing_ReturnsFailureEnvelope()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-tool-2");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-tool-2");
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/tool/call",
                  "id": 22,
                  "params": {
                    "threadId": "thread-tool-2",
                    "turnId": "turn-tool-2",
                    "callId": "tool-call-2",
                    "tool": "missing_handler",
                    "arguments": {}
                  }
                }
                """),
            CancellationToken.None);

        await ReflectionTestHelper.AwaitTaskResultAsync(handlerTask);

        using var response = await ReadSingleRequestAsync(stream);
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("dynamic tool call handler is not configured", result.GetProperty("contentItems")[0].GetProperty("text").GetString(), StringComparison.Ordinal);

        var completedEvent = events
            .LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted && item.CallId?.Value == "tool-call-2");
        Assert.NotNull(completedEvent);
        Assert.Equal("missing_handler", completedEvent!.ToolName);
        Assert.Equal("failed", completedEvent.Status);
        var completedPayload = GetToolCallPayload(completedEvent);
        Assert.Equal("failed", ReadStructuredString(completedPayload, "status"));
        Assert.Equal("tool_call", ReadStructuredString(completedPayload, "phase"));
        Assert.Contains("dynamic tool call handler is not configured", ReadStructuredString(completedPayload, "outputText"), StringComparison.Ordinal);
    }

    public static IEnumerable<object?[]> StructuredNotificationCases()
    {
        yield return new object?[]
        {
            """
            {"method":"turn/steered","params":{"threadId":"thread-1","turnId":"turn-1","status":"accepted","source":"late_steer_input"}}
            """,
            ControlPlaneConversationStreamEventKind.TurnSteered,
            "accepted",
            "late_steer_input",
            (object?)null,
            "turn/steered",
        };

        yield return new object?[]
        {
            """
            {"method":"mcpServerStatus/list/updated","params":{"data":[{"name":"a"},{"name":"b"}]}}
            """,
            ControlPlaneConversationStreamEventKind.McpServerStatusUpdated,
            (object?)null,
            "mcp servers updated: 2",
            (object?)null,
            "mcpServerStatus/list/updated",
        };
    }

    public static IEnumerable<object?[]> StructuredNotificationMissingTurnIdCases()
    {
        yield return new object?[]
        {
            """
            {"method":"turn/diff/updated","params":{"threadId":"thread-structured-owner-1","diff":"old diff content"}}
            """,
            ControlPlaneConversationStreamEventKind.DiffUpdated,
            (string?)null,
            "old diff content",
            (string?)null,
            "turn/diff/updated",
        };

        yield return new object?[]
        {
            """
            {"method":"turn/plan/updated","params":{"threadId":"thread-structured-owner-1","explanation":"old plan update","plan":[{"step":"inspect","status":"pending"}]}}
            """,
            ControlPlaneConversationStreamEventKind.PlanUpdated,
            (string?)null,
            "old plan update",
            (string?)null,
            "turn/plan/updated",
        };

        yield return new object?[]
        {
            """
            {"method":"turn/steered","params":{"threadId":"thread-structured-owner-1","status":"accepted","source":"late_steer_input"}}
            """,
            ControlPlaneConversationStreamEventKind.TurnSteered,
            "accepted",
            "late_steer_input",
            (string?)null,
            "turn/steered",
        };
    }

    [Theory]
    [MemberData(nameof(StructuredNotificationCases))]
    public void HandleNotification_WhenSpecialKernelNotificationsArrive_RaisesTypedEvents(
        string notificationJson,
        ControlPlaneConversationStreamEventKind expectedKind,
        string? expectedStatus,
        string? expectedText,
        string? expectedToolName,
        string expectedMessage)
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notificationJson),
            notificationJson);

        var streamEvent = Assert.Single(events);
        Assert.Equal(expectedKind, streamEvent.Kind);
        Assert.Equal(expectedStatus, streamEvent.Status);
        Assert.Equal(expectedText, streamEvent.Text);
        Assert.Equal(expectedToolName, streamEvent.ToolName);
        Assert.Equal(expectedMessage, streamEvent.Message);

        switch (expectedKind)
        {
            case ControlPlaneConversationStreamEventKind.McpServerStatusUpdated:
            {
                var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.McpServerStatus);
                Assert.Equal("2", ReadStructuredString(payload, "count"));
                Assert.Equal(2, ReadStructuredItems(payload, "servers").Count);
                break;
            }
            case ControlPlaneConversationStreamEventKind.TurnSteered:
                Assert.Equal(expectedText, streamEvent.Source);
                Assert.Null(streamEvent.PayloadKind);
                Assert.Null(streamEvent.Payload);
                break;
        }
    }

    [Fact]
    public void HandleNotification_WhenDeprecationNoticeArrives_EmitsTypedPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string notificationJson =
            """
            {"method":"deprecationNotice","params":{"summary":"getAuthStatus 已废弃。","details":"请改用 account/read 获取账户状态。"}}
            """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notificationJson),
            notificationJson);

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.DeprecationNotice, streamEvent.Kind);
        Assert.Equal("getAuthStatus 已废弃。", streamEvent.Text);
        var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.DeprecationNotice);
        Assert.Equal("getAuthStatus 已废弃。", ReadStructuredString(payload, "summary"));
        Assert.Equal("请改用 account/read 获取账户状态。", ReadStructuredString(payload, "details"));
    }

    [Fact]
    public void HandleNotification_WhenThreadStatusChangedArrives_EmitsTypedPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string notificationJson =
            """
            {"method":"thread/status/changed","params":{"threadId":"thread-status-1","status":{"type":"active","activeFlags":["thinking","tool_call"]}}}
            """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notificationJson),
            notificationJson);

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.ThreadStatusChanged, streamEvent.Kind);
        Assert.Equal("thread-status-1", streamEvent.ThreadId?.Value);
        Assert.Equal("active", streamEvent.Status);
        var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadStatusChanged);
        Assert.Equal("active", ReadStructuredString(payload, "type"));
        Assert.Collection(
            ReadStructuredItems(payload, "activeFlags"),
            flag => Assert.Equal("thinking", ReadStructuredString(flag)),
            flag => Assert.Equal("tool_call", ReadStructuredString(flag)));
    }

    [Fact]
    public void HandleNotification_WhenThreadNameUpdatedArrives_EmitsTypedPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string notificationJson =
            """
            {"method":"thread/name/updated","params":{"threadId":"thread-name-1","turnId":"turn-name-1","threadName":"Contracts-first runtime"}} 
            """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notificationJson),
            notificationJson);

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.ThreadNameUpdated, streamEvent.Kind);
        Assert.Equal("thread-name-1", streamEvent.ThreadId?.Value);
        Assert.Equal("turn-name-1", streamEvent.TurnId?.Value);
        Assert.Equal("Contracts-first runtime", streamEvent.Text);
        var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadNameUpdated);
        Assert.Equal("Contracts-first runtime", ReadStructuredString(payload, "threadName"));
    }

    [Fact]
    public void HandleNotification_WhenThreadTokenUsageUpdatedArrives_EmitsTypedPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string notificationJson =
            """
            {"method":"thread/tokenUsage/updated","params":{"threadId":"thread-token-1","turnId":"turn-token-1","tokenUsage":{"last":{"totalTokens":12,"inputTokens":5,"cachedInputTokens":1,"outputTokens":4,"reasoningOutputTokens":2},"total":{"totalTokens":30,"inputTokens":11,"cachedInputTokens":3,"outputTokens":10,"reasoningOutputTokens":6},"modelContextWindow":200000}}}
            """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notificationJson),
            notificationJson);

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.ThreadTokenUsageUpdated, streamEvent.Kind);
        Assert.Equal("thread-token-1", streamEvent.ThreadId?.Value);
        Assert.Equal("turn-token-1", streamEvent.TurnId?.Value);
        Assert.Equal("tokens total=30", streamEvent.Text);
        var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadTokenUsage);
        Assert.Equal("30", ReadStructuredString(payload, "total", "totalTokens"));
        Assert.Equal("6", ReadStructuredString(payload, "total", "reasoningOutputTokens"));
        Assert.Equal("12", ReadStructuredString(payload, "last", "totalTokens"));
        Assert.Equal("200000", ReadStructuredString(payload, "modelContextWindow"));
    }

    [Fact]
    public void HandleNotification_WhenConfigWarningArrives_EmitsTypedPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string notificationJson =
            """
            {"method":"configWarning","params":{"summary":"配置文件中存在未知键。","details":"请移除 experimental.badFlag。","path":"D:/Work/TianShu/.tianshu/tianshu.toml","range":{"start":{"line":3,"column":5},"end":{"line":3,"column":24}}}}
            """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notificationJson),
            notificationJson);

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.ConfigWarning, streamEvent.Kind);
        Assert.Equal("配置文件中存在未知键。", streamEvent.Text);
        var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.ConfigWarning);
        Assert.Equal("配置文件中存在未知键。", ReadStructuredString(payload, "summary"));
        Assert.Equal("请移除 experimental.badFlag。", ReadStructuredString(payload, "details"));
        Assert.Equal("D:/Work/TianShu/.tianshu/tianshu.toml", ReadStructuredString(payload, "path"));
        Assert.Equal("3", ReadStructuredString(payload, "range", "start", "line"));
        Assert.Equal("24", ReadStructuredString(payload, "range", "end", "column"));
    }

    [Fact]
    public void HandleNotification_WhenCommandExecOutputDeltaArrives_EmitsTypedPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string notificationJson =
            """
            {"method":"command/exec/outputDelta","params":{"processId":"proc-17","stream":"stderr","deltaBase64":"ZXJyb3I6IGJhZA==","capReached":true}}
            """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notificationJson),
            notificationJson);

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.CommandExecOutputDelta, streamEvent.Kind);
        Assert.Equal("proc-17", streamEvent.CallId?.Value);
        Assert.Equal("stderr", streamEvent.Status);
        Assert.Equal("stderr output", streamEvent.Text);
        var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.CommandExecOutputDelta);
        Assert.Equal("proc-17", ReadStructuredString(payload, "processId"));
        Assert.Equal("stderr", ReadStructuredString(payload, "stream"));
        Assert.Equal("ZXJyb3I6IGJhZA==", ReadStructuredString(payload, "deltaBase64"));
        Assert.True(ReadStructuredBoolean(payload, "capReached"));
    }

    [Fact]
    public void HandleNotification_WhenAppListUpdatedArrives_EmitsTypedPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string notificationJson =
            """
            {"method":"app/list/updated","params":{"data":[{"id":"app-1","name":"TianShu VSIX","description":"Visual Studio integration","distributionChannel":"marketplace","branding":{"category":"developer-tools","developer":"Example","website":"https://tianshu.example.com","isDiscoverableApp":true},"metadata":{"review":{"status":"approved","message":"ready"},"screenshots":[{"caption":"主界面","url":"https://example.com/1.png"}]},"labels":{"platform":"vs"},"isAccessible":true,"isEnabled":true,"installUrl":"https://example.com/install","pluginDisplayNames":["vsix","sidecar"]}]}}
            """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notificationJson),
            notificationJson);

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.AppListUpdated, streamEvent.Kind);
        Assert.Equal("apps updated: 1", streamEvent.Text);
        var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.AppListUpdated);
        var item = Assert.Single(ReadStructuredItems(payload, "items"));
        Assert.Equal("app-1", ReadStructuredString(item, "id"));
        Assert.Equal("TianShu VSIX", ReadStructuredString(item, "name"));
        Assert.Equal("developer-tools", ReadStructuredString(item, "branding", "category"));
        Assert.Equal("approved", ReadStructuredString(item, "metadata", "review", "status"));
        Assert.Equal("主界面", ReadStructuredString(item, "metadata", "screenshots", 0, "caption"));
        Assert.Equal("vs", ReadStructuredString(item, "labels", "platform"));
        Assert.True(ReadStructuredBoolean(item, "isAccessible"));
        Assert.Equal("sidecar", ReadStructuredString(item, "pluginDisplayNames", 1));
    }

    [Fact]
    public async Task HandleNotification_WhenThreadStatusBecomesIdleAfterInterruptRequest_CompletesPendingTurn()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-status-idle-001");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-status-idle-001");
        var interruptRequestedTurns = Assert.IsType<ConcurrentDictionary<string, byte>>(
            ReflectionTestHelper.GetField(runtime, "interruptRequestedTurns"));
        interruptRequestedTurns["turn-status-idle-001"] = 0;

        var waitTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "WaitTurnCompletionAsync",
            "turn-status-idle-001",
            CancellationToken.None);

        const string notificationJson =
            """
            {"method":"thread/status/changed","params":{"threadId":"thread-status-idle-001","status":{"type":"idle","activeFlags":[]}}}
            """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notificationJson),
            notificationJson);

        var completion = await ReflectionTestHelper.AwaitTaskResultAsync(waitTask);
        Assert.NotNull(completion);
        Assert.False((bool)(completion!.GetType().GetProperty("Success")?.GetValue(completion) ?? true));
        Assert.Equal("interrupted", completion.GetType().GetProperty("Status")?.GetValue(completion));
        Assert.Equal("回合已中断。", completion.GetType().GetProperty("ErrorMessage")?.GetValue(completion));
        Assert.False(runtime.HasActiveTurn);
        Assert.Null(ReflectionTestHelper.GetField(runtime, "activeTurnId"));
        Assert.Null(ReflectionTestHelper.GetField(runtime, "submittedTurnId"));
        Assert.Empty(interruptRequestedTurns);

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.ThreadStatusChanged, streamEvent.Kind);
        Assert.Equal("idle", streamEvent.Status);
        var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadStatusChanged);
        Assert.Equal("idle", ReadStructuredString(payload, "type"));
        Assert.Empty(ReadStructuredItems(payload, "activeFlags"));
    }

    [Fact]
    public void HandleNotification_WhenThreadRealtimeOutputAudioDeltaArrives_EmitsTypedPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string notificationJson =
            """
            {"method":"thread/realtime/outputAudio/delta","params":{"threadId":"thread-realtime-1","audio":{"data":"UklGRg==","sampleRate":24000,"numChannels":1,"samplesPerChannel":480}}}
            """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notificationJson),
            notificationJson);

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.ThreadRealtimeOutputAudioDelta, streamEvent.Kind);
        Assert.Equal("thread-realtime-1", streamEvent.ThreadId?.Value);
        var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.ThreadRealtimeOutputAudioDelta);
        Assert.Equal("UklGRg==", ReadStructuredString(payload, "data"));
        Assert.Equal("24000", ReadStructuredString(payload, "sampleRate"));
        Assert.Equal("1", ReadStructuredString(payload, "numChannels"));
        Assert.Equal("480", ReadStructuredString(payload, "samplesPerChannel"));
    }

    [Theory]
    [MemberData(nameof(StructuredNotificationMissingTurnIdCases))]
    public void HandleNotification_WhenSameThreadNewTurnIsActiveAndStructuredNotificationOmitsTurnId_AttributesEventToObservedOldTurn(
        string notificationJson,
        ControlPlaneConversationStreamEventKind expectedKind,
        string? expectedStatus,
        string? expectedText,
        string? expectedToolName,
        string expectedMessage)
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string oldTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-structured-owner-1","turn":{"id":"turn-structured-owner-1-old","status":"inProgress"}}}
        """;
        const string newTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-structured-owner-1","turn":{"id":"turn-structured-owner-1-new","status":"inProgress"}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(oldTurnStarted), oldTurnStarted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(newTurnStarted), newTurnStarted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(notificationJson), notificationJson);

        var streamEvent = Assert.Single(events.Where(item => item.Kind == expectedKind));
        Assert.Equal("thread-structured-owner-1", streamEvent.ThreadId?.Value);
        Assert.Equal("turn-structured-owner-1-old", streamEvent.TurnId?.Value);
        Assert.Equal(expectedStatus, streamEvent.Status);
        Assert.Equal(expectedText, streamEvent.Text);
        Assert.Equal(expectedToolName, streamEvent.ToolName);
        Assert.Equal(expectedMessage, streamEvent.Message);
        Assert.Equal("turn-structured-owner-1-new", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.RawNotification);

        switch (expectedKind)
        {
            case ControlPlaneConversationStreamEventKind.PlanUpdated:
            {
                var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.Plan);
                Assert.Equal("old plan update", ReadStructuredString(payload, "explanation"));
                Assert.Equal("inspect", ReadStructuredString(payload, "steps", 0, "step"));
                Assert.Equal("pending", ReadStructuredString(payload, "steps", 0, "status"));
                break;
            }
            case ControlPlaneConversationStreamEventKind.TurnSteered:
                Assert.Equal(expectedText, streamEvent.Source);
                Assert.Null(streamEvent.PayloadKind);
                Assert.Null(streamEvent.Payload);
                break;
        }
    }

    [Fact]
    public void HandleNotification_WhenItemToolCallLifecycleArrives_RaisesTypedToolEvents()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string started = """
        {"method":"item/tool/call","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"tool-call-1","type":"tool_call","toolName":"shell","status":"inProgress","arguments":"{\"command\":\"dotnet --version\"}"}}}
        """;
        const string completed = """
        {"method":"item/tool/call","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"tool-call-1","type":"tool_call","toolName":"shell","status":"completed","arguments":"{\"command\":\"dotnet --version\"}","output":"10.0.202"}}}
        """;
        const string failed = """
        {"method":"item/tool/call","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"tool-call-2","type":"tool_call","toolName":"tool_search","status":"failed","output":"search failed"}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(started), started);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(completed), completed);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(failed), failed);

        var toolEvents = events
            .Where(static item => item.Kind is ControlPlaneConversationStreamEventKind.ToolCallStarted or ControlPlaneConversationStreamEventKind.ToolCallCompleted)
            .ToArray();
        Assert.Equal(3, toolEvents.Length);

        Assert.Equal(ControlPlaneConversationStreamEventKind.ToolCallStarted, toolEvents[0].Kind);
        Assert.Equal("shell", toolEvents[0].ToolName);
        Assert.Equal("tool-call-1", toolEvents[0].CallId?.Value);
        Assert.Equal("inProgress", toolEvents[0].Status);
        var startedPayload = GetPayloadValue(toolEvents[0], ControlPlaneConversationStreamPayloadKind.ToolCall);
        Assert.Equal("shell", ReadStructuredString(startedPayload, "toolName"));
        Assert.Equal("tool-call-1", ReadStructuredString(startedPayload, "callId"));
        Assert.Equal("inProgress", ReadStructuredString(startedPayload, "status"));
        Assert.Equal("{\"command\":\"dotnet --version\"}", ReadStructuredString(startedPayload, "inputText"));

        Assert.Equal(ControlPlaneConversationStreamEventKind.ToolCallCompleted, toolEvents[1].Kind);
        Assert.Equal("shell", toolEvents[1].ToolName);
        Assert.Equal("tool-call-1", toolEvents[1].CallId?.Value);
        Assert.Equal("completed", toolEvents[1].Status);
        Assert.Equal("10.0.202", toolEvents[1].Text);
        var completedPayload = GetPayloadValue(toolEvents[1], ControlPlaneConversationStreamPayloadKind.ToolCall);
        Assert.Equal("{\"command\":\"dotnet --version\"}", ReadStructuredString(completedPayload, "inputText"));
        Assert.Equal("10.0.202", ReadStructuredString(completedPayload, "outputText"));
        Assert.Equal("completed", ReadStructuredString(completedPayload, "status"));

        Assert.Equal(ControlPlaneConversationStreamEventKind.ToolCallCompleted, toolEvents[2].Kind);
        Assert.Equal("tool-call-2", toolEvents[2].CallId?.Value);
        Assert.Equal("failed", toolEvents[2].Status);
        Assert.Equal("search failed", toolEvents[2].Text);
        var failedPayload = GetPayloadValue(toolEvents[2], ControlPlaneConversationStreamPayloadKind.ToolCall);
        Assert.Equal("search failed", ReadStructuredString(failedPayload, "outputText"));
        Assert.Equal("failed", ReadStructuredString(failedPayload, "status"));
    }

    [Fact]
    public void HandleNotification_WhenFileChangeItemCompletes_PreservesRawItemAsToolInput()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string completed = """
        {"method":"item/completed","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"file-change-1","type":"fileChange","status":"completed","changes":[{"path":"src/App.xaml","kind":"update","diff":"<Window />"}]}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(completed), completed);

        var completedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted));
        Assert.Equal("fileChange", completedEvent.ToolName);
        Assert.Equal("file-change-1", completedEvent.CallId?.Value);
        var payload = GetPayloadValue(completedEvent, ControlPlaneConversationStreamPayloadKind.ToolCall);
        var inputText = ReadStructuredString(payload, "inputText");
        Assert.NotNull(inputText);
        using var input = JsonDocument.Parse(inputText!);
        Assert.Equal("fileChange", input.RootElement.GetProperty("type").GetString());
        Assert.Equal("src/App.xaml", input.RootElement.GetProperty("changes")[0].GetProperty("path").GetString());
    }

    [Fact]
    public void HandleNotification_WhenCommandExecutionOutputDeltaArrives_ProjectsThroughProviderBoundary()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string outputDelta = """
        {"method":"item/commandExecution/outputDelta","params":{"threadId":"thread-tool-delta-1","turnId":"turn-tool-delta-1","itemId":"command-tool-1","type":"commandExecution","status":"delta","input":"dir /b","delta":"line 1","item":{"id":"command-tool-1","type":"commandExecution","status":"delta","input":"dir /b","delta":"line 1"}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(outputDelta), outputDelta);

        var streamEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallOutputDelta));
        Assert.Equal("thread-tool-delta-1", streamEvent.ThreadId?.Value);
        Assert.Equal("turn-tool-delta-1", streamEvent.TurnId?.Value);
        Assert.Equal("command-tool-1", streamEvent.CallId?.Value);
        Assert.Equal("commandExecution", streamEvent.ToolName);
        Assert.Equal("line 1", streamEvent.Text);
        Assert.Equal("item/commandExecution/outputDelta", streamEvent.SourceMethod);
        var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.ToolCall);
        Assert.Equal("command-tool-1", ReadStructuredString(payload, "callId"));
        Assert.Equal("dir /b", ReadStructuredString(payload, "inputText"));
        Assert.Equal("line 1", ReadStructuredString(payload, "outputText"));
    }

    [Fact]
    public void HandleNotification_WhenCommandExecutionTerminalInteractionArrives_ProjectsThroughProviderBoundary()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string terminalInteraction = """
        {"method":"item/commandExecution/terminalInteraction","params":{"threadId":"thread-tool-terminal-1","turnId":"turn-tool-terminal-1","itemId":"command-tool-terminal-1","type":"commandExecution","status":"running","processId":"42","stdin":"y"}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(terminalInteraction), terminalInteraction);

        var streamEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallOutputDelta));
        Assert.Equal("thread-tool-terminal-1", streamEvent.ThreadId?.Value);
        Assert.Equal("turn-tool-terminal-1", streamEvent.TurnId?.Value);
        Assert.Equal("command-tool-terminal-1", streamEvent.CallId?.Value);
        Assert.Equal("commandExecution", streamEvent.ToolName);
        Assert.Equal("terminal<42> y", streamEvent.Text);
        Assert.Equal("item/commandExecution/terminalInteraction", streamEvent.SourceMethod);
        var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.ToolCall);
        Assert.Equal("command-tool-terminal-1", ReadStructuredString(payload, "callId"));
        Assert.Equal("y", ReadStructuredString(payload, "inputText"));
        Assert.Equal("terminal<42> y", ReadStructuredString(payload, "outputText"));
    }

    [Fact]
    public void HandleNotification_WhenMcpServerStatusUpdatedArrives_RaisesTypedServerEntries()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string notification = """
        {"method":"mcpServerStatus/list/updated","params":{"data":[{"name":"filesystem","authStatus":"authorized","tools":{"read_file":{}},"resources":[{"uri":"file://README.md"}],"resourceTemplates":[]},{"name":"github","authStatus":"unauthorized","tools":{"search_repos":{},"list_prs":{}},"resources":[],"resourceTemplates":[{"uriTemplate":"github://repos/{owner}/{repo}"}]}]}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(notification), notification);

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.McpServerStatusUpdated, streamEvent.Kind);
        var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.McpServerStatus);
        Assert.Equal("2", ReadStructuredString(payload, "count"));
        Assert.Collection(
            ReadStructuredItems(payload, "servers"),
            server =>
            {
                Assert.Equal("filesystem", ReadStructuredString(server, "name"));
                Assert.Equal("authorized", ReadStructuredString(server, "authStatus"));
                Assert.Equal("1", ReadStructuredString(server, "toolCount"));
                Assert.Equal("1", ReadStructuredString(server, "resourceCount"));
                Assert.Equal("0", ReadStructuredString(server, "resourceTemplateCount"));
            },
            server =>
            {
                Assert.Equal("github", ReadStructuredString(server, "name"));
                Assert.Equal("unauthorized", ReadStructuredString(server, "authStatus"));
                Assert.Equal("2", ReadStructuredString(server, "toolCount"));
                Assert.Equal("0", ReadStructuredString(server, "resourceCount"));
                Assert.Equal("1", ReadStructuredString(server, "resourceTemplateCount"));
            });
    }

    [Fact]
    public void HandleNotification_WhenThreadClosedOmitsTurnId_DoesNotBorrowActiveTurnId()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-close-active-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-close-active-1");

        const string notification = """
        {"method":"thread/closed","params":{"threadId":"thread-close-active-1"}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(notification), notification);

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.Info, streamEvent.Kind);
        Assert.Equal("thread-close-active-1", streamEvent.ThreadId?.Value);
        Assert.Null(streamEvent.TurnId);
        Assert.Equal("thread/closed", streamEvent.Message);
        Assert.Null(streamEvent.PayloadKind);
        Assert.Null(ReflectionTestHelper.GetField(runtime, "activeThreadId"));
        Assert.Null(ReflectionTestHelper.GetField(runtime, "activeTurnId"));
        Assert.False(runtime.HasActiveTurn);
    }

    [Fact]
    public void HandleNotification_WhenMcpServerStatusUpdatedOmitsTurnId_DoesNotBorrowActiveTurnId()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-mcp-active-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-mcp-active-1");

        const string notification = """
        {"method":"mcpServerStatus/list/updated","params":{"threadId":"thread-mcp-active-1","data":[{"name":"filesystem"}]}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(notification), notification);

        var streamEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.McpServerStatusUpdated, streamEvent.Kind);
        Assert.Equal("thread-mcp-active-1", streamEvent.ThreadId?.Value);
        Assert.Null(streamEvent.TurnId);
        var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.McpServerStatus);
        Assert.Equal("1", ReadStructuredString(payload, "count"));
        Assert.Equal("filesystem", ReadStructuredString(payload, "servers", 0, "name"));
        Assert.Equal("turn-mcp-active-1", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
    }

    [Fact]
    public void HandleNotification_WhenTurnStartedOmitsAllTurnIdentifiers_UsesSubmittedTurnIdInsteadOfActiveTurnId()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-turn-start-missing-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-turn-start-old-1");
        ReflectionTestHelper.SetField(runtime, "submittedTurnId", "turn-turn-start-submitted-1");

        const string notification = """
        {"method":"turn/started","params":{"threadId":"thread-turn-start-missing-1","turn":{"status":"inProgress"}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(notification), notification);

        var streamEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.TurnStarted));
        Assert.Equal("thread-turn-start-missing-1", streamEvent.ThreadId?.Value);
        Assert.Equal("turn-turn-start-submitted-1", streamEvent.TurnId?.Value);
        Assert.Null(streamEvent.PayloadKind);
        Assert.Equal("turn-turn-start-submitted-1", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
        Assert.Null(ReflectionTestHelper.GetField(runtime, "submittedTurnId"));
    }

    [Fact]
    public void HandleNotification_WhenUnknownNotificationOmitsIdentifiers_DoesNotBorrowActiveContext()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-raw-active-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-raw-active-1");

        const string notification = """
        {"method":"thread/customUpdated","params":{"value":"demo"}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(notification), notification);

        var rawEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.RawNotification, rawEvent.Kind);
        Assert.Null(rawEvent.ThreadId);
        Assert.Null(rawEvent.TurnId);
        Assert.Equal("thread/customUpdated", rawEvent.Message);
        Assert.Null(rawEvent.PayloadKind);
        Assert.Equal("turn-raw-active-1", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
    }

    [Fact]
    public async Task HandleToolRequestUserInputAsync_WhenCancelled_RemovesPendingRequest()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-input-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-input-1");

        using var cancellation = new CancellationTokenSource();
        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/tool/requestUserInput",
                  "id": 9,
                  "params": {
                    "threadId": "thread-input-1",
                    "turnId": "turn-input-1",
                    "itemId": "input-call-1",
                    "questions": [
                      {
                        "id": "notes",
                        "header": "Notes",
                        "question": "Notes",
                        "isOther": true,
                        "isSecret": false,
                        "options": null
                      }
                    ]
                  }
                }
                """),
            cancellation.Token);

        await Task.Delay(20);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReflectionTestHelper.AwaitTaskResultAsync(handlerTask));
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingUserInputRequests"))!.Cast<object>());

        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var writtenPayload = await reader.ReadToEndAsync();
        Assert.True(string.IsNullOrWhiteSpace(writtenPayload));
    }

    [Fact]
    public async Task HandleNotification_WhenServerRequestResolvedMatchesPendingUserInput_CancelsPendingRequestAndEmitsTypedEvent()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-input-2");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-input-2");

        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/tool/requestUserInput",
                  "id": 42,
                  "params": {
                    "threadId": "thread-input-2",
                    "turnId": "turn-input-2",
                    "itemId": "input-call-2",
                    "questions": [
                      {
                        "id": "notes",
                        "header": "Notes",
                        "question": "Notes",
                        "isOther": true,
                        "isSecret": false,
                        "options": null
                      }
                    ]
                  }
                }
                """),
            CancellationToken.None);

        await Task.Delay(20);

        const string resolvedNotification =
            """
            {
              "method": "serverRequest/resolved",
              "params": {
                "threadId": "thread-input-2",
                "requestId": 42
              }
            }
            """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(resolvedNotification),
            resolvedNotification);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReflectionTestHelper.AwaitTaskResultAsync(handlerTask));
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingUserInputRequests"))!.Cast<object>());

        var resolvedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ServerRequestResolved));
        Assert.Equal("thread-input-2", resolvedEvent.ThreadId?.Value);
        Assert.Equal("turn-input-2", resolvedEvent.TurnId?.Value);
        Assert.Equal("input-call-2", resolvedEvent.CallId?.Value);
        Assert.Equal("requestUserInput", resolvedEvent.ToolName);
        var payload = GetPayloadValue(resolvedEvent, ControlPlaneConversationStreamPayloadKind.ServerRequestResolved);
        Assert.Equal(StructuredValueKind.Number, ReadStructuredValue(payload, "requestId")!.Kind);
        Assert.Equal("42", ReadStructuredString(payload, "requestId"));
        Assert.Equal("request_user_input", ReadStructuredString(payload, "requestKind"));
        Assert.Equal("input-call-2", ReadStructuredString(payload, "callId"));

        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var writtenPayload = await reader.ReadToEndAsync();
        Assert.True(string.IsNullOrWhiteSpace(writtenPayload));
    }

    [Fact]
    public async Task HandleNotification_WhenServerRequestResolvedMatchesPendingApproval_CancelsPendingRequestAndEmitsTypedEvent()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-approval-resolved-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-approval-resolved-1");

        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/tool/requestApproval",
                  "id": 52,
                  "params": {
                    "threadId": "thread-approval-resolved-1",
                    "turnId": "turn-approval-resolved-1",
                    "itemId": "approval-item-resolved-1",
                    "callId": "approval-call-resolved-1",
                    "toolName": "shell",
                    "command": ["cmd.exe", "/c", "echo hi"]
                  }
                }
                """),
            CancellationToken.None);

        await Task.Delay(20);

        const string resolvedNotification =
            """
            {
              "method": "serverRequest/resolved",
              "params": {
                "threadId": "thread-approval-resolved-1",
                "requestId": 52
              }
            }
            """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(resolvedNotification),
            resolvedNotification);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReflectionTestHelper.AwaitTaskResultAsync(handlerTask));
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingApprovalRequests"))!.Cast<object>());

        var resolvedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ServerRequestResolved));
        Assert.Equal("thread-approval-resolved-1", resolvedEvent.ThreadId?.Value);
        Assert.Equal("turn-approval-resolved-1", resolvedEvent.TurnId?.Value);
        Assert.Equal("approval-call-resolved-1", resolvedEvent.CallId?.Value);
        Assert.Equal("shell", resolvedEvent.ToolName);
        var payload = GetPayloadValue(resolvedEvent, ControlPlaneConversationStreamPayloadKind.ServerRequestResolved);
        Assert.Equal(StructuredValueKind.Number, ReadStructuredValue(payload, "requestId")!.Kind);
        Assert.Equal("52", ReadStructuredString(payload, "requestId"));
        Assert.Equal("approval_requested", ReadStructuredString(payload, "requestKind"));
        Assert.Equal("approval-call-resolved-1", ReadStructuredString(payload, "callId"));

        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var writtenPayload = await reader.ReadToEndAsync();
        Assert.True(string.IsNullOrWhiteSpace(writtenPayload));
    }

    [Fact]
    public async Task HandleNotification_WhenServerRequestResolvedMatchesPendingPermission_CancelsPendingRequestAndEmitsTypedEvent()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-permission-resolved-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-permission-resolved-1");

        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/permissions/requestApproval",
                  "id": 53,
                  "params": {
                    "threadId": "thread-permission-resolved-1",
                    "turnId": "turn-permission-resolved-1",
                    "itemId": "permission-call-resolved-1",
                    "reason": "Need broader access",
                    "permissions": {
                      "network": {
                        "enabled": true
                      }
                    }
                  }
                }
                """),
            CancellationToken.None);

        await Task.Delay(20);

        const string resolvedNotification =
            """
            {
              "method": "serverRequest/resolved",
              "params": {
                "threadId": "thread-permission-resolved-1",
                "requestId": 53
              }
            }
            """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(resolvedNotification),
            resolvedNotification);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReflectionTestHelper.AwaitTaskResultAsync(handlerTask));
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingPermissionRequests"))!.Cast<object>());

        var resolvedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ServerRequestResolved));
        Assert.Equal("thread-permission-resolved-1", resolvedEvent.ThreadId?.Value);
        Assert.Equal("turn-permission-resolved-1", resolvedEvent.TurnId?.Value);
        Assert.Equal("permission-call-resolved-1", resolvedEvent.CallId?.Value);
        Assert.Equal("request_permissions", resolvedEvent.ToolName);
        var payload = GetPayloadValue(resolvedEvent, ControlPlaneConversationStreamPayloadKind.ServerRequestResolved);
        Assert.Equal(StructuredValueKind.Number, ReadStructuredValue(payload, "requestId")!.Kind);
        Assert.Equal("53", ReadStructuredString(payload, "requestId"));
        Assert.Equal("permission_requested", ReadStructuredString(payload, "requestKind"));
        Assert.Equal("permission-call-resolved-1", ReadStructuredString(payload, "callId"));

        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var writtenPayload = await reader.ReadToEndAsync();
        Assert.True(string.IsNullOrWhiteSpace(writtenPayload));
    }

    [Fact]
    public async Task HandleNotification_WhenServerRequestResolvedUsesStringRequestId_CancelsPendingRequestAndEmitsRawId()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-approval-resolved-string-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-approval-resolved-string-1");

        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/tool/requestApproval",
                  "id": "approval-request-id-string-1",
                  "params": {
                    "threadId": "thread-approval-resolved-string-1",
                    "turnId": "turn-approval-resolved-string-1",
                    "itemId": "approval-item-resolved-string-1",
                    "callId": "approval-call-resolved-string-1",
                    "toolName": "shell",
                    "command": ["cmd.exe", "/c", "echo hi"]
                  }
                }
                """),
            CancellationToken.None);

        await Task.Delay(20);

        const string resolvedNotification =
            """
            {
              "method": "serverRequest/resolved",
              "params": {
                "threadId": "thread-approval-resolved-string-1",
                "requestId": "approval-request-id-string-1"
              }
            }
            """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(resolvedNotification),
            resolvedNotification);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReflectionTestHelper.AwaitTaskResultAsync(handlerTask));
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingApprovalRequests"))!.Cast<object>());

        var resolvedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ServerRequestResolved));
        Assert.Equal("thread-approval-resolved-string-1", resolvedEvent.ThreadId?.Value);
        Assert.Equal("turn-approval-resolved-string-1", resolvedEvent.TurnId?.Value);
        Assert.Equal("approval-call-resolved-string-1", resolvedEvent.CallId?.Value);
        Assert.Equal("shell", resolvedEvent.ToolName);
        var payload = GetPayloadValue(resolvedEvent, ControlPlaneConversationStreamPayloadKind.ServerRequestResolved);
        Assert.Equal(StructuredValueKind.Number, ReadStructuredValue(payload, "requestId")!.Kind);
        Assert.Equal("0", ReadStructuredString(payload, "requestId"));
        Assert.Equal("approval-request-id-string-1", ReadStructuredString(payload, "requestIdRaw"));
        Assert.Equal("approval_requested", ReadStructuredString(payload, "requestKind"));
        Assert.Equal("approval-call-resolved-string-1", ReadStructuredString(payload, "callId"));

        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var writtenPayload = await reader.ReadToEndAsync();
        Assert.True(string.IsNullOrWhiteSpace(writtenPayload));
    }

    [Fact]
    public async Task HandleNotification_WhenTurnCompletedArrivesBeforeResolved_FallbackClearsPendingInteractiveStateForTurn()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-turn-cleanup-001");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-turn-cleanup-001");

        var approvalTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/tool/requestApproval",
                  "id": 61,
                  "params": {
                    "threadId": "thread-turn-cleanup-001",
                    "turnId": "turn-turn-cleanup-001",
                    "itemId": "approval-item-turn-cleanup-001",
                    "callId": "approval-call-turn-cleanup-001",
                    "toolName": "shell",
                    "command": ["cmd.exe", "/c", "echo hi"]
                  }
                }
                """),
            CancellationToken.None);
        var permissionTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/permissions/requestApproval",
                  "id": 62,
                  "params": {
                    "threadId": "thread-turn-cleanup-001",
                    "turnId": "turn-turn-cleanup-001",
                    "itemId": "permission-call-turn-cleanup-001",
                    "reason": "Need broader access",
                    "permissions": {
                      "network": {
                        "enabled": true
                      }
                    }
                  }
                }
                """),
            CancellationToken.None);
        var userInputTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/tool/requestUserInput",
                  "id": 63,
                  "params": {
                    "threadId": "thread-turn-cleanup-001",
                    "turnId": "turn-turn-cleanup-001",
                    "itemId": "input-call-turn-cleanup-001",
                    "questions": [
                      {
                        "id": "notes",
                        "header": "Notes",
                        "question": "Notes",
                        "isOther": true,
                        "isSecret": false,
                        "options": null
                      }
                    ]
                  }
                }
                """),
            CancellationToken.None);

        await Task.Delay(20);

        const string approvalNotification =
            """
            {
              "method": "item/tool/requestApproval",
              "params": {
                "threadId": "thread-turn-cleanup-001",
                "turnId": "turn-turn-cleanup-001",
                "itemId": "approval-item-orphan-turn-cleanup-001",
                "callId": "approval-orphan-turn-cleanup-001",
                "toolName": "shell",
                "type": "tool",
                "status": "awaitingApproval",
                "requiresApproval": true,
                "arguments": "echo hi"
              }
            }
            """;
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(approvalNotification),
            approvalNotification);

        const string turnCompleted =
            """
            {
              "method": "turn/completed",
              "params": {
                "threadId": "thread-turn-cleanup-001",
                "turn": {
                  "id": "turn-turn-cleanup-001",
                  "status": "completed",
                  "items": []
                }
              }
            }
            """;
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(turnCompleted),
            turnCompleted);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReflectionTestHelper.AwaitTaskResultAsync(approvalTask));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReflectionTestHelper.AwaitTaskResultAsync(permissionTask));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReflectionTestHelper.AwaitTaskResultAsync(userInputTask));

        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingApprovalRequests"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingPermissionRequests"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingUserInputRequests"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingInteractiveServerRequestsByRequestId"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingInteractiveServerRequestIdsByCallId"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingInteractiveReplayContextsByCallId"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingApprovals"))!.Cast<object>());

        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        Assert.True(string.IsNullOrWhiteSpace(payload));
    }

    [Fact]
    public void HandleNotification_WhenRawResponseCompletesAfterDuplicateDeltaSources_PreservesStreamedAssistantText()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string agentMessageDelta = """
        {"method":"item/agentMessage/delta","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"msg-1","item":{"id":"msg-1","type":"agentMessage","delta":"commentary"},"delta":"commentary"}}
        """;
        const string assistantTextDelta = """
        {"method":"item/delta","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"msg-1","type":"assistant_text","item":{"id":"msg-1","type":"assistant_text","delta":"final answer"},"delta":"final answer"}}
        """;
        const string responseCompleted = """
        {"method":"rawResponseItem/completed","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"msg-1","type":"agentMessage","status":"completed","text":"commentary completed"}}}
        """;
        const string turnCompleted = """
        {"method":"turn/completed","params":{"threadId":"thread-1","turn":{"id":"turn-1","status":"completed","items":[]}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(agentMessageDelta), agentMessageDelta);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(assistantTextDelta), assistantTextDelta);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(responseCompleted), responseCompleted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(turnCompleted), turnCompleted);

        var commentaryEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ReasoningDelta));
        Assert.Equal("commentary", commentaryEvent.Text);
        Assert.Equal("item/agentMessage/delta", commentaryEvent.SourceMethod);
        var commentaryPayload = GetPayloadValue(commentaryEvent, ControlPlaneConversationStreamPayloadKind.Reasoning);
        Assert.Equal("commentary", ReadStructuredString(commentaryPayload, "text"));
        Assert.Equal("commentary", ReadStructuredString(commentaryPayload, "phase"));

        var deltaEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.AssistantTextDelta));
        Assert.Equal("final answer", deltaEvent.Text);
        Assert.Equal("assistant_text", deltaEvent.Status);

        var completedAssistantEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.AssistantTextCompleted));
        Assert.Equal("final answer", completedAssistantEvent.Text);
        Assert.Equal("agentMessage", completedAssistantEvent.Status);
        Assert.Equal("final_answer", completedAssistantEvent.Phase);

        var turnCompletedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.TurnCompleted));
        Assert.Equal("final answer", turnCompletedEvent.Text);
        Assert.Equal("final_answer", turnCompletedEvent.Phase);
    }

    [Fact]
    public void HandleNotification_WhenAssistantDeltaArrives_DoesNotMisclassifyAssistantTextAsToolOutput()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string assistantTextDelta = """
        {"method":"item/delta","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"msg-1","type":"assistant_text","item":{"id":"msg-1","type":"assistant_text","delta":"hello"},"delta":"hello"}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(assistantTextDelta), assistantTextDelta);

        var assistantEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.AssistantTextDelta));
        Assert.Equal("hello", assistantEvent.Text);
        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallOutputDelta);
    }

    [Fact]
    public void HandleNotification_WhenAssistantMirrorNotificationsArrive_DoesNotTreatThemAsToolLifecycle()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string agentMessageDelta = """
        {"method":"item/agentMessage/delta","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"msg-1","item":{"id":"msg-1","type":"agentMessage","delta":"mirror"},"delta":"mirror"}}
        """;
        const string agentMessageCompleted = """
        {"method":"item/completed","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"msg-1","type":"agentMessage","text":"done"}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(agentMessageDelta), agentMessageDelta);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(agentMessageCompleted), agentMessageCompleted);

        var commentaryEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ReasoningDelta));
        Assert.Equal("mirror", commentaryEvent.Text);
        Assert.Equal("item/agentMessage/delta", commentaryEvent.SourceMethod);
        var commentaryPayload = GetPayloadValue(commentaryEvent, ControlPlaneConversationStreamPayloadKind.Reasoning);
        Assert.Equal("mirror", ReadStructuredString(commentaryPayload, "text"));
        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallOutputDelta);
        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted);
        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.AssistantTextDelta);
    }

    [Fact]
    public void HandleNotification_WhenAgentJobProgressCommentaryArrives_EmitsStructuredAgentJobProgressEvent()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string agentJobProgressDelta = """
        {"method":"item/agentMessage/delta","params":{"threadId":"thread-agent-job-1","turnId":"turn-agent-job-1","itemId":"agent_job_progress_job-1","item":{"id":"agent_job_progress_job-1","type":"agentMessage","delta":"agent_job_progress:{\"job_id\":\"job-1\",\"total_items\":3,\"pending_items\":1,\"running_items\":1,\"completed_items\":1,\"failed_items\":0,\"eta_seconds\":12}"},"delta":"agent_job_progress:{\"job_id\":\"job-1\",\"total_items\":3,\"pending_items\":1,\"running_items\":1,\"completed_items\":1,\"failed_items\":0,\"eta_seconds\":12}"}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(agentJobProgressDelta), agentJobProgressDelta);

        var progressEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.AgentJobProgress));
        Assert.Equal("thread-agent-job-1", progressEvent.ThreadId?.Value);
        Assert.Equal("turn-agent-job-1", progressEvent.TurnId?.Value);
        var payload = GetPayloadValue(progressEvent, ControlPlaneConversationStreamPayloadKind.AgentJobProgress);
        Assert.Equal("job-1", ReadStructuredString(payload, "jobId"));
        Assert.Equal("3", ReadStructuredString(payload, "totalItems"));
        Assert.Equal("1", ReadStructuredString(payload, "pendingItems"));
        Assert.Equal("1", ReadStructuredString(payload, "runningItems"));
        Assert.Equal("1", ReadStructuredString(payload, "completedItems"));
        Assert.Equal("0", ReadStructuredString(payload, "failedItems"));
        Assert.Equal("12", ReadStructuredString(payload, "etaSeconds"));
        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.ReasoningDelta);
        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.RawNotification);
    }

    [Fact]
    public void HandleNotification_WhenReasoningDeltaArrives_EmitsStructuredReasoningEvent()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string reasoningDelta = """
        {"method":"item/reasoning/summaryTextDelta","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"reason-1","delta":"thinking","summaryIndex":0}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(reasoningDelta), reasoningDelta);

        var reasoningEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ReasoningDelta));
        Assert.Equal("thinking", reasoningEvent.Text);
        Assert.Equal("item/reasoning/summaryTextDelta", reasoningEvent.SourceMethod);
        var payload = GetPayloadValue(reasoningEvent, ControlPlaneConversationStreamPayloadKind.Reasoning);
        Assert.Equal("thinking", ReadStructuredString(payload, "text"));
        Assert.Equal("reasoning", reasoningEvent.Phase);
        Assert.Equal("0", ReadStructuredString(payload, "summaryIndex"));
        AssertStructuredNull(ReadStructuredValue(payload, "contentIndex"));
        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.RawNotification);
    }

    [Fact]
    public void HandleNotification_WhenReasoningSummaryPartAddedArrives_EmitsStructuredReasoningMarkerEvent()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string summaryPartAdded = """
        {"method":"item/reasoning/summaryPartAdded","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"reason-1","summaryIndex":1}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(summaryPartAdded), summaryPartAdded);

        var reasoningEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ReasoningDelta));
        Assert.Null(reasoningEvent.Text);
        Assert.Equal("item/reasoning/summaryPartAdded", reasoningEvent.SourceMethod);
        var payload = GetPayloadValue(reasoningEvent, ControlPlaneConversationStreamPayloadKind.Reasoning);
        Assert.Equal("1", ReadStructuredString(payload, "summaryIndex"));
        AssertStructuredNull(ReadStructuredValue(payload, "contentIndex"));
        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.RawNotification);
    }

    [Fact]
    public void HandleNotification_WhenReasoningTextDeltaArrives_PreservesContentIndex()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string reasoningDelta = """
        {"method":"item/reasoning/textDelta","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"reason-1","delta":"raw thinking","contentIndex":2}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(reasoningDelta), reasoningDelta);

        var reasoningEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ReasoningDelta));
        Assert.Equal("raw thinking", reasoningEvent.Text);
        Assert.Equal("item/reasoning/textDelta", reasoningEvent.SourceMethod);
        var payload = GetPayloadValue(reasoningEvent, ControlPlaneConversationStreamPayloadKind.Reasoning);
        AssertStructuredNull(ReadStructuredValue(payload, "summaryIndex"));
        Assert.Equal("2", ReadStructuredString(payload, "contentIndex"));
        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.RawNotification);
    }

    [Fact]
    public void HandleNotification_WhenHookStartedArrives_EmitsTypedHookEvent()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string hookStarted = """
        {"method":"hook/started","params":{"threadId":"thread-hook-1","turnId":"turn-hook-1","run":{"id":"hook-run-1","eventName":"sessionStart","handlerType":"command","executionMode":"sync","scope":"thread","sourcePath":"D:/Work/TianShu/.tianshu/hooks/session-start.ps1","displayOrder":0,"status":"running","statusMessage":"执行中","startedAt":1743300000,"completedAt":null,"durationMs":null,"entries":[{"kind":"context","text":"准备环境"}]}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(hookStarted), hookStarted);

        var hookEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.HookStarted));
        Assert.Equal("thread-hook-1", hookEvent.ThreadId?.Value);
        Assert.Equal("turn-hook-1", hookEvent.TurnId?.Value);
        Assert.Equal("running", hookEvent.Status);
        Assert.Equal("hook/started", hookEvent.SourceMethod);
        var payload = GetPayloadValue(hookEvent, ControlPlaneConversationStreamPayloadKind.HookRun);
        Assert.Equal("hook-run-1", ReadStructuredString(payload, "id"));
        Assert.Equal("sessionStart", ReadStructuredString(payload, "eventName"));
        Assert.Equal("command", ReadStructuredString(payload, "handlerType"));
        Assert.Equal("context", ReadStructuredString(payload, "entries", 0, "kind"));
        Assert.Equal("准备环境", ReadStructuredString(payload, "entries", 0, "text"));
    }

    [Fact]
    public void HandleNotification_WhenHookCompletedArrives_EmitsTypedHookEvent()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string hookCompleted = """
        {"method":"hook/completed","params":{"threadId":"thread-hook-1","turnId":"turn-hook-1","run":{"id":"hook-run-1","eventName":"sessionStart","handlerType":"command","executionMode":"sync","scope":"thread","sourcePath":"D:/Work/TianShu/.tianshu/hooks/session-start.ps1","displayOrder":0,"status":"completed","statusMessage":"已完成","startedAt":1743300000,"completedAt":1743300005,"durationMs":5,"entries":[{"kind":"feedback","text":"完成初始化"}]}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(hookCompleted), hookCompleted);

        var hookEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.HookCompleted));
        Assert.Equal("completed", hookEvent.Status);
        Assert.Equal("hook/completed", hookEvent.SourceMethod);
        var payload = GetPayloadValue(hookEvent, ControlPlaneConversationStreamPayloadKind.HookRun);
        Assert.Equal("5", ReadStructuredString(payload, "durationMs"));
        Assert.Equal("feedback", ReadStructuredString(payload, "entries", 0, "kind"));
        Assert.Equal("完成初始化", ReadStructuredString(payload, "entries", 0, "text"));
    }

    [Fact]
    public void HandleNotification_WhenModelReroutedArrives_EmitsTypedModelReroutedEvent()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string modelRerouted = """
        {"method":"model/rerouted","params":{"threadId":"thread-model-1","turnId":"turn-model-1","fromModel":"gpt-5.3-codex","toModel":"gpt-5.2","reason":"highRiskCyberActivity"}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(modelRerouted), modelRerouted);

        var reroutedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ModelRerouted));
        Assert.Equal("thread-model-1", reroutedEvent.ThreadId?.Value);
        Assert.Equal("turn-model-1", reroutedEvent.TurnId?.Value);
        Assert.Equal("highRiskCyberActivity", reroutedEvent.Status);
        Assert.Equal("gpt-5.3-codex -> gpt-5.2", reroutedEvent.Text);
        Assert.Equal("model/rerouted", reroutedEvent.SourceMethod);
        var payload = GetPayloadValue(reroutedEvent, ControlPlaneConversationStreamPayloadKind.ModelRerouted);
        Assert.Equal("gpt-5.3-codex", ReadStructuredString(payload, "fromModel"));
        Assert.Equal("gpt-5.2", ReadStructuredString(payload, "toModel"));
        Assert.Equal("highRiskCyberActivity", ReadStructuredString(payload, "reason"));
    }

    [Fact]
    public void HandleNotification_WhenTurnPlanUpdatedArrives_PreservesStructuredPlanPayload()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string planUpdated = """
        {"method":"turn/plan/updated","params":{"threadId":"thread-1","turnId":"turn-1","explanation":"先检查再修改","plan":[{"step":"检查日志","status":"completed"},{"step":"修复 VSIX 展示","status":"in_progress"},{"step":"补测试","status":"pending"}]}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(planUpdated), planUpdated);

        var planEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.PlanUpdated));
        Assert.Equal("先检查再修改", planEvent.Text);
        Assert.Equal("turn/plan/updated", planEvent.SourceMethod);
        var payload = GetPayloadValue(planEvent, ControlPlaneConversationStreamPayloadKind.Plan);
        Assert.Equal("先检查再修改", ReadStructuredString(payload, "explanation"));
        Assert.Equal(3, ReadStructuredItems(payload, "steps").Count);
        Assert.Equal("修复 VSIX 展示", ReadStructuredString(payload, "steps", 1, "step"));
        Assert.Equal("in_progress", ReadStructuredString(payload, "steps", 1, "status"));
    }

    [Fact]
    public void HandleNotification_WhenTurnPlanUpdatedUsesCamelCaseStatus_NormalizesToSnakeCase()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string planUpdated = """
        {"method":"turn/plan/updated","params":{"threadId":"thread-1","turnId":"turn-1","explanation":"准备处理中","plan":[{"step":"读取线程日志","status":"inProgress"},{"step":"整理最终总结","status":"pending"}]}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(planUpdated), planUpdated);

        var planEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.PlanUpdated));
        var payload = GetPayloadValue(planEvent, ControlPlaneConversationStreamPayloadKind.Plan);
        Assert.Equal("in_progress", ReadStructuredString(payload, "steps", 0, "status"));
        Assert.Equal("pending", ReadStructuredString(payload, "steps", 1, "status"));
    }

    [Fact]
    public void HandleNotification_WhenContextCompactionItemLifecycleArrives_TreatsItAsToolLifecycle()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string itemStarted = """
        {"method":"item/started","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"compact-1","type":"contextCompaction"}}}
        """;
        const string itemCompleted = """
        {"method":"item/completed","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"compact-1","type":"contextCompaction","status":"completed"}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(itemStarted), itemStarted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(itemCompleted), itemCompleted);

        var startedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted));
        Assert.Equal("compact-1", startedEvent.CallId?.Value);
        Assert.Equal("contextCompaction", startedEvent.ToolName);
        var startedPayload = GetPayloadValue(startedEvent, ControlPlaneConversationStreamPayloadKind.ToolCall);
        Assert.Equal("compact-1", ReadStructuredString(startedPayload, "callId"));
        Assert.Equal("contextCompaction", ReadStructuredString(startedPayload, "toolName"));

        var completedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted));
        Assert.Equal("compact-1", completedEvent.CallId?.Value);
        Assert.Equal("contextCompaction", completedEvent.ToolName);
        var completedPayload = GetPayloadValue(completedEvent, ControlPlaneConversationStreamPayloadKind.ToolCall);
        Assert.Equal("completed", ReadStructuredString(completedPayload, "status"));
        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.RawNotification);
    }

    [Fact]
    public void HandleNotification_WhenUserMessageItemCompletes_EmitsTypedItemCompletedAndCommittedUserMessage()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string userMessageCompleted = """
        {"method":"item/completed","params":{"threadId":"thread-typed-1","turnId":"turn-typed-1","item":{"id":"user-msg-1","type":"user_message","status":"completed","content":[{"type":"input_text","text":"请在下一个工具调用后插入这条引导。"}]}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(userMessageCompleted), userMessageCompleted);

        var itemCompleted = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ItemCompleted));
        Assert.Equal("thread-typed-1", itemCompleted.ThreadId?.Value);
        Assert.Equal("turn-typed-1", itemCompleted.TurnId?.Value);
        Assert.Equal("user-msg-1", itemCompleted.ItemId);
        Assert.Equal("item/completed", itemCompleted.SourceMethod);
        var itemCompletedPayload = GetItemPayload(itemCompleted);
        Assert.Equal("user_message", ReadStructuredString(itemCompletedPayload, "itemType"));
        Assert.Equal("请在下一个工具调用后插入这条引导。", ReadStructuredString(itemCompletedPayload, "text"));
        Assert.Equal("请在下一个工具调用后插入这条引导。", itemCompleted.Text);

        var committed = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.UserMessageCommitted));
        Assert.Equal("thread-typed-1", committed.ThreadId?.Value);
        Assert.Equal("turn-typed-1", committed.TurnId?.Value);
        Assert.Equal("user-msg-1", committed.ItemId);
        var committedPayload = GetCommittedUserMessagePayload(committed);
        Assert.Equal("请在下一个工具调用后插入这条引导。", ReadStructuredString(committedPayload, "text"));
        Assert.Equal(0, ReadStructuredInt32(committedPayload, "imageCount"));
        Assert.Null(ReadStructuredString(committedPayload, "correlationId"));
        Assert.Null(committed.Diagnostics?.RawJson);
        Assert.DoesNotContain(events, static item => item.Kind == ControlPlaneConversationStreamEventKind.RawNotification);
    }

    [Fact]
    public void HandleNotification_WhenChildThreadLifecycleArrives_DoesNotReplaceActiveRootConversation()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-root-001");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-root-001");

        const string childThreadStarted = """
        {"method":"thread/started","params":{"threadId":"thread-child-001"}}
        """;
        const string childTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-child-001","turn":{"id":"turn-child-001","status":"inProgress"}}}
        """;
        const string childTurnCompleted = """
        {"method":"turn/completed","params":{"threadId":"thread-child-001","turn":{"id":"turn-child-001","status":"completed","items":[]}}}
        """;
        const string rootTurnCompleted = """
        {"method":"turn/completed","params":{"threadId":"thread-root-001","turn":{"id":"turn-root-001","status":"completed","items":[]}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(childThreadStarted), childThreadStarted);

        Assert.Equal("thread-root-001", runtime.ActiveThreadId);
        Assert.Equal("turn-root-001", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
        var rawThreadStarted = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.RawNotification));
        Assert.Equal("thread-child-001", rawThreadStarted.ThreadId?.Value);
        Assert.Null(rawThreadStarted.TurnId);
        Assert.Equal("thread/started", rawThreadStarted.Message);

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(childTurnStarted), childTurnStarted);

        Assert.Equal("thread-root-001", runtime.ActiveThreadId);
        Assert.Equal("turn-root-001", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
        var startedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.TurnStarted));
        Assert.Equal("thread-child-001", startedEvent.ThreadId?.Value);
        Assert.Equal("turn-child-001", startedEvent.TurnId?.Value);

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(childTurnCompleted), childTurnCompleted);

        Assert.True(runtime.HasActiveTurn);
        Assert.Equal("thread-root-001", runtime.ActiveThreadId);
        Assert.Equal("turn-root-001", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
        var completedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.TurnCompleted && item.TurnId?.Value == "turn-child-001"));
        Assert.Equal("thread-child-001", completedEvent.ThreadId?.Value);

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(rootTurnCompleted), rootTurnCompleted);

        Assert.False(runtime.HasActiveTurn);
        Assert.Equal("thread-root-001", runtime.ActiveThreadId);
        Assert.Null(ReflectionTestHelper.GetField(runtime, "activeTurnId"));
    }

    [Fact]
    public void HandleNotification_WhenSameThreadNewTurnStartedBeforeOldTurnCompleted_PreservesNewestActiveTurnId()
    {
        var runtime = new TianShuExecutionRuntime();
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-root-002");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-root-002-old");

        const string newTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-002","turn":{"id":"turn-root-002-new","status":"inProgress"}}}
        """;
        const string oldTurnCompleted = """
        {"method":"turn/completed","params":{"threadId":"thread-root-002","turn":{"id":"turn-root-002-old","status":"interrupted","items":[]}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(newTurnStarted), newTurnStarted);

        Assert.Equal("thread-root-002", runtime.ActiveThreadId);
        Assert.Equal("turn-root-002-new", ReflectionTestHelper.GetField(runtime, "activeTurnId"));

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(oldTurnCompleted), oldTurnCompleted);

        Assert.Equal("thread-root-002", runtime.ActiveThreadId);
        Assert.True(runtime.HasActiveTurn);
        Assert.Equal("turn-root-002-new", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
    }

    [Fact]
    public void HandleNotification_WhenSameThreadNewTurnStartedBeforeOldTurnCompleted_ClearsOldTurnPendingInteractiveState()
    {
        var runtime = new TianShuExecutionRuntime();
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-root-002b");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-root-002b-old");

        var pendingApprovalRequests = Assert.IsType<ConcurrentDictionary<string, TaskCompletionSource<ApprovalResponse>>>(
            ReflectionTestHelper.GetField(runtime, "pendingApprovalRequests"));
        var approvalCompletion = new TaskCompletionSource<ApprovalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingApprovalRequests["approval-call-root-002b-old"] = approvalCompletion;
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "TrackPendingInteractiveServerRequest",
            711L,
            "approval-call-root-002b-old",
            "thread-root-002b",
            "turn-root-002b-old",
            "approval_requested",
            "shell",
            "item/tool/requestApproval");

        const string newTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-002b","turn":{"id":"turn-root-002b-new","status":"inProgress"}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(newTurnStarted), newTurnStarted);

        Assert.True(approvalCompletion.Task.IsCanceled);
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingInteractiveServerRequestsByRequestId"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingInteractiveServerRequestIdsByCallId"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingInteractiveReplayContextsByCallId"))!.Cast<object>());
        Assert.Empty(((IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingApprovalRequests"))!.Cast<object>());
        Assert.Equal("thread-root-002b", runtime.ActiveThreadId);
        Assert.Equal("turn-root-002b-new", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
    }

    [Fact]
    public void HandleTurnCompletedNotification_WhenSameThreadNewTurnIsActiveAndCompletedNotificationOmitsTurnId_DoesNotCompleteNewestTurn()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string oldTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-003","turn":{"id":"turn-root-003-old","status":"inProgress"}}}
        """;
        const string newTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-003","turn":{"id":"turn-root-003-new","status":"inProgress"}}}
        """;
        const string oldTurnCompletedWithoutId = """
        {"method":"turn/completed","params":{"threadId":"thread-root-003","turn":{"status":"interrupted","items":[]}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(oldTurnStarted), oldTurnStarted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(newTurnStarted), newTurnStarted);

        Assert.Equal("thread-root-003", runtime.ActiveThreadId);
        Assert.Equal("turn-root-003-new", ReflectionTestHelper.GetField(runtime, "activeTurnId"));

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(oldTurnCompletedWithoutId),
            oldTurnCompletedWithoutId);

        var completedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.TurnCompleted));
        Assert.Equal("turn-root-003-old", completedEvent.TurnId?.Value);
        Assert.Equal("interrupted", completedEvent.Status);
        Assert.Equal("thread-root-003", completedEvent.ThreadId?.Value);
        Assert.True(runtime.HasActiveTurn);
        Assert.Equal("turn-root-003-new", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
    }

    [Fact]
    public void HandleRawResponseItemCompletedNotification_WhenSameThreadNewTurnIsActiveAndItemOmitsTurnId_AttributesAssistantTextToOldTurn()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string oldTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-004","turn":{"id":"turn-root-004-old","status":"inProgress"}}}
        """;
        const string newTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-004","turn":{"id":"turn-root-004-new","status":"inProgress"}}}
        """;
        const string oldAssistantCompletedWithoutTurnId = """
        {"method":"rawResponseItem/completed","params":{"threadId":"thread-root-004","item":{"id":"assistant-root-004-old","type":"agentMessage","status":"completed","text":"旧回合总结"}}}
        """;
        const string oldTurnCompletedWithoutId = """
        {"method":"turn/completed","params":{"threadId":"thread-root-004","turn":{"status":"interrupted","items":[]}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(oldTurnStarted), oldTurnStarted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(newTurnStarted), newTurnStarted);

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(oldAssistantCompletedWithoutTurnId),
            oldAssistantCompletedWithoutTurnId);

        var assistantCompletedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.AssistantTextCompleted));
        Assert.Equal("thread-root-004", assistantCompletedEvent.ThreadId?.Value);
        Assert.Equal("turn-root-004-old", assistantCompletedEvent.TurnId?.Value);
        Assert.Equal("旧回合总结", assistantCompletedEvent.Text);

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(oldTurnCompletedWithoutId),
            oldTurnCompletedWithoutId);

        var completedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.TurnCompleted));
        Assert.Equal("turn-root-004-old", completedEvent.TurnId?.Value);
        Assert.Equal("旧回合总结", completedEvent.Text);
        Assert.True(runtime.HasActiveTurn);
        Assert.Equal("turn-root-004-new", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
    }

    [Fact]
    public void HandleErrorNotification_WhenSameThreadNewTurnIsActiveAndErrorOmitsTurnId_DoesNotCompleteNewestTurn()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string oldTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-005","turn":{"id":"turn-root-005-old","status":"inProgress"}}}
        """;
        const string newTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-005","turn":{"id":"turn-root-005-new","status":"inProgress"}}}
        """;
        const string oldTurnErrorWithoutId = """
        {"method":"error","params":{"threadId":"thread-root-005","message":"old turn failed"}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(oldTurnStarted), oldTurnStarted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(newTurnStarted), newTurnStarted);

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(oldTurnErrorWithoutId),
            oldTurnErrorWithoutId);

        var errorEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.Error));
        Assert.Equal("thread-root-005", errorEvent.ThreadId?.Value);
        Assert.Equal("turn-root-005-old", errorEvent.TurnId?.Value);
        Assert.Equal("error", errorEvent.Status);
        Assert.Equal("old turn failed", errorEvent.Message);
        Assert.True(runtime.HasActiveTurn);
        Assert.Equal("turn-root-005-new", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
    }

    [Fact]
    public void HandleNotification_WhenInterruptedTurnReceivesLateProgressEvents_SuppressesOldTurnAndKeepsNewTurnActive()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string oldTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-006","turn":{"id":"turn-root-006-old","status":"inProgress"}}}
        """;
        const string newTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-006","turn":{"id":"turn-root-006-new","status":"inProgress"}}}
        """;
        const string oldTurnInterrupted = """
        {"method":"turn/completed","params":{"threadId":"thread-root-006","turn":{"id":"turn-root-006-old","status":"interrupted","items":[]}}}
        """;
        const string staleAssistantDelta = """
        {"method":"item/agentMessage/delta","params":{"threadId":"thread-root-006","turnId":"turn-root-006-old","itemId":"old-msg-006","item":{"id":"old-msg-006","type":"agentMessage","delta":"stale commentary"},"delta":"stale commentary"}}
        """;
        const string staleItemStarted = """
        {"method":"item/started","params":{"threadId":"thread-root-006","turnId":"turn-root-006-old","item":{"id":"old-item-006","type":"user_message","status":"started","content":[{"type":"input_text","text":"stale item"}]}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(oldTurnStarted), oldTurnStarted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(newTurnStarted), newTurnStarted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(oldTurnInterrupted), oldTurnInterrupted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(staleAssistantDelta), staleAssistantDelta);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(staleItemStarted), staleItemStarted);

        var suppressedKinds = new HashSet<ControlPlaneConversationStreamEventKind>
        {
            ControlPlaneConversationStreamEventKind.AssistantTextDelta,
            ControlPlaneConversationStreamEventKind.ItemStarted,
        };
        Assert.DoesNotContain(
            events,
            item => string.Equals(item.TurnId?.Value, "turn-root-006-old", StringComparison.Ordinal)
                    && suppressedKinds.Contains(item.Kind));

        var interruptedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.TurnCompleted));
        Assert.Equal("turn-root-006-old", interruptedEvent.TurnId?.Value);
        Assert.Equal("interrupted", interruptedEvent.Status);
        Assert.True(runtime.HasActiveTurn);
        Assert.Equal("turn-root-006-new", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
    }

    [Fact]
    public void HandleErrorNotification_WhenStructuredErrorArrives_PreservesAdditionalDetails()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string errorNotification = """
        {
          "method": "error",
          "params": {
            "threadId": "thread-error-structured-001",
            "turnId": "turn-error-structured-001",
            "message": "tool failed",
            "error": {
              "message": "tool failed",
              "additionalDetails": "stderr: permission denied"
            },
            "willRetry": false
          }
        }
        """;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(errorNotification),
            errorNotification);

        var errorEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.Error));
        Assert.NotNull(errorEvent.TurnError);
        Assert.Equal("tool failed", errorEvent.TurnError!.Message);
        Assert.Equal("stderr: permission denied", errorEvent.TurnError.AdditionalDetails);
    }

    [Fact]
    public void HandleNotification_WhenForeignThreadPlanUpdateOmitsTurnId_DoesNotBorrowActiveTurnId()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-local-plan");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-local-plan");

        const string planUpdated = """
        {"method":"turn/plan/updated","params":{"threadId":"thread-foreign-plan","explanation":"foreign plan","plan":[{"step":"inspect","status":"pending"}]}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(planUpdated), planUpdated);

        var planEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.PlanUpdated));
        Assert.Equal("thread-foreign-plan", planEvent.ThreadId?.Value);
        Assert.Null(planEvent.TurnId);
        Assert.Equal("foreign plan", planEvent.Text);
        var payload = GetPayloadValue(planEvent, ControlPlaneConversationStreamPayloadKind.Plan);
        Assert.Equal("foreign plan", ReadStructuredString(payload, "explanation"));
        Assert.Equal("inspect", ReadStructuredString(payload, "steps", 0, "step"));
    }

    [Fact]
    public void HandleNotification_WhenForeignThreadTypedItemOmitsTurnId_DoesNotBorrowActiveTurnId()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-local-item");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-local-item");

        const string itemStarted = """
        {"method":"item/started","params":{"threadId":"thread-foreign-item","item":{"id":"foreign-item-1","type":"user_message","status":"started","content":[{"type":"input_text","text":"foreign"}]}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(itemStarted), itemStarted);

        var itemEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ItemStarted));
        Assert.Equal("thread-foreign-item", itemEvent.ThreadId?.Value);
        Assert.Null(itemEvent.TurnId);
        Assert.Equal("foreign", itemEvent.Text);
        var itemPayload = GetItemPayload(itemEvent);
        Assert.Equal("foreign", ReadStructuredString(itemPayload, "text"));
    }

    [Fact]
    public void HandleNotification_WhenSameThreadNewTurnIsActiveAndTypedItemOmitsTurnId_AttributesItemToObservedOldTurn()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string oldTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-typed-owner-1","turn":{"id":"turn-root-typed-owner-1-old","status":"inProgress"}}}
        """;
        const string newTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-typed-owner-1","turn":{"id":"turn-root-typed-owner-1-new","status":"inProgress"}}}
        """;
        const string oldItemStartedWithoutTurnId = """
        {"method":"item/started","params":{"threadId":"thread-root-typed-owner-1","item":{"id":"typed-owner-item-1","type":"user_message","status":"started","content":[{"type":"input_text","text":"old owner payload"}]}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(oldTurnStarted), oldTurnStarted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(newTurnStarted), newTurnStarted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(oldItemStartedWithoutTurnId), oldItemStartedWithoutTurnId);

        var itemEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ItemStarted));
        Assert.Equal("thread-root-typed-owner-1", itemEvent.ThreadId?.Value);
        Assert.Equal("turn-root-typed-owner-1-old", itemEvent.TurnId?.Value);
        Assert.Equal("old owner payload", itemEvent.Text);
        var itemPayload = GetItemPayload(itemEvent);
        Assert.Equal("old owner payload", ReadStructuredString(itemPayload, "text"));
        Assert.Equal("turn-root-typed-owner-1-new", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
    }

    [Fact]
    public async Task RespondToApprovalAsync_WhenTypedNotificationApprovalBelongsToOlderObservedTurn_UsesObservedTurnId()
    {
        var runtime = new TianShuExecutionRuntime();
        var (stream, writer) = CreateConnectedRuntime(runtime, activeThreadId: null, activeTurnId: null);
        using var streamHandle = stream;
        using var writerHandle = writer;

        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string oldTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-typed-approval-1","turn":{"id":"turn-root-typed-approval-1-old","status":"inProgress"}}}
        """;
        const string newTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-root-typed-approval-1","turn":{"id":"turn-root-typed-approval-1-new","status":"inProgress"}}}
        """;
        const string approvalNotificationWithoutTurnId = """
        {"method":"item/tool/requestApproval","params":{"threadId":"thread-root-typed-approval-1","itemId":"typed-approval-item-1","callId":"typed-approval-call-1","toolName":"shell","type":"tool","status":"awaitingApproval","requiresApproval":true,"arguments":"echo hi"}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(oldTurnStarted), oldTurnStarted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(newTurnStarted), newTurnStarted);
        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(approvalNotificationWithoutTurnId),
            approvalNotificationWithoutTurnId);

        var approvalEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ApprovalRequested));
        Assert.Equal("thread-root-typed-approval-1", approvalEvent.ThreadId?.Value);
        Assert.Equal("turn-root-typed-approval-1-old", approvalEvent.TurnId?.Value);
        Assert.Equal("typed-approval-call-1", approvalEvent.CallId?.Value);
        Assert.Equal("echo hi", approvalEvent.Text);
        Assert.Equal("request_approval", approvalEvent.Phase);
        Assert.Equal("item/tool/requestApproval", approvalEvent.SourceMethod);
        var approvalPayload = GetApprovalRequestPayload(approvalEvent);
        Assert.Equal("echo hi", ReadStructuredString(approvalPayload, "summary"));

        var startedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted));
        Assert.Equal("thread-root-typed-approval-1", startedEvent.ThreadId?.Value);
        Assert.Equal("turn-root-typed-approval-1-old", startedEvent.TurnId?.Value);
        Assert.Equal("typed-approval-call-1", startedEvent.CallId?.Value);
        Assert.Equal("echo hi", startedEvent.Text);
        Assert.Equal("request_approval", startedEvent.Phase);
        var startedPayload = GetToolCallPayload(startedEvent);
        Assert.Equal("echo hi", ReadStructuredString(startedPayload, "inputText"));
        Assert.Equal("request_approval", ReadStructuredString(startedPayload, "phase"));
        Assert.True(ReadStructuredBoolean(startedPayload, "requiresApproval"));

        var responseTask = runtime.RespondToApprovalAsync(
            new ControlPlaneApprovalResolution
            {
                CallId = new CallId("typed-approval-call-1"),
                Decision = ControlPlaneApprovalDecision.Approve,
                Note = "允许",
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(runtime, requestId, "{}");

        Assert.True(await responseTask);

        using var request = await ReadSingleRequestAsync(stream);
        Assert.Equal("turn/approval/respond", request.RootElement.GetProperty("method").GetString());
        var payload = request.RootElement.GetProperty("params");
        Assert.Equal("thread-root-typed-approval-1", payload.GetProperty("threadId").GetString());
        Assert.Equal("turn-root-typed-approval-1-old", payload.GetProperty("turnId").GetString());
        Assert.Equal("typed-approval-call-1", payload.GetProperty("callId").GetString());
    }

    [Fact]
    public async Task HandleToolRequestApprovalAsync_WhenThreadDiffersAndTurnIdMissing_DoesNotBorrowActiveTurnId()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-approval-local");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-approval-local");

        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/tool/requestApproval",
                  "id": 71,
                  "params": {
                    "threadId": "thread-approval-foreign",
                    "itemId": "approval-foreign-item-1",
                    "callId": "approval-foreign-call-1",
                    "toolName": "shell",
                    "command": ["cmd.exe", "/c", "echo hi"]
                  }
                }
                """),
            CancellationToken.None);

        ControlPlaneConversationStreamEvent? approvalEvent = null;
        for (var attempt = 0; attempt < 20 && approvalEvent is null; attempt++)
        {
            approvalEvent = events.LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ApprovalRequested);
            if (approvalEvent is null)
            {
                await Task.Delay(10);
            }
        }

        Assert.NotNull(approvalEvent);
        Assert.Equal("thread-approval-foreign", approvalEvent!.ThreadId?.Value);
        Assert.Null(approvalEvent.TurnId);

        var startedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted));
        Assert.Equal("thread-approval-foreign", startedEvent.ThreadId?.Value);
        Assert.Null(startedEvent.TurnId);

        var responded = false;
        for (var attempt = 0; attempt < 20 && !responded; attempt++)
        {
            responded = await runtime.RespondToApprovalAsync(
                new ControlPlaneApprovalResolution
                {
                    CallId = new CallId("approval-foreign-call-1"),
                    Decision = ControlPlaneApprovalDecision.Approve,
                    Note = "允许",
                },
                CancellationToken.None);
            if (!responded)
            {
                await Task.Delay(10);
            }
        }

        Assert.True(responded);
        await ReflectionTestHelper.AwaitTaskResultAsync(handlerTask);

        var completedEvent = events.Last(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted);
        Assert.Equal("thread-approval-foreign", completedEvent.ThreadId?.Value);
        Assert.Null(completedEvent.TurnId);
    }

    [Fact]
    public async Task HandleToolRequestApprovalAsync_WhenSameThreadNewTurnIsActiveAndTurnIdMissing_DoesNotBorrowNewestActiveTurnId()
    {
        var runtime = new TianShuExecutionRuntime();
        await using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);

        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string oldTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-approval-overlap-1","turn":{"id":"turn-approval-overlap-1-old","status":"inProgress"}}}
        """;
        const string newTurnStarted = """
        {"method":"turn/started","params":{"threadId":"thread-approval-overlap-1","turn":{"id":"turn-approval-overlap-1-new","status":"inProgress"}}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(oldTurnStarted), oldTurnStarted);
        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(newTurnStarted), newTurnStarted);

        var handlerTask = ReflectionTestHelper.InvokeMethod(
            runtime,
            "TryHandleServerRequestAsync",
            ReflectionTestHelper.ParseJsonElement(
                """
                {
                  "method": "item/tool/requestApproval",
                  "id": 72,
                  "params": {
                    "threadId": "thread-approval-overlap-1",
                    "itemId": "approval-overlap-item-1",
                    "callId": "approval-overlap-call-1",
                    "toolName": "shell",
                    "command": ["cmd.exe", "/c", "echo hi"]
                  }
                }
                """),
            CancellationToken.None);

        ControlPlaneConversationStreamEvent? approvalEvent = null;
        for (var attempt = 0; attempt < 20 && approvalEvent is null; attempt++)
        {
            approvalEvent = events.LastOrDefault(static item => item.Kind == ControlPlaneConversationStreamEventKind.ApprovalRequested);
            if (approvalEvent is null)
            {
                await Task.Delay(10);
            }
        }

        Assert.NotNull(approvalEvent);
        Assert.Equal("thread-approval-overlap-1", approvalEvent!.ThreadId?.Value);
        Assert.Null(approvalEvent.TurnId);

        var startedEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted));
        Assert.Equal("thread-approval-overlap-1", startedEvent.ThreadId?.Value);
        Assert.Null(startedEvent.TurnId);

        var responded = false;
        for (var attempt = 0; attempt < 20 && !responded; attempt++)
        {
            responded = await runtime.RespondToApprovalAsync(
                new ControlPlaneApprovalResolution
                {
                    CallId = new CallId("approval-overlap-call-1"),
                    Decision = ControlPlaneApprovalDecision.Approve,
                    Note = "允许",
                },
                CancellationToken.None);
            if (!responded)
            {
                await Task.Delay(10);
            }
        }

        Assert.True(responded);
        await ReflectionTestHelper.AwaitTaskResultAsync(handlerTask);

        var completedEvent = events.Last(static item => item.Kind == ControlPlaneConversationStreamEventKind.ToolCallCompleted);
        Assert.Equal("thread-approval-overlap-1", completedEvent.ThreadId?.Value);
        Assert.Null(completedEvent.TurnId);
        Assert.Equal("turn-approval-overlap-1-new", ReflectionTestHelper.GetField(runtime, "activeTurnId"));
    }

    [Fact]
    public void HandleNotification_WhenUnknownNotificationArrives_EmitsRawNotification()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);

        const string unknownNotification = """
        {"method":"thread/customUpdated","params":{"threadId":"thread-1","turnId":"turn-1","value":"demo"}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(unknownNotification), unknownNotification);

        var rawEvent = Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.RawNotification, rawEvent.Kind);
        Assert.Equal("thread/customUpdated", rawEvent.Message);
        Assert.Equal("thread-1", rawEvent.ThreadId?.Value);
        Assert.Equal("turn-1", rawEvent.TurnId?.Value);
        Assert.Null(rawEvent.PayloadKind);
    }

    [Fact]
    public void HandleNotification_WhenRetryableErrorArrives_DoesNotCompleteTurn()
    {
        var runtime = new TianShuExecutionRuntime();
        var events = new List<ControlPlaneConversationStreamEvent>();
        runtime.StreamEventReceived += (_, args) => events.Add(args.StreamEvent);
        ReflectionTestHelper.SetField(runtime, "activeThreadId", "thread-1");
        ReflectionTestHelper.SetField(runtime, "activeTurnId", "turn-1");

        const string retryableError = """
        {"method":"error","params":{"threadId":"thread-1","turnId":"turn-1","message":"stream failed, retrying","willRetry":true}}
        """;

        ReflectionTestHelper.InvokeMethod(runtime, "HandleNotification", ReflectionTestHelper.ParseJsonElement(retryableError), retryableError);

        var errorEvent = Assert.Single(events.Where(static item => item.Kind == ControlPlaneConversationStreamEventKind.Error));
        Assert.Equal("turn-1", errorEvent.TurnId?.Value);
        Assert.Equal("retrying", errorEvent.Status);
        Assert.True(errorEvent.WillRetry);
        Assert.Equal("stream failed, retrying", errorEvent.Message);

        var completedTurns = (IEnumerable?)ReflectionTestHelper.GetField(runtime, "completedTurns");
        Assert.NotNull(completedTurns);
        Assert.Empty(completedTurns!.Cast<object>());

        var activeTurnId = Assert.IsType<string>(ReflectionTestHelper.GetField(runtime, "activeTurnId"));
        Assert.Equal("turn-1", activeTurnId);
    }

    private static (MemoryStream Stream, StreamWriter Writer) CreateConnectedRuntime(
        TianShuExecutionRuntime runtime,
        string? activeThreadId,
        string? activeTurnId,
        bool enablePendingInputPersistence = false)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };

        ReflectionTestHelper.SetField(runtime, "stdin", writer);
        ReflectionTestHelper.SetField(runtime, "process", Process.GetCurrentProcess());
        ReflectionTestHelper.SetField(runtime, "activeThreadId", activeThreadId);
        ReflectionTestHelper.SetField(runtime, "activeTurnId", activeTurnId);
        ReflectionTestHelper.SetField(runtime, "pendingInputStateKernelPersistenceEnabled", enablePendingInputPersistence);
        return (stream, writer);
    }

    private static async Task<List<JsonDocument>> ReadRequestDocumentsAsync(MemoryStream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        var lines = payload.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Select(static line => JsonDocument.Parse(line)).ToList();
    }

    private static async Task<long> WaitForPendingResponseIdAsync(TianShuExecutionRuntime runtime, params long[] excludedRequestIds)
    {
        var excluded = excludedRequestIds.Length == 0
            ? null
            : excludedRequestIds.ToHashSet();

        for (var attempt = 0; attempt < 500; attempt++)
        {
            var pendingResponses = (IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingResponses");
            Assert.NotNull(pendingResponses);

            foreach (var entry in pendingResponses!)
            {
                var keyProperty = entry.GetType().GetProperty("Key");
                Assert.NotNull(keyProperty);

                var requestId = (long)keyProperty!.GetValue(entry)!;
                if (excluded?.Contains(requestId) == true)
                {
                    continue;
                }

                return requestId;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("未捕获到待完成的 RPC 请求。");
    }

    private static async Task<ControlPlaneThreadSnapshot?> ResumeThreadWithPendingInputStateAsync(
        string threadId,
        string pendingInputStateJson)
    {
        var runtime = new TianShuExecutionRuntime();
        var connection = CreateConnectedRuntime(runtime, activeThreadId: null, activeTurnId: null);
        using var stream = connection.Stream;
        using var writer = connection.Writer;

        var pendingTask = runtime.ResumeThreadAsync(threadId, CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            $$"""
            {
              "thread": {
                "id": "{{threadId}}",
                "preview": "pending input state resume",
                "cwd": "D:/Work/TianShu",
                "pendingInputState": {{pendingInputStateJson}},
                "pendingInteractiveRequests": [],
                "seedHistory": [],
                "turns": []
              }
            }
            """);

        return await pendingTask;
    }

    private static async Task<ControlPlaneThreadOperationResult> ReadThreadWithPendingInputStateAsync(
        string threadId,
        string pendingInputStateJson)
    {
        var runtime = new TianShuExecutionRuntime();
        var connection = CreateConnectedRuntime(runtime, activeThreadId: null, activeTurnId: null);
        using var stream = connection.Stream;
        using var writer = connection.Writer;

        var pendingTask = runtime.ReadThreadAsync(
            new ControlPlaneReadThreadQuery
            {
                ThreadId = new ThreadId(threadId),
            },
            CancellationToken.None);
        var requestId = await WaitForPendingResponseIdAsync(runtime);
        CompletePendingResponse(
            runtime,
            requestId,
            $$"""
            {
              "thread": {
                "id": "{{threadId}}",
                "preview": "pending input state read",
                "cwd": "D:/Work/TianShu",
                "pendingInputState": {{pendingInputStateJson}},
                "turns": []
              }
            }
            """);

        return await pendingTask;
    }

    private static void CompletePendingResponse(TianShuExecutionRuntime runtime, long requestId, string resultJson)
    {
        var pendingResponses = (IEnumerable?)ReflectionTestHelper.GetField(runtime, "pendingResponses");
        Assert.NotNull(pendingResponses);

        foreach (var entry in pendingResponses!)
        {
            var keyProperty = entry.GetType().GetProperty("Key");
            var valueProperty = entry.GetType().GetProperty("Value");
            Assert.NotNull(keyProperty);
            Assert.NotNull(valueProperty);

            var currentId = (long)keyProperty!.GetValue(entry)!;
            if (currentId != requestId)
            {
                continue;
            }

            var completionSource = valueProperty!.GetValue(entry);
            Assert.NotNull(completionSource);

            var trySetResult = completionSource!.GetType().GetMethod("TrySetResult", new[] { typeof(JsonElement) });
            Assert.NotNull(trySetResult);

            var success = (bool)trySetResult!.Invoke(completionSource, new object[] { ReflectionTestHelper.ParseJsonElement(resultJson) })!;
            Assert.True(success);
            return;
        }

        throw new InvalidOperationException($"未找到待完成的 RPC 请求：{requestId}");
    }

    private static async Task<JsonDocument> ReadSingleRequestAsync(MemoryStream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        var lines = payload.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Single(lines);
        return JsonDocument.Parse(lines[0]);
    }

    private static async Task<JsonDocument> ReadSingleRequestEventuallyAsync(MemoryStream stream, int maxAttempts = 100)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var requests = await ReadRequestDocumentsAsync(stream);
            if (requests.Count == 1)
            {
                return requests[0];
            }

            foreach (var request in requests)
            {
                request.Dispose();
            }

            await Task.Delay(10);
        }

        throw new Xunit.Sdk.XunitException("未在超时时间内观察到单条 RPC 请求。");
    }

    private static StructuredValue GetPayloadValue(
        ControlPlaneConversationStreamEvent streamEvent,
        ControlPlaneConversationStreamPayloadKind expectedKind)
    {
        Assert.Equal(expectedKind, streamEvent.PayloadKind);
        return Assert.IsType<StructuredValue>(streamEvent.Payload);
    }

    private static StructuredValue GetPendingInputStatePayload(ControlPlaneConversationStreamEvent streamEvent)
        => GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.PendingInputState);

    private static StructuredValue GetToolCallPayload(ControlPlaneConversationStreamEvent streamEvent)
        => GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.ToolCall);

    private static StructuredValue GetApprovalRequestPayload(ControlPlaneConversationStreamEvent streamEvent)
        => GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.ApprovalRequest);

    private static StructuredValue GetPermissionRequestPayload(ControlPlaneConversationStreamEvent streamEvent)
        => GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.PermissionRequest);

    private static StructuredValue GetUserInputRequestPayload(ControlPlaneConversationStreamEvent streamEvent)
        => GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.UserInputRequest);

    private static StructuredValue GetItemPayload(ControlPlaneConversationStreamEvent streamEvent)
        => GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.Item);

    private static StructuredValue GetCommittedUserMessagePayload(ControlPlaneConversationStreamEvent streamEvent)
        => GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.CommittedUserMessage);

    private static int ReadStructuredInt32(StructuredValue? value, params object[] path)
    {
        var current = ReadStructuredValue(value, path);
        Assert.NotNull(current);
        return current!.GetInt32();
    }

    private static IReadOnlyList<StructuredValue> ReadStructuredItems(StructuredValue? value, params object[] path)
    {
        var current = ReadStructuredValue(value, path);
        if (current is null)
        {
            return Array.Empty<StructuredValue>();
        }

        Assert.Equal(StructuredValueKind.Array, current.Kind);
        return current.Items;
    }

    private static void AssertStructuredNull(StructuredValue? value)
    {
        Assert.NotNull(value);
        Assert.Equal(StructuredValueKind.Null, value!.Kind);
    }

    private static StructuredValue StructuredJson(string json)
        => StructuredValue.FromJsonElement(ReflectionTestHelper.ParseJsonElement(json));

    private static string? ReadStructuredString(StructuredValue? value, params object[] path)
    {
        var current = ReadStructuredValue(value, path);
        return current?.Kind switch
        {
            StructuredValueKind.String => current.StringValue,
            StructuredValueKind.Number => current.NumberValue,
            StructuredValueKind.Boolean => current.BooleanValue?.ToString(),
            _ => null,
        };
    }

    private static bool ReadStructuredBoolean(StructuredValue? value, params object[] path)
        => ReadStructuredValue(value, path)?.BooleanValue ?? false;

    private static StructuredValue? ReadStructuredValue(StructuredValue? value, params object[] path)
    {
        var current = value;
        foreach (var segment in path)
        {
            if (current is null)
            {
                return null;
            }

            switch (segment)
            {
                case string propertyName when current.Kind == StructuredValueKind.Object
                                              && current.Properties.TryGetValue(propertyName, out var propertyValue):
                    current = propertyValue;
                    break;
                case int index when current.Kind == StructuredValueKind.Array
                                    && index >= 0
                                    && index < current.Items.Count:
                    current = current.Items[index];
                    break;
                default:
                    return null;
            }
        }

        return current;
    }

    private sealed class BlockingConcurrentWriteStream : MemoryStream
    {
        private readonly TaskCompletionSource<bool> firstWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> allowWritesToFinish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int activeWrites;

        public Task FirstWriteStarted => firstWriteStarted.Task;

        public bool ConcurrentWriteObserved { get; private set; }

        public void ReleaseWrites()
            => allowWritesToFinish.TrySetResult(true);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => new(WriteCoreAsync(buffer, cancellationToken));

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteCoreAsync(buffer.AsMemory(offset, count), cancellationToken);

        private async Task WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref activeWrites) > 1)
            {
                ConcurrentWriteObserved = true;
            }

            firstWriteStarted.TrySetResult(true);
            try
            {
                await allowWritesToFinish.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref activeWrites);
            }
        }
    }
}
