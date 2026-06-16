using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Google;

/// <summary>
/// Google Generative 工具面构建器。
/// Tool-surface builder for Google Generative function calling.
/// </summary>
public sealed class GoogleGenerativeToolSurfaceBuilder : IProviderResponsesToolSurfaceBuilder
{
    /// <inheritdoc />
    public string WireApi => ProviderWireApi.GoogleGenerative;

    /// <inheritdoc />
    public IReadOnlyList<object> Build(ProviderResponsesToolSurfaceBuilderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Definitions);

        var declarations = new List<object>();
        foreach (var definition in context.Definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (definition is ProviderResponsesFunctionToolDefinition function)
            {
                declarations.Add(BuildFunctionDeclaration(function));
                continue;
            }

            // Gemini function calling 只接受 function declarations；OpenAI hosted/custom tools 在这里不可投影。
            // Gemini function calling accepts function declarations only; OpenAI hosted/custom tools are not projectable here.
        }

        if (declarations.Count == 0)
        {
            return [];
        }

        return
        [
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["functionDeclarations"] = declarations,
            },
        ];
    }

    private static object BuildFunctionDeclaration(ProviderResponsesFunctionToolDefinition definition)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = definition.Name,
            ["description"] = definition.Description,
            ["parameters"] = JsonSerializer.Deserialize<object?>(definition.InputSchema.GetRawText()),
        };
}
