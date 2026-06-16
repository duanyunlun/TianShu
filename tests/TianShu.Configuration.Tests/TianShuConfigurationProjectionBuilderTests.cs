using TianShu.Contracts.Configuration;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;
using TianShu.AppHost.Configuration;

namespace TianShu.Configuration.Tests;

public sealed class TianShuConfigurationProjectionBuilderTests
{
    [Fact]
    public void DefaultRouteKinds_AreDerivedFromBuiltInStages()
    {
        Assert.Equal(
            BuiltInStageDefinitions.All.Select(static stage => stage.ModelRouteKind.Value).ToArray(),
            TianShuModelRouteSetDefaults.DefaultRouteKinds);
        Assert.DoesNotContain("vision", TianShuModelRouteSetDefaults.DefaultRouteKinds);
        Assert.DoesNotContain("embedding", TianShuModelRouteSetDefaults.DefaultRouteKinds);
        Assert.DoesNotContain("rerank", TianShuModelRouteSetDefaults.DefaultRouteKinds);
    }

    [Fact]
    public void BuildRouteDiagnostic_DoesNotFallbackUnknownRouteKindToDefault()
    {
        var config = new Dictionary<string, object?>
        {
            ["model_route_set"] = "default",
            ["model_route_sets"] = new Dictionary<string, object?>
            {
                ["default"] = new Dictionary<string, object?>
                {
                    ["routes"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["kind"] = "default",
                            ["candidates"] = new object?[]
                            {
                                new Dictionary<string, object?>
                                {
                                    ["provider"] = "openai",
                                    ["model"] = "gpt-5",
                                },
                            },
                        },
                    },
                },
            },
        };

        var diagnostic = TianShuModelRouteSetDefaults.BuildRouteDiagnostic(config, "custom_stage");

