using TianShu.AppHost.Configuration;
using TianShu.Contracts.Catalog;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Catalog;

/// <summary>
/// model / experimental feature / collaboration mode surface 辅助件。
/// Helpers for model, experimental-feature, and collaboration-mode list surfaces.
/// </summary>
internal static class KernelCatalogSurfaceUtilities
{
    public static IReadOnlyList<ControlPlaneModelCatalogItem> GetBuiltInModels()
    {
        var models = ProviderModelCatalogs.ListModels()
            .Where(static model => model.SupportedInApi)
            .Select(static model => new ControlPlaneModelCatalogItem
            {
                Id = model.Id,
                Model = model.Model,
                DisplayName = model.DisplayName,
                DefaultReasoningEffort = model.DefaultReasoningEffort,
                SupportedReasoningEfforts = model.SupportedReasoningEfforts
                    .Select(static effort => effort.Effort)
                    .ToArray(),
                InputModalities = model.InputModalities.Count == 0 ? ["text", "image"] : model.InputModalities,
                SupportsPersonality = model.SupportsPersonality,
                Hidden = model.Hidden,
                IsDefault = false,
                SupportsParallelToolCalls = model.SupportsParallelToolCalls,
                SupportsReasoningSummaries = model.SupportsReasoningSummaries,
                DefaultReasoningSummary = model.DefaultReasoningSummary,
                SupportsVerbosity = model.SupportsVerbosity,
                DefaultVerbosity = model.DefaultVerbosity,
                PreferWebsocketTransport = model.PreferWebsockets,
                Description = model.Description,
                AvailabilityNuxMessage = model.AvailabilityNuxMessage,
                UpgradeModel = model.UpgradeModel,
                UpgradeMigrationMarkdown = model.UpgradeMigrationMarkdown,
            })
            .ToList();

        var defaultIndex = models.FindIndex(static item => !item.Hidden);
        if (defaultIndex < 0 && models.Count > 0)
        {
            defaultIndex = 0;
        }

        for (var index = 0; index < models.Count; index++)
        {
            models[index] = models[index] with
            {
                IsDefault = index == defaultIndex,
            };
        }

        return models;
    }

    public static bool TryGetBuiltInModel(string? model, out ControlPlaneModelCatalogItem? descriptor)
    {
        descriptor = GetBuiltInModels()
            .FirstOrDefault(candidate => string.Equals(candidate.Model, model, StringComparison.OrdinalIgnoreCase));
        return descriptor is not null;
    }

    public static IReadOnlyList<string> GetBuiltInModelNames()
        => GetBuiltInModels()
            .Select(static model => model.Model)
            .ToArray();

    public static string GetDefaultReasoningEffort(string? model)
        => ProviderModelCatalogs.GetDefaultReasoningEffort(model);

    public static bool SupportsReasoningSummaries(string? model)
        => ProviderModelCatalogs.SupportsReasoningSummaries(model);

    public static string? GetDefaultReasoningSummary(string? model)
        => ProviderModelCatalogs.GetDefaultReasoningSummary(model);

    public static bool SupportsVerbosity(string? model)
        => ProviderModelCatalogs.SupportsVerbosity(model);

    public static string? GetDefaultVerbosity(string? model)
        => ProviderModelCatalogs.GetDefaultVerbosity(model);

    public static string GetBaseInstructions(string? model)
        => ProviderModelCatalogs.GetBaseInstructions(model);

    public static object ToModelPayload(ControlPlaneModelCatalogItem model)
    {
        ProviderModelCatalogs.TryGetModel(model.Model, out var providerModelDescriptor);
        var reasoningEfforts = providerModelDescriptor?.SupportedReasoningEfforts
            ?? Array.Empty<ProviderReasoningEffortDescriptor>();

