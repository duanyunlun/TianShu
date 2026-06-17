using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using static TianShu.Tools.McpResources.McpResourceToolHandlerImports;

namespace TianShu.Tools.McpResources;

/// <summary>
/// MCP Resource 工具域 Provider。
/// Provider for the MCP Resource tool domain.
/// </summary>
public sealed class McpResourceToolProvider : ITianShuToolProvider
{
    private readonly IReadOnlyDictionary<string, ToolDescriptor> mcpToolDescriptors;
    private readonly IReadOnlyDictionary<string, TianShuMcpToolDescriptor> mcpToolBindings;

    public McpResourceToolProvider()
        : this(null)
    {
    }

    public McpResourceToolProvider(IReadOnlyList<TianShuMcpToolDescriptor>? mcpToolBindings = null)
    {
        var validBindings = (mcpToolBindings ?? Array.Empty<TianShuMcpToolDescriptor>())
            .Where(static binding => IsValidInputSchema(binding.InputSchema))
            .ToArray();
        this.mcpToolBindings = validBindings.ToDictionary(static binding => binding.ToolId, StringComparer.Ordinal);
        mcpToolDescriptors = validBindings
            .Select(static binding => CreateMcpToolDescriptor(binding))
            .ToDictionary(static descriptor => descriptor.ToolId, StringComparer.Ordinal);
    }

    private static readonly IReadOnlyDictionary<string, ToolDescriptor> Descriptors =
        new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal)
        {
            [McpResourceToolNames.ListResources] = ListMcpResourcesToolHandler.DescriptorInstance,
            [McpResourceToolNames.ListResourceTemplates] = ListMcpResourceTemplatesToolHandler.DescriptorInstance,
            [McpResourceToolNames.ReadResource] = ReadMcpResourceToolHandler.DescriptorInstance,
        };

    public IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context)
    {
        _ = context;
        return Descriptors.Values.Concat(mcpToolDescriptors.Values).ToArray();
    }

    public ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context)
    {
        _ = context;
        return toolKey switch
        {
            McpResourceToolNames.ListResources => new ListMcpResourcesToolHandler(),
            McpResourceToolNames.ListResourceTemplates => new ListMcpResourceTemplatesToolHandler(),
            McpResourceToolNames.ReadResource => new ReadMcpResourceToolHandler(),
            _ when mcpToolDescriptors.TryGetValue(toolKey, out var descriptor)
                   && mcpToolBindings.TryGetValue(toolKey, out var binding) => new McpToolHandler(descriptor, binding),
            _ => throw new InvalidOperationException($"Unknown MCP resource tool: {toolKey}"),
        };
    }

    private static bool IsValidInputSchema(JsonElement schema)
        => schema.ValueKind == JsonValueKind.Object
           && schema.TryGetProperty("type", out var type)
           && string.Equals(type.GetString(), "object", StringComparison.Ordinal);

    private static ToolDescriptor CreateMcpToolDescriptor(TianShuMcpToolDescriptor binding)
    {
        var sideEffectLevel = binding.SideEffectLevel == SideEffectLevel.Unspecified
            ? SideEffectLevel.ExternalMutation
            : binding.SideEffectLevel;
        var approvalRequirement = binding.RequiresHumanGate
            ? ToolApprovalRequirement.Required
            : ToolApprovalRequirement.None;
        var concurrencyClass = sideEffectLevel <= SideEffectLevel.ReadOnly
            ? ToolConcurrencyClass.SharedReadOnly
            : ToolConcurrencyClass.Sequential;
        return new ToolDescriptor(
            binding.ToolId,
            binding.DisplayName,
            binding.Description,
            capabilities: [new ToolCapability("mcp-tool", $"Invoke MCP tool {binding.ServerId}/{binding.ToolName}.")],
            approvalRequirement: approvalRequirement,
            concurrencyClass: concurrencyClass,
            implementationBinding: new ToolImplementationBinding(
                binding.ToolId,
                binding.ImplementationKind,
                implementationId: $"{McpResourceToolNames.McpToolImplementationId}:{binding.ServerId}"),
            inputSchema: binding.InputSchema,
            outputSchema: binding.OutputSchema,
            permissions: new PermissionDeclaration(binding.RequiredScopes, binding.RequiresHumanGate),
            sideEffects: new SideEffectProfile(
                sideEffectLevel,
                sideEffectLevel <= SideEffectLevel.ReadOnly ? ["mcp-resource"] : ["mcp-tool", "remote"],
                reversible: sideEffectLevel <= SideEffectLevel.ReadOnly,
                requiresAudit: true));
    }
}

