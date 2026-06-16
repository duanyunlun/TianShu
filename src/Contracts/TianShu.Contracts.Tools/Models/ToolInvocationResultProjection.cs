using System.Text.Json;

namespace TianShu.Contracts.Tools;

/// <summary>
/// 工具调用结果的模型可消费投影。
/// Model-consumable projection of a tool-invocation result.
/// </summary>
public sealed record ToolInvocationResultProjection
{
    /// <summary>
    /// 初始化工具调用结果投影。
    /// Initializes a tool-invocation result projection.
    /// </summary>
    public ToolInvocationResultProjection(
        bool success,
        string outputText,
        JsonElement structuredOutput,
        ToolInvocationFailure? failure = null,
        IReadOnlyList<ToolOutputContentItem>? outputContentItems = null,
        IReadOnlyList<JsonElement>? rawOutputContentItems = null)
    {
        Success = success;
        OutputText = outputText ?? string.Empty;
        StructuredOutput = structuredOutput.Clone();
        Failure = failure;
        OutputContentItems = outputContentItems is null ? Array.Empty<ToolOutputContentItem>() : outputContentItems.ToArray();
        RawOutputContentItems = rawOutputContentItems is null
            ? Array.Empty<JsonElement>()
            : rawOutputContentItems.Select(static item => item.Clone()).ToArray();
    }

    public bool Success { get; }

    public string OutputText { get; }

    public JsonElement StructuredOutput { get; }

    public ToolInvocationFailure? Failure { get; }

    public IReadOnlyList<ToolOutputContentItem> OutputContentItems { get; }

    public IReadOnlyList<JsonElement> RawOutputContentItems { get; }
}

/// <summary>
/// 工具调用结果投影器，集中维护 ToolUse 输出面向模型的投影策略。
/// Tool-invocation result projector that owns model-facing ToolUse output projection policy.
/// </summary>
public static class ToolInvocationResultProjector
{
    private static readonly HashSet<string> TerminalTextToolKeys = new(StringComparer.Ordinal)
    {
        "tool_search",
        "shell",
        "local_shell",
        "shell_command",
        "exec_command",
        "write_stdin",
        "request_user_input",
        "request_permissions",
        "update_plan",
        "spawn_agent",
        "send_input",
        "resume_agent",
        "wait",
        "close_agent",
        "spawn_agents_on_csv",
        "report_agent_job_result",
        "exec",
        "exec_wait",
        "js_repl",
        "js_repl_reset",
        "artifacts",
        "view_image",
        "test_sync_tool",
        "list_mcp_resources",
        "list_mcp_resource_templates",
        "read_mcp_resource",
        "list_dir",
        "read_file",
        "grep_files",
        "grep",
        "glob",
        "write",
        "apply_patch",
        "tool_suggest",
    };

    /// <summary>
    /// 从正式工具调用结果生成模型可消费投影。
    /// Projects a formal tool-invocation result into a model-consumable shape.
    /// </summary>
    public static ToolInvocationResultProjection Project(ToolInvocationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var structuredOutput = JsonSerializer.SerializeToElement(result);
        if (result.Failure is not null)
        {
            return new ToolInvocationResultProjection(
                success: false,
                outputText: result.Failure.Message,
                structuredOutput,
                result.Failure);
        }

        return new ToolInvocationResultProjection(
            success: true,
            outputText: ResolveOutputText(result),
            structuredOutput,
            outputContentItems: result.OutputContentItems,
            rawOutputContentItems: result.RawOutputContentItems);
    }

    /// <summary>
    /// 判断该工具是否优先把 terminal text 投影为模型可读输出。
    /// Determines whether the tool should project terminal text as model-readable output.
    /// </summary>
    public static bool ShouldProjectTerminalText(string toolKey)
    {
        if (string.IsNullOrWhiteSpace(toolKey))
        {
            return false;
        }

        return TerminalTextToolKeys.Contains(toolKey)
            || toolKey.StartsWith("memory_", StringComparison.Ordinal);
    }

    private static string ResolveOutputText(ToolInvocationResult result)
    {
        if (!ShouldProjectTerminalText(result.ToolKey))
        {
            return JsonSerializer.Serialize(result);
        }

        var terminalText = result.StreamItems
            .LastOrDefault(static item => item.IsTerminal && string.Equals(item.Channel, "text", StringComparison.OrdinalIgnoreCase))
            ?.Payload;
        if (terminalText is null)
        {
            return JsonSerializer.Serialize(result);
        }

        var plain = terminalText.ToPlainObject();
        return plain is string text ? text : JsonSerializer.Serialize(plain);
    }
}
