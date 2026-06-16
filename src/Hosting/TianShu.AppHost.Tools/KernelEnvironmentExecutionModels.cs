using System.Text.Json;
using TianShu.Contracts.Primitives;

namespace TianShu.AppHost.Tools;

/// <summary>
/// Artifacts 执行请求载荷。
/// Execution request payload for artifacts runs.
/// </summary>
internal sealed record KernelArtifactsExecutionRequest(string Source, int? TimeoutMs);

/// <summary>
/// Artifacts 执行结果载荷。
/// Execution result payload for artifacts runs.
/// </summary>
internal sealed record KernelArtifactsExecutionResult(bool Success, string Output);

/// <summary>
/// Code mode 执行请求载荷。
/// Execution request payload for code mode runs.
/// </summary>
internal sealed record KernelCodeModeExecutionRequest(
    string Code,
    int? YieldTimeMs,
    int? MaxOutputTokens);

/// <summary>
/// Code mode 等待/终止请求载荷。
/// Wait/terminate request payload for code mode runs.
/// </summary>
internal sealed record KernelCodeModeWaitRequest(
    string CellId,
    int YieldTimeMs,
    int? MaxTokens,
    bool Terminate);

/// <summary>
/// Code mode 的标准化执行结果。
/// Normalized execution result produced by code mode.
/// </summary>
internal sealed record KernelCodeModeOperationResult(
    bool Success,
    string Output,
    IReadOnlyList<KernelToolOutputContentItem> ContentItems);

/// <summary>
/// JS REPL 执行请求载荷。
/// Execution request payload for JS REPL runs.
/// </summary>
internal sealed record KernelJsReplExecutionRequest(string Code, int? TimeoutMs);

/// <summary>
/// JS REPL 的标准化执行结果。
/// Normalized execution result produced by the JS REPL runtime.
/// </summary>
internal sealed record KernelJsReplExecutionResult(
    bool Success,
    string Output,
    IReadOnlyList<KernelToolOutputContentItem> ContentItems);

/// <summary>
/// 仅用于 execution envelope 记录的 managed-network 请求快照。
/// Managed-network request snapshot used only for execution envelope recording.
/// </summary>
internal sealed record KernelManagedNetworkExecutionEnvelopeRequest(
    string ThreadId,
    string TurnId,
    string ItemId,
    string Command,
    string Cwd,
    JsonElement? SandboxPolicy,
    string? SandboxMode,
    StructuredValue? ApprovalPolicy,
    IReadOnlyList<string>? SkillAllowedDomains = null,
    IReadOnlyList<string>? SkillDeniedDomains = null,
    TianShu.Contracts.Interactions.InteractionEnvelopeRef? InteractionEnvelope = null);

/// <summary>
/// 仅用于 execution envelope 投影的 managed-network lease 视图。
/// Managed-network lease snapshot used only for execution envelope projection.
/// </summary>
internal sealed record KernelManagedNetworkExecutionLeaseSnapshot(
    bool IsActive,
    string? HttpProxyUrl,
    string? SocksProxyUrl,
    long BlockedRequestTotal);
