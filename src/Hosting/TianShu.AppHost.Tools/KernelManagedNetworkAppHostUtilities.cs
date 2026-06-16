using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// managed-network 宿主编排侧 JSON 解析与 snapshot 构造辅助件。
/// Helpers for managed-network host-side JSON parsing and snapshot construction.
/// </summary>
internal static class KernelManagedNetworkAppHostUtilities
{
    public static KernelManagedNetworkExecutionLeaseSnapshot CreateManagedNetworkLeaseSnapshot(IKernelManagedNetworkExecutionLease lease)
        => new(
            lease.IsActive,
            lease.HttpProxyUrl,
            lease.SocksProxyUrl,
            lease.GetBlockedRequestTotal());

    public static bool IsSandboxPolicyNetworkEnabled(JsonElement sandboxPolicy)
    {
        var boolValue = ReadBool(sandboxPolicy, "networkAccess");
        if (boolValue == true)
        {
            return true;
        }

        var networkAccess = ReadString(sandboxPolicy, "networkAccess");
        return string.Equals(networkAccess, "enabled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(networkAccess, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static string ExtractApprovalDecision(JsonElement response)
    {
        var decision = Normalize(ReadString(response, "decision"));
        if (!string.IsNullOrWhiteSpace(decision))
        {
            return decision!;
        }

        if (TryReadObject(response, "decision", out var decisionObject))
        {
            var typedDecision = Normalize(ReadString(decisionObject, "type"));
            if (!string.IsNullOrWhiteSpace(typedDecision))
            {
                return typedDecision!;
            }

            if (decisionObject.TryGetProperty("acceptWithExecpolicyAmendment", out _))
            {
                return "acceptWithExecpolicyAmendment";
            }

            if (decisionObject.TryGetProperty("applyNetworkPolicyAmendment", out _))
            {
                return "applyNetworkPolicyAmendment";
            }
        }

        return "cancel";
    }

    public static bool TryReadNetworkPolicyAmendment(JsonElement response, out KernelManagedNetworkPolicyAmendment? amendment)
    {
        amendment = null;
        if (TryReadNetworkPolicyAmendmentCore(response, out amendment))
        {
            return amendment is not null;
        }

        if (TryReadObject(response, "decision", out var decisionObject))
        {
            return TryReadNetworkPolicyAmendmentCore(decisionObject, out amendment) && amendment is not null;
        }

        return false;
    }

    private static bool TryReadNetworkPolicyAmendmentCore(JsonElement json, out KernelManagedNetworkPolicyAmendment? amendment)
    {
        amendment = null;
        if (TryReadObject(json, "applyNetworkPolicyAmendment", out var applyNetworkPolicyAmendment)
            && TryReadObject(applyNetworkPolicyAmendment, "network_policy_amendment", out var nestedNetworkPolicyAmendment))
        {
            return TryBuildNetworkPolicyAmendment(nestedNetworkPolicyAmendment, out amendment);
        }

        if (!TryReadObject(json, "networkPolicyAmendment", out var networkPolicyAmendment))
        {
            if (!TryReadObject(json, "network_policy_amendment", out networkPolicyAmendment))
            {
                return false;
            }
        }

        return TryBuildNetworkPolicyAmendment(networkPolicyAmendment, out amendment);
    }

    private static bool TryBuildNetworkPolicyAmendment(JsonElement networkPolicyAmendment, out KernelManagedNetworkPolicyAmendment? amendment)
    {
        amendment = null;
        var host = Normalize(ReadString(networkPolicyAmendment, "host"));
        var action = Normalize(ReadString(networkPolicyAmendment, "action"));
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        if (!string.Equals(action, "allow", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(action, "deny", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        amendment = new KernelManagedNetworkPolicyAmendment(
            host!,
            string.Equals(action, "deny", StringComparison.OrdinalIgnoreCase)
                ? KernelManagedNetworkRuleAction.Deny
                : KernelManagedNetworkRuleAction.Allow);
        return true;
    }

    private static bool? ReadBool(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool TryReadObject(JsonElement json, string propertyName, out JsonElement value)
    {
        value = default;
        if (!json.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        value = property;
        return true;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
