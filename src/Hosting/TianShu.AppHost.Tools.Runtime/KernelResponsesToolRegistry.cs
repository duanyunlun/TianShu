using TianShu.AppHost.Tools;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Tools;
using TianShu.Provider.Abstractions;
using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelResponsesNativeToolOptions(
    string? WebSearchMode,
    bool ImageGenerationEnabled,
    bool ApplyPatchEnabled = true,
    bool ApplyPatchFreeform = false,
    bool WebSearchSupportsImageContent = false,
    bool ViewImageEnabled = true,
    bool ViewImageCanRequestOriginalDetail = false,
    bool ArtifactToolEnabled = false,
    bool McpResourceToolsEnabled = false,
    bool SearchToolEnabled = false,
    IReadOnlyList<string>? SearchToolConnectorNames = null,
    bool ToolSuggestEnabled = false,
    IReadOnlyList<KernelToolSuggestConnectorInfo>? ToolSuggestDiscoverableConnectors = null,
    bool CodeModeEnabled = false,
    IReadOnlyList<string>? CodeModeEnabledToolNames = null,
    KernelShellToolType ShellToolType = KernelShellToolType.ShellCommand,
    bool UnifiedExecEnabled = true,
    bool ExecPermissionApprovalsEnabled = false,
    bool RequestPermissionsToolEnabled = false,
    bool RequestUserInputVisible = true,
    bool MemoryToolsEnabled = true,
    bool MultiAgentEnabled = true,
    string? SpawnAgentTypeDescription = null,
    bool FanoutEnabled = true,
    bool AgentJobWorkerToolsEnabled = true,
    bool ViewImageModelSupportsImageInput = true,
    IReadOnlyList<string>? ExperimentalSupportedTools = null,
    KernelToolProfileOptions? ToolProfileOptions = null);

internal sealed class KernelToolRegistry
{
    private static readonly HashSet<string> InternalModelHiddenToolNames = new(StringComparer.Ordinal)
    {
        "exec_command",
        "write_stdin",
    };

    private static readonly HashSet<string> InternalRuntimeOnlyToolNames = new(StringComparer.Ordinal)
    {
        KernelTestSyncRuntimeSupport.ToolName,
    };

    private readonly Dictionary<string, IKernelToolHandler> handlers = new(StringComparer.Ordinal);
    private readonly KernelToolImplementationResolver implementationResolver;

    public KernelToolRegistry()
        : this(new KernelToolImplementationResolver())
    {
    }

    internal KernelToolRegistry(KernelToolImplementationResolver implementationResolver)
    {
        this.implementationResolver = implementationResolver;
    }

    public void Register(IKernelToolHandler handler)
    {
        Register(handler.Name, handler);
    }

