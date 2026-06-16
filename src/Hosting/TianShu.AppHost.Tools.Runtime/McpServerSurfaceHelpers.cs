using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record McpServerToolCallResult(
    string Content,
    bool IsError,
    string? ThreadId = null);

internal static class McpServerSurfaceHelpers
{
    public const string McpServerTianShuToolName = "tianshu";
    public const string McpServerTianShuReplyToolName = "tianshu-reply";
    public const string MissingTianShuToolArgumentsMessage = "Missing arguments for tianshu tool-call; the `prompt` field is required.";
    public const string MissingTianShuReplyToolArgumentsMessage = "Missing arguments for tianshu-reply tool-call; the `thread_id` and `prompt` fields are required.";

    public static object CreateMcpServerToolCallPayload(McpServerToolCallResult result)
    {
        var payload = new Dictionary<string, object?>
        {
            ["content"] = CreateMcpServerTextContent(result.Content),
            ["isError"] = result.IsError,
        };

        if (!string.IsNullOrWhiteSpace(result.ThreadId))
        {
            payload["structuredContent"] = new
            {
                threadId = result.ThreadId,
                content = result.Content,
            };
        }

        return payload;
    }

    public static object CreateMcpServerTianShuToolDefinition()
    {
        return new
        {
            name = McpServerTianShuToolName,
            title = "TianShu",
            description = "Run a TianShu session. Accepts configuration parameters matching the TianShu config surface.",
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object?>
                {
                    ["prompt"] = new { type = "string" },
                    ["model"] = new { type = "string" },
                    ["profile"] = new { type = "string" },
                    ["cwd"] = new { type = "string" },
                    ["approval-policy"] = new
                    {
                        type = "string",
                        @enum = new[] { "untrusted", "on-failure", "on-request", "never" },
                    },
                    ["sandbox"] = new
                    {
                        type = "string",
                        @enum = new[] { "read-only", "workspace-write", "danger-full-access" },
                    },
                    ["config"] = new { type = "object" },
                    ["base-instructions"] = new { type = "string" },
                    ["developer-instructions"] = new { type = "string" },
                    ["compact-prompt"] = new { type = "string" },
                },
                required = new[] { "prompt" },
            },
            outputSchema = CreateMcpServerToolOutputSchema(),
        };
    }

    public static object CreateMcpServerTianShuReplyToolDefinition()
    {
        return new
        {
            name = McpServerTianShuReplyToolName,
            title = "TianShu Reply",
            description = "Continue a TianShu conversation by providing the thread id and prompt.",
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object?>
                {
                    ["conversationId"] = new { type = "string" },
                    ["threadId"] = new { type = "string" },
                    ["prompt"] = new { type = "string" },
                },
                required = new[] { "prompt" },
            },
            outputSchema = CreateMcpServerToolOutputSchema(),
        };
    }

    public static KernelApprovalPolicy? TryReadMcpApprovalPolicy(string? value)
    {
        var normalized = KernelToolJsonHelpers.Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return KernelApprovalPolicy.TryParse(normalized, out var approvalPolicy)
            ? approvalPolicy
            : throw new InvalidOperationException($"Unsupported approval policy: {value}");
    }

    public static KernelSandboxPolicyOverride? TryReadMcpSandboxOverride(string? value)
    {
        var normalized = KernelToolJsonHelpers.Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var mappedMode = normalized.ToLowerInvariant() switch
        {
            "read-only" => "readOnly",
            "workspace-write" => "workspaceWrite",
            "danger-full-access" => "danger-full-access",
            _ => throw new InvalidOperationException($"Unsupported sandbox mode: {value}"),
        };

        return KernelSandboxPolicyOverride.FromMode(mappedMode);
    }

    public static string? ResolveMcpToolCwd(string? cwd)
    {
        var normalized = KernelToolJsonHelpers.Normalize(cwd);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return Path.GetFullPath(normalized);
    }

    public static string? ReadMcpTianShuReplyThreadId(JsonElement arguments)
        => KernelToolJsonHelpers.ReadString(arguments, "threadId")
           ?? KernelToolJsonHelpers.ReadString(arguments, "thread_id")
           ?? KernelToolJsonHelpers.ReadString(arguments, "conversationId")
           ?? KernelToolJsonHelpers.ReadString(arguments, "conversation_id");

    public static string NormalizeMcpServerToolContent(string prompt, string assistantText)
    {
        if (!KernelToolRuntimeParsingHelpers.TryParseInlineToolCall(prompt, out var toolName, out _))
        {
            return assistantText;
        }

        var normalized = assistantText.Replace("\r\n", "\n", StringComparison.Ordinal);
        var prefix = $"工具执行结果\ntool: {toolName}\noutput:\n";
        return normalized.StartsWith(prefix, StringComparison.Ordinal)
            ? normalized[prefix.Length..]
            : assistantText;
    }

    private static object[] CreateMcpServerTextContent(string text)
        =>
        [
            new
            {
                type = "text",
                text,
            },
        ];

    private static object CreateMcpServerToolOutputSchema()
    {
        return new
        {
            type = "object",
            properties = new
            {
                threadId = new { type = "string" },
                content = new { type = "string" },
            },
            required = new[] { "threadId", "content" },
        };
    }
}
