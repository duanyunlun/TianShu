using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelStateSqliteStorePersistenceTests
{
    [Fact]
    public async Task KernelStateSqliteStore_ShouldPersistMemoryRecordsAndUsage()
    {
        using var scope = new TestDirectoryScope();
        var store = new KernelStateSqliteStore(Path.Combine(scope.Root, "state.db"));
        await store.InitializeAsync(CancellationToken.None);

        await store.UpsertThreadAsync(
            KernelStoredThreadStateTestHelper.FromThread(CreateThread("thread_memory_001")),
            CancellationToken.None);
        await store.UpsertMemoryAsync(
            new KernelThreadMemoryRecord(
                ThreadId: "thread_memory_001",
                SourceUpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
                RawMemory: "raw-memory",
                RolloutSummary: "rollout-summary",
                GeneratedAt: DateTimeOffset.UtcNow,
                UsageCount: 0,
                LastUsageAt: null),
            CancellationToken.None);

        await store.MarkMemoryUsedAsync("thread_memory_001", CancellationToken.None);

        var memory = await store.GetMemoryAsync("thread_memory_001", CancellationToken.None);

        Assert.NotNull(memory);
        Assert.Equal("raw-memory", memory!.RawMemory);
        Assert.Equal("rollout-summary", memory.RolloutSummary);
        Assert.Equal(1, memory.UsageCount);
        Assert.NotNull(memory.LastUsageAt);
    }

    [Fact]
    public async Task KernelStateSqliteStore_ShouldPersistAgentJobsAndItems()
    {
        using var scope = new TestDirectoryScope();
        var store = new KernelStateSqliteStore(Path.Combine(scope.Root, "state.db"));
        await store.InitializeAsync(CancellationToken.None);

        var job = new KernelAgentJobRecord(
            Id: "job_001",
            Name: "csv-job",
            Status: "running",
            Instruction: "process rows",
            OutputSchemaJson: null,
            InputHeadersJson: "[\"id\",\"name\"]",
            InputCsvPath: "input.csv",
            OutputCsvPath: "output.csv",
            AutoExport: true,
            MaxRuntimeSeconds: 120,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            LastError: null);
        await store.UpsertAgentJobAsync(job, CancellationToken.None);

        await store.UpsertAgentJobItemAsync(
            new KernelAgentJobItemRecord(
                JobId: job.Id,
                ItemId: "item_001",
                RowIndex: 0,
                SourceId: "row-1",
                RowJson: "{\"id\":1}",
                Status: "completed",
                AssignedThreadId: "thread_agent_001",
                AttemptCount: 1,
                ResultJson: "{\"ok\":true}",
                LastError: null,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                CompletedAt: DateTimeOffset.UtcNow,
                ReportedAt: DateTimeOffset.UtcNow),
            CancellationToken.None);

        var loadedJob = await store.GetAgentJobAsync(job.Id, CancellationToken.None);
        var loadedItem = await store.GetAgentJobItemAsync(job.Id, "item_001", CancellationToken.None);
        var completedItems = await store.ListAgentJobItemsByStatusAsync(job.Id, "completed", CancellationToken.None);
        var progress = await store.GetAgentJobProgressAsync(job.Id, CancellationToken.None);
        var items = await store.ListAgentJobItemsAsync(job.Id, CancellationToken.None);

        Assert.NotNull(loadedJob);
        Assert.Equal("running", loadedJob!.Status);
        Assert.Equal(120, loadedJob.MaxRuntimeSeconds);
        Assert.NotNull(loadedItem);
        var item = Assert.Single(items);
        Assert.Equal("completed", item.Status);
        Assert.Equal("thread_agent_001", item.AssignedThreadId);
        Assert.Single(completedItems);
        Assert.Equal(1, progress.TotalItems);
        Assert.Equal(1, progress.CompletedItems);
        Assert.Equal(0, progress.PendingItems);
    }

    [Fact]
    public async Task KernelStateSqliteStore_ClearMemoriesAsync_ShouldOnlyDeleteStage1Outputs()
    {
        using var scope = new TestDirectoryScope();
        var store = new KernelStateSqliteStore(Path.Combine(scope.Root, "state.db"));
        await store.InitializeAsync(CancellationToken.None);

        var threadId = "thread_memory_clear_001";
        await store.UpsertThreadAsync(
            KernelStoredThreadStateTestHelper.FromThread(CreateThread(threadId)),
            CancellationToken.None);
        await store.AppendTurnLogAsync(
            threadId,
            "turn_memory_clear_001",
            "assistant",
            "completed",
            "keep-turn-log",
            new { keep = true },
            CancellationToken.None);
        await store.UpsertRolloutAsync(
            $"{threadId}/turn_memory_clear_001",
            threadId,
            "turn_memory_clear_001",
            "assistant",
            Path.Combine(scope.Root, "rollout.jsonl"),
            "keep-rollout",
            new { keep = true },
            CancellationToken.None);
        await store.UpsertMemoryAsync(
            new KernelThreadMemoryRecord(
                ThreadId: threadId,
                SourceUpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                RawMemory: "raw-memory",
                RolloutSummary: "rollout-summary",
                GeneratedAt: DateTimeOffset.UtcNow,
                UsageCount: 0,
                LastUsageAt: null),
            CancellationToken.None);

        var clearedStage1OutputCount = await store.ClearMemoriesAsync(CancellationToken.None);

        Assert.Equal(1, clearedStage1OutputCount);
        var mirrored = await store.GetThreadAsync(threadId, CancellationToken.None);
        Assert.NotNull(mirrored);
        var mirroredPayload = KernelStoredThreadStateTestHelper.DeserializePayload(mirrored!);
        Assert.Equal(threadId, mirrored.ThreadId);
        Assert.Equal(threadId, mirroredPayload.Id);
        Assert.Null(await store.GetMemoryAsync(threadId, CancellationToken.None));
        Assert.Single(await store.ListTurnLogsAsync(threadId, CancellationToken.None));
        Assert.Single(await store.ListRolloutsAsync(threadId, CancellationToken.None));
    }

    private static KernelThreadRecord CreateThread(string id)
    {
        return new KernelThreadRecord
        {
            Id = id,
            Cwd = "D:/Repo",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            StatusType = "idle",
            ActiveFlags = [],
            Turns = [],
        };
    }

    private sealed class TestDirectoryScope : IDisposable
    {
        public TestDirectoryScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "tianshu-kernel-state-store-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
