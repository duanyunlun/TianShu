using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelPendingInteractiveReplayHelpers
{
    public static bool IsPendingInteractiveRequestMethod(string method)
        => string.Equals(method, "item/commandExecution/requestApproval", StringComparison.Ordinal)
           || string.Equals(method, "item/fileChange/requestApproval", StringComparison.Ordinal)
           || string.Equals(method, "item/tool/requestApproval", StringComparison.Ordinal)
           || string.Equals(method, "item/permissions/requestApproval", StringComparison.Ordinal)
           || string.Equals(method, "item/tool/requestUserInput", StringComparison.Ordinal)
           || string.Equals(method, "mcpServer/elicitation/request", StringComparison.Ordinal);

    public static string? TryReadPendingInteractiveCallId(string method, JsonElement parameters, long requestId)
    {
        var callId = Normalize(ReadString(parameters, "approvalId"))
            ?? Normalize(ReadString(parameters, "callId"))
            ?? Normalize(ReadString(parameters, "itemId"));
        if (!string.IsNullOrWhiteSpace(callId))
        {
            return callId;
        }

        if (string.Equals(method, "mcpServer/elicitation/request", StringComparison.Ordinal))
        {
            return Normalize(ReadString(parameters, "elicitationId")) ?? $"elicitation-{requestId}";
        }

        return null;
    }

    public static object BuildPendingInteractiveRequestPayload(
        long requestId,
        string method,
        string callId,
        string threadId,
        string? turnId,
        JsonElement parameters,
        DateTimeOffset? requestedAt = null)
    {
        return method switch
        {
            "item/permissions/requestApproval" => BuildPendingPermissionRequestPayload(requestId, method, callId, threadId, turnId, parameters, requestedAt),
            "item/tool/requestUserInput" => BuildPendingUserInputRequestPayload(requestId, method, callId, threadId, turnId, parameters, requestedAt),
            _ => BuildPendingApprovalRequestPayload(requestId, method, callId, threadId, turnId, parameters, requestedAt),
        };
    }

    public static JsonElement? ReadPendingAvailableDecisionsElement(JsonElement parameters)
    {
        if (TryGetProperty(parameters, "availableDecisions", out var decisionsElement)
            && decisionsElement.ValueKind == JsonValueKind.Array)
        {
            return decisionsElement;
        }

        if (TryReadObject(parameters, "_meta", out var meta)
            && TryGetProperty(meta, "available_decisions", out var metaDecisions)
            && metaDecisions.ValueKind == JsonValueKind.Array)
        {
            return metaDecisions;
        }

        return null;
    }

    public static string[]? ExtractAvailableDecisionTypes(JsonElement parameters)
    {
        var decisionsElement = ReadPendingAvailableDecisionsElement(parameters);
        if (!decisionsElement.HasValue || decisionsElement.Value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var decisions = new List<string>();
        foreach (var decision in decisionsElement.Value.EnumerateArray())
        {
            string? type = decision.ValueKind switch
            {
                JsonValueKind.String => decision.GetString(),
                JsonValueKind.Object => Normalize(ReadString(decision, "type"))
                    ?? decision.EnumerateObject().Select(static property => property.Name).FirstOrDefault(),
                _ => null,
            };

            type = NormalizeApprovalDecision(type);
            if (!string.IsNullOrWhiteSpace(type))
            {
                decisions.Add(type!);
            }
        }

        return decisions.Count > 0 ? decisions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : null;
    }

    public static string ResolvePendingApprovalToolName(string method, JsonElement parameters)
        => method switch
        {
            "item/commandExecution/requestApproval" => "commandExecution",
            "item/fileChange/requestApproval" => "fileChange",
            "mcpServer/elicitation/request" => string.Equals(
                Normalize(ReadString(parameters, "_meta", "codex_approval_kind")),
                "tool_suggestion",
                StringComparison.OrdinalIgnoreCase)
                ? "tool_suggest"
                : Normalize(ReadString(parameters, "serverName")) ?? "mcpServerElicitation",
            _ => Normalize(ReadString(parameters, "toolName")) ?? "tool",
        };

    public static string BuildPendingCommandExecutionApprovalSummary(JsonElement parameters)
    {
        var parts = new List<string>();
        var command = Normalize(ReadString(parameters, "command"));
        var reason = Normalize(ReadString(parameters, "reason"));
        var host = Normalize(ReadString(parameters, "networkApprovalContext", "host"));
        if (!string.IsNullOrWhiteSpace(command))
        {
            parts.Add(command!);
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            parts.Add(reason!);
        }

        if (!string.IsNullOrWhiteSpace(host))
        {
            parts.Add($"host={host}");
        }

        return parts.Count == 0 ? "commandExecution approval" : string.Join(" | ", parts);
    }

    public static string BuildPendingToolApprovalSummary(JsonElement parameters)
    {
        var parts = new[]
        {
            Normalize(ReadString(parameters, "toolName")),
            Normalize(ReadString(parameters, "arguments")) ?? Normalize(ReadString(parameters, "input")),
            Normalize(ReadString(parameters, "reason")),
        };

        return string.Join(" | ", parts.Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    public static string BuildPendingMcpServerElicitationSummary(JsonElement parameters)
    {
        var parts = new List<string>();
        var message = Normalize(ReadString(parameters, "message"));
        var toolName = Normalize(ReadString(parameters, "_meta", "tool_name"));
        var installUrl = Normalize(ReadString(parameters, "_meta", "install_url"));
        if (!string.IsNullOrWhiteSpace(message))
        {
            parts.Add(message!);
        }

        if (!string.IsNullOrWhiteSpace(toolName))
        {
            parts.Add($"tool={toolName}");
        }

        if (!string.IsNullOrWhiteSpace(installUrl))
        {
            parts.Add($"install_url={installUrl}");
        }

        return parts.Count == 0 ? "mcp server elicitation request" : string.Join(" | ", parts);
    }

    public static string BuildPendingUserInputSummary(JsonElement parameters)
    {
        if (!TryGetProperty(parameters, "questions", out var questionsElement)
            || questionsElement.ValueKind != JsonValueKind.Array)
        {
            return "等待用户补充输入";
        }

        var summary = new List<string>();
        foreach (var question in questionsElement.EnumerateArray())
        {
            if (question.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var header = Normalize(ReadString(question, "header"))
                         ?? Normalize(ReadString(question, "prompt"))
                         ?? Normalize(ReadString(question, "question"))
                         ?? Normalize(ReadString(question, "id"));
            if (!string.IsNullOrWhiteSpace(header))
            {
                summary.Add($"- {header}");
            }
        }

        return summary.Count == 0 ? "等待用户补充输入" : string.Join(Environment.NewLine, summary);
    }

    public static object[] BuildPendingUserInputQuestions(JsonElement parameters)
    {
        if (!TryGetProperty(parameters, "questions", out var questionsElement)
            || questionsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<object>();
        }

        var questions = new List<object>();
        foreach (var question in questionsElement.EnumerateArray())
        {
            if (question.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = Normalize(ReadString(question, "id"));
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            questions.Add(new
            {
                id,
                header = ReadString(question, "header") ?? string.Empty,
                prompt = ReadString(question, "prompt") ?? ReadString(question, "question") ?? string.Empty,
                isSecret = ReadBool(question, "isSecret") == true,
                isOther = ReadBool(question, "isOther") == true,
                options = BuildPendingUserInputOptions(question),
            });
        }

        return questions.ToArray();
    }

    public static bool TryGetProperty(JsonElement json, string propertyName, out JsonElement value)
    {
        value = default;
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        value = property;
        return true;
    }

    private static object BuildPendingApprovalRequestPayload(
        long requestId,
        string method,
        string callId,
        string threadId,
        string? turnId,
        JsonElement parameters,
        DateTimeOffset? requestedAt)
    {
        var availableDecisionsElement = ReadPendingAvailableDecisionsElement(parameters);
        var availableDecisionOptions = availableDecisionsElement.HasValue && availableDecisionsElement.Value.ValueKind == JsonValueKind.Array
            ? ConvertJsonElementToObject(availableDecisionsElement.Value)
            : null;
        var availableDecisionTypes = ExtractAvailableDecisionTypes(parameters);
        var summary = method switch
        {
            "item/commandExecution/requestApproval" => BuildPendingCommandExecutionApprovalSummary(parameters),
            "mcpServer/elicitation/request" => BuildPendingMcpServerElicitationSummary(parameters),
            _ => BuildPendingToolApprovalSummary(parameters),
        };
        var toolName = ResolvePendingApprovalToolName(method, parameters);
        var approvalKind = Normalize(ReadString(parameters, "approvalKind"))
                           ?? Normalize(ReadString(parameters, "_meta", "codex_approval_kind"));

        return new
        {
            requestId,
            requestKind = "approval_requested",
            requestMethod = method,
            callId,
            threadId,
            turnId,
            requestedAt = requestedAt ?? DateTimeOffset.UtcNow,
            toolName,
            serverName = Normalize(ReadString(parameters, "serverName")),
            text = summary,
            status = "awaitingApproval",
            phase = "request_approval",
            requiresApproval = true,
            approvalKind,
            availableDecisions = availableDecisionTypes,
            availableDecisionOptions,
            approvalRequest = new
            {
                toolName,
                approvalKind,
                availableDecisions = availableDecisionTypes,
                availableDecisionOptions,
                summary,
                metadataFields = Array.Empty<object>(),
            },
        };
    }

    private static object BuildPendingPermissionRequestPayload(
        long requestId,
        string method,
        string callId,
        string threadId,
        string? turnId,
        JsonElement parameters,
        DateTimeOffset? requestedAt)
    {
        var permissions = TryReadObject(parameters, "permissions", out var permissionsObject)
            ? permissionsObject
            : default;
        var permissionsJson = permissions.ValueKind == JsonValueKind.Object
            ? permissions.GetRawText()
            : "{}";
        var reason = Normalize(ReadString(parameters, "reason"));
        var summary = string.Join(
            " | ",
            new[] { reason, permissionsJson }.Where(static value => !string.IsNullOrWhiteSpace(value)));

        return new
        {
            requestId,
            requestKind = "permission_requested",
            requestMethod = method,
            callId,
            threadId,
            turnId,
            requestedAt = requestedAt ?? DateTimeOffset.UtcNow,
            toolName = "request_permissions",
            text = summary,
            status = "awaitingPermission",
            phase = "request_permission",
            permissionRequest = new
            {
                reason,
                fields = Array.Empty<object>(),
                permissionsJson,
                summary,
            },
        };
    }

    private static object BuildPendingUserInputRequestPayload(
        long requestId,
        string method,
        string callId,
        string threadId,
        string? turnId,
        JsonElement parameters,
        DateTimeOffset? requestedAt)
    {
        var summary = BuildPendingUserInputSummary(parameters);
        return new
        {
            requestId,
            requestKind = "request_user_input",
            requestMethod = method,
            callId,
            threadId,
            turnId,
            requestedAt = requestedAt ?? DateTimeOffset.UtcNow,
            toolName = "requestUserInput",
            text = summary,
            status = "awaitingUserInput",
            phase = "request_user_input",
            userInputRequest = new
            {
                questions = BuildPendingUserInputQuestions(parameters),
                summary,
            },
        };
    }

    private static object[]? BuildPendingUserInputOptions(JsonElement question)
    {
        if (!TryGetProperty(question, "options", out var optionsElement)
            || optionsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var options = new List<object>();
        foreach (var option in optionsElement.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var label = Normalize(ReadString(option, "label"));
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            options.Add(new
            {
                label,
                description = ReadString(option, "description"),
            });
        }

        return options.Count > 0 ? options.ToArray() : Array.Empty<object>();
    }

    private static object? ConvertJsonElementToObject(JsonElement? element)
    {
        if (element is not JsonElement value)
        {
            return null;
        }

        return JsonSerializer.Deserialize<object?>(value.GetRawText());
    }

    private static string? ReadString(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static bool? ReadBool(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(current.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool TryReadObject(JsonElement json, string propertyName, out JsonElement value)
    {
        value = default;
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var candidate))
        {
            return false;
        }

        if (candidate.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static string? Normalize(string? value)
        => KernelToolJsonHelpers.Normalize(value);

    private static string? NormalizeApprovalDecision(string? decision)
    {
        var normalized = Normalize(decision);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.ToLowerInvariant() switch
        {
            "accept" => "accept",
            "approved" => "accept",
            "approve" => "accept",
            "acceptforsession" => "acceptForSession",
            "accept_for_session" => "acceptForSession",
            "acceptandremember" => "acceptAndRemember",
            "accept_and_remember" => "acceptAndRemember",
            "acceptwithexecpolicyamendment" => "acceptWithExecpolicyAmendment",
            "accept_with_execpolicy_amendment" => "acceptWithExecpolicyAmendment",
            "applynetworkpolicyamendment" => "applyNetworkPolicyAmendment",
            "apply_network_policy_amendment" => "applyNetworkPolicyAmendment",
            "decline" => "decline",
            "denied" => "decline",
            "deny" => "decline",
            "reject" => "decline",
            "rejected" => "decline",
            "cancel" => "cancel",
            _ => normalized,
        };
    }
}
