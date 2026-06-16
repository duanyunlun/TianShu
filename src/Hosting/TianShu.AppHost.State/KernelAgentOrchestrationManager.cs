namespace TianShu.AppHost.State;

internal sealed record KernelAgentJobProgressSnapshot(
    string JobId,
    int TotalItems,
    int PendingItems,
    int RunningItems,
    int CompletedItems,
    int FailedItems,
    int? EtaSeconds);

internal sealed class KernelAgentOrchestrationManager
{
    private readonly KernelStateSqliteStore stateStore;

    public KernelAgentOrchestrationManager(KernelStateSqliteStore stateStore)
    {
        this.stateStore = stateStore;
    }

    public async Task<(KernelAgentJobRecord Job, IReadOnlyList<KernelAgentJobItemRecord> Items)> CreateJobAsync(
        string? jobId,
        string? name,
        string instruction,
        string inputHeadersJson,
        string inputCsvPath,
        string outputCsvPath,
        bool autoExport,
        IReadOnlyList<(string? ItemId, string? SourceId, string RowJson)> items,
        CancellationToken cancellationToken,
        string? outputSchemaJson = null,
        int? maxRuntimeSeconds = null)
    {
        var now = DateTimeOffset.UtcNow;
        var resolvedJobId = string.IsNullOrWhiteSpace(jobId) ? $"job_{Guid.NewGuid():N}" : jobId!;
        var job = new KernelAgentJobRecord(
            Id: resolvedJobId,
            Name: string.IsNullOrWhiteSpace(name) ? resolvedJobId : name!,
            Status: items.Count == 0 ? "completed" : "pending",
            Instruction: instruction,
            OutputSchemaJson: outputSchemaJson,
            InputHeadersJson: inputHeadersJson,
            InputCsvPath: inputCsvPath,
            OutputCsvPath: outputCsvPath,
            AutoExport: autoExport,
            MaxRuntimeSeconds: maxRuntimeSeconds,
            CreatedAt: now,
            UpdatedAt: now,
            StartedAt: null,
            CompletedAt: items.Count == 0 ? now : null,
            LastError: null);
        await stateStore.UpsertAgentJobAsync(job, cancellationToken).ConfigureAwait(false);

        var records = new List<KernelAgentJobItemRecord>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var record = new KernelAgentJobItemRecord(
                JobId: resolvedJobId,
                ItemId: string.IsNullOrWhiteSpace(item.ItemId) ? $"item_{i + 1}" : item.ItemId!,
                RowIndex: i,
                SourceId: item.SourceId,
                RowJson: item.RowJson,
                Status: "pending",
                AssignedThreadId: null,
                AttemptCount: 0,
                ResultJson: null,
                LastError: null,
                CreatedAt: now,
                UpdatedAt: now,
                CompletedAt: null,
                ReportedAt: null);
            await stateStore.UpsertAgentJobItemAsync(record, cancellationToken).ConfigureAwait(false);
            records.Add(record);
        }

