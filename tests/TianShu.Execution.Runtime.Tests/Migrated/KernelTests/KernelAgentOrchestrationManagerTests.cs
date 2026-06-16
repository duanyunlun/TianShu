using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelAgentOrchestrationManagerTests
{
    [Fact]
    public async Task KernelAgentOrchestrationManager_ShouldCreateDispatchAndReportJob()
    {
        using var scope = new TestDirectoryScope();
        var store = new KernelStateSqliteStore(Path.Combine(scope.Root, "state.db"));
        await store.InitializeAsync(CancellationToken.None);
        var manager = new KernelAgentOrchestrationManager(store);

        var created = await manager.CreateJobAsync(
            jobId: "job_dispatch_001",
            name: "dispatch-job",
            instruction: "process rows",
            inputHeadersJson: "[\"id\"]",
            inputCsvPath: "input.csv",
            outputCsvPath: "output.csv",
            autoExport: true,
            items:
            [
                ("item_001", "row-1", "{\"id\":1}"),
                ("item_002", "row-2", "{\"id\":2}"),
            ],
            CancellationToken.None);

        Assert.Equal("pending", created.Job.Status);
        Assert.Equal(2, created.Items.Count);

        var dispatched = await manager.DispatchAsync(created.Job.Id, ["thread_agent_a", "thread_agent_b"], CancellationToken.None);
        Assert.Equal(2, dispatched.Count);
        Assert.All(dispatched, static item => Assert.Equal("running", item.Status));

        var recorded = await manager.RecordItemResultAsync(created.Job.Id, "item_001", "thread_agent_a", "{\"ok\":true}", CancellationToken.None);
        Assert.NotNull(recorded);
        Assert.Equal("running", recorded!.Value.Item.Status);
        Assert.Equal("thread_agent_a", recorded.Value.Item.AssignedThreadId);

        var firstReport = await manager.ReportItemAsync(created.Job.Id, "item_001", "completed", null, null, CancellationToken.None);
        Assert.Equal("completed", firstReport.Item.Status);
        Assert.Equal("running", firstReport.Job.Status);
        Assert.Null(firstReport.Item.AssignedThreadId);

        var secondReport = await manager.ReportItemAsync(created.Job.Id, "item_002", "completed", "{\"ok\":true}", null, CancellationToken.None);
        Assert.Equal("completed", secondReport.Item.Status);
        Assert.Equal("completed", secondReport.Job.Status);

        var loaded = await manager.ReadJobAsync(created.Job.Id, CancellationToken.None);
        Assert.Equal("completed", loaded.Job.Status);
        Assert.Equal(2, loaded.Items.Count);
    }

    [Fact]
    public async Task KernelAgentOrchestrationManager_ShouldCancelJobWithoutFailingPendingItems()
    {
        using var scope = new TestDirectoryScope();
        var store = new KernelStateSqliteStore(Path.Combine(scope.Root, "state.db"));
        await store.InitializeAsync(CancellationToken.None);
        var manager = new KernelAgentOrchestrationManager(store);

        var created = await manager.CreateJobAsync(
            jobId: "job_cancel_001",
            name: "cancel-job",
            instruction: "process rows",
            inputHeadersJson: "[\"id\"]",
            inputCsvPath: "input.csv",
            outputCsvPath: "output.csv",
            autoExport: true,
            items:
            [
                ("item_001", "row-1", "{\"id\":1}"),
                ("item_002", "row-2", "{\"id\":2}"),
            ],
            CancellationToken.None);

        var assignment = await manager.AssignItemAsync(created.Job.Id, "item_001", "thread_agent_a", CancellationToken.None);
        Assert.NotNull(assignment);
        var cancelled = await manager.CancelJobAsync(created.Job.Id, "cancelled by worker request", CancellationToken.None);

        Assert.True(cancelled);

        var recorded = await manager.RecordItemResultAsync(created.Job.Id, "item_001", "thread_agent_a", "{\"ok\":true}", CancellationToken.None);
        Assert.NotNull(recorded);

        var report = await manager.ReportItemAsync(created.Job.Id, "item_001", "completed", null, null, CancellationToken.None);
        Assert.Equal("completed", report.Item.Status);
        Assert.Equal("cancelled", report.Job.Status);

        var loaded = await manager.ReadJobAsync(created.Job.Id, CancellationToken.None);
        Assert.Equal("cancelled", loaded.Job.Status);
        Assert.Equal("cancelled by worker request", loaded.Job.LastError);
        Assert.Equal("completed", loaded.Items.Single(static item => item.ItemId == "item_001").Status);
        Assert.Equal("pending", loaded.Items.Single(static item => item.ItemId == "item_002").Status);
    }

    [Fact]
    public async Task KernelAgentOrchestrationManager_ShouldCompleteJobWhenItemsFinishWithFailures()
    {
        using var scope = new TestDirectoryScope();
        var store = new KernelStateSqliteStore(Path.Combine(scope.Root, "state.db"));
        await store.InitializeAsync(CancellationToken.None);
        var manager = new KernelAgentOrchestrationManager(store);

        var created = await manager.CreateJobAsync(
            jobId: "job_partial_failure_001",
            name: "partial-failure-job",
            instruction: "process rows",
            inputHeadersJson: "[\"id\"]",
            inputCsvPath: "input.csv",
            outputCsvPath: "output.csv",
            autoExport: true,
            items:
            [
                ("item_001", "row-1", "{\"id\":1}"),
                ("item_002", "row-2", "{\"id\":2}"),
            ],
            CancellationToken.None);

        _ = await manager.DispatchAsync(created.Job.Id, ["thread_agent_a", "thread_agent_b"], CancellationToken.None);
        _ = await manager.RecordItemResultAsync(created.Job.Id, "item_001", "thread_agent_a", "{\"ok\":true}", CancellationToken.None);
        _ = await manager.ReportItemAsync(created.Job.Id, "item_001", "completed", null, null, CancellationToken.None);
        var finalReport = await manager.ReportItemAsync(
            created.Job.Id,
            "item_002",
            "failed",
            null,
            "worker finished without calling report_agent_job_result",
            CancellationToken.None);

        Assert.Equal("failed", finalReport.Item.Status);
        Assert.Equal("completed", finalReport.Job.Status);
        Assert.Null(finalReport.Job.LastError);
    }

    [Fact]
    public async Task KernelAgentOrchestrationManager_ShouldReadProgressSnapshotWithEta()
    {
        using var scope = new TestDirectoryScope();
        var store = new KernelStateSqliteStore(Path.Combine(scope.Root, "state.db"));
        await store.InitializeAsync(CancellationToken.None);
        var manager = new KernelAgentOrchestrationManager(store);

        var created = await manager.CreateJobAsync(
            jobId: "job_progress_001",
            name: "progress-job",
            instruction: "process rows",
            inputHeadersJson: "[\"id\"]",
            inputCsvPath: "input.csv",
            outputCsvPath: "output.csv",
            autoExport: true,
            items:
            [
                ("item_001", "row-1", "{\"id\":1}"),
                ("item_002", "row-2", "{\"id\":2}"),
                ("item_003", "row-3", "{\"id\":3}"),
            ],
            cancellationToken: CancellationToken.None,
            maxRuntimeSeconds: 180);

        _ = await manager.DispatchAsync(created.Job.Id, ["thread_agent_a"], CancellationToken.None);
        _ = await manager.RecordItemResultAsync(created.Job.Id, "item_001", "thread_agent_a", "{\"ok\":true}", CancellationToken.None);
        _ = await manager.ReportItemAsync(created.Job.Id, "item_001", "completed", null, null, CancellationToken.None);

        var progress = await manager.ReadJobProgressAsync(created.Job.Id, CancellationToken.None);

        Assert.Equal(3, progress.TotalItems);
        Assert.Equal(2, progress.PendingItems + progress.RunningItems);
        Assert.Equal(1, progress.CompletedItems);
        Assert.True(progress.EtaSeconds is null or >= 0);
    }

    private sealed class TestDirectoryScope : IDisposable
    {
        public TestDirectoryScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "tianshu-kernel-agent-tests", Guid.NewGuid().ToString("N"));
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
