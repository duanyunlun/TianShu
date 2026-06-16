using System.Net;
using System.Text;
using System.Text.Json;
using TianShu.AppHost.Catalog;
using TianShu.AppHost.Configuration;
using TianShu.Configuration;
using TianShu.Execution.Runtime;
using TianShu.Contracts.Sessions;
using TianShu.Provider.Abstractions;
using TianShu.RuntimeComposition;
using System.Reflection;
using Tomlyn.Model;

namespace TianShu.AppHost.Configuration.Tests;

public sealed class TianShuTomlConfigurationLoaderTests
{
    private const string SystemRootOverrideEnvironmentVariable = "TIANSHU_SYSTEM_CONFIG_ROOT";

    [Fact]
    public void PrimarySolution_ShouldIncludeAppHostConfigurationTestsProject()
    {
        var solutionFile = Path.Combine(FindRepoRoot(), "TianShu.sln");
        var source = File.ReadAllText(solutionFile);

        Assert.Contains(
            "\"TianShu.AppHost.Configuration.Tests\", \"tests\\TianShu.AppHost.Configuration.Tests\\TianShu.AppHost.Configuration.Tests.csproj\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AgentRuntimeTests_Project_ShouldNotRetainAppHostConfigurationLoaderLock()
    {
        var oldFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime.Tests",
            "TianShuTomlConfigurationLoaderTests.cs");

        Assert.False(File.Exists(oldFile));
    }

    [Fact]
    public void Load_ConfigOverrides_DoNotCanonicalizeLegacyAliasKeys()
    {
        var path = WriteConfigToml(
            """
            model = "gpt-5-codex"
            provider = "openai"

            [providers.openai]
            base_url = "https://api.openai.com/v1"
            api_key_env = "OPENAI_API_KEY"
            default_protocol = "openai_responses"

            [providers.anthropic]
            base_url = "https://api.anthropic.com/v1"
            api_key_env = "ANTHROPIC_API_KEY"
            default_protocol = "anthropic_messages"
            """);

        var loader = new TianShuTomlConfigurationLoader();
        var config = loader.Load(
            path,
            profileOverride: null,
            configOverrides: new Dictionary<string, string>
            {
                ["modelProvider"] = "anthropic",
                ["providers.openai.apiKey"] = "plain-secret",
                ["mcpServers.demo.url"] = "https://legacy.example/mcp",
            });

        Assert.Equal("openai", config.ModelProvider);
        Assert.Equal("OPENAI_API_KEY", config.ProviderEnvKey);
        Assert.False(TestConfigReaders.TryReadConfiguredNestedValue(
            config.RawConfig,
            ["providers", "openai", "api_key"],
            out _));
        Assert.False(TestConfigReaders.TryReadConfiguredNestedValue(
            config.RawConfig,
            ["mcp_servers", "demo", "url"],
            out _));
    }

    [Fact]
    public void Load_WhenProfileOverridesRoot_MergesProfileAndProviderValues()
    {
        var path = WriteConfigToml(
            """
            profile = "work"
            model = "gpt-5-codex"
            provider = "openai"
            approval_policy = "never"
            sandbox_mode = "read-only"
            web_search = "off"
            service_tier = "flex"
            model_reasoning_summary = "auto"
            model_verbosity = "high"

            [profiles.work]
            model = "claude-3-7-sonnet"
            provider = "anthropic"
            approval_policy = "on-request"
            model_reasoning_summary = "concise"
            model_verbosity = "low"

            [providers.openai]
            base_url = "https://api.openai.com/v1"
            api_key_env = "OPENAI_API_KEY"
            default_protocol = "openai_responses"
            request_max_retries = 1
            stream_max_retries = 2
            stream_idle_timeout_ms = 15000
            websocket_connect_timeout_ms = 9000
            supports_websockets = true

            [providers.anthropic]
            base_url = "https://api.anthropic.com/v1"
            api_key_env = "ANTHROPIC_API_KEY"
            default_protocol = "anthropic_messages"
            request_max_retries = 3
            stream_max_retries = 4
            stream_idle_timeout_ms = 25000
            websocket_connect_timeout_ms = 11000
            supports_websockets = false
            """);

        var loader = new TianShuTomlConfigurationLoader();
        var config = loader.Load(path, profileOverride: null);

        Assert.Equal(path, config.ConfigFilePath);
        Assert.Equal("work", config.ActiveProfile);
        Assert.Equal("claude-3-7-sonnet", config.Model);
        Assert.Equal("anthropic", config.ModelProvider);
        Assert.Equal("on-request", config.ApprovalPolicy);
        Assert.Equal("read-only", config.SandboxMode);
        Assert.Equal("off", config.WebSearchMode);
        Assert.Equal("flex", config.ServiceTier);
        Assert.Equal("concise", config.ModelReasoningSummary);
        Assert.Equal("low", config.ModelVerbosity);
        Assert.Equal("https://api.anthropic.com/v1", config.ProviderBaseUrl);
        Assert.Equal("ANTHROPIC_API_KEY", config.ProviderEnvKey);
        Assert.Equal("anthropic_messages", config.ProviderWireApi);
        Assert.Equal(3, config.ProviderRequestMaxRetries);
        Assert.Equal(4, config.ProviderStreamMaxRetries);
        Assert.Equal(25000, config.ProviderStreamIdleTimeoutMs);
        Assert.Equal(11000, config.ProviderWebsocketConnectTimeoutMs);
        Assert.False(config.ProviderSupportsWebsockets);
        Assert.Equal(ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId(), config.ProtocolAdapter);
    }

    [Theory]
    [InlineData("anthropic", "https://api.example.com/v1", "anthropic_messages")]
    [InlineData("openai", "https://anthropic-proxy.example.com/v1", "openai_responses")]
    [InlineData("openai", "https://api.example.com/v1", "openai_responses")]
    public void Load_ResolvesCurrentProviderDefaultProtocol(string modelProvider, string baseUrl, string defaultProtocol)
    {
        var path = WriteConfigToml(
            $"""
            model = "test-model"
            provider = "{modelProvider}"

            [providers.{modelProvider}]
            base_url = "{baseUrl}"
            api_key_env = "TEST_API_KEY"
            default_protocol = "{defaultProtocol}"
            """);

        var loader = new TianShuTomlConfigurationLoader();
        var config = loader.Load(path, profileOverride: null);

        Assert.Equal(ProviderWireApi.NormalizeOrThrow(defaultProtocol, "test"), config.ProviderWireApi);
        Assert.Equal(ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId(), config.ProtocolAdapter);
    }

    [Fact]
    public void Load_WhenNativeAgentReasoningConfigured_ResolvesReasoningOptions()
    {
        var path = WriteConfigToml(
            """
            profile = "default"

            [profiles.default]
            agent = "default"

            [agents.default]
            model = "gpt-5.5"
            provider = "openai-compatible"

            [agents.default.reasoning]
            enabled = true
            effort = "high"
            summary = "detailed"
            verbosity = "normal"
            budget_tokens = 8192

            [providers.openai-compatible]
            base_url = "https://proxy.example"
            api_key_env = "TIANSHU_TEST_KEY"
            default_protocol = "openai_chat_completions"
            """);

        var loader = new TianShuTomlConfigurationLoader();
        var config = loader.Load(path, profileOverride: null);

        Assert.True(config.ModelReasoningEnabled);
        Assert.Equal("high", config.ModelReasoningEffort);
        Assert.Equal("detailed", config.ModelReasoningSummary);
        Assert.Equal("normal", config.ModelVerbosity);
        Assert.Equal(8192, config.ModelReasoningBudgetTokens);
    }

    [Theory]
    [InlineData("gpt-5.5", "responses")]
    [InlineData("claude-opus-4.1", "anthropic_messages")]
    [InlineData("gemini-2.5-pro", "google_generative")]
    [InlineData("deepseek-v4-flash", "anthropic_messages")]
    [InlineData("qwen3-max", "anthropic_messages")]
    [InlineData("unknown-model", "openai_chat_completions")]
    public void Load_WhenProviderCentricConfigUsesAutoProtocol_ResolvesModelProtocol(string model, string expectedProtocol)
    {
        var path = WriteConfigToml(
            $$"""
            model = "{{model}}"
            provider = "openai-compatible"

            [providers.openai-compatible]
            base_url = "https://proxy.example"
            api_key_env = "TIANSHU_TEST_KEY"
            default_protocol = "auto"
            protocol_fallbacks = ["openai_chat_completions", "openai_responses", "anthropic_messages", "google_generative"]

            [providers.openai-compatible.reasoning]
            enabled = true
            effort = "medium"
            summary = "auto"
            verbosity = "normal"
            budget_tokens = 4096
            """);

        var loader = new TianShuTomlConfigurationLoader();
        var config = loader.Load(path, profileOverride: null);

        Assert.Equal("openai-compatible", config.ModelProvider);
        Assert.Equal(expectedProtocol, config.ProviderWireApi);
        Assert.True(config.ModelReasoningEnabled);
        Assert.Equal("medium", config.ModelReasoningEffort);
        Assert.Equal("auto", config.ModelReasoningSummary);
        Assert.Equal("normal", config.ModelVerbosity);
        Assert.Equal(4096, config.ModelReasoningBudgetTokens);
    }

    [Fact]
    public void Load_WhenProviderCentricConfigDefinesProtocolRules_RulesOverrideBuiltInHeuristic()
    {
        var path = WriteConfigToml(
            """
            model = "gpt-5.5"
            provider = "openai-compatible"

            [providers.openai-compatible]
            base_url = "https://proxy.example"
            api_key_env = "TIANSHU_TEST_KEY"
            default_protocol = "auto"
            protocol_rules = [
              { match = "gpt-*", protocols = ["openai_chat_completions"] }
            ]
            """);

        var loader = new TianShuTomlConfigurationLoader();
        var config = loader.Load(path, profileOverride: null);

        Assert.Equal("openai_chat_completions", config.ProviderWireApi);
    }

    [Fact]
    public void Load_WhenProviderDefaultProtocolIsExplicit_DefaultProtocolWinsOverBuiltInModelProtocol()
    {
        var path = WriteConfigToml(
            """
            model = "openai-compatible-default"
            provider = "openai-compatible"

            [providers.openai-compatible]
            base_url = "https://proxy.example"
            api_key_env = "TIANSHU_TEST_KEY"
            default_protocol = "anthropic_messages"
            protocol_fallbacks = ["anthropic_messages", "openai_chat_completions", "openai_responses", "google_generative"]
            """);

        var loader = new TianShuTomlConfigurationLoader();
        var config = loader.Load(path, profileOverride: null);

        Assert.Equal("anthropic_messages", config.ProviderWireApi);
    }

    [Fact]
    public void Load_WhenProviderDefaultProtocolIsPresent_UsesCurrentProtocolResolution()
    {
        var path = WriteConfigToml(
            """
            model = "openai-compatible-default"
            provider = "openai-compatible"

            [providers.openai-compatible]
            base_url = "https://proxy.example"
            api_key_env = "TIANSHU_TEST_KEY"
            default_protocol = "openai_responses"
            """);

        var loader = new TianShuTomlConfigurationLoader();
        var config = loader.Load(path, profileOverride: null);

        Assert.Equal("responses", config.ProviderWireApi);
    }

    [Fact]
    public void Load_WhenProviderModelOverrideIsExplicit_OverrideWinsOverBuiltInModelProtocol()
    {
        var path = WriteConfigToml(
            """
            model = "openai-compatible-default"
            provider = "openai-compatible"

            [providers.openai-compatible]
            base_url = "https://proxy.example"
            api_key_env = "TIANSHU_TEST_KEY"
            default_protocol = "openai_chat_completions"
            model_overrides = [
              { name = "openai-compatible-default", protocols = ["anthropic_messages"] }
            ]
            """);

        var loader = new TianShuTomlConfigurationLoader();
        var config = loader.Load(path, profileOverride: null);

        Assert.Equal("anthropic_messages", config.ProviderWireApi);
    }

    [Fact]
    public void Load_WhenProviderModelOverrideDefinesProtocolPriority_UsesFirstProtocol()
    {
        var path = WriteConfigToml(
            """
            model = "openai-compatible-default"
            provider = "openai-compatible"

            [providers.openai-compatible]
            base_url = "https://proxy.example"
            api_key_env = "TIANSHU_TEST_KEY"
            default_protocol = "auto"
            protocol_fallbacks = ["openai_chat_completions", "openai_responses"]
            model_overrides = [
              { name = "openai-compatible-default", protocols = ["anthropic_messages", "openai_chat_completions"] }
            ]
            """);

        var loader = new TianShuTomlConfigurationLoader();
        var config = loader.Load(path, profileOverride: null);

        Assert.Equal("anthropic_messages", config.ProviderWireApi);
    }

    [Fact]
    public void Load_ShouldHonorProjectRootMarkersFromUserConfig_WhenResolvingProjectLayers()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = Path.Combine(Path.GetTempPath(), "tianshu-config-loader-tests", Guid.NewGuid().ToString("N"));
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var parentRoot = Path.Combine(root, "parent");
        var projectRoot = Path.Combine(parentRoot, "workspace");
        var nestedWorkspace = Path.Combine(projectRoot, "src", "feature");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var projectConfigPath = Path.Combine(projectRoot, ".tianshu", "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            var projectRootTomlPath = ToTomlPath(projectRoot);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(parentRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(projectRoot, ".tianshu"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(
                userConfigPath,
                $$"""
                model = "gpt-5"
                project_root_markers = [".project-root"]

                [projects."{{projectRootTomlPath}}"]
                trust_level = "trusted"
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(Path.Combine(projectRoot, ".project-root"), string.Empty, new UTF8Encoding(false));
            File.WriteAllText(
                projectConfigPath,
                """
                model = "claude-3-7-sonnet"
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: nestedWorkspace);

            Assert.Equal(projectConfigPath, config.ConfigFilePath);
            Assert.Equal("claude-3-7-sonnet", config.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Load_ShouldMergeWorkspaceResolverRootMarkers_WhenResolvingProjectLayers()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = Path.Combine(Path.GetTempPath(), "tianshu-config-loader-tests", Guid.NewGuid().ToString("N"));
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var projectRoot = Path.Combine(root, "workspace");
        var nestedWorkspace = Path.Combine(projectRoot, "src", "feature");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var resolverManifestPath = Path.Combine(tianShuHome, "modules", "workspace", "resolvers", "custom", "resolver.toml");
        var projectConfigPath = Path.Combine(projectRoot, ".tianshu", "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.GetDirectoryName(resolverManifestPath)!);
            Directory.CreateDirectory(Path.Combine(projectRoot, ".tianshu"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(
                userConfigPath,
                """
                model = "gpt-5"
                project_root_markers = []
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(
                resolverManifestPath,
                """
                id = "custom"
                display_name = "Custom Workspace Resolver"
                enabled = true
                type = "package"
                priority = 0

                [[resolvers]]
                id = "marker"
                enabled = true
                type = "marker"
                priority = 0
                root_markers = [".workspace-root"]
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(Path.Combine(projectRoot, ".workspace-root"), string.Empty, new UTF8Encoding(false));
            File.WriteAllText(
                projectConfigPath,
                """
                model = "claude-3-7-sonnet"
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: nestedWorkspace);

            Assert.Equal(projectConfigPath, config.ConfigFilePath);
            Assert.Equal("claude-3-7-sonnet", config.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Load_ShouldExposeWorkspaceResolverPolicySnapshot()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = Path.Combine(Path.GetTempPath(), "tianshu-config-loader-tests", Guid.NewGuid().ToString("N"));
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var resolverManifestPath = Path.Combine(tianShuHome, "modules", "workspace", "resolvers", "custom", "resolver.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.GetDirectoryName(resolverManifestPath)!);
            File.WriteAllText(
                userConfigPath,
                """
                model = "gpt-5"
                project_root_markers = [".project-root"]
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(
                resolverManifestPath,
                """
                id = "custom"
                display_name = "Custom Workspace Resolver"
                enabled = true
                type = "package"
                priority = 0

                [[resolvers]]
                id = "dotnet"
                enabled = true
                type = "marker"
                priority = 0
                root_markers = ["*.sln", "Directory.Build.props"]
                profile = "dotnet"
                trust_policy = "trusted-only"
                artifact_root = ".cache/artifacts"
                state_root = ".cache/state"
                ignore_globs = ["bin/**", "obj/**"]
                language_markers = ["*.csproj"]
                framework_markers = ["*.sln"]
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null);

            Assert.Equal([".project-root", "*.sln", "Directory.Build.props"], config.WorkspaceResolverPolicy.RootMarkers);
            Assert.Equal("dotnet", config.WorkspaceResolverPolicy.DefaultProfile);
            Assert.Equal("trusted-only", config.WorkspaceResolverPolicy.TrustPolicy);
            Assert.Equal(Path.Combine(tianShuHome, "modules", "workspace", "resolvers", "custom", ".cache", "artifacts"), config.WorkspaceResolverPolicy.ArtifactRoot);
            Assert.Equal(Path.Combine(tianShuHome, "modules", "workspace", "resolvers", "custom", ".cache", "state"), config.WorkspaceResolverPolicy.StateRoot);
            Assert.Equal(["bin/**", "obj/**"], config.WorkspaceResolverPolicy.IgnoreGlobs);
            Assert.Equal(["*.csproj"], config.WorkspaceResolverPolicy.LanguageMarkers);
            Assert.Equal(["*.sln"], config.WorkspaceResolverPolicy.FrameworkMarkers);
            var resolver = Assert.Single(config.WorkspaceResolverPolicy.Resolvers);
            Assert.Equal("custom", resolver.PackageId);
            Assert.Equal("dotnet", resolver.ResolverId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Load_UsesPolicyStrategyDefaults_WhenApprovalAndSandboxAreNotExplicit()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalSystemRoot = Environment.GetEnvironmentVariable("TIANSHU_SYSTEM_CONFIG_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "tianshu-config-loader-tests", Guid.NewGuid().ToString("N"));
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var systemRoot = Path.Combine(root, "system");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var policyManifestPath = Path.Combine(tianShuHome, "modules", "policies", "strategies", "custom", "policy.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_SYSTEM_CONFIG_ROOT", systemRoot);
            Directory.CreateDirectory(systemRoot);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.GetDirectoryName(policyManifestPath)!);
            File.WriteAllText(userConfigPath, "model = \"gpt-5\"\n", new UTF8Encoding(false));
            File.WriteAllText(
                policyManifestPath,
                """
                id = "custom"
                enabled = true
                type = "package"
                priority = 0

                [[strategies]]
                id = "default"
                enabled = true
                type = "rules"
                priority = 0
                approval_policy = "on-failure"
                sandbox_mode = "read-only"
                """,
                new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: userConfigPath, profileOverride: null, workingDirectory: null);

            Assert.Equal("on-failure", config.ApprovalPolicy);
            Assert.Equal("read-only", config.SandboxMode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_SYSTEM_CONFIG_ROOT", originalSystemRoot);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_ShouldLoadProjectLayer_ByDefault()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = Path.Combine(Path.GetTempPath(), "tianshu-config-loader-tests", Guid.NewGuid().ToString("N"));
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var projectRoot = Path.Combine(root, "workspace");
        var nestedWorkspace = Path.Combine(projectRoot, "src", "feature");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var projectConfigPath = Path.Combine(projectRoot, ".tianshu", "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(projectRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(projectRoot, ".tianshu"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(userConfigPath, "model = \"gpt-5\"\n", new UTF8Encoding(false));
            File.WriteAllText(projectConfigPath, "model = \"claude-3-7-sonnet\"\n", new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: nestedWorkspace);

            Assert.Equal(projectConfigPath, config.ConfigFilePath);
            Assert.Equal("claude-3-7-sonnet", config.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Load_ShouldIgnoreStandaloneCwdConfigToml_WhenDirectoryIsTrusted()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalSystemRoot = Environment.GetEnvironmentVariable(SystemRootOverrideEnvironmentVariable);
        var root = Path.Combine(Path.GetTempPath(), "tianshu-config-loader-tests", Guid.NewGuid().ToString("N"));
        var systemRoot = Path.Combine(root, "system");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace", "src", "feature");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var cwdConfigPath = Path.Combine(cwd, "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable(SystemRootOverrideEnvironmentVariable, systemRoot);
            Directory.CreateDirectory(systemRoot);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            File.WriteAllText(
                userConfigPath,
                """
                model = "gpt-5"
                project_root_markers = [".git"]
                """,
                new UTF8Encoding(false));
            File.WriteAllText(cwdConfigPath, "model = \"claude-3-7-sonnet\"\n", new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: userConfigPath, profileOverride: null, workingDirectory: cwd);

            Assert.Equal(userConfigPath, config.UserConfigPath);
            Assert.Equal("gpt-5", config.Model);
            Assert.DoesNotContain(config.Layers, layer => layer.Path is not null && string.Equals(layer.Path, cwdConfigPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable(SystemRootOverrideEnvironmentVariable, originalSystemRoot);
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Load_ShouldTreatEmptyProjectRootMarkersAsStopAtCwd()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = Path.Combine(Path.GetTempPath(), "tianshu-config-loader-tests", Guid.NewGuid().ToString("N"));
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var parentRoot = Path.Combine(root, "parent");
        var projectRoot = Path.Combine(parentRoot, "workspace");
        var nestedWorkspace = Path.Combine(projectRoot, "src", "feature");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var projectConfigPath = Path.Combine(projectRoot, ".tianshu", "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(parentRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(projectRoot, ".tianshu"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(
                userConfigPath,
                """
                model = "gpt-5"
                project_root_markers = []
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(
                projectConfigPath,
                """
                model = "claude-3-7-sonnet"
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: nestedWorkspace);

            Assert.Equal(userConfigPath, config.ConfigFilePath);
            Assert.Equal("gpt-5", config.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Load_WhenUserConfigExistsButEmpty_TreatsItAsRequiredEmptyLayer()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            File.WriteAllText(userConfigPath, "   \n", new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: null);

            var userLayer = Assert.Single(config.Layers.Where(static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.User));
            Assert.Equal(userConfigPath, userLayer.Path);
            Assert.True(userLayer.FileExists);
            Assert.True(userLayer.IsEmpty);
            Assert.False(userLayer.IsDisabled);
            Assert.Equal(userConfigPath, config.ConfigFilePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenProjectRootMarkersHasInvalidShape_ThrowsFormatException()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var cwd = Path.Combine(root, "workspace");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(cwd);
            File.WriteAllText(
                userConfigPath,
                """
                model = "gpt-5"
                project_root_markers = "invalid"
                """,
                new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var exception = Assert.Throws<FormatException>(() => loader.Load(configFilePath: null, profileOverride: null, workingDirectory: cwd));
            Assert.Contains("project_root_markers must be an array of strings", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenActiveProfileMissing_ThrowsInvalidOperationException()
    {
        var path = WriteConfigToml(
            """
            profile = "missing"
            model = "gpt-5"
            """);

        var loader = new TianShuTomlConfigurationLoader();
        var exception = Assert.Throws<InvalidOperationException>(() => loader.Load(path, profileOverride: null));
        Assert.Equal("config profile `missing` not found", exception.Message);
    }

    [Fact]
    public void Load_WhenUserConfigIsMissing_ReturnsRequiredSystemAndUserLayers()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: null);

            var expectedUserPath = Path.Combine(tianShuHome, "tianshu.toml");
            Assert.Equal(expectedUserPath, config.UserConfigPath);
            Assert.Collection(
                config.Layers,
                layer =>
                {
                    Assert.Equal(ResolvedTianShuConfigLayerSourceKind.System, layer.SourceKind);
                    if (!layer.FileExists)
                    {
                        Assert.True(layer.IsEmpty);
                    }

                    Assert.False(layer.IsDisabled);
                },
                layer =>
                {
                    Assert.Equal(ResolvedTianShuConfigLayerSourceKind.User, layer.SourceKind);
                    Assert.Equal(expectedUserPath, layer.Path);
                    Assert.False(layer.FileExists);
                    Assert.True(layer.IsEmpty);
                    Assert.False(layer.IsDisabled);
                });
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenDotTianShuDirectoryExistsWithoutConfig_PreservesEmptyProjectLayer()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "src", "feature");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(userConfigPath, "model = \"gpt-5\"\n", new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: nestedWorkspace);

            var projectLayer = Assert.Single(config.Layers.Where(static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.Project));
            Assert.Equal(Path.Combine(repoRoot, ".tianshu", "tianshu.toml"), projectLayer.Path);
            Assert.Equal(repoRoot, projectLayer.DirectoryPath);
            Assert.False(projectLayer.FileExists);
            Assert.True(projectLayer.IsEmpty);
            Assert.False(projectLayer.IsDisabled);
            Assert.Equal("gpt-5", config.Model);
            Assert.Equal(userConfigPath, config.ConfigFilePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenTrustedProjectConfigMalformed_ThrowsFormatException()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "src", "feature");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var projectConfigPath = Path.Combine(repoRoot, ".tianshu", "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            var repoRootTomlPath = ToTomlPath(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(
                userConfigPath,
                $$"""
                model = "gpt-5"

                [projects."{{repoRootTomlPath}}"]
                trust_level = "trusted"
                """,
                new UTF8Encoding(false));
            File.WriteAllText(projectConfigPath, "model = [\n", new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            _ = Assert.Throws<FormatException>(() => loader.Load(configFilePath: null, profileOverride: null, workingDirectory: nestedWorkspace));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenUntrustedProjectConfigMalformed_PreservesDisabledEmptyLayer()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "src", "feature");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var projectConfigPath = Path.Combine(repoRoot, ".tianshu", "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(
                userConfigPath,
                $$"""
                model = "gpt-5"

                [projects."{{ToTomlPath(repoRoot)}}"]
                trust_level = "untrusted"
                """,
                new UTF8Encoding(false));
            File.WriteAllText(projectConfigPath, "model = [\n", new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: nestedWorkspace);

            var projectLayer = Assert.Single(config.Layers.Where(static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.Project));
            Assert.True(projectLayer.FileExists);
            Assert.True(projectLayer.IsEmpty);
            Assert.True(projectLayer.IsDisabled);
            Assert.Contains("is marked as untrusted", projectLayer.DisabledReason, StringComparison.Ordinal);
            Assert.Equal("gpt-5", config.Model);
            Assert.Equal(userConfigPath, config.ConfigFilePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenProjectIsUntrusted_DefaultsApprovalPolicyToUntrusted()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "src", "feature");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            var repoRootTomlPath = ToTomlPath(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(
                userConfigPath,
                $$"""
                model = "gpt-5"

                [projects."{{repoRootTomlPath}}"]
                trust_level = "untrusted"
                """,
                new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: nestedWorkspace);

            Assert.Equal("untrusted", config.ApprovalPolicy);
            Assert.Equal("gpt-5", config.Model);
            Assert.Equal(userConfigPath, config.ConfigFilePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenProjectIsUntrusted_DoesNotOverrideExplicitRootApprovalPolicy()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "src", "feature");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            var repoRootTomlPath = ToTomlPath(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(
                userConfigPath,
                $$"""
                model = "gpt-5"
                approval_policy = "never"

                [projects."{{repoRootTomlPath}}"]
                trust_level = "untrusted"
                """,
                new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: nestedWorkspace);

            Assert.Equal("never", config.ApprovalPolicy);
            Assert.Equal("gpt-5", config.Model);
            Assert.Equal(userConfigPath, config.ConfigFilePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenProjectIsUntrusted_DoesNotOverrideExplicitProfileApprovalPolicy()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "src", "feature");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            var repoRootTomlPath = ToTomlPath(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(
                userConfigPath,
                $$"""
                profile = "work"
                model = "gpt-5"

                [profiles.work]
                approval_policy = "on-request"

                [projects."{{repoRootTomlPath}}"]
                trust_level = "untrusted"
                """,
                new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: nestedWorkspace);

            Assert.Equal("on-request", config.ApprovalPolicy);
            Assert.Equal("gpt-5", config.Model);
            Assert.Equal("work", config.ActiveProfile);
            Assert.Equal(userConfigPath, config.ConfigFilePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenTrustedProjectLayerAddsNestedTrustEntry_DoesNotChangeImplicitApprovalPolicy()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "src", "feature");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var projectConfigPath = Path.Combine(repoRoot, ".tianshu", "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            var repoRootTomlPath = ToTomlPath(repoRoot);
            var nestedWorkspaceTomlPath = ToTomlPath(nestedWorkspace);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(
                userConfigPath,
                $$"""
                model = "gpt-5"

                [projects."{{repoRootTomlPath}}"]
                trust_level = "trusted"
                """,
                new UTF8Encoding(false));
            File.WriteAllText(
                projectConfigPath,
                $$"""
                model = "claude-3-7-sonnet"

                [projects."{{nestedWorkspaceTomlPath}}"]
                trust_level = "untrusted"
                """,
                new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: nestedWorkspace);

            Assert.Null(config.ApprovalPolicy);
            Assert.Equal("claude-3-7-sonnet", config.Model);
            Assert.Equal(projectConfigPath, config.ConfigFilePath);

            var projectLayer = Assert.Single(config.Layers.Where(static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.Project));
            Assert.False(projectLayer.IsDisabled);
            Assert.Equal(projectConfigPath, projectLayer.Path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenManagedConfigExists_ExposesDisabledMigrationOnlyLayer()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "src", "feature");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var projectConfigPath = Path.Combine(repoRoot, ".tianshu", "tianshu.toml");
        var managedConfigPath = Path.Combine(tianShuHome, "managed_config.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            var repoRootTomlPath = ToTomlPath(repoRoot);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(
                userConfigPath,
                $$"""
                model = "gpt-5"

                [projects."{{repoRootTomlPath}}"]
                trust_level = "trusted"
                """,
                new UTF8Encoding(false));
            File.WriteAllText(projectConfigPath, "model = \"claude-3-7-sonnet\"\n", new UTF8Encoding(false));
            File.WriteAllText(managedConfigPath, "model = \"o3\"\n", new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: nestedWorkspace);

            Assert.Equal("claude-3-7-sonnet", config.Model);
            Assert.Equal(userConfigPath, config.UserConfigPath);
            Assert.Equal(projectConfigPath, config.ConfigFilePath);
            var managedLayer = Assert.Single(config.Layers.Where(static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.LegacyManagedConfig));
            Assert.True(managedLayer.FileExists);
            Assert.False(managedLayer.IsEmpty);
            Assert.True(managedLayer.IsDisabled);
            Assert.Contains("迁移诊断层", managedLayer.DisabledReason, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenSystemRootOverrideConfigured_ExposesManagedConfigAsDisabledMigrationOnlyLayer()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalSystemRoot = Environment.GetEnvironmentVariable(SystemRootOverrideEnvironmentVariable);
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var systemRoot = Path.Combine(root, "system-tianshu");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var systemConfigPath = Path.Combine(systemRoot, "tianshu.toml");
        var managedConfigPath = Path.Combine(systemRoot, "managed_config.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable(SystemRootOverrideEnvironmentVariable, systemRoot);
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(systemRoot);
            File.WriteAllText(userConfigPath, "model = \"gpt-5\"\n", new UTF8Encoding(false));
            File.WriteAllText(systemConfigPath, "approval_policy = \"never\"\n", new UTF8Encoding(false));
            File.WriteAllText(managedConfigPath, "model = \"o3\"\n", new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: null);

            Assert.Equal("gpt-5", config.Model);
            Assert.Equal(userConfigPath, config.UserConfigPath);
            Assert.Equal(userConfigPath, config.ConfigFilePath);

            var systemLayer = Assert.Single(config.Layers.Where(static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.System));
            Assert.Equal(systemConfigPath, systemLayer.Path);
            Assert.True(systemLayer.FileExists);

            var managedLayer = Assert.Single(config.Layers.Where(static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.LegacyManagedConfig));
            Assert.Equal(managedConfigPath, managedLayer.Path);
            Assert.True(managedLayer.FileExists);
            Assert.True(managedLayer.IsDisabled);
            Assert.Contains("迁移诊断层", managedLayer.DisabledReason, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable(SystemRootOverrideEnvironmentVariable, originalSystemRoot);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenUserModelRouteSetModuleExists_LayersRouteSetBeforeUserConfig()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var modelRouteSetsDirectory = Path.Combine(tianShuHome, "modules", "model", "route-sets");
        var defaultRouteSetPath = Path.Combine(modelRouteSetsDirectory, "default.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(modelRouteSetsDirectory);
            File.WriteAllText(
                userConfigPath,
                """
                model_route_set = "default"
                approval_policy = "never"
                sandbox_mode = "danger-full-access"
                """,
                new UTF8Encoding(false));
            File.WriteAllText(
                defaultRouteSetPath,
                """
                [model_route_sets.default]
                display_name = "Default"
                routes = [
                  { kind = "coding", candidates = [{ provider = "openai-compatible", model = "from-route-set-model", protocol = "auto" }] }
                ]
                """,
                new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: null);

            Assert.Equal(userConfigPath, config.UserConfigPath);
            Assert.Equal(userConfigPath, config.ConfigFilePath);
            Assert.Equal("default", TestConfigReaders.ReadConfiguredString(config.RawConfig, "model_route_set"));
            Assert.Equal("never", config.ApprovalPolicy);
            Assert.Equal("danger-full-access", config.SandboxMode);

            var routeSetLayer = Assert.Single(config.Layers, static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.UserModule);
            Assert.Equal(defaultRouteSetPath, routeSetLayer.Path);
            Assert.True(routeSetLayer.FileExists);

            Assert.True(TestConfigReaders.TryReadConfiguredNestedValue(
                config.RawConfig,
                ["model_route_sets", "default", "routes"],
                out var rawRoutes));
            var route = Assert.Single(Assert.IsAssignableFrom<IEnumerable<object?>>(rawRoutes));
            Assert.True(TestConfigReaders.TryAsDictionary(route, out var routeConfig));
            Assert.True(TestConfigReaders.TryReadConfiguredValue(routeConfig, out var rawCandidates, "candidates"));
            var candidate = Assert.Single(Assert.IsAssignableFrom<IEnumerable<object?>>(rawCandidates));
            Assert.True(TestConfigReaders.TryAsDictionary(candidate, out var candidateConfig));
            Assert.Equal("from-route-set-model", TestConfigReaders.ReadConfiguredString(candidateConfig, "model"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenUserModelProtocolRuleSetModuleExists_LayersRulesBeforeUserConfig()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var ruleSetDirectory = Path.Combine(tianShuHome, "modules", "model", "protocol-rules");
        var defaultRuleSetPath = Path.Combine(ruleSetDirectory, "default.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(ruleSetDirectory);
            File.WriteAllText(
                userConfigPath,
                """
                model_protocol_rule_set = "default"
                approval_policy = "never"
                sandbox_mode = "danger-full-access"
                """,
                new UTF8Encoding(false));
            File.WriteAllText(
                defaultRuleSetPath,
                """
                [model_protocol_rule_sets.default]
                display_name = "Default Rules"
                rules = [
                  { match = "qwen*", protocols = ["anthropic_messages", "openai_chat_completions"] }
                ]
                """,
                new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: null);

            Assert.Equal("default", TestConfigReaders.ReadConfiguredString(config.RawConfig, "model_protocol_rule_set"));
            var ruleSetLayer = Assert.Single(config.Layers, static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.UserModule);
            Assert.Equal(defaultRuleSetPath, ruleSetLayer.Path);
            Assert.True(ruleSetLayer.FileExists);

            Assert.True(TestConfigReaders.TryReadConfiguredNestedValue(
                config.RawConfig,
                ["model_protocol_rule_sets", "default", "rules"],
                out var rawRules));
            var rule = Assert.Single(Assert.IsAssignableFrom<IEnumerable<object?>>(rawRules));
            Assert.True(TestConfigReaders.TryAsDictionary(rule, out var ruleConfig));
            Assert.Equal("qwen*", TestConfigReaders.ReadConfiguredString(ruleConfig, "match"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenUserAgentModuleExists_LayersKnownModuleBeforeUserConfig()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var agentsDirectory = Path.Combine(tianShuHome, "modules", "agent", "agents");
        var defaultAgentPath = Path.Combine(agentsDirectory, "default.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(agentsDirectory);
            File.WriteAllText(
                userConfigPath,
                """
                approval_policy = "never"
                sandbox_mode = "danger-full-access"
                """,
                new UTF8Encoding(false));
            File.WriteAllText(
                defaultAgentPath,
                """
                [agents.default]
                model = "module-agent-model"
                provider = "openai-compatible"
                """,
                new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: null);

            Assert.Equal("module-agent-model", config.Model);
            Assert.Equal("openai-compatible", config.ModelProvider);
            var agentLayer = Assert.Single(config.Layers, static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.UserModule);
            Assert.Equal(defaultAgentPath, agentLayer.Path);
            Assert.True(agentLayer.FileExists);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenUserProviderInstanceModuleSelected_LayersProviderInstancesAfterUserConfig()
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");
        var providerInstancesDirectory = Path.Combine(tianShuHome, "modules", "model", "provider-instances");
        var defaultProviderInstancesPath = Path.Combine(providerInstancesDirectory, "default.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(providerInstancesDirectory);
            File.WriteAllText(
                userConfigPath,
                """
                provider = "openai-compatible"
                provider_instances = "default"

                [providers.openai-compatible]
                base_url = "https://stale-root.example/v1"
                api_key_env = "STALE_ROOT_KEY"
                """,
                new UTF8Encoding(false));
            File.WriteAllText(
                defaultProviderInstancesPath,
                """
                [providers.openai-compatible]
                base_url = "https://module.example/v1"
                api_key_env = "MODULE_KEY"
                default_protocol = "openai_chat_completions"
                """,
                new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: null);

            Assert.Equal("default", TestConfigReaders.ReadConfiguredString(config.RawConfig, "provider_instances"));
            Assert.Equal("https://module.example/v1", config.ProviderBaseUrl);
            Assert.Equal("MODULE_KEY", config.ProviderEnvKey);
            Assert.True(TestConfigReaders.TryReadConfiguredNestedValue(
                config.RawConfig,
                ["providers", "openai-compatible"],
                out var rawProvider));
            Assert.True(TestConfigReaders.TryAsDictionary(rawProvider, out var providerConfig));
            Assert.Equal("openai_chat_completions", TestConfigReaders.ReadConfiguredString(providerConfig, "default_protocol"));

            var providerLayer = Assert.Single(config.Layers, static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.UserProviderInstance);
            Assert.Equal(defaultProviderInstancesPath, providerLayer.Path);
            Assert.True(providerLayer.FileExists);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenExplicitConfigPathProvided_UsesItAsUserLayer()
    {
        var root = CreateTempDirectory();
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var explicitUserConfigPath = Path.Combine(root, "explicit-user.toml");
        var repoRoot = Path.Combine(root, "repo");
        var nestedWorkspace = Path.Combine(repoRoot, "src", "feature");
        var defaultUserConfigPath = Path.Combine(tianShuHome, "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(repoRoot, ".tianshu"));
            Directory.CreateDirectory(tianShuHome);
            Directory.CreateDirectory(nestedWorkspace);
            File.WriteAllText(defaultUserConfigPath, "model = \"base-user\"\n", new UTF8Encoding(false));
            File.WriteAllText(
                explicitUserConfigPath,
                """
                model = "gpt-5"
                """,
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(repoRoot, ".tianshu", "tianshu.toml"), "model = \"claude-3-7-sonnet\"\n", new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(configFilePath: explicitUserConfigPath, profileOverride: null, workingDirectory: nestedWorkspace);

            Assert.Equal(explicitUserConfigPath, config.UserConfigPath);
            Assert.Equal("claude-3-7-sonnet", config.Model);
            Assert.Equal(Path.Combine(repoRoot, ".tianshu", "tianshu.toml"), config.ConfigFilePath);
            var userLayer = Assert.Single(config.Layers.Where(static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.User));
            Assert.Equal(explicitUserConfigPath, userLayer.Path);
            Assert.DoesNotContain(config.Layers, static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.SessionFlags);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Load_WhenConfigOverridesProvided_UsesSessionFlagsLayer()
    {
        var root = CreateTempDirectory();
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tianShuHome = Path.Combine(root, "tianshu-home");
        var userConfigPath = Path.Combine(tianShuHome, "tianshu.toml");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Directory.CreateDirectory(tianShuHome);
            File.WriteAllText(
                userConfigPath,
                """
                model = "gpt-5"

                [profiles.work]
                model = "gpt-5-work"
                """,
                new UTF8Encoding(false));

            var loader = new TianShuTomlConfigurationLoader();
            var config = loader.Load(
                configFilePath: null,
                profileOverride: null,
                configOverrides: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["model"] = "gpt-5-mini",
                    ["web_search"] = "live",
                });

            Assert.Equal(userConfigPath, config.UserConfigPath);
            Assert.Equal("gpt-5-mini", config.Model);
            Assert.Equal("live", config.WebSearchMode);
            Assert.Null(config.ActiveProfile);

            var sessionFlagsLayer = Assert.Single(config.Layers.Where(static layer => layer.SourceKind == ResolvedTianShuConfigLayerSourceKind.SessionFlags));
            Assert.Null(sessionFlagsLayer.Path);
            Assert.False(sessionFlagsLayer.FileExists);
            Assert.False(sessionFlagsLayer.IsDisabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void CreateSessionFlagsLayer_ShouldRebaseRelativeScalarPathOverridesAgainstWorkingDirectory()
    {
        var workingDirectory = CreateTempDirectory();

        try
        {
            var root = InvokeCreateSessionFlagsLayer(
                profileOverride: null,
                configOverrides: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["log_dir"] = "run-logs",
                },
                baseDirectory: workingDirectory);

            Assert.Equal(
                Path.GetFullPath(Path.Combine(workingDirectory, "run-logs")),
                Assert.IsType<string>(root["log_dir"]));
        }
        finally
        {
            DeleteDirectory(workingDirectory);
        }
    }

    [Fact]
    public void CreateSessionFlagsLayer_ShouldRebaseNestedPathOverridesAgainstWorkingDirectory()
    {
        var workingDirectory = CreateTempDirectory();

        try
        {
            var root = InvokeCreateSessionFlagsLayer(
                profileOverride: null,
                configOverrides: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["mcp_servers.docs.cwd"] = "servers/docs",
                    ["sandbox_workspace_write.writable_roots"] = """json:["workspace-a","workspace-b"]""",
                    ["skills.config"] = """json:[{"path":"skills/repo-skill/SKILL.md","enabled":false}]""",
                },
                baseDirectory: workingDirectory);

            var mcpServers = Assert.IsType<TomlTable>(root["mcp_servers"]);
            var docs = Assert.IsType<TomlTable>(mcpServers["docs"]);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(workingDirectory, "servers", "docs")),
                Assert.IsType<string>(docs["cwd"]));

            var sandboxWorkspaceWrite = Assert.IsType<TomlTable>(root["sandbox_workspace_write"]);
            var writableRoots = Assert.IsType<TomlArray>(sandboxWorkspaceWrite["writable_roots"]);
            Assert.Equal(
                new[]
                {
                    Path.GetFullPath(Path.Combine(workingDirectory, "workspace-a")),
                    Path.GetFullPath(Path.Combine(workingDirectory, "workspace-b")),
                },
                writableRoots.Cast<object?>().Select(static item => Assert.IsType<string>(item)).ToArray());

            var skills = Assert.IsType<TomlTable>(root["skills"]);
            var skillConfigs = Assert.IsType<TomlArray>(skills["config"]);
            var firstSkill = Assert.IsType<TomlTable>(Assert.Single(skillConfigs.Cast<object?>()));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(workingDirectory, "skills", "repo-skill", "SKILL.md")),
                Assert.IsType<string>(firstSkill["path"]));
        }
        finally
        {
            DeleteDirectory(workingDirectory);
        }
    }

    [Fact]
    public void ApplyToOptions_WhenTargetIsEmpty_AppliesResolvedValues()
    {
        var options = new ControlPlaneInitializeRuntimeCommand
        {
            ProtocolAdapter = ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId(),
        };

        var config = new ResolvedTianShuConfig
        {
            ConfigFilePath = "repo/.tianshu/managed_config.toml",
            UserConfigPath = "repo/.tianshu/tianshu.toml",
            ActiveProfile = "work",
            Model = "claude-3-7-sonnet",
            ModelProvider = "anthropic",
            ApprovalPolicy = "on-request",
            SandboxMode = "workspace-write",
            WebSearchMode = "on",
            ServiceTier = "flex",
            ModelReasoningSummary = "auto",
            ModelVerbosity = "high",
            ProviderBaseUrl = "https://api.anthropic.com/v1",
            ProviderEnvKey = "ANTHROPIC_API_KEY",
            ProviderWireApi = "responses",
            ProviderRequestMaxRetries = 3,
            ProviderStreamMaxRetries = 4,
            ProviderStreamIdleTimeoutMs = 25000,
            ProviderSupportsWebsockets = true,
            ProtocolAdapter = ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId(),
        };

        TianShuTomlConfigurationLoader.ApplyToOptions(options, config);

        Assert.Equal("repo/.tianshu/tianshu.toml", options.ConfigFilePath);
        Assert.Equal("work", options.ProfileName);
        Assert.True(options.ProfileNameResolvedFromConfig);
        Assert.Equal("claude-3-7-sonnet", options.Model);
        Assert.Equal("anthropic", options.ModelProvider);
        Assert.Equal("on-request", options.ApprovalPolicy);
        Assert.Equal("workspace-write", options.SandboxMode);
        Assert.Equal("on", options.WebSearchMode);
        Assert.Equal("flex", options.ServiceTier);
        Assert.Equal("auto", options.ModelReasoningSummary);
        Assert.Equal("high", options.ModelVerbosity);
        Assert.Equal("https://api.anthropic.com/v1", options.ProviderBaseUrl);
        Assert.Equal("ANTHROPIC_API_KEY", options.ProviderApiKeyEnvironmentVariable);
        Assert.Equal("responses", options.ProviderWireApi);
        Assert.Equal(3, options.ProviderRequestMaxRetries);
        Assert.Equal(4, options.ProviderStreamMaxRetries);
        Assert.Equal(25000, options.ProviderStreamIdleTimeoutMs);
        Assert.True(options.ProviderSupportsWebsockets);
        Assert.Equal(ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId(), options.ProtocolAdapter);
    }

    [Fact]
    public void ApplyToOptions_WhenExplicitValuesExist_PreservesThem()
    {
        var options = new ControlPlaneInitializeRuntimeCommand
        {
            ConfigFilePath = "custom.toml",
            ProfileName = "manual",
            Model = "gpt-5-codex-custom",
            ModelProvider = "custom-provider",
            ApprovalPolicy = "never",
            SandboxMode = "danger-full-access",
            WebSearchMode = "off",
            ServiceTier = "fast",
            ModelReasoningSummary = "none",
            ModelVerbosity = "medium",
            ProviderBaseUrl = "https://custom.example.com/v1",
            ProviderApiKeyEnvironmentVariable = "CUSTOM_API_KEY",
            ProviderWireApi = "responses",
            ProviderRequestMaxRetries = 7,
            ProviderStreamMaxRetries = 8,
            ProviderStreamIdleTimeoutMs = 9000,
            ProviderSupportsWebsockets = false,
            ProtocolAdapter = ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId(),
        };

        var config = new ResolvedTianShuConfig
        {
            ConfigFilePath = "repo/.tianshu/tianshu.toml",
            ActiveProfile = "work",
            Model = "claude-3-7-sonnet",
            ModelProvider = "anthropic",
            ApprovalPolicy = "on-request",
            SandboxMode = "workspace-write",
            WebSearchMode = "on",
            ServiceTier = "flex",
            ModelReasoningSummary = "auto",
            ModelVerbosity = "high",
            ProviderBaseUrl = "https://api.anthropic.com/v1",
            ProviderEnvKey = "ANTHROPIC_API_KEY",
            ProviderWireApi = "responses",
            ProviderRequestMaxRetries = 3,
            ProviderStreamMaxRetries = 4,
            ProviderStreamIdleTimeoutMs = 25000,
            ProviderSupportsWebsockets = true,
            ProtocolAdapter = ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId(),
        };

        TianShuTomlConfigurationLoader.ApplyToOptions(options, config);

        Assert.Equal("custom.toml", options.ConfigFilePath);
        Assert.Equal("manual", options.ProfileName);
        Assert.False(options.ProfileNameResolvedFromConfig);
        Assert.Equal("gpt-5-codex-custom", options.Model);
        Assert.Equal("custom-provider", options.ModelProvider);
        Assert.Equal("never", options.ApprovalPolicy);
        Assert.Equal("danger-full-access", options.SandboxMode);
        Assert.Equal("off", options.WebSearchMode);
        Assert.Equal("fast", options.ServiceTier);
        Assert.Equal("none", options.ModelReasoningSummary);
        Assert.Equal("medium", options.ModelVerbosity);
        Assert.Equal("https://custom.example.com/v1", options.ProviderBaseUrl);
        Assert.Equal("CUSTOM_API_KEY", options.ProviderApiKeyEnvironmentVariable);
        Assert.Equal("responses", options.ProviderWireApi);
        Assert.Equal(7, options.ProviderRequestMaxRetries);
        Assert.Equal(8, options.ProviderStreamMaxRetries);
        Assert.Equal(9000, options.ProviderStreamIdleTimeoutMs);
        Assert.False(options.ProviderSupportsWebsockets);
        Assert.Equal(ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId(), options.ProtocolAdapter);
    }

    [Fact]
    public void ApplyToOptions_WhenExplicitProtocolAdapterIsUnsupported_Throws()
    {
        var options = new ControlPlaneInitializeRuntimeCommand
        {
            ProtocolAdapter = "unsupported-adapter",
        };

        var config = new ResolvedTianShuConfig
        {
            ConfigFilePath = "repo/.tianshu/tianshu.toml",
            UserConfigPath = "repo/.tianshu/tianshu.toml",
            ProtocolAdapter = ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId(),
        };

        var exception = Assert.Throws<InvalidOperationException>(() => TianShuTomlConfigurationLoader.ApplyToOptions(options, config));
        Assert.Equal(
            ProviderRuntimeBootstrapRegistry.BuildUnsupportedProtocolAdapterMessage(options.ProtocolAdapter),
            exception.Message);
    }

    [Fact]
    public void ProviderRuntimeBootstrapRegistry_CreateRuntimeState_ReturnsProtocolAdapter()
    {
        var canonicalAdapterId = ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId();

        var expectedExplicitAdapter = ProviderRuntimeBootstrapRegistry.CreateRuntimeState(canonicalAdapterId).ProtocolAdapter;
        var explicitAdapter = ProviderRuntimeBootstrapRegistry.CreateRuntimeState(canonicalAdapterId).ProtocolAdapter;
        Assert.Equal(expectedExplicitAdapter.GetType(), explicitAdapter.GetType());

        var expectedDefaultAdapter = ProviderRuntimeBootstrapRegistry.CreateRuntimeState(null).ProtocolAdapter;
        var defaultAdapter = ProviderRuntimeBootstrapRegistry.CreateRuntimeState(null).ProtocolAdapter;
        Assert.Equal(expectedDefaultAdapter.GetType(), defaultAdapter.GetType());
    }

    [Theory]
    [InlineData("unsupported-adapter")]
    [InlineData("UNSUPPORTED-ADAPTER")]
    [InlineData("unknown-adapter")]
    public void ProviderRuntimeBootstrapRegistry_CreateRuntimeState_WhenAdapterUnsupported_Throws(string adapterId)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ProviderRuntimeBootstrapRegistry.CreateRuntimeState(adapterId));
        Assert.Equal(
            ProviderRuntimeBootstrapRegistry.BuildUnsupportedProtocolAdapterMessage(adapterId),
            exception.Message);
    }

    [Fact]
    public void ProviderSmokeTestPlan_WhenFakeConfigAndCredentialExist_IsReadyWithoutEchoingSecret()
    {
        var path = WriteConfigToml(
            """
            model = "gpt-5.4"
            provider = "openai"

            [providers.openai]
            base_url = "https://api.openai.com/v1"
            api_key_env = "FAKE_OPENAI_API_KEY"
            default_protocol = "responses"
            supports_websockets = true
            """);

        var loader = new TianShuTomlConfigurationLoader();
        var config = loader.Load(path, profileOverride: null);
        var plan = ProviderSmokeTestPlan.Create(
            config,
            static name => string.Equals(name, "FAKE_OPENAI_API_KEY", StringComparison.Ordinal)
                ? "sk-test-secret"
                : null);

        Assert.True(plan.Ready, plan.FailureReason);
        Assert.True(plan.BootstrapResolved);
        Assert.True(plan.CredentialValueAvailable);
        Assert.Equal("openai", plan.ModelProvider);
        Assert.Equal("gpt-5.4", plan.Model);
        Assert.Equal("https://api.openai.com/v1", plan.ProviderBaseUrl);
        Assert.Equal("responses", plan.ProviderWireApi);
        Assert.Equal("FAKE_OPENAI_API_KEY", plan.ProviderApiKeyEnvironmentVariable);
        Assert.Equal(ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId(), plan.ProtocolAdapter);
        Assert.DoesNotContain(plan.ToRedactedDiagnostics(), item => item.Contains("sk-test-secret", StringComparison.Ordinal));
    }

    [Fact]
    public void ProviderSmokeTestPlan_WhenCredentialMissing_IsNotReadyAndOnlyReportsEnvironmentVariableName()
    {
        var config = new ResolvedTianShuConfig
        {
            ConfigFilePath = "repo/.tianshu/tianshu.toml",
            Model = "gpt-5.4",
            ModelProvider = "openai",
            ProviderBaseUrl = "https://api.openai.com/v1",
            ProviderEnvKey = "FAKE_OPENAI_API_KEY",
            ProviderWireApi = "responses",
            ProtocolAdapter = ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId(),
        };

        var plan = ProviderSmokeTestPlan.Create(config, static _ => null);

        Assert.False(plan.Ready);
        Assert.True(plan.BootstrapResolved);
        Assert.False(plan.CredentialValueAvailable);
        Assert.Contains("FAKE_OPENAI_API_KEY", plan.FailureReason, StringComparison.Ordinal);
        Assert.DoesNotContain(plan.ToRedactedDiagnostics(), item => item.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProviderSmokeTestPlan_UserTianShuConfigReadinessSmoke_IsOptIn()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("TIANSHU_RUN_OPENAI_PROVIDER_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var loader = new TianShuTomlConfigurationLoader();
        var config = loader.Load(configFilePath: null, profileOverride: null, workingDirectory: FindRepoRoot());
        var plan = ProviderSmokeTestPlan.Create(config);

        Assert.True(plan.Ready, plan.FailureReason);
        var secret = Environment.GetEnvironmentVariable(plan.ProviderApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            Assert.DoesNotContain(plan.ToRedactedDiagnostics(), item => item.Contains(secret, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenChatCompletionsModelsProvided_PostsEachModelWithoutEchoingSecret()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"chatcmpl-test","choices":[{"message":{"content":"OK"}}]}"""),
        });
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "openai-compatible",
            ProviderBaseUrl = "https://proxy.example",
            ProviderEnvKey = "TIANSHU_TEST_KEY",
            ProviderWireApi = "openai_chat_completions",
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["openai-compatible-default", "qwen3-coder-plus"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static name => string.Equals(name, "TIANSHU_TEST_KEY", StringComparison.Ordinal)
                    ? "sk-secret-value"
                    : null,
            });

        Assert.True(result.Succeeded);
        Assert.Equal("openai-compatible", result.ProviderId);
        Assert.Equal("openai_chat_completions", result.Protocol);
        Assert.All(result.Items, static item =>
        {
            Assert.True(item.Succeeded, item.Reason);
            Assert.Equal("/v1/chat/completions", item.RequestPath);
            Assert.Equal(200, item.HttpStatusCode);
            Assert.DoesNotContain("sk-secret-value", item.Reason ?? string.Empty, StringComparison.Ordinal);
        });

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, static request => Assert.Equal("/v1/chat/completions", request.RequestUri!.AbsolutePath));
        var requestedModels = handler.Bodies
            .Select(static body =>
            {
                using var document = System.Text.Json.JsonDocument.Parse(body);
                return document.RootElement.GetProperty("model").GetString() ?? string.Empty;
            })
            .ToArray();
        Assert.Equal(["openai-compatible-default", "qwen3-coder-plus"], requestedModels);
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenChatCompletionsReturnsReasoningContent_ShouldTreatAsReachable()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"chatcmpl-test","choices":[{"message":{"role":"assistant","content":"","reasoning_content":"OK reasoning"}}]}"""),
        });
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "openai-compatible",
            ProviderBaseUrl = "https://proxy.example",
            ProviderEnvKey = "TIANSHU_TEST_KEY",
            ProviderWireApi = "openai_chat_completions",
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["deepseek-v4-flash"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static name => string.Equals(name, "TIANSHU_TEST_KEY", StringComparison.Ordinal)
                    ? "sk-secret-value"
                    : null,
            });

        Assert.True(result.Succeeded);
        var item = Assert.Single(result.Items);
        Assert.True(item.Succeeded, item.Reason);
        Assert.Equal(200, item.HttpStatusCode);
        Assert.False(item.HasText);
        Assert.True(item.HasReasoning);
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenStreamingChatCompletionsEmitsReasoning_ShouldStopAtFirstSignal()
    {
        var content = new StringContent(
            """
            data: {"choices":[{"delta":{"reasoning_content":"thinking"}}]}

            data: {"choices":[{"delta":{"content":"OK"}}]}

            data: [DONE]

            """);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
        });
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "openai-compatible",
            ProviderBaseUrl = "https://proxy.example",
            ProviderEnvKey = "TIANSHU_TEST_KEY",
            ProviderWireApi = "openai_chat_completions",
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["claude-opus-4.7"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static name => string.Equals(name, "TIANSHU_TEST_KEY", StringComparison.Ordinal)
                    ? "sk-secret-value"
                    : null,
            });

        var item = Assert.Single(result.Items);
        Assert.True(item.Succeeded, item.Reason);
        Assert.False(item.HasText);
        Assert.True(item.HasReasoning);
        using var document = System.Text.Json.JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.True(document.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenQwenReasoningConfigured_AddsOfficialThinkingOptions()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"chatcmpl-test","choices":[{"message":{"content":"OK"}}]}"""),
        });
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "openai-compatible",
            ProviderBaseUrl = "https://proxy.example",
            ProviderEnvKey = "TIANSHU_TEST_KEY",
            ProviderWireApi = "openai_chat_completions",
            ModelReasoningEnabled = true,
            ModelReasoningEffort = "high",
            ModelReasoningBudgetTokens = 8192,
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["qwen3-coder-plus"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static name => string.Equals(name, "TIANSHU_TEST_KEY", StringComparison.Ordinal)
                    ? "sk-secret-value"
                    : null,
            });

        Assert.True(result.Succeeded);
        using var document = System.Text.Json.JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.True(document.RootElement.GetProperty("enable_thinking").GetBoolean());
        Assert.Equal(8192, document.RootElement.GetProperty("thinking_budget").GetInt32());
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenCredentialMissing_DoesNotSendRequests()
    {
        var handler = new CapturingHttpMessageHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK));
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "openai-compatible",
            ProviderBaseUrl = "https://proxy.example/v1",
            ProviderEnvKey = "TIANSHU_TEST_KEY",
            ProviderWireApi = "openai_chat_completions",
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["gpt-5.4"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static _ => null,
            });

        Assert.False(result.Succeeded);
        var item = Assert.Single(result.Items);
        Assert.Equal("gpt-5.4", item.Model);
        Assert.False(item.Succeeded);
        Assert.Contains("TIANSHU_TEST_KEY", item.Reason, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenResponsesModelsProvided_RequiresParseableText()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"resp_test","output":[{"type":"message","content":[{"type":"output_text","text":"OK"}]}]}"""),
        });
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "openai",
            ProviderBaseUrl = "https://api.openai.com",
            ProviderEnvKey = "OPENAI_API_KEY",
            ProviderWireApi = "openai_responses",
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["gpt-5.4"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static name => string.Equals(name, "OPENAI_API_KEY", StringComparison.Ordinal)
                    ? "sk-openai-secret"
                    : null,
            });

        Assert.True(result.Succeeded);
        Assert.Equal("responses", result.Protocol);
        var item = Assert.Single(result.Items);
        Assert.True(item.Succeeded, item.Reason);
        Assert.Equal("/v1/responses", item.RequestPath);
        Assert.Equal(200, item.HttpStatusCode);
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenResponsesBodyIsLong_ShouldParseBeforeDisplayTruncation()
    {
        var longInstructions = new string('a', 1000);
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"id":"resp_test","instructions":"{{longInstructions}}","output":[{"type":"message","content":[{"type":"output_text","text":"OK after long metadata"}]}]}"""),
        });
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "openai",
            ProviderBaseUrl = "https://api.openai.com",
            ProviderEnvKey = "OPENAI_API_KEY",
            ProviderWireApi = "openai_responses",
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["gpt-5.4"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static name => string.Equals(name, "OPENAI_API_KEY", StringComparison.Ordinal)
                    ? "sk-openai-secret"
                    : null,
            });

        Assert.True(result.Succeeded);
        var item = Assert.Single(result.Items);
        Assert.True(item.Succeeded, item.Reason);
        Assert.Equal(200, item.HttpStatusCode);
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenHttpSuccessHasNoAdapterText_ReturnsFailure()
    {
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":""}}]}"""),
        });
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "openai-compatible",
            ProviderBaseUrl = "https://proxy.example/v1",
            ProviderEnvKey = "TIANSHU_TEST_KEY",
            ProviderWireApi = "openai_chat_completions",
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["empty-model"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static name => string.Equals(name, "TIANSHU_TEST_KEY", StringComparison.Ordinal)
                    ? "sk-secret-value"
                    : null,
            });

        Assert.False(result.Succeeded);
        var item = Assert.Single(result.Items);
        Assert.False(item.Succeeded);
        Assert.Equal(200, item.HttpStatusCode);
        Assert.Contains("没有可由当前 provider adapter 解析的文本内容", item.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-secret-value", item.Reason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenAnthropicMessagesModelsProvided_UsesOfficialMessagesShape()
    {
        var handler = new CapturingHttpMessageHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"msg_test","type":"message","content":[{"type":"text","text":"OK"}]}"""),
        });
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "anthropic",
            ProviderBaseUrl = "https://api.anthropic.com",
            ProviderEnvKey = "ANTHROPIC_API_KEY",
            ProviderWireApi = "anthropic_messages",
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["claude-sonnet-4-5"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static name => string.Equals(name, "ANTHROPIC_API_KEY", StringComparison.Ordinal)
                    ? "sk-ant-secret"
                    : null,
            });

        Assert.True(result.Succeeded);
        Assert.Equal("anthropic", result.ProviderId);
        Assert.Equal("anthropic_messages", result.Protocol);
        var item = Assert.Single(result.Items);
        Assert.True(item.Succeeded, item.Reason);
        Assert.Equal("/v1/messages", item.RequestPath);
        Assert.Equal(200, item.HttpStatusCode);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/messages", request.RequestUri!.AbsolutePath);
        Assert.True(request.Headers.TryGetValues("x-api-key", out var apiKeys));
        Assert.Equal("sk-ant-secret", Assert.Single(apiKeys));
        Assert.True(request.Headers.TryGetValues("anthropic-version", out var versions));
        Assert.Equal("2023-06-01", Assert.Single(versions));
        Assert.Null(request.Headers.Authorization);

        using var document = System.Text.Json.JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.Equal("claude-sonnet-4-5", document.RootElement.GetProperty("model").GetString());
        Assert.Equal(4096, document.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.True(document.RootElement.GetProperty("stream").GetBoolean());
        var message = document.RootElement.GetProperty("messages")[0];
        Assert.Equal("user", message.GetProperty("role").GetString());
        var content = Assert.Single(message.GetProperty("content").EnumerateArray());
        Assert.Equal("text", content.GetProperty("type").GetString());
        Assert.Equal("hello", content.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenAnthropicReasoningConfigured_AddsThinkingBudget()
    {
        var handler = new CapturingHttpMessageHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"msg_test","type":"message","content":[{"type":"thinking","thinking":"step"},{"type":"text","text":"OK"}]}"""),
        });
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "anthropic",
            ProviderBaseUrl = "https://api.anthropic.com",
            ProviderEnvKey = "ANTHROPIC_API_KEY",
            ProviderWireApi = "anthropic_messages",
            ModelReasoningEnabled = true,
            ModelReasoningEffort = "medium",
            ModelReasoningBudgetTokens = 4096,
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["claude-sonnet-4-5"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static name => string.Equals(name, "ANTHROPIC_API_KEY", StringComparison.Ordinal)
                    ? "sk-ant-secret"
                    : null,
            });

        Assert.True(result.Succeeded);
        var item = Assert.Single(result.Items);
        Assert.True(item.HasReasoning);
        using var document = System.Text.Json.JsonDocument.Parse(Assert.Single(handler.Bodies));
        var thinking = document.RootElement.GetProperty("thinking");
        Assert.Equal("enabled", thinking.GetProperty("type").GetString());
        Assert.Equal(4096, thinking.GetProperty("budget_tokens").GetInt32());
        Assert.Equal("summarized", thinking.GetProperty("display").GetString());
        Assert.Equal(5120, document.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenStreamingAnthropicMessagesEmitsText_ShouldTreatAsReachable()
    {
        var content = new StringContent(
            """
            event: message_start
            data: {"type":"message_start","message":{"id":"msg_test","type":"message"}}

            event: content_block_delta
            data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"O"}}

            """);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        var handler = new CapturingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
        });
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "anthropic",
            ProviderBaseUrl = "https://api.anthropic.com",
            ProviderEnvKey = "ANTHROPIC_API_KEY",
            ProviderWireApi = "anthropic_messages",
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["claude-sonnet-4-5"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static name => string.Equals(name, "ANTHROPIC_API_KEY", StringComparison.Ordinal)
                    ? "sk-ant-secret"
                    : null,
            });

        var item = Assert.Single(result.Items);
        Assert.True(item.Succeeded, item.Reason);
        Assert.True(item.HasText);
        Assert.False(item.HasReasoning);
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenGoogleGenerativeModelsProvided_UsesOfficialGenerateContentShape()
    {
        var handler = new CapturingHttpMessageHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"candidates":[{"content":{"parts":[{"text":"OK"}]}}]}"""),
        });
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "google",
            ProviderBaseUrl = "https://generativelanguage.googleapis.com",
            ProviderEnvKey = "GEMINI_API_KEY",
            ProviderWireApi = "google_generative",
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["gemini-2.5-pro"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static name => string.Equals(name, "GEMINI_API_KEY", StringComparison.Ordinal)
                    ? "gemini-secret"
                    : null,
            });

        Assert.True(result.Succeeded);
        Assert.Equal("google", result.ProviderId);
        Assert.Equal("google_generative", result.Protocol);
        var item = Assert.Single(result.Items);
        Assert.True(item.Succeeded, item.Reason);
        Assert.Equal("/v1beta/models/gemini-2.5-pro:streamGenerateContent", item.RequestPath);
        Assert.Equal(200, item.HttpStatusCode);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1beta/models/gemini-2.5-pro:streamGenerateContent", request.RequestUri!.AbsolutePath);
        Assert.Contains("key=gemini-secret", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("alt=sse", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Null(request.Headers.Authorization);

        using var document = System.Text.Json.JsonDocument.Parse(Assert.Single(handler.Bodies));
        var part = document.RootElement.GetProperty("contents")[0].GetProperty("parts")[0];
        Assert.Equal("hello", part.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenGoogleReasoningConfigured_AddsThinkingConfig()
    {
        var handler = new CapturingHttpMessageHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"candidates":[{"content":{"parts":[{"thought":true,"text":"thinking"},{"text":"OK"}]}}]}"""),
        });
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "google",
            ProviderBaseUrl = "https://generativelanguage.googleapis.com",
            ProviderEnvKey = "GEMINI_API_KEY",
            ProviderWireApi = "google_generative",
            ModelReasoningEnabled = true,
            ModelReasoningSummary = "auto",
            ModelReasoningBudgetTokens = 2048,
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["gemini-2.5-pro"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static name => string.Equals(name, "GEMINI_API_KEY", StringComparison.Ordinal)
                    ? "gemini-secret"
                    : null,
            });

        Assert.True(result.Succeeded);
        var item = Assert.Single(result.Items);
        Assert.True(item.HasReasoning);
        using var document = System.Text.Json.JsonDocument.Parse(Assert.Single(handler.Bodies));
        var thinkingConfig = document.RootElement
            .GetProperty("generationConfig")
            .GetProperty("thinkingConfig");
        Assert.Equal(2048, thinkingConfig.GetProperty("thinkingBudget").GetInt32());
        Assert.True(thinkingConfig.GetProperty("includeThoughts").GetBoolean());
    }

    [Fact]
    public async Task ProviderModelConnectivityProbe_WhenGemini3ReasoningConfigured_UsesThinkingLevel()
    {
        var handler = new CapturingHttpMessageHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"candidates":[{"content":{"parts":[{"text":"OK"}]}}]}"""),
        });
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "google",
            ProviderBaseUrl = "https://generativelanguage.googleapis.com",
            ProviderEnvKey = "GEMINI_API_KEY",
            ProviderWireApi = "google_generative",
            ModelReasoningEnabled = true,
            ModelReasoningEffort = "medium",
            ModelReasoningSummary = "auto",
            ModelReasoningBudgetTokens = 4096,
        };

        var result = await new ProviderModelConnectivityProbe().ProbeAsync(
            config,
            ["gemini-3.1-pro-preview"],
            new ProviderModelConnectivityProbeOptions
            {
                HttpMessageHandler = handler,
                ReadEnvironmentVariable = static name => string.Equals(name, "GEMINI_API_KEY", StringComparison.Ordinal)
                    ? "gemini-secret"
                    : null,
            });

        Assert.True(result.Succeeded);
        using var document = System.Text.Json.JsonDocument.Parse(Assert.Single(handler.Bodies));
        var thinkingConfig = document.RootElement
            .GetProperty("generationConfig")
            .GetProperty("thinkingConfig");
        Assert.Equal("high", thinkingConfig.GetProperty("thinkingLevel").GetString());
        Assert.False(thinkingConfig.TryGetProperty("thinkingBudget", out _));
        Assert.True(thinkingConfig.GetProperty("includeThoughts").GetBoolean());
    }

    private static string WriteConfigToml(string content)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-configs");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.toml");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-config-loader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string ToTomlPath(string path)
        => path.Replace("\\", "/", StringComparison.Ordinal);

    private static TomlTable InvokeCreateSessionFlagsLayer(
        string? profileOverride,
        IReadOnlyDictionary<string, string>? configOverrides,
        string? baseDirectory)
    {
        var method = typeof(TianShuTomlConfigurationLoader).GetMethod(
            "CreateSessionFlagsLayer",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var layer = method!.Invoke(null, [profileOverride, configOverrides, baseDirectory]);
        Assert.NotNull(layer);

        var rootProperty = layer!.GetType().GetProperty("Root", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(rootProperty);

        return Assert.IsType<TomlTable>(rootProperty!.GetValue(layer));
    }

    private static string FindRepoRoot()
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

        throw new DirectoryNotFoundException("未找到 TianShu.sln。");
    }

    private static class TestConfigReaders
    {
        public static string? ReadConfiguredString(Dictionary<string, object?> config, string propertyName)
            => config.TryGetValue(propertyName, out var rawValue)
               && TryReadString(rawValue, out var value)
                ? value
                : null;

        public static bool TryReadConfiguredValue(
            Dictionary<string, object?> config,
            out object? value,
            string propertyName)
            => config.TryGetValue(propertyName, out value);

        public static bool TryReadConfiguredNestedValue(
            Dictionary<string, object?> config,
            IReadOnlyList<string> propertyPath,
            out object? value)
        {
            var current = config;
            for (var index = 0; index < propertyPath.Count; index++)
            {
                if (!current.TryGetValue(propertyPath[index], out value))
                {
                    return false;
                }

                if (index == propertyPath.Count - 1)
                {
                    return true;
                }

                if (!TryAsDictionary(value, out current))
                {
                    value = null;
                    return false;
                }
            }

            value = null;
            return false;
        }

        public static bool TryAsDictionary(object? value, out Dictionary<string, object?> dictionary)
        {
            switch (value)
            {
                case Dictionary<string, object?> concrete:
                    dictionary = concrete;
                    return true;
                case IReadOnlyDictionary<string, object?> readOnly:
                    dictionary = readOnly.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                    return true;
                case IDictionary<string, object?> mutable:
                    dictionary = mutable.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
                    return true;
                case JsonElement element when element.ValueKind == JsonValueKind.Object:
                    dictionary = element.EnumerateObject().ToDictionary(
                        static property => property.Name,
                        static property => JsonSerializer.Deserialize<object?>(property.Value.GetRawText()),
                        StringComparer.Ordinal);
                    return true;
                default:
                    dictionary = null!;
                    return false;
            }
        }

        private static bool TryReadString(object? value, out string text)
        {
            switch (value)
            {
                case string stringValue:
                    text = stringValue;
                    return true;
                case JsonElement element when element.ValueKind == JsonValueKind.String:
                    text = element.GetString() ?? string.Empty;
                    return true;
                default:
                    text = string.Empty;
                    return false;
            }
        }
    }

    private sealed class CapturingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return responseFactory(request);
        }
    }
}
