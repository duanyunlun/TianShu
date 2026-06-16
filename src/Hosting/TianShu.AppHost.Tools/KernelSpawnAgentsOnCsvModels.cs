using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// `spawn_agents_on_csv` 工具在宿主工具层与 Kernel 间共享的请求/响应载荷。
/// Shared request/response payloads for the `spawn_agents_on_csv` tool.
/// </summary>
internal sealed record KernelSpawnAgentsOnCsvRequest(
    string CsvPath,
    string Instruction,
    string? IdColumn,
    string? OutputCsvPath,
    int? MaxConcurrency,
    int? MaxWorkers,
    int? MaxRuntimeSeconds,
    JsonElement? OutputSchema);

internal sealed record KernelSpawnAgentsOnCsvFailureSummary(string ItemId, string? SourceId, string LastError);

internal sealed record KernelSpawnAgentsOnCsvResponse(
    string JobId,
    string Status,
    string OutputCsvPath,
    int TotalItems,
    int CompletedItems,
    int FailedItems,
    string? JobError,
    IReadOnlyList<KernelSpawnAgentsOnCsvFailureSummary>? FailedItemErrors);