internal static class McpResourceToolNames
{
    public const string ListResources = "list_mcp_resources";
    public const string ListResourceTemplates = "list_mcp_resource_templates";
    public const string ReadResource = "read_mcp_resource";
    public const string ImplementationId = "tianshu.tools.mcp-resources";
    public const string McpToolImplementationId = "tianshu.tools.mcp-tool";
}

internal sealed class ListMcpResourcesToolHandler : ITianShuToolHandler
{
    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            server = new
            {
                type = "string",
                description = "Optional MCP server name. When omitted, lists resources from every configured server.",
            },
            cursor = new
            {
                type = "string",
                description = "Opaque cursor returned by a previous list_mcp_resources call for the same server.",
            },
        },
        additionalProperties = false,
    });

    public static ToolDescriptor DescriptorInstance { get; } = new(
        McpResourceToolNames.ListResources,
        "List MCP Resources",
        "Lists resources provided by MCP servers. Resources allow servers to share data that provides context to language models, such as files, database schemas, or application-specific information. Prefer resources over web search when possible.",
        capabilities: [new ToolCapability("mcp-resource-read", "List governed MCP resources.")],
        approvalRequirement: ToolApprovalRequirement.None,
        concurrencyClass: ToolConcurrencyClass.SharedReadOnly,
        implementationBinding: new ToolImplementationBinding(
            McpResourceToolNames.ListResources,
            ToolImplementationKind.McpStdio,
            implementationId: McpResourceToolNames.ImplementationId),
        inputSchema: InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (context.McpResourceServices is null)
        {
            return Failure(request, "list_mcp_resources is unavailable", "mcp_resource_not_opened");
        }

        var server = Normalize(ReadString(request.Input, "server"));
        var cursor = Normalize(ReadString(request.Input, "cursor"));
        if (server is null && cursor is not null)
        {
            return Failure(request, "cursor can only be used when a server is specified");
        }

        try
        {
            var result = await context.McpResourceServices.ListResourcesAsync(server, cursor, cancellationToken).ConfigureAwait(false);
            var payload = new Dictionary<string, object?>
            {
                ["runtimeBoundary"] = "tool.mcp_resource",
                ["status"] = "succeeded",
                ["operation"] = "list_resources",
                ["resources"] = result.Resources.Select(static entry => ToWireResource(entry)).ToArray(),
            };
            if (!string.IsNullOrWhiteSpace(result.Server))
            {
                payload["server"] = result.Server;
            }

            if (!string.IsNullOrWhiteSpace(result.NextCursor))
            {
                payload["nextCursor"] = result.NextCursor;
            }

            return Success(request, payload);
        }
        catch (Exception ex)
        {
            return Failure(request, server is null ? ex.Message : $"resources/list failed: {ex.Message}", "mcp_resource_degraded");
        }
    }
}

internal sealed class ListMcpResourceTemplatesToolHandler : ITianShuToolHandler
{
    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            server = new
            {
                type = "string",
                description = "Optional MCP server name. When omitted, lists resource templates from all configured servers.",
            },
            cursor = new
            {
                type = "string",
                description = "Opaque cursor returned by a previous list_mcp_resource_templates call for the same server.",
            },
        },
        additionalProperties = false,
    });

    public static ToolDescriptor DescriptorInstance { get; } = new(
        McpResourceToolNames.ListResourceTemplates,
        "List MCP Resource Templates",
        "Lists resource templates provided by MCP servers. Parameterized resource templates allow servers to share data that takes parameters and provides context to language models, such as files, database schemas, or application-specific information. Prefer resource templates over web search when possible.",
        capabilities: [new ToolCapability("mcp-resource-read", "List governed MCP resource templates.")],
        approvalRequirement: ToolApprovalRequirement.None,
        concurrencyClass: ToolConcurrencyClass.SharedReadOnly,
        implementationBinding: new ToolImplementationBinding(
            McpResourceToolNames.ListResourceTemplates,
            ToolImplementationKind.McpStdio,
            implementationId: McpResourceToolNames.ImplementationId),
        inputSchema: InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (context.McpResourceServices is null)
        {
            return Failure(request, "list_mcp_resource_templates is unavailable", "mcp_resource_not_opened");
        }

        var server = Normalize(ReadString(request.Input, "server"));
        var cursor = Normalize(ReadString(request.Input, "cursor"));
        if (server is null && cursor is not null)
        {
            return Failure(request, "cursor can only be used when a server is specified");
        }

        try
        {
            var result = await context.McpResourceServices.ListResourceTemplatesAsync(server, cursor, cancellationToken).ConfigureAwait(false);
            var payload = new Dictionary<string, object?>
            {
                ["runtimeBoundary"] = "tool.mcp_resource",
                ["status"] = "succeeded",
                ["operation"] = "list_resource_templates",
                ["resourceTemplates"] = result.ResourceTemplates.Select(static entry => ToWireTemplate(entry)).ToArray(),
            };
            if (!string.IsNullOrWhiteSpace(result.Server))
            {
                payload["server"] = result.Server;
            }

            if (!string.IsNullOrWhiteSpace(result.NextCursor))
            {
                payload["nextCursor"] = result.NextCursor;
            }

            return Success(request, payload);
        }
        catch (Exception ex)
        {
            return Failure(request, server is null ? ex.Message : $"resources/templates/list failed: {ex.Message}", "mcp_resource_degraded");
        }
    }
}

