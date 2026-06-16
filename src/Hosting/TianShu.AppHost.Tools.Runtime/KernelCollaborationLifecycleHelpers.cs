using System.Text.Json;
using System.Text.Json.Nodes;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelCollabLifecycleDescriptor(
    string Tool,
    IReadOnlyList<string> ReceiverThreadIds,
    string? Prompt,
    string? Model,
    string? ReasoningEffort);

internal sealed record KernelCollabCompletedState(
    string Status,
    IReadOnlyList<string> ReceiverThreadIds,
    IReadOnlyDictionary<string, object?> AgentsStates);

internal static class KernelCollaborationLifecycleHelpers
{
    public static object CreateCollabToolCallItem(
        string itemId,
        string tool,
        string status,
        string senderThreadId,
        IReadOnlyList<string> receiverThreadIds,
        string? prompt,
        string? model,
        string? reasoningEffort,
        IReadOnlyDictionary<string, object?> agentsStates)
    {
        return new
        {
            id = itemId,
            type = "collabAgentToolCall",
            tool,
            status,
            senderThreadId,
            receiverThreadIds,
            prompt,
            model,
            reasoningEffort,
            agentsStates,
        };
    }

    public static KernelCollabCompletedState BuildCollabCompletedState(
        string toolName,
        JsonElement arguments,
        KernelToolResult result,
        KernelCollabLifecycleDescriptor descriptor)
    {
        if (!result.Success)
        {
            return new KernelCollabCompletedState(
                "failed",
                descriptor.ReceiverThreadIds,
                new Dictionary<string, object?>(StringComparer.Ordinal));
        }

        return KernelToolJsonHelpers.Normalize(toolName) switch
        {
            "spawn_agent" => BuildSpawnCompletedState(result),
            "send_input" => BuildSendInputCompletedState(arguments, descriptor),
            "resume_agent" => BuildSingleAgentStatusCompletedState(arguments, result, descriptor),
            "close_agent" => BuildSingleAgentStatusCompletedState(arguments, result, descriptor),
            "wait" => BuildWaitCompletedState(result),
            _ => new KernelCollabCompletedState(
                "completed",
                descriptor.ReceiverThreadIds,
                new Dictionary<string, object?>(StringComparer.Ordinal)),
        };
    }

