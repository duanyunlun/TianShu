using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;

namespace TianShu.Configuration.Tests;

public sealed class TianShuConfigurationSchemaConvergenceTests
{
    [Fact]
    public void SchemaCatalog_CoversAllFormalConfigurationAreas()
    {
        var snapshot = new TianShuConfigurationSchemaCatalog().GetSnapshot();
        var fields = snapshot.Fields.Select(static field => field.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = snapshot.Groups.ToDictionary(static group => group.Id, StringComparer.OrdinalIgnoreCase);

        AssertRepresentativeField("host", "host.surface", ConfigurationCategoryIds.DiagnosticsState);
        AssertRepresentativeField("control", "governance_profiles.*.approval_queue", ConfigurationCategoryIds.SecurityGovernance);
        AssertRepresentativeField("kernel", "kernel.enabled", ConfigurationCategoryIds.KernelCore);
        AssertRepresentativeField("execution", "execution.profiles.*.timeout_ms", ConfigurationCategoryIds.ExecutionRuntime);
        AssertRepresentativeField("modules", "modules.discovery_roots", ConfigurationCategoryIds.ModulePlane);
        AssertRepresentativeField("providers", "providers.*.base_url", ConfigurationCategoryIds.ConnectivityModel);
        AssertRepresentativeField("tools", "tools.*.implementation_id", ConfigurationCategoryIds.CapabilitiesTools);
        AssertRepresentativeField("plugins", "plugins.enabled", ConfigurationCategoryIds.CapabilitiesTools);
        AssertRepresentativeField("apps", "apps.connectors.*.enabled", ConfigurationCategoryIds.CapabilitiesTools);
        AssertRepresentativeField("memory", "memory.providers.*.root", ConfigurationCategoryIds.IdentityMemory);
        AssertRepresentativeField("diagnostics", "diagnostics.events_jsonl", ConfigurationCategoryIds.DiagnosticsState);
        AssertRepresentativeField("workspace", "workspace_profiles.default.trust_policy", ConfigurationCategoryIds.Workspace);
        AssertRepresentativeField("experience", "review_profiles.*.include_diff", ConfigurationCategoryIds.Experience);

        void AssertRepresentativeField(string area, string fieldKey, string categoryId)
        {
            Assert.Contains(fieldKey, fields);
            var field = Assert.Single(snapshot.Fields, field => string.Equals(field.Key, fieldKey, StringComparison.OrdinalIgnoreCase));
            Assert.True(groups.TryGetValue(field.GroupId, out var group), $"{area}: field `{fieldKey}` references unknown group `{field.GroupId}`.");
            Assert.Equal(categoryId, group!.CategoryId);
        }
    }

    [Fact]
    public void EnvironmentLayer_OnlyAcceptsExplicitSchemaPrefixAndOverridesUserLayer()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"tianshu-config-plane-{Guid.NewGuid():N}.toml");
        try
        {
            File.WriteAllText(
                configPath,
                """
                provider = "openai"

                [providers.openai]
                api_key_env = "OPENAI_API_KEY"
                """);

            var projection = new TianShuConfigurationTomlProjectionLoader().LoadFileWithEnvironmentLayer(
                configPath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TIANSHU_CONFIG__PROVIDER"] = "anthropic",
                    ["TIANSHU_CONFIG__PROVIDERS__ANTHROPIC__API_KEY_ENV"] = "ANTHROPIC_API_KEY",
                    ["TIANSHU_PROVIDER"] = "legacy-ignored",
                });