        return new
        {
            id = model.Id,
            model = model.Model,
            upgrade = model.UpgradeModel,
            upgradeInfo = string.IsNullOrWhiteSpace(model.UpgradeModel)
                ? null
                : new
                {
                    model = model.UpgradeModel,
                    upgradeCopy = (string?)null,
                    modelLink = (string?)null,
                    migrationMarkdown = model.UpgradeMigrationMarkdown,
                },
            availabilityNux = string.IsNullOrWhiteSpace(model.AvailabilityNuxMessage)
                ? null
                : new
                {
                    message = model.AvailabilityNuxMessage,
                },
            displayName = model.DisplayName,
            description = model.Description,
            hidden = model.Hidden,
            supportedReasoningEfforts = model.SupportedReasoningEfforts
                .Select(effort => new
                {
                    reasoningEffort = effort,
                    description = reasoningEfforts
                        .FirstOrDefault(candidate => string.Equals(candidate.Effort, effort, StringComparison.OrdinalIgnoreCase))
                        ?.Description,
                })
                .ToArray(),
            defaultReasoningEffort = model.DefaultReasoningEffort,
            inputModalities = model.InputModalities.ToArray(),
            supportsPersonality = model.SupportsPersonality,
            supportsParallelToolCalls = model.SupportsParallelToolCalls,
            supportsReasoningSummaries = model.SupportsReasoningSummaries,
            defaultReasoningSummary = model.DefaultReasoningSummary,
            supportsVerbosity = model.SupportsVerbosity,
            defaultVerbosity = model.DefaultVerbosity,
            preferWebsocketTransport = model.PreferWebsocketTransport,
            isDefault = model.IsDefault,
        };
    }

