using System.Text.Json;
using TianShu.AppHost.State;
using static TianShu.AppHost.Tools.KernelToolJsonHelpers;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelAgentJobsAppHostRuntime
{
    private readonly KernelAgentOrchestrationManager agentOrchestrationManager;
    private readonly Func<JsonElement, int, string, CancellationToken, Task> writeErrorAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;

    public KernelAgentJobsAppHostRuntime(
        KernelAgentOrchestrationManager agentOrchestrationManager,
        Func<JsonElement, int, string, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync)
    {
        this.agentOrchestrationManager = agentOrchestrationManager;
        this.writeErrorAsync = writeErrorAsync;
        this.writeResultAsync = writeResultAsync;
    }

    public async Task HandleAgentJobCreateAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var instruction = ReadString(@params, "instruction");
        if (string.IsNullOrWhiteSpace(instruction))
        {
            await writeErrorAsync(id, -32602, "instruction 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var items = new List<(string? ItemId, string? SourceId, string RowJson)>();
        if (@params.ValueKind == JsonValueKind.Object
            && @params.TryGetProperty("items", out var itemsElement)
            && itemsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsElement.EnumerateArray())
            {
                items.Add((
                    ReadString(item, "itemId"),
                    ReadString(item, "sourceId"),
                    item.GetRawText()));
            }
        }

        var created = await agentOrchestrationManager.CreateJobAsync(
            jobId: ReadString(@params, "jobId"),
            name: ReadString(@params, "name"),
            instruction: instruction!,
            inputHeadersJson: @params.TryGetProperty("inputHeaders", out var inputHeaders) ? inputHeaders.GetRawText() : "[]",
            inputCsvPath: ReadString(@params, "inputCsvPath") ?? string.Empty,
            outputCsvPath: ReadString(@params, "outputCsvPath") ?? string.Empty,
            autoExport: ReadBool(@params, "autoExport") ?? true,
            items: items,
            cancellationToken,
            outputSchemaJson: @params.TryGetProperty("outputSchema", out var outputSchema) ? outputSchema.GetRawText() : null).ConfigureAwait(false);

        await writeResultAsync(id, new
        {
            job = created.Job,
            items = created.Items,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAgentJobDispatchAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var jobId = ReadString(@params, "jobId");
        if (string.IsNullOrWhiteSpace(jobId))
        {
            await writeErrorAsync(id, -32602, "jobId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var threadIds = ReadStringArray(@params, "threadIds");
        if (threadIds.Count == 0)
        {
            await writeErrorAsync(id, -32602, "threadIds 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var assignments = await agentOrchestrationManager.DispatchAsync(jobId!, threadIds, cancellationToken).ConfigureAwait(false);
        await writeResultAsync(id, new { items = assignments }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAgentJobItemReportAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var jobId = ReadString(@params, "jobId");
        var itemId = ReadString(@params, "itemId");
        var status = ReadString(@params, "status");
        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(status))
        {
            await writeErrorAsync(id, -32602, "jobId/itemId/status 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var reported = await agentOrchestrationManager.ReportItemAsync(
            jobId!,
            itemId!,
            status!,
            @params.TryGetProperty("result", out var result) ? result.GetRawText() : null,
            ReadString(@params, "lastError"),
            cancellationToken).ConfigureAwait(false);
        await writeResultAsync(id, new { job = reported.Job, item = reported.Item }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAgentJobReadAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var jobId = ReadString(@params, "jobId");
        if (string.IsNullOrWhiteSpace(jobId))
        {
            await writeErrorAsync(id, -32602, "jobId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var job = await agentOrchestrationManager.ReadJobAsync(jobId!, cancellationToken).ConfigureAwait(false);
        await writeResultAsync(id, new { job = job.Job, items = job.Items }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAgentJobsListAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var statuses = ReadStringArray(@params, "statuses");
        var limit = ReadInt(@params, "limit");
        var jobs = await agentOrchestrationManager.ListJobsAsync(statuses, limit, cancellationToken).ConfigureAwait(false);
        var projected = jobs
            .Select(static job => new
            {
                id = new { value = job.Id },
                name = job.Name,
                status = job.Status,
                instruction = job.Instruction,
            })
            .ToArray();
        await writeResultAsync(id, new { jobs = projected }, cancellationToken).ConfigureAwait(false);
    }
}
