using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;
using TianShu.Provider.Abstractions;
using static TianShu.AppHost.Configuration.KernelTomlTextParsingUtilities;
using static TianShu.AppHost.Tools.KernelToolJsonHelpers;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelNativeToolOptionsAppHostRuntimeContext(
    string? Cwd,
    string? Model,
    string? WebSearchMode,
    JsonElement? SandboxPolicy,
    string? SandboxMode,
    IReadOnlyList<KernelDynamicToolDescriptor>? DynamicTools,
    KernelSessionSource? SessionSource,
    bool EnableAgentJobWorkerTools,
    KernelWindowsSandboxLevel WindowsSandboxLevel);

internal sealed class KernelNativeToolOptionsAppHostRuntime
{
    private readonly KernelMcpManager mcpManager;
    private readonly Func<string?, CancellationToken, Task<string?>> loadMergedPersistedConfigTextAsync;
    private readonly Func<string?, CancellationToken, Task<KernelSpawnAgentGuardConfiguration>> resolveSpawnAgentGuardConfigurationAsync;
    private readonly Func<string?, CancellationToken, Task<string>> buildSpawnAgentTypeDescriptionAsync;
    private readonly Func<CancellationToken, Task<IReadOnlyList<KernelToolSuggestConnectorInfo>>> loadToolSuggestDiscoverableConnectorsAsync;
    private readonly Func<IReadOnlyList<KernelDynamicToolDescriptor>?, KernelResponsesNativeToolOptions, IReadOnlyList<KernelCodeModeEnabledTool>> buildCodeModeEnabledTools;
    private readonly string defaultModel;

    public KernelNativeToolOptionsAppHostRuntime(
        KernelMcpManager mcpManager,
        Func<string?, CancellationToken, Task<string?>> loadMergedPersistedConfigTextAsync,
        Func<string?, CancellationToken, Task<KernelSpawnAgentGuardConfiguration>> resolveSpawnAgentGuardConfigurationAsync,
        Func<string?, CancellationToken, Task<string>> buildSpawnAgentTypeDescriptionAsync,
        Func<CancellationToken, Task<IReadOnlyList<KernelToolSuggestConnectorInfo>>> loadToolSuggestDiscoverableConnectorsAsync,
        Func<IReadOnlyList<KernelDynamicToolDescriptor>?, KernelResponsesNativeToolOptions, IReadOnlyList<KernelCodeModeEnabledTool>> buildCodeModeEnabledTools,
        string defaultModel)
    {
        this.mcpManager = mcpManager;
        this.loadMergedPersistedConfigTextAsync = loadMergedPersistedConfigTextAsync;
        this.resolveSpawnAgentGuardConfigurationAsync = resolveSpawnAgentGuardConfigurationAsync;
        this.buildSpawnAgentTypeDescriptionAsync = buildSpawnAgentTypeDescriptionAsync;
        this.loadToolSuggestDiscoverableConnectorsAsync = loadToolSuggestDiscoverableConnectorsAsync;
        this.buildCodeModeEnabledTools = buildCodeModeEnabledTools;
        this.defaultModel = defaultModel;
    }

