using System.Text.Json;
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
        return Descriptors.Values.ToArray();
    }

    public ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context)
    {
        _ = context;
        return toolKey switch
        {
            McpResourceToolNames.ListResources => new ListMcpResourcesToolHandler(),
            McpResourceToolNames.ListResourceTemplates => new ListMcpResourceTemplatesToolHandler(),
            McpResourceToolNames.ReadResource => new ReadMcpResourceToolHandler(),
            _ => throw new InvalidOperationException($"Unknown MCP resource tool: {toolKey}"),
        };
    }
}

internal static class McpResourceToolNames
{
    public const string ListResources = "list_mcp_resources";
    public const string ListResourceTemplates = "list_mcp_resource_templates";
    public const string ReadResource = "read_mcp_resource";
    public const string ImplementationId = "tianshu.tools.mcp-resources";
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
            return Failure(request, "list_mcp_resources is unavailable");
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
            return Failure(request, server is null ? ex.Message : $"resources/list failed: {ex.Message}");
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
            return Failure(request, "list_mcp_resource_templates is unavailable");
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
            return Failure(request, server is null ? ex.Message : $"resources/templates/list failed: {ex.Message}");
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
            return Failure(request, "read_mcp_resource is unavailable");
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
            return Success(request, ToWireReadResult(result));
        }
        catch (Exception ex)
        {
            return Failure(request, $"resources/read failed: {ex.Message}");
        }
    }
}

internal static class McpResourceToolResult
{
    public static ToolInvocationResult Success(ToolInvocationRequest request, object payload)
        => new(
            request.CallId,
            request.ToolKey,
            [new ToolStreamItem("text", StructuredValue.FromPlainObject(payload), isTerminal: true)]);

    public static ToolInvocationResult Failure(ToolInvocationRequest request, string message)
        => new(
            request.CallId,
            request.ToolKey,
            failure: new ToolInvocationFailure($"{request.ToolKey}.invalid_request", message));
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

internal static class McpResourceToolHandlerImports
{
    public static ToolInvocationResult Success(ToolInvocationRequest request, object payload)
        => McpResourceToolResult.Success(request, payload);

    public static ToolInvocationResult Failure(ToolInvocationRequest request, string message)
        => McpResourceToolResult.Failure(request, message);

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