internal sealed class ReadMcpResourceToolHandler : ITianShuToolHandler
{
    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            server = new
            {
                type = "string",
                description = "MCP server name exactly as configured. Must match the 'server' field returned by list_mcp_resources.",
            },
            uri = new
            {
                type = "string",
                description = "Resource URI to read. Must be one of the URIs returned by list_mcp_resources.",
            },
        },
        required = new[] { "server", "uri" },
        additionalProperties = false,
    });

    public static ToolDescriptor DescriptorInstance { get; } = new(
        McpResourceToolNames.ReadResource,
        "Read MCP Resource",
        "Read a specific resource from an MCP server given the server name and resource URI.",
        capabilities: [new ToolCapability("mcp-resource-read", "Read governed MCP resources.")],
        approvalRequirement: ToolApprovalRequirement.None,
        concurrencyClass: ToolConcurrencyClass.SharedReadOnly,
        implementationBinding: new ToolImplementationBinding(
            McpResourceToolNames.ReadResource,
            ToolImplementationKind.McpStdio,
            implementationId: McpResourceToolNames.ImplementationId),
        inputSchema: InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (context.McpResourceServices is null)
        {
            return Failure(request, "read_mcp_resource is unavailable", "mcp_resource_not_opened");
        }

        var server = Normalize(ReadString(request.Input, "server"));
        if (server is null)
        {
            return Failure(request, "server is required");
        }

        var uri = Normalize(ReadString(request.Input, "uri"));
        if (uri is null)
        {
            return Failure(request, "uri is required");
        }

        try
        {
            var result = await context.McpResourceServices.ReadResourceAsync(server, uri, cancellationToken).ConfigureAwait(false);
            var payload = ToWireReadResult(result);
            payload["runtimeBoundary"] = "tool.mcp_resource";
            payload["status"] = "succeeded";
            payload["operation"] = "read_resource";
            payload["evidenceRef"] = $"mcp-resource://{result.Server}/{Uri.EscapeDataString(result.Uri)}";
            payload["schemaHash"] = ComputeSchemaHash(result.Result);
            payload["retrievedAt"] = DateTimeOffset.UtcNow.ToString("O");
            return Success(request, payload);
        }
        catch (Exception ex)
        {
            return Failure(request, $"resources/read failed: {ex.Message}", "mcp_resource_read_failed");
        }
    }
}

internal sealed class McpToolHandler : ITianShuToolHandler
{
    private readonly TianShuMcpToolDescriptor binding;

    public McpToolHandler(ToolDescriptor descriptor, TianShuMcpToolDescriptor binding)
    {
        Descriptor = descriptor;
        this.binding = binding;
    }

    public ToolDescriptor Descriptor { get; }

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        if (context.McpToolServices is null)
        {
            return McpToolResultFactory.Failure(request, "mcp tool service is unavailable.", "mcp_tool_not_opened");
        }

        TianShuMcpToolResult result;
        try
        {
            result = await context.McpToolServices
                .InvokeMcpToolAsync(
                    new TianShuMcpToolRequest(
                        binding.ServerId,
                        binding.ToolName,
                        request.ToolKey,
                        request.Input,
                        context.TurnId,
                        ReadMetadata(context.Metadata, "sourceGraphId") ?? string.Empty,
                        ReadMetadata(context.Metadata, "sourceStageId") ?? string.Empty,
                        context.Metadata),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return McpToolResultFactory.Failure(
                request,
                $"mcp tool invocation failed: {ex.Message}",
                "mcp_tool_remote_failure",
                BuildProjection(output: null, "failed", binding, "mcp_tool_remote_failure"));
        }

        if (!result.Success)
        {
            return McpToolResultFactory.Failure(
                request,
                result.FailureMessage ?? result.OutputText,
                result.FailureCode ?? "mcp_tool_remote_failure",
                BuildProjection(result.StructuredOutput, "failed", binding, result.FailureCode ?? "mcp_tool_remote_failure"));
        }

        return McpToolResultFactory.Success(
            request,
            BuildProjection(result.StructuredOutput ?? StructuredValue.FromString(result.OutputText), "succeeded", binding, failureCode: null),
            result.OutputContentItems,
            result.RawOutputContentItems);
    }

