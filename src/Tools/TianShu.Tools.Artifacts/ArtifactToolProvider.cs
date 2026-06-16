using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Tools.Artifacts;

/// <summary>
/// Artifact / View 工具域 Provider。
/// Provider for the Artifact / View tool domain.
/// </summary>
public sealed class ArtifactToolProvider : ITianShuToolProvider
{
    private static readonly IReadOnlyDictionary<string, ToolDescriptor> Descriptors =
        new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal)
        {
            [ArtifactToolNames.Artifacts] = ArtifactToolDescriptors.BuildDescriptor(
                ArtifactToolNames.Artifacts,
                "Artifacts",
                ArtifactToolSchemas.ArtifactsDescription,
                ArtifactToolSchemas.CustomInputWrapperSchema,
                ToolApprovalRequirement.Required,
                ToolConcurrencyClass.Exclusive,
                [new ToolCapability("artifact-runtime", "Run governed artifact JavaScript through the host runtime.")],
                customInputDefinition: new ToolCustomInputDefinition(
                    ArtifactToolSchemas.ArtifactsDescription,
                    ArtifactToolSchemas.ArtifactsInputFormat)),
            [ArtifactToolNames.ViewImage] = ArtifactToolDescriptors.BuildDescriptor(
                ArtifactToolNames.ViewImage,
                "View Image",
                "View a local image from the filesystem (only use if given a full filepath by the user, and the image isn't already attached to the thread context within <image ...> tags).",
                ArtifactToolSchemas.ViewImageInputSchema,
                ToolApprovalRequirement.None,
                ToolConcurrencyClass.SharedReadOnly,
                [new ToolCapability("image-input", "Attach a governed local image as model input content.")]),
        };

    public IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context)
    {
        _ = context;
        return Descriptors.Values.ToArray();
    }

    public ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context)
    {
        _ = context;
        return Descriptors.TryGetValue(toolKey, out var descriptor)
            ? new ArtifactToolHandler(descriptor)
            : throw new InvalidOperationException($"Unknown artifact tool: {toolKey}");
    }
}

internal static class ArtifactToolNames
{
    public const string Artifacts = "artifacts";
    public const string ViewImage = "view_image";
    public const string ImplementationId = "tianshu.tools.artifacts";
}

internal sealed class ArtifactToolHandler : ITianShuToolHandler
{
    public ArtifactToolHandler(ToolDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public ToolDescriptor Descriptor { get; }

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (context.ArtifactServices is null)
        {
            return ArtifactToolResultFactory.Failure(request, "artifact services unavailable");
        }

        var result = await context.ArtifactServices
            .InvokeArtifactToolAsync(
                new TianShuArtifactToolRequest(
                    request.ToolKey,
                    request.Input,
                    CustomInput: ArtifactToolInput.TryReadString(request.Input, "input")),
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return ArtifactToolResultFactory.Failure(request, result.OutputText);
        }

        return ArtifactToolResultFactory.Success(
            request,
            result.StructuredOutput ?? StructuredValue.FromString(result.OutputText),
            result.OutputContentItems,
            result.RawOutputContentItems);
    }
}

internal static class ArtifactToolDescriptors
{
    public static ToolDescriptor BuildDescriptor(
        string name,
        string displayName,
        string description,
        JsonElement inputSchema,
        ToolApprovalRequirement approvalRequirement,
        ToolConcurrencyClass concurrencyClass,
        IReadOnlyList<ToolCapability> capabilities,
        ToolCustomInputDefinition? customInputDefinition = null)
        => new(
            name,
            displayName,
            description,
            capabilities: capabilities,
            approvalRequirement: approvalRequirement,
            concurrencyClass: concurrencyClass,
            implementationBinding: new ToolImplementationBinding(
                name,
                ToolImplementationKind.Managed,
                implementationId: ArtifactToolNames.ImplementationId),
            inputSchema: inputSchema,
            customInputDefinition: customInputDefinition);
}

internal static class ArtifactToolResultFactory
{
    public static ToolInvocationResult Success(
        ToolInvocationRequest request,
        StructuredValue payload,
        IReadOnlyList<ToolOutputContentItem>? outputContentItems,
        IReadOnlyList<JsonElement>? rawOutputContentItems)
        => new(
            request.CallId,
            request.ToolKey,
            [new ToolStreamItem("text", payload, isTerminal: true)],
            outputContentItems: outputContentItems,
            rawOutputContentItems: rawOutputContentItems);

    public static ToolInvocationResult Failure(ToolInvocationRequest request, string message)
        => new(
            request.CallId,
            request.ToolKey,
            failure: new ToolInvocationFailure($"{request.ToolKey}.invalid_request", message));
}

internal static class ArtifactToolInput
{
    public static string? TryReadString(StructuredValue input, string propertyName)
    {
        if (input.Kind != StructuredValueKind.Object
            || !input.TryGetProperty(propertyName, out var value)
            || value is null
            || value.Kind != StructuredValueKind.String)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value.StringValue) ? null : value.StringValue;
    }
}

internal static class ArtifactToolSchemas
{
    private const string ArtifactsFreeformGrammar = """
start: pragma_source | plain_source

pragma_source: PRAGMA_LINE NEWLINE js_source
plain_source: PLAIN_JS_SOURCE

js_source: JS_SOURCE

PRAGMA_LINE: /[ \t]*\/\/ tianshu-artifacts:[^\r\n]*/ | /[ \t]*\/\/ tianshu-artifact-tool:[^\r\n]*/
NEWLINE: /\r?\n/
PLAIN_JS_SOURCE: /(?:\s*)(?:[^\s{\"`]|`[^`]|``[^`])[\s\S]*/
JS_SOURCE: /(?:\s*)(?:[^\s{\"`]|`[^`]|``[^`])[\s\S]*/
""";

    public const string ArtifactsDescription = """
Runs raw JavaScript against the preinstalled TianShu artifact runtime for creating presentations or spreadsheets. This is plain JavaScript with top-level await, not TypeScript. Omit the import line because the package surface is already preloaded on globalThis as named exports, artifactTool and artifacts. This is a freeform tool: send raw JavaScript source text, optionally with a first-line pragma like `// tianshu-artifacts: timeout_ms=15000` or `// tianshu-artifact-tool: timeout_ms=15000`; do not send JSON, quoted code, or markdown fences.
""";

    public static readonly JsonElement ArtifactsInputFormat = JsonSerializer.SerializeToElement(new
    {
        type = "grammar",
        syntax = "lark",
        definition = ArtifactsFreeformGrammar,
    });

    public static readonly JsonElement CustomInputWrapperSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            input = new { type = "string" },
        },
        required = new[] { "input" },
        additionalProperties = false,
    });

    public static readonly JsonElement ViewImageInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            path = new
            {
                type = "string",
                description = "Local filesystem path to an image file",
            },
            detail = new
            {
                type = "string",
                description = "Optional detail override. The only supported value is `original`; omit this field for default resized behavior. Use `original` to preserve the file's original resolution instead of resizing to fit.",
            },
        },
        required = new[] { "path" },
        additionalProperties = false,
    });
}
