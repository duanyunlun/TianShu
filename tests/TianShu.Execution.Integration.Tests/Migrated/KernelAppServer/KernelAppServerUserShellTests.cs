using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerUserShellTests
{
    [Fact]
    public async Task RunAsync_ShouldPersistStandaloneUserShellTurn_AndReplayUserShellHistoryWithoutPublicSource()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000601";
        const string expectedOutput = "user-shell-standalone";

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            using var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);

            await InvokeHandleUserShellRunAsync(server, 1, threadId, $"Write-Output '{expectedOutput}'");

            var runMessages = ParseMessages(writer.ToString());
            try
            {
                var result = GetResponseResult(runMessages, 1);
                Assert.False(result.GetProperty("reusedActiveTurn").GetBoolean());
                Assert.Equal("completed", result.GetProperty("turnStatus").GetString());
                Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
                Assert.Contains(expectedOutput, result.GetProperty("stdout").GetString(), StringComparison.Ordinal);
                Assert.Contains(runMessages, static x => IsNotificationMethod(x.RootElement, "turn/started"));

                var startedItem = Assert.Single(runMessages.Where(static x =>
                    IsCommandExecutionNotification(x.RootElement, "item/started")));
                AssertCommandExecutionItemHasNoSource(startedItem.RootElement.GetProperty("params").GetProperty("item"), "inProgress");

                var completedItem = Assert.Single(runMessages.Where(static x =>
                    IsCommandExecutionNotification(x.RootElement, "item/completed")));
                AssertCommandExecutionItemHasNoSource(completedItem.RootElement.GetProperty("params").GetProperty("item"), "completed");
            }
            finally
            {
                DisposeMessages(runMessages);
            }

            writer.GetStringBuilder().Clear();
            await InvokeHandleThreadReadAsync(server, 2, threadId, includeTurns: true);

            var readMessages = ParseMessages(writer.ToString());
            try
            {
                var result = GetResponseResult(readMessages, 2);
                AssertReplayMessagesContainUserShell(result.GetProperty("messages"), expectedOutput);
                AssertReplayMessagesContainUserShell(result.GetProperty("thread").GetProperty("messages"), expectedOutput);

                var turn = Assert.Single(result.GetProperty("thread").GetProperty("turns").EnumerateArray());
                var commandItem = Assert.Single(turn.GetProperty("items").EnumerateArray().Where(static item =>
                    string.Equals(item.GetProperty("type").GetString(), "commandExecution", StringComparison.Ordinal)));
                Assert.False(commandItem.TryGetProperty("source", out _));
            }
            finally
            {
                DisposeMessages(readMessages);
            }

            writer.GetStringBuilder().Clear();
            await InvokeHandleThreadResumeAsync(server, 3, threadId);

            var resumeMessages = ParseMessages(writer.ToString());
            try
            {
                var result = GetResponseResult(resumeMessages, 3);
                AssertReplayMessagesContainUserShell(result.GetProperty("messages"), expectedOutput);
                AssertReplayMessagesContainUserShell(result.GetProperty("thread").GetProperty("messages"), expectedOutput);
            }
            finally
            {
                DisposeMessages(resumeMessages);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReuseActiveTurnForUserShell_WithoutExtraTurnStarted_AndExposeLiveReplay()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "00000000-0000-7000-8000-000000000602";
        const string activeTurnId = "turn_user_shell_active_001";
        const string expectedOutput = "user-shell-active";

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            await MaterializeThreadRolloutAsync(threadStore, threadId);

            using var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);

            await InvokeHandleThreadResumeAsync(server, 1, threadId);
            writer.GetStringBuilder().Clear();

            var threadManager = GetPrivateField<KernelThreadManager>(server, "threadManager");
            Assert.True(threadManager.TryGetThread(threadId, out var runtimeThread));
            Assert.NotNull(runtimeThread);
            runtimeThread!.SetActiveTurn(activeTurnId);

            var runningTurns = GetPrivateField<ConcurrentDictionary<string, CancellationTokenSource>>(server, "runningTurns");
            using var activeTurnCts = new CancellationTokenSource();
            Assert.True(runningTurns.TryAdd(activeTurnId, activeTurnCts));

            try
            {
                await InvokeHandleUserShellRunAsync(server, 2, threadId, $"Write-Output '{expectedOutput}'");

                var runMessages = ParseMessages(writer.ToString());
                try
                {
                    var result = GetResponseResult(runMessages, 2);
                    Assert.True(result.GetProperty("reusedActiveTurn").GetBoolean());
                    Assert.Equal(activeTurnId, result.GetProperty("turnId").GetString());
                    Assert.Equal("completed", result.GetProperty("turnStatus").GetString());
                    Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
                    Assert.Contains(expectedOutput, result.GetProperty("stdout").GetString(), StringComparison.Ordinal);
                    Assert.DoesNotContain(runMessages, static x => IsNotificationMethod(x.RootElement, "turn/started"));

                    var startedItem = Assert.Single(runMessages.Where(static x =>
                        IsCommandExecutionNotification(x.RootElement, "item/started")));
                    AssertCommandExecutionItemHasNoSource(startedItem.RootElement.GetProperty("params").GetProperty("item"), "inProgress");

                    var completedItem = Assert.Single(runMessages.Where(static x =>
                        IsCommandExecutionNotification(x.RootElement, "item/completed")));
                    AssertCommandExecutionItemHasNoSource(completedItem.RootElement.GetProperty("params").GetProperty("item"), "completed");
                }
                finally
                {
                    DisposeMessages(runMessages);
                }

                writer.GetStringBuilder().Clear();
                await InvokeHandleThreadResumeAsync(server, 3, threadId);

                var resumeMessages = ParseMessages(writer.ToString());
                try
                {
                    var result = GetResponseResult(resumeMessages, 3);
                    AssertReplayMessagesContainUserShell(result.GetProperty("messages"), expectedOutput);
                    AssertReplayMessagesContainUserShell(result.GetProperty("thread").GetProperty("messages"), expectedOutput);

                    var turn = Assert.Single(result.GetProperty("thread").GetProperty("turns").EnumerateArray());
                    Assert.Equal("inProgress", turn.GetProperty("status").GetString());
                    var commandItem = Assert.Single(turn.GetProperty("items").EnumerateArray().Where(static item =>
                        string.Equals(item.GetProperty("type").GetString(), "commandExecution", StringComparison.Ordinal)));
                    Assert.False(commandItem.TryGetProperty("source", out _));
                }
                finally
                {
                    DisposeMessages(resumeMessages);
                }
            }
            finally
            {
                if (runningTurns.TryRemove(activeTurnId, out var runningTurn))
                {
                    runningTurn.Dispose();
                }

                _ = runtimeThread.ClearActiveTurn(activeTurnId);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static JsonDocument[] ParseMessages(string output)
        => output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => JsonDocument.Parse(line))
            .ToArray();

    private static void DisposeMessages(IEnumerable<JsonDocument> messages)
    {
        foreach (var message in messages)
        {
            message.Dispose();
        }
    }

    private static JsonElement GetResponseResult(IEnumerable<JsonDocument> messages, long id)
        => messages.Single(message => IsResponseId(message.RootElement, id))
            .RootElement
            .GetProperty("result");

    private static bool IsResponseId(JsonElement json, long id)
        => json.TryGetProperty("id", out var idElement)
           && idElement.ValueKind == JsonValueKind.Number
           && idElement.TryGetInt64(out var parsedId)
           && parsedId == id;

    private static bool IsNotificationMethod(JsonElement json, string method)
        => json.TryGetProperty("method", out var methodElement)
           && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal);

    private static bool IsCommandExecutionNotification(JsonElement json, string method)
        => IsNotificationMethod(json, method)
           && json.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "commandExecution";

    private static void AssertCommandExecutionItemHasNoSource(JsonElement item, string expectedStatus)
    {
        Assert.Equal(expectedStatus, item.GetProperty("status").GetString());
        Assert.Equal("commandExecution", item.GetProperty("type").GetString());
        Assert.False(item.TryGetProperty("source", out _));
    }

    private static void AssertReplayMessagesContainUserShell(JsonElement messages, string expectedOutput)
    {
        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        var replayItem = Assert.Single(messages.EnumerateArray().Where(static item =>
            item.TryGetProperty("role", out var role)
            && string.Equals(role.GetString(), "user", StringComparison.Ordinal)
            && item.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String
            && content.GetString()!.Contains("<user_shell_command>", StringComparison.Ordinal)));
        var replayText = replayItem.GetProperty("content").GetString();
        Assert.Contains(expectedOutput, replayText, StringComparison.Ordinal);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(instance);
        Assert.NotNull(value);
        return Assert.IsType<T>(value);
    }

    private static async Task InvokeHandleUserShellRunAsync(AppHostServer server, int id, string threadId, string command)
    {
        var runtime = GetPrivateField<KernelUserShellAppHostRuntime>(server, "userShellAppHostRuntime");

        using var idDocument = JsonDocument.Parse(id.ToString());
        using var paramsDocument = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                threadId,
                command,
            }));
        await runtime.HandleUserShellRunAsync(
            idDocument.RootElement.Clone(),
            paramsDocument.RootElement.Clone(),
            CancellationToken.None);
    }

    private static async Task InvokeHandleThreadReadAsync(AppHostServer server, int id, string threadId, bool includeTurns)
    {
        var method = typeof(AppHostServer).GetMethod("HandleThreadReadAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using var idDocument = JsonDocument.Parse(id.ToString());
        using var paramsDocument = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                threadId,
                includeTurns,
            }));
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(server, new object?[]
        {
            idDocument.RootElement.Clone(),
            paramsDocument.RootElement.Clone(),
            CancellationToken.None,
        }));
        await task;
    }

    private static async Task InvokeHandleThreadResumeAsync(AppHostServer server, int id, string threadId)
    {
        var method = typeof(AppHostServer).GetMethod("HandleThreadResumeAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using var idDocument = JsonDocument.Parse(id.ToString());
        using var paramsDocument = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                threadId,
            }));
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(server, new object?[]
        {
            idDocument.RootElement.Clone(),
            paramsDocument.RootElement.Clone(),
            CancellationToken.None,
        }));
        await task;
    }

    private static async Task MaterializeThreadRolloutAsync(KernelThreadStore threadStore, string threadId)
    {
        var record = await threadStore.GetThreadAsync(threadId, CancellationToken.None)
                     ?? throw new InvalidOperationException($"线程不存在：{threadId}");
        var snapshot = record.ConfigSnapshot;
        if (snapshot is null)
        {
            snapshot = BuildTestThreadConfigSnapshot(record.Cwd ?? Environment.CurrentDirectory);
            record.ConfigSnapshot = snapshot.DeepClone();
            record = await threadStore.UpsertThreadAsync(record, CancellationToken.None);
        }

        await threadStore.RolloutRecorder.EnsureSessionMetaAsync(
            threadId,
            KernelRolloutStateMapper.ToRolloutThreadRecord(record, snapshot),
            CancellationToken.None);
        await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
    }

    private static KernelThreadConfigSnapshot BuildTestThreadConfigSnapshot(string cwd)
    {
        var session = new KernelThreadSessionState(
            Model: "gpt-5",
            ModelProvider: "openai",
            ServiceTier: null,
            Cwd: cwd,
            ApprovalPolicy: KernelApprovalPolicy.OnRequest,
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly" }),
            SandboxMode: "readOnly");
        return KernelThreadConfigSnapshotFactory.FromSession(session);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                ResetReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 11)
            {
                Thread.Sleep(150 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 11)
            {
                Thread.Sleep(150 * (attempt + 1));
            }
        }

        ResetReadOnlyAttributes(path);
        Directory.Delete(path, recursive: true);
    }

    private static void ResetReadOnlyAttributes(string path)
    {
        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(directory);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(directory, attrs & ~FileAttributes.ReadOnly);
            }
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
        }
    }
}
