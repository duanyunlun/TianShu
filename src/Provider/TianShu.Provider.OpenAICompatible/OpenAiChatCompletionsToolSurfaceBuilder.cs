using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAICompatible;

/// <summary>
/// OpenAI-compatible Chat Completions 工具面构建器。
/// Tool-surface builder for OpenAI-compatible Chat Completions.
/// </summary>
public sealed class OpenAiChatCompletionsToolSurfaceBuilder : IProviderResponsesToolSurfaceBuilder
{
    /// <inheritdoc />
    public string WireApi => ProviderWireApi.OpenAiChatCompletions;

    /// <inheritdoc />
    public IReadOnlyList<object> Build(ProviderResponsesToolSurfaceBuilderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Definitions);

        var tools = new List<object>(context.Definitions.Count);
        foreach (var definition in context.Definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (definition is ProviderResponsesFunctionToolDefinition function)
            {
                tools.Add(BuildFunctionTool(function));
                continue;
            }

            // Chat Completions 官方工具面只接受 function tools；hosted/custom Responses 工具在该协议下不可投影。
            // Official Chat Completions tool payloads only accept function tools; hosted/custom Responses tools are not projectable here.
        }

        return tools;
    }

    private static object BuildFunctionTool(ProviderResponsesFunctionToolDefinition definition)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = definition.Name,
                ["description"] = definition.Description,
                ["parameters"] = JsonSerializer.Deserialize<object?>(definition.InputSchema.GetRawText()),
                ["strict"] = definition.Strict,
            },
        };
}
