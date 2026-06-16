using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Anthropic;

/// <summary>
/// Anthropic Messages 工具面构建器。
/// Tool-surface builder for Anthropic Messages.
/// </summary>
public sealed class AnthropicMessagesToolSurfaceBuilder : IProviderResponsesToolSurfaceBuilder
{
    /// <inheritdoc />
    public string WireApi => ProviderWireApi.AnthropicMessages;

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

            // Anthropic Messages 官方工具面只接受 client tools；OpenAI hosted/custom tools 在这里不可投影。
            // The official Anthropic Messages tool surface accepts client tools only; OpenAI hosted/custom tools are not projectable here.
        }

        return tools;
    }

    private static object BuildFunctionTool(ProviderResponsesFunctionToolDefinition definition)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = definition.Name,
            ["description"] = definition.Description,
            ["input_schema"] = JsonSerializer.Deserialize<object?>(definition.InputSchema.GetRawText()),
        };
}
