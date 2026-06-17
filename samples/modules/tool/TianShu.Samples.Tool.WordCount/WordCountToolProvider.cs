using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Samples.Tool.WordCount;

/// <summary>
/// WordCount Tool 示例：演示第三方只读工具的 schema、治理信封和结果投影。
/// WordCount Tool sample that demonstrates schema, governance envelope, and result projection for a third-party read-only tool.
/// </summary>
public sealed class WordCountToolProvider : ITianShuToolProvider
{
    public const string ModuleId = "sample.tool.word_count";
    public const string ToolKey = "sample.word_count";

    public IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context)
        => [WordCountToolHandler.DescriptorValue];

    public ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context)
    {
        if (!string.Equals(toolKey, ToolKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unknown sample tool: {toolKey}");
        }

        return new WordCountToolHandler();
    }

    public static ToolModuleManifest CreateManifest()
        => new(
            ModuleId,
            "Sample Word Count Tool",
            "1.0.0",
            "0.6.0",
            tools:
            [
                new ToolModuleToolBinding(
                    ToolKey,
                    "Word Count",
                    "Counts words and characters in supplied text.",
                    inputSchema: WordCountSchemas.Input,
                    outputSchema: WordCountSchemas.Output,
                    permission: new PermissionDeclaration(["tool.sample.word_count"], requiresHumanGate: false),
                    sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly, ["runtime"], reversible: true),
                    requiresHumanGate: false),
            ],
            diagnostics: ["sample.tool.word_count.access"]);

    public static GovernanceEnvelope CreateGovernance()
        => new(
            "sample-word-count-governance",
            allowedToolIds: [ToolKey],
            allowedModuleIds: [ModuleId],
            maxSideEffectLevel: SideEffectLevel.ReadOnly,
            requiresHumanGate: false);
}

public sealed class WordCountToolHandler : ITianShuToolHandler
{
    public static ToolDescriptor DescriptorValue { get; } = new(
        WordCountToolProvider.ToolKey,
        "Word Count",
        "Counts words and characters in supplied text.",
        inputSchema: WordCountSchemas.Input,
        outputSchema: WordCountSchemas.Output,
        permissions: new PermissionDeclaration(["tool.sample.word_count"], requiresHumanGate: false),
        sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly, ["runtime"], reversible: true),
        audit: new AuditProfile(eventKinds: ["tool.sample.word_count.invoked"]));

    public ToolDescriptor Descriptor => DescriptorValue;

    public ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var text = request.Input.Properties.TryGetValue("text", out var value)
            ? value.StringValue ?? string.Empty
            : string.Empty;
        var words = CountWords(text);
        var result = StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["wordCount"] = words,
            ["characterCount"] = text.Length,
            ["isEmpty"] = string.IsNullOrWhiteSpace(text),
        });

        return ValueTask.FromResult(new ToolInvocationResult(
            request.CallId,
            request.ToolKey,
            streamItems:
            [
                new ToolStreamItem("json", result, isTerminal: true),
            ],
            outputContentItems:
            [
                new ToolOutputContentItem("text", $"words={words}; characters={text.Length}"),
            ]));
    }

    private static int CountWords(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

internal static class WordCountSchemas
{
    public static JsonElement Input { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "text" },
        properties = new
        {
            text = new { type = "string" },
        },
    });

    public static JsonElement Output { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "wordCount", "characterCount", "isEmpty" },
        properties = new
        {
            wordCount = new { type = "integer" },
            characterCount = new { type = "integer" },
            isEmpty = new { type = "boolean" },
        },
    });
}