    public void Register(string name, IKernelToolHandler handler)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tool name must not be empty.", nameof(name));
        }

        handlers[name] = handler;
    }

    public void RegisterMany(IEnumerable<string> names, IKernelToolHandler handler)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            Register(name, handler);
        }
    }

    public bool TryGet(string name, out IKernelToolHandler? handler)
    {
        return handlers.TryGetValue(name, out handler);
    }

    public bool ToolSupportsParallelToolCalls(string name)
    {
        return handlers.TryGetValue(name, out var handler) && handler.SupportsParallelToolCalls;
    }

    public IReadOnlyList<IKernelToolHandler> EnumerateHandlers()
    {
        return handlers.Values
            .GroupBy(static x => x.Name, StringComparer.Ordinal)
            .Select(static x => x.First())
            .OrderBy(static x => x.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<object> BuildDescriptorList()
    {
        return EnumerateHandlers()
            .Select(x =>
            {
                var implementationBinding = implementationResolver.Resolve(x);
                return (object)new
                {
                    name = x.Name,
                    description = x.Description,
                    isMutating = x.IsMutating,
                    supportsParallelToolCalls = x.SupportsParallelToolCalls,
                    inputSchema = x.InputSchema,
                    outputSchema = x.OutputSchema is { } outputSchema ? outputSchema : (object?)null,
                    implementationBinding,
                };
            })
            .ToList();
    }

    public ResolvedToolCatalogSnapshot BuildResolvedToolCatalog(
        bool includeHiddenTools = false,
        KernelResponsesNativeToolOptions? nativeToolOptions = null,
        KernelToolProfileOptions? toolProfileOptions = null)
    {
        var profileOptions = toolProfileOptions ?? nativeToolOptions?.ToolProfileOptions;
        var items = EnumerateHandlers()
            .Select(handler =>
            {
                var implementationBinding = implementationResolver.Resolve(handler);
                string? disabledReason = null;
                var disabled = profileOptions?.TryGetDisabledReason(handler.Name, handler.Name, out disabledReason) == true;
                var implementationSelected = profileOptions?.IsSelectedImplementation(handler.Name, implementationBinding) != false;
                var available = implementationBinding.ImplementationKind != ToolImplementationKind.Unavailable
                                && !disabled
                                && implementationSelected;
                var modelVisible = available && ShouldIncludeResponsesTool(handler, nativeToolOptions);
                var reason = disabled
                    ? disabledReason
                    : implementationSelected
                        ? implementationBinding.Probe?.Reason
                        : "configured implementation is not registered";
                return new ResolvedToolCatalogItem(
                    handler.Name,
                    handler.Description,
                    implementationBinding.ImplementationKind,
                    available,
                    modelVisible,
                    reason,
                    implementationBinding.ImplementationId,
                    implementationBinding.Requirements,
                    implementationBinding.FallbackPolicy,
                    implementationBinding.PlatformProfile);
            })
            .Where(item => !InternalRuntimeOnlyToolNames.Contains(item.Name)
                           && (includeHiddenTools || !InternalModelHiddenToolNames.Contains(item.Name)))
            .OrderBy(static item => item.Name, StringComparer.Ordinal)
            .ToArray();

        return new ResolvedToolCatalogSnapshot(items);
    }

    public IReadOnlyList<object> BuildProviderResponsesToolList(
        KernelResponsesNativeToolOptions? nativeToolOptions = null,
        string? providerWireApi = null)
        => BuildProviderResponsesToolList(dynamicTools: null, nativeToolOptions, providerWireApi);

    public IReadOnlyList<object> BuildProviderResponsesToolList(
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools,
        KernelResponsesNativeToolOptions? nativeToolOptions = null,
        string? providerWireApi = null)
    {
        var definitions = BuildProviderResponsesToolDefinitions(dynamicTools, nativeToolOptions);
        var builder = ProviderResponsesToolSurfaceBuilders.Resolve(
            providerWireApi,
            "responses tool surface providerWireApi");
        return builder.Build(new ProviderResponsesToolSurfaceBuilderContext(definitions));
    }

    internal static KernelShellToolType ResolveVisibleShellToolType(KernelResponsesNativeToolOptions? nativeToolOptions)
        => nativeToolOptions?.ShellToolType
           ?? (OperatingSystem.IsWindows() ? KernelShellToolType.ShellCommand : KernelShellToolType.Default);

    private IReadOnlyList<ProviderResponsesToolDefinition> BuildProviderResponsesToolDefinitions(
        IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools,
        KernelResponsesNativeToolOptions? nativeToolOptions)
    {
        var definitions = EnumerateHandlers()
            .Select(x => new ResolvedKernelToolHandler(x, implementationResolver.Resolve(x)))
            .Where(static x => x.ImplementationBinding.ImplementationKind != ToolImplementationKind.Unavailable)
            .Where(x => nativeToolOptions?.ToolProfileOptions?.TryGetDisabledReason(x.Handler.Name, x.Handler.Name, out _) != true)
            .Where(x => nativeToolOptions?.ToolProfileOptions?.IsSelectedImplementation(x.Handler.Name, x.ImplementationBinding) != false)
            .Where(x => ShouldIncludeResponsesTool(x.Handler, nativeToolOptions))
            .Select(x => BuildProviderToolDefinition(x.Handler, nativeToolOptions))
            .ToList();

        foreach (var dynamicTool in BuildDynamicToolDefinitions(dynamicTools))
        {
            definitions.Add(dynamicTool);
        }

        foreach (var nativeTool in BuildNativeToolDefinitions(nativeToolOptions))
        {
            definitions.Add(nativeTool);
        }

        return definitions;
    }

    private sealed record ResolvedKernelToolHandler(IKernelToolHandler Handler, ToolImplementationBinding ImplementationBinding);

    private static IReadOnlyList<ProviderResponsesToolDefinition> BuildDynamicToolDefinitions(IReadOnlyList<KernelDynamicToolDescriptor>? dynamicTools)
    {
        if (!KernelDynamicToolResolver.HasAnyTools(dynamicTools))
        {
            return Array.Empty<ProviderResponsesToolDefinition>();
        }

        var tools = new List<ProviderResponsesToolDefinition>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in KernelDynamicToolResolver.Describe(dynamicTools))
        {
            if (!seenNames.Add(descriptor.FullName))
            {
                continue;
            }

            tools.Add(BuildDynamicToolDefinition(descriptor));
        }

        return tools;
    }

    private static ProviderResponsesToolDefinition BuildProviderToolDefinition(IKernelToolHandler handler, KernelResponsesNativeToolOptions? nativeToolOptions)
    {
        return handler switch
        {
            KernelContractToolHandlerAdapter contractHandler
                when nativeToolOptions?.ApplyPatchFreeform == true
                     && string.Equals(contractHandler.Name, "apply_patch", StringComparison.Ordinal)
                     && contractHandler.BuildCustomProviderToolDefinition() is { } customDefinition => customDefinition,
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, KernelToolDiscoveryToolNames.Search, StringComparison.Ordinal) =>
                KernelToolDiscoveryRuntimeSupport.BuildSearchProviderToolDefinition(nativeToolOptions?.SearchToolConnectorNames),
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, KernelToolDiscoveryToolNames.Suggest, StringComparison.Ordinal) =>
                KernelToolDiscoveryRuntimeSupport.BuildSuggestProviderToolDefinition(nativeToolOptions?.ToolSuggestDiscoverableConnectors),
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, "shell", StringComparison.Ordinal) =>
                KernelShellRuntimeSupport.BuildShellProviderToolDefinition(nativeToolOptions?.ExecPermissionApprovalsEnabled == true),
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, "local_shell", StringComparison.Ordinal) =>
                KernelShellRuntimeSupport.BuildLocalShellProviderToolDefinition(nativeToolOptions?.ExecPermissionApprovalsEnabled == true),
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, "shell_command", StringComparison.Ordinal) =>
                KernelShellRuntimeSupport.BuildShellCommandProviderToolDefinition(nativeToolOptions?.ExecPermissionApprovalsEnabled == true),
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, "spawn_agent", StringComparison.Ordinal) =>
                KernelCollaborationRuntimeSupport.BuildSpawnAgentProviderToolDefinition(nativeToolOptions?.SpawnAgentTypeDescription),
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, "exec", StringComparison.Ordinal) =>
                KernelCodeModeRuntimeSupport.BuildExecProviderToolDefinition(FilterModelVisibleToolNames(nativeToolOptions?.CodeModeEnabledToolNames)),
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, "js_repl", StringComparison.Ordinal) =>
                KernelJsReplRuntimeSupport.BuildJsReplProviderToolDefinition(),
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, "artifacts", StringComparison.Ordinal)
                     && contractHandler.BuildCustomProviderToolDefinition() is { } customDefinition => customDefinition,
            KernelContractToolHandlerAdapter contractHandler
                when string.Equals(contractHandler.Name, "view_image", StringComparison.Ordinal) =>
                KernelViewImageRuntimeSupport.BuildProviderToolDefinition(nativeToolOptions?.ViewImageCanRequestOriginalDetail == true),
            _ => handler.BuildProviderToolDefinition(),
        };
    }

    private static ProviderResponsesToolDefinition BuildDynamicToolDefinition(KernelDynamicToolDescriptor descriptor)
    {
        var schema = descriptor.InputSchema ?? JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { },
            additionalProperties = false,
        });

        return new ProviderResponsesFunctionToolDefinition(
            descriptor.FullName,
            descriptor.Description
            ?? descriptor.Title
            ?? descriptor.ConnectorDescription
            ?? descriptor.ConnectorName
            ?? descriptor.FullName,
            schema,
            ShouldIncludeDynamicToolOutputSchema(descriptor) ? descriptor.OutputSchema : null,
            strict: false,
            outputShape: ShouldIncludeDynamicToolOutputSchema(descriptor)
                ? ProviderResponsesToolOutputShape.McpToolResultEnvelope
                : ProviderResponsesToolOutputShape.DirectSchema);
    }

    private static bool ShouldIncludeDynamicToolOutputSchema(KernelDynamicToolDescriptor descriptor)
    {
        if (descriptor.FullName.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.Namespace)
            && descriptor.Namespace.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.ConnectorId))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(descriptor.Server)
               && !string.Equals(descriptor.Server, "dynamic", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ProviderResponsesToolDefinition> BuildNativeToolDefinitions(KernelResponsesNativeToolOptions? nativeToolOptions)
    {
        var definitions = new List<ProviderResponsesToolDefinition>();
        var webSearchMode = KernelToolJsonHelpers.Normalize(nativeToolOptions?.WebSearchMode);
        if (!string.IsNullOrWhiteSpace(webSearchMode)
            && !string.Equals(webSearchMode, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            definitions.Add(new ProviderResponsesHostedToolDefinition(
                toolType: "web_search",
                externalWebAccess: string.Equals(webSearchMode, "live", StringComparison.OrdinalIgnoreCase),
                searchContentTypes: nativeToolOptions?.WebSearchSupportsImageContent == true
                    ? ["text", "image"]
                    : null));
        }

        if (nativeToolOptions?.ImageGenerationEnabled == true)
        {
            definitions.Add(new ProviderResponsesHostedToolDefinition(
                toolType: "image_generation",
                outputFormat: "png"));
        }

        return definitions;
    }

    private static bool ShouldIncludeResponsesTool(IKernelToolHandler handler, KernelResponsesNativeToolOptions? nativeToolOptions)
    {
        if (nativeToolOptions?.ToolProfileOptions?.TryGetDisabledReason(handler.Name, handler.Name, out _) == true)
        {
            return false;
        }

        return handler.Name switch
        {
            "shell" => ResolveVisibleShellToolType(nativeToolOptions) == KernelShellToolType.Default,
            "local_shell" => ResolveVisibleShellToolType(nativeToolOptions) == KernelShellToolType.Local,
            "shell_command" => ResolveVisibleShellToolType(nativeToolOptions) == KernelShellToolType.ShellCommand,
            "exec_command" or "write_stdin" => ResolveVisibleShellToolType(nativeToolOptions) == KernelShellToolType.UnifiedExec,
            "apply_patch" => nativeToolOptions?.ApplyPatchEnabled != false,
            "view_image" => nativeToolOptions?.ViewImageEnabled != false,
            "artifacts" => nativeToolOptions?.ArtifactToolEnabled == true,
            "list_dir" => SupportsExperimentalTool(nativeToolOptions, "list_dir"),
            "read_file" => SupportsExperimentalTool(nativeToolOptions, "read_file"),
            "grep_files" => SupportsExperimentalTool(nativeToolOptions, "grep_files"),
            KernelMcpResourceToolNames.ListResources
                or KernelMcpResourceToolNames.ListResourceTemplates
                or KernelMcpResourceToolNames.ReadResource => nativeToolOptions?.McpResourceToolsEnabled == true,
            "exec" or "exec_wait" => nativeToolOptions?.CodeModeEnabled == true,
            "js_repl" or "js_repl_reset" => nativeToolOptions?.CodeModeEnabled != true,
            "request_permissions" => nativeToolOptions?.RequestPermissionsToolEnabled == true,
            "request_user_input" => nativeToolOptions?.RequestUserInputVisible != false,
            KernelMemoryToolNames.Search
                or KernelMemoryToolNames.ExplainOverlay
                or KernelMemoryToolNames.Feedback => nativeToolOptions?.MemoryToolsEnabled != false,
            "spawn_agent" or "send_input" or "resume_agent" or "wait" or "close_agent" => nativeToolOptions?.MultiAgentEnabled != false,
            "spawn_agents_on_csv" => nativeToolOptions?.FanoutEnabled == true,
            "report_agent_job_result" => nativeToolOptions?.AgentJobWorkerToolsEnabled == true,
            KernelToolDiscoveryToolNames.Search => nativeToolOptions?.SearchToolEnabled == true,
            KernelToolDiscoveryToolNames.Suggest => nativeToolOptions?.ToolSuggestEnabled == true
                                                   && nativeToolOptions.SearchToolEnabled,
            "test_sync_tool" => SupportsExplicitExperimentalTool(nativeToolOptions, "test_sync_tool"),
            _ => true,
        };
    }

    private static bool SupportsExperimentalTool(KernelResponsesNativeToolOptions? nativeToolOptions, string toolName)
    {
        return nativeToolOptions?.ExperimentalSupportedTools is null
            || nativeToolOptions.ExperimentalSupportedTools.Contains(toolName, StringComparer.Ordinal);
    }

    private static bool SupportsExplicitExperimentalTool(KernelResponsesNativeToolOptions? nativeToolOptions, string toolName)
    {
        return nativeToolOptions?.ExperimentalSupportedTools?.Contains(toolName, StringComparer.Ordinal) == true;
    }

    private static IReadOnlyList<string>? FilterModelVisibleToolNames(IReadOnlyList<string>? toolNames)
    {
        if (toolNames is null || toolNames.Count == 0)
        {
            return toolNames;
        }

        return toolNames
            .Where(static name => !InternalModelHiddenToolNames.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }
}

internal static class KernelToolRegistryFactory
{
    public static KernelToolRegistry CreateDefaultRegistry()
        => CreateDefaultRegistry((IReadOnlyDictionary<string, string>?)null, workspacePath: null);

    public static KernelToolRegistry CreateDefaultRegistry(
        IReadOnlyDictionary<string, string>? configValues,
        string? workspacePath)
    {
        var profileOptions = KernelToolProfileOptions.FromConfigValues(configValues);
        return new KernelToolRegistryBuilder()
            .AddConfiguredToolPackages(configValues, profileOptions, workspacePath)
            .Build();
    }

    public static KernelToolRegistry CreateDefaultRegistry(
        IReadOnlyDictionary<string, object?>? configValues,
        string? workspacePath)
    {
        var normalizedConfigValues = KernelToolProfileOptions.NormalizeConfigValues(configValues);
        return CreateDefaultRegistry(normalizedConfigValues, workspacePath);
    }
}
