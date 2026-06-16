using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// exec policy 对命令或工具的决策结果类型。
/// Decision kind returned by exec policy evaluation for commands or tools.
/// </summary>
internal enum KernelExecPolicyDecisionKind
{
    Allow,
    NeedsApproval,
    Forbidden,
}

/// <summary>
/// 建议写回 exec policy 的命令前缀修正规则。
/// Suggested exec policy amendment describing an allow-listed command prefix.
/// </summary>
internal sealed record KernelExecPolicyAmendment(IReadOnlyList<string> CommandPrefix)
{
    public object ToPayload() => new
    {
        type = "allowPrefix",
        commandPrefix = CommandPrefix.ToArray(),
    };
}

/// <summary>
/// exec policy 的求值结果。
/// Evaluation result produced by exec policy.
/// </summary>
internal sealed record KernelExecPolicyDecision(
    KernelExecPolicyDecisionKind Kind,
    string Reason,
    bool BypassSandbox,
    KernelExecPolicyAmendment? ProposedAmendment);

/// <summary>
/// 从审批响应中解析是否接受了建议的 exec policy amendment。
/// Interprets approval responses to determine whether the proposed exec policy amendment was accepted.
/// </summary>
internal static class KernelExecPolicyApprovalResponseReader
{
    public static KernelExecPolicyAmendment? TryReadAppliedAmendment(
        JsonElement response,
        KernelExecPolicyAmendment? proposedAmendment)
    {
        if (proposedAmendment is null)
        {
            return null;
        }

        if (response.ValueKind == JsonValueKind.Object
            && response.TryGetProperty("applyProposedExecPolicyAmendment", out var apply)
            && apply.ValueKind is JsonValueKind.True)
        {
            return proposedAmendment;
        }

        var decision = Normalize(ReadString(response, "decision"));
        if (string.Equals(decision, "acceptWithExecpolicyAmendment", StringComparison.OrdinalIgnoreCase))
        {
            return proposedAmendment;
        }

        if (TryReadObject(response, "decision", out var decisionObject))
        {
            var typedDecision = Normalize(ReadString(decisionObject, "type"));
            if (string.Equals(typedDecision, "acceptWithExecpolicyAmendment", StringComparison.OrdinalIgnoreCase))
            {
                return proposedAmendment;
            }

            if (decisionObject.TryGetProperty("acceptWithExecpolicyAmendment", out _))
            {
                return proposedAmendment;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool TryReadObject(JsonElement json, string propertyName, out JsonElement value)
    {
        value = default;
        return json.ValueKind == JsonValueKind.Object
               && json.TryGetProperty(propertyName, out value)
               && value.ValueKind == JsonValueKind.Object;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
