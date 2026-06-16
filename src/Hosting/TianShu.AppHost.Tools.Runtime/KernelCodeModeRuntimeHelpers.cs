using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelCodeModeToolReference(
    string ModulePath,
    IReadOnlyList<string> Namespace,
    string ToolKey);

internal static class KernelCodeModeRuntimeHelpers
{
    public static bool ShouldIncludeCodeModeNestedTool(
        IKernelToolHandler handler,
        KernelResponsesNativeToolOptions nativeToolOptions)
    {
        return handler.Name switch
        {
            "shell" => KernelToolRegistry.ResolveVisibleShellToolType(nativeToolOptions) == KernelShellToolType.Default,
            "shell_command" => KernelToolRegistry.ResolveVisibleShellToolType(nativeToolOptions) == KernelShellToolType.ShellCommand,
            "exec" or "exec_wait" or "js_repl" or "js_repl_reset" => false,
            "exec_command" or "write_stdin" => false,
            "view_image" => nativeToolOptions.ViewImageEnabled,
            "artifacts" => nativeToolOptions.ArtifactToolEnabled,
            KernelMcpResourceToolNames.ListResources
                or KernelMcpResourceToolNames.ListResourceTemplates
                or KernelMcpResourceToolNames.ReadResource => nativeToolOptions.McpResourceToolsEnabled,
            "request_permissions" => nativeToolOptions.RequestPermissionsToolEnabled,
            KernelToolDiscoveryToolNames.Search => false,
            KernelToolDiscoveryToolNames.Suggest => nativeToolOptions.ToolSuggestEnabled && nativeToolOptions.SearchToolEnabled,
            "test_sync_tool" => false,
            _ => true,
        };
    }

    public static KernelCodeModeEnabledTool BuildCodeModeEnabledTool(IKernelToolHandler handler)
        => BuildCodeModeEnabledTool(handler, null);

    public static KernelCodeModeEnabledTool BuildCodeModeEnabledTool(
        IKernelToolHandler handler,
        KernelResponsesNativeToolOptions? nativeToolOptions)
    {
        var reference = ResolveCodeModeToolReference(handler.Name);
        var isFreeform = handler is KernelCustomToolHandlerBase;
        var inputSchema = ResolveCodeModeInputSchema(handler, nativeToolOptions);
        return new KernelCodeModeEnabledTool(
            ToolName: handler.Name,
            GlobalName: KernelCodeModeDescriptionBuilder.NormalizeIdentifier(handler.Name),
            ModulePath: reference.ModulePath,
            Namespace: reference.Namespace.ToArray(),
            Name: KernelCodeModeDescriptionBuilder.NormalizeIdentifier(reference.ToolKey),
            Description: KernelCodeModeDescriptionBuilder.BuildNestedToolDescription(
                handler.Description,
                handler.Name,
                isFreeform,
                inputSchema,
                handler.OutputSchema),
            Kind: isFreeform ? "freeform" : "function");
    }

    public static KernelCodeModeToolReference ResolveCodeModeToolReference(string toolName)
    {
        if (TrySplitQualifiedMcpToolName(toolName, out var serverName, out var toolKey))
        {
            return new KernelCodeModeToolReference(
                $"tools/mcp/{serverName}.js",
                new[] { "mcp", serverName! },
                toolKey!);
        }

        return new KernelCodeModeToolReference(
            "tools.js",
            Array.Empty<string>(),
            toolName);
    }

