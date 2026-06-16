using System.Reflection;
using System.Text.Json;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime.Models;
using TianShu.Execution.Runtime;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Integration.Tests;

public sealed class TianShuExecutionRuntimeRemediationTests
{
    [Fact]
    public void BuildThreadResumeParams_WhenHistoryProvided_EmitsRoleAwareHistory()
    {
        var request = new ControlPlaneResumeThreadCommand
        {
            ThreadId = new ThreadId("thread-123"),
            History =
            [
                ControlPlaneStructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "message",
                    ["role"] = "system",
                    ["content"] = new object?[] { "system rule" },
                }),
                ControlPlaneStructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "message",
                    ["role"] = "user",
                    ["content"] = new object?[] { "older user" },
                }),
                ControlPlaneStructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "message",
                    ["role"] = "assistant",
                    ["content"] = new object?[] { "older assistant" },
                }),
            ],
        };

        var payload = ReflectionTestHelper.InvokeStaticMethod(typeof(TianShuExecutionRuntime), "BuildThreadResumeParams", request, new ExecutionRuntimeOptions());
        Assert.NotNull(payload);

        var json = JsonSerializer.SerializeToElement(payload);
        var historyJson = json.GetProperty("history");
        Assert.Equal(3, historyJson.GetArrayLength());

        var items = historyJson.EnumerateArray().ToArray();
        Assert.Equal("system", items[0].GetProperty("role").GetString());
        Assert.Equal("system rule", items[0].GetProperty("content")[0].GetString());
        Assert.Equal("user", items[1].GetProperty("role").GetString());
        Assert.Equal("older user", items[1].GetProperty("content")[0].GetString());
        Assert.Equal("assistant", items[2].GetProperty("role").GetString());
        Assert.Equal("older assistant", items[2].GetProperty("content")[0].GetString());
    }

    [Fact]
    public void BuildThreadResumeParams_WhenUserHistoryContainsTypedInputs_EmitsTypedContentArray()
    {
        var request = new ControlPlaneResumeThreadCommand
        {
            ThreadId = new ThreadId("thread-typed-history"),
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
                            ["type"] = "mention",
                            ["name"] = "worker-1",
                            ["path"] = "app://worker-1",
                        },
                        new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["type"] = "text",
                            ["text"] = "请继续处理",
                        },
                        new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["type"] = "local_image",
                            ["path"] = "D:/images/demo.png",
                        },
                    },
                }),
            ],
        };

        var payload = ReflectionTestHelper.InvokeStaticMethod(typeof(TianShuExecutionRuntime), "BuildThreadResumeParams", request, new ExecutionRuntimeOptions());
        Assert.NotNull(payload);

        var json = JsonSerializer.SerializeToElement(payload);
        var historyItem = Assert.Single(json.GetProperty("history").EnumerateArray());
        Assert.Equal("user", historyItem.GetProperty("role").GetString());

        var content = historyItem.GetProperty("content");
        Assert.Equal(3, content.GetArrayLength());
        Assert.Equal("mention", content[0].GetProperty("type").GetString());
        Assert.Equal("worker-1", content[0].GetProperty("name").GetString());
        Assert.Equal("text", content[1].GetProperty("type").GetString());
        Assert.Equal("请继续处理", content[1].GetProperty("text").GetString());
        Assert.Equal("local_image", content[2].GetProperty("type").GetString());
        Assert.Equal("D:/images/demo.png", content[2].GetProperty("path").GetString());
    }

    [Fact]
    public void BuildMessagesFromThreadHistory_WhenSeedHistoryPresent_IncludesPreludeMessages()
    {
        var seedHistory = new[]
        {
            new AgentThreadSeedHistoryItem { Role = "system", Content = "system prelude" },
            new AgentThreadSeedHistoryItem { Role = "assistant", Content = "assistant prelude" },
        };
        var turns = new[]
        {
            new AgentThreadTurn
            {
                Items =
                [
                    new UserMessageThreadItem
                    {
                        Type = "user_message",
                        Content =
                        [
                            new TextUserInput
                            {
                                Type = "text",
                                Text = "older user",
                            },
                        ],
                    },
                    new AgentMessageThreadItem
                    {
                        Type = "assistant_message",
                        MessageText = "older assistant",
                    },
                ],
            },
        };

        var result = ReflectionTestHelper.InvokeStaticMethod(typeof(TianShuExecutionRuntime), "BuildMessagesFromThreadHistory", seedHistory, turns);
        var messages = Assert.IsAssignableFrom<IReadOnlyList<ConversationMessage>>(result);

        Assert.Collection(
            messages,
            item =>
            {
                Assert.Equal(ConversationRole.System, item.Role);
                Assert.Equal("system prelude", item.Content);
            },
            item =>
            {
                Assert.Equal(ConversationRole.Assistant, item.Role);
                Assert.Equal("assistant prelude", item.Content);
            },
            item =>
            {
                Assert.Equal(ConversationRole.User, item.Role);
                Assert.Equal("older user", item.Content);
            },
            item =>
            {
                Assert.Equal(ConversationRole.Assistant, item.Role);
                Assert.Equal("older assistant", item.Content);
            });
    }

    [Fact]
    public void BuildMessagesFromThreadHistory_WhenRichItemsPresent_UsesOnlyConversationBodies()
    {
        var seedHistory = new[]
        {
            new AgentThreadSeedHistoryItem { Role = "system", Content = "system rich" },
            new AgentThreadSeedHistoryItem { Role = "assistant", Content = "assistant prelude" },
        };
        var turns = new[]
        {
            new AgentThreadTurn
            {
                Items =
                [
                    new ReasoningThreadItem
                    {
                        Type = "reasoning",
                        Content = ["hidden reasoning"],
                    },
                    new ContextCompactionThreadItem
                    {
                        Type = "contextCompaction",
                    },
                    new UserMessageThreadItem
                    {
                        Type = "userMessage",
                        Content =
                        [
                            new TextUserInput
                            {
                                Type = "text",
                                Text = $"line 1{Environment.NewLine}line 2",
                            },
                        ],
                    },
                    new AgentMessageThreadItem
                    {
                        Type = "agentMessage",
                        MessageText = "assistant reply",
                    },
                    new WebSearchThreadItem
                    {
                        Type = "webSearch",
                        Query = "ignored search",
                    },
                    new UserMessageThreadItem
                    {
                        Type = "user_message",
                        Content =
                        [
                            new TextUserInput
                            {
                                Type = "text",
                                Text = "legacy user",
                            },
                        ],
                    },
                    new AgentMessageThreadItem
                    {
                        Type = "assistant_message",
                        MessageText = "legacy assistant",
                    },
                ],
            },
        };

        var result = ReflectionTestHelper.InvokeStaticMethod(typeof(TianShuExecutionRuntime), "BuildMessagesFromThreadHistory", seedHistory, turns);
        var messages = Assert.IsAssignableFrom<IReadOnlyList<ConversationMessage>>(result);

        Assert.Collection(
            messages,
            item =>
            {
                Assert.Equal(ConversationRole.System, item.Role);
                Assert.Equal("system rich", item.Content);
            },
            item =>
            {
                Assert.Equal(ConversationRole.Assistant, item.Role);
                Assert.Equal("assistant prelude", item.Content);
            },
            item =>
            {
                Assert.Equal(ConversationRole.User, item.Role);
                Assert.Equal($"line 1{Environment.NewLine}line 2", item.Content);
            },
            item =>
            {
                Assert.Equal(ConversationRole.Assistant, item.Role);
                Assert.Equal("assistant reply", item.Content);
            },
            item =>
            {
                Assert.Equal(ConversationRole.User, item.Role);
                Assert.Equal("legacy user", item.Content);
            },
            item =>
            {
                Assert.Equal(ConversationRole.Assistant, item.Role);
                Assert.Equal("legacy assistant", item.Content);
            });
    }

    [Fact]
    public void BuildMessagesFromThreadHistory_WhenAssistantItemPrecedesUserItem_ReordersToUserThenAssistant()
    {
        var turns = new[]
        {
            new AgentThreadTurn
            {
                Items =
                [
                    new AgentMessageThreadItem
                    {
                        Type = "assistant_message",
                        MessageText = "先出现的回答",
                    },
                    new UserMessageThreadItem
                    {
                        Type = "user_message",
                        Content =
                        [
                            new TextUserInput
                            {
                                Type = "text",
                                Text = "实际提问",
                            },
                        ],
                    },
                ],
            },
        };

        var result = ReflectionTestHelper.InvokeStaticMethod(
            typeof(TianShuExecutionRuntime),
            "BuildMessagesFromThreadHistory",
            Array.Empty<AgentThreadSeedHistoryItem>(),
            turns);
        var messages = Assert.IsAssignableFrom<IReadOnlyList<ConversationMessage>>(result);

        Assert.Collection(
            messages,
            item =>
            {
                Assert.Equal(ConversationRole.User, item.Role);
                Assert.Equal("实际提问", item.Content);
            },
            item =>
            {
                Assert.Equal(ConversationRole.Assistant, item.Role);
                Assert.Equal("先出现的回答", item.Content);
            });
    }

    [Fact]
    public void ConversationMessage_ShouldNotExposeUiBindingContract()
    {
        Assert.DoesNotContain(
            typeof(ConversationMessage).GetInterfaces(),
            static contract => string.Equals(contract.FullName, "System.ComponentModel.INotifyPropertyChanged", StringComparison.Ordinal));

        Assert.Null(typeof(ConversationMessage).GetEvent(
            "PropertyChanged",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
    }

    [Fact]
    public void ProtocolAdapters_ShouldExposeCapabilityState()
    {
        var defaultAdapter = ProviderRuntimeBootstrapRegistry.CreateRuntimeState(null).ProtocolAdapter;
        Assert.False(defaultAdapter.IsExperimental);
        Assert.Contains("完整支持", defaultAdapter.CapabilitySummary, StringComparison.Ordinal);

        IProtocolAdapter anthropic = new TestAnthropicMessagesProtocolAdapter();
        Assert.True(anthropic.IsExperimental);
        Assert.Contains("实验性", anthropic.CapabilitySummary, StringComparison.Ordinal);
        Assert.Contains("未实现", anthropic.CapabilitySummary, StringComparison.Ordinal);
    }

    public static IEnumerable<object?[]> StructuredNotificationMetadataCases()
    {
        yield return new object?[]
        {
            "{\"method\":\"turn/steered\",\"params\":{\"threadId\":\"thread-1\",\"turnId\":\"turn-1\",\"status\":\"accepted\",\"source\":\"late_steer_input\"}}",
            ControlPlaneConversationStreamEventKind.TurnSteered,
            null,
            "accepted",
            null,
            null,
            "late_steer_input",
            "turn/steered",
        };

        yield return new object?[]
        {
            "{\"method\":\"mcpServerStatus/list/updated\",\"params\":{\"data\":[{\"name\":\"a\"},{\"name\":\"b\"}]}}",
            ControlPlaneConversationStreamEventKind.McpServerStatusUpdated,
            ControlPlaneConversationStreamPayloadKind.McpServerStatus,
            null,
            null,
            null,
            null,
            "mcpServerStatus/list/updated",
        };
    }

    [Theory]
    [MemberData(nameof(StructuredNotificationMetadataCases))]
    public void HandleNotification_WhenStructuredKernelNotificationsArrive_PopulatesStructuredFields(
        string notificationJson,
        ControlPlaneConversationStreamEventKind expectedKind,
        ControlPlaneConversationStreamPayloadKind? expectedPayloadKind,
        string? expectedStatus,
        string? expectedTaskType,
        string? expectedOperationName,
        string? expectedSource,
        string expectedSourceMethod)
    {
        var runtime = new TianShuExecutionRuntime();
        ControlPlaneConversationStreamEvent? streamEvent = null;
        runtime.StreamEventReceived += (_, args) => streamEvent = args.StreamEvent;

        ReflectionTestHelper.InvokeMethod(
            runtime,
            "HandleNotification",
            ReflectionTestHelper.ParseJsonElement(notificationJson),
            notificationJson);

        Assert.NotNull(streamEvent);
        Assert.Equal(expectedKind, streamEvent!.Kind);
        Assert.Equal(expectedStatus, streamEvent.Status);
        Assert.Equal(expectedTaskType, streamEvent.TaskType);
        Assert.Equal(expectedOperationName, streamEvent.OperationName);
        Assert.Equal(expectedSource, streamEvent.Source);
        Assert.Equal(expectedSourceMethod, streamEvent.SourceMethod);
        Assert.Null(streamEvent.Diagnostics);

        switch (expectedPayloadKind)
        {
            case ControlPlaneConversationStreamPayloadKind.Task:
            {
                var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.Task);
                Assert.Equal(expectedTaskType, ReadStructuredString(payload, "taskType"));
                Assert.Equal(expectedStatus, ReadStructuredString(payload, "status"));
                break;
            }
            case ControlPlaneConversationStreamPayloadKind.Operation:
            {
                var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.Operation);
                Assert.Equal(expectedOperationName, ReadStructuredString(payload, "operationName"));
                Assert.Equal(expectedStatus, ReadStructuredString(payload, "phase"));
                break;
            }
            case ControlPlaneConversationStreamPayloadKind.McpServerStatus:
            {
                var payload = GetPayloadValue(streamEvent, ControlPlaneConversationStreamPayloadKind.McpServerStatus);
                Assert.Equal("2", ReadStructuredString(payload, "count"));
                Assert.Equal(2, ReadStructuredItems(payload, "servers").Count);
                break;
            }
            default:
                Assert.Null(streamEvent.PayloadKind);
                Assert.Null(streamEvent.Payload);
                break;
        }
    }

    private static StructuredValue GetPayloadValue(
        ControlPlaneConversationStreamEvent streamEvent,
        ControlPlaneConversationStreamPayloadKind expectedKind)
    {
        Assert.Equal(expectedKind, streamEvent.PayloadKind);
        return Assert.IsType<StructuredValue>(streamEvent.Payload);
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
}

