using System.Reflection;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

public sealed class AppHostServerSeedHistoryTests
{
    [Fact]
    public async Task RunAsync_WhenTurnStartProvidesHistory_PersistsSeedHistoryIntoThreadState()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_seed_history_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """
                {"jsonrpc":"2.0","id":1,"method":"thread/resume","params":{"threadId":"thread_seed_history_001","history":[{"type":"message","role":"system","content":[{"type":"input_text","text":"be concise"}]},{"type":"message","role":"assistant","content":[{"type":"output_text","text":"previous answer"}]}]}}
                """);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await Task.Delay(400);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var threadResume = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result").GetProperty("thread");
                var resumedThreadId = threadResume.GetProperty("id").GetString();
                Assert.False(string.IsNullOrWhiteSpace(resumedThreadId));

                var record = await threadStore.GetThreadAsync(resumedThreadId!, CancellationToken.None);
                Assert.NotNull(record);
                Assert.Collection(
                    record!.SeedHistory,
                    item =>
                    {
                        Assert.Equal("system", item.Role);
                        Assert.Equal("be concise", item.Content);
                    },
                    item =>
                    {
                        Assert.Equal("assistant", item.Role);
                        Assert.Equal("previous answer", item.Content);
                    });

                Assert.False(threadResume.TryGetProperty("seedHistory", out _));
                var turns = threadResume.GetProperty("turns");
                Assert.Equal(2, turns.GetArrayLength());
                Assert.Equal("seed_history_000001", turns[0].GetProperty("id").GetString());
                Assert.Equal("userMessage", turns[0].GetProperty("items")[0].GetProperty("type").GetString());
                Assert.Equal("be concise", turns[0].GetProperty("items")[0].GetProperty("content")[0].GetProperty("text").GetString());
                Assert.Equal("agentMessage", turns[1].GetProperty("items")[0].GetProperty("type").GetString());
                Assert.Equal("previous answer", turns[1].GetProperty("items")[0].GetProperty("text").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task BuildThreadSessionResponse_WhenIncludeTurnsEnabled_EmitsSessionConfigurationReplayMessagesAndTurnHistory()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);

            var created = await threadStore.CreateThreadAsync("thread_session_response_001", root, CancellationToken.None);
            var withHistory = await threadStore.SetSeedHistoryAsync(
                created.Id,
                [
                    new KernelConversationHistoryItem
                    {
                        Role = "user",
                        Content = "[mention:$worker-1](app://worker-1)\n请继续处理",
                        Inputs =
                        [
                            new KernelConversationInputRecord
                            {
                                Type = "mention",
                                Name = "$worker-1",
                                Path = "app://worker-1",
                            },
                            new KernelConversationInputRecord
                            {
                                Type = "text",
                                Text = "请继续处理",
                            },
                        ],
                    },
                ],
                CancellationToken.None);
            Assert.NotNull(withHistory);

            var snapshot = KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder.FromRecord(withHistory!, "gpt-5.4", "openai", "never").Build());
            withHistory.ForkedFromThreadId = "thread_parent_001";
            withHistory.ConfigSnapshot = snapshot;
            var withSnapshot = await threadStore.UpsertThreadAsync(withHistory, CancellationToken.None);
            await threadStore.AppendCompletedTurnAsync(
                withSnapshot.Id,
                "turn_session_response_001",
                "legacy user",
                "legacy assistant",
                "completed",
                CancellationToken.None,
                items:
                [
                    new KernelTurnItemRecord
                    {
                        Id = "assistant-item-001",
                        Type = "assistant_message",
                        Payload = JsonSerializer.SerializeToElement(new
                        {
                            text = "先出现的回答",
                        }),
                    },
                    new KernelTurnItemRecord
                    {
                        Id = "user-item-001",
                        Type = "user_message",
                        Payload = JsonSerializer.SerializeToElement(new
                        {
                            content = new object[]
                            {
                                new
                                {
                                    type = "text",
                                    text = "实际提问",
                                },
                            },
                        }),
                    },
                ]);

            var record = await threadStore.GetThreadAsync(withSnapshot.Id, CancellationToken.None);
            Assert.NotNull(record);
            await threadStore.RolloutRecorder.EnsureSessionMetaAsync(
                record!.Id,
                KernelRolloutStateMapper.ToRolloutThreadRecord(record, snapshot),
                CancellationToken.None);
            await threadStore.RolloutRecorder.CloseThreadWriterAsync(record.Id, CancellationToken.None);

            var server = new AppHostServer(new StringReader(string.Empty), new StringWriter(), threadStore);
            var method = typeof(AppHostServer).GetMethod("BuildThreadSessionResponse", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var payload = method!.Invoke(server, [record, true, null, null]);
            var json = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.True(json.TryGetProperty("sessionConfiguration", out var sessionConfiguration));
            Assert.False(json.TryGetProperty("configSnapshot", out _));
            Assert.False(json.TryGetProperty("messagesAreAuthoritative", out _));
            Assert.True(json.TryGetProperty("messages", out var messages));
            Assert.Equal("gpt-5.4", json.GetProperty("model").GetString());
            Assert.Equal("openai", json.GetProperty("modelProvider").GetString());
            Assert.Equal("never", json.GetProperty("approvalPolicy").GetString());
            Assert.Equal("gpt-5.4", sessionConfiguration.GetProperty("model").GetString());
            Assert.Equal("openai", sessionConfiguration.GetProperty("modelProvider").GetString());
            Assert.Equal("never", sessionConfiguration.GetProperty("approvalPolicy").GetString());
            Assert.True(messages.GetArrayLength() >= 3);

            var turns = json.GetProperty("thread").GetProperty("turns");
            Assert.Equal(2, turns.GetArrayLength());

            var seedTurn = turns[0];
            Assert.Equal("seed_history_000001", seedTurn.GetProperty("id").GetString());
            Assert.Equal("userMessage", seedTurn.GetProperty("items")[0].GetProperty("type").GetString());
            var seedContent = seedTurn.GetProperty("items")[0].GetProperty("content");
            Assert.Equal(2, seedContent.GetArrayLength());
            Assert.Equal("mention", seedContent[0].GetProperty("type").GetString());
            Assert.Equal("$worker-1", seedContent[0].GetProperty("name").GetString());
            Assert.Equal("text", seedContent[1].GetProperty("type").GetString());
            Assert.Equal("请继续处理", seedContent[1].GetProperty("text").GetString());

            var actualTurn = turns[1];
            var actualTexts = actualTurn
                .GetProperty("items")
                .EnumerateArray()
                .SelectMany(ExtractTurnItemTexts)
                .ToArray();
            Assert.Contains("实际提问", actualTexts);
            Assert.Contains("先出现的回答", actualTexts);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenTurnStartProvidesTypedHistory_PersistsSeedHistoryInputsAndThreadReadRoundTrips()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_seed_history_typed_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """
                {"jsonrpc":"2.0","id":1,"method":"thread/resume","params":{"threadId":"__THREAD_ID__","history":[{"type":"message","role":"user","content":[{"type":"mention","name":"worker-1","path":"app://worker-1"},{"type":"text","text":"请继续处理"},{"type":"local_image","path":"D:/images/demo.png"}]},{"type":"message","role":"assistant","content":[{"type":"output_text","text":"previous answer"}]}]}}
                """
                .Replace("__THREAD_ID__", threadId, StringComparison.Ordinal));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await Task.Delay(400);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var threadResume = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result").GetProperty("thread");
                var resumedThreadId = threadResume.GetProperty("id").GetString();
                Assert.False(string.IsNullOrWhiteSpace(resumedThreadId));

                var record = await threadStore.GetThreadAsync(resumedThreadId!, CancellationToken.None);
                Assert.NotNull(record);
                Assert.Equal(2, record!.SeedHistory.Count);
                Assert.Equal("user", record.SeedHistory[0].Role);
                Assert.Equal(3, record.SeedHistory[0].Inputs.Count);
                Assert.Equal("mention", record.SeedHistory[0].Inputs[0].Type);
                Assert.Equal("worker-1", record.SeedHistory[0].Inputs[0].Name);
                Assert.Equal("app://worker-1", record.SeedHistory[0].Inputs[0].Path);
                Assert.Equal("text", record.SeedHistory[0].Inputs[1].Type);
                Assert.Equal("请继续处理", record.SeedHistory[0].Inputs[1].Text);
                Assert.Equal("local_image", record.SeedHistory[0].Inputs[2].Type);
                Assert.Equal("D:/images/demo.png", record.SeedHistory[0].Inputs[2].Path);
                Assert.Equal("assistant", record.SeedHistory[1].Role);
                Assert.Single(record.SeedHistory[1].Inputs);
                Assert.Equal("text", record.SeedHistory[1].Inputs[0].Type);
                Assert.Equal("previous answer", record.SeedHistory[1].Inputs[0].Text);

                Assert.False(threadResume.TryGetProperty("seedHistory", out _));
                var turns = threadResume.GetProperty("turns");
                Assert.Equal(2, turns.GetArrayLength());

                var userSeed = turns[0].GetProperty("items")[0];
                Assert.Equal("userMessage", userSeed.GetProperty("type").GetString());
                var inputs = userSeed.GetProperty("content");
                Assert.Equal(3, inputs.GetArrayLength());
                Assert.Equal("mention", inputs[0].GetProperty("type").GetString());
                Assert.Equal("worker-1", inputs[0].GetProperty("name").GetString());
                Assert.Equal("text", inputs[1].GetProperty("type").GetString());
                Assert.Equal("请继续处理", inputs[1].GetProperty("text").GetString());
                Assert.Equal("local_image", inputs[2].GetProperty("type").GetString());
                Assert.Equal("D:/images/demo.png", inputs[2].GetProperty("path").GetString());

                var assistantSeed = turns[1].GetProperty("items")[0];
                Assert.Equal("agentMessage", assistantSeed.GetProperty("type").GetString());
                Assert.Equal("previous answer", assistantSeed.GetProperty("text").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenResumeHistoryContainsLegacyToolOutputWrappers_ShouldNormalizeOutputPayloads()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_seed_history_raw_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input = string.Join(
                Environment.NewLine,
                """
                {"jsonrpc":"2.0","id":1,"method":"thread/resume","params":{"threadId":"thread_seed_history_raw_001","history":[{"type":"message","role":"user","content":[{"type":"input_text","text":"run tool"}]},{"type":"function_call","name":"shell_command","arguments":"{\"command\":\"pwd\"}","call_id":"call_123"},{"type":"function_call_output","call_id":"call_123","output":"D:/repo"},{"type":"function_call_output","call_id":"call_124","output":{"body":"D:/repo-legacy","success":true}},{"type":"custom_tool_call","name":"local_tool","input":"demo-input","call_id":"call_125"},{"type":"custom_tool_call_output","call_id":"call_125","output":{"content":[{"type":"input_text","text":"done"}],"success":true}}]}}
                """);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await Task.Delay(400);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var threadResume = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result").GetProperty("thread");
                var resumedThreadId = threadResume.GetProperty("id").GetString();
                Assert.False(string.IsNullOrWhiteSpace(resumedThreadId));

                var record = await threadStore.GetThreadAsync(resumedThreadId!, CancellationToken.None);
                Assert.NotNull(record);
                Assert.Equal(6, record!.SeedHistory.Count);

                var functionCall = record.SeedHistory[1];
                Assert.True(functionCall.RawResponseItem.HasValue);
                Assert.Equal("function_call", functionCall.RawResponseItem.Value.GetProperty("type").GetString());
                Assert.Equal("shell_command", functionCall.RawResponseItem.Value.GetProperty("name").GetString());
                Assert.Equal("call_123", functionCall.RawResponseItem.Value.GetProperty("call_id").GetString());
                Assert.Equal("""{"command":"pwd"}""", functionCall.RawResponseItem.Value.GetProperty("arguments").GetString());

                var functionCallOutputString = record.SeedHistory[2];
                Assert.True(functionCallOutputString.RawResponseItem.HasValue);
                Assert.Equal("function_call_output", functionCallOutputString.RawResponseItem.Value.GetProperty("type").GetString());
                Assert.Equal("call_123", functionCallOutputString.RawResponseItem.Value.GetProperty("call_id").GetString());
                Assert.Equal(JsonValueKind.String, functionCallOutputString.RawResponseItem.Value.GetProperty("output").ValueKind);
                Assert.Equal("D:/repo", functionCallOutputString.RawResponseItem.Value.GetProperty("output").GetString());

                var functionCallOutputObject = record.SeedHistory[3];
                Assert.True(functionCallOutputObject.RawResponseItem.HasValue);
                Assert.Equal("function_call_output", functionCallOutputObject.RawResponseItem.Value.GetProperty("type").GetString());
                Assert.Equal("call_124", functionCallOutputObject.RawResponseItem.Value.GetProperty("call_id").GetString());
                Assert.Equal(JsonValueKind.String, functionCallOutputObject.RawResponseItem.Value.GetProperty("output").ValueKind);
                Assert.Equal("D:/repo-legacy", functionCallOutputObject.RawResponseItem.Value.GetProperty("output").GetString());

                var customToolCallOutput = record.SeedHistory[5];
                Assert.True(customToolCallOutput.RawResponseItem.HasValue);
                Assert.Equal("custom_tool_call_output", customToolCallOutput.RawResponseItem.Value.GetProperty("type").GetString());
                Assert.Equal("call_125", customToolCallOutput.RawResponseItem.Value.GetProperty("call_id").GetString());
                Assert.Equal(JsonValueKind.Array, customToolCallOutput.RawResponseItem.Value.GetProperty("output").ValueKind);
                Assert.Equal("done", customToolCallOutput.RawResponseItem.Value.GetProperty("output")[0].GetProperty("text").GetString());

                await threadStore.RolloutRecorder.CloseThreadWriterAsync(resumedThreadId!, CancellationToken.None);
                var rolloutRecord = await threadStore.RolloutRecorder.RehydrateThreadAsync(
                    threadStore.RolloutRecorder.GetRolloutPath(resumedThreadId),
                    CancellationToken.None);
                Assert.NotNull(rolloutRecord);
                var rehydratedRecord = KernelRolloutStateMapper.FromRolloutThreadRecord(rolloutRecord!);
                Assert.Equal(6, rehydratedRecord.SeedHistory.Count);
                Assert.True(rehydratedRecord.SeedHistory[1].RawResponseItem.HasValue);
                Assert.Equal("function_call", rehydratedRecord.SeedHistory[1].RawResponseItem.Value.GetProperty("type").GetString());
                Assert.True(rehydratedRecord.SeedHistory[2].RawResponseItem.HasValue);
                Assert.Equal("function_call_output", rehydratedRecord.SeedHistory[2].RawResponseItem.Value.GetProperty("type").GetString());
                Assert.Equal(JsonValueKind.String, rehydratedRecord.SeedHistory[2].RawResponseItem.Value.GetProperty("output").ValueKind);
                Assert.True(rehydratedRecord.SeedHistory[3].RawResponseItem.HasValue);
                Assert.Equal(JsonValueKind.String, rehydratedRecord.SeedHistory[3].RawResponseItem.Value.GetProperty("output").ValueKind);
                Assert.True(rehydratedRecord.SeedHistory[5].RawResponseItem.HasValue);
                Assert.Equal("custom_tool_call_output", rehydratedRecord.SeedHistory[5].RawResponseItem.Value.GetProperty("type").GetString());
                Assert.Equal(JsonValueKind.Array, rehydratedRecord.SeedHistory[5].RawResponseItem.Value.GetProperty("output").ValueKind);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenResumeHistoryContainsLegacyItems_ShouldRejectRequest()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_seed_history_legacy_reject_001";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"thread/resume","params":{"threadId":"thread_seed_history_legacy_reject_001","history":[{"role":"user","content":"legacy item"}]}}
                """;

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var message = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .Single(static document => IsResponseId(document.RootElement, 1));

            var error = message.RootElement.GetProperty("error");
            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Contains("history[0]", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Contains("type", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_WhenResumeHistoryContainsUnknownResponseItemType_ShouldRejectRequest()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_seed_history_unknown_type_001";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"thread/resume","params":{"threadId":"thread_seed_history_unknown_type_001","history":[{"type":"legacy_message","role":"user","content":[{"type":"input_text","text":"hello"}]}]}}
                """;

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var message = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .Single(static document => IsResponseId(document.RootElement, 1));

            var error = message.RootElement.GetProperty("error");
            Assert.Equal(-32602, error.GetProperty("code").GetInt32());
            Assert.Contains("history[0]", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Contains("legacy_message", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public Task RunAsync_WhenResumeHistoryMessageMissingRole_ShouldRejectRequest()
        => AssertInvalidResumeHistoryRequestAsync(
            "thread_seed_history_message_missing_role_001",
            """
            {"type":"message","content":[{"type":"input_text","text":"hello"}]}
            """,
            "message",
            "role");

    [Fact]
    public Task RunAsync_WhenResumeHistoryFunctionCallMissingCallId_ShouldRejectRequest()
        => AssertInvalidResumeHistoryRequestAsync(
            "thread_seed_history_function_call_missing_call_id_001",
            """
            {"type":"function_call","name":"shell_command","arguments":"{\"command\":\"pwd\"}"}
            """,
            "function_call",
            "call_id");

    [Fact]
    public Task RunAsync_WhenResumeHistoryFunctionCallOutputUsesNumericOutput_ShouldRejectRequest()
        => AssertInvalidResumeHistoryRequestAsync(
            "thread_seed_history_function_call_output_invalid_output_001",
            """
            {"type":"function_call_output","call_id":"call_123","output":123}
            """,
            "function_call_output",
            "output");

    [Fact]
    public Task RunAsync_WhenResumeHistoryToolSearchOutputMissingTools_ShouldRejectRequest()
        => AssertInvalidResumeHistoryRequestAsync(
            "thread_seed_history_tool_search_output_missing_tools_001",
            """
            {"type":"tool_search_output","execution":"local","status":"completed"}
            """,
            "tool_search_output",
            "tools");

    [Fact]
    public async Task RunAsync_WhenThreadReadPrefersStaleRollout_PreservesStoreSeedHistory()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "019d478e-5ed6-7757-8141-878cfe65343b";

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);

            var created = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            var snapshot = KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder.FromRecord(created, "gpt-5", "openai", "never").Build());
            await setupStore.RolloutRecorder.EnsureSessionMetaAsync(
                created.Id,
                KernelRolloutStateMapper.ToRolloutThreadRecord(created, snapshot),
                CancellationToken.None);

            KernelConversationHistoryItem[] seedHistory =
            [
                new()
                {
                    Role = "user",
                    Content = "run tool",
                    Inputs =
                    [
                        new KernelConversationInputRecord
                        {
                            Type = "text",
                            Text = "run tool",
                        },
                    ],
                    RawResponseItem = JsonSerializer.SerializeToElement(new
                    {
                        type = "message",
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "input_text",
                                text = "run tool",
                            },
                        },
                    }),
                },
                new()
                {
                    Role = "user",
                    RawResponseItem = JsonSerializer.SerializeToElement(new
                    {
                        type = "function_call",
                        name = "shell_command",
                        arguments = """{"command":"pwd"}""",
                        call_id = "call_stale_001",
                    }),
                },
            ];

            _ = await setupStore.SetSeedHistoryAsync(created.Id, seedHistory, CancellationToken.None);
            await setupStore.RolloutRecorder.CloseThreadWriterAsync(created.Id, CancellationToken.None);

            var threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);

            var reader = new StringReader(
                KernelAppServerTestProtocol.WithInitialize(
                    """
                    {"jsonrpc":"2.0","id":1,"method":"thread/read","params":{"threadId":"019d478e-5ed6-7757-8141-878cfe65343b","includeTurns":true}}
                    """));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);
            await Task.Delay(400);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();

            try
            {
                var response = messages.Single(x => IsResponseId(x.RootElement, 1));
                Assert.True(response.RootElement.TryGetProperty("result", out var result), writer.ToString());
                Assert.True(result.TryGetProperty("thread", out _), writer.ToString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            var persisted = await threadStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.Equal(2, persisted!.SeedHistory.Count);
            Assert.True(persisted.SeedHistory[1].RawResponseItem.HasValue);
            Assert.Equal("function_call", persisted.SeedHistory[1].RawResponseItem.Value.GetProperty("type").GetString());
            Assert.Equal("call_stale_001", persisted.SeedHistory[1].RawResponseItem.Value.GetProperty("call_id").GetString());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void BuildProviderMessages_WhenThreadHasSeedHistory_PrependsPreludeBeforeTurnsAndCurrentUser()
    {
        var thread = new KernelThreadRecord
        {
            SeedHistory =
            [
                new KernelConversationHistoryItem { Role = "system", Content = "system prelude" },
                new KernelConversationHistoryItem { Role = "assistant", Content = "assistant prelude" },
            ],
            Turns =
            [
                new KernelTurnRecord { UserMessage = "older user", AssistantMessage = "older assistant" },
            ],
        };

        var method = typeof(AppHostServer).GetMethod(
            "BuildProviderMessages",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                typeof(KernelThreadRecord),
                typeof(string),
            ],
            modifiers: null);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [thread, "current user"]);
        var messages = Assert.IsType<List<Dictionary<string, object?>>>(result);

        Assert.Collection(
            messages,
            item => AssertMessage(item, "system", "system prelude"),
            item => AssertMessage(item, "assistant", "assistant prelude"),
            item => AssertMessage(item, "user", "older user"),
            item => AssertMessage(item, "assistant", "older assistant"),
            item => AssertMessage(item, "user", "current user"));
    }

    [Fact]
    public void BuildProviderMessages_WhenDeveloperInstructionsProvided_PrependsDeveloperMessage()
    {
        var thread = new KernelThreadRecord
        {
            Turns =
            [
                new KernelTurnRecord { UserMessage = "older user", AssistantMessage = "older assistant" },
            ],
        };

        var method = typeof(AppHostServer).GetMethod(
            "BuildProviderMessages",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                typeof(KernelThreadRecord),
                typeof(string),
                typeof(string),
            ],
            modifiers: null);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [thread, "current user", "fallback developer prompt"]);
        var messages = Assert.IsType<List<Dictionary<string, object?>>>(result);

        Assert.Collection(
            messages,
            item => AssertMessage(item, "developer", "fallback developer prompt"),
            item => AssertMessage(item, "user", "older user"),
            item => AssertMessage(item, "assistant", "older assistant"),
            item => AssertMessage(item, "user", "current user"));
    }

    [Fact]
    public void BuildProviderMessages_WhenContextualUserInstructionsProvided_PrependsUserMessageAfterDeveloper()
    {
        var thread = new KernelThreadRecord
        {
            Turns =
            [
                new KernelTurnRecord { UserMessage = "older user", AssistantMessage = "older assistant" },
            ],
        };

        var method = typeof(AppHostServer).GetMethod(
            "BuildProviderMessages",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                typeof(KernelThreadRecord),
                typeof(string),
                typeof(string),
                typeof(IReadOnlyList<string>),
            ],
            modifiers: null);
        Assert.NotNull(method);

        var result = method!.Invoke(
            null,
            [thread, "current user", "fallback developer prompt", new[] { "# AGENTS.md instructions for D:/Repo\n\n<INSTRUCTIONS>\nuser instructions\n</INSTRUCTIONS>" }]);
        var messages = Assert.IsType<List<Dictionary<string, object?>>>(result);

        Assert.Collection(
            messages,
            item => AssertMessage(item, "developer", "fallback developer prompt"),
            item => AssertMessage(item, "user", "# AGENTS.md instructions for D:/Repo\n\n<INSTRUCTIONS>\nuser instructions\n</INSTRUCTIONS>"),
            item => AssertMessage(item, "user", "older user"),
            item => AssertMessage(item, "assistant", "older assistant"),
            item => AssertMessage(item, "user", "current user"));
    }

    [Fact]
    public void BuildProviderMessages_WhenSeedHistoryUserHasTypedTextInput_PrefersTypedInputsOverFallbackContent()
    {
        var thread = new KernelThreadRecord
        {
            SeedHistory =
            [
                new KernelConversationHistoryItem
                {
                    Role = "user",
                    Content = "legacy fallback",
                    Inputs =
                    [
                        new KernelConversationInputRecord
                        {
                            Type = "text",
                            Text = "typed authoritative",
                        },
                    ],
                },
                new KernelConversationHistoryItem
                {
                    Role = "assistant",
                    Content = "assistant prelude",
                },
            ],
        };

        var method = typeof(AppHostServer).GetMethod(
            "BuildProviderMessages",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                typeof(KernelThreadRecord),
                typeof(string),
            ],
            modifiers: null);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [thread, "current user"]);
        var messages = Assert.IsType<List<Dictionary<string, object?>>>(result);

        Assert.Collection(
            messages,
            item => AssertMessage(item, "user", "typed authoritative"),
            item => AssertMessage(item, "assistant", "assistant prelude"),
            item => AssertMessage(item, "user", "current user"));
    }

    [Fact]
    public void BuildResponsesConversationInput_WhenSeedHistoryUserHasTypedTextInput_PrefersTypedInputsOverFallbackContent()
    {
        var thread = new KernelThreadRecord
        {
            SeedHistory =
            [
                new KernelConversationHistoryItem
                {
                    Role = "user",
                    Content = "legacy fallback",
                    Inputs =
                    [
                        new KernelConversationInputRecord
                        {
                            Type = "text",
                            Text = "typed authoritative",
                        },
                    ],
                },
                new KernelConversationHistoryItem
                {
                    Role = "assistant",
                    Content = "assistant prelude",
                },
            ],
        };

        var result = KernelTurnExecutionRuntimeHelpers.BuildResponsesConversationInput(thread, "current user", null, null, null);
        var payload = JsonSerializer.SerializeToElement(result);

        Assert.Equal(3, payload.GetArrayLength());
        Assert.Equal("user", payload[0].GetProperty("role").GetString());
        Assert.Equal("input_text", payload[0].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("typed authoritative", payload[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.NotEqual("legacy fallback", payload[0].GetProperty("content")[0].GetProperty("text").GetString());

        Assert.Equal("assistant", payload[1].GetProperty("role").GetString());
        Assert.Equal("output_text", payload[1].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("assistant prelude", payload[1].GetProperty("content")[0].GetProperty("text").GetString());

        Assert.Equal("user", payload[2].GetProperty("role").GetString());
        Assert.Equal("current user", payload[2].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void BuildResponsesConversationInput_WhenCurrentInputItemsPresent_UsesStructuredCurrentUserMessage()
    {
        IReadOnlyList<KernelTurnInputItem> currentInputItems =
        [
            new KernelTurnInputItem
            {
                Type = "mention",
                Name = "worker-1",
                Path = "app://worker-1",
            },
            new KernelTurnInputItem
            {
                Type = "text",
                Text = "当前 typed 输入",
            },
        ];

        var result = KernelTurnExecutionRuntimeHelpers.BuildResponsesConversationInput(null, "legacy current user", null, null, currentInputItems);
        var payload = JsonSerializer.SerializeToElement(result);

        var currentUser = Assert.Single(payload.EnumerateArray());
        Assert.Equal("user", currentUser.GetProperty("role").GetString());
        var content = currentUser.GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("input_text", content[0].GetProperty("type").GetString());
        Assert.Equal("[mention:$worker-1](app://worker-1)", content[0].GetProperty("text").GetString());
        Assert.Equal("input_text", content[1].GetProperty("type").GetString());
        Assert.Equal("当前 typed 输入", content[1].GetProperty("text").GetString());
    }

    [Fact]
    public void BuildProviderMessages_WhenTurnUserItemHasTypedInputs_PrefersTypedPreviewOverLegacyFallback()
    {
        using var userPayload = JsonDocument.Parse(
            """
            {
              "content": [
                { "type": "mention", "name": "worker-1", "path": "app://worker-1" },
                { "type": "text", "text": "typed authoritative" }
              ]
            }
            """);
        using var assistantPayload = JsonDocument.Parse(
            """
            {
              "text": "assistant reply"
            }
            """);

        var thread = new KernelThreadRecord
        {
            Turns =
            [
                new KernelTurnRecord
                {
                    UserMessage = "legacy fallback",
                    AssistantMessage = "legacy assistant fallback",
                    Items =
                    [
                        new KernelTurnItemRecord
                        {
                            Id = "user-item",
                            Type = "userMessage",
                            Payload = userPayload.RootElement.Clone(),
                        },
                        new KernelTurnItemRecord
                        {
                            Id = "assistant-item",
                            Type = "agentMessage",
                            Payload = assistantPayload.RootElement.Clone(),
                        },
                    ],
                },
            ],
        };

        var method = typeof(AppHostServer).GetMethod(
            "BuildProviderMessages",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                typeof(KernelThreadRecord),
                typeof(string),
            ],
            modifiers: null);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [thread, "current user"]);
        var messages = Assert.IsType<List<Dictionary<string, object?>>>(result);

        Assert.Collection(
            messages,
            item => AssertMessage(
                item,
                "user",
                $"[mention:$worker-1](app://worker-1){Environment.NewLine}typed authoritative"),
            item => AssertMessage(item, "assistant", "assistant reply"),
            item => AssertMessage(item, "user", "current user"));
    }

    [Fact]
    public void BuildResponsesConversationInput_WhenTurnUserItemHasTypedInputs_UsesStructuredContent()
    {
        using var userPayload = JsonDocument.Parse(
            """
            {
              "content": [
                { "type": "mention", "name": "worker-1", "path": "app://worker-1" },
                { "type": "text", "text": "typed authoritative" },
                { "type": "local_image", "path": "D:/images/demo.png" }
              ]
            }
            """);
        using var assistantPayload = JsonDocument.Parse(
            """
            {
              "text": "assistant reply"
            }
            """);

        var thread = new KernelThreadRecord
        {
            Turns =
            [
                new KernelTurnRecord
                {
                    UserMessage = "legacy fallback",
                    AssistantMessage = "legacy assistant fallback",
                    Items =
                    [
                        new KernelTurnItemRecord
                        {
                            Id = "assistant-item",
                            Type = "agentMessage",
                            Payload = assistantPayload.RootElement.Clone(),
                        },
                        new KernelTurnItemRecord
                        {
                            Id = "user-item",
                            Type = "userMessage",
                            Payload = userPayload.RootElement.Clone(),
                        },
                    ],
                },
            ],
        };

        var result = KernelTurnExecutionRuntimeHelpers.BuildResponsesConversationInput(thread, "current user", null, null, null);
        var payload = JsonSerializer.SerializeToElement(result);

        Assert.Equal(3, payload.GetArrayLength());
        Assert.Equal("user", payload[0].GetProperty("role").GetString());
        Assert.Equal(3, payload[0].GetProperty("content").GetArrayLength());
        Assert.Equal("input_text", payload[0].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("[mention:$worker-1](app://worker-1)", payload[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("input_text", payload[0].GetProperty("content")[1].GetProperty("type").GetString());
        Assert.Equal("typed authoritative", payload[0].GetProperty("content")[1].GetProperty("text").GetString());
        Assert.Equal("input_text", payload[0].GetProperty("content")[2].GetProperty("type").GetString());
        Assert.Equal("[local_image:D:/images/demo.png]", payload[0].GetProperty("content")[2].GetProperty("text").GetString());

        Assert.Equal("assistant", payload[1].GetProperty("role").GetString());
        Assert.Equal("output_text", payload[1].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("assistant reply", payload[1].GetProperty("content")[0].GetProperty("text").GetString());

        Assert.Equal("user", payload[2].GetProperty("role").GetString());
        Assert.Equal("current user", payload[2].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void BuildResponsesConversationInput_WhenTurnContainsSyntheticReasoningItem_ShouldSkipRawReplay()
    {
        using var reasoningPayload = JsonDocument.Parse(
            """
            {
              "id": "reasoning_turn_001",
              "type": "reasoning",
              "summary": ["本地推理摘要"]
            }
            """);

        var thread = new KernelThreadRecord
        {
            Turns =
            [
                new KernelTurnRecord
                {
                    UserMessage = "上一轮用户消息",
                    Items =
                    [
                        new KernelTurnItemRecord
                        {
                            Id = "reasoning-item",
                            Type = "reasoning",
                            Payload = reasoningPayload.RootElement.Clone(),
                        },
                    ],
                },
            ],
        };

        var result = KernelTurnExecutionRuntimeHelpers.BuildResponsesConversationInput(thread, "继续当前任务", null, null, null);
        var payload = JsonSerializer.SerializeToElement(result);

        Assert.Equal(2, payload.GetArrayLength());
        Assert.All(payload.EnumerateArray(), static item =>
        {
            if (item.TryGetProperty("type", out var type))
            {
                Assert.NotEqual("reasoning", type.GetString());
            }
        });
        Assert.Equal("user", payload[0].GetProperty("role").GetString());
        Assert.Equal("上一轮用户消息", payload[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("user", payload[1].GetProperty("role").GetString());
        Assert.Equal("继续当前任务", payload[1].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void BuildResponsesConversationInput_WhenTurnContainsProviderReasoningItem_ShouldReplayRawReasoningItem()
    {
        using var userPayload = JsonDocument.Parse(
            """
            {
              "content": [
                { "type": "text", "text": "上一轮用户消息" }
              ]
            }
            """);
        using var reasoningPayload = JsonDocument.Parse(
            """
            {
              "id": "rs_123",
              "type": "reasoning",
              "summary": ["provider summary"]
            }
            """);

        var thread = new KernelThreadRecord
        {
            Turns =
            [
                new KernelTurnRecord
                {
                    Items =
                    [
                        new KernelTurnItemRecord
                        {
                            Id = "user-item",
                            Type = "userMessage",
                            Payload = userPayload.RootElement.Clone(),
                        },
                        new KernelTurnItemRecord
                        {
                            Id = "reasoning-item",
                            Type = "reasoning",
                            Payload = reasoningPayload.RootElement.Clone(),
                        },
                    ],
                },
            ],
        };

        var result = KernelTurnExecutionRuntimeHelpers.BuildResponsesConversationInput(thread, "继续当前任务", null, null, null);
        var payload = JsonSerializer.SerializeToElement(result);

        Assert.Equal(3, payload.GetArrayLength());
        Assert.Equal("user", payload[0].GetProperty("role").GetString());
        Assert.Equal("上一轮用户消息", payload[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("reasoning", payload[1].GetProperty("type").GetString());
        Assert.Equal("rs_123", payload[1].GetProperty("id").GetString());
        Assert.Equal("user", payload[2].GetProperty("role").GetString());
        Assert.Equal("继续当前任务", payload[2].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void BuildResponsesConversationInput_WhenTurnContainsLegacyToolOutputWrappers_ShouldNormalizeReplayPayload()
    {
        using var userPayload = JsonDocument.Parse(
            """
            {
              "content": [
                { "type": "text", "text": "上一轮用户消息" }
              ]
            }
            """);
        using var functionCallOutputPayload = JsonDocument.Parse(
            """
            {
              "type": "function_call_output",
              "call_id": "call_legacy_fn",
              "output": {
                "body": "legacy-function-output",
                "success": true
              }
            }
            """);
        using var customToolCallOutputPayload = JsonDocument.Parse(
            """
            {
              "type": "custom_tool_call_output",
              "call_id": "call_legacy_custom",
              "output": {
                "content": [
                  { "type": "input_text", "text": "legacy-custom-output" }
                ],
                "success": true
              }
            }
            """);

        var thread = new KernelThreadRecord
        {
            Turns =
            [
                new KernelTurnRecord
                {
                    Items =
                    [
                        new KernelTurnItemRecord
                        {
                            Id = "user-item",
                            Type = "userMessage",
                            Payload = userPayload.RootElement.Clone(),
                        },
                        new KernelTurnItemRecord
                        {
                            Id = "function-output-item",
                            Type = "function_call_output",
                            Payload = functionCallOutputPayload.RootElement.Clone(),
                        },
                        new KernelTurnItemRecord
                        {
                            Id = "custom-output-item",
                            Type = "custom_tool_call_output",
                            Payload = customToolCallOutputPayload.RootElement.Clone(),
                        },
                    ],
                },
            ],
        };

        var result = KernelTurnExecutionRuntimeHelpers.BuildResponsesConversationInput(thread, "继续当前任务", null, null, null);
        var payload = JsonSerializer.SerializeToElement(result);

        Assert.Equal(4, payload.GetArrayLength());
        Assert.Equal("user", payload[0].GetProperty("role").GetString());
        Assert.Equal("上一轮用户消息", payload[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("function_call_output", payload[1].GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.String, payload[1].GetProperty("output").ValueKind);
        Assert.Equal("legacy-function-output", payload[1].GetProperty("output").GetString());
        Assert.Equal("custom_tool_call_output", payload[2].GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Array, payload[2].GetProperty("output").ValueKind);
        Assert.Equal("legacy-custom-output", payload[2].GetProperty("output")[0].GetProperty("text").GetString());
        Assert.Equal("user", payload[3].GetProperty("role").GetString());
        Assert.Equal("继续当前任务", payload[3].GetProperty("content")[0].GetProperty("text").GetString());
    }

    private static void AssertMessage(IReadOnlyDictionary<string, object?> item, string expectedRole, string expectedContent)
    {
        Assert.Equal(expectedRole, Assert.IsType<string>(item["role"]));
        Assert.Equal(expectedContent, Assert.IsType<string>(item["content"]));
    }

    private static IReadOnlyList<string> ExtractTurnItemTexts(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        var texts = new List<string>();
        if (item.TryGetProperty("text", out var directText) && directText.ValueKind == JsonValueKind.String)
        {
            texts.Add(directText.GetString() ?? string.Empty);
        }

        if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.ValueKind == JsonValueKind.Object
                    && contentItem.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    texts.Add(text.GetString() ?? string.Empty);
                }
            }
        }

        return texts;
    }

    private static bool IsResponseId(JsonElement root, int id)
        => root.TryGetProperty("id", out var idElement)
           && idElement.ValueKind == JsonValueKind.Number
           && idElement.GetInt32() == id;

    private static async Task AssertInvalidResumeHistoryRequestAsync(
        string threadId,
        string historyItemJson,
        params string[] expectedFragments)
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var input =
                """
                {"jsonrpc":"2.0","id":1,"method":"thread/resume","params":{"threadId":"__THREAD_ID__","history":[__HISTORY_ITEM__]}}
                """
                .Replace("__THREAD_ID__", threadId, StringComparison.Ordinal)
                .Replace("__HISTORY_ITEM__", historyItemJson, StringComparison.Ordinal);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            using var message = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .Single(static document => IsResponseId(document.RootElement, 1));

            var error = message.RootElement.GetProperty("error");
            Assert.Equal(-32602, error.GetProperty("code").GetInt32());

            var messageText = error.GetProperty("message").GetString();
            Assert.False(string.IsNullOrWhiteSpace(messageText));
            Assert.Contains("history[0]", messageText, StringComparison.Ordinal);
            foreach (var expectedFragment in expectedFragments)
            {
                Assert.Contains(expectedFragment, messageText, StringComparison.Ordinal);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tianshu-kernel-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
