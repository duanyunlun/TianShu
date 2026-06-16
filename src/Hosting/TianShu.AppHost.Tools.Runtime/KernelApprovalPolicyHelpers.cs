using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelApprovalPolicyHelpers
{
    public static bool IsNever(KernelApprovalPolicy? approvalPolicy)
        => string.Equals(NormalizeScalar(approvalPolicy), "never", StringComparison.OrdinalIgnoreCase);

    public static bool IsOnRequest(KernelApprovalPolicy? approvalPolicy)
        => string.Equals(NormalizeScalar(approvalPolicy), "on-request", StringComparison.OrdinalIgnoreCase);

    public static bool IsOnFailure(KernelApprovalPolicy? approvalPolicy)
        => string.Equals(NormalizeScalar(approvalPolicy), "on-failure", StringComparison.OrdinalIgnoreCase);

    public static bool IsUntrusted(KernelApprovalPolicy? approvalPolicy)
        => string.Equals(NormalizeScalar(approvalPolicy), "untrusted", StringComparison.OrdinalIgnoreCase);

    public static string? NormalizeScalar(KernelApprovalPolicy? approvalPolicy)
    {
        if (approvalPolicy is null)
        {
            return null;
        }

        return approvalPolicy.IsGranular
            ? "granular"
            : approvalPolicy.ScalarValue;
    }

    public static bool IsGranular(KernelApprovalPolicy? approvalPolicy)
        => approvalPolicy?.IsGranular == true;

    public static string? PromptRejectedByPolicy(KernelApprovalPolicy? approvalPolicy, bool promptIsRule)
    {
        if (approvalPolicy is null)
        {
            return null;
        }

        if (IsNever(approvalPolicy))
        {
            return promptIsRule
                ? "approval required by policy rule, but approvalPolicy is never"
                : "approval required by policy, but approvalPolicy is never";
        }

        if (approvalPolicy.GranularPolicy is null)
        {
            return null;
        }

        if (promptIsRule)
        {
            return approvalPolicy.GranularPolicy.Rules
                ? null
                : "approval required by policy rule, but approvalPolicy.granular.rules is false";
        }

        return approvalPolicy.GranularPolicy.SandboxApproval
            ? null
            : "approval required by policy, but approvalPolicy.granular.sandbox_approval is false";
    }

    public static bool TryGetGranularFlag(
        KernelApprovalPolicy? approvalPolicy,
        string snakeCasePropertyName,
        string camelCasePropertyName,
        out bool value)
    {
        value = false;
        var granular = approvalPolicy?.GranularPolicy;
        if (granular is null)
        {
            return false;
        }

        switch (NormalizePropertyName(snakeCasePropertyName), NormalizePropertyName(camelCasePropertyName))
        {
            case ("sandbox_approval", _) or (_, "sandbox_approval"):
                value = granular.SandboxApproval;
                return true;
            case ("rules", _) or (_, "rules"):
                value = granular.Rules;
                return true;
            case ("skill_approval", _) or (_, "skill_approval"):
                value = granular.SkillApproval;
                return true;
            case ("request_permissions", _) or (_, "request_permissions"):
                value = granular.RequestPermissions;
                return true;
            case ("mcp_elicitations", _) or (_, "mcp_elicitations"):
                value = granular.McpElicitations;
                return true;
            default:
                return false;
        }
    }

    public static object? ToPayloadValue(KernelApprovalPolicy? approvalPolicy)
    {
        return approvalPolicy?.ToPlainObject();
    }

    public static bool TryRead(object? rawValue, out KernelApprovalPolicy? approvalPolicy)
    {
        approvalPolicy = null;
        try
        {
            switch (rawValue)
            {
                case null:
                    return false;
                case KernelApprovalPolicy typedPolicy:
                    approvalPolicy = typedPolicy;
                    return true;
                case string text:
                    approvalPolicy = KernelApprovalPolicy.Parse(text);
                    return true;
                case JsonElement element when element.ValueKind is JsonValueKind.String or JsonValueKind.Object:
                    approvalPolicy = JsonSerializer.Deserialize<KernelApprovalPolicy>(element.GetRawText());
                    return approvalPolicy is not null;
                case Dictionary<string, object?> dictionary:
                    approvalPolicy = JsonSerializer.Deserialize<KernelApprovalPolicy>(JsonSerializer.Serialize(dictionary));
                    return approvalPolicy is not null;
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            approvalPolicy = null;
            return false;
        }
    }

    private static string NormalizePropertyName(string? propertyName)
        => string.IsNullOrWhiteSpace(propertyName)
            ? string.Empty
            : propertyName.Trim().Replace('-', '_').ToLowerInvariant();
}