    public async Task<KernelResponsesNativeToolOptions> ResolveResponsesNativeToolOptionsAsync(
        KernelNativeToolOptionsAppHostRuntimeContext context,
        CancellationToken cancellationToken)
    {
        var configText = await loadMergedPersistedConfigTextAsync(context.Cwd, cancellationToken).ConfigureAwait(false);
        var toolProfileOptions = KernelToolProfileOptions.FromTomlText(configText);

        var webSearchMode = ResolveConfiguredWebSearchMode(configText, Normalize(context.SandboxMode), Normalize(context.WebSearchMode));
        var model = Normalize(context.Model) ?? defaultModel;
        var imageGenerationEnabled = ResolveConfiguredImageGenerationEnabled(configText, model);
        var webSearchSupportsImageContent = ProviderModelCatalogs.SupportsWebSearchImageContent(model);
        var requestUserInputVisible = context.SessionSource?.SubAgentSource is null;
        var experimentalSupportedTools = ProviderModelCatalogs.GetExperimentalSupportedTools(model);
        var applyPatchToolType = ProviderModelCatalogs.GetApplyPatchToolType(model);
        if (string.IsNullOrWhiteSpace(applyPatchToolType)
            && ResolveConfiguredFeatureFlag(configText, defaultValue: false, "include_apply_patch_tool"))
        {
            applyPatchToolType = "freeform";
        }

        var applyPatchEnabled = !string.IsNullOrWhiteSpace(applyPatchToolType);
        var applyPatchFreeform = string.Equals(applyPatchToolType, "freeform", StringComparison.OrdinalIgnoreCase);
        var viewImageEnabled = ResolveConfiguredViewImageEnabled(configText);
        var viewImageModelSupportsImageInput = ProviderModelCatalogs.SupportsImageInput(model);
        var viewImageCanRequestOriginalDetail = viewImageEnabled
                                                && viewImageModelSupportsImageInput
                                                && ProviderModelCatalogs.SupportsImageDetailOriginal(model)
                                                && ResolveConfiguredImageDetailOriginalEnabled(configText);
        var artifactToolEnabled = KernelArtifactsRuntimeHelpers.ResolveConfiguredArtifactEnabled(configText)
                                  && KernelArtifactsRuntimeManager.CanManageArtifactRuntime();
        var mcpResourceToolsEnabled = (await mcpManager.ListServerNamesAsync(cancellationToken).ConfigureAwait(false)).Count > 0;
        var guardConfiguration = await resolveSpawnAgentGuardConfigurationAsync(context.Cwd, cancellationToken).ConfigureAwait(false);
        var currentDepth = Math.Max(context.SessionSource?.GetThreadSpawnDepth() ?? 0, 0);
        var depthLimitReached = currentDepth >= guardConfiguration.MaxDepth;
        var fanoutEnabled = ResolveConfiguredFanoutEnabled(configText) && !depthLimitReached;
        var multiAgentEnabled = (fanoutEnabled || ResolveConfiguredMultiAgentEnabled(configText)) && !depthLimitReached;
        var spawnAgentTypeDescription = multiAgentEnabled
            ? await buildSpawnAgentTypeDescriptionAsync(context.Cwd, cancellationToken).ConfigureAwait(false)
            : null;
        var searchToolConnectorNames = KernelDynamicToolResolver.GetConnectorNames(context.DynamicTools);
        var searchToolEnabled = KernelDynamicToolResolver.HasAnyTools(context.DynamicTools)
            && ProviderModelCatalogs.SupportsSearchTool(model);
        var toolSuggestDiscoverableConnectors = searchToolEnabled
            ? await loadToolSuggestDiscoverableConnectorsAsync(cancellationToken).ConfigureAwait(false)
            : Array.Empty<KernelToolSuggestConnectorInfo>();
        var codeModeEnabled = ResolveConfiguredCodeModeEnabled(configText);
        var shellToolEnabled = ResolveConfiguredShellToolEnabled(configText);
        var unifiedExecConfigured = ResolveConfiguredUnifiedExecEnabled(configText);
        var unifiedExecEnabled = unifiedExecConfigured
                                 && KernelUnifiedExecAvailability.IsAllowed(
                                     context.SandboxPolicy,
                                     context.SandboxMode,
                                     context.WindowsSandboxLevel);
        var shellToolType = ResolveConfiguredShellToolType(model, shellToolEnabled, unifiedExecEnabled);
        var execPermissionApprovalsEnabled = ResolveConfiguredExecPermissionApprovalsEnabled(configText);
        var requestPermissionsToolEnabled = ResolveConfiguredRequestPermissionsToolEnabled(configText);
        var memoryToolsEnabled = ResolveConfiguredMemoryToolsEnabled(configText);
        var baseOptions = new KernelResponsesNativeToolOptions(
            WebSearchMode: webSearchMode,
            ImageGenerationEnabled: imageGenerationEnabled,
            ApplyPatchEnabled: applyPatchEnabled,
            ApplyPatchFreeform: applyPatchFreeform,
            WebSearchSupportsImageContent: webSearchSupportsImageContent,
            ViewImageEnabled: viewImageEnabled,
            ViewImageCanRequestOriginalDetail: viewImageCanRequestOriginalDetail,
            ArtifactToolEnabled: artifactToolEnabled,
            McpResourceToolsEnabled: mcpResourceToolsEnabled,
            SearchToolEnabled: searchToolEnabled,
            SearchToolConnectorNames: searchToolConnectorNames,
            ToolSuggestEnabled: searchToolEnabled && toolSuggestDiscoverableConnectors.Count > 0,
            ToolSuggestDiscoverableConnectors: toolSuggestDiscoverableConnectors,
            CodeModeEnabled: codeModeEnabled,
            ShellToolType: shellToolType,
            UnifiedExecEnabled: unifiedExecEnabled,
            ExecPermissionApprovalsEnabled: execPermissionApprovalsEnabled,
            RequestPermissionsToolEnabled: requestPermissionsToolEnabled,
            RequestUserInputVisible: requestUserInputVisible,
            MemoryToolsEnabled: memoryToolsEnabled,
            MultiAgentEnabled: multiAgentEnabled,
            SpawnAgentTypeDescription: spawnAgentTypeDescription,
            FanoutEnabled: fanoutEnabled,
            AgentJobWorkerToolsEnabled: fanoutEnabled && context.EnableAgentJobWorkerTools,
            ViewImageModelSupportsImageInput: viewImageModelSupportsImageInput,
            ExperimentalSupportedTools: experimentalSupportedTools,
            ToolProfileOptions: toolProfileOptions);

        var codeModeEnabledToolNames = codeModeEnabled
            ? buildCodeModeEnabledTools(context.DynamicTools, baseOptions)
                .Select(static tool => tool.ToolName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray()
            : null;
        return baseOptions with
        {
            CodeModeEnabledToolNames = codeModeEnabledToolNames,
        };
    }

    private static string ResolveConfiguredWebSearchMode(string? configText, string? sandboxMode, string? requestedMode)
    {
        var preferred = Normalize(requestedMode) ?? ReadConfiguredWebSearchValue(configText) ?? "cached";
        var allowedModes = ReadConfiguredAllowedWebSearchModes(configText);
        if (allowedModes is { Count: > 0 })
        {
            if (!allowedModes.Contains("disabled", StringComparer.OrdinalIgnoreCase))
            {
                allowedModes.Add("disabled");
            }
        }

        if (string.Equals(Normalize(sandboxMode), "danger-full-access", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(preferred, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in new[] { "live", "cached", "disabled" })
            {
                if (IsAllowedWebSearchMode(candidate, allowedModes))
                {
                    return candidate;
                }
            }
        }
        else
        {
            if (IsAllowedWebSearchMode(preferred, allowedModes))
            {
                return preferred;
            }

            foreach (var candidate in new[] { "cached", "live", "disabled" })
            {
                if (IsAllowedWebSearchMode(candidate, allowedModes))
                {
                    return candidate;
                }
            }
        }

        return "disabled";
    }

    private static bool ResolveConfiguredImageGenerationEnabled(string? configText, string model)
    {
        if (!SupportsImageGenerationModel(model))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(configText))
        {
            return false;
        }

        var features = ParseTomlBooleanSection(configText, "features");
        return features.TryGetValue("image_generation", out var enabled) && enabled;
    }

    private static bool ResolveConfiguredImageDetailOriginalEnabled(string? configText)
    {
        return ResolveConfiguredFeatureFlag(
            configText,
            defaultValue: false,
            "image_detail_original");
    }

    private static bool ResolveConfiguredViewImageEnabled(string? configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return true;
        }

        var tools = ParseTomlSectionRawValues(configText, "tools");
        return TryReadTomlSectionBoolean(tools, "view_image") ?? true;
    }

