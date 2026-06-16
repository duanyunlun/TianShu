using System.Globalization;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.Configuration;

/// <summary>
/// 从配置投影构造 Kernel / Execution / Module 可消费的正式配置事实。
/// Builds formal Kernel / Execution / Module configuration facts from a projection.
/// </summary>
public sealed class TianShuConfigurationFactsBuilder
{
    public ConfigurationFacts Build(ConfigurationProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var fieldsByKey = projection.Fields.ToDictionary(static field => field.Key, StringComparer.OrdinalIgnoreCase);
        var valuesByKey = projection.Values.ToDictionary(static value => value.Key, StringComparer.OrdinalIgnoreCase);
        var issues = new List<ConfigurationIssue>(projection.Issues);
        var formalValues = new Dictionary<string, ConfigurationFieldValue>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in projection.Values)
        {
            if (!fieldsByKey.TryGetValue(value.Key, out var field)
                || string.Equals(field.GroupId, TianShuConfigurationSchemaCatalog.RawUnmappedGroupId, StringComparison.OrdinalIgnoreCase))
            {
                if (value.IsConfigured)
                {
                    issues.Add(new ConfigurationIssue
                    {
                        Severity = ConfigurationIssueSeverity.Warning,
                        Code = ConfigurationIssueCodes.FormalFactsRejectedUnmapped,
                        Message = $"配置键 `{value.Key}` 未进入正式 schema，不会进入 Kernel、Execution Runtime 或 Module Plane 配置事实。",
                        FieldKey = value.Key,
                        SourceLayerId = value.SourceLayerId,
                    });
                }

                continue;
            }

            formalValues[value.Key] = value;
        }

