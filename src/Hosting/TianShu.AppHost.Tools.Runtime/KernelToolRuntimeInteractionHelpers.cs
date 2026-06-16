using System.Text.Json;
using TianShu.AppHost.Catalog;
using TianShu.AppHost.Configuration;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelToolRuntimeInteractionHelpers
{
    private const string ForkedSpawnAgentOutputMessage = "You are the newly spawned agent. The prior conversation history was forked from your parent agent. Treat the next user message as your new task, and use the forked history only as background context.";

    public static string NormalizePlanStatus(string status)
    {
        return KernelToolJsonHelpers.Normalize(status)?.ToLowerInvariant() switch
        {
            "in_progress" => "inProgress",
            "pending" => "pending",
            "completed" => "completed",
            _ => status,
        };
    }

    public static KernelRequestUserInputResponse ParseRequestUserInputResponse(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("answers", out var answersElement)
            || answersElement.ValueKind != JsonValueKind.Object)
        {
            return new KernelRequestUserInputResponse(new Dictionary<string, KernelRequestUserInputAnswer>(StringComparer.Ordinal));
        }

        var answers = new Dictionary<string, KernelRequestUserInputAnswer>(StringComparer.Ordinal);
        foreach (var entry in answersElement.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object
                || !entry.Value.TryGetProperty("answers", out var answerArray)
                || answerArray.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var values = answerArray
                .EnumerateArray()
                .Where(static value => value.ValueKind == JsonValueKind.String)
                .Select(static value => value.GetString())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToArray();

            answers[entry.Name] = new KernelRequestUserInputAnswer(values);
        }

        return new KernelRequestUserInputResponse(answers);
    }

    public static string BuildCollabPrompt(string? message, IReadOnlyList<KernelCollabInputItem>? items)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message.Trim();
        }

        if (items is null || items.Count == 0)
        {
            throw new InvalidOperationException("Provide one of: message or items");
        }

        var prompt = ToolSchemaHelpers.BuildInputPreview(items);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException("Items can't be empty");
        }

        return prompt;
    }

    public static IReadOnlyList<KernelTurnInputItem>? BuildCollabTurnInputItems(IReadOnlyList<KernelCollabInputItem>? items)
    {
        if (items is null || items.Count == 0)
        {
            return null;
        }

        var converted = new List<KernelTurnInputItem>(items.Count);
        foreach (var item in items)
        {
            var parsed = BuildCollabTurnInputItem(item);
            if (parsed is not null)
            {
                converted.Add(parsed);
            }
        }

        return converted.Count == 0 ? null : converted;
    }

    public static KernelTurnInputItem? BuildCollabTurnInputItem(KernelCollabInputItem item)
    {
        var type = KernelToolJsonHelpers.Normalize(item.Type) ?? "text";
        var text = KernelToolJsonHelpers.Normalize(item.Text);
        var name = KernelToolJsonHelpers.Normalize(item.Name);
        var path = KernelToolJsonHelpers.Normalize(item.Path);
        var imageUrl = KernelToolJsonHelpers.Normalize(item.ImageUrl);

        return type.ToLowerInvariant() switch
        {
            "text" => string.IsNullOrWhiteSpace(text)
                ? null
                : new KernelTurnInputItem
                {
                    Type = "text",
                    Text = text,
                },
            "image" => string.IsNullOrWhiteSpace(imageUrl)
                ? null
                : new KernelTurnInputItem
                {
                    Type = "image",
                    Url = imageUrl,
                },
            "local_image" => string.IsNullOrWhiteSpace(path)
                ? null
                : new KernelTurnInputItem
                {
                    Type = "local_image",
                    Path = path,
                },
            "mention" => string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(name)
                ? null
                : new KernelTurnInputItem
                {
                    Type = "mention",
                    Name = name,
                    Path = path,
                },
            "skill" => string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(name)
                ? null
                : new KernelTurnInputItem
                {
                    Type = "skill",
                    Name = name,
                    Path = path,
                },
            _ => string.IsNullOrWhiteSpace(text)
                 && string.IsNullOrWhiteSpace(imageUrl)
                 && string.IsNullOrWhiteSpace(path)
                 && string.IsNullOrWhiteSpace(name)
                ? null
                : new KernelTurnInputItem
                {
                    Type = type,
                    Text = text,
                    Url = imageUrl,
                    Path = path,
                    Name = name,
                },
        };
    }

    public static JsonElement BuildForkedSpawnAgentFunctionCallItem(string parentCallId, KernelSpawnAgentRequest request)
    {
        return JsonSerializer.SerializeToElement(ToolUseFollowUpItemProjector.BuildFunctionCallItem(
            name: "spawn_agent",
            arguments: SerializeSpawnAgentArguments(request),
            callId: parentCallId));
    }

    public static JsonElement BuildForkedSpawnAgentFunctionCallOutputItem(string parentCallId)
    {
        return JsonSerializer.SerializeToElement(ToolUseFollowUpItemProjector.BuildFunctionCallOutputItem(
            parentCallId,
            isCustomToolCall: false,
            output: ForkedSpawnAgentOutputMessage));
    }

    public static string SerializeSpawnAgentArguments(KernelSpawnAgentRequest request)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["fork_context"] = request.ForkContext,
        };

        var message = KernelToolJsonHelpers.Normalize(request.Message);
        if (!string.IsNullOrWhiteSpace(message))
        {
            payload["message"] = message;
        }

        if (request.Items is { Count: > 0 })
        {
            payload["items"] = request.Items;
        }

        var agentType = KernelToolJsonHelpers.Normalize(request.AgentType);
        if (!string.IsNullOrWhiteSpace(agentType))
        {
            payload["agent_type"] = agentType;
        }

        var model = KernelToolJsonHelpers.Normalize(request.Model);
        if (!string.IsNullOrWhiteSpace(model))
        {
            payload["model"] = model;
        }

        var reasoningEffort = KernelToolJsonHelpers.Normalize(request.ReasoningEffort);
        if (!string.IsNullOrWhiteSpace(reasoningEffort))
        {
            payload["reasoning_effort"] = reasoningEffort;
        }

        return JsonSerializer.Serialize(payload);
    }

    public static (string? Model, string? ReasoningEffort) NormalizeSpawnAgentRequestedModelAndReasoning(
        string currentModel,
        string? requestedModel,
        string? requestedReasoningEffort)
    {
        var normalizedModel = KernelToolJsonHelpers.Normalize(requestedModel);
        var normalizedReasoningEffort = KernelToolJsonHelpers.Normalize(requestedReasoningEffort);
        if (string.IsNullOrWhiteSpace(normalizedModel) && string.IsNullOrWhiteSpace(normalizedReasoningEffort))
        {
            return (normalizedModel, normalizedReasoningEffort);
        }

        if (!string.IsNullOrWhiteSpace(normalizedModel))
        {
            if (!KernelCatalogSurfaceUtilities.TryGetBuiltInModel(normalizedModel, out var descriptor))
            {
                var available = string.Join(", ", KernelCatalogSurfaceUtilities.GetBuiltInModelNames());
                throw new InvalidOperationException($"Unknown model `{normalizedModel}` for spawn_agent. Available models: {available}");
            }

            if (!string.IsNullOrWhiteSpace(normalizedReasoningEffort))
            {
                ValidateSpawnAgentReasoningEffort(descriptor!, normalizedReasoningEffort);
            }
            else
            {
                normalizedReasoningEffort = descriptor!.DefaultReasoningEffort;
            }

            return (descriptor!.Model, normalizedReasoningEffort);
        }

        if (KernelCatalogSurfaceUtilities.TryGetBuiltInModel(currentModel, out var currentDescriptor)
            && !string.IsNullOrWhiteSpace(normalizedReasoningEffort))
        {
            ValidateSpawnAgentReasoningEffort(currentDescriptor!, normalizedReasoningEffort);
        }

        return (normalizedModel, normalizedReasoningEffort);
    }

    public static void ValidateSpawnAgentReasoningEffort(
        ControlPlaneModelCatalogItem model,
        string requestedReasoningEffort)
    {
        if (model.SupportedReasoningEfforts.Any(level => string.Equals(level, requestedReasoningEffort, StringComparison.Ordinal)))
        {
            return;
        }

        var supported = string.Join(", ", model.SupportedReasoningEfforts);
        throw new InvalidOperationException(
            $"Reasoning effort `{requestedReasoningEffort}` is not supported for model `{model.Model}`. Supported reasoning efforts: {supported}");
    }

    public static bool ContainsRawResponseTurnItem(
        IReadOnlyList<KernelTurnItemRecord> items,
        string type,
        string parentCallId)
    {
        foreach (var item in items)
        {
            if (item.Payload.ValueKind != JsonValueKind.Object
                || !item.Payload.TryGetProperty("type", out var typeElement)
                || !string.Equals(typeElement.GetString(), type, StringComparison.Ordinal)
                || !item.Payload.TryGetProperty("call_id", out var callIdElement)
                || !string.Equals(callIdElement.GetString(), parentCallId, StringComparison.Ordinal))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    public static KernelTurnRecord CloneTurnRecordForResponse(KernelTurnRecord source)
        => new()
        {
            Id = source.Id,
            StartedAt = source.StartedAt,
            CompletedAt = source.CompletedAt,
            Status = source.Status,
            UserMessage = source.UserMessage,
            AssistantMessage = source.AssistantMessage,
            InteractionEnvelope = source.InteractionEnvelope,
            Items = source.Items.Select(static item => new KernelTurnItemRecord
            {
                Id = item.Id,
                Type = item.Type,
                Payload = item.Payload.Clone(),
            }).ToList(),
            Error = source.Error is null
                ? null
                : new KernelTurnErrorRecord
                {
                    Message = source.Error.Message,
                    AdditionalDetails = source.Error.AdditionalDetails,
                },
            IsContextCompaction = source.IsContextCompaction,
        };

    public static KernelTurnRecord? InjectForkedSpawnAgentToolItems(
        KernelTurnRecord? liveParentTurn,
        string? parentCallId,
        KernelSpawnAgentRequest request)
    {
        var normalizedParentCallId = KernelToolJsonHelpers.Normalize(parentCallId);
        if (liveParentTurn is null || string.IsNullOrWhiteSpace(normalizedParentCallId))
        {
            return liveParentTurn;
        }

        var turn = CloneTurnRecordForResponse(liveParentTurn);
        if (!ContainsRawResponseTurnItem(turn.Items, "function_call", normalizedParentCallId))
        {
            turn.Items.Add(new KernelTurnItemRecord
            {
                Id = $"{normalizedParentCallId}_function_call",
                Type = "function_call",
                Payload = BuildForkedSpawnAgentFunctionCallItem(normalizedParentCallId, request),
            });
        }

        if (!ContainsRawResponseTurnItem(turn.Items, "function_call_output", normalizedParentCallId))
        {
            turn.Items.Add(new KernelTurnItemRecord
            {
                Id = $"{normalizedParentCallId}_function_call_output",
                Type = "function_call_output",
                Payload = BuildForkedSpawnAgentFunctionCallOutputItem(normalizedParentCallId),
            });
        }

        return turn;
    }
}
