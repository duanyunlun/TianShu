using System.Text.Json;
using System.Text.Json.Nodes;
using TianShu.AppHost.State;
using static TianShu.AppHost.Tools.KernelToolJsonHelpers;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// 收口 `spawn_agents_on_csv` 的 fan-out 编排主循环。
/// Owns the `spawn_agents_on_csv` fan-out orchestration loop.
/// </summary>
internal sealed class KernelSpawnAgentsOnCsvAppHostRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly KernelAgentOrchestrationManager agentOrchestrationManager;
    private readonly KernelSpawnAgentGuardAppHostRuntime spawnAgentGuardAppHostRuntime;
    private readonly Func<string, KernelToolRuntimeRequestContext, KernelSpawnAgentRequest, CancellationToken, Task<KernelSpawnAgentResponse>> spawnAgentAsync;
    private readonly Func<string, CancellationToken, Task<JsonNode?>> closeAgentAsync;
    private readonly Func<string, bool, CancellationToken, Task<JsonNode?>> getAgentStatusNodeAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<string?, CancellationToken, Task<Dictionary<string, object?>>> readConfigAsync;

    public KernelSpawnAgentsOnCsvAppHostRuntime(
        KernelThreadStore threadStore,
        KernelAgentOrchestrationManager agentOrchestrationManager,
        KernelSpawnAgentGuardAppHostRuntime spawnAgentGuardAppHostRuntime,
        Func<string, KernelToolRuntimeRequestContext, KernelSpawnAgentRequest, CancellationToken, Task<KernelSpawnAgentResponse>> spawnAgentAsync,
        Func<string, CancellationToken, Task<JsonNode?>> closeAgentAsync,
        Func<string, bool, CancellationToken, Task<JsonNode?>> getAgentStatusNodeAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<string?, CancellationToken, Task<Dictionary<string, object?>>> readConfigAsync)
    {
        this.threadStore = threadStore;
        this.agentOrchestrationManager = agentOrchestrationManager;
        this.spawnAgentGuardAppHostRuntime = spawnAgentGuardAppHostRuntime;
        this.spawnAgentAsync = spawnAgentAsync;
        this.closeAgentAsync = closeAgentAsync;
        this.getAgentStatusNodeAsync = getAgentStatusNodeAsync;
        this.writeNotificationAsync = writeNotificationAsync;
        this.readConfigAsync = readConfigAsync;
    }

    public async Task<KernelSpawnAgentsOnCsvResponse> ExecuteAsync(
        string parentThreadId,
        string parentTurnId,
        KernelToolRuntimeRequestContext parentTurnContext,
        KernelSpawnAgentsOnCsvRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Instruction))
        {
            throw new InvalidOperationException("instruction must be non-empty");
        }

        var inputPath = KernelSpawnAgentsOnCsvRuntimeHelpers.ResolveAgentJobPath(parentTurnContext.Cwd, request.CsvPath);
        string csvContent;
        try
        {
            csvContent = await File.ReadAllTextAsync(inputPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"failed to read csv input {inputPath}: {ex.Message}", ex);
        }

        var (headers, rows) = KernelSpawnAgentsOnCsvRuntimeHelpers.ParseAgentJobCsv(csvContent);
        if (headers.Count == 0)
        {
            throw new InvalidOperationException("csv input must include a header row");
        }

        KernelSpawnAgentsOnCsvRuntimeHelpers.EnsureUniqueAgentJobHeaders(headers);

        int? idColumnIndex = null;
        if (!string.IsNullOrWhiteSpace(request.IdColumn))
        {
            idColumnIndex = headers.FindIndex(header => string.Equals(header, request.IdColumn, StringComparison.Ordinal));
            if (idColumnIndex < 0)
            {
                throw new InvalidOperationException($"id_column {request.IdColumn} was not found in csv headers");
            }
        }

        var jobId = Guid.NewGuid().ToString();
        var jobSuffix = jobId[..8];
        var outputPath = string.IsNullOrWhiteSpace(request.OutputCsvPath)
            ? KernelSpawnAgentsOnCsvRuntimeHelpers.BuildDefaultAgentJobOutputPath(inputPath, jobSuffix)
            : KernelSpawnAgentsOnCsvRuntimeHelpers.ResolveAgentJobPath(parentTurnContext.Cwd, request.OutputCsvPath!);

        var items = KernelSpawnAgentsOnCsvRuntimeHelpers.BuildAgentJobItems(headers, rows, idColumnIndex);
        var outputSchemaJson = request.OutputSchema?.GetRawText();
        var maxRuntimeSeconds = await ResolveSpawnAgentsOnCsvMaxRuntimeSecondsAsync(
            Normalize(parentTurnContext.Cwd),
            request.MaxRuntimeSeconds,
            cancellationToken).ConfigureAwait(false);
        var created = await agentOrchestrationManager.CreateJobAsync(
            jobId: jobId,
            name: $"agent-job-{jobSuffix}",
            instruction: request.Instruction,
            inputHeadersJson: JsonSerializer.Serialize(headers),
            inputCsvPath: inputPath,
            outputCsvPath: outputPath,
            autoExport: true,
            items: items,
            cancellationToken: cancellationToken,
            outputSchemaJson: outputSchemaJson,
            maxRuntimeSeconds: maxRuntimeSeconds).ConfigureAwait(false);

        int maxConcurrency;
        try
        {
            var guardConfiguration = await spawnAgentGuardAppHostRuntime.ResolveSpawnAgentGuardConfigurationAsync(
                Normalize(parentTurnContext.Cwd),
                cancellationToken).ConfigureAwait(false);
            var childDepth = KernelSpawnAgentGuardAppHostRuntime.GetNextThreadSpawnDepth(parentTurnContext.SessionSource);
            if (childDepth > guardConfiguration.MaxDepth)
            {
                throw new InvalidOperationException("agent depth limit reached; this session cannot spawn more subagents");
            }

            maxConcurrency = KernelSpawnAgentsOnCsvRuntimeHelpers.NormalizeSpawnAgentsOnCsvConcurrency(
                request.MaxConcurrency ?? request.MaxWorkers,
                guardConfiguration.MaxThreads);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MarkAgentJobFailedAsync(created.Job, ex.Message, cancellationToken).ConfigureAwait(false);
            throw;
        }

        var activeWorkers = new Dictionary<string, KernelAgentJobActiveWorker>(StringComparer.Ordinal);
        var progressEmitter = new KernelAgentJobProgressEmitter();
        var cancelRequested = false;
        await EmitAgentJobProgressAsync(
            parentThreadId,
            parentTurnId,
            await agentOrchestrationManager.ReadJobProgressAsync(jobId, cancellationToken).ConfigureAwait(false),
            progressEmitter,
            force: true,
            cancellationToken).ConfigureAwait(false);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var progressed = false;
            var snapshot = await agentOrchestrationManager.ReadJobAsync(jobId, cancellationToken).ConfigureAwait(false);
            var runtimeTimeout = KernelSpawnAgentsOnCsvRuntimeHelpers.ResolveAgentJobRuntimeTimeout(snapshot.Job);
            if (await RecoverRunningAgentJobWorkersAsync(jobId, snapshot.Items, activeWorkers, runtimeTimeout, cancellationToken).ConfigureAwait(false))
            {
                snapshot = await agentOrchestrationManager.ReadJobAsync(jobId, cancellationToken).ConfigureAwait(false);
                runtimeTimeout = KernelSpawnAgentsOnCsvRuntimeHelpers.ResolveAgentJobRuntimeTimeout(snapshot.Job);
                progressed = true;
            }

            if (!cancelRequested && string.Equals(snapshot.Job.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                cancelRequested = true;
                progressed = true;
            }

            var itemsById = snapshot.Items.ToDictionary(item => item.ItemId, StringComparer.Ordinal);

            foreach (var active in activeWorkers.Values.ToArray())
            {
                if (itemsById.TryGetValue(active.ItemId, out var item) && KernelSpawnAgentsOnCsvRuntimeHelpers.IsTerminalAgentJobItemStatus(item.Status))
                {
                    await TryArchiveAgentJobWorkerAsync(active.AgentId, cancellationToken).ConfigureAwait(false);
                    activeWorkers.Remove(active.AgentId);
                    progressed = true;
                }
            }

            foreach (var active in activeWorkers.Values.ToArray())
            {
                if (DateTimeOffset.UtcNow - active.StartedAt < runtimeTimeout)
                {
                    continue;
                }

                _ = await agentOrchestrationManager.ReportItemAsync(
                    jobId,
                    active.ItemId,
                    "failed",
                    resultJson: null,
                    lastError: $"worker exceeded max runtime of {KernelSpawnAgentsOnCsvRuntimeHelpers.FormatAgentJobRuntimeTimeout(runtimeTimeout)}",
                    cancellationToken).ConfigureAwait(false);
                await TryArchiveAgentJobWorkerAsync(active.AgentId, cancellationToken).ConfigureAwait(false);
                activeWorkers.Remove(active.AgentId);
                progressed = true;
            }

            foreach (var active in activeWorkers.Values.ToArray())
            {
                var status = await getAgentStatusNodeAsync(active.AgentId, false, cancellationToken).ConfigureAwait(false);
                if (!KernelToolRuntimeAgentHelpers.IsFinalAgentStatus(status))
                {
                    continue;
                }

                await FinalizeAgentJobWorkerAsync(jobId, active.ItemId, active.AgentId, cancellationToken).ConfigureAwait(false);
                activeWorkers.Remove(active.AgentId);
                progressed = true;
            }

            snapshot = await agentOrchestrationManager.ReadJobAsync(jobId, cancellationToken).ConfigureAwait(false);
            runtimeTimeout = KernelSpawnAgentsOnCsvRuntimeHelpers.ResolveAgentJobRuntimeTimeout(snapshot.Job);
            if (!cancelRequested && string.Equals(snapshot.Job.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                cancelRequested = true;
                progressed = true;
            }

            if (!cancelRequested && activeWorkers.Count < maxConcurrency)
            {
                var availableSlots = maxConcurrency - activeWorkers.Count;
                foreach (var item in snapshot.Items.Where(static item => string.Equals(item.Status, "pending", StringComparison.OrdinalIgnoreCase)).Take(availableSlots))
                {
                    KernelSpawnAgentResponse spawned;
                    try
                    {
                        spawned = await spawnAgentAsync(
                            parentThreadId,
                            parentTurnContext,
                            new KernelSpawnAgentRequest(
                                Message: KernelSpawnAgentsOnCsvRuntimeHelpers.BuildAgentJobWorkerPrompt(snapshot.Job, item),
                                Items: null,
                                AgentType: "worker",
                                ForkContext: false,
                                Model: null,
                                ReasoningEffort: null),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (KernelSpawnAgentsOnCsvRuntimeHelpers.IsSpawnAgentThreadLimitError(ex))
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _ = await agentOrchestrationManager.ReportItemAsync(
                            jobId,
                            item.ItemId,
                            "failed",
                            resultJson: null,
                            lastError: $"failed to spawn worker: {ex.Message}",
                            cancellationToken).ConfigureAwait(false);
                        progressed = true;
                        continue;
                    }

                    var assignment = await agentOrchestrationManager.AssignItemAsync(
                        jobId,
                        item.ItemId,
                        spawned.AgentId,
                        cancellationToken).ConfigureAwait(false);
                    if (assignment is null)
                    {
                        await TryArchiveAgentJobWorkerAsync(spawned.AgentId, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    activeWorkers[spawned.AgentId] = new KernelAgentJobActiveWorker(
                        spawned.AgentId,
                        item.ItemId,
                        DateTimeOffset.UtcNow);
                    progressed = true;
                }
            }

            snapshot = await agentOrchestrationManager.ReadJobAsync(jobId, cancellationToken).ConfigureAwait(false);
            runtimeTimeout = KernelSpawnAgentsOnCsvRuntimeHelpers.ResolveAgentJobRuntimeTimeout(snapshot.Job);
            if (!cancelRequested && string.Equals(snapshot.Job.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                cancelRequested = true;
                progressed = true;
            }

            await EmitAgentJobProgressAsync(
                parentThreadId,
                parentTurnId,
                await agentOrchestrationManager.ReadJobProgressAsync(jobId, cancellationToken).ConfigureAwait(false),
                progressEmitter,
                force: progressed,
                cancellationToken).ConfigureAwait(false);

            var pendingExists = snapshot.Items.Any(static item => string.Equals(item.Status, "pending", StringComparison.OrdinalIgnoreCase));
            if (cancelRequested)
            {
                if (activeWorkers.Count == 0)
                {
                    break;
                }
            }
            else if (!pendingExists && activeWorkers.Count == 0)
            {
                break;
            }

            if (!progressed)
            {
                await Task.Delay(KernelToolRuntimeAgentHelpers.WaitPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        var finalSnapshot = await agentOrchestrationManager.ReadJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        await EmitAgentJobProgressAsync(
            parentThreadId,
            parentTurnId,
            await agentOrchestrationManager.ReadJobProgressAsync(jobId, cancellationToken).ConfigureAwait(false),
            progressEmitter,
            force: true,
            cancellationToken).ConfigureAwait(false);
        try
        {
            await ExportAgentJobCsvAsync(finalSnapshot.Job, finalSnapshot.Items, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await MarkAgentJobFailedAsync(finalSnapshot.Job, $"auto-export failed: {ex.Message}", cancellationToken).ConfigureAwait(false);
            await EmitAgentJobProgressAsync(
                parentThreadId,
                parentTurnId,
                await agentOrchestrationManager.ReadJobProgressAsync(jobId, cancellationToken).ConfigureAwait(false),
                progressEmitter,
                force: true,
                cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"auto-export failed: {ex.Message}", ex);
        }

        finalSnapshot = await agentOrchestrationManager.ReadJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        await EmitAgentJobProgressAsync(
            parentThreadId,
            parentTurnId,
            await agentOrchestrationManager.ReadJobProgressAsync(jobId, cancellationToken).ConfigureAwait(false),
            progressEmitter,
            force: true,
            cancellationToken).ConfigureAwait(false);
        return KernelSpawnAgentsOnCsvRuntimeHelpers.BuildSpawnAgentsOnCsvResponse(finalSnapshot.Job, finalSnapshot.Items);
    }

    public async Task<bool> RecoverRunningAgentJobWorkersAsync(
        string jobId,
        IReadOnlyList<KernelAgentJobItemRecord> items,
        Dictionary<string, KernelAgentJobActiveWorker> activeWorkers,
        TimeSpan runtimeTimeout,
        CancellationToken cancellationToken)
    {
        var progressed = false;
        var runningItems = items
            .Where(static item => string.Equals(item.Status, "running", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var runningAssignedThreads = new HashSet<string>(
            runningItems
                .Select(static item => item.AssignedThreadId)
                .Where(static threadId => !string.IsNullOrWhiteSpace(threadId))
                .Cast<string>(),
            StringComparer.Ordinal);

        foreach (var active in activeWorkers.Values.ToArray())
        {
            if (!runningAssignedThreads.Contains(active.AgentId))
            {
                activeWorkers.Remove(active.AgentId);
            }
        }

        foreach (var item in runningItems)
        {
            if (KernelSpawnAgentsOnCsvRuntimeHelpers.IsAgentJobItemStale(item, runtimeTimeout))
            {
                _ = await agentOrchestrationManager.ReportItemAsync(
                    jobId,
                    item.ItemId,
                    "failed",
                    resultJson: null,
                    lastError: $"worker exceeded max runtime of {KernelSpawnAgentsOnCsvRuntimeHelpers.FormatAgentJobRuntimeTimeout(runtimeTimeout)}",
                    cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(item.AssignedThreadId))
                {
                    await TryArchiveAgentJobWorkerAsync(item.AssignedThreadId, cancellationToken).ConfigureAwait(false);
                    activeWorkers.Remove(item.AssignedThreadId);
                }

                progressed = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.AssignedThreadId))
            {
                _ = await agentOrchestrationManager.ReportItemAsync(
                    jobId,
                    item.ItemId,
                    "failed",
                    resultJson: null,
                    lastError: "running item is missing assigned_thread_id",
                    cancellationToken).ConfigureAwait(false);
                progressed = true;
                continue;
            }

            var workerStatus = await getAgentStatusNodeAsync(item.AssignedThreadId, false, cancellationToken).ConfigureAwait(false);
            if (KernelToolRuntimeAgentHelpers.IsFinalAgentStatus(workerStatus))
            {
                await FinalizeAgentJobWorkerAsync(jobId, item.ItemId, item.AssignedThreadId, cancellationToken).ConfigureAwait(false);
                activeWorkers.Remove(item.AssignedThreadId);
                progressed = true;
                continue;
            }

            activeWorkers[item.AssignedThreadId] = new KernelAgentJobActiveWorker(
                item.AssignedThreadId,
                item.ItemId,
                item.UpdatedAt);
        }

        return progressed;
    }

    private async Task FinalizeAgentJobWorkerAsync(
        string jobId,
        string itemId,
        string agentId,
        CancellationToken cancellationToken)
    {
        var item = await ReadAgentJobItemAsync(jobId, itemId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(item.ResultJson) && !KernelSpawnAgentsOnCsvRuntimeHelpers.IsTerminalAgentJobItemStatus(item.Status))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            item = await ReadAgentJobItemAsync(jobId, itemId, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(item.ResultJson))
        {
            if (!KernelSpawnAgentsOnCsvRuntimeHelpers.IsTerminalAgentJobItemStatus(item.Status))
            {
                _ = await agentOrchestrationManager.ReportItemAsync(
                    jobId,
                    itemId,
                    "completed",
                    item.ResultJson,
                    item.LastError,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        else if (!KernelSpawnAgentsOnCsvRuntimeHelpers.IsTerminalAgentJobItemStatus(item.Status))
        {
            _ = await agentOrchestrationManager.ReportItemAsync(
                jobId,
                itemId,
                "failed",
                resultJson: null,
                lastError: "worker finished without calling report_agent_job_result",
                cancellationToken).ConfigureAwait(false);
        }

        await TryArchiveAgentJobWorkerAsync(agentId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<KernelAgentJobItemRecord> ReadAgentJobItemAsync(string jobId, string itemId, CancellationToken cancellationToken)
        => await agentOrchestrationManager.ReadJobItemAsync(jobId, itemId, cancellationToken).ConfigureAwait(false);

    private async Task TryArchiveAgentJobWorkerAsync(string agentId, CancellationToken cancellationToken)
    {
        try
        {
            _ = await closeAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task EmitAgentJobProgressAsync(
        string parentThreadId,
        string parentTurnId,
        KernelAgentJobProgressSnapshot progress,
        KernelAgentJobProgressEmitter emitter,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!emitter.ShouldEmit(progress, force))
        {
            return;
        }

        var message = $"agent_job_progress:{JsonSerializer.Serialize(new
        {
            job_id = progress.JobId,
            total_items = progress.TotalItems,
            pending_items = progress.PendingItems,
            running_items = progress.RunningItems,
            completed_items = progress.CompletedItems,
            failed_items = progress.FailedItems,
            eta_seconds = progress.EtaSeconds,
        })}";
        var itemId = $"agent_job_progress_{progress.JobId}";
        await writeNotificationAsync("item/agentMessage/delta", new
        {
            threadId = parentThreadId,
            turnId = parentTurnId,
            itemId,
            delta = message,
            item = new
            {
                id = itemId,
                type = "agentMessage",
                delta = message,
            },
        }, cancellationToken).ConfigureAwait(false);
        emitter.MarkEmitted(progress);
    }

    private async Task ExportAgentJobCsvAsync(
        KernelAgentJobRecord job,
        IReadOnlyList<KernelAgentJobItemRecord> items,
        CancellationToken cancellationToken)
    {
        var headers = KernelSpawnAgentsOnCsvRuntimeHelpers.ParseAgentJobHeaders(job.InputHeadersJson);
        var csv = KernelSpawnAgentsOnCsvRuntimeHelpers.RenderAgentJobCsv(headers, items);
        var outputPath = job.OutputCsvPath;
        var parent = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await File.WriteAllTextAsync(outputPath, csv, cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkAgentJobFailedAsync(KernelAgentJobRecord job, string error, CancellationToken cancellationToken)
    {
        await threadStore.StateStore.UpsertAgentJobAsync(
            job with
            {
                Status = "failed",
                LastError = error,
                UpdatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ResolveSpawnAgentsOnCsvMaxRuntimeSecondsAsync(
        string? cwd,
        int? requested,
        CancellationToken cancellationToken)
    {
        if (requested.HasValue)
        {
            return KernelSpawnAgentsOnCsvRuntimeHelpers.NormalizeSpawnAgentsOnCsvMaxRuntimeSeconds(requested);
        }

        var effectiveCwd = Normalize(cwd) ?? Environment.CurrentDirectory;
        var config = await readConfigAsync(effectiveCwd, cancellationToken).ConfigureAwait(false);
        return KernelSpawnAgentsOnCsvRuntimeHelpers.ResolveConfiguredAgentJobMaxRuntimeSeconds(config);
    }
}