    private static bool ResolveConfiguredShellToolEnabled(string? configText)
    {
        return ResolveConfiguredFeatureFlag(
            configText,
            defaultValue: true,
            "shell_tool");
    }

    private static bool ResolveConfiguredUnifiedExecEnabled(string? configText)
    {
        return ResolveConfiguredFeatureFlag(
            configText,
            defaultValue: !OperatingSystem.IsWindows(),
            "unified_exec",
            "experimental_use_unified_exec_tool");
    }

    private static bool ResolveConfiguredExecPermissionApprovalsEnabled(string? configText)
    {
        return ResolveConfiguredFeatureFlag(
            configText,
            defaultValue: false,
            "exec_permission_approvals",
            "request_permissions");
    }

    private static bool ResolveConfiguredRequestPermissionsToolEnabled(string? configText)
    {
        return ResolveConfiguredFeatureFlag(
            configText,
            defaultValue: false,
            "request_permissions_tool");
    }

    private static bool ResolveConfiguredMemoryToolsEnabled(string? configText)
    {
        return ResolveConfiguredFeatureFlag(
            configText,
            defaultValue: true,
            "memory_tools",
            "memory_tool");
    }

    private static bool ResolveConfiguredCodeModeEnabled(string? configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return false;
        }

        var features = ParseTomlBooleanSection(configText, "features");
        return features.TryGetValue("code_mode", out var enabled) && enabled;
    }

    private static KernelShellToolType ResolveConfiguredShellToolType(
        string model,
        bool shellToolEnabled,
        bool unifiedExecEnabled)
    {
        if (!shellToolEnabled)
        {
            return KernelShellToolType.Disabled;
        }

        if (unifiedExecEnabled)
        {
            return KernelShellToolType.UnifiedExec;
        }

        return ProviderModelCatalogs.GetShellToolType(model) switch
        {
            "disabled" => OperatingSystem.IsWindows() ? KernelShellToolType.ShellCommand : KernelShellToolType.Default,
            "unified_exec" => KernelShellToolType.ShellCommand,
            "local" => KernelShellToolType.Local,
            "shell_command" => KernelShellToolType.ShellCommand,
            "default" => KernelShellToolType.Default,
            _ => OperatingSystem.IsWindows() ? KernelShellToolType.ShellCommand : KernelShellToolType.Default,
        };
    }

    private static bool ResolveConfiguredMultiAgentEnabled(string? configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return false;
        }

        var features = ParseTomlBooleanSection(configText, "features");
        return features.TryGetValue("multi_agent", out var enabled) && enabled;
    }

    private static bool ResolveConfiguredFanoutEnabled(string? configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return false;
        }

        var features = ParseTomlBooleanSection(configText, "features");
        return features.TryGetValue("enable_fanout", out var enabled) && enabled;
    }

    private static bool ResolveConfiguredFeatureFlag(
        string? configText,
        bool defaultValue,
        params string[] keys)
    {
        if (string.IsNullOrWhiteSpace(configText) || keys.Length == 0)
        {
            return defaultValue;
        }

        foreach (var sectionName in new[] { "features", "experimental_features" })
        {
            var features = ParseTomlBooleanSection(configText, sectionName);
            foreach (var key in keys)
            {
                if (features.TryGetValue(key, out var enabled))
                {
                    return enabled;
                }
            }
        }

        foreach (var key in keys)
        {
            if (TryParseTopLevelTomlScalar(configText, key, out var rawValue)
                && bool.TryParse(rawValue, out var enabled))
            {
                return enabled;
            }
        }

        return defaultValue;
    }

    private static string? ReadConfiguredWebSearchValue(string? configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return null;
        }

        return TryParseTopLevelTomlScalar(configText, "web_search", out var value)
            ? Normalize(value)
            : null;
    }

    private static List<string>? ReadConfiguredAllowedWebSearchModes(string? configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return null;
        }

        return TryParseTomlStringArray(configText, "allowed_web_search_modes", out var values)
            ? values
            : null;
    }

    private static bool IsAllowedWebSearchMode(string mode, IReadOnlyList<string>? allowedModes)
    {
        var normalized = Normalize(mode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (allowedModes is null || allowedModes.Count == 0)
        {
            return normalized is "cached" or "live" or "disabled";
        }

        return allowedModes.Contains(normalized!, StringComparer.OrdinalIgnoreCase);
    }

    private static bool SupportsImageGenerationModel(string model)
    {
        var normalized = Normalize(model);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);
    }
}