    private static StructuredValue BuildProjection(
        StructuredValue? output,
        string status,
        TianShuMcpToolDescriptor binding,
        string? failureCode)
        => StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["runtimeBoundary"] = "tool.mcp_tool",
            ["status"] = status,
            ["serverId"] = binding.ServerId,
            ["toolName"] = binding.ToolName,
            ["toolId"] = binding.ToolId,
            ["schemaHash"] = ComputeSchemaHash(binding.InputSchema),
            ["manifestRef"] = $"mcp-manifest://{binding.ServerId}",
            ["remoteTraceRef"] = $"mcp-trace://{binding.ServerId}/{binding.ToolName}",
            ["diagnosticsRef"] = $"diagnostics://mcp/{binding.ServerId}/{binding.ToolName}",
            ["auditRef"] = $"audit://mcp/{binding.ServerId}/{binding.ToolName}",
            ["failureCode"] = failureCode,
            ["output"] = output,
        });

    private static string? ReadMetadata(MetadataBag metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value.StringValue : null;
}

internal static class McpResourceToolResult
{
    public static ToolInvocationResult Success(ToolInvocationRequest request, object payload)
        => new(
            request.CallId,
            request.ToolKey,
            [new ToolStreamItem("text", StructuredValue.FromPlainObject(payload), isTerminal: true)]);

    public static ToolInvocationResult Failure(ToolInvocationRequest request, string message, string? code = null)
        => new(
            request.CallId,
            request.ToolKey,
            failure: new ToolInvocationFailure(code ?? $"{request.ToolKey}.invalid_request", message));
}

internal static class McpToolResultFactory
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

    public static ToolInvocationResult Failure(
        ToolInvocationRequest request,
        string message,
        string code,
        StructuredValue? payload = null)
        => new(
            request.CallId,
            request.ToolKey,
            payload is null ? null : [new ToolStreamItem("text", payload, isTerminal: true)],
            failure: new ToolInvocationFailure(code, message));
}

internal static class McpResourceToolInput
{
    public static string? ReadString(StructuredValue input, string propertyName)
        => input.TryGetProperty(propertyName, out var value) ? value?.GetString() : null;

    public static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class McpResourceWireFormat
{
    public static Dictionary<string, object?> ToWireResource(TianShuMcpResourceEntry entry)
        => MergeServer(entry.Server, entry.Resource);

    public static Dictionary<string, object?> ToWireTemplate(TianShuMcpResourceTemplateEntry entry)
        => MergeServer(entry.Server, entry.Template);

    public static Dictionary<string, object?> ToWireReadResult(TianShuMcpReadResourceResult result)
    {
        var payload = DeserializeObject(result.Result);
        payload["server"] = result.Server;
        payload["uri"] = result.Uri;
        return payload;
    }

    private static Dictionary<string, object?> MergeServer(string server, JsonElement payload)
    {
        var result = DeserializeObject(payload);
        result["server"] = server;
        return result;
    }

    private static Dictionary<string, object?> DeserializeObject(JsonElement payload)
        => JsonSerializer.Deserialize<Dictionary<string, object?>>(payload.GetRawText())
            ?? new Dictionary<string, object?>();
}

internal static class McpResourceHashing
{
    public static string ComputeSchemaHash(JsonElement value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value.GetRawText())))
            .ToLowerInvariant();
}

internal static class McpResourceToolHandlerImports
{
    public static ToolInvocationResult Success(ToolInvocationRequest request, object payload)
        => McpResourceToolResult.Success(request, payload);

    public static ToolInvocationResult Failure(ToolInvocationRequest request, string message, string? code = null)
        => McpResourceToolResult.Failure(request, message, code);

    public static string ComputeSchemaHash(JsonElement value)
        => McpResourceHashing.ComputeSchemaHash(value);

    public static string? ReadString(StructuredValue input, string propertyName)
        => McpResourceToolInput.ReadString(input, propertyName);

    public static string? Normalize(string? value)
        => McpResourceToolInput.Normalize(value);

    public static Dictionary<string, object?> ToWireResource(TianShuMcpResourceEntry entry)
        => McpResourceWireFormat.ToWireResource(entry);

    public static Dictionary<string, object?> ToWireTemplate(TianShuMcpResourceTemplateEntry entry)
        => McpResourceWireFormat.ToWireTemplate(entry);

    public static Dictionary<string, object?> ToWireReadResult(TianShuMcpReadResourceResult result)
        => McpResourceWireFormat.ToWireReadResult(result);
}