    public static bool TrySplitQualifiedMcpToolName(
        string? qualifiedName,
        out string? serverName,
        out string? toolKey)
    {
        serverName = null;
        toolKey = null;
        var normalized = KernelToolJsonHelpers.Normalize(qualifiedName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        const string delimiter = "__";
        var parts = normalized!.Split([delimiter], StringSplitOptions.None);
        if (parts.Length < 3 || !string.Equals(parts[0], "mcp", StringComparison.Ordinal))
        {
            return false;
        }

        serverName = parts[1];
        toolKey = string.Join(delimiter, parts.Skip(2));
        return !string.IsNullOrWhiteSpace(serverName) && !string.IsNullOrWhiteSpace(toolKey);
    }

    public static JsonElement ConvertKernelToolResultToCodeModeResult(
        string toolName,
        IKernelToolHandler handler,
        KernelToolResult result)
    {
        if (handler is KernelCustomToolHandlerBase)
        {
            return JsonSerializer.SerializeToElement(BuildCodeModeContentString(result));
        }

        return ConvertToolResultToCodeModeResult(toolName, handler.OutputSchema, result, isDynamicTool: false);
    }

    public static JsonElement ConvertToolResultToCodeModeResult(
        string toolName,
        JsonElement? outputSchema,
        KernelToolResult result,
        bool isDynamicTool)
    {
        if (TrySplitQualifiedMcpToolName(toolName, out _, out _))
        {
            return BuildCodeModeMcpResult(result);
        }

        if (result.StructuredOutput is { } structuredOutput)
        {
            return structuredOutput.Clone();
        }

        if (string.Equals(toolName, KernelToolDiscoveryToolNames.Search, StringComparison.Ordinal)
            && TryParseJsonElement(result.OutputText, out var searchPayload)
            && searchPayload.ValueKind == JsonValueKind.Object
            && searchPayload.TryGetProperty("tools", out var tools)
            && tools.ValueKind == JsonValueKind.Array)
        {
            return tools.Clone();
        }

        if (outputSchema is not null && TryParseJsonElement(result.OutputText, out var typedOutput))
        {
            return typedOutput;
        }

        return JsonSerializer.SerializeToElement(BuildCodeModeContentString(result));
    }

    public static bool TryBuildCodeModeFunctionArguments(
        string toolName,
        JsonElement? input,
        out JsonElement arguments,
        out string? error)
    {
        arguments = JsonSerializer.SerializeToElement(new { });
        error = null;
        if (input is null || input.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (input.Value.ValueKind != JsonValueKind.Object)
        {
            error = $"tool `{toolName}` expects a JSON object for arguments";
            return false;
        }

        arguments = input.Value.Clone();
        return true;
    }

    public static bool TryParseJsonElement(string? text, out JsonElement value)
    {
        value = default;
        var normalized = KernelToolJsonHelpers.Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(normalized!);
            value = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static JsonElement? TryFindDynamicToolSchema(
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools,
        string toolName,
        params string[] propertyNames)
    {
        if (!KernelDynamicToolResolver.TryResolveDescriptor(dynamicTools, toolName, out var descriptor))
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            JsonElement? schema = propertyName switch
            {
                "inputSchema" or "input_schema" => descriptor.InputSchema,
                "outputSchema" or "output_schema" => descriptor.OutputSchema,
                _ => null,
            };
            if (schema is not null)
            {
                return schema.Value.Clone();
            }
        }

        return null;
    }

    public static string BuildCodeModeNestedToolCallItemId(string turnId, string requestId, string toolName)
    {
        static char MapChar(char value) => char.IsLetterOrDigit(value) || value is '_' or '-' ? value : '_';

        var safeToolName = string.IsNullOrWhiteSpace(toolName)
            ? "tool"
            : new string(toolName.Select(MapChar).ToArray());
        var safeRequestId = string.IsNullOrWhiteSpace(requestId)
            ? Guid.NewGuid().ToString("N")
            : new string(requestId.Select(MapChar).ToArray());
        return $"codemode_{safeToolName}_{safeRequestId}_{turnId}";
    }

    private static JsonElement ResolveCodeModeInputSchema(
        IKernelToolHandler handler,
        KernelResponsesNativeToolOptions? nativeToolOptions)
    {
        return handler switch
        {
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, "shell", StringComparison.Ordinal) =>
                KernelShellRuntimeSupport.BuildShellModelInputSchema(nativeToolOptions?.ExecPermissionApprovalsEnabled == true),
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, "local_shell", StringComparison.Ordinal) =>
                KernelShellRuntimeSupport.BuildLocalShellModelInputSchema(nativeToolOptions?.ExecPermissionApprovalsEnabled == true),
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, "shell_command", StringComparison.Ordinal) =>
                KernelShellRuntimeSupport.BuildShellCommandModelInputSchema(nativeToolOptions?.ExecPermissionApprovalsEnabled == true),
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, "view_image", StringComparison.Ordinal) =>
                KernelViewImageRuntimeSupport.BuildInputSchema(nativeToolOptions?.ViewImageCanRequestOriginalDetail == true),
            _ => handler.InputSchema,
        };
    }

    private static JsonElement BuildCodeModeMcpResult(KernelToolResult result)
    {
        var payload = new Dictionary<string, object?>
        {
            ["content"] = BuildCodeModeMcpContent(result),
            ["isError"] = !result.Success,
        };
        if (result.StructuredOutput is not null)
        {
            payload["structuredContent"] = ConvertCodeModeJsonElementToObject(result.StructuredOutput);
        }

        if (result.Metadata is not null)
        {
            payload["_meta"] = ConvertCodeModeJsonElementToObject(result.Metadata);
        }

        return JsonSerializer.SerializeToElement(payload);
    }

    private static object[] BuildCodeModeMcpContent(KernelToolResult result)
    {
        if (result.RawOutputContentItems is { Count: > 0 } rawItems)
        {
            return rawItems
                .Select(static item => ConvertCodeModeJsonElementToObject(item) ?? new { })
                .ToArray();
        }

        if (string.IsNullOrWhiteSpace(result.OutputText))
        {
            return [];
        }

        return
        [
            new
            {
                type = "text",
                text = result.OutputText,
            },
        ];
    }

    private static string BuildCodeModeContentString(KernelToolResult result)
    {
        if (result.OutputContentItems is { Count: > 0 } outputItems)
        {
            var content = string.Join(
                "\n",
                outputItems
                    .Select(ConvertCodeModeContentItemToString)
                    .Where(static value => !string.IsNullOrWhiteSpace(value)));
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        if (result.RawOutputContentItems is { Count: > 0 } rawItems)
        {
            var content = string.Join(
                "\n",
                rawItems
                    .Select(ConvertCodeModeRawContentItemToString)
                    .Where(static value => !string.IsNullOrWhiteSpace(value)));
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        return result.OutputText ?? string.Empty;
    }

    private static string? ConvertCodeModeContentItemToString(KernelToolOutputContentItem item)
    {
        return KernelToolJsonHelpers.Normalize(
            string.Equals(item.Type, "input_image", StringComparison.OrdinalIgnoreCase)
                ? item.ImageUrl
                : item.Text);
    }

    private static string? ConvertCodeModeRawContentItemToString(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var type = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "type"));
        if (string.Equals(type, "input_image", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "image", StringComparison.OrdinalIgnoreCase))
        {
            return KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "image_url") ?? KernelToolJsonHelpers.ReadString(item, "imageUrl"));
        }

        return KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(item, "text"));
    }

    private static object? ConvertCodeModeJsonElementToObject(JsonElement? element)
    {
        if (element is not JsonElement value)
        {
            return null;
        }

        return JsonSerializer.Deserialize<object?>(value.GetRawText());
    }
}
