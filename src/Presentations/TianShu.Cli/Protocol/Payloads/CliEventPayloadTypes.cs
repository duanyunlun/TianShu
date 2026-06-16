namespace TianShu.Cli;

/// <summary>
/// CLI 本地的任务进度载荷。
/// CLI-local job progress payload projected from stream structured payload.
/// </summary>
internal sealed record CliJobProgressPayload(
    string JobId,
    int TotalItems,
    int PendingItems,
    int RunningItems,
    int CompletedItems,
    int FailedItems,
    int? EtaSeconds);