            var provider = Assert.Single(projection.Values, static value => value.Key == "provider");
            Assert.Equal("anthropic", provider.Value?.StringValue);
            Assert.Equal("environment", provider.SourceLayerId);
            Assert.Contains(projection.Sources, static source => source.Kind == ConfigurationSourceKind.Environment);
            Assert.DoesNotContain(projection.Values, static value => value.Key == "tianshu_provider");
            Assert.Equal("ANTHROPIC_API_KEY", Assert.Single(projection.Values, static value => value.Key == "providers.anthropic.api_key_env").Value?.StringValue);
        }
        finally
        {
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }
        }
    }

    [Fact]
    public void LegacyAliasFieldsRemainRawUnmappedAndNeverEnterFormalFacts()
    {
        var projection = new TianShuConfigurationProjectionBuilder().Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                Layer("user", ConfigurationSourceKind.User, 20, new Dictionary<string, StructuredValue>
                {
                    ["modelProvider"] = StructuredValue.FromString("anthropic"),
                    ["providers.openai.apiKey"] = StructuredValue.FromString("plain-secret"),
                    ["mcpServers.demo.url"] = StructuredValue.FromString("https://legacy.example/mcp"),
                    ["enabledTools"] = StructuredValue.FromArray([StructuredValue.FromString("shell")]),
                    ["provider"] = StructuredValue.FromString("openai"),
                    ["providers.openai.api_key_env"] = StructuredValue.FromString("OPENAI_API_KEY"),
                }),
            ],
        });

        foreach (var key in new[] { "modelProvider", "providers.openai.apiKey", "mcpServers.demo.url", "enabledTools" })
        {
            var field = Assert.Single(projection.Fields, field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(TianShuConfigurationSchemaCatalog.RawUnmappedGroupId, field.GroupId);
            Assert.Contains(projection.Issues, issue => issue.Code == ConfigurationIssueCodes.FieldUnmapped && string.Equals(issue.FieldKey, key, StringComparison.OrdinalIgnoreCase));
        }

        var facts = new TianShuConfigurationFactsBuilder().Build(projection);
        Assert.Contains(facts.Issues, static issue => issue.Code == ConfigurationIssueCodes.FormalFactsRejectedUnmapped);
        Assert.DoesNotContain(facts.Modules.Tools, static tool => string.Equals(tool.ToolId, "enabledTools", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(facts.Modules.Tools, static tool => string.Equals(tool.SourceLayerId, "user", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("OPENAI_API_KEY", Assert.Single(facts.Modules.Providers).SecretReferences.Single());
    }

    [Fact]
    public void ChangeApplier_RoutesModuleOwnedConfigurationOutsideRootTianShuToml()
    {
        var home = Path.Combine(Path.GetTempPath(), $"tianshu-config-routing-{Guid.NewGuid():N}");
        var userConfigPath = Path.Combine(home, "tianshu.toml");

        try
        {
            var applier = new TianShuConfigurationTomlChangeApplier();
            var result = applier.ApplyRouted(userConfigPath, new ConfigurationChangeSet
            {
                Changes =
                [
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Set,
                        Key = "model_route_sets.default.display_name",
                        Value = StructuredValue.FromString("Default Routes"),
                    },
                    new ConfigurationChange
                    {
                        Operation = ConfigurationChangeOperation.Set,
                        Key = "providers.openai.api_key_env",
                        Value = StructuredValue.FromString("OPENAI_API_KEY"),
                    },
                ],
            });

            Assert.True(result.Applied);
            var routeSetPath = Path.Combine(home, "modules", "model", "route-sets", "default.toml");
            var providerInstancesPath = Path.Combine(home, "modules", "model", "provider-instances", "default.toml");
            Assert.True(File.Exists(routeSetPath), routeSetPath);
            Assert.True(File.Exists(providerInstancesPath), providerInstancesPath);
            Assert.False(File.Exists(userConfigPath));

            Assert.Contains("display_name = \"Default Routes\"", File.ReadAllText(routeSetPath), StringComparison.Ordinal);
            Assert.Contains("api_key_env = \"OPENAI_API_KEY\"", File.ReadAllText(providerInstancesPath), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(home))
            {
                Directory.Delete(home, recursive: true);
            }
        }
    }

    [Fact]
    public void ConfigSchemaDocs_DoNotReintroduceRemovedLegacyRootFieldsAsFormalInputs()
    {
        var repoRoot = FindRepositoryRoot();
        var configDoc = File.ReadAllText(Path.Combine(repoRoot, "docs", "config", "tianshu-config-schema.md"));
        var architectureSpec = File.ReadAllText(Path.Combine(repoRoot, "docs", "tianshu-architecture-spec.md"));
        var installScript = File.ReadAllText(Path.Combine(repoRoot, "tools", "Install-TianShuCli.ps1"));

        Assert.DoesNotContain("modelProvider", configDoc.Replace("`modelProvider`", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", configDoc.Replace("`apiKey`", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("modules/prompts/<package>/prompt.toml", configDoc, StringComparison.Ordinal);
        Assert.Contains("Provider Module", architectureSpec, StringComparison.Ordinal);
        Assert.DoesNotContain("default_prompt.toml\" / \"prompt/", installScript, StringComparison.Ordinal);
        Assert.Contains("openai/gpt-5.5/openai_responses", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("anthropic/claude-opus-4.8/anthropic_messages", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("openai-compatible/openai-compatible-default/openai_chat_completions", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("OpenAI Responses", configDoc, StringComparison.Ordinal);
        Assert.Contains("Anthropic Messages", configDoc, StringComparison.Ordinal);
        Assert.Contains("OpenAI Chat Completions-compatible", configDoc, StringComparison.Ordinal);
        Assert.Contains("[providers.openai]", installScript, StringComparison.Ordinal);
        Assert.Contains("api_key_env = \"OPENAI_API_KEY\"", installScript, StringComparison.Ordinal);
        Assert.Contains("default_protocol = \"openai_responses\"", installScript, StringComparison.Ordinal);
        Assert.Contains("[providers.anthropic]", installScript, StringComparison.Ordinal);
        Assert.Contains("api_key_env = \"ANTHROPIC_API_KEY\"", installScript, StringComparison.Ordinal);
        Assert.Contains("default_protocol = \"anthropic_messages\"", installScript, StringComparison.Ordinal);
        Assert.Contains("model_overrides = [{ name = \"openai-compatible-default\", protocols = [\"openai_chat_completions\"] }]", installScript, StringComparison.Ordinal);
        Assert.Contains("{ kind = \"review\", candidates = [{ provider = \"anthropic\", model = \"claude-opus-4.8\", protocol = \"anthropic_messages\" }] }", installScript, StringComparison.Ordinal);
        Assert.DoesNotContain("protocol_fallbacks = [\"openai_chat_completions\", \"openai_responses\", \"anthropic_messages\", \"google_generative\"]", installScript, StringComparison.Ordinal);
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试运行目录定位 TianShu 仓库根目录。");
    }
}
