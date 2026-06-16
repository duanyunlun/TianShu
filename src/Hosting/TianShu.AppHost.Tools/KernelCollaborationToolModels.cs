using System.Text.Json.Nodes;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 协作类工具在宿主工具层与 Kernel 之间传递的轻量载荷。
/// Lightweight collaboration tool payloads shared between the host tool layer and Kernel.
/// </summary>
internal sealed record KernelPlanStep(string Step, string Status);

internal sealed record KernelPlanUpdateRequest(string? Explanation, IReadOnlyList<KernelPlanStep> Plan);

internal sealed record KernelCollabInputItem(
    string? Type,
    string? Text,
    string? Name,
    string? Path,
    string? ImageUrl);

internal sealed record KernelSpawnAgentRequest(
    string? Message,
    IReadOnlyList<KernelCollabInputItem>? Items,
    string? AgentType,
    bool ForkContext,
    string? Model,
    string? ReasoningEffort,
    string? ParentCallId = null);

internal sealed record KernelSpawnAgentResponse(string AgentId, string? Nickname);

internal sealed record KernelSendInputRequest(
    string Id,
    string? Message,
    IReadOnlyList<KernelCollabInputItem>? Items,
    bool Interrupt);

internal sealed record KernelSendInputResponse(string SubmissionId);

internal sealed record KernelWaitAgentsResponse(
    IReadOnlyDictionary<string, JsonNode?> Status,
    bool TimedOut);
