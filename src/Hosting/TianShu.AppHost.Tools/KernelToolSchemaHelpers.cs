using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 协作工具输入/输出 schema 与共享参数解析辅助方法。
/// Helpers for collaboration tool schemas and shared argument parsing.
/// </summary>
internal static class ToolSchemaHelpers
{
    public static JsonElement BuildCollaborationAgentStatusSchema()
    {
        return ResumeAgentToolHandlerStatusSchema();
    }

    public static object BuildCollabInputItemsArraySchema()
    {
        return new
        {
            type = "array",
            items = new
            {
                type = "object",
                properties = new
                {
                    type = new { type = "string" },
                    text = new { type = "string" },
                    name = new { type = "string" },
                    path = new { type = "string" },
                    image_url = new { type = "string" },
                    imageUrl = new { type = "string" },
                },
                additionalProperties = false,
            },
        };
    }

    public static KernelSpawnAgentRequest? ParseSpawnAgentRequest(JsonElement arguments, out string? error)
    {
        error = null;
        var input = ParseSharedInput(arguments, out error);
        if (input is null)
        {
            return null;
        }

        return new KernelSpawnAgentRequest(
            input.Value.Message,
            input.Value.Items,
            KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "agent_type")),
            KernelToolJsonHelpers.ReadBool(arguments, "fork_context") ?? false,
            KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "model")),
            KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "reasoning_effort")));
    }

    public static (string? Message, IReadOnlyList<KernelCollabInputItem>? Items)? ParseSharedInput(JsonElement arguments, out string? error)
    {
        error = null;
        var message = KernelToolJsonHelpers.ReadString(arguments, "message");
        var items = ParseCollabInputItems(arguments, "items");
        if (message is not null && items is not null)
        {
            error = "Provide either message or items, but not both";
            return null;
        }

        if (message is null && items is null)
        {
            error = "Provide one of: message or items";
            return null;
        }

        if (message is not null && string.IsNullOrWhiteSpace(message))
        {
            error = "Empty message can't be sent to an agent";
            return null;
        }

        if (items is not null && items.Count == 0)
        {
            error = "Items can't be empty";
            return null;
        }

        return (KernelToolJsonHelpers.Normalize(message), items);
    }

    public static IReadOnlyList<string> ReadStringArray(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<string>();
        foreach (var element in property.EnumerateArray())
        {
            var value = element.ValueKind == JsonValueKind.String ? KernelToolJsonHelpers.Normalize(element.GetString()) : null;
            if (!string.IsNullOrWhiteSpace(value))
            {
                results.Add(value!);
            }
        }

        return results;
    }

    public static string BuildInputPreview(IReadOnlyList<KernelCollabInputItem> items)
    {
        var parts = new List<string>(items.Count);
        foreach (var item in items)
        {
            var kind = KernelToolJsonHelpers.Normalize(item.Type) ?? "text";
            switch (kind)
            {
                case "text":
                    parts.Add(KernelToolJsonHelpers.Normalize(item.Text) ?? string.Empty);
                    break;
                case "image":
                    parts.Add("[image]");
                    break;
                case "local_image":
                    parts.Add($"[local_image:{KernelToolJsonHelpers.Normalize(item.Path) ?? string.Empty}]");
                    break;
                case "skill":
                    parts.Add($"[skill:${KernelToolJsonHelpers.Normalize(item.Name) ?? string.Empty}]({KernelToolJsonHelpers.Normalize(item.Path) ?? string.Empty})");
                    break;
                case "mention":
                    parts.Add($"[mention:${KernelToolJsonHelpers.Normalize(item.Name) ?? string.Empty}]({KernelToolJsonHelpers.Normalize(item.Path) ?? string.Empty})");
                    break;
                default:
                    parts.Add("[input]");
                    break;
            }
        }

        return string.Join(Environment.NewLine, parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static IReadOnlyList<KernelCollabInputItem>? ParseCollabInputItems(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<KernelCollabInputItem>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new KernelCollabInputItem(
                Type: KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "type")),
                Text: KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "text")),
                Name: KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "name")),
                Path: KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "path")),
                ImageUrl: KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "image_url") ?? KernelToolJsonHelpers.ReadString(item, "imageUrl"))));
        }

        return items;
    }

    private static JsonElement ResumeAgentToolHandlerStatusSchema()
    {
        return JsonSerializer.SerializeToElement(new
        {
            oneOf = new object[]
            {
                new
                {
                    type = "string",
                    @enum = new[] { "pending_init", "running", "shutdown", "not_found" },
                },
                new
                {
                    type = "object",
                    properties = new
                    {
                        completed = new
                        {
                            oneOf = new object[]
                            {
                                new { type = "string" },
                                new { type = "null" },
                            },
                        },
                    },
                    required = new[] { "completed" },
                    additionalProperties = false,
                },
                new
                {
                    type = "object",
                    properties = new
                    {
                        errored = new { type = "string" },
                    },
                    required = new[] { "errored" },
                    additionalProperties = false,
                },
            },
        });
    }
}
