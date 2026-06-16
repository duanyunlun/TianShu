using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelJsReplRuntimeSupport
{
    private const string FreeformGrammar = """
start: pragma_source | plain_source

pragma_source: PRAGMA_LINE NEWLINE js_source
plain_source: PLAIN_JS_SOURCE

js_source: JS_SOURCE

PRAGMA_LINE: /[ \t]*\/\/ tianshu-js-repl:[^\r\n]*/
NEWLINE: /\r?\n/
PLAIN_JS_SOURCE: /(?:\s*)(?:[^\s{\"`]|`[^`]|``[^`])[\s\S]*/
JS_SOURCE: /(?:\s*)(?:[^\s{\"`]|`[^`]|``[^`])[\s\S]*/
""";

    private static readonly JsonElement InputFormat = JsonSerializer.SerializeToElement(new
    {
        type = "grammar",
        syntax = "lark",
        definition = FreeformGrammar,
    });

    private static readonly JsonElement JsReplInputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            code = new { type = "string" },
            timeout_ms = new { type = "integer", minimum = 1 },
        },
        required = new[] { "code" },
        additionalProperties = false,
    });

    private static readonly JsonElement JsReplResetInputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { },
        additionalProperties = false,
    });

    internal const string JsReplDescription =
        "Runs JavaScript in a persistent Node kernel with top-level await. This is a freeform tool: send raw JavaScript source text, optionally with a first-line pragma like `// tianshu-js-repl: timeout_ms=15000`; do not send JSON/quotes/markdown fences.";

    internal const string JsReplResetDescription =
        "Restarts the js_repl kernel for this run and clears persisted top-level bindings.";

    internal static ProviderResponsesToolDefinition BuildJsReplProviderToolDefinition()
        => new ProviderResponsesCustomToolDefinition(
            "js_repl",
            JsReplDescription,
            InputFormat);

    internal static JsonElement JsReplInputSchema => JsReplInputSchemaElement.Clone();

    internal static JsonElement JsReplResetInputSchema => JsReplResetInputSchemaElement.Clone();

    internal static KernelJsReplParseResult ParseFreeformInput(string input)
    {
        var normalized = input ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return KernelJsReplParseResult.FromError("js_repl expects raw JavaScript tool input (non-empty). Provide JS source text, optionally with first-line `// tianshu-js-repl: ...`.");
        }

        var firstNewline = normalized.IndexOf('\n');
        if (firstNewline < 0)
        {
            var validationError = KernelJsReplManager.ValidateSource(normalized);
            return validationError is null
                ? KernelJsReplParseResult.FromRequest(new KernelJsReplExecutionRequest(normalized, null))
                : KernelJsReplParseResult.FromError(validationError);
        }

        var firstLine = normalized[..firstNewline];
        var remainder = normalized[(firstNewline + 1)..];
        var trimmedFirstLine = firstLine.TrimStart();
        if (!trimmedFirstLine.StartsWith("// tianshu-js-repl:", StringComparison.Ordinal))
        {
            var validationError = KernelJsReplManager.ValidateSource(normalized);
            return validationError is null
                ? KernelJsReplParseResult.FromRequest(new KernelJsReplExecutionRequest(normalized, null))
                : KernelJsReplParseResult.FromError(validationError);
        }

        var pragma = trimmedFirstLine["// tianshu-js-repl:".Length..].Trim();
        int? timeoutMs = null;
        if (!string.IsNullOrWhiteSpace(pragma))
        {
            foreach (var token in pragma.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = token.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    return KernelJsReplParseResult.FromError($"js_repl pragma expects space-separated key=value pairs (supported keys: timeout_ms); got `{token}`");
                }

                if (!string.Equals(parts[0], "timeout_ms", StringComparison.Ordinal))
                {
                    return KernelJsReplParseResult.FromError($"js_repl pragma only supports timeout_ms; got `{parts[0]}`");
                }

                if (timeoutMs is not null)
                {
                    return KernelJsReplParseResult.FromError("js_repl pragma specifies timeout_ms more than once");
                }

                if (!int.TryParse(parts[1], out var parsedTimeout) || parsedTimeout <= 0)
                {
                    return KernelJsReplParseResult.FromError($"js_repl pragma timeout_ms must be an integer; got `{parts[1]}`");
                }

                timeoutMs = parsedTimeout;
            }
        }

        var trimmedCode = remainder.TrimStart('\r', '\n');
        var validation = KernelJsReplManager.ValidateSource(trimmedCode);
        return validation is null
            ? KernelJsReplParseResult.FromRequest(new KernelJsReplExecutionRequest(trimmedCode, timeoutMs))
            : KernelJsReplParseResult.FromError(validation);
    }
}

internal sealed record KernelJsReplParseResult(KernelJsReplExecutionRequest? Request, string? Error)
{
    public bool Success => Request is not null && string.IsNullOrWhiteSpace(Error);

    public static KernelJsReplParseResult FromRequest(KernelJsReplExecutionRequest request) => new(request, null);

    public static KernelJsReplParseResult FromError(string error) => new(null, error);
}