        Assert.Equal("custom_stage", diagnostic.RequestedRouteKind);
        Assert.Null(diagnostic.ResolvedRouteKind);
        Assert.Null(diagnostic.PreferredCandidate);
        Assert.Empty(diagnostic.Candidates);
        Assert.Contains("Stage Registry", diagnostic.RouteFallbackReason, StringComparison.Ordinal);
        Assert.Equal(TianShuModelRouteSetDefaults.DefaultRouteKinds, diagnostic.RegisteredRouteKinds);
        Assert.Equal(["default"], diagnostic.ConfiguredRegisteredRouteKinds);
        Assert.Contains("coding", diagnostic.MissingRegisteredRouteKinds);
        Assert.Empty(diagnostic.UnknownRouteKinds);
    }

    [Fact]
    public void Build_UsesLaterLayerAsEffectiveValue()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("system", ConfigurationSourceKind.System, 10, new Dictionary<string, StructuredValue>
                {
                    ["model"] = StructuredValue.FromString("system-model"),
                }),
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["model"] = StructuredValue.FromString("user-model"),
                }),
            ],
        });

        var value = Assert.Single(projection.Values, static value => value.Key == "model");
        Assert.Equal("user-model", value.Value?.StringValue);
        Assert.Equal("user", value.SourceLayerId);
    }

    [Fact]
    public void Build_AddsUnmappedFieldsWithoutDroppingValues()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["custom.future_option"] = StructuredValue.FromString("enabled"),
                }),
            ],
        });

        var field = Assert.Single(projection.Fields, static field => field.Key == "custom.future_option");
        Assert.Equal(TianShuConfigurationSchemaCatalog.RawUnmappedGroupId, field.GroupId);
        Assert.Contains(projection.Issues, static issue => issue.Code == "config.field.unmapped");
    }

    [Fact]
    public void Build_MapsProviderWildcardKeysToHumanReadableFields()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["providers.openai.base_url"] = StructuredValue.FromString("https://api.openai.com"),
                }),
            ],
        });

        var field = Assert.Single(projection.Fields, static field => field.Key == "providers.openai.base_url");
        Assert.Equal("provider", field.GroupId);
        Assert.Contains("openai", field.DisplayName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(projection.Fields, static field => field.Key == "providers.*.base_url");
        Assert.DoesNotContain(projection.Issues, static issue => issue.FieldKey == "providers.openai.base_url");
    }

    [Fact]
    public void Build_AddsDynamicProviderChoicesToProviderReferenceFields()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["provider"] = StructuredValue.FromString("openai-compatible"),
                    ["provider"] = StructuredValue.FromString("openai"),
                    ["execution_profiles.default.provider"] = StructuredValue.FromString("openai"),
                    ["providers.openai-compatible.base_url"] = StructuredValue.FromString("https://openai-compatible.example"),
                    ["providers.openai.display_name"] = StructuredValue.FromString("OpenAI"),
                    ["providers.openai.base_url"] = StructuredValue.FromString("https://api.openai.com"),
                    ["memory.providers.local.kind"] = StructuredValue.FromString("local"),
                }),
            ],
        });

        var providerField = Assert.Single(projection.Fields, static field => field.Key == "provider");
        Assert.Equal(ConfigurationValueKind.String, providerField.ValueKind);
        Assert.Equal(["openai", "openai-compatible"], providerField.AllowedValues.Select(static value => value.Value.StringValue!).ToArray());
        Assert.Contains(providerField.AllowedValues, static value => value.DisplayName == "openai (OpenAI)");

        var profileProviderField = Assert.Single(projection.Fields, static field => field.Key == "execution_profiles.default.provider");
        Assert.Equal(["openai", "openai-compatible"], profileProviderField.AllowedValues.Select(static value => value.Value.StringValue!).ToArray());
        Assert.DoesNotContain(profileProviderField.AllowedValues, static value => value.Value.StringValue == "local");

        var legacyProviderField = Assert.Single(projection.Fields, static field => field.Key == "provider");
        Assert.Equal(["openai", "openai-compatible"], legacyProviderField.AllowedValues.Select(static value => value.Value.StringValue!).ToArray());
    }

    [Fact]
    public void Build_MapsProviderReasoningProtocolRulesToDocumentedField()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["providers.openai-compatible.reasoning.protocol_rules"] = StructuredValue.FromArray([
                        StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                        {
                            ["match"] = StructuredValue.FromString("claude*"),
                            ["protocol"] = StructuredValue.FromString("anthropic_messages"),
                        }),
                    ]),
                }),
            ],
        });

        var field = Assert.Single(projection.Fields, static field => field.Key == "providers.openai-compatible.reasoning.protocol_rules");
        Assert.Equal("provider", field.GroupId);
        Assert.Contains("reasoning", field.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(projection.Issues, static issue => issue.FieldKey == "providers.openai-compatible.reasoning.protocol_rules");
    }

    [Fact]
    public void Build_MapsToolProfileAndProviderFieldsToCapabilitiesCategory()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["tool_profiles.default.enabled"] = StructuredValue.FromArray([StructuredValue.FromString("read_file")]),
                    ["tools.read_file.implementation_id"] = StructuredValue.FromString("read_file"),
                    ["tools.read_file.implementation_kind"] = StructuredValue.FromString("managed"),
                    ["tools.read_file.fallback"] = StructuredValue.FromString("managed-search"),
                    ["tool_providers.company.type"] = StructuredValue.FromString("assembly"),
                    ["tool_providers.company.provider_type"] = StructuredValue.FromString("Company.Tools.CompanyToolProvider"),
                    ["plugins.enabled"] = StructuredValue.FromBoolean(true),
                    ["plugins.installed.sample@debug.enabled"] = StructuredValue.FromBoolean(true),
                    ["plugins.marketplace_trust.require_signer"] = StructuredValue.FromBoolean(true),
                    ["plugins.remote_marketplaces.lab.url"] = StructuredValue.FromString("https://plugins.example.test/marketplace.json"),
                    ["apps.connectors.calendar.enabled"] = StructuredValue.FromBoolean(true),
                }),
            ],
        });

        Assert.Equal("tools", Assert.Single(projection.Fields, static field => field.Key == "tool_profiles.default.enabled").GroupId);
        Assert.Equal("tools", Assert.Single(projection.Fields, static field => field.Key == "tools.read_file.implementation_id").GroupId);
        Assert.Equal("tools", Assert.Single(projection.Fields, static field => field.Key == "tools.read_file.implementation_kind").GroupId);
        Assert.Equal("tools", Assert.Single(projection.Fields, static field => field.Key == "tools.read_file.fallback").GroupId);
        Assert.Equal("tools", Assert.Single(projection.Fields, static field => field.Key == "tool_providers.company.type").GroupId);
        Assert.Equal("plugins_apps", Assert.Single(projection.Fields, static field => field.Key == "plugins.enabled").GroupId);
        Assert.Equal("plugins_apps", Assert.Single(projection.Fields, static field => field.Key == "plugins.installed.sample@debug.enabled").GroupId);
        Assert.Equal("plugins_apps", Assert.Single(projection.Fields, static field => field.Key == "plugins.marketplace_trust.require_signer").GroupId);
        Assert.Equal("plugins_apps", Assert.Single(projection.Fields, static field => field.Key == "plugins.remote_marketplaces.lab.url").GroupId);
        Assert.Equal("plugins_apps", Assert.Single(projection.Fields, static field => field.Key == "apps.connectors.calendar.enabled").GroupId);
        Assert.DoesNotContain(projection.Issues, static issue => issue.FieldKey?.StartsWith("tools.", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(projection.Issues, static issue => issue.FieldKey?.StartsWith("tool_providers.", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(projection.Issues, static issue => issue.FieldKey?.StartsWith("plugins.", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(projection.Issues, static issue => issue.FieldKey?.StartsWith("apps.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Build_OrdersFieldsByConfigurationModule()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["providers.openai.base_url"] = StructuredValue.FromString("https://api.openai.com"),
                    ["agents.default.model"] = StructuredValue.FromString("gpt-test"),
                    ["permission_profiles.default.approval"] = StructuredValue.FromString("never"),
                }),
            ],
        });
        var keys = projection.Fields.Select(static field => field.Key).ToArray();

        Assert.True(Array.IndexOf(keys, "schema_version") < Array.IndexOf(keys, "providers.openai.base_url"));
        Assert.True(Array.IndexOf(keys, "providers.openai.base_url") < Array.IndexOf(keys, "agents.default.model"));
        Assert.True(Array.IndexOf(keys, "agents.default.model") < Array.IndexOf(keys, "permission_profiles.default.approval"));
        Assert.True(Array.IndexOf(keys, "permission_profiles.default.approval") < Array.IndexOf(keys, "memory.enabled"));
    }

    [Fact]
    public void SchemaCatalog_CoversFormalConfigPlanes()
    {
        var fields = new TianShuConfigurationSchemaCatalog()
            .GetSnapshot()
            .Fields;

        Assert.DoesNotContain(fields.GroupBy(static field => field.Key, StringComparer.OrdinalIgnoreCase), static group => group.Count() > 1);
        Assert.Contains(fields, static field => field.Key == "profiles.*.execution");
        Assert.Contains(fields, static field => field.Key == "providers.*.protocol_fallbacks");
        Assert.Contains(fields, static field => field.Key == "providers.*.reasoning.protocol_rules");
        Assert.Contains(fields, static field => field.Key == "providers.*.default_protocol");
        Assert.DoesNotContain(fields, static field => field.Key == "providers.*.protocol");
        Assert.DoesNotContain(fields, static field => field.Key == "providers.*." + "wire" + "_" + "api");
        Assert.Contains(fields, static field => field.Key == "models.*.context_window");
        Assert.DoesNotContain(fields, static field => field.Key == "models.*.protocol");
        Assert.Contains(fields, static field => field.Key == "model_route_set");
        Assert.Contains(fields, static field => field.Key == "model_route_sets.*.display_name");
        Assert.Contains(fields, static field => field.Key == "model_route_sets.*.routes");
        Assert.Contains(fields, static field => field.Key == "stage_registry.stages");
        Assert.Contains(fields, static field => field.Key == "stage_registry.stages.*.model_route_kind");
        Assert.Contains(fields, static field => field.Key == "agents.*.model_route_set");
        Assert.Contains(fields, static field => field.Key == "execution_profiles.*.model_route_set");
        Assert.Contains(fields, static field => field.Key == "session_profiles.*.model_route_set");
        Assert.Contains(fields, static field => field.Key == "workspace_profiles.default.trust_policy");
        Assert.Contains(fields, static field => field.Key == "workspace_profiles.default.model_lock");
        Assert.Contains(fields, static field => field.Key == "permission_profiles.*.allow_network");
        Assert.Contains(fields, static field => field.Key == "mcp.servers.*.command");
        Assert.Contains(fields, static field => field.Key == "memory.providers.*.root");
        Assert.Contains(fields, static field => field.Key == "memory.providers.*.host");
        Assert.Contains(fields, static field => field.Key == "memory.providers.*.port");
        Assert.Contains(fields, static field => field.Key == "memory.providers.*.grpc_port");
        Assert.Contains(fields, static field => field.Key == "memory.providers.*.api_key_env");
        Assert.Contains(fields, static field => field.Key == "memory.providers.*.authorization_env");
        Assert.Contains(fields, static field => field.Key == "memory.providers.*.display_name");
        Assert.Contains(fields, static field => field.Key == "memory.providers.*.connect_timeout_ms");
        Assert.Contains(fields, static field => field.Key == "memory.providers.*.capabilities");
        Assert.Contains(fields, static field => field.Key == "tools.*.implementation_id");
        Assert.Contains(fields, static field => field.Key == "tools.*.implementation_kind");
        Assert.Contains(fields, static field => field.Key == "tools.*.fallback");
        Assert.Contains(fields, static field => field.Key == "tool_providers.*.assembly_path");
        Assert.Contains(fields, static field => field.Key == "tool_providers.*.provider_type");
        Assert.Contains(fields, static field => field.Key == "runtime.typed_surface_first");
        Assert.Contains(fields, static field => field.Key == "review_profiles.*.include_diff");
        Assert.Contains(fields, static field => field.Key == "requires.require_secret_indirection");
        Assert.All(
            fields.Where(static field => field.ValueKind == ConfigurationValueKind.Enum && field.AllowedValues.Count > 0),
            static field => Assert.All(field.AllowedValues, static value => Assert.False(string.IsNullOrWhiteSpace(value.Description))));
    }

    [Fact]
    public void SchemaCatalog_CoversKernelExecutionAndModuleConfigFacts()
    {
        var fields = new TianShuConfigurationSchemaCatalog()
            .GetSnapshot()
            .Fields;

        Assert.Contains(fields, static field => field.Key == "kernel.enabled");
        Assert.Contains(fields, static field => field.Key == "kernel.default_graph_id");
        Assert.Contains(fields, static field => field.Key == "kernel.adaptive.allowed_kernel_tools");
        Assert.Contains(fields, static field => field.Key == "kernel.strategy.promotion_gate");
        Assert.Contains(fields, static field => field.Key == "kernel.budget.token_budget");
        Assert.Contains(fields, static field => field.Key == "kernel.validation.require_governance_envelope");
        Assert.Contains(fields, static field => field.Key == "execution.default_profile");
        Assert.Contains(fields, static field => field.Key == "execution.profiles.*.timeout_ms");
        Assert.Contains(fields, static field => field.Key == "execution.profiles.*.side_effect_ceiling");
        Assert.Contains(fields, static field => field.Key == "modules.discovery_roots");
        Assert.Contains(fields, static field => field.Key == "modules.providers.*.descriptor_ref");
        Assert.Contains(fields, static field => field.Key == "modules.*.trust_level");
    }

    [Fact]
    public void Build_ReportsFormalSchemaFailClosedIssues()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["kernel.strategy.promotion_gate"] = StructuredValue.FromString("auto_promote"),
                    ["kernel.budget.token_budget"] = StructuredValue.FromNumber("-1"),
                    ["execution.profiles.default.max_parallelism"] = StructuredValue.FromNumber("0"),
                    ["providers.openai.api_key"] = StructuredValue.FromString("plain-secret"),
                    ["providers.openai.api_key_env"] = StructuredValue.FromString("OPENAI_API_KEY"),
                    ["providers.openai.api_key_secret"] = StructuredValue.FromString("secret://openai"),
                    ["modules.tools.shell.enabled"] = StructuredValue.FromBoolean(true),
                }),
            ],
        });

        Assert.Contains(projection.Issues, static issue => issue.Code == ConfigurationIssueCodes.EnumInvalid);
        Assert.Contains(projection.Issues, static issue => issue.Code == ConfigurationIssueCodes.BudgetNegative && issue.FieldKey == "kernel.budget.token_budget");
        Assert.Contains(projection.Issues, static issue => issue.Code == ConfigurationIssueCodes.BudgetNegative && issue.FieldKey == "execution.profiles.default.max_parallelism");
        Assert.Contains(projection.Issues, static issue => issue.Code == ConfigurationIssueCodes.SecretPlaintextForbidden && issue.FieldKey == "providers.openai.api_key");
        Assert.Contains(projection.Issues, static issue => issue.Code == ConfigurationIssueCodes.MutualExclusionConflict);
        Assert.Contains(projection.Issues, static issue => issue.Code == ConfigurationIssueCodes.RequiredFieldMissing && issue.FieldKey == "modules.tools.shell.descriptor_ref");
    }

    [Fact]
    public void FactsBuilder_ProjectsFormalFactsAndRejectsUnmappedInputs()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("system", ConfigurationSourceKind.System, 10, new Dictionary<string, StructuredValue>
                {
                    ["kernel.enabled"] = StructuredValue.FromBoolean(true),
                    ["kernel.budget.token_budget"] = StructuredValue.FromNumber("1000"),
                    ["execution.default_profile"] = StructuredValue.FromString("default"),
                    ["execution.profiles.default.timeout_ms"] = StructuredValue.FromNumber("30000"),
                    ["modules.discovery_roots"] = StructuredValue.FromArray([StructuredValue.FromString("modules")]),
                    ["modules.providers.openai.descriptor_ref"] = StructuredValue.FromString("module://providers/openai"),
                    ["modules.providers.openai.enabled"] = StructuredValue.FromBoolean(true),
                }),
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["kernel.budget.token_budget"] = StructuredValue.FromNumber("2000"),
                    ["custom.future"] = StructuredValue.FromString("ignored"),
                }),
            ],
        });

        var facts = new TianShuConfigurationFactsBuilder().Build(projection);

        Assert.Equal(2000, facts.Kernel.TokenBudget);
        Assert.Equal(["system", "user"], facts.Kernel.SourceLayerIds);
        Assert.Equal("default", facts.Execution.DefaultProfile);
        Assert.Equal(30000, Assert.Single(facts.Execution.Profiles).TimeoutMs);
        Assert.Equal("modules", Assert.Single(facts.Modules.DiscoveryRoots));
        Assert.Equal("openai", Assert.Single(facts.Modules.Entries).ModuleId);
        Assert.Contains(facts.Issues, static issue => issue.Code == ConfigurationIssueCodes.FormalFactsRejectedUnmapped);
        Assert.DoesNotContain(facts.Modules.Entries, static entry => entry.ModuleId == "future");
    }

    [Fact]
    public void FactsBuilder_DoesNotPromoteProviderToolMemoryDiagnosticsOrWorkspaceFieldsIntoExecutionFacts()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["providers.openai.base_url"] = StructuredValue.FromString("https://api.openai.com"),
                    ["providers.openai.api_key_env"] = StructuredValue.FromString("OPENAI_API_KEY"),
                    ["tools.shell.enabled"] = StructuredValue.FromBoolean(true),
                    ["tools.shell.implementation_kind"] = StructuredValue.FromString("managed"),
                    ["memory.providers.local.root"] = StructuredValue.FromString(".tianshu/memory"),
                    ["memory.spaces.default.read_only"] = StructuredValue.FromBoolean(true),
                    ["diagnostics.events_jsonl"] = StructuredValue.FromString(".tianshu/diagnostics/events.jsonl"),
                    ["diagnostics.redact_secrets"] = StructuredValue.FromBoolean(true),
                    ["workspace_profiles.default.default_workspace"] = StructuredValue.FromString("D:/work"),
                    ["workspace_profiles.default.trust_policy"] = StructuredValue.FromString("trusted-only"),
                }),
            ],
        });

        var facts = new TianShuConfigurationFactsBuilder().Build(projection);

        Assert.Empty(facts.Kernel.SourceLayerIds);
        Assert.Equal("default", facts.Execution.DefaultProfile);
        Assert.Empty(facts.Execution.Profiles);
        Assert.Empty(facts.Execution.SourceLayerIds);
        Assert.Empty(facts.Modules.DiscoveryRoots);
        Assert.Empty(facts.Modules.Entries);
        Assert.Empty(facts.Modules.SourceLayerIds);
        Assert.DoesNotContain(facts.Issues, static issue => issue.Code == ConfigurationIssueCodes.FormalFactsRejectedUnmapped);
    }

    [Fact]
    public void FactsBuilder_ProjectsModuleSpecificProviderToolMemoryDiagnosticsAndWorkspaceFacts()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["providers.openai.display_name"] = StructuredValue.FromString("OpenAI"),
                    ["providers.openai.kind"] = StructuredValue.FromString("openai"),
                    ["providers.openai.transport"] = StructuredValue.FromString("http"),
                    ["providers.openai.base_url"] = StructuredValue.FromString("https://api.openai.com/v1"),
                    ["providers.openai.api_key_env"] = StructuredValue.FromString("OPENAI_API_KEY"),
                    ["providers.openai.default_protocol"] = StructuredValue.FromString("openai_responses"),
                    ["providers.openai.protocol_fallbacks"] = StructuredValue.FromArray([StructuredValue.FromString("openai_chat_completions")]),
                    ["providers.openai.supports_streaming"] = StructuredValue.FromBoolean(true),
                    ["models.gpt5.provider"] = StructuredValue.FromString("openai"),
                    ["models.gpt5.name"] = StructuredValue.FromString("gpt-5"),
                    ["models.gpt5.display_name"] = StructuredValue.FromString("GPT-5"),
                    ["models.gpt5.context_window"] = StructuredValue.FromNumber("200000"),
                    ["models.gpt5.protocols"] = StructuredValue.FromArray([StructuredValue.FromString("openai_responses")]),
                    ["tools.shell.timeout_seconds"] = StructuredValue.FromNumber("120"),
                    ["tools.shell.environment_policy"] = StructuredValue.FromString("inherit-safe"),
                    ["tools.filesystem.max_read_bytes"] = StructuredValue.FromNumber("2048"),
                    ["tools.filesystem.write_requires_approval"] = StructuredValue.FromBoolean(true),
                    ["tools.custom.enabled"] = StructuredValue.FromBoolean(false),
                    ["tools.custom.implementation_id"] = StructuredValue.FromString("custom.impl"),
                    ["tools.custom.implementation_kind"] = StructuredValue.FromString("managed"),
                    ["tools.custom.approval"] = StructuredValue.FromString("on-request"),
                    ["memory.enabled"] = StructuredValue.FromBoolean(true),
                    ["memory.default_profile"] = StructuredValue.FromString("default"),
                    ["memory.spaces.default.scope"] = StructuredValue.FromString("workspace"),
                    ["memory.spaces.default.provider"] = StructuredValue.FromString("local"),
                    ["memory.spaces.default.read_only"] = StructuredValue.FromBoolean(true),
                    ["memory.providers.local.kind"] = StructuredValue.FromString("local-vector"),
                    ["memory.providers.local.mode"] = StructuredValue.FromString("read-write"),
                    ["memory.providers.local.root"] = StructuredValue.FromString(".tianshu/memory"),
                    ["memory.providers.local.api_key_env"] = StructuredValue.FromString("MEMORY_API_KEY"),
                    ["memory.bindings.primary.space"] = StructuredValue.FromString("default"),
                    ["memory.bindings.primary.provider"] = StructuredValue.FromString("local"),
                    ["memory.bindings.primary.mode"] = StructuredValue.FromString("read-only"),
                    ["diagnostics.enabled"] = StructuredValue.FromBoolean(true),
                    ["diagnostics.default_level"] = StructuredValue.FromString("artifact"),
                    ["diagnostics.level"] = StructuredValue.FromString("debug"),
                    ["diagnostics.events_jsonl"] = StructuredValue.FromString(".tianshu/diagnostics/events.jsonl"),
                    ["diagnostics.redact_secrets"] = StructuredValue.FromBoolean(true),
                    ["workspace_profiles.default.root_markers"] = StructuredValue.FromArray([StructuredValue.FromString(".git")]),
                    ["workspace_profiles.default.default_workspace"] = StructuredValue.FromString("D:/work"),
                    ["workspace_profiles.default.trust_policy"] = StructuredValue.FromString("trusted-only"),
                    ["workspace_profiles.default.model_lock"] = StructuredValue.FromString("snapshot-on-create"),
                    ["projects.repo.path"] = StructuredValue.FromString("D:/work/repo"),
                    ["projects.repo.trust_level"] = StructuredValue.FromString("trusted"),
                    ["projects.repo.config_allowed"] = StructuredValue.FromBoolean(true),
                }),
            ],
        });

        var facts = new TianShuConfigurationFactsBuilder().Build(projection);

        var provider = Assert.Single(facts.Modules.Providers);
        Assert.Equal("openai", provider.ProviderId);
        Assert.Equal("https://api.openai.com/v1", provider.Endpoint);
        Assert.Equal("openai_responses", provider.DefaultProtocol);
        Assert.Equal(["openai_chat_completions"], provider.ProtocolCapabilities);
        Assert.Equal(["OPENAI_API_KEY"], provider.SecretReferences);
        var model = Assert.Single(provider.ModelCatalog);
        Assert.Equal("gpt5", model.ModelId);
        Assert.Equal("gpt-5", model.NativeName);
        Assert.Equal(200000, model.ContextWindow);

        var customTool = Assert.Single(facts.Modules.Tools, static tool => tool.ToolId == "custom");
        Assert.False(customTool.Enabled);
        Assert.Equal("custom.impl", customTool.ImplementationBinding);
        Assert.Equal("managed", customTool.ImplementationKind);
        Assert.Equal("on-request", customTool.PermissionDeclaration.ApprovalPolicy);
        var shellTool = Assert.Single(facts.Modules.Tools, static tool => tool.ToolId == "shell");
        Assert.Equal(120, shellTool.SideEffectProfile.TimeoutSeconds);
        Assert.Equal("inherit-safe", shellTool.AuditProfile.EnvironmentPolicy);
        var filesystemTool = Assert.Single(facts.Modules.Tools, static tool => tool.ToolId == "filesystem");
        Assert.True(filesystemTool.PermissionDeclaration.WriteRequiresApproval);
        Assert.Equal(2048, filesystemTool.SideEffectProfile.MaxReadBytes);

        Assert.Equal("default", facts.Modules.Memory.DefaultProfile);
        Assert.True(Assert.Single(facts.Modules.Memory.Spaces).ReadOnly);
        Assert.Equal(["MEMORY_API_KEY"], Assert.Single(facts.Modules.Memory.Providers).SecretReferences);
        Assert.Equal("read-only", Assert.Single(facts.Modules.Memory.Bindings).Mode);

        Assert.Equal("artifact", facts.Modules.Diagnostics.DefaultLevel);
        Assert.Equal("debug", facts.Modules.Diagnostics.LogLevel);
        Assert.Equal(".tianshu/diagnostics/events.jsonl", facts.Modules.Diagnostics.EventsJsonl);
        Assert.True(facts.Modules.Diagnostics.RedactSecrets);

        var workspace = Assert.Single(facts.Modules.Workspace.Profiles);
        Assert.Equal("default", workspace.ProfileId);
        Assert.Equal([".git"], workspace.RootMarkers);
        Assert.Equal("trusted-only", workspace.TrustPolicy);
        var project = Assert.Single(facts.Modules.Workspace.Projects);
        Assert.Equal("repo", project.ProjectId);
        Assert.Equal("trusted", project.TrustLevel);
        Assert.True(project.ConfigAllowed);

        Assert.Empty(facts.Kernel.SourceLayerIds);
        Assert.Empty(facts.Execution.SourceLayerIds);
    }

    [Fact]
    public void Build_MapsModelRouteSetRoutesToTypedFields()
    {
        var routes = CompleteRoutes([
            Route(
                "coding",
                [
                    Candidate("openai", "gpt-5-coding", "openai_responses", ["code", "tools"]),
                    Candidate("anthropic", "claude-opus", "anthropic_messages", ["code"]),
                ],
                fallback: "default"),
            Route(
                "default",
                [
                    Candidate("openai", "gpt-5", "openai_responses"),
                ]),
        ]);

        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["providers.openai.base_url"] = StructuredValue.FromString("https://api.openai.com"),
                    ["providers.anthropic.base_url"] = StructuredValue.FromString("https://api.anthropic.com"),
                    ["model_route_sets.default.display_name"] = StructuredValue.FromString("Default Route Set"),
                    ["model_route_sets.default.routes"] = routes,
                    ["agents.default.model_route_set"] = StructuredValue.FromString("default"),
                    ["execution_profiles.default.model_route_set"] = StructuredValue.FromString("default"),
                    ["session_profiles.default.model_route_set"] = StructuredValue.FromString("default"),
                }),
            ],
        });

        Assert.Equal("model_route_set", Assert.Single(projection.Fields, static field => field.Key == "model_route_sets.default.routes").GroupId);
        Assert.Equal("agent", Assert.Single(projection.Fields, static field => field.Key == "agents.default.model_route_set").GroupId);
        Assert.Equal("execution", Assert.Single(projection.Fields, static field => field.Key == "execution_profiles.default.model_route_set").GroupId);
        Assert.Equal("session", Assert.Single(projection.Fields, static field => field.Key == "session_profiles.default.model_route_set").GroupId);
        Assert.DoesNotContain(projection.Issues, static issue => issue.FieldKey == "model_route_sets.default.routes");

        var value = Assert.Single(projection.Values, static value => value.Key == "model_route_sets.default.routes").Value;
        Assert.NotNull(value);
        var coding = value.Items[0];
        var candidates = coding.GetProperty("candidates").Items;
        Assert.Equal("openai", candidates[0].GetProperty("provider").StringValue);
        Assert.Equal("anthropic", candidates[1].GetProperty("provider").StringValue);
        Assert.Equal("default", coding.GetProperty("fallback").StringValue);
    }

    [Fact]
    public void Build_WhenModelRouteSetMissing_ReportsRouteSetReferenceGap()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["provider"] = StructuredValue.FromString("openai-compatible"),
                    ["model"] = StructuredValue.FromString("gpt-5.5"),
                    ["providers.openai-compatible.api_key_env"] = StructuredValue.FromString("OPENAI_COMPATIBLE_API_KEY"),
                }),
            ],
        });

        var routeSetId = Assert.Single(projection.Values, static value => value.Key == "model_route_set");
        Assert.False(routeSetId.IsConfigured);
        Assert.Equal("default", routeSetId.Value?.StringValue);

        Assert.DoesNotContain(projection.Values, static value => value.Key == "model_route_sets.default.routes");
        var issue = Assert.Single(projection.Issues, static issue => issue.Code == "config.model_route_set.reference_unknown");
        Assert.Equal("model_route_set", issue.FieldKey);
        Assert.Equal("OPENAI_COMPATIBLE_API_KEY", Assert.Single(projection.Values, static value => value.Key == "providers.openai-compatible.api_key_env").Value?.StringValue);
    }

    [Fact]
    public void ModelRouteSetDefaults_ExportsStableTomlWithoutSecrets()
    {
        var toml = TianShuModelRouteSetDefaults.ExportDefaultRouteSetToml(
            provider: "openai-compatible",
            model: "gpt-5.5",
            protocol: "auto");

        Assert.Contains("model_route_set = \"default\"", toml, StringComparison.Ordinal);
        Assert.Contains("kind = \"default\"", toml, StringComparison.Ordinal);
        Assert.Contains("kind = \"fast\"", toml, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key", toml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("base_url", toml, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            toml.IndexOf("kind = \"default\"", StringComparison.Ordinal) <
            toml.IndexOf("kind = \"planning\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_ReportsModelRouteSetValidationIssues()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["providers.openai.base_url"] = StructuredValue.FromString("https://api.openai.com"),
                    ["model_route_sets.empty.routes"] = CompleteRoutes([Route("default", [])]),
                    ["model_route_sets.unknown_provider.routes"] = CompleteRoutes([Route("default", [Candidate("anthropic", "claude-opus")])]),
                    ["model_route_sets.duplicate.routes"] = CompleteRoutes([Route("default", [Candidate("openai", "gpt-5"), Candidate("openai", "gpt-5")])]),
                    ["model_route_sets.duplicate_route.routes"] = StructuredValue.FromArray([
                        Route("default", [Candidate("openai", "gpt-5")]),
                        Route("default", [Candidate("openai", "gpt-5-mini")]),
                    ]),
                    ["model_route_sets.missing_stage.routes"] = StructuredValue.FromArray([Route("default", [Candidate("openai", "gpt-5")])]),
                    ["model_route_sets.bad_fallback.routes"] = CompleteRoutes([Route("coding", [Candidate("openai", "gpt-5")], fallback: "missing_review")]),
                    ["agents.default.model_route_set"] = StructuredValue.FromString("missing"),
                }),
            ],
        });

        Assert.Contains(projection.Issues, static issue => issue.Code == "config.model_route_set.candidates_empty");
        Assert.Contains(projection.Issues, static issue => issue.Code == "config.model_route_set.candidate_provider_unknown");
        Assert.Contains(projection.Issues, static issue => issue.Code == "config.model_route_set.candidate_duplicate");
        Assert.Contains(projection.Issues, static issue => issue.Code == "config.model_route_set.route_kind_duplicate");
        Assert.Contains(projection.Issues, static issue => issue.Code == "config.model_route_set.route_kind_missing_registered");
        Assert.Contains(projection.Issues, static issue => issue.Code == "config.model_route_set.fallback_route_unknown");
        Assert.Contains(projection.Issues, static issue => issue.Code == "config.model_route_set.reference_unknown");
        Assert.All(
            projection.Issues.Where(static issue => issue.Code.StartsWith("config.model_route_set.", StringComparison.Ordinal)),
            static issue => Assert.Equal(ConfigurationIssueSeverity.Error, issue.Severity));
    }

    [Fact]
    public void Build_ErrorsForUnregisteredModelRouteKind()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["providers.openai.base_url"] = StructuredValue.FromString("https://api.openai.com"),
                    ["model_route_sets.default.routes"] = CompleteRoutes([Route("custom_stage", [Candidate("openai", "gpt-5")])]),
                }),
            ],
        });

        var issue = Assert.Single(projection.Issues, static issue => issue.Code == "config.model_route_set.route_kind_unknown");
        Assert.Equal(ConfigurationIssueSeverity.Error, issue.Severity);
        Assert.Contains("Stage Registry", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenStageRegistryDefinesCustomStage_AllowsCustomModelRouteKind()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["providers.openai.base_url"] = StructuredValue.FromString("https://api.openai.com"),
                    ["stage_registry.stages"] = StructuredValue.FromArray([
                        StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                        {
                            ["id"] = StructuredValue.FromString("triage"),
                            ["display_name"] = StructuredValue.FromString("Triage"),
                            ["lifecycle_order"] = StructuredValue.FromNumber("5"),
                            ["model_route_kind"] = StructuredValue.FromString("triage"),
                            ["allowed_previous"] = StructuredValue.FromArray([StructuredValue.FromString("default")]),
                        }),
                    ]),
                    ["model_route_sets.default.routes"] = CompleteRoutes([
                        Route("triage", [Candidate("openai", "gpt-5", "openai_responses")]),
                    ]),
                }),
            ],
        });

        Assert.DoesNotContain(projection.Issues, static issue => issue.Code == "config.model_route_set.route_kind_unknown");
        Assert.DoesNotContain(
            projection.Issues,
            static issue => issue.Code == "config.model_route_set.route_kind_missing_registered"
                            && issue.Message.Contains("triage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MemoryProviderConfigurationLoader_ShouldReadExternalProviderOptions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tianshu-memory-provider-{Guid.NewGuid():N}.toml");
        File.WriteAllText(
            path,
            """
            [memory.providers.local_vector]
            kind = "qdrant"
            enabled = true
            display_name = "Local Vector Memory"
            host = "203.0.113.10"
            port = 8231
            grpc_port = 50059
            api_key_env = "TIANSHU_MEMORY_API_KEY"
            authorization_env = "TIANSHU_MEMORY_AUTHORIZATION"
            mode = "read-only"
            connect_timeout_ms = 250
            capabilities = ["semantic-search", "embedding-indexing", "llm-extraction"]
            """);

        try
        {
            var provider = Assert.Single(TianShuMemoryProviderConfigurationLoader.LoadFile(path));

            Assert.Equal("local_vector", provider.ProviderId);
            Assert.Equal("qdrant", provider.Kind);
            Assert.True(provider.Enabled);
            Assert.Equal("Local Vector Memory", provider.DisplayName);
            Assert.Equal("203.0.113.10", provider.Host);
            Assert.Equal(8231, provider.Port);
            Assert.Equal(50059, provider.GrpcPort);
            Assert.Equal("TIANSHU_MEMORY_API_KEY", provider.ApiKeyEnvironmentVariable);
            Assert.Equal("TIANSHU_MEMORY_AUTHORIZATION", provider.AuthorizationEnvironmentVariable);
            Assert.Equal("read-only", provider.Mode);
            Assert.Equal(250, provider.ConnectTimeoutMs);
            Assert.Equal(["semantic-search", "embedding-indexing", "llm-extraction"], provider.Capabilities);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SchemaCatalog_EnumDescriptionsAreUserReadable()
    {
        var fields = new TianShuConfigurationSchemaCatalog()
            .GetSnapshot()
            .Fields;

        var enumValues = fields
            .Where(static field => field.ValueKind == ConfigurationValueKind.Enum && field.AllowedValues.Count > 0)
            .SelectMany(static field => field.AllowedValues.Select(value => (field.Key, Value: value.Value.StringValue!, Description: value.Description!)))
            .ToArray();

        Assert.DoesNotContain(enumValues, static value => value.Description.Contains("选择 `", StringComparison.Ordinal));
        Assert.DoesNotContain(enumValues, static value => value.Description.Contains("缺少专门说明", StringComparison.Ordinal));

        var shellPolicy = Assert.Single(enumValues, static value => value.Key == "tools.shell.environment_policy" && value.Value == "inherit-safe");
        Assert.Contains("过滤", shellPolicy.Description, StringComparison.Ordinal);
        Assert.Contains("推荐", shellPolicy.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadFile_FlattensTomlIntoProjection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tianshu-config-{Guid.NewGuid():N}.toml");
        File.WriteAllText(
            path,
            """
            model = "gpt-test"

            [diagnostics]
            enabled = true
            default_level = "artifact"
            """);

        try
        {
            var projection = new TianShuConfigurationTomlProjectionLoader().LoadFile(path);

            Assert.Equal("gpt-test", Assert.Single(projection.Values, static value => value.Key == "model").Value?.StringValue);
            Assert.Equal("artifact", Assert.Single(projection.Values, static value => value.Key == "diagnostics.default_level").Value?.StringValue);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFileWithEnvironmentLayer_UsesOnlyExplicitSchemaEnvironmentVariables()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tianshu-config-{Guid.NewGuid():N}.toml");
        File.WriteAllText(
            path,
            """
            [kernel]
            enabled = false
            """);

        try
        {
            var projection = new TianShuConfigurationTomlProjectionLoader().LoadFileWithEnvironmentLayer(
                path,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TIANSHU_CONFIG__KERNEL__ENABLED"] = "true",
                    ["TIANSHU_CONFIG__EXECUTION__PROFILES__DEFAULT__TIMEOUT_MS"] = "30000",
                    ["TIANSHU_API_KEY"] = "must-not-enter-projection",
                });

            var kernelEnabled = Assert.Single(projection.Values, static value => value.Key == "kernel.enabled");
            Assert.True(kernelEnabled.Value?.BooleanValue);
            Assert.Equal("environment", kernelEnabled.SourceLayerId);
            Assert.Equal(30000, Assert.Single(projection.Values, static value => value.Key == "execution.profiles.default.timeout_ms").Value?.GetInt32());
            Assert.DoesNotContain(projection.Values, static value => value.Key.Contains("api_key", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(projection.Sources, static source => source.Kind == ConfigurationSourceKind.Environment);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadUserFileWithModelRouteSetModules_LayersRouteSetFilesBeforeRootConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tianshu-config-home-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "tianshu.toml");
        var routeSetDirectory = Path.Combine(root, "modules", "model", "route-sets");
        var routeSetPath = Path.Combine(routeSetDirectory, "default.toml");
        Directory.CreateDirectory(routeSetDirectory);
        File.WriteAllText(
            routeSetPath,
            """
            [model_route_sets.default]
            display_name = "Module Default"
            description = "Loaded from module file"
            routes = [
              { kind = "default", candidates = [{ provider = "module", model = "module-model" }] },
            ]
            """);
        File.WriteAllText(
            path,
            """
            model_route_set = "default"

            [model_route_sets.default]
            display_name = "Root Override"
            """);

        try
        {
            var projection = new TianShuConfigurationTomlProjectionLoader().LoadUserFileWithModelRouteSetModules(path);

            Assert.Equal("Root Override", Assert.Single(projection.Values, static value => value.Key == "model_route_sets.default.display_name").Value?.StringValue);
            Assert.Equal("Loaded from module file", Assert.Single(projection.Values, static value => value.Key == "model_route_sets.default.description").Value?.StringValue);
            var routes = Assert.Single(projection.Values, static value => value.Key == "model_route_sets.default.routes").Value;
            Assert.NotNull(routes);
            Assert.Equal(StructuredValueKind.Array, routes.Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ChangeApplier_UpdatesTomlAndPreservesOtherValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tianshu-config-{Guid.NewGuid():N}.toml");
        File.WriteAllText(
            path,
            """
            model = "old-model"

            [diagnostics]
            enabled = true
            """);

        try
        {
            var applier = new TianShuConfigurationTomlChangeApplier();
            var result = applier.Apply(path, new ConfigurationChangeSet
            {
                Changes =
                [
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Set,
                        Key = "model",
                        Value = StructuredValue.FromString("new-model"),
                    },
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Set,
                        Key = "providers.demo.base_url",
                        Value = StructuredValue.FromString("https://example.test"),
                    },
                ],
            });

            Assert.True(result.Applied);
            var projection = new TianShuConfigurationTomlProjectionLoader().LoadFile(path);
            Assert.Equal("new-model", Assert.Single(projection.Values, static value => value.Key == "model").Value?.StringValue);
            Assert.Equal("https://example.test", Assert.Single(projection.Values, static value => value.Key == "providers.demo.base_url").Value?.StringValue);
            Assert.True(Assert.Single(projection.Values, static value => value.Key == "diagnostics.enabled").Value?.BooleanValue);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ChangeApplier_CanUnsetProviderTable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tianshu-config-{Guid.NewGuid():N}.toml");
        File.WriteAllText(
            path,
            """
            provider = "openai"

            [providers.openai]
            base_url = "https://api.openai.com"
            api_key_env = "OPENAI_API_KEY"
            default_protocol = "openai_responses"

            [providers.local]
            base_url = "http://localhost"
            """);

        try
        {
            var result = new TianShuConfigurationTomlChangeApplier().Apply(path, new ConfigurationChangeSet
            {
                Changes =
                [
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Unset,
                        Key = "providers.openai",
                    },
                ],
            });

            Assert.True(result.Applied);
            var projection = new TianShuConfigurationTomlProjectionLoader().LoadFile(path);
            Assert.DoesNotContain(projection.Values, static value => value.Key.StartsWith("providers.openai.", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(projection.Values, static value => value.Key == "providers.local.base_url");
            Assert.Equal("openai", Assert.Single(projection.Values, static value => value.Key == "provider").Value?.StringValue);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ChangeApplier_ApplyRouted_WritesKnownModuleKeysToModuleFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tianshu-config-home-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "tianshu.toml");
        Directory.CreateDirectory(root);
        File.WriteAllText(
            path,
            """
            profile = "default"
            provider_instances = "default"
            """);

        try
        {
            var applier = new TianShuConfigurationTomlChangeApplier();
            var result = applier.ApplyRouted(path, new ConfigurationChangeSet
            {
                Changes =
                [
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Set,
                        Key = "agents.default.model",
                        Value = StructuredValue.FromString("module-agent-model"),
                    },
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Set,
                        Key = "memory.providers.local.root",
                        Value = StructuredValue.FromString("./data/memory"),
                    },
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Set,
                        Key = "profiles.default.agent",
                        Value = StructuredValue.FromString("default"),
                    },
                ],
            });

            Assert.True(result.Applied);
            Assert.Contains("profile = \"default\"", File.ReadAllText(path));
            Assert.Contains("[profiles.default]", File.ReadAllText(path));
            Assert.DoesNotContain("[agents.default]", File.ReadAllText(path));
            Assert.DoesNotContain("[memory.providers.local]", File.ReadAllText(path));
            Assert.Contains(
                "model = \"module-agent-model\"",
                File.ReadAllText(Path.Combine(root, "modules", "agent", "agents", "default.toml")));
            Assert.Contains(
                "root = \"./data/memory\"",
                File.ReadAllText(Path.Combine(root, "modules", "memory", "providers", "local.toml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ChangeApplier_WritesModelRouteSetRoutesWithoutTouchingUnrelatedSections()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tianshu-config-{Guid.NewGuid():N}.toml");
        File.WriteAllText(
            path,
            """
            provider = "openai"
            default_prompt = "default"

            [providers.openai]
            base_url = "https://api.openai.com"
            api_key_env = "OPENAI_API_KEY"

            [prompts.default]
            path = "prompt/default.md"
            """);

        try
        {
            var routes = CompleteRoutes([
                Route(
                    "coding",
                    [
                        Candidate("openai", "gpt-5-coding", "openai_responses", ["code"]),
                        Candidate("openai", "gpt-5-mini", "openai_responses", ["fast"]),
                    ],
                    fallback: "default"),
                Route("default", [Candidate("openai", "gpt-5", "openai_responses")]),
            ]);
            var changeSet = new ConfigurationChangeSet
            {
                Changes =
                [
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Set,
                        Key = "model_route_sets.default.routes",
                        Value = routes,
                    },
                ],
            };
            var applier = new TianShuConfigurationTomlChangeApplier();

            var preview = applier.Preview(path, changeSet);
            var afterRoutes = Assert.Single(preview.After.Values, static value => value.Key == "model_route_sets.default.routes").Value;
            Assert.NotNull(afterRoutes);
            Assert.Equal("gpt-5-coding", afterRoutes.Items[0].GetProperty("candidates").Items[0].GetProperty("model").StringValue);
            Assert.Equal("gpt-5-mini", afterRoutes.Items[0].GetProperty("candidates").Items[1].GetProperty("model").StringValue);

            var result = applier.Apply(path, changeSet);

            Assert.True(result.Applied);
            var projection = new TianShuConfigurationTomlProjectionLoader().LoadFile(path);
            Assert.Equal("openai", Assert.Single(projection.Values, static value => value.Key == "provider").Value?.StringValue);
            Assert.Equal("OPENAI_API_KEY", Assert.Single(projection.Values, static value => value.Key == "providers.openai.api_key_env").Value?.StringValue);
            Assert.Equal("prompt/default.md", Assert.Single(projection.Values, static value => value.Key == "prompts.default.path").Value?.StringValue);
            var persistedRoutes = Assert.Single(projection.Values, static value => value.Key == "model_route_sets.default.routes").Value;
            Assert.NotNull(persistedRoutes);
            Assert.Equal("coding", persistedRoutes.Items[0].GetProperty("kind").StringValue);
            Assert.Equal("openai", persistedRoutes.Items[0].GetProperty("candidates").Items[0].GetProperty("provider").StringValue);
            Assert.Equal("gpt-5-mini", persistedRoutes.Items[0].GetProperty("candidates").Items[1].GetProperty("model").StringValue);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ChangeApplier_ParsesArrayAndObjectInputsWithoutDynamicJsonDeserialization()
    {
        var arrayValue = TianShuConfigurationTomlChangeApplier.ParseUserInput(
            """["AGENTS.md", ".git", 42, true]""",
            ConfigurationValueKind.Array);
        var objectValue = TianShuConfigurationTomlChangeApplier.ParseUserInput(
            """{"Authorization":"Bearer ${TOKEN}","Retry":2}""",
            ConfigurationValueKind.Object);

        Assert.Equal(StructuredValueKind.Array, arrayValue.Kind);
        Assert.Equal("AGENTS.md", arrayValue.Items[0].StringValue);
        Assert.Equal("42", arrayValue.Items[2].NumberValue);
        Assert.True(arrayValue.Items[3].BooleanValue);

        Assert.Equal(StructuredValueKind.Object, objectValue.Kind);
        Assert.Equal("Bearer ${TOKEN}", objectValue.GetProperty("Authorization").StringValue);
        Assert.Equal("2", objectValue.GetProperty("Retry").NumberValue);
    }

    [Fact]
    public void ChangeApplier_WritesArrayAndObjectValuesReadableByProjectionLoader()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tianshu-config-{Guid.NewGuid():N}.toml");

        try
        {
            var applier = new TianShuConfigurationTomlChangeApplier();
            var result = applier.Apply(path, new ConfigurationChangeSet
            {
                Changes =
                [
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Set,
                        Key = "workspace_profiles.default.root_markers",
                        Value = TianShuConfigurationTomlChangeApplier.ParseUserInput(
                            """["AGENTS.md", ".git"]""",
                            ConfigurationValueKind.Array),
                    },
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Set,
                        Key = "providers.demo.headers",
                        Value = TianShuConfigurationTomlChangeApplier.ParseUserInput(
                            """{"X-Test":"yes","Retry":2}""",
                            ConfigurationValueKind.Object),
                    },
                ],
            });

            Assert.True(result.Applied);
            var projection = new TianShuConfigurationTomlProjectionLoader().LoadFile(path);
            var rootMarkers = Assert.Single(projection.Values, static value => value.Key == "workspace_profiles.default.root_markers").Value;
            var headerText = Assert.Single(projection.Values, static value => value.Key == "providers.demo.headers.X-Test").Value;
            var retry = Assert.Single(projection.Values, static value => value.Key == "providers.demo.headers.Retry").Value;

            Assert.NotNull(rootMarkers);
            Assert.Equal(StructuredValueKind.Array, rootMarkers.Kind);
            Assert.Equal(["AGENTS.md", ".git"], rootMarkers.Items.Select(static item => item.StringValue ?? string.Empty).ToArray());
            Assert.Equal("yes", headerText?.StringValue);
            Assert.Equal("2", retry?.NumberValue);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ChangeApplier_RejectsJsonInputWithWrongContainerKind()
    {
        Assert.Throws<FormatException>(() => TianShuConfigurationTomlChangeApplier.ParseUserInput(
            """{"not":"array"}""",
            ConfigurationValueKind.Array));
        Assert.Throws<FormatException>(() => TianShuConfigurationTomlChangeApplier.ParseUserInput(
            """["not-object"]""",
            ConfigurationValueKind.Object));
    }

    private static StructuredValue Route(string kind, IReadOnlyList<StructuredValue> candidates, string? fallback = null)
    {
        var properties = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["kind"] = StructuredValue.FromString(kind),
            ["candidates"] = StructuredValue.FromArray(candidates),
        };

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            properties["fallback"] = StructuredValue.FromString(fallback);
        }

        return StructuredValue.FromObject(properties);
    }

    private static StructuredValue CompleteRoutes(IReadOnlyList<StructuredValue> routeOverrides)
    {
        var routes = routeOverrides.ToList();
        var configuredKinds = routeOverrides
            .Select(static route => route.GetProperty("kind").StringValue)
            .Where(static kind => !string.IsNullOrWhiteSpace(kind))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var requiredKind in TianShuModelRouteSetDefaults.DefaultRouteKinds)
        {
            if (configuredKinds.Contains(requiredKind))
            {
                continue;
            }

            routes.Add(Route(requiredKind, [Candidate("openai", "gpt-5", "openai_responses")]));
        }

        return StructuredValue.FromArray(routes);
    }

    private static StructuredValue Candidate(
        string provider,
        string model,
        string? protocol = null,
        IReadOnlyList<string>? capabilities = null)
    {
        var properties = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["provider"] = StructuredValue.FromString(provider),
            ["model"] = StructuredValue.FromString(model),
        };

        if (!string.IsNullOrWhiteSpace(protocol))
        {
            properties["protocol"] = StructuredValue.FromString(protocol);
        }

        if (capabilities is not null)
        {
            properties["capabilities"] = StructuredValue.FromArray(capabilities.Select(StructuredValue.FromString).ToArray());
        }

        return StructuredValue.FromObject(properties);
    }

    private static ConfigurationLayerSnapshot Layer(
        string id,
        ConfigurationSourceKind kind,
        int order,
        IReadOnlyDictionary<string, StructuredValue> values)
        => new()
        {
            Source = new ConfigurationSourceLayer
            {
                Id = id,
                Kind = kind,
                DisplayName = id,
                Order = order,
                Exists = true,
                IsWritable = kind == ConfigurationSourceKind.User,
            },
            Values = values,
        };
}