        return (job, records);
    }

    public async Task<(KernelAgentJobRecord Job, IReadOnlyList<KernelAgentJobItemRecord> Items)> ReadJobAsync(string jobId, CancellationToken cancellationToken)
    {
        var job = await stateStore.GetAgentJobAsync(jobId, cancellationToken).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"Agent job not found: {jobId}");
        var items = await stateStore.ListAgentJobItemsAsync(jobId, cancellationToken).ConfigureAwait(false);
        return (job, items);
    }

    public async Task<IReadOnlyList<KernelAgentJobRecord>> ListJobsAsync(
        IReadOnlyList<string>? statuses,
        int? limit,
        CancellationToken cancellationToken)
    {
        var normalizedStatuses = statuses?
            .Select(static status => status?.Trim())
            .Where(static status => !string.IsNullOrWhiteSpace(status))
            .Select(static status => status!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalizedStatuses is null || normalizedStatuses.Count == 0)
        {
            normalizedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "pending",
                "running",
            };
        }

        var take = limit is > 0 ? limit.Value : 50;
        var jobs = await stateStore.ListAgentJobsAsync(cancellationToken).ConfigureAwait(false);
        return jobs
            .Where(job => normalizedStatuses.Contains(job.Status))
            .Take(take)
            .ToArray();
    }

    public async Task<KernelAgentJobItemRecord> ReadJobItemAsync(string jobId, string itemId, CancellationToken cancellationToken)
    {
        return await stateStore.GetAgentJobItemAsync(jobId, itemId, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException($"Agent job item not found: {jobId}/{itemId}");
    }

    public async Task<IReadOnlyList<KernelAgentJobItemRecord>> ReadJobItemsByStatusAsync(
        string jobId,
        string status,
        CancellationToken cancellationToken)
    {
        return await stateStore.ListAgentJobItemsByStatusAsync(jobId, status, cancellationToken).ConfigureAwait(false);
    }

    public async Task<KernelAgentJobProgressSnapshot> ReadJobProgressAsync(string jobId, CancellationToken cancellationToken)
    {
        var job = await stateStore.GetAgentJobAsync(jobId, cancellationToken).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"Agent job not found: {jobId}");
        var progress = await stateStore.GetAgentJobProgressAsync(jobId, cancellationToken).ConfigureAwait(false);
        return BuildProgressSnapshot(job, progress, DateTimeOffset.UtcNow);
    }

    public async Task<KernelAgentJobItemRecord?> AssignItemAsync(
        string jobId,
        string itemId,
        string assignedThreadId,
        CancellationToken cancellationToken)
    {
        var job = await stateStore.GetAgentJobAsync(jobId, cancellationToken).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"Agent job not found: {jobId}");
        var items = await stateStore.ListAgentJobItemsAsync(jobId, cancellationToken).ConfigureAwait(false);
        var target = items.SingleOrDefault(item => string.Equals(item.ItemId, itemId, StringComparison.Ordinal))
                     ?? throw new InvalidOperationException($"Agent job item not found: {jobId}/{itemId}");
        if (!string.Equals(target.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var assignment = target with
        {
            Status = "running",
            AssignedThreadId = assignedThreadId,
            AttemptCount = target.AttemptCount + 1,
            UpdatedAt = now,
            LastError = null,
        };
        await stateStore.UpsertAgentJobItemAsync(assignment, cancellationToken).ConfigureAwait(false);

        var nextJob = job with
        {
            Status = "running",
            StartedAt = job.StartedAt ?? now,
            UpdatedAt = now,
            CompletedAt = null,
            LastError = null,
        };
        await stateStore.UpsertAgentJobAsync(nextJob, cancellationToken).ConfigureAwait(false);
        return assignment;
    }

    public async Task<bool> CancelJobAsync(string jobId, string? reason, CancellationToken cancellationToken)
    {
        var job = await stateStore.GetAgentJobAsync(jobId, cancellationToken).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"Agent job not found: {jobId}");
        if (IsTerminalJobStatus(job.Status))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var nextJob = job with
        {
            Status = "cancelled",
            UpdatedAt = now,
            CompletedAt = job.CompletedAt ?? now,
            LastError = string.IsNullOrWhiteSpace(reason) ? job.LastError : reason,
        };
        await stateStore.UpsertAgentJobAsync(nextJob, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> IsJobCancelledAsync(string jobId, CancellationToken cancellationToken)
    {
        var job = await stateStore.GetAgentJobAsync(jobId, cancellationToken).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"Agent job not found: {jobId}");
        return string.Equals(job.Status, "cancelled", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<KernelAgentJobItemRecord>> DispatchAsync(
        string jobId,
        IReadOnlyList<string> threadIds,
        CancellationToken cancellationToken)
    {
        if (threadIds.Count == 0)
        {
            return Array.Empty<KernelAgentJobItemRecord>();
        }

        var items = await ReadJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (IsTerminalJobStatus(items.Job.Status))
        {
            return Array.Empty<KernelAgentJobItemRecord>();
        }

        var pending = items.Items.Where(static item => string.Equals(item.Status, "pending", StringComparison.OrdinalIgnoreCase)).ToArray();
        var updated = new List<KernelAgentJobItemRecord>(pending.Length);
        for (var i = 0; i < pending.Length; i++)
        {
            var assignment = await AssignItemAsync(jobId, pending[i].ItemId, threadIds[i % threadIds.Count], cancellationToken).ConfigureAwait(false);
            if (assignment is not null)
            {
                updated.Add(assignment);
            }
        }

        return updated;
    }

    public async Task<(KernelAgentJobRecord Job, KernelAgentJobItemRecord Item)> ReportItemAsync(
        string jobId,
        string itemId,
        string status,
        string? resultJson,
        string? lastError,
        CancellationToken cancellationToken)
    {
        var job = await stateStore.GetAgentJobAsync(jobId, cancellationToken).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"Agent job not found: {jobId}");
        var target = await ReadJobItemAsync(jobId, itemId, cancellationToken).ConfigureAwait(false);
        if (IsTerminalItemStatus(target.Status))
        {
            return (job, target);
        }

        var now = DateTimeOffset.UtcNow;
        var updatedItem = target with
        {
            Status = status,
            ResultJson = resultJson ?? target.ResultJson,
            LastError = lastError,
            UpdatedAt = now,
            CompletedAt = status.Equals("completed", StringComparison.OrdinalIgnoreCase) || status.Equals("failed", StringComparison.OrdinalIgnoreCase) ? now : target.CompletedAt,
            ReportedAt = resultJson is not null ? now : target.ReportedAt,
            AssignedThreadId = IsTerminalItemStatus(status) ? null : target.AssignedThreadId,
        };
        await stateStore.UpsertAgentJobItemAsync(updatedItem, cancellationToken).ConfigureAwait(false);

        var refreshedItems = await stateStore.ListAgentJobItemsAsync(jobId, cancellationToken).ConfigureAwait(false);
        var nextJobStatus = ResolveJobStatus(job, refreshedItems);
        var nextJob = job with
        {
            Status = nextJobStatus,
            UpdatedAt = now,
            StartedAt = nextJobStatus == "running" ? job.StartedAt ?? now : job.StartedAt,
            CompletedAt = IsTerminalJobStatus(nextJobStatus) ? job.CompletedAt ?? now : null,
            LastError = ResolveJobLastError(job, refreshedItems, nextJobStatus, lastError),
        };
        await stateStore.UpsertAgentJobAsync(nextJob, cancellationToken).ConfigureAwait(false);
        return (nextJob, updatedItem);
    }

    public async Task<(KernelAgentJobRecord Job, KernelAgentJobItemRecord Item)?> RecordItemResultAsync(
        string jobId,
        string itemId,
        string reportingThreadId,
        string resultJson,
        CancellationToken cancellationToken)
    {
        var job = await stateStore.GetAgentJobAsync(jobId, cancellationToken).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"Agent job not found: {jobId}");
        var target = await ReadJobItemAsync(jobId, itemId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(target.Status, "running", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(target.AssignedThreadId, reportingThreadId, StringComparison.Ordinal))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var updatedItem = target with
        {
            ResultJson = resultJson,
            LastError = null,
            UpdatedAt = now,
            ReportedAt = now,
        };
        await stateStore.UpsertAgentJobItemAsync(updatedItem, cancellationToken).ConfigureAwait(false);
        return (job, updatedItem);
    }

    private static bool IsTerminalJobStatus(string? status)
        => string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminalItemStatus(string? status)
        => string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);

    private static string ResolveJobStatus(KernelAgentJobRecord job, IReadOnlyList<KernelAgentJobItemRecord> items)
    {
        if (string.Equals(job.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return "cancelled";
        }

        if (!items.All(static item => IsTerminalItemStatus(item.Status)))
        {
            return "running";
        }

        return "completed";
    }

    private static string? ResolveJobLastError(
        KernelAgentJobRecord job,
        IReadOnlyList<KernelAgentJobItemRecord> items,
        string nextJobStatus,
        string? itemLastError)
    {
        if (string.Equals(nextJobStatus, "running", StringComparison.OrdinalIgnoreCase)
            || string.Equals(nextJobStatus, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(nextJobStatus, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return job.LastError ?? itemLastError;
        }

        if (!string.IsNullOrWhiteSpace(itemLastError))
        {
            return itemLastError;
        }

        return items
            .FirstOrDefault(static item => string.Equals(item.Status, "failed", StringComparison.OrdinalIgnoreCase)
                                           && !string.IsNullOrWhiteSpace(item.LastError))
            ?.LastError
            ?? job.LastError;
    }

    private static KernelAgentJobProgressSnapshot BuildProgressSnapshot(
        KernelAgentJobRecord job,
        KernelAgentJobProgressRecord progress,
        DateTimeOffset now)
    {
        var processed = progress.CompletedItems + progress.FailedItems;
        var etaSeconds = default(int?);
        var startedAt = job.StartedAt ?? job.CreatedAt;
        if (processed > 0)
        {
            var elapsedSeconds = Math.Max(0d, (now - startedAt).TotalSeconds);
            if (elapsedSeconds > 0d)
            {
                var remaining = Math.Max(0, progress.TotalItems - processed);
                var rate = processed / elapsedSeconds;
                if (rate > 0d)
                {
                    etaSeconds = Math.Max(0, (int)Math.Round(remaining / rate));
                }
            }
        }

        return new KernelAgentJobProgressSnapshot(
            JobId: progress.JobId,
            TotalItems: progress.TotalItems,
            PendingItems: progress.PendingItems,
            RunningItems: progress.RunningItems,
            CompletedItems: progress.CompletedItems,
            FailedItems: progress.FailedItems,
            EtaSeconds: etaSeconds);
    }
}
