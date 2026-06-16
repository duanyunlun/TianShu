using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelArtifactsRuntimeSupport
{
    private const string FreeformGrammar = """
start: pragma_source | plain_source

pragma_source: PRAGMA_LINE NEWLINE js_source
plain_source: PLAIN_JS_SOURCE

js_source: JS_SOURCE

PRAGMA_LINE: /[ \t]*\/\/ tianshu-artifacts:[^\r\n]*/ | /[ \t]*\/\/ tianshu-artifact-tool:[^\r\n]*/
NEWLINE: /\r?\n/
PLAIN_JS_SOURCE: /(?:\s*)(?:[^\s{\"`]|`[^`]|``[^`])[\s\S]*/
JS_SOURCE: /(?:\s*)(?:[^\s{\"`]|`[^`]|``[^`])[\s\S]*/
""";

    public static readonly JsonElement InputFormat = JsonSerializer.SerializeToElement(new
    {
        type = "grammar",
        syntax = "lark",
        definition = FreeformGrammar,
    });

    public static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new
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

    public const string Description = "Runs raw JavaScript against the preinstalled TianShu artifact runtime for creating presentations or spreadsheets. This is plain JavaScript with top-level await, not TypeScript. Omit the import line because the package surface is already preloaded on globalThis as named exports, artifactTool and artifacts. This is a freeform tool: send raw JavaScript source text, optionally with a first-line pragma like `// tianshu-artifacts: timeout_ms=15000` or `// tianshu-artifact-tool: timeout_ms=15000`; do not send JSON, quoted code, or markdown fences.";

    public static async Task<KernelToolResult> ExecuteAsync(JsonElement arguments, KernelToolCallContext context, CancellationToken cancellationToken)
    {
        var code = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "code"));
        if (string.IsNullOrWhiteSpace(code))
        {
            return Failure("artifacts expects raw JavaScript source text (non-empty) authored against the preloaded TianShu artifact surface. Provide JS only, optionally with first-line `// tianshu-artifacts: timeout_ms=15000` or `// tianshu-artifact-tool: timeout_ms=15000`.");
        }

        var timeoutMs = KernelToolJsonHelpers.ReadInt(arguments, "timeout_ms");
        return await ExecuteCoreAsync(new KernelArtifactsExecutionRequest(code!, timeoutMs), context, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<KernelToolResult> ExecuteCustomAsync(string input, KernelToolCallContext context, CancellationToken cancellationToken)
    {
        var parseResult = ParseFreeformInput(input);
        if (!parseResult.Success || parseResult.Request is null)
        {
            return Failure(parseResult.Error ?? "artifacts 输入无效。");
        }

        return await ExecuteCoreAsync(parseResult.Request, context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<KernelToolResult> ExecuteCoreAsync(
        KernelArtifactsExecutionRequest request,
        KernelToolCallContext context,
        CancellationToken cancellationToken)
    {
        if (context.RuntimeServices?.ExecuteArtifacts is null)
        {
            return Failure("artifacts is unavailable");
        }

        var result = await context.RuntimeServices.ExecuteArtifacts(request, cancellationToken).ConfigureAwait(false);
        return new KernelToolResult(result.Success, result.Output);
    }

    internal static KernelArtifactsParseResult ParseFreeformInput(string input)
    {
        var normalized = input ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return KernelArtifactsParseResult.FromError("artifacts expects raw JavaScript source text (non-empty) authored against the preloaded TianShu artifact surface. Provide JS only, optionally with first-line `// tianshu-artifacts: timeout_ms=15000` or `// tianshu-artifact-tool: timeout_ms=15000`.");
        }

        var firstNewline = normalized.IndexOf('\n');
        if (firstNewline < 0)
        {
            var validationError = KernelArtifactsRuntimeManager.ValidateSource(normalized);
            return validationError is null
                ? KernelArtifactsParseResult.FromRequest(new KernelArtifactsExecutionRequest(normalized, null))
                : KernelArtifactsParseResult.FromError(validationError);
        }

        var firstLine = normalized[..firstNewline];
        var remainder = normalized[(firstNewline + 1)..];
        var trimmedFirstLine = firstLine.TrimStart();
        if (!trimmedFirstLine.StartsWith("// tianshu-artifacts:", StringComparison.Ordinal)
            && !trimmedFirstLine.StartsWith("// tianshu-artifact-tool:", StringComparison.Ordinal))
        {
            var validationError = KernelArtifactsRuntimeManager.ValidateSource(normalized);
            return validationError is null
                ? KernelArtifactsParseResult.FromRequest(new KernelArtifactsExecutionRequest(normalized, null))
                : KernelArtifactsParseResult.FromError(validationError);
        }

        var pragmaPrefix = trimmedFirstLine.StartsWith("// tianshu-artifact-tool:", StringComparison.Ordinal)
            ? "// tianshu-artifact-tool:"
            : "// tianshu-artifacts:";
        var pragma = trimmedFirstLine[pragmaPrefix.Length..].Trim();
        int? timeoutMs = null;
        if (!string.IsNullOrWhiteSpace(pragma))
        {
            foreach (var token in pragma.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = token.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    return KernelArtifactsParseResult.FromError($"artifacts pragma expects space-separated key=value pairs (supported keys: timeout_ms); got `{token}`");
                }

                if (!string.Equals(parts[0], "timeout_ms", StringComparison.Ordinal))
                {
                    return KernelArtifactsParseResult.FromError($"artifacts pragma only supports timeout_ms; got `{parts[0]}`");
                }

                if (timeoutMs is not null)
                {
                    return KernelArtifactsParseResult.FromError("artifacts pragma specifies timeout_ms more than once");
                }

                if (!int.TryParse(parts[1], out var parsedTimeout) || parsedTimeout <= 0)
                {
                    return KernelArtifactsParseResult.FromError($"artifacts pragma timeout_ms must be an integer; got `{parts[1]}`");
                }

                timeoutMs = parsedTimeout;
            }
        }

        if (string.IsNullOrWhiteSpace(remainder.Trim()))
        {
            return KernelArtifactsParseResult.FromError("artifacts pragma must be followed by JavaScript source on subsequent lines");
        }

        var trimmedCode = remainder.TrimStart('\r', '\n');
        var validation = KernelArtifactsRuntimeManager.ValidateSource(trimmedCode);
        return validation is null
            ? KernelArtifactsParseResult.FromRequest(new KernelArtifactsExecutionRequest(trimmedCode, timeoutMs))
            : KernelArtifactsParseResult.FromError(validation);
    }

    internal sealed record KernelArtifactsParseResult(KernelArtifactsExecutionRequest? Request, string? Error)
    {
        public bool Success => Request is not null && string.IsNullOrWhiteSpace(Error);

        public static KernelArtifactsParseResult FromRequest(KernelArtifactsExecutionRequest request) => new(request, null);

        public static KernelArtifactsParseResult FromError(string error) => new(null, error);
    }

    private static KernelToolResult Failure(string message) => new(false, message);
}
