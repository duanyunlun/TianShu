using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Protocol;

internal static class AppServerServerRequestDtoHelpers
{
    public static IReadOnlyList<string>? ResolveAvailableDecisions(IReadOnlyList<string>? availableDecisions, IReadOnlyList<string>? legacyAvailableDecisions)
        => availableDecisions is { Count: > 0 }
            ? availableDecisions
            : legacyAvailableDecisions is { Count: > 0 }
                ? legacyAvailableDecisions
                : null;

    public static IReadOnlyList<ApprovalDecisionOptionPayload>? ResolveAvailableDecisionOptions(
        JsonElement availableDecisions,
        JsonElement legacyAvailableDecisions)
    {
        var source = availableDecisions.ValueKind == JsonValueKind.Array
            ? availableDecisions
            : legacyAvailableDecisions.ValueKind == JsonValueKind.Array
                ? legacyAvailableDecisions
                : default;
        if (source.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var options = new List<ApprovalDecisionOptionPayload>();
        foreach (var item in source.EnumerateArray())
        {
            if (TryParseApprovalDecisionOption(item, out var option))
            {
                options.Add(option);
            }
        }

        return options.Count > 0 ? options : null;
    }

    public static IReadOnlyList<string>? ResolveAvailableDecisions(IReadOnlyList<ApprovalDecisionOptionPayload>? options)
        => options is { Count: > 0 }
            ? options.Select(static option => option.Type).ToArray()
            : null;

    public static ExecPolicyAmendmentPayload? ResolveExecPolicyAmendment(JsonElement amendment)
    {
        return TryReadExecPolicyAmendment(amendment, out var parsed) ? parsed : null;
    }

    internal static string? ReadString(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        if (current.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
    }

    internal static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool TryParseApprovalDecisionOption(JsonElement json, out ApprovalDecisionOptionPayload option)
    {
        option = default!;
        switch (json.ValueKind)
        {
            case JsonValueKind.String:
                var stringType = NormalizeDecisionType(json.GetString());
                if (string.IsNullOrWhiteSpace(stringType))
                {
                    return false;
                }

                option = new ApprovalDecisionOptionPayload(stringType!);
                return true;

            case JsonValueKind.Object:
                if (TryReadObjectDecisionOption(json, out option))
                {
                    return true;
                }

                return TryReadUnionDecisionOption(json, out option);

            default:
                return false;
        }
    }

    private static bool TryReadObjectDecisionOption(JsonElement json, out ApprovalDecisionOptionPayload option)
    {
        option = default!;
        var type = NormalizeDecisionType(ReadString(json, "type"));
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        var execPolicyAmendment = type == "acceptWithExecpolicyAmendment"
            ? ReadExecPolicyAmendmentFromObject(json)
            : null;
        var networkPolicyAmendment = type == "applyNetworkPolicyAmendment"
            ? ReadNetworkPolicyAmendmentFromObject(json)
            : null;
        option = new ApprovalDecisionOptionPayload(type!, execPolicyAmendment, networkPolicyAmendment);
        return true;
    }

    private static bool TryReadUnionDecisionOption(JsonElement json, out ApprovalDecisionOptionPayload option)
    {
        option = default!;
        if (!json.EnumerateObject().MoveNext())
        {
            return false;
        }

        foreach (var property in json.EnumerateObject())
        {
            var type = NormalizeDecisionType(property.Name);
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            var execPolicyAmendment = type == "acceptWithExecpolicyAmendment"
                ? ReadExecPolicyAmendmentFromUnion(property.Value)
                : null;
            var networkPolicyAmendment = type == "applyNetworkPolicyAmendment"
                ? ReadNetworkPolicyAmendmentFromUnion(property.Value)
                : null;
            option = new ApprovalDecisionOptionPayload(type!, execPolicyAmendment, networkPolicyAmendment);
            return true;
        }

        return false;
    }

    private static string? NormalizeDecisionType(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant() switch
        {
            "accept" => "accept",
            "acceptforsession" => "acceptForSession",
            "acceptandremember" => "acceptAndRemember",
            "acceptwithexecpolicyamendment" => "acceptWithExecpolicyAmendment",
            "applynetworkpolicyamendment" => "applyNetworkPolicyAmendment",
            "decline" => "decline",
            "cancel" => "cancel",
            _ => normalized,
        };
    }

    private static bool TryReadExecPolicyAmendment(JsonElement json, out ExecPolicyAmendmentPayload? amendment)
    {
        amendment = null;
        if (json.ValueKind == JsonValueKind.Undefined || json.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        amendment = json.ValueKind switch
        {
            JsonValueKind.Array => BuildExecPolicyAmendment(json),
            JsonValueKind.Object => ReadExecPolicyAmendmentFromObject(json) ?? ReadExecPolicyAmendmentFromUnion(json),
            _ => null,
        };
        return amendment is not null;
    }

    private static ExecPolicyAmendmentPayload? ReadExecPolicyAmendmentFromObject(JsonElement json)
    {
        if (json.TryGetProperty("execPolicyAmendment", out var camel))
        {
            return camel.ValueKind == JsonValueKind.Array ? BuildExecPolicyAmendment(camel) : ReadExecPolicyAmendmentFromUnion(camel);
        }

        if (json.TryGetProperty("execpolicy_amendment", out var snake))
        {
            return snake.ValueKind == JsonValueKind.Array ? BuildExecPolicyAmendment(snake) : ReadExecPolicyAmendmentFromUnion(snake);
        }

        return null;
    }

    private static ExecPolicyAmendmentPayload? ReadExecPolicyAmendmentFromUnion(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
        {
            return BuildExecPolicyAmendment(json);
        }

        if (json.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (json.TryGetProperty("execpolicy_amendment", out var snake))
        {
            return snake.ValueKind == JsonValueKind.Array ? BuildExecPolicyAmendment(snake) : null;
        }

        if (json.TryGetProperty("execPolicyAmendment", out var camel))
        {
            return camel.ValueKind == JsonValueKind.Array ? BuildExecPolicyAmendment(camel) : null;
        }

        return null;
    }

    private static ExecPolicyAmendmentPayload? BuildExecPolicyAmendment(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var commandPrefix = json
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => Normalize(item.GetString()))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
        return commandPrefix.Length == 0 ? null : new ExecPolicyAmendmentPayload(commandPrefix);
    }

    private static NetworkPolicyAmendmentPayload? ReadNetworkPolicyAmendmentFromObject(JsonElement json)
    {
        if (json.TryGetProperty("networkPolicyAmendment", out var camel))
        {
            return BuildNetworkPolicyAmendment(camel);
        }

        if (json.TryGetProperty("network_policy_amendment", out var snake))
        {
            return BuildNetworkPolicyAmendment(snake);
        }

        return null;
    }

    private static NetworkPolicyAmendmentPayload? ReadNetworkPolicyAmendmentFromUnion(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (json.TryGetProperty("network_policy_amendment", out var snake))
        {
            return BuildNetworkPolicyAmendment(snake);
        }

        if (json.TryGetProperty("networkPolicyAmendment", out var camel))
        {
            return BuildNetworkPolicyAmendment(camel);
        }

        return null;
    }

    private static NetworkPolicyAmendmentPayload? BuildNetworkPolicyAmendment(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var host = Normalize(ReadString(json, "host"));
        var action = Normalize(ReadString(json, "action"));
        return string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(action)
            ? null
            : new NetworkPolicyAmendmentPayload(host!, action!);
    }
}