    public static IReadOnlyList<ControlPlaneExperimentalFeatureDescriptor> GetExperimentalFeatureDescriptors()
    {
        return
        [
            new ControlPlaneExperimentalFeatureDescriptor { Name = "undo", Stage = "stable", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "shell_tool", Stage = "stable", DefaultEnabled = true },
            CreateUnifiedExecExperimentalFeatureDescriptor(),
            new ControlPlaneExperimentalFeatureDescriptor { Name = "shell_zsh_fork", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "shell_snapshot", Stage = "stable", DefaultEnabled = true },
            new ControlPlaneExperimentalFeatureDescriptor
            {
                Name = "js_repl",
                Stage = "beta",
                DisplayName = "JavaScript REPL",
                Description = "Enable a persistent Node-backed JavaScript REPL for interactive website debugging and other inline JavaScript execution capabilities. Requires Node >= v22.22.0 installed.",
                Announcement = "NEW: JavaScript REPL is now available in /experimental. Enable it, then start a new chat or restart TianShu to use it.",
                DefaultEnabled = false,
            },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "code_mode", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "js_repl_tools_only", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "web_search_request", Stage = "deprecated", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "web_search_cached", Stage = "deprecated", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "search_tool", Stage = "removed", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "tianshu_git_commit", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "runtime_metrics", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "sqlite", Stage = "removed", DefaultEnabled = true },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "memories", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "child_agents_md", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "image_detail_original", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "apply_patch_freeform", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "exec_permission_approvals", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "tianshu_hooks", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "request_permissions_tool", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "use_linux_sandbox_bwrap", Stage = "removed", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "use_legacy_landlock", Stage = "stable", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "request_rule", Stage = "removed", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "experimental_windows_sandbox", Stage = "removed", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "elevated_windows_sandbox", Stage = "removed", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "remote_models", Stage = "removed", DefaultEnabled = false },
            CreatePowershellUtf8ExperimentalFeatureDescriptor(),
            new ControlPlaneExperimentalFeatureDescriptor { Name = "enable_request_compression", Stage = "stable", DefaultEnabled = true },
            new ControlPlaneExperimentalFeatureDescriptor
            {
                Name = "multi_agent",
                Stage = "beta",
                DisplayName = "Multi-agents",
                Description = "Ask TianShu to spawn multiple agents to parallelize the work and win in efficiency.",
                Announcement = "NEW: Multi-agents can now be spawned by TianShu. Enable in /experimental and restart TianShu!",
                DefaultEnabled = false,
            },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "enable_fanout", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor
            {
                Name = "apps",
                Stage = "beta",
                DisplayName = "Apps",
                Description = "Use a connected ChatGPT App using \"$\". Install Apps via /apps command. Restart TianShu after enabling.",
                Announcement = "NEW: Use ChatGPT Apps (Connectors) in TianShu via $ mentions. Enable in /experimental and restart TianShu!",
                DefaultEnabled = false,
            },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "tool_suggest", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "plugins", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "image_generation", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "apps_mcp_gateway", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "skill_mcp_dependency_install", Stage = "stable", DefaultEnabled = true },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "skill_env_var_dependency_prompt", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "steer", Stage = "removed", DefaultEnabled = true },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "default_mode_request_user_input", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor
            {
                Name = "guardian_approval",
                Stage = "beta",
                DisplayName = "Automatic approval review",
                Description = "Dispatch `on-request` approval prompts (for e.g. sandbox escapes or blocked network access) to a carefully-prompted security reviewer subagent rather than blocking the agent on your input.",
                Announcement = string.Empty,
                DefaultEnabled = false,
            },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "collaboration_modes", Stage = "removed", DefaultEnabled = true },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "tool_call_mcp_elicitation", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "personality", Stage = "stable", DefaultEnabled = true },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "artifact", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "fast_mode", Stage = "stable", DefaultEnabled = true },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "voice_transcription", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "realtime_conversation", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "realtime_conversation_v2", Stage = "underDevelopment", DefaultEnabled = false },
            CreatePreventIdleSleepExperimentalFeatureDescriptor(),
            new ControlPlaneExperimentalFeatureDescriptor { Name = "responses_websockets", Stage = "underDevelopment", DefaultEnabled = false },
            new ControlPlaneExperimentalFeatureDescriptor { Name = "responses_websockets_v2", Stage = "underDevelopment", DefaultEnabled = false },
        ];
    }

    public static object ToExperimentalFeaturePayload(ControlPlaneExperimentalFeatureDescriptor descriptor, bool enabled)
    {
        return new
        {
            name = descriptor.Name,
            stage = descriptor.Stage,
            displayName = descriptor.DisplayName,
            description = descriptor.Description,
            announcement = descriptor.Announcement,
            enabled,
            defaultEnabled = descriptor.DefaultEnabled,
        };
    }

    public static bool ResolveFeatureEnabledState(
        ControlPlaneExperimentalFeatureDescriptor descriptor,
        IReadOnlyDictionary<string, string> values)
    {
        foreach (var key in new[]
        {
            $"features.{descriptor.Name}",
            $"features.\"{descriptor.Name}\"",
            $"experimental_features.{descriptor.Name}",
            $"experimental_features.\"{descriptor.Name}\"",
            $"experimentalFeatures.{descriptor.Name}",
            $"experimentalFeatures.\"{descriptor.Name}\"",
        })
        {
            if (!values.TryGetValue(key, out var raw))
            {
                continue;
            }

            var scalar = KernelTomlTextParsingUtilities.ReadScalarConfigValue(raw);
            if (bool.TryParse(scalar, out var parsed))
            {
                return parsed;
            }
        }

        return descriptor.DefaultEnabled;
    }

    public static IReadOnlyList<ControlPlaneCollaborationModeDescriptor> BuildCollaborationModeMasks()
    {
        return
        [
            new ControlPlaneCollaborationModeDescriptor
            {
                Name = "Plan",
                Mode = "plan",
                ReasoningEffort = "medium",
            },
            new ControlPlaneCollaborationModeDescriptor
            {
                Name = "Default",
                Mode = "default",
            },
        ];
    }

    public static object ToCollaborationModePayload(ControlPlaneCollaborationModeDescriptor descriptor)
    {
        return new
        {
            name = descriptor.Name,
            mode = descriptor.Mode,
            model = descriptor.Model,
            reasoning_effort = descriptor.ReasoningEffort,
        };
    }

    private static ControlPlaneExperimentalFeatureDescriptor CreateUnifiedExecExperimentalFeatureDescriptor()
        => new()
        {
            Name = "unified_exec",
            Stage = "stable",
            DefaultEnabled = !OperatingSystem.IsWindows(),
        };

    private static ControlPlaneExperimentalFeatureDescriptor CreatePowershellUtf8ExperimentalFeatureDescriptor()
        => OperatingSystem.IsWindows()
            ? new ControlPlaneExperimentalFeatureDescriptor { Name = "powershell_utf8", Stage = "stable", DefaultEnabled = true }
            : new ControlPlaneExperimentalFeatureDescriptor { Name = "powershell_utf8", Stage = "underDevelopment", DefaultEnabled = false };

    private static ControlPlaneExperimentalFeatureDescriptor CreatePreventIdleSleepExperimentalFeatureDescriptor()
        => OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows()
            ? new ControlPlaneExperimentalFeatureDescriptor
            {
                Name = "prevent_idle_sleep",
                Stage = "beta",
                DisplayName = "Prevent sleep while running",
                Description = "Keep your computer awake while TianShu is running a thread.",
                Announcement = "NEW: Prevent sleep while running is now available in /experimental.",
                DefaultEnabled = false,
            }
            : new ControlPlaneExperimentalFeatureDescriptor { Name = "prevent_idle_sleep", Stage = "underDevelopment", DefaultEnabled = false };
}
