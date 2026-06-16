using System.Text.Json;
using TianShu.Contracts.Interactions;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelManagedNetworkExecutionRequest(
    string ThreadId,
    string TurnId,
    string ItemId,
    string Command,
    string Cwd,
    JsonElement? SandboxPolicy,
    string? SandboxMode,
    KernelApprovalPolicy? ApprovalPolicy,
    IReadOnlyList<string>? SkillAllowedDomains = null,
    IReadOnlyList<string>? SkillDeniedDomains = null,
    InteractionEnvelopeRef? InteractionEnvelope = null);
