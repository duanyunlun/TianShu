using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Tools.Code;

/// <summary>
/// Code / REPL 工具域 Provider。
/// Provider for the Code / REPL tool domain.
/// </summary>
public sealed class CodeToolProvider : ITianShuToolProvider
{
    private static readonly IReadOnlyDictionary<string, ToolDescriptor> Descriptors =
        new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal)
        {
            [CodeToolNames.Exec] = CodeToolDescriptors.BuildDescriptor(
                CodeToolNames.Exec,
                "Exec",
                CodeToolSchemas.ExecDescription,
                CodeToolSchemas.CustomInputWrapperSchema,
                [new ToolCapability("code-mode", "Run governed JavaScript code mode execution.")],
                customInputDefinition: new ToolCustomInputDefinition(CodeToolSchemas.ExecDescription, CodeToolSchemas.ExecInputFormat)),
            [CodeToolNames.ExecWait] = CodeToolDescriptors.BuildDescriptor(
                CodeToolNames.ExecWait,
                "Exec Wait",
                "Waits on a yielded `exec` cell and returns new output or completion.",
                CodeToolSchemas.ExecWaitInputSchema,
                [new ToolCapability("code-mode-wait", "Wait on or terminate a governed code mode cell.")]),
            [CodeToolNames.JsRepl] = CodeToolDescriptors.BuildDescriptor(
                CodeToolNames.JsRepl,
                "JS REPL",
                "Runs JavaScript in a persistent Node kernel with top-level await. This is a freeform tool: send raw JavaScript source text, optionally with a first-line pragma like `// tianshu-js-repl: timeout_ms=15000`; do not send JSON/quotes/markdown fences.",
                CodeToolSchemas.JsReplInputSchema,
                [new ToolCapability("js-repl", "Run governed JavaScript in the persistent REPL kernel.")],
                customInputDefinition: new ToolCustomInputDefinition(
                    "Runs JavaScript in a persistent Node kernel with top-level await.",
                    CodeToolSchemas.JsReplInputFormat)),
            [CodeToolNames.JsReplReset] = CodeToolDescriptors.BuildDescriptor(
                CodeToolNames.JsReplReset,
                "JS REPL Reset",
                "Restarts the js_repl kernel for this run and clears persisted top-level bindings.",
                CodeToolSchemas.EmptyInputSchema,
                [new ToolCapability("js-repl-reset", "Reset the governed JavaScript REPL kernel.")]),
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
            ? new CodeToolHandler(descriptor)
            : throw new InvalidOperationException($"Unknown code tool: {toolKey}");
    }
}

internal static class CodeToolNames
{
    public const string Exec = "exec";
    public const string ExecWait = "exec_wait";
    public const string JsRepl = "js_repl";
    public const string JsReplReset = "js_repl_reset";
    public const string ImplementationId = "tianshu.tools.code";
}

internal sealed class CodeToolHandler : ITianShuToolHandler
{
    public CodeToolHandler(ToolDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public ToolDescriptor Descriptor { get; }

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (context.CodeServices is null)
        {
            return CodeToolResultFactory.Failure(request, "code services unavailable");
        }

        var result = await context.CodeServices
            .InvokeCodeToolAsync(
                new TianShuCodeToolRequest(
                    request.ToolKey,
                    request.Input,
                    CustomInput: CodeToolInput.TryReadString(request.Input, "input")),
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return CodeToolResultFactory.Failure(request, result.OutputText);
        }

        return CodeToolResultFactory.Success(
            request,
            result.StructuredOutput ?? StructuredValue.FromString(result.OutputText),
            result.OutputContentItems,
            result.RawOutputContentItems);
    }
}

internal static class CodeToolDescriptors
{
    public static ToolDescriptor BuildDescriptor(
        string name,
        string displayName,
        string description,
        JsonElement inputSchema,
        IReadOnlyList<ToolCapability> capabilities,
        ToolCustomInputDefinition? customInputDefinition = null)
        => new(
            name,
            displayName,
            description,
            capabilities: capabilities,
            approvalRequirement: ToolApprovalRequirement.None,
            concurrencyClass: ToolConcurrencyClass.Sequential,
            implementationBinding: new ToolImplementationBinding(
                name,
                ToolImplementationKind.Managed,
                implementationId: CodeToolNames.ImplementationId),
            inputSchema: inputSchema,
            customInputDefinition: customInputDefinition);
}

internal static class CodeToolResultFactory
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

internal static class CodeToolInput
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

internal static class CodeToolSchemas
{
    private const string ExecFreeformGrammar = """
start: pragma_source | plain_source
pragma_source: PRAGMA_LINE NEWLINE SOURCE
plain_source: SOURCE

PRAGMA_LINE: /[ \t]*\/\/ @exec:[^\r\n]*/
NEWLINE: /\r?\n/
SOURCE: /[\s\S]+/
""";

    private const string JsReplFreeformGrammar = """
start: pragma_source | plain_source

pragma_source: PRAGMA_LINE NEWLINE js_source
plain_source: PLAIN_JS_SOURCE

js_source: JS_SOURCE

PRAGMA_LINE: /[ \t]*\/\/ tianshu-js-repl:[^\r\n]*/
NEWLINE: /\r?\n/
PLAIN_JS_SOURCE: /(?:\s*)(?:[^\s{\"`]|`[^`]|``[^`])[\s\S]*/
JS_SOURCE: /(?:\s*)(?:[^\s{\"`]|`[^`]|``[^`])[\s\S]*/
""";

    public const string ExecDescription = """
Runs raw JavaScript in an isolated context. Send raw JavaScript source text, not JSON, quoted strings, or markdown code fences. You may optionally start the tool input with a first-line pragma like `// @exec: {"yield_time_ms": 10000, "max_output_tokens": 1000}`.
""";

    public static readonly JsonElement ExecInputFormat = JsonSerializer.SerializeToElement(new
    {
        type = "grammar",
        syntax = "lark",
        definition = ExecFreeformGrammar,
    });

    public static readonly JsonElement JsReplInputFormat = JsonSerializer.SerializeToElement(new
    {
        type = "grammar",
        syntax = "lark",
        definition = JsReplFreeformGrammar,
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

    public static readonly JsonElement ExecWaitInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            cell_id = new { type = "string" },
            yield_time_ms = new { type = "number" },
            max_tokens = new { type = "number" },
            terminate = new { type = "boolean" },
        },
        required = new[] { "cell_id" },
        additionalProperties = false,
    });

    public static readonly JsonElement JsReplInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            code = new { type = "string" },
            timeout_ms = new { type = "integer", minimum = 1 },
            input = new { type = "string" },
        },
        additionalProperties = false,
    });

    public static readonly JsonElement EmptyInputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { },
        additionalProperties = false,
    });
}
