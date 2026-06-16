namespace TianShu.Execution.Runtime.Providers;

/// <summary>
/// Provider 事件投影上下文，描述 southbound typed 事件投影为 runtime 事件时所需的外围语义。
/// Projection context that carries the surrounding runtime semantics required to map southbound provider events.
/// </summary>
internal sealed record ProviderEventProjectionContext(
    string? ThreadId = null,
    string? TurnId = null,
    string? ItemId = null,
    string? CallId = null,
    string? ToolName = null,
    string? ServerName = null,
    string? Status = null,
    string? Phase = null,
    string? SourceMethod = null,
    string? RawJson = null,
    long? SummaryIndex = null,
    long? ContentIndex = null);
