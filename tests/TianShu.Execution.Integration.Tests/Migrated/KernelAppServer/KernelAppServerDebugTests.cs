using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerDebugTests
{
[Fact]
    public async Task RunAsync_TianShuDebugClearMemories_ShouldClearStage1Outputs_DisableEnabledThreads_AndRemoveMemoryRoot()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var memoryRoot = Path.Combine(tianShuHome, "data", "memory");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);

            const string threadId = "thread_debug_clear_memory_001";
            const string disabledThreadId = "thread_debug_clear_memory_002";
            const string turnId = "turn_debug_clear_memory_001";
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            var disabledThread = await setupStore.CreateThreadAsync(disabledThreadId, root, CancellationToken.None);
            disabledThread.MemoryMode = "disabled";
            _ = await setupStore.UpsertThreadAsync(disabledThread, CancellationToken.None);
            await setupStore.StateStore.AppendTurnLogAsync(
                threadId,
                turnId,
                "assistant",
                "completed",
                "keep-turn-log",
                new { keep = true },
                CancellationToken.None);
            await setupStore.StateStore.UpsertRolloutAsync(
                $"{threadId}/{turnId}",
                threadId,
                turnId,
                "assistant",
                Path.Combine(root, "rollout.jsonl"),
                "keep-rollout",
                new { keep = true },
                CancellationToken.None);
            await setupStore.StateStore.UpsertMemoryAsync(
                new KernelThreadMemoryRecord(
                    ThreadId: threadId,
                    SourceUpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                    RawMemory: "raw-memory",
                    RolloutSummary: "rollout-summary",
                    GeneratedAt: DateTimeOffset.UtcNow,
                    UsageCount: 0,
                    LastUsageAt: null),
                CancellationToken.None);

            Directory.CreateDirectory(memoryRoot);
            await File.WriteAllTextAsync(Path.Combine(memoryRoot, "memory.txt"), "keep nothing");

            var input = """{"jsonrpc":"2.0","id":1,"method":"tianshu/debug/clear-memories","params":{}}""";
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));

            await server.RunAsync(CancellationToken.None);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var result = messages.Single(x => IsResponseId(x.RootElement, 1)).RootElement.GetProperty("result");

                Assert.Equal(Path.GetFullPath(Path.Combine(root, "state.db")), result.GetProperty("stateDbPath").GetString());
                Assert.Equal(1, result.GetProperty("clearedStage1OutputCount").GetInt64());
                Assert.Equal(1, result.GetProperty("disabledThreadCount").GetInt64());
                Assert.Equal(Path.GetFullPath(memoryRoot), result.GetProperty("memoryRootPath").GetString());
                Assert.True(result.GetProperty("removedMemoryRoot").GetBoolean());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }

            Assert.Null(await setupStore.StateStore.GetMemoryAsync(threadId, CancellationToken.None));
            var verificationStore = new KernelThreadStore(storePath);
            await verificationStore.InitializeAsync(CancellationToken.None);
            var enabledThread = await verificationStore.GetThreadAsync(threadId, CancellationToken.None);
            var stillDisabledThread = await verificationStore.GetThreadAsync(disabledThreadId, CancellationToken.None);
            Assert.NotNull(enabledThread);
            Assert.NotNull(stillDisabledThread);
            Assert.Equal("disabled", enabledThread!.MemoryMode);
            Assert.Equal("disabled", stillDisabledThread!.MemoryMode);
            Assert.Single(await verificationStore.StateStore.ListTurnLogsAsync(threadId, CancellationToken.None));
            Assert.Single(await verificationStore.StateStore.ListRolloutsAsync(threadId, CancellationToken.None));
            Assert.False(Directory.Exists(memoryRoot));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    private static bool IsResponseId(JsonElement json, int id)
    {
        if (!json.TryGetProperty("id", out var idElement))
        {
            return false;
        }

        return idElement.ValueKind switch
        {
            JsonValueKind.Number => idElement.GetInt32() == id,
            JsonValueKind.String => string.Equals(idElement.GetString(), id.ToString(), StringComparison.Ordinal),
            _ => false,
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-debug-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