        return new ConfigurationFacts
        {
            Kernel = BuildKernelFacts(formalValues),
            Execution = BuildExecutionFacts(formalValues),
            Modules = BuildModuleFacts(formalValues),
            Issues = issues,
        };
    }

    private static KernelConfigurationFacts BuildKernelFacts(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
        => new()
        {
            Enabled = ReadBool(values, "kernel.enabled") ?? true,
            DefaultGraphId = ReadString(values, "kernel.default_graph_id"),
            AdaptiveOrchestrationEnabled = ReadBool(values, "kernel.adaptive.enabled") ?? false,
            AllowedKernelTools = ReadStringArray(values, "kernel.adaptive.allowed_kernel_tools"),
            MaxProposalsPerTurn = ReadInt(values, "kernel.adaptive.max_proposals_per_turn"),
            StrategyDefaultRegistry = ReadString(values, "kernel.strategy.default_registry"),
            StrategyPromotionGate = ReadString(values, "kernel.strategy.promotion_gate"),
            StrategyTrialRuns = ReadInt(values, "kernel.strategy.trial_runs"),
            TokenBudget = ReadLong(values, "kernel.budget.token_budget"),
            TimeBudgetMs = ReadLong(values, "kernel.budget.time_budget_ms"),
            CostBudget = ReadDecimal(values, "kernel.budget.cost_budget"),
            RetryBudget = ReadInt(values, "kernel.budget.retry_budget"),
            ToolCallBudget = ReadInt(values, "kernel.budget.tool_call_budget"),
            FailClosedValidation = ReadBool(values, "kernel.validation.fail_closed") ?? true,
            RequireGovernanceEnvelope = ReadBool(values, "kernel.validation.require_governance_envelope") ?? true,
            RequireTracePolicy = ReadBool(values, "kernel.validation.require_trace_policy") ?? true,
            SourceLayerIds = SourceLayerIds(values, "kernel."),
        };

    private static ExecutionConfigurationFacts BuildExecutionFacts(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
    {
        var profileIds = values.Keys
            .Select(TryReadExecutionProfileId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ExecutionConfigurationFacts
        {
            DefaultProfile = ReadString(values, "execution.default_profile") ?? "default",
            Profiles = profileIds.Select(id => new ExecutionRuntimeProfileConfigurationFacts
            {
                ProfileId = id,
                TimeoutMs = ReadLong(values, $"execution.profiles.{id}.timeout_ms"),
                StreamIdleTimeoutMs = ReadLong(values, $"execution.profiles.{id}.stream_idle_timeout_ms"),
                RetryBudget = ReadInt(values, $"execution.profiles.{id}.retry_budget"),
                MaxParallelism = ReadInt(values, $"execution.profiles.{id}.max_parallelism"),
                RequireSourceIds = ReadBool(values, $"execution.profiles.{id}.require_source_ids") ?? true,
                RequirePermissionEnvelope = ReadBool(values, $"execution.profiles.{id}.require_permission_envelope") ?? true,
                RequireTracePolicy = ReadBool(values, $"execution.profiles.{id}.require_trace_policy") ?? true,
                DiagnosticsRefRequired = ReadBool(values, $"execution.profiles.{id}.diagnostics_ref_required") ?? true,
                RuntimeTraceRefRequired = ReadBool(values, $"execution.profiles.{id}.runtime_trace_ref_required") ?? true,
                SideEffectCeiling = ReadString(values, $"execution.profiles.{id}.side_effect_ceiling") ?? "read_only",
                SourceLayerId = SourceLayerId(values, $"execution.profiles.{id}."),
            }).ToArray(),
            SourceLayerIds = SourceLayerIds(values, "execution."),
        };
    }

    private static ModuleConfigurationFacts BuildModuleFacts(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
    {
        var entries = new Dictionary<string, ModuleEntryBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in values)
        {
            if (!pair.Key.StartsWith("modules.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pair.Key, "modules.discovery_roots", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = pair.Key.Split('.');
            if (parts.Length == 4)
            {
                var key = $"{parts[1]}:{parts[2]}";
                ApplyModuleEntry(entries, key, parts[1], parts[2], parts[3], pair.Value);
            }
            else if (parts.Length == 3)
            {
                var key = $"module:{parts[1]}";
                ApplyModuleEntry(entries, key, "module", parts[1], parts[2], pair.Value);
            }
        }

        return new ModuleConfigurationFacts
        {
            DiscoveryRoots = ReadStringArray(values, "modules.discovery_roots"),
            Entries = entries.Values
                .OrderBy(static entry => entry.Area, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(static entry => entry.ToFacts())
                .ToArray(),
            Providers = BuildProviderFacts(values),
            Tools = BuildToolFacts(values),
            Memory = BuildMemoryFacts(values),
            Diagnostics = BuildDiagnosticsFacts(values),
            Workspace = BuildWorkspaceFacts(values),
            SourceLayerIds = SourceLayerIds(values, "modules."),
        };
    }

    private static IReadOnlyList<ProviderConfigurationFacts> BuildProviderFacts(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
    {
        var modelCatalog = BuildModelCatalogFacts(values);
        return CollectIds(values, "providers.")
            .Select(id => new ProviderConfigurationFacts
            {
                ProviderId = id,
                DisplayName = ReadString(values, $"providers.{id}.display_name"),
                Kind = ReadString(values, $"providers.{id}.kind"),
                Transport = ReadString(values, $"providers.{id}.transport"),
                Endpoint = ReadString(values, $"providers.{id}.base_url"),
                DefaultProtocol = ReadString(values, $"providers.{id}.default_protocol"),
                ProtocolCapabilities = ReadStringArray(values, $"providers.{id}.protocol_fallbacks"),
                ModelCatalog = modelCatalog
                    .Where(model => string.Equals(model.ProviderId, id, StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
                SecretReferences = ReadSecretReferences(values, [
                    $"providers.{id}.api_key_env",
                    $"providers.{id}.api_key_secret",
                    $"providers.{id}.organization_env",
                ]),
                SupportsStreaming = ReadBool(values, $"providers.{id}.supports_streaming") ?? true,
                SupportsWebSockets = ReadBool(values, $"providers.{id}.supports_websockets") ?? false,
                SourceLayerId = SourceLayerId(values, $"providers.{id}."),
            })
            .ToArray();
    }

    private static IReadOnlyList<ModelCatalogConfigurationFacts> BuildModelCatalogFacts(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
        => CollectIds(values, "models.")
            .Select(id => new ModelCatalogConfigurationFacts
            {
                ModelId = id,
                ProviderId = ReadString(values, $"models.{id}.provider"),
                NativeName = ReadString(values, $"models.{id}.name"),
                DisplayName = ReadString(values, $"models.{id}.display_name"),
                Family = ReadString(values, $"models.{id}.family"),
                ContextWindow = ReadLong(values, $"models.{id}.context_window"),
                ProtocolCapabilities = ReadStringArray(values, $"models.{id}.protocols"),
                Hidden = ReadBool(values, $"models.{id}.hidden") ?? false,
                SourceLayerId = SourceLayerId(values, $"models.{id}."),
            })
            .ToArray();

    private static IReadOnlyList<ToolConfigurationFacts> BuildToolFacts(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
        => CollectIds(values, "tools.")
            .Where(static id => !string.Equals(id, "shell", StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(id, "filesystem", StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(id, "patch", StringComparison.OrdinalIgnoreCase))
            .Select(id => new ToolConfigurationFacts
            {
                ToolId = id,
                Enabled = ReadBool(values, $"tools.{id}.enabled") ?? true,
                ProviderId = ReadString(values, $"tools.{id}.provider"),
                ImplementationBinding = ReadString(values, $"tools.{id}.implementation_id"),
                ImplementationKind = ReadString(values, $"tools.{id}.implementation_kind"),
                PermissionDeclaration = new ToolPermissionDeclarationFacts
                {
                    ApprovalPolicy = ReadString(values, $"tools.{id}.approval"),
                },
                SideEffectProfile = new ToolSideEffectProfileFacts
                {
                    Fallback = ReadString(values, $"tools.{id}.fallback"),
                },
                AuditProfile = new ToolAuditProfileFacts
                {
                    Priority = ReadInt(values, $"tools.{id}.priority"),
                },
                SourceLayerId = SourceLayerId(values, $"tools.{id}."),
            })
            .Concat(BuildBuiltInToolFacts(values))
            .OrderBy(static tool => tool.ToolId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<ToolConfigurationFacts> BuildBuiltInToolFacts(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
    {
        if (HasPrefix(values, "tools.shell."))
        {
            yield return new ToolConfigurationFacts
            {
                ToolId = "shell",
                PermissionDeclaration = new ToolPermissionDeclarationFacts
                {
                    ApprovalPolicy = ReadString(values, "tools.shell.approval"),
                },
                SideEffectProfile = new ToolSideEffectProfileFacts
                {
                    TimeoutSeconds = ReadInt(values, "tools.shell.timeout_seconds"),
                },
                AuditProfile = new ToolAuditProfileFacts
                {
                    WorkingDirectory = ReadString(values, "tools.shell.working_directory"),
                    EnvironmentPolicy = ReadString(values, "tools.shell.environment_policy"),
                },
                SourceLayerId = SourceLayerId(values, "tools.shell."),
            };
        }

        if (HasPrefix(values, "tools.filesystem."))
        {
            yield return new ToolConfigurationFacts
            {
                ToolId = "filesystem",
                PermissionDeclaration = new ToolPermissionDeclarationFacts
                {
                    WriteRequiresApproval = ReadBool(values, "tools.filesystem.write_requires_approval"),
                },
                SideEffectProfile = new ToolSideEffectProfileFacts
                {
                    MaxReadBytes = ReadInt(values, "tools.filesystem.max_read_bytes"),
                },
                SourceLayerId = SourceLayerId(values, "tools.filesystem."),
            };
        }

        if (HasPrefix(values, "tools.patch."))
        {
            yield return new ToolConfigurationFacts
            {
                ToolId = "patch",
                ImplementationBinding = ReadString(values, "tools.patch.engine"),
                SourceLayerId = SourceLayerId(values, "tools.patch."),
            };
        }
    }

    private static MemoryConfigurationFacts BuildMemoryFacts(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
        => new()
        {
            Enabled = ReadBool(values, "memory.enabled") ?? true,
            DefaultProfile = ReadString(values, "memory.default_profile"),
            Spaces = CollectIds(values, "memory.spaces.")
                .Select(id => new MemorySpaceConfigurationFacts
                {
                    SpaceId = id,
                    Scope = ReadString(values, $"memory.spaces.{id}.scope"),
                    ProviderId = ReadString(values, $"memory.spaces.{id}.provider"),
                    ReadOnly = ReadBool(values, $"memory.spaces.{id}.read_only") ?? false,
                    Tags = ReadStringArray(values, $"memory.spaces.{id}.tags"),
                    SourceLayerId = SourceLayerId(values, $"memory.spaces.{id}."),
                })
                .ToArray(),
            Providers = CollectIds(values, "memory.providers.")
                .Select(id => new MemoryProviderConfigurationFacts
                {
                    ProviderId = id,
                    Enabled = ReadBool(values, $"memory.providers.{id}.enabled") ?? true,
                    Kind = ReadString(values, $"memory.providers.{id}.kind"),
                    DisplayName = ReadString(values, $"memory.providers.{id}.display_name"),
                    Mode = ReadString(values, $"memory.providers.{id}.mode"),
                    Root = ReadString(values, $"memory.providers.{id}.root"),
                    Capabilities = ReadStringArray(values, $"memory.providers.{id}.capabilities"),
                    SecretReferences = ReadSecretReferences(values, [
                        $"memory.providers.{id}.api_key_env",
                        $"memory.providers.{id}.authorization_env",
                    ]),
                    SourceLayerId = SourceLayerId(values, $"memory.providers.{id}."),
                })
                .ToArray(),
            Bindings = CollectIds(values, "memory.bindings.")
                .Select(id => new MemoryBindingConfigurationFacts
                {
                    BindingId = id,
                    SpaceId = ReadString(values, $"memory.bindings.{id}.space"),
                    ProviderId = ReadString(values, $"memory.bindings.{id}.provider"),
                    Mode = ReadString(values, $"memory.bindings.{id}.mode"),
                    Capabilities = ReadStringArray(values, $"memory.bindings.{id}.capabilities"),
                    SourceLayerId = SourceLayerId(values, $"memory.bindings.{id}."),
                })
                .ToArray(),
            SourceLayerIds = SourceLayerIds(values, "memory."),
        };

    private static DiagnosticsConfigurationFacts BuildDiagnosticsFacts(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
        => new()
        {
            Enabled = ReadBool(values, "diagnostics.enabled") ?? true,
            DefaultLevel = ReadString(values, "diagnostics.default_level") ?? "stats",
            LogLevel = ReadString(values, "diagnostics.level") ?? "info",
            TraceEnabled = ReadBool(values, "diagnostics.trace") ?? true,
            RedactSecrets = ReadBool(values, "diagnostics.redact_secrets") ?? true,
            EventsJsonl = ReadString(values, "diagnostics.events_jsonl"),
            TelemetryEnabled = ReadBool(values, "diagnostics.telemetry.enabled") ?? false,
            SourceLayerIds = SourceLayerIds(values, "diagnostics."),
        };

    private static WorkspaceConfigurationFacts BuildWorkspaceFacts(IReadOnlyDictionary<string, ConfigurationFieldValue> values)
        => new()
        {
            Profiles = CollectIds(values, "workspace_profiles.")
                .Select(id => new WorkspaceProfileConfigurationFacts
                {
                    ProfileId = id,
                    RootMarkers = ReadStringArray(values, $"workspace_profiles.{id}.root_markers"),
                    DefaultWorkspace = ReadString(values, $"workspace_profiles.{id}.default_workspace"),
                    TrustPolicy = ReadString(values, $"workspace_profiles.{id}.trust_policy"),
                    ArtifactRoot = ReadString(values, $"workspace_profiles.{id}.artifact_root"),
                    StateRoot = ReadString(values, $"workspace_profiles.{id}.state_root"),
                    Model = ReadString(values, $"workspace_profiles.{id}.model"),
                    ModelLock = ReadString(values, $"workspace_profiles.{id}.model_lock"),
                    SourceLayerId = SourceLayerId(values, $"workspace_profiles.{id}."),
                })
                .ToArray(),
            Projects = CollectIds(values, "projects.")
                .Select(id => new ProjectTrustConfigurationFacts
                {
                    ProjectId = id,
                    Path = ReadString(values, $"projects.{id}.path"),
                    Trust = ReadString(values, $"projects.{id}.trust"),
                    TrustLevel = ReadString(values, $"projects.{id}.trust_level"),
                    Profile = ReadString(values, $"projects.{id}.profile"),
                    ConfigAllowed = ReadBool(values, $"projects.{id}.config_allowed"),
                    SourceLayerId = SourceLayerId(values, $"projects.{id}."),
                })
                .ToArray(),
            SourceLayerIds = SourceLayerIds(values, "workspace_profiles.")
                .Concat(SourceLayerIds(values, "projects."))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };

    private static void ApplyModuleEntry(
        Dictionary<string, ModuleEntryBuilder> entries,
        string key,
        string area,
        string id,
        string property,
        ConfigurationFieldValue value)
    {
        if (!entries.TryGetValue(key, out var entry))
        {
            entry = new ModuleEntryBuilder(area, id);
            entries[key] = entry;
        }

        entry.SourceLayerId ??= value.SourceLayerId;
        switch (property)
        {
            case "enabled":
                entry.Enabled = ReadBool(value.Value) ?? entry.Enabled;
                break;
            case "descriptor_ref":
                entry.DescriptorRef = ReadString(value.Value) ?? entry.DescriptorRef;
                break;
            case "trust_level":
                entry.TrustLevel = ReadString(value.Value) ?? entry.TrustLevel;
                break;
            case "capabilities":
                entry.Capabilities = ReadStringArray(value.Value);
                break;
            case "health_check":
                entry.HealthCheck = ReadString(value.Value) ?? entry.HealthCheck;
                break;
        }
    }

    private static string? TryReadExecutionProfileId(string key)
    {
        var parts = key.Split('.');
        return parts.Length == 4
               && string.Equals(parts[0], "execution", StringComparison.OrdinalIgnoreCase)
               && string.Equals(parts[1], "profiles", StringComparison.OrdinalIgnoreCase)
            ? parts[2]
            : null;
    }

    private static string? ReadString(IReadOnlyDictionary<string, ConfigurationFieldValue> values, string key)
        => values.TryGetValue(key, out var value) ? ReadString(value.Value) : null;

    private static string? ReadString(StructuredValue? value)
        => value?.Kind == StructuredValueKind.String ? value.StringValue : null;

    private static bool? ReadBool(IReadOnlyDictionary<string, ConfigurationFieldValue> values, string key)
        => values.TryGetValue(key, out var value) ? ReadBool(value.Value) : null;

    private static bool? ReadBool(StructuredValue? value)
        => value?.Kind == StructuredValueKind.Boolean ? value.BooleanValue : null;

    private static int? ReadInt(IReadOnlyDictionary<string, ConfigurationFieldValue> values, string key)
        => values.TryGetValue(key, out var value) && value.Value?.Kind == StructuredValueKind.Number
           && int.TryParse(value.Value.NumberValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static long? ReadLong(IReadOnlyDictionary<string, ConfigurationFieldValue> values, string key)
        => values.TryGetValue(key, out var value) && value.Value?.Kind == StructuredValueKind.Number
           && long.TryParse(value.Value.NumberValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static decimal? ReadDecimal(IReadOnlyDictionary<string, ConfigurationFieldValue> values, string key)
        => values.TryGetValue(key, out var value) && value.Value?.Kind == StructuredValueKind.Number
           && decimal.TryParse(value.Value.NumberValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static IReadOnlyList<string> ReadStringArray(IReadOnlyDictionary<string, ConfigurationFieldValue> values, string key)
        => values.TryGetValue(key, out var value) ? ReadStringArray(value.Value) : Array.Empty<string>();

    private static IReadOnlyList<string> ReadStringArray(StructuredValue? value)
        => value?.Kind == StructuredValueKind.Array
            ? value.Items
                .Where(static item => item.Kind == StructuredValueKind.String && !string.IsNullOrWhiteSpace(item.StringValue))
                .Select(static item => item.StringValue!)
                .ToArray()
            : Array.Empty<string>();

    private static IReadOnlyList<string> ReadSecretReferences(IReadOnlyDictionary<string, ConfigurationFieldValue> values, IReadOnlyList<string> keys)
        => keys
            .Select(key => ReadString(values, key))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> CollectIds(IReadOnlyDictionary<string, ConfigurationFieldValue> values, string prefix)
        => values.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(key => TryReadIdAfterPrefix(key, prefix))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? TryReadIdAfterPrefix(string key, string prefix)
    {
        var remaining = key[prefix.Length..];
        var dotIndex = remaining.IndexOf('.', StringComparison.Ordinal);
        return dotIndex > 0 ? remaining[..dotIndex] : null;
    }

    private static bool HasPrefix(IReadOnlyDictionary<string, ConfigurationFieldValue> values, string prefix)
        => values.Keys.Any(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> SourceLayerIds(IReadOnlyDictionary<string, ConfigurationFieldValue> values, string prefix)
        => values.Values
            .Where(value => value.IsConfigured
                            && value.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(value.SourceLayerId))
            .Select(static value => value.SourceLayerId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? SourceLayerId(IReadOnlyDictionary<string, ConfigurationFieldValue> values, string prefix)
        => values.Values
            .Where(value => value.IsConfigured
                            && value.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(value.SourceLayerId))
            .Select(static value => value.SourceLayerId)
            .FirstOrDefault();

    private sealed class ModuleEntryBuilder
    {
        public ModuleEntryBuilder(string area, string id)
        {
            Area = area;
            Id = id;
        }

        public string Area { get; }

        public string Id { get; }

        public bool Enabled { get; set; } = true;

        public string? DescriptorRef { get; set; }

        public string? TrustLevel { get; set; }

        public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();

        public string? HealthCheck { get; set; }

        public string? SourceLayerId { get; set; }

        public ModuleConfigurationEntryFacts ToFacts()
            => new()
            {
                ModuleArea = Area,
                ModuleId = Id,
                Enabled = Enabled,
                DescriptorRef = DescriptorRef,
                TrustLevel = TrustLevel,
                Capabilities = Capabilities,
                HealthCheck = HealthCheck,
                SourceLayerId = SourceLayerId,
            };
    }
}