    public static bool TryCreateCollabLifecycleDescriptor(string toolName, JsonElement arguments, out KernelCollabLifecycleDescriptor descriptor)
    {
        descriptor = null!;
        switch (KernelToolJsonHelpers.Normalize(toolName))
        {
            case "spawn_agent":
                {
                    var request = ToolSchemaHelpers.ParseSpawnAgentRequest(arguments, out _);
                    if (request is null)
                    {
                        return false;
                    }

                    var prompt = KernelToolRuntimeInteractionHelpers.BuildCollabPrompt(request.Message, request.Items);
                    descriptor = new KernelCollabLifecycleDescriptor("spawnAgent", Array.Empty<string>(), prompt, request.Model, request.ReasoningEffort);
                    return true;
                }
            case "send_input":
                {
                    var receiverId = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "id"));
                    var input = ToolSchemaHelpers.ParseSharedInput(arguments, out _);
                    if (string.IsNullOrWhiteSpace(receiverId) || input is null)
                    {
                        return false;
                    }

                    var prompt = KernelToolRuntimeInteractionHelpers.BuildCollabPrompt(input.Value.Message, input.Value.Items);
                    descriptor = new KernelCollabLifecycleDescriptor("sendInput", new[] { receiverId! }, prompt, null, null);
                    return true;
                }
            case "resume_agent":
                {
                    var receiverId = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "id"));
                    if (string.IsNullOrWhiteSpace(receiverId))
                    {
                        return false;
                    }

                    descriptor = new KernelCollabLifecycleDescriptor("resumeAgent", new[] { receiverId! }, null, null, null);
                    return true;
                }
            case "wait":
                {
                    var ids = ToolSchemaHelpers.ReadStringArray(arguments, "ids");
                    if (ids.Count == 0)
                    {
                        return false;
                    }

                    descriptor = new KernelCollabLifecycleDescriptor("wait", ids, null, null, null);
                    return true;
                }
            case "close_agent":
                {
                    var receiverId = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "id"));
                    if (string.IsNullOrWhiteSpace(receiverId))
                    {
                        return false;
                    }

                    descriptor = new KernelCollabLifecycleDescriptor("closeAgent", new[] { receiverId! }, null, null, null);
                    return true;
                }
            default:
                return false;
        }
    }

    private static KernelCollabCompletedState BuildSpawnCompletedState(KernelToolResult result)
    {
        var agentId = TryReadJsonString(result.OutputText, "agent_id");
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return new KernelCollabCompletedState(
                "failed",
                Array.Empty<string>(),
                new Dictionary<string, object?>(StringComparer.Ordinal));
        }

        var receiverThreadIds = new[] { agentId! };
        var agentsStates = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [agentId!] = CreateCollabAgentStatePayload("running", null),
        };

        return new KernelCollabCompletedState("completed", receiverThreadIds, agentsStates);
    }

    private static KernelCollabCompletedState BuildSendInputCompletedState(
        JsonElement arguments,
        KernelCollabLifecycleDescriptor descriptor)
    {
        var agentId = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "id"));
        var receiverThreadIds = !string.IsNullOrWhiteSpace(agentId)
            ? new[] { agentId! }
            : descriptor.ReceiverThreadIds;
        var agentsStates = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (receiverThreadIds.Count > 0)
        {
            agentsStates[receiverThreadIds[0]] = CreateCollabAgentStatePayload("running", null);
        }

        return new KernelCollabCompletedState("completed", receiverThreadIds, agentsStates);
    }

    private static KernelCollabCompletedState BuildSingleAgentStatusCompletedState(
        JsonElement arguments,
        KernelToolResult result,
        KernelCollabLifecycleDescriptor descriptor)
    {
        var receiverId = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "id"));
        var receiverThreadIds = !string.IsNullOrWhiteSpace(receiverId)
            ? new[] { receiverId! }
            : descriptor.ReceiverThreadIds;
        var agentsStates = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (receiverThreadIds.Count > 0
            && TryReadTopLevelStatusNode(result.OutputText, out var statusNode)
            && TryCreateCollabAgentState(statusNode, out var state))
        {
            agentsStates[receiverThreadIds[0]] = state;
        }

        var status = ShouldFailCollabCall(agentsStates) ? "failed" : "completed";
        return new KernelCollabCompletedState(status, receiverThreadIds, agentsStates);
    }

    private static KernelCollabCompletedState BuildWaitCompletedState(KernelToolResult result)
    {
        var receiverThreadIds = new List<string>();
        var agentsStates = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (TryReadStatusMap(result.OutputText, out var statuses))
        {
            foreach (var pair in statuses)
            {
                receiverThreadIds.Add(pair.Key);
                if (TryCreateCollabAgentState(pair.Value, out var state))
                {
                    agentsStates[pair.Key] = state;
                }
            }
        }

        var status = ShouldFailCollabCall(agentsStates) ? "failed" : "completed";
        return new KernelCollabCompletedState(status, receiverThreadIds, agentsStates);
    }

    private static bool TryReadTopLevelStatusNode(string outputText, out JsonNode? statusNode)
    {
        statusNode = null;
        if (string.IsNullOrWhiteSpace(outputText))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(outputText) as JsonObject;
            if (root is null || !root.TryGetPropertyValue("status", out statusNode))
            {
                return false;
            }

            statusNode = statusNode?.DeepClone();
            return statusNode is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadStatusMap(string outputText, out Dictionary<string, JsonNode?> statuses)
    {
        statuses = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(outputText) as JsonObject;
            var statusObject = root?["status"] as JsonObject;
            if (statusObject is null)
            {
                return false;
            }

            foreach (var pair in statusObject)
            {
                statuses[pair.Key] = pair.Value?.DeepClone();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryReadJsonString(string outputText, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(outputText))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(outputText);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return KernelToolJsonHelpers.Normalize(property.GetString());
        }
        catch
        {
            return null;
        }
    }

    private static bool TryCreateCollabAgentState(JsonNode? statusNode, out object? state)
    {
        state = null;
        if (statusNode is null)
        {
            return false;
        }

        if (statusNode is JsonValue)
        {
            var value = TryReadJsonNodeString(statusNode);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            state = CreateCollabAgentStatePayload(NormalizeCollabAgentStatus(value!), null);
            return true;
        }

        if (statusNode is not JsonObject obj)
        {
            return false;
        }

        foreach (var pair in obj)
        {
            var status = KernelToolJsonHelpers.Normalize(pair.Key);
            if (string.IsNullOrWhiteSpace(status))
            {
                continue;
            }

            state = CreateCollabAgentStatePayload(
                NormalizeCollabAgentStatus(status!),
                TryReadJsonNodeString(pair.Value));
            return true;
        }

        return false;
    }

    private static object CreateCollabAgentStatePayload(string status, string? message)
    {
        return new
        {
            status,
            message,
        };
    }

    private static string? TryReadJsonNodeString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldFailCollabCall(IReadOnlyDictionary<string, object?> agentsStates)
    {
        foreach (var pair in agentsStates)
        {
            var serialized = JsonSerializer.Serialize(pair.Value);
            using var doc = JsonDocument.Parse(serialized);
            var stateStatus = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(doc.RootElement, "status"));
            if (string.Equals(stateStatus, "errored", StringComparison.Ordinal)
                || string.Equals(stateStatus, "notFound", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeCollabAgentStatus(string status)
    {
        return KernelToolJsonHelpers.Normalize(status) switch
        {
            "PendingInit" => "pendingInit",
            "pending_init" => "pendingInit",
            "Running" => "running",
            "Completed" => "completed",
            "Errored" => "errored",
            "Shutdown" => "shutdown",
            "NotFound" => "notFound",
            "not_found" => "notFound",
            "pendingInit" => "pendingInit",
            "running" => "running",
            "completed" => "completed",
            "errored" => "errored",
            "shutdown" => "shutdown",
            "notFound" => "notFound",
            var value when !string.IsNullOrWhiteSpace(value) => char.ToLowerInvariant(value![0]) + value[1..],
            _ => "completed",
        };
    }
}
