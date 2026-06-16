using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using TianShu.Configuration;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.ConfigGui;

internal static class ConfigGuiSmokeCommand
{
    private const string SmokeProviderId = "config_gui_smoke";
    private const string SmokeMarker = "ConfigGUI smoke marker";

    public static bool TryRun(string[] args, out int exitCode)
    {
        if (!args.Any(static arg => string.Equals(arg, "--smoke", StringComparison.OrdinalIgnoreCase)))
        {
            exitCode = 0;
            return false;
        }

        exitCode = Run(args);
        return true;
    }

    private static int Run(string[] args)
    {
        var reportPath = ReadOption(args, "--smoke-report");
        var report = new ConfigGuiSmokeReport();
        try
        {
            if (!args.Any(static arg => string.Equals(arg, "--smoke-allow-write", StringComparison.OrdinalIgnoreCase)))
            {
                report.Checks.Add(new ConfigGuiSmokeCheck("write-consent", false, "ConfigGUI smoke 会写入临时配置，必须显式传入 --smoke-allow-write。"));
                WriteReport(reportPath, report);
                return 2;
            }

            var home = Environment.GetEnvironmentVariable("TIANSHU_HOME");
            if (string.IsNullOrWhiteSpace(home))
            {
                report.Checks.Add(new ConfigGuiSmokeCheck("home", false, "ConfigGUI smoke 需要显式设置临时 TIANSHU_HOME，避免误写真实用户配置。"));
                WriteReport(reportPath, report);
                return 2;
            }

            home = Path.GetFullPath(home);
            Directory.CreateDirectory(home);
            SeedSmokeHomeIfNeeded(home);

            var state = new ConfigGuiState();
            state.Refresh();
            report.ConfigPath = state.ConfigPath;
            report.Modules.AddRange(state.NavigationModules.Select(static module => module.DisplayName));
            report.ExpandedModules.AddRange(state.NavigationModules.Where(static module => module.IsExpanded.Value).Select(static module => module.DisplayName));
            report.Categories.AddRange(state.Categories.Select(static category => category.DisplayName));
            report.FoundationPages.AddRange(state.NavigationModules
                .FirstOrDefault(static module => module.DisplayName == "基础配置")
                ?.Pages.Select(static page => page.DisplayName) ?? []);
            report.ProfileCompositionPages.AddRange(state.NavigationModules
                .FirstOrDefault(static module => module.DisplayName == "配置方案编排")
                ?.Pages.Select(static page => page.DisplayName) ?? []);
            report.AgentPages.AddRange(state.NavigationModules
                .FirstOrDefault(static module => module.DisplayName == "代理")
                ?.Pages.Select(static page => page.DisplayName) ?? []);
            report.ModelConfigurationPages.AddRange(state.NavigationModules
                .FirstOrDefault(static module => module.DisplayName == "模型")
                ?.Pages.Select(static page => page.DisplayName) ?? []);
            report.MemoryPages.AddRange(state.NavigationModules
                .FirstOrDefault(static module => module.DisplayName == "记忆")
                ?.Pages.Select(static page => page.DisplayName) ?? []);
            report.ToolPages.AddRange(state.NavigationModules
                .FirstOrDefault(static module => module.DisplayName == "工具")
                ?.Pages.Select(static page => page.DisplayName) ?? []);
            report.DiagnosticPages.AddRange(state.NavigationModules
                .FirstOrDefault(static module => module.DisplayName == "诊断输出")
                ?.Pages.Select(static page => page.DisplayName) ?? []);
            report.WorkspacePages.AddRange(state.NavigationModules
                .FirstOrDefault(static module => module.DisplayName == "工作空间")
                ?.Pages.Select(static page => page.DisplayName) ?? []);
            report.PolicyPages.AddRange(state.NavigationModules
                .FirstOrDefault(static module => module.DisplayName == "审批策略")
                ?.Pages.Select(static page => page.DisplayName) ?? []);
            report.ProviderLabels.AddRange(state.ProviderItemLabels);
            report.ModelProtocolProviders.AddRange(state.ModelProtocolProviderLabels);
            report.ModelRouteSets.AddRange(state.ModelRouteSetLabels);
            report.PromptFiles.AddRange(state.PromptFileLabels);
            report.PromptSections.AddRange(state.PromptSectionLabels);
            report.SkillPackages.AddRange(state.SkillPackageLabels);
            report.ToolPackages.AddRange(state.ToolPackageLabels);
            report.ProviderPackages.AddRange(state.ModelProviderPackageLabels);
            report.McpServerPackages.AddRange(state.McpServerPackageLabels);
            report.ArtifactStorePackages.AddRange(state.ArtifactStorePackageLabels);
            report.DiagnosticSinkPackages.AddRange(state.DiagnosticSinkPackageLabels);
            report.WorkspaceResolverPackages.AddRange(state.WorkspaceResolverPackageLabels);
            report.PolicyStrategyPackages.AddRange(state.PolicyStrategyPackageLabels);

            Check(report, "navigation-modules", report.Modules.SequenceEqual(["基础配置", "配置方案编排", "代理", "模型", "记忆", "提示词", "技能", "工具", "MCP服务", "过程生成物", "诊断输出", "工作空间", "审批策略"], StringComparer.Ordinal),
                $"modules={string.Join(", ", report.Modules)}");
            Check(report, "navigation-initial-expanded-module", report.ExpandedModules.SequenceEqual(["基础配置"]),
                $"expanded={string.Join(", ", report.ExpandedModules)}");
            Check(report, "categories", ContainsAll(report.Categories, ["基础入口", "当前配置方案", "模块绑定", "阶段编排", "有效配置预览", "完整性检查", "模型选择", "模型路由方案", "代理行为", "安全治理", "工具配置", "记忆配置文件", "记忆空间", "记忆提供方", "记忆绑定", "诊断配置", "界面体验", "扩展导入", "工作空间配置", "提供方实例", "模型协议适配", "默认协议规则", "Prompt Pack", "技能包", "工具包", "协议适配器包", "MCP Server 包", "工件存储包", "诊断输出包", "工作空间解析包", "审批策略包"]),
                $"categories={string.Join(", ", report.Categories)}");
            Check(report, "foundation-page-order", report.FoundationPages.SequenceEqual(["基础入口", "界面体验", "扩展导入"], StringComparer.Ordinal),
                $"pages={string.Join(", ", report.FoundationPages)}");
            Check(report, "foundation-excludes-module-pages", !report.FoundationPages.Any(static page => page is "当前配置方案" or "模块绑定" or "代理行为" or "模型选择" or "安全治理" or "工具配置" or "诊断配置" or "工作空间配置"),
                $"pages={string.Join(", ", report.FoundationPages)}");
            Check(report, "profile-composition-page-order", report.ProfileCompositionPages.SequenceEqual(["当前配置方案", "模块绑定", "阶段编排", "有效配置预览", "完整性检查"], StringComparer.Ordinal),
                $"pages={string.Join(", ", report.ProfileCompositionPages)}");
            Check(report, "agent-page-order", report.AgentPages.SequenceEqual(["代理行为"], StringComparer.Ordinal),
                $"pages={string.Join(", ", report.AgentPages)}");
            Check(report, "model-configuration-page-order", report.ModelConfigurationPages.SequenceEqual(["模型选择", "提供方实例", "模型协议适配", "默认协议规则", "模型路由方案", "协议适配器包"], StringComparer.Ordinal),
                $"pages={string.Join(", ", report.ModelConfigurationPages)}");
            Check(report, "memory-page-order", report.MemoryPages.SequenceEqual(["记忆配置文件", "记忆空间", "记忆提供方", "记忆绑定"], StringComparer.Ordinal),
                $"pages={string.Join(", ", report.MemoryPages)}");
            Check(report, "tool-page-order", report.ToolPages.SequenceEqual(["工具配置", "工具包"], StringComparer.Ordinal),
                $"pages={string.Join(", ", report.ToolPages)}");
            Check(report, "diagnostic-page-order", report.DiagnosticPages.SequenceEqual(["诊断配置", "诊断输出包"], StringComparer.Ordinal),
                $"pages={string.Join(", ", report.DiagnosticPages)}");
            Check(report, "workspace-page-order", report.WorkspacePages.SequenceEqual(["工作空间配置", "工作空间解析包"], StringComparer.Ordinal),
                $"pages={string.Join(", ", report.WorkspacePages)}");
            Check(report, "policy-page-order", report.PolicyPages.SequenceEqual(["安全治理", "审批策略包"], StringComparer.Ordinal),
                $"pages={string.Join(", ", report.PolicyPages)}");
            CheckNavigationPageCaptions(state, report);
            CheckFoundationProfileDescriptionHidden(state, report);
            CheckProfileCompositionFieldPlacement(state, report);
            CheckProfileCompositionRemainingPages(state, report);
            CheckProfileCompositionManagementFlow(state, report);
            CheckExtensionsImportsDoesNotContainKnownModuleFields(state, report);
            Check(report, "tool-package-type-options", ContainsAll(state.ToolPackageTypeLabels, ["builtin", "assembly", "package", "plugin"]),
                $"options={string.Join(", ", state.ToolPackageTypeLabels)}");
            Check(report, "tool-provider-type-options", ContainsAll(state.ToolProviderTypeLabels, ["assembly", "package", "plugin"]),
                $"options={string.Join(", ", state.ToolProviderTypeLabels)}");
            Check(report, "model-provider-package-type-options", ContainsAll(state.ModelProviderPackageTypeLabels, ["builtin", "assembly", "package", "plugin"]),
                $"options={string.Join(", ", state.ModelProviderPackageTypeLabels)}");
            Check(report, "model-provider-adapter-type-options", ContainsAll(state.ModelProviderAdapterTypeLabels, ["assembly", "package", "plugin"]),
                $"options={string.Join(", ", state.ModelProviderAdapterTypeLabels)}");
            Check(report, "mcp-server-package-type-options", ContainsAll(state.McpServerPackageTypeLabels, ["builtin", "package", "plugin"]),
                $"options={string.Join(", ", state.McpServerPackageTypeLabels)}");
            Check(report, "mcp-server-transport-options", ContainsAll(state.McpServerTransportLabels, ["stdio", "http"]),
                $"options={string.Join(", ", state.McpServerTransportLabels)}");
            Check(report, "artifact-store-package-type-options", ContainsAll(state.ArtifactStorePackageTypeLabels, ["builtin", "filesystem", "assembly", "package", "plugin"]),
                $"options={string.Join(", ", state.ArtifactStorePackageTypeLabels)}");
            Check(report, "artifact-store-type-options", ContainsAll(state.ArtifactStoreTypeLabels, ["filesystem", "local-filesystem", "assembly", "package", "plugin"]),
                $"options={string.Join(", ", state.ArtifactStoreTypeLabels)}");
            Check(report, "diagnostic-sink-package-type-options", ContainsAll(state.DiagnosticSinkPackageTypeLabels, ["builtin", "package", "assembly", "plugin", "telemetry"]),
                $"options={string.Join(", ", state.DiagnosticSinkPackageTypeLabels)}");
            Check(report, "diagnostic-sink-type-options", ContainsAll(state.DiagnosticSinkTypeLabels, ["turn-log", "artifact-file", "telemetry", "otlp", "assembly", "package", "plugin"]),
                $"options={string.Join(", ", state.DiagnosticSinkTypeLabels)}");
            Check(report, "workspace-resolver-package-type-options", ContainsAll(state.WorkspaceResolverPackageTypeLabels, ["builtin", "package", "assembly", "plugin"]),
                $"options={string.Join(", ", state.WorkspaceResolverPackageTypeLabels)}");
            Check(report, "workspace-resolver-type-options", ContainsAll(state.WorkspaceResolverTypeLabels, ["marker", "assembly", "package", "plugin"]),
                $"options={string.Join(", ", state.WorkspaceResolverTypeLabels)}");
            Check(report, "policy-strategy-package-type-options", ContainsAll(state.PolicyStrategyPackageTypeLabels, ["builtin", "package", "assembly", "plugin"]),
                $"options={string.Join(", ", state.PolicyStrategyPackageTypeLabels)}");
            Check(report, "policy-strategy-type-options", ContainsAll(state.PolicyStrategyTypeLabels, ["rules", "assembly", "package", "plugin"]),
                $"options={string.Join(", ", state.PolicyStrategyTypeLabels)}");
            Check(report, "policy-strategy-approval-options", ContainsAll(state.PolicyStrategyApprovalPolicyLabels, ["never", "on-request", "on-failure", "untrusted"]),
                $"options={string.Join(", ", state.PolicyStrategyApprovalPolicyLabels)}");
            CheckEnumOptionSwitchIsolation(state, report);
            CheckBooleanChoiceEditor(state, report);
            CheckTelemetryChoiceEditor(state, report);
            CheckComboBoxItemSourceIsolation(report);
            CheckProviderComboBoxSelectedIndexRestore(report);
            CheckModelRouteSetRouteBindingRefresh(report);
            CheckModelRouteSetRouteSelection(report);
            CheckCollectionCommandBindingRefresh(report);
            CheckContextPanelState(state, report);
            CheckNavigationSelectionState(state, report);
            CheckAutoHidingScrollModeDoesNotDisable(report);
            CheckDailyModelSelectionFields(state, report);
            CheckTokenBudgetFieldsVisible(state, report);
            CheckDefaultInstanceFieldDisplayNames(state, report);
            CheckMemoryPageFields(state, report);
            CheckMemoryDefaultAndCreationFlow(report, home);
            CheckConfigurationLifecycleActions(report, home);

            SaveField(state, report, ConfigurationCategoryIds.ConnectivityModel, "model_route_set", "default");
            SaveField(state, report, ConfigurationCategoryIds.ConnectivityModel, "model_protocol_rule_set", "default");
            SaveField(state, report, ConfigurationCategoryIds.Experience, "features.config_gui_smoke.enabled", "true");
            SaveField(state, report, ConfigGuiState.MemoryProvidersCategoryId, "memory.providers.config_gui_smoke.mode", "read-write");
            SaveField(state, report, ConfigurationCategoryIds.CapabilitiesTools, "tools.grep_files.implementation_id", "config-gui-smoke-grep");
            SaveField(state, report, ConfigurationCategoryIds.CapabilitiesTools, "tool_providers.config_gui_smoke.type", "plugin");

            using var smokeModelEndpoint = SmokeModelEndpoint.Start();
            SaveProvider(state, report, smokeModelEndpoint.BaseUrl);
            SaveModelProtocolMappings(state, report);
            SaveDefaultModelProtocolRules(state, report);

            SaveModelRouteSet(state, report);
            var configAfterModelRouteSetWrites = ReadFileOrEmpty(state.ConfigPath);
            var providerInstancesAfterModelRouteSetWrites = ReadFileOrEmpty(ModulePath(state.ConfigPath, "model", "provider-instances", "default.toml"));

            SavePrompt(state, report);
            Check(report, "model-route-set-does-not-touch-provider-secret", providerInstancesAfterModelRouteSetWrites.Contains("api_key_env = \"TIANSHU_CONFIG_GUI_SMOKE_API_KEY\"", StringComparison.Ordinal),
                "保存模型路由方案必须保留提供方实例模块中的 secret env 引用但不读取或显示 secret 明文。");
            Check(report, "prompt-does-not-touch-tianshu-toml", string.Equals(configAfterModelRouteSetWrites, ReadFileOrEmpty(state.ConfigPath), StringComparison.Ordinal),
                "保存 Prompt 段只允许写入 Prompt Pack manifest，不应修改 tianshu.toml。");

            var configAfterPromptWrites = ReadFileOrEmpty(state.ConfigPath);
            SaveToolManifest(state, report);
            Check(report, "tool-manifest-does-not-touch-tianshu-toml", string.Equals(configAfterPromptWrites, ReadFileOrEmpty(state.ConfigPath), StringComparison.Ordinal),
                "保存工具包 manifest 只允许写入 modules/tools/packages/<package>/tool.toml，不应修改 tianshu.toml。");

            var configAfterToolManifestWrites = ReadFileOrEmpty(state.ConfigPath);
            SaveProviderManifest(state, report);
            Check(report, "provider-manifest-does-not-touch-tianshu-toml", string.Equals(configAfterToolManifestWrites, ReadFileOrEmpty(state.ConfigPath), StringComparison.Ordinal),
                "保存协议适配器包 manifest 只允许写入 modules/model/provider-adapters/<package>/provider.toml，不应修改 tianshu.toml。");

            var configAfterProviderManifestWrites = ReadFileOrEmpty(state.ConfigPath);
            SaveMcpServerManifest(state, report);
            Check(report, "mcp-server-manifest-does-not-touch-tianshu-toml", string.Equals(configAfterProviderManifestWrites, ReadFileOrEmpty(state.ConfigPath), StringComparison.Ordinal),
                "保存 MCP Server 包 manifest 只允许写入 modules/mcp-servers/<package>/server.toml，不应修改 tianshu.toml。");

            var configAfterMcpServerManifestWrites = ReadFileOrEmpty(state.ConfigPath);
            SaveArtifactStoreManifest(state, report);
            Check(report, "artifact-store-manifest-does-not-touch-tianshu-toml", string.Equals(configAfterMcpServerManifestWrites, ReadFileOrEmpty(state.ConfigPath), StringComparison.Ordinal),
                "保存工件存储包 manifest 只允许写入 modules/artifacts/stores/<package>/store.toml，不应修改 tianshu.toml。");

            var configAfterArtifactStoreManifestWrites = ReadFileOrEmpty(state.ConfigPath);
            SaveDiagnosticSinkManifest(state, report);
            Check(report, "diagnostic-sink-manifest-does-not-touch-tianshu-toml", string.Equals(configAfterArtifactStoreManifestWrites, ReadFileOrEmpty(state.ConfigPath), StringComparison.Ordinal),
                "保存诊断输出包 manifest 只允许写入 modules/diagnostics/sinks/<package>/sink.toml，不应修改 tianshu.toml。");

            var configAfterDiagnosticSinkManifestWrites = ReadFileOrEmpty(state.ConfigPath);
            SaveWorkspaceResolverManifest(state, report);
            Check(report, "workspace-resolver-manifest-does-not-touch-tianshu-toml", string.Equals(configAfterDiagnosticSinkManifestWrites, ReadFileOrEmpty(state.ConfigPath), StringComparison.Ordinal),
                "保存工作空间解析包 manifest 只允许写入 modules/workspace/resolvers/<package>/resolver.toml，不应修改 tianshu.toml。");

            var configAfterWorkspaceResolverManifestWrites = ReadFileOrEmpty(state.ConfigPath);
            SavePolicyStrategyManifest(state, report);
            Check(report, "policy-strategy-manifest-does-not-touch-tianshu-toml", string.Equals(configAfterWorkspaceResolverManifestWrites, ReadFileOrEmpty(state.ConfigPath), StringComparison.Ordinal),
                "保存审批策略包 manifest 只允许写入 modules/policies/strategies/<package>/policy.toml，不应修改 tianshu.toml。");

            SaveSkillPackageState(state, report);

            WriteReport(reportPath, report);
            return report.Checks.All(static check => check.Passed) ? 0 : 1;
        }
        catch (Exception ex)
        {
            report.Checks.Add(new ConfigGuiSmokeCheck("exception", false, ex.Message));
            WriteReport(reportPath, report);
            return 1;
        }
    }

    private static void SeedSmokeHomeIfNeeded(string home)
    {
        var configPath = Path.Combine(home, "tianshu.toml");
        if (!File.Exists(configPath))
        {
            File.WriteAllText(
                configPath,
                """
                schema_version = 1
                profile = "default"
                model = "config-gui-smoke-model"
                provider = "config_gui_smoke"

                [profiles.default]
                agent = "default"
                execution = "default"
                conversation = "default"
                permissions = "default"
                model_route_set = "default"
                memory = "default"
                tools = "default"
                tui = "default"
                workspace = "default"
                session = "default"
                collaboration = "default"
                workflow = "default"
                identity = "local"
                governance = "default"
                features = "default"
                realtime = "default"

                [profiles.default.stages]
                execution = "default"

                [models.config_gui_smoke_fast]
                provider = "config_gui_smoke"
                name = "config-gui-smoke-fast"

                [models.config_gui_smoke_model_updated]
                provider = "config_gui_smoke"
                name = "config-gui-smoke-model-updated"

                [agents.default]
                max_output_tokens = 2048

                [execution_profiles.default]
                provider = "config_gui_smoke"
                agent = "default"

                [conversation_profiles.default]
                thread_source = "local"
                history = "sliced"
                fuzzy_file_search = true
                pending_input_timeout_seconds = 120

                [permission_profiles.default]
                approval = "never"
                sandbox = "danger-full-access"

                [context]
                default_budget_tokens = 50000

                [features.config_gui_smoke]
                enabled = false
                stage = "experimental"
                description = "ConfigGUI smoke feature"

                [feature_profiles.default]
                enabled = ["config_gui_smoke"]
                disabled = []

                [realtime_profiles.default]
                enabled = false
                handoff_mode = "off"

                [memory.providers.config_gui_smoke]
                kind = "local"
                enabled = true
                mode = "read-only"
                root = "./data/memory"

                [memory_profiles.default]
                enabled = true
                default_space = "config_gui_smoke"
                overlay = true
                extract = "manual"
                retention = "keep"

                [memory_profiles.config_gui_smoke]
                enabled = true
                default_space = "config_gui_smoke"
                overlay = true
                extract = "manual"
                retention = "keep"

                [memory.spaces.config_gui_smoke]
                scope = "user"
                provider = "config_gui_smoke"
                read_only = false
                tags = ["smoke"]

                [memory.bindings.config_gui_smoke]
                space = "config_gui_smoke"
                provider = "config_gui_smoke"
                capabilities = ["filter", "add"]
                mode = "read-write"

                [tool_profiles.default]
                enabled = ["read_file", "grep_files"]
                disabled = []

                [tool_profiles.default.memory]
                enabled = true
                default_profile = "default"

                [workspace_profiles.default]
                trust_policy = "prompt"
                model_lock = "snapshot-on-create"

                [session_profiles.default]
                model_binding = "snapshot-on-create"
                memory_mode = "read-write"

                [collaboration_profiles.default]
                default_space = "default"
                default_execution_profile = "default"

                [workflow_profiles.default]
                verification_gate = "manual"

                [identity_profiles.local]
                account = "local"
                allow_device_sync = false

                [governance_profiles.default]
                approval_queue = true
                user_input_requests = true

                [tui_profiles.default]
                startup_card = true
                show_model = true

                [tools.grep_files]
                enabled = true
                implementation_id = "builtin-grep-files"
                implementation_kind = "managed"
                fallback = "managed-search"

                [tool_providers.config_gui_smoke]
                enabled = true
                type = "assembly"
                priority = 0
                """);
        }

        var modelRouteSetDirectory = Path.Combine(home, "modules", "model", "route-sets");
        Directory.CreateDirectory(modelRouteSetDirectory);
        var modelRouteSetPath = Path.Combine(modelRouteSetDirectory, "default.toml");
        if (!File.Exists(modelRouteSetPath))
        {
            File.WriteAllText(
                modelRouteSetPath,
                """
                [model_route_sets.default]
                display_name = "Default Smoke Route Set"
                description = "ConfigGUI smoke default model route set"

                [[model_route_sets.default.routes]]
                key = "default"
                candidates = [
                  { provider = "config_gui_smoke", model = "config-gui-smoke-model" },
                ]
                """);
        }

        var promptPackDirectory = Path.Combine(home, "modules", "prompts", "smoke");
        Directory.CreateDirectory(promptPackDirectory);
        var promptPackPath = Path.Combine(promptPackDirectory, "prompt.toml");
        if (!File.Exists(promptPackPath))
        {
            File.WriteAllText(
                promptPackPath,
                """
                id = "smoke"
                display_name = "ConfigGUI Smoke Prompt Pack"
                enabled = true
                type = "package"
                priority = 10

                [developer]
                mode = "append"
                text = "ConfigGUI smoke prompt pack"
                """);
        }

        var toolDirectory = Path.Combine(home, "modules", "tools", "packages", "builtin");
        Directory.CreateDirectory(toolDirectory);
        var toolManifestPath = Path.Combine(toolDirectory, "tool.toml");
        if (!File.Exists(toolManifestPath))
        {
            File.WriteAllText(
                toolManifestPath,
                """
                id = "builtin"
                display_name = "TianShu Builtin Tools"
                enabled = true
                type = "builtin"
                priority = 0

                [[providers]]
                id = "search"
                enabled = true
                type = "assembly"
                assembly_path = "./search/TianShu.Tools.Search.dll"
                provider_type = "TianShu.Tools.Search.SearchToolProvider"
                priority = 10
                replace_existing = true
                """);
        }

        var providerDirectory = Path.Combine(home, "modules", "model", "provider-adapters", "builtin");
        Directory.CreateDirectory(providerDirectory);
        var providerManifestPath = Path.Combine(providerDirectory, "provider.toml");
        if (!File.Exists(providerManifestPath))
        {
            File.WriteAllText(
                providerManifestPath,
                """
                id = "builtin"
                display_name = "TianShu Builtin Model Providers"
                enabled = true
                type = "builtin"
                priority = 0

                [[adapters]]
                id = "openai_responses"
                display_name = "OpenAI Responses"
                enabled = true
                type = "assembly"
                assembly_path = "./openai/TianShu.Provider.OpenAI.dll"
                priority = 20
                """);
        }

        var mcpServerDirectory = Path.Combine(home, "modules", "mcp-servers", "builtin");
        Directory.CreateDirectory(mcpServerDirectory);
        var mcpServerManifestPath = Path.Combine(mcpServerDirectory, "server.toml");
        if (!File.Exists(mcpServerManifestPath))
        {
            File.WriteAllText(
                mcpServerManifestPath,
                """
                id = "builtin"
                display_name = "TianShu Builtin MCP Servers"
                enabled = true
                type = "builtin"
                priority = 0

                [[servers]]
                id = "docs"
                display_name = "Docs MCP"
                enabled = false
                required = false
                transport = "http"
                url = "https://example.com/mcp"
                startup_timeout_ms = 10000
                tool_timeout_ms = 120000
                """);
        }

        var artifactStoreDirectory = Path.Combine(home, "modules", "artifacts", "stores", "builtin");
        Directory.CreateDirectory(artifactStoreDirectory);
        var artifactStoreManifestPath = Path.Combine(artifactStoreDirectory, "store.toml");
        if (!File.Exists(artifactStoreManifestPath))
        {
            File.WriteAllText(
                artifactStoreManifestPath,
                """
                id = "builtin"
                display_name = "TianShu Builtin Artifact Store"
                enabled = true
                type = "builtin"
                priority = 0

                [[stores]]
                id = "local-filesystem"
                display_name = "Local FileSystem Artifact Store"
                enabled = true
                type = "filesystem"
                root = "./data"
                max_history_versions = 20
                enable_cross_process_sync = true
                priority = 0
                """);
        }

        var diagnosticSinkDirectory = Path.Combine(home, "modules", "diagnostics", "sinks", "builtin");
        Directory.CreateDirectory(diagnosticSinkDirectory);
        var diagnosticSinkManifestPath = Path.Combine(diagnosticSinkDirectory, "sink.toml");
        if (!File.Exists(diagnosticSinkManifestPath))
        {
            File.WriteAllText(
                diagnosticSinkManifestPath,
                """
                id = "builtin"
                display_name = "TianShu Builtin Diagnostics"
                enabled = true
                type = "builtin"
                priority = 0

                [[sinks]]
                id = "turn-log"
                display_name = "Turn Log Diagnostics"
                enabled = true
                type = "turn-log"
                level = "stats"
                modules = ["provider", "runtime"]
                priority = 0

                [[sinks]]
                id = "provider-request-artifacts"
                display_name = "Provider Request Payload Artifacts"
                enabled = true
                type = "artifact-file"
                target = "./artifacts/provider-requests"
                level = "artifact"
                max_bytes = 1048576
                priority = 10
                """);
        }

        var workspaceResolverDirectory = Path.Combine(home, "modules", "workspace", "resolvers", "builtin");
        Directory.CreateDirectory(workspaceResolverDirectory);
        var workspaceResolverManifestPath = Path.Combine(workspaceResolverDirectory, "resolver.toml");
        if (!File.Exists(workspaceResolverManifestPath))
        {
            File.WriteAllText(
                workspaceResolverManifestPath,
                """
                id = "builtin"
                display_name = "TianShu Builtin Workspace Resolver"
                enabled = true
                type = "builtin"
                priority = 0

                [[resolvers]]
                id = "default"
                display_name = "Default Project Resolver"
                enabled = true
                type = "builtin"
                priority = 0
                root_markers = [".git", ".tianshu", "TianShu.sln"]
                profile = "default"
                trust_policy = "prompt"
                artifact_root = ".tianshu/artifacts"
                state_root = ".tianshu/state"
                ignore_globs = ["bin/**", "obj/**", ".git/**"]
                language_markers = ["*.sln", "*.csproj", "package.json"]
                framework_markers = ["*.sln", "Directory.Build.props"]
                """);
        }

        var policyStrategyDirectory = Path.Combine(home, "modules", "policies", "strategies", "builtin");
        Directory.CreateDirectory(policyStrategyDirectory);
        var policyStrategyManifestPath = Path.Combine(policyStrategyDirectory, "policy.toml");
        if (!File.Exists(policyStrategyManifestPath))
        {
            File.WriteAllText(
                policyStrategyManifestPath,
                """
                id = "builtin"
                display_name = "TianShu Builtin Policy Strategy"
                enabled = true
                type = "builtin"
                priority = 0

                [[strategies]]
                id = "default"
                display_name = "Default Approval Policy Strategy"
                enabled = true
                type = "builtin"
                priority = 0
                approval_policy = "on-request"
                sandbox_mode = "workspace-write"
                network_access = false
                allow_login_shell = true
                write_requires_approval_globs = ["**/*"]
                dangerous_command_patterns = ["git reset"]

                [[strategies.command_rules]]
                prefix = ["git", "reset"]
                decision = "ask"
                reason = "需要审批"
                """);
        }

        var skillDirectory = Path.Combine(home, "modules", "skills", "smoke");
        Directory.CreateDirectory(Path.Combine(skillDirectory, "agents"));
        Directory.CreateDirectory(Path.Combine(skillDirectory, "assets"));
        var skillPath = Path.Combine(skillDirectory, "SKILL.md");
        if (!File.Exists(skillPath))
        {
            File.WriteAllText(
                skillPath,
                """
                ---
                name: smoke
                description: ConfigGUI smoke skill package
                ---

                用于验证 ConfigGUI 技能包投影。
                """);
        }

        var skillMetadataPath = Path.Combine(skillDirectory, "agents", "tianshu.yaml");
        if (!File.Exists(skillMetadataPath))
        {
            File.WriteAllText(
                skillMetadataPath,
                """
                interface:
                  display_name: "ConfigGUI Smoke Skill"
                  short_description: "用于验证技能包集合页。"
                  default_prompt: "使用 smoke 技能。"
                dependencies:
                  tools:
                    - type: tool
                      value: shell
                      description: "验证依赖展示。"
                permissions:
                  network:
                    enabled: false
                """);
        }
    }

    private static void SaveField(ConfigGuiState state, ConfigGuiSmokeReport report, string categoryId, string key, string value)
    {
        state.SearchText.Value = string.Empty;
        state.SelectCategory(categoryId);
        var field = state.FilteredFields.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            Check(report, $"field:{key}", false, $"未在 {categoryId} 分类投影中找到字段。");
            return;
        }

        if (!state.BeginEdit(field))
        {
            Check(report, $"field:{key}", false, $"字段不可编辑：{field.DisplayName}");
            return;
        }

        state.SelectedEditValue.Value = value;
        state.SaveSelected();
        var saved = ReadFileOrEmpty(state.ConfigPath).Contains(value, StringComparison.Ordinal);
        Check(report, $"field:{key}", saved, state.StatusText.Value);
    }

    private static void CheckEnumOptionSwitchIsolation(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SearchText.Value = string.Empty;
        state.SelectCategory(ConfigurationCategoryIds.SecurityGovernance);
        var approvalField = state.FilteredFields.FirstOrDefault(row => string.Equals(row.Key, "approval_policy", StringComparison.OrdinalIgnoreCase));
        var sandboxField = state.FilteredFields.FirstOrDefault(row => string.Equals(row.Key, "sandbox_mode", StringComparison.OrdinalIgnoreCase));
        if (approvalField is null || sandboxField is null)
        {
            Check(report, "enum-options:sandbox_mode", false, "未找到 approval_policy 或 sandbox_mode 字段。");
            return;
        }

        state.BeginEdit(approvalField);
        var approvalOptions = state.SelectedEnumLabels.ToArray();
        state.BeginEdit(sandboxField);
        var sandboxOptions = state.SelectedEnumLabels.ToArray();

        var approvalOk = approvalOptions.SequenceEqual(["never", "on-request", "on-failure", "always", "ask", "untrusted"], StringComparer.OrdinalIgnoreCase);
        var sandboxOk = sandboxOptions.SequenceEqual(["read-only", "workspace-write", "danger-full-access"], StringComparer.OrdinalIgnoreCase)
            && !sandboxOptions.Any(option => string.Equals(option, "ask", StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(option, "never", StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(option, "on-failure", StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(option, "untrusted", StringComparison.OrdinalIgnoreCase));

        Check(report, "enum-options:approval_policy", approvalOk, $"options={string.Join(", ", approvalOptions)}");
        Check(report, "enum-options:sandbox_mode", sandboxOk, $"options={string.Join(", ", sandboxOptions)}");
    }

    private static void CheckBooleanChoiceEditor(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SearchText.Value = string.Empty;
        state.SelectCategory(ConfigurationCategoryIds.Experience);
        var field = state.FilteredFields.FirstOrDefault(row => string.Equals(row.Key, "features.config_gui_smoke.enabled", StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            Check(report, "boolean-editor:field", false, "未找到 features.config_gui_smoke.enabled 字段。");
            return;
        }

        state.BeginEdit(field);
        var labels = state.SelectedEnumLabels.ToArray();
        Check(report, "boolean-editor:uses-dropdown", state.IsEnumEditorVisible.Value && !state.IsTextEditorVisible.Value,
            $"kind={field.ValueKind}; labels={string.Join(", ", labels)}");
        Check(report, "boolean-editor:options", labels.SequenceEqual(["true", "false"], StringComparer.OrdinalIgnoreCase),
            $"labels={string.Join(", ", labels)}");
        state.SelectEnumIndex(1);
        Check(report, "boolean-editor:select-false", string.Equals(state.SelectedEditValue.Value, "false", StringComparison.OrdinalIgnoreCase),
            $"value={state.SelectedEditValue.Value}");
        state.SelectEnumIndex(0);

        state.SelectCategory(ConfigurationCategoryIds.CapabilitiesTools);
        var enumField = state.FilteredFields.FirstOrDefault(row => string.Equals(row.Key, "tools.shell.environment_policy", StringComparison.OrdinalIgnoreCase));
        if (enumField is null)
        {
            Check(report, "boolean-editor:enum-switch-field", false, "未找到 tools.shell.environment_policy 字段。");
            return;
        }

        state.BeginEdit(enumField);
        labels = state.SelectedEnumLabels.ToArray();
        var enumOptionsOk = labels.Contains("empty", StringComparer.OrdinalIgnoreCase)
            && labels.Contains("inherit-safe", StringComparer.OrdinalIgnoreCase)
            && labels.Contains("inherit-all", StringComparer.OrdinalIgnoreCase)
            && !labels.Contains("true", StringComparer.OrdinalIgnoreCase)
            && !labels.Contains("false", StringComparer.OrdinalIgnoreCase);
        Check(report, "boolean-editor:enum-switch-no-boolean-options", enumOptionsOk,
            $"labels={string.Join(", ", labels)}");
    }

    private static void CheckTelemetryChoiceEditor(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SearchText.Value = string.Empty;
        state.SelectCategory(ConfigurationCategoryIds.Experience);
        var booleanField = state.FilteredFields.FirstOrDefault(row => string.Equals(row.Key, "features.config_gui_smoke.enabled", StringComparison.OrdinalIgnoreCase));
        state.SelectCategory(ConfigurationCategoryIds.Foundation);
        var telemetryField = state.FilteredFields.FirstOrDefault(row => string.Equals(row.Key, "app.telemetry", StringComparison.OrdinalIgnoreCase));
        if (booleanField is null || telemetryField is null)
        {
            Check(report, "telemetry-editor:field", false, "未找到 features.config_gui_smoke.enabled 或 app.telemetry 字段。");
            return;
        }

        state.BeginEdit(booleanField);
        state.BeginEdit(telemetryField);
        var labels = state.SelectedEnumLabels.ToArray();
        var ok = labels.SequenceEqual(["off", "local", "remote"], StringComparer.OrdinalIgnoreCase)
            && !labels.Contains("true", StringComparer.OrdinalIgnoreCase)
            && !labels.Contains("false", StringComparer.OrdinalIgnoreCase);

        Check(report, "telemetry-editor:no-boolean-options", ok, $"labels={string.Join(", ", labels)}");
    }

    private static void CheckComboBoxItemSourceIsolation(ConfigGuiSmokeReport report)
    {
        var editor = new ComboBox();
        ConfigGuiComboBoxUtilities.ReplaceItems(editor, ["true", "false"]);
        editor.IsDropDownOpen = true;
        ConfigGuiComboBoxUtilities.ReplaceItems(editor, ["off", "local", "remote"]);
        editor.SelectedIndex = 1;

        var labels = ConfigGuiComboBoxUtilities.SnapshotLabels(editor, 6);
        var ok = labels.SequenceEqual(["off", "local", "remote"], StringComparer.OrdinalIgnoreCase)
            && !labels.Contains("true", StringComparer.OrdinalIgnoreCase)
            && !labels.Contains("false", StringComparer.OrdinalIgnoreCase)
            && editor.SelectedIndex == 1;

        Check(report, "combobox-source:boolean-to-telemetry-isolated", ok,
            $"labels={string.Join(", ", labels)}; selected={editor.SelectedIndex}");

        var detailEditor = new ComboBox();
        ConfigGuiComboBoxUtilities.ReplaceItemsAndSelect(detailEditor, ["default - Default Model Protocol Rules"], selectedIndex: 0);
        detailEditor.IsDropDownOpen = true;
        ConfigGuiComboBoxUtilities.ReplaceItemsAndSelect(detailEditor, ["never", "on-request", "on-failure", "always", "ask", "untrusted"], selectedIndex: 0);
        var approvalLabels = ConfigGuiComboBoxUtilities.SnapshotLabels(detailEditor, 8);
        detailEditor.IsDropDownOpen = true;
        ConfigGuiComboBoxUtilities.ReplaceItemsAndSelect(detailEditor, ["read-only", "workspace-write", "danger-full-access"], selectedIndex: 1);
        var sandboxLabels = ConfigGuiComboBoxUtilities.SnapshotLabels(detailEditor, 6);
        var detailOk = approvalLabels.SequenceEqual(["never", "on-request", "on-failure", "always", "ask", "untrusted"], StringComparer.OrdinalIgnoreCase)
            && sandboxLabels.SequenceEqual(["read-only", "workspace-write", "danger-full-access"], StringComparer.OrdinalIgnoreCase)
            && !approvalLabels.Any(static label => label.Contains("Default Model Protocol Rules", StringComparison.OrdinalIgnoreCase))
            && !sandboxLabels.Any(static label => label.Contains("Default Model Protocol Rules", StringComparison.OrdinalIgnoreCase))
            && detailEditor.SelectedIndex == 1
            && !detailEditor.IsDropDownOpen;

        Check(report, "combobox-source:model-protocol-to-security-isolated", detailOk,
            $"approval={string.Join(", ", approvalLabels)}; sandbox={string.Join(", ", sandboxLabels)}; selected={detailEditor.SelectedIndex}; open={detailEditor.IsDropDownOpen}");

        var seededEditor = new ComboBox().StableItems(["default - Default Model Protocol Rules"]);
        seededEditor.IsDropDownOpen = true;
        ConfigGuiComboBoxUtilities.ReplaceItemsAndSelect(seededEditor, ["never", "on-request", "on-failure", "always", "ask", "untrusted"], selectedIndex: 0);
        var seededApprovalLabels = ConfigGuiComboBoxUtilities.SnapshotLabels(seededEditor, 8);
        seededEditor.IsDropDownOpen = true;
        ConfigGuiComboBoxUtilities.ReplaceItemsAndSelect(seededEditor, ["read-only", "workspace-write", "danger-full-access"], selectedIndex: 1);
        var seededSandboxLabels = ConfigGuiComboBoxUtilities.SnapshotLabels(seededEditor, 6);
        var seededOk = seededApprovalLabels.SequenceEqual(["never", "on-request", "on-failure", "always", "ask", "untrusted"], StringComparer.OrdinalIgnoreCase)
            && seededSandboxLabels.SequenceEqual(["read-only", "workspace-write", "danger-full-access"], StringComparer.OrdinalIgnoreCase)
            && !seededApprovalLabels.Any(static label => label.Contains("Default Model Protocol Rules", StringComparison.OrdinalIgnoreCase))
            && !seededSandboxLabels.Any(static label => label.Contains("Default Model Protocol Rules", StringComparison.OrdinalIgnoreCase))
            && seededEditor.SelectedIndex == 1
            && !seededEditor.IsDropDownOpen;

        Check(report, "combobox-source:static-items-to-dynamic-isolated", seededOk,
            $"approval={string.Join(", ", seededApprovalLabels)}; sandbox={string.Join(", ", seededSandboxLabels)}; selected={seededEditor.SelectedIndex}; open={seededEditor.IsDropDownOpen}");

        var routeEditor = new ComboBox().StableItems(["default - default", "review - review"]);
        routeEditor.SelectedIndex = 0;
        routeEditor.IsDropDownOpen = true;
        ConfigGuiComboBoxUtilities.ReplaceItemsAndSelect(routeEditor, ["default - default", "review - review"], selectedIndex: 0);
        var routeLabels = ConfigGuiComboBoxUtilities.SnapshotLabels(routeEditor, 4);
        var routeOk = routeLabels.SequenceEqual(["default - default", "review - review"], StringComparer.Ordinal)
            && routeLabels.Count(static label => string.Equals(label, "default - default", StringComparison.Ordinal)) == 1
            && routeEditor.SelectedIndex == 0
            && !routeEditor.IsDropDownOpen;

        Check(report, "combobox-source:route-refresh-no-duplicate-selected-label", routeOk,
            $"labels={string.Join(", ", routeLabels)}; selected={routeEditor.SelectedIndex}; open={routeEditor.IsDropDownOpen}");
    }

    private static void CheckProviderComboBoxSelectedIndexRestore(ConfigGuiSmokeReport report)
    {
        var providerEditor = new ComboBox();
        var protocolEditor = new ComboBox();
        var providerLabels = new[] { "openai", "openai-compatible" };
        var protocolLabels = new[] { "auto", "openai_chat_completions", "anthropic_messages" };

        ConfigGuiComboBoxUtilities.ReplaceItemsAndSelect(providerEditor, providerLabels, selectedIndex: 1);
        ConfigGuiComboBoxUtilities.ReplaceItemsAndSelect(protocolEditor, protocolLabels, selectedIndex: 2);

        Check(report, "provider-combobox-selected-index", providerEditor.SelectedIndex == 1,
            $"control={providerEditor.SelectedIndex}; labels={string.Join(", ", providerLabels)}");
        Check(report, "provider-protocol-combobox-selected-index", protocolEditor.SelectedIndex == 2,
            $"control={protocolEditor.SelectedIndex}; labels={string.Join(", ", protocolLabels)}");
    }

    private static void CheckModelRouteSetRouteBindingRefresh(ConfigGuiSmokeReport report)
    {
        var result = Program.RunModelRouteSetRouteBindingSmoke();
        Check(report, "model-route-set-route-fixed-stage-registry", result.Passed, result.Detail);
    }

    private static void CheckModelRouteSetRouteSelection(ConfigGuiSmokeReport report)
    {
        var result = Program.RunModelRouteSetRouteSelectionSmoke();
        Check(report, "model-route-set-route-selection-sync", result.Passed, result.Detail);
    }

    private static void CheckCollectionCommandBindingRefresh(ConfigGuiSmokeReport report)
    {
        var result = Program.RunCollectionCommandBindingSmoke();
        Check(report, "collection-command-bound-add-refresh", result.Passed, result.Detail);
    }

    private static void CheckContextPanelState(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SelectCategory(ConfigurationCategoryIds.ConnectivityModel);
        Check(report, "context-panel-field-visible", state.IsDetailViewVisible.Value && state.IsFieldDetailEditorVisible.Value,
            $"title={state.ContextTitle.Value}; target={state.ContextSaveTarget.Value}");
        Check(report, "context-panel-field-target", state.ContextSaveTarget.Value.EndsWith("tianshu.toml", StringComparison.OrdinalIgnoreCase),
            $"target={state.ContextSaveTarget.Value}");

        state.SelectCategory(ConfigGuiState.MemoryProvidersCategoryId);
        var memoryProviderField = state.FilteredFields.FirstOrDefault(static row => string.Equals(row.Key, "memory.providers.config_gui_smoke.mode", StringComparison.OrdinalIgnoreCase));
        if (memoryProviderField is not null)
        {
            state.SelectField(memoryProviderField);
            Check(report, "context-panel-module-field-target", ContainsPathSegments(state.ContextSaveTarget.Value, "modules", "memory", "providers"),
                $"target={state.ContextSaveTarget.Value}");
        }
        else
        {
            Check(report, "context-panel-module-field", false, "未找到 memory.providers.config_gui_smoke.mode 字段。");
        }

        var modelRouteSetCategoryId = state.Categories.FirstOrDefault(static category => category.DisplayName == "模型路由方案")?.Id;
        if (!string.IsNullOrWhiteSpace(modelRouteSetCategoryId))
        {
            state.SelectCategory(modelRouteSetCategoryId);
            Check(report, "context-panel-model-route-set-visible", state.IsDetailViewVisible.Value && !state.IsFieldDetailEditorVisible.Value,
                $"title={state.ContextTitle.Value}; target={state.ContextSaveTarget.Value}");
            Check(report, "context-panel-model-route-set-target", ContainsPathSegments(state.ContextSaveTarget.Value, "modules", "model", "route-sets"),
                $"target={state.ContextSaveTarget.Value}");
        }
        else
        {
            Check(report, "context-panel-model-route-set-category", false, "未找到模型路由方案分类。");
        }

        var modelProtocolCategoryId = state.Categories.FirstOrDefault(static category => category.DisplayName == "模型协议适配")?.Id;
        if (!string.IsNullOrWhiteSpace(modelProtocolCategoryId))
        {
            state.SelectCategory(modelProtocolCategoryId);
            Check(report, "context-panel-model-protocol-visible", state.IsDetailViewVisible.Value && !state.IsFieldDetailEditorVisible.Value,
                $"title={state.ContextTitle.Value}; target={state.ContextSaveTarget.Value}");
            Check(report, "context-panel-model-protocol-target", ContainsPathSegments(state.ContextSaveTarget.Value, "modules", "model", "provider-instances"),
                $"target={state.ContextSaveTarget.Value}");
            Check(report, "context-panel-model-protocol-source", state.ContextSourceName.Value.Contains("model_overrides", StringComparison.Ordinal)
                                                                  && state.ContextSourceName.Value.Contains("protocol_rules", StringComparison.Ordinal),
                $"source={state.ContextSourceName.Value}");
        }
        else
        {
            Check(report, "context-panel-model-protocol-category", false, "未找到模型协议适配分类。");
        }

        var toolPackageCategoryId = state.Categories.FirstOrDefault(static category => category.DisplayName == "工具包")?.Id;
        if (string.IsNullOrWhiteSpace(toolPackageCategoryId))
        {
            Check(report, "context-panel-collection-category", false, "未找到工具包分类。");
            return;
        }

        state.SelectCategory(toolPackageCategoryId);
        Check(report, "context-panel-collection-visible", state.IsDetailViewVisible.Value && !state.IsFieldDetailEditorVisible.Value,
            $"title={state.ContextTitle.Value}; target={state.ContextSaveTarget.Value}");
        Check(report, "context-panel-collection-target", state.ContextSaveTarget.Value.Contains("tools", StringComparison.OrdinalIgnoreCase),
            $"target={state.ContextSaveTarget.Value}");
        Check(report, "context-panel-collection-boundary", state.ContextWriteBoundary.Value.Contains("不隐式修改 tianshu.toml", StringComparison.Ordinal),
            state.ContextWriteBoundary.Value);
    }

    private static void CheckNavigationSelectionState(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SelectCategory(ConfigurationCategoryIds.ConnectivityModel);
        var selectedHighlights = state.Categories.Where(static category => category.IsNavigationHighlighted.Value).Select(static category => category.DisplayName).ToArray();
        Check(report, "navigation-selected-highlight-single", selectedHighlights.SequenceEqual(["模型选择"], StringComparer.Ordinal),
            $"highlights={string.Join(", ", selectedHighlights)}");
        Check(report, "content-page-title-selected-category", string.Equals(state.SelectedPageTitle.Value, "模型选择", StringComparison.Ordinal),
            $"title={state.SelectedPageTitle.Value}");

        var modelRouteSetCategoryId = state.Categories.FirstOrDefault(static category => category.DisplayName == "模型路由方案")?.Id;
        if (!string.IsNullOrWhiteSpace(modelRouteSetCategoryId))
        {
            state.SelectCategory(modelRouteSetCategoryId);
            selectedHighlights = state.Categories.Where(static category => category.IsNavigationHighlighted.Value).Select(static category => category.DisplayName).ToArray();
            Check(report, "navigation-selected-model-route-set", selectedHighlights.SequenceEqual(["模型路由方案"], StringComparer.Ordinal),
                $"highlights={string.Join(", ", selectedHighlights)}");
        }
        else
        {
            Check(report, "navigation-selected-model-route-set-category", false, "未找到模型路由方案分类。");
        }

        var modelProtocolCategoryId = state.Categories.FirstOrDefault(static category => category.DisplayName == "模型协议适配")?.Id;
        if (!string.IsNullOrWhiteSpace(modelProtocolCategoryId))
        {
            state.SelectCategory(modelProtocolCategoryId);
            selectedHighlights = state.Categories.Where(static category => category.IsNavigationHighlighted.Value).Select(static category => category.DisplayName).ToArray();
            Check(report, "navigation-selected-model-protocol", selectedHighlights.SequenceEqual(["模型协议适配"], StringComparer.Ordinal),
                $"highlights={string.Join(", ", selectedHighlights)}");
        }
        else
        {
            Check(report, "navigation-selected-model-protocol-category", false, "未找到模型协议适配分类。");
        }

        var toolPackageCategoryId = state.Categories.FirstOrDefault(static category => category.DisplayName == "工具包")?.Id;
        if (string.IsNullOrWhiteSpace(toolPackageCategoryId))
        {
            Check(report, "navigation-selected-tool-package", false, "未找到工具包分类。");
            return;
        }

        state.SelectCategory(toolPackageCategoryId);
        selectedHighlights = state.Categories.Where(static category => category.IsNavigationHighlighted.Value).Select(static category => category.DisplayName).ToArray();
        Check(report, "navigation-selected-highlight-switch", selectedHighlights.SequenceEqual(["工具包"], StringComparer.Ordinal),
            $"highlights={string.Join(", ", selectedHighlights)}");
        Check(report, "content-page-title-tool-package", string.Equals(state.SelectedPageTitle.Value, "工具包", StringComparison.Ordinal),
            $"title={state.SelectedPageTitle.Value}");

        state.SelectCategory(ConfigurationCategoryIds.ConnectivityModel);
        var modelRowBeforeRefresh = state.Categories.FirstOrDefault(static category => category.DisplayName == "模型选择");
        var toolRowBeforeRefresh = state.Categories.FirstOrDefault(static category => category.DisplayName == "工具包");
        state.Refresh();
        toolPackageCategoryId = state.Categories.FirstOrDefault(static category => category.DisplayName == "工具包")?.Id;
        if (modelRowBeforeRefresh is null || toolRowBeforeRefresh is null || string.IsNullOrWhiteSpace(toolPackageCategoryId))
        {
            Check(report, "navigation-highlight-preserved-row-bindings", false, "刷新前后未找到模型选择或工具包分类。");
            return;
        }

        state.SelectCategory(toolPackageCategoryId);
        var preservedRowBindings = !modelRowBeforeRefresh.IsNavigationHighlighted.Value
            && toolRowBeforeRefresh.IsNavigationHighlighted.Value;
        Check(report, "navigation-highlight-preserved-row-bindings", preservedRowBindings,
            $"modelOld={modelRowBeforeRefresh.IsNavigationHighlighted.Value}; toolOld={toolRowBeforeRefresh.IsNavigationHighlighted.Value}");
    }

    private static void CheckNavigationPageCaptions(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        var pages = state.NavigationModules.SelectMany(static module => module.Pages).ToArray();
        var mismatched = pages
            .Where(static page => !string.Equals(page.CaptionText.Value, page.DisplayName, StringComparison.Ordinal))
            .Select(static page => $"{page.DisplayName}->{page.CaptionText.Value}")
            .ToArray();

        Check(report, "navigation-page-captions-no-counts", mismatched.Length == 0,
            mismatched.Length == 0
                ? $"captions={string.Join(", ", pages.Select(static page => page.CaptionText.Value))}"
                : $"mismatched={string.Join(", ", mismatched)}");
    }

    private static void CheckAutoHidingScrollModeDoesNotDisable(ConfigGuiSmokeReport report)
    {
        var root = Directory.GetCurrentDirectory();
        var shellPath = Path.Combine(root, "src", "Presentations", "TianShu.ConfigGui", "Program.Shell.cs");
        var detailPath = Path.Combine(root, "src", "Presentations", "TianShu.ConfigGui", "Program.BasicConfiguration.cs");
        if (!File.Exists(shellPath) || !File.Exists(detailPath))
        {
            Check(report, "auto-hiding-scroll-mode-source", true, "未在当前工作目录找到源码文件，跳过源码级滚动模式回归。");
            return;
        }

        var shellText = File.ReadAllText(shellPath);
        var detailText = File.ReadAllText(detailPath);
        var disablesScroll = shellText.Contains("VerticalScroll(ScrollMode.Disabled)", StringComparison.Ordinal)
                             || shellText.Contains("VerticalScroll = ScrollMode.Disabled", StringComparison.Ordinal)
                             || detailText.Contains("VerticalScroll(ScrollMode.Disabled)", StringComparison.Ordinal)
                             || detailText.Contains("VerticalScroll = ScrollMode.Disabled", StringComparison.Ordinal);
        Check(report, "auto-hiding-scroll-mode-stays-auto", !disablesScroll,
            "自动隐藏滚动条不得通过 ScrollMode.Disabled 隐藏滚动条，否则 MewUI 会在模式切换时重置 offset。");
    }

    private static void CheckFoundationProfileDescriptionHidden(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SearchText.Value = string.Empty;
        state.SelectCategory(ConfigurationCategoryIds.Foundation);

        var hiddenKeys = state.FilteredFields
            .Where(static field => field.Key.StartsWith("profiles.", StringComparison.OrdinalIgnoreCase)
                                   && field.Key.EndsWith(".description", StringComparison.OrdinalIgnoreCase))
            .Select(static field => field.Key)
            .ToArray();
        Check(report, "foundation-profile-description-hidden", hiddenKeys.Length == 0,
            hiddenKeys.Length == 0
                ? $"keys={string.Join(", ", state.FilteredFields.Select(static field => field.Key))}"
                : $"visible={string.Join(", ", hiddenKeys)}");
    }

    private static void CheckProfileCompositionFieldPlacement(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SearchText.Value = string.Empty;
        state.SelectCategory(ConfigurationCategoryIds.Foundation);

        var foundationProfileFields = state.FilteredFields
            .Where(static field => string.Equals(field.Key, "profile", StringComparison.OrdinalIgnoreCase)
                                   || field.Key.StartsWith("profiles.", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Check(report, "foundation-profile-composition-hidden", foundationProfileFields.Length == 0,
            $"keys={string.Join(", ", foundationProfileFields.Select(static field => field.Key))}");

        state.SelectCategory(ConfigGuiState.ProfileCompositionCurrentCategoryId);
        var currentKeys = state.FilteredFields.Select(static field => field.Key).ToArray();
        var profileField = state.FilteredFields.FirstOrDefault(static field => string.Equals(field.Key, "profile", StringComparison.OrdinalIgnoreCase));
        var extendsField = state.FilteredFields.FirstOrDefault(static field => string.Equals(field.Key, "profiles.default.extends", StringComparison.OrdinalIgnoreCase));
        Check(report, "profile-composition-current-fields", currentKeys.SequenceEqual(["profile", "profiles.default.extends"], StringComparer.OrdinalIgnoreCase)
                                                               && profileField?.UsesChoiceEditor == true
                                                               && profileField.ChoiceOptions.Any(static option => string.Equals(option.Value, "default", StringComparison.OrdinalIgnoreCase))
                                                               && extendsField?.CanEdit == true
                                                               && extendsField.UsesChoiceEditor
                                                               && extendsField.ChoiceOptions.Any(static option => string.IsNullOrEmpty(option.Value) && string.Equals(option.DisplayLabel, "不继承", StringComparison.Ordinal)),
            $"keys={string.Join(", ", currentKeys)}; profileChoices={string.Join(", ", profileField?.ChoiceOptions.Select(static option => option.DisplayLabel) ?? [])}; extendsChoices={string.Join(", ", extendsField?.ChoiceOptions.Select(static option => option.DisplayLabel) ?? [])}");

        state.SelectCategory(ConfigGuiState.ProfileCompositionBindingsCategoryId);
        var profileFields = state.FilteredFields
            .Where(static field => field.Key.StartsWith("profiles.", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var hasInstancePrefix = profileFields.Any(static field => field.DisplayName.Contains(" / ", StringComparison.Ordinal));
        var expectedNames = new[] { "Agent 配置文件", "执行配置文件", "对话配置文件", "模型路由方案", "记忆配置文件", "权限配置文件" };
        var hasExpectedNames = expectedNames.All(expected => profileFields.Any(field => string.Equals(field.DisplayName, expected, StringComparison.Ordinal)));

        Check(report, "profile-composition-reference-display-names", !hasInstancePrefix && hasExpectedNames,
            $"names={string.Join(", ", profileFields.Select(static field => field.DisplayName))}");

        var chooserChecks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["profiles.default.agent"] = "default",
            ["profiles.default.execution"] = "default",
            ["profiles.default.conversation"] = "default",
            ["profiles.default.model_route_set"] = "default",
            ["profiles.default.permissions"] = "default",
            ["profiles.default.memory"] = "default",
            ["profiles.default.tools"] = "default",
            ["profiles.default.workspace"] = "default",
            ["profiles.default.session"] = "default",
            ["profiles.default.identity"] = "local",
            ["profiles.default.governance"] = "default",
            ["profiles.default.features"] = "default",
            ["profiles.default.realtime"] = "default",
        };
        var chooserFailures = chooserChecks
            .Select(pair => new
            {
                Key = pair.Key,
                Expected = pair.Value,
                Field = profileFields.FirstOrDefault(field => string.Equals(field.Key, pair.Key, StringComparison.OrdinalIgnoreCase)),
            })
            .Where(item => item.Field?.UsesChoiceEditor != true
                           || !item.Field.ChoiceOptions.Any(option => string.Equals(option.Value, item.Expected, StringComparison.OrdinalIgnoreCase)))
            .Select(item => $"{item.Key}:{item.Field?.UsesChoiceEditor}/{string.Join("|", item.Field?.ChoiceOptions.Select(static option => option.Value) ?? [])}")
            .ToArray();
        Check(report, "profile-composition-binding-choosers", chooserFailures.Length == 0,
            chooserFailures.Length == 0
                ? $"keys={string.Join(", ", chooserChecks.Keys)}"
                : $"failures={string.Join(", ", chooserFailures)}");

        Check(report, "profile-composition-inheritance-page-removed", !report.ProfileCompositionPages.Contains("继承与覆写", StringComparer.Ordinal),
            $"pages={string.Join(", ", report.ProfileCompositionPages)}");
    }

    private static void CheckProfileCompositionRemainingPages(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SearchText.Value = string.Empty;

        state.SelectCategory(ConfigGuiState.ProfileCompositionStagesCategoryId);
        var stageFields = state.FilteredFields.ToArray();
        var stageKeys = stageFields.Select(static field => field.Key).ToArray();
        var stagesOk = stageFields.Length == 4
                       && stageKeys.All(static key => key.StartsWith("profiles.default.stages.", StringComparison.OrdinalIgnoreCase))
                       && stageFields.All(static field => field.CanEdit && field.UsesChoiceEditor)
                       && stageFields.Any(static field => string.Equals(field.Key, "profiles.default.stages.execution", StringComparison.OrdinalIgnoreCase)
                                                        && field.ChoiceOptions.Any(static option => string.Equals(option.Value, "default", StringComparison.OrdinalIgnoreCase)));
        Check(report, "profile-composition-stages-fields", stagesOk,
            $"keys={string.Join(", ", stageKeys)}; choices={string.Join(", ", stageFields.SelectMany(static field => field.ChoiceOptions.Select(static option => option.Value)).Distinct(StringComparer.OrdinalIgnoreCase))}");

        state.SelectCategory(ConfigGuiState.ProfileCompositionPreviewCategoryId);
        var previewFields = state.FilteredFields.ToArray();
        var previewOk = previewFields.Any(static field => string.Equals(field.Key, "__profile_composition.preview.agent", StringComparison.OrdinalIgnoreCase)
                                                       && string.Equals(field.CurrentValueSummary, "default", StringComparison.OrdinalIgnoreCase))
                        && previewFields.Any(static field => string.Equals(field.Key, "__profile_composition.preview.identity", StringComparison.OrdinalIgnoreCase)
                                                       && string.Equals(field.CurrentValueSummary, "local", StringComparison.OrdinalIgnoreCase))
                        && previewFields.Any(static field => string.Equals(field.Key, "__profile_composition.preview.stages.execution", StringComparison.OrdinalIgnoreCase)
                                                       && string.Equals(field.CurrentValueSummary, "default", StringComparison.OrdinalIgnoreCase))
                        && previewFields.All(static field => !field.CanEdit);
        Check(report, "profile-composition-preview-fields", previewOk,
            $"fields={string.Join(", ", previewFields.Select(static field => $"{field.Key}={field.CurrentValueSummary}/{field.CanEdit}"))}");

        state.SelectCategory(ConfigGuiState.ProfileCompositionValidationCategoryId);
        var validationFields = state.FilteredFields.ToArray();
        var validationOk = validationFields.Length == 1
                           && validationFields.Any(static field => string.Equals(field.Key, "__profile_composition.validation.ok", StringComparison.OrdinalIgnoreCase)
                                                            && string.Equals(field.CurrentValueSummary, "通过", StringComparison.OrdinalIgnoreCase))
                           && validationFields.All(static field => !field.CanEdit);
        Check(report, "profile-composition-validation-fields", validationOk,
            $"fields={string.Join(", ", validationFields.Select(static field => $"{field.Key}={field.CurrentValueSummary}/{field.Issues}"))}");
    }

    private static void CheckProfileCompositionManagementFlow(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SearchText.Value = string.Empty;
        state.SelectCategory(ConfigGuiState.ProfileCompositionCurrentCategoryId);

        Check(report, "profile-composition-current-actions-visible", state.IsProfileCompositionCurrentPageView.Value,
            $"visible={state.IsProfileCompositionCurrentPageView.Value}; page={state.SelectedPageTitle.Value}");

        state.CreateProfileCompositionProfile();
        state.SelectCategory(ConfigGuiState.ProfileCompositionCurrentCategoryId);
        var createdProfileId = state.FilteredFields
            .FirstOrDefault(static field => string.Equals(field.Key, "profile", StringComparison.OrdinalIgnoreCase))
            ?.CurrentValueSummary;
        var configAfterCreate = ReadFileOrEmpty(state.ConfigPath);
        var created = !string.IsNullOrWhiteSpace(createdProfileId)
                      && !string.Equals(createdProfileId, "default", StringComparison.OrdinalIgnoreCase)
                      && configAfterCreate.Contains($"profile = \"{createdProfileId}\"", StringComparison.Ordinal)
                      && configAfterCreate.Contains($"[profiles.{createdProfileId}]", StringComparison.Ordinal);
        Check(report, "profile-composition-create-profile", created,
            $"created={createdProfileId}; status={state.StatusText.Value}");

        state.SaveCurrentProfileSelection();
        var configAfterSave = ReadFileOrEmpty(state.ConfigPath);
        var saved = !string.IsNullOrWhiteSpace(createdProfileId)
                    && configAfterSave.Contains($"profile = \"{createdProfileId}\"", StringComparison.Ordinal);
        Check(report, "profile-composition-save-current-profile", saved,
            $"created={createdProfileId}; status={state.StatusText.Value}");

        state.DeleteCurrentProfileCompositionProfile();
        var configAfterDelete = ReadFileOrEmpty(state.ConfigPath);
        var deleted = !string.IsNullOrWhiteSpace(createdProfileId)
                      && !configAfterDelete.Contains($"[profiles.{createdProfileId}]", StringComparison.Ordinal)
                      && configAfterDelete.Contains("profile = \"default\"", StringComparison.Ordinal);
        Check(report, "profile-composition-delete-current-profile", deleted,
            $"created={createdProfileId}; status={state.StatusText.Value}");
    }

    private static void CheckExtensionsImportsDoesNotContainKnownModuleFields(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SearchText.Value = string.Empty;
        state.SelectCategory(ConfigurationCategoryIds.ExtensionsImports);

        var knownModulePrefixes = new[]
        {
            "profiles.",
            "tool_profiles.",
            "session_profiles.",
            "conversation_profiles.",
            "execution_profiles.",
            "permission_profiles.",
            "governance_profiles.",
            "feature_profiles.",
            "realtime_profiles.",
            "tui_profiles.",
            "collaboration_profiles.",
            "workflow_profiles.",
            "identity_profiles.",
            "memory_profiles.",
            "workspace_profiles.",
        };
        var leakedKeys = state.FilteredFields
            .Select(static field => field.Key)
            .Where(key => knownModulePrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        Check(report, "extensions-imports-known-module-fields-hidden", leakedKeys.Length == 0,
            leakedKeys.Length == 0
                ? $"keys={string.Join(", ", state.FilteredFields.Select(static field => field.Key))}"
                : $"visible={string.Join(", ", leakedKeys)}");

        state.SelectCategory(ConfigurationCategoryIds.CapabilitiesTools);
        var toolKeys = state.FilteredFields.Select(static field => field.Key).ToArray();
        var toolMemoryFieldsVisible = toolKeys.Contains("tool_profiles.default.memory.enabled", StringComparer.OrdinalIgnoreCase)
                                      && toolKeys.Contains("tool_profiles.default.memory.default_profile", StringComparer.OrdinalIgnoreCase);
        Check(report, "tool-profile-memory-fields-in-tools", toolMemoryFieldsVisible,
            $"keys={string.Join(", ", toolKeys)}");
    }

    private static void CheckDailyModelSelectionFields(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SearchText.Value = string.Empty;
        state.SelectCategory(ConfigurationCategoryIds.ConnectivityModel);

        var keys = state.FilteredFields.Select(static field => field.Key).ToArray();
        var displayNames = state.FilteredFields.Select(static field => field.DisplayName).ToArray();
        var category = state.Categories.FirstOrDefault(static row => row.Id == ConfigurationCategoryIds.ConnectivityModel);
        var catalogField = state.FilteredFields.FirstOrDefault(static field => string.Equals(field.Key, "model_route_set", StringComparison.OrdinalIgnoreCase));
        var protocolField = state.FilteredFields.FirstOrDefault(static field => string.Equals(field.Key, "model_protocol_rule_set", StringComparison.OrdinalIgnoreCase));
        var expectedKeys = new[] { "model_route_set", "model_protocol_rule_set" };
        var expectedNames = new[] { "当前模型路由方案", "模型协议" };

        var ok = keys.SequenceEqual(expectedKeys, StringComparer.OrdinalIgnoreCase)
                 && displayNames.SequenceEqual(expectedNames, StringComparer.Ordinal)
                 && category?.FieldCount == 2
                 && catalogField?.UsesChoiceEditor == true
                 && protocolField?.UsesChoiceEditor == true
                 && catalogField.ChoiceOptions.Any(static option => string.Equals(option.Value, "default", StringComparison.OrdinalIgnoreCase))
                 && protocolField.ChoiceOptions.Any(static option => string.Equals(option.Value, "default", StringComparison.OrdinalIgnoreCase));
        Check(report, "daily-model-selection-fields", ok,
            $"keys={string.Join(", ", keys)}; names={string.Join(", ", displayNames)}; count={category?.FieldCount}; catalogChoices={string.Join(", ", catalogField?.ChoiceOptions.Select(static option => option.DisplayLabel) ?? [])}; protocolChoices={string.Join(", ", protocolField?.ChoiceOptions.Select(static option => option.DisplayLabel) ?? [])}");
    }

    private static void CheckTokenBudgetFieldsVisible(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SearchText.Value = string.Empty;
        state.SelectCategory(ConfigurationCategoryIds.AgentBehavior);

        var maxOutput = state.FilteredFields.FirstOrDefault(static field => string.Equals(field.Key, "agents.default.max_output_tokens", StringComparison.OrdinalIgnoreCase));
        var contextBudget = state.FilteredFields.FirstOrDefault(static field => string.Equals(field.Key, "context.default_budget_tokens", StringComparison.OrdinalIgnoreCase));
        var ok = maxOutput?.CurrentValueSummary == "2048"
                 && contextBudget?.CurrentValueSummary == "50000"
                 && maxOutput.IsSensitive == false
                 && contextBudget.IsSensitive == false;
        Check(report, "token-budget-fields-not-redacted", ok,
            $"max={maxOutput?.CurrentValueSummary}/{maxOutput?.IsSensitive}; context={contextBudget?.CurrentValueSummary}/{contextBudget?.IsSensitive}");
    }

    private static void CheckDefaultInstanceFieldDisplayNames(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SearchText.Value = string.Empty;
        state.SelectCategory(ConfigurationCategoryIds.AgentBehavior);

        var defaultAgentFields = state.FilteredFields
            .Where(static field => field.Key.StartsWith("agents.default.", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var hasDefaultPrefix = defaultAgentFields.Any(static field => field.DisplayName.StartsWith("default / ", StringComparison.OrdinalIgnoreCase));
        var maxOutputField = defaultAgentFields.FirstOrDefault(static field => string.Equals(field.Key, "agents.default.max_output_tokens", StringComparison.OrdinalIgnoreCase));
        state.SelectField(maxOutputField);
        var ok = defaultAgentFields.Length > 0
                 && !hasDefaultPrefix
                 && maxOutputField?.DisplayName == "输出上限"
                 && state.SelectedDisplayName.Value == "输出上限";
        Check(report, "default-instance-field-display-names", ok,
            $"names={string.Join(", ", defaultAgentFields.Select(static field => field.DisplayName))}; selected={state.SelectedDisplayName.Value}");
    }

    private static void CheckMemoryPageFields(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        state.SearchText.Value = string.Empty;

        CheckMemoryPage(
            state,
            report,
            ConfigGuiState.MemoryProfilesCategoryId,
            "memory-page-profile-fields",
            ["memory.", "memory_profiles."],
            ["memory.enabled", "memory.default_profile", "memory_profiles.*.enabled", "memory_profiles.*.default_space", "memory_profiles.*.overlay", "memory_profiles.*.extract", "memory_profiles.*.retention"]);
        var defaultProfileField = state.FilteredFields.FirstOrDefault(static field => string.Equals(field.Key, "memory.default_profile", StringComparison.OrdinalIgnoreCase));
        Check(report, "memory-profile-selection-field", defaultProfileField?.UsesChoiceEditor == true
                                                        && defaultProfileField.ChoiceOptions.Any(static option => string.Equals(option.Value, "config_gui_smoke", StringComparison.OrdinalIgnoreCase)),
            $"choices={string.Join(", ", defaultProfileField?.ChoiceOptions.Select(static option => option.DisplayLabel) ?? [])}");
        CheckMemoryPage(
            state,
            report,
            ConfigGuiState.MemorySpacesCategoryId,
            "memory-page-space-fields",
            ["memory.spaces."],
            ["memory.spaces.*.scope", "memory.spaces.*.provider", "memory.spaces.*.read_only", "memory.spaces.*.tags"]);
        CheckMemoryPage(
            state,
            report,
            ConfigGuiState.MemoryProvidersCategoryId,
            "memory-page-provider-fields",
            ["memory.providers."],
            ["memory.providers.*.kind", "memory.providers.*.mode", "memory.providers.*.root", "memory.providers.*.enabled"]);
        CheckMemoryPage(
            state,
            report,
            ConfigGuiState.MemoryBindingsCategoryId,
            "memory-page-binding-fields",
            ["memory.bindings."],
            ["memory.bindings.*.space", "memory.bindings.*.provider", "memory.bindings.*.capabilities", "memory.bindings.*.mode"]);
    }

    private static void CheckMemoryDefaultAndCreationFlow(ConfigGuiSmokeReport report, string home)
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var isolatedHome = Path.Combine(home, "memory-empty-home");
        Directory.CreateDirectory(isolatedHome);
        File.WriteAllText(
            Path.Combine(isolatedHome, "tianshu.toml"),
            """
            schema_version = 1
            """);

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", isolatedHome);
            var state = new ConfigGuiState();
            state.Refresh();

            var initiallyEmpty = MemoryProfileInstanceKeys(state).Length == 0
                                 && IsMemoryInstancePageEmpty(state, ConfigGuiState.MemorySpacesCategoryId)
                                 && IsMemoryInstancePageEmpty(state, ConfigGuiState.MemoryProvidersCategoryId)
                                 && IsMemoryInstancePageEmpty(state, ConfigGuiState.MemoryBindingsCategoryId);
            Check(report, "memory-empty-instance-pages", initiallyEmpty,
                $"profiles={string.Join(", ", MemoryProfileInstanceKeys(state))}; spaces={MemoryKeys(state, ConfigGuiState.MemorySpacesCategoryId)}; providers={MemoryKeys(state, ConfigGuiState.MemoryProvidersCategoryId)}; bindings={MemoryKeys(state, ConfigGuiState.MemoryBindingsCategoryId)}");

            state.SelectCategory(ConfigGuiState.MemoryProfilesCategoryId);
            var creationVisible = state.IsMemoryFieldPageView.Value && state.IsMemoryInstanceCreationVisible.Value;
            Check(report, "memory-create-button-visible", creationVisible,
                $"fieldPage={state.IsMemoryFieldPageView.Value}; create={state.IsMemoryInstanceCreationVisible.Value}");

            state.EnsureDefaultMemoryConfiguration();
            var memoryProfile = ReadFileOrEmpty(ModulePath(state.ConfigPath, "memory", "profiles", "default.toml"));
            var memorySpace = ReadFileOrEmpty(ModulePath(state.ConfigPath, "memory", "spaces", "default.toml"));
            var memoryProvider = ReadFileOrEmpty(ModulePath(state.ConfigPath, "memory", "providers", "local.toml"));
            var memoryBinding = ReadFileOrEmpty(ModulePath(state.ConfigPath, "memory", "bindings", "default.toml"));
            var defaultsSaved = memoryProfile.Contains("[memory_profiles.default]", StringComparison.Ordinal)
                                && memorySpace.Contains("[memory.spaces.default]", StringComparison.Ordinal)
                                && memoryProvider.Contains("[memory.providers.local]", StringComparison.Ordinal)
                                && memoryBinding.Contains("[memory.bindings.default]", StringComparison.Ordinal)
                                && MemoryPageContainsKey(state, ConfigGuiState.MemoryProfilesCategoryId, "memory.default_profile")
                                && MemoryPageContainsKey(state, ConfigGuiState.MemoryProfilesCategoryId, "memory_profiles.default.enabled")
                                && MemoryPageContainsKey(state, ConfigGuiState.MemorySpacesCategoryId, "memory.spaces.default.provider")
                                && MemoryPageContainsKey(state, ConfigGuiState.MemoryProvidersCategoryId, "memory.providers.local.mode")
                                && MemoryPageContainsKey(state, ConfigGuiState.MemoryBindingsCategoryId, "memory.bindings.default.mode");
            Check(report, "memory-default-configuration-fill", defaultsSaved,
                $"status={state.StatusText.Value}; config={state.ConfigPath}");
            state.SelectCategory(ConfigGuiState.MemoryProfilesCategoryId);
            var defaultProfileField = state.FilteredFields.FirstOrDefault(static field => string.Equals(field.Key, "memory.default_profile", StringComparison.OrdinalIgnoreCase));
            Check(report, "memory-default-profile-choice-after-fill", defaultProfileField?.UsesChoiceEditor == true
                                                                  && defaultProfileField.ChoiceOptions.Any(static option => string.Equals(option.Value, "default", StringComparison.OrdinalIgnoreCase)),
                $"choices={string.Join(", ", defaultProfileField?.ChoiceOptions.Select(static option => option.DisplayLabel) ?? [])}");

            state.SelectCategory(ConfigGuiState.MemoryProfilesCategoryId);
            state.CreateMemoryConfigurationForCurrentPage();
            var profileCreated = MemoryPageContainsKey(state, ConfigGuiState.MemoryProfilesCategoryId, "memory_profiles.profile.enabled");

            state.SelectCategory(ConfigGuiState.MemorySpacesCategoryId);
            state.CreateMemoryConfigurationForCurrentPage();
            var spaceCreated = MemoryPageContainsKey(state, ConfigGuiState.MemorySpacesCategoryId, "memory.spaces.space.provider");

            state.SelectCategory(ConfigGuiState.MemoryProvidersCategoryId);
            state.CreateMemoryConfigurationForCurrentPage();
            var providerCreated = MemoryPageContainsKey(state, ConfigGuiState.MemoryProvidersCategoryId, "memory.providers.provider.mode");

            state.SelectCategory(ConfigGuiState.MemoryBindingsCategoryId);
            state.CreateMemoryConfigurationForCurrentPage();
            var bindingCreated = MemoryPageContainsKey(state, ConfigGuiState.MemoryBindingsCategoryId, "memory.bindings.binding.mode");

            Check(report, "memory-instance-create-flow", profileCreated && spaceCreated && providerCreated && bindingCreated,
                $"profile={profileCreated}; space={spaceCreated}; provider={providerCreated}; binding={bindingCreated}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
        }
    }

    private static void CheckConfigurationLifecycleActions(ConfigGuiSmokeReport report, string home)
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var isolatedHome = Path.Combine(home, "configuration-lifecycle-home");
        Directory.CreateDirectory(isolatedHome);
        SeedSmokeHomeIfNeeded(isolatedHome);
        File.AppendAllText(
            Path.Combine(isolatedHome, "tianshu.toml"),
            """

            [projects.config_gui_smoke]
            path = "."
            trust_level = "trusted"
            profile = "default"
            """);

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", isolatedHome);
            var state = new ConfigGuiState();
            state.Refresh();

            CheckAgentLifecycle(state, report, "agents.default.max_output_tokens", "agents.agent.display_name", "agents.agent-copy.display_name", "agent");
            CheckAgentLifecycle(state, report, "execution_profiles.default.agent", "execution_profiles.execution.agent", "execution_profiles.execution-copy.agent", "execution");
            CheckAgentLifecycle(state, report, "session_profiles.default.memory_mode", "session_profiles.session.memory_mode", "session_profiles.session-copy.memory_mode", "session");
            CheckAgentLifecycle(state, report, "conversation_profiles.default.history", "conversation_profiles.conversation.history", "conversation_profiles.conversation-copy.history", "conversation");

            state.EnsureDefaultMemoryConfiguration();
            CheckMemoryLifecycle(state, report, ConfigGuiState.MemoryProfilesCategoryId, "memory_profiles.default.enabled", "memory_profiles.default-copy.enabled", "memory_profiles.default-copy-renamed.enabled", "profile");
            CheckMemoryLifecycle(state, report, ConfigGuiState.MemorySpacesCategoryId, "memory.spaces.default.scope", "memory.spaces.default-copy.scope", "memory.spaces.default-copy-renamed.scope", "space");
            CheckMemoryLifecycle(state, report, ConfigGuiState.MemoryProvidersCategoryId, "memory.providers.local.kind", "memory.providers.local-copy.kind", "memory.providers.local-copy-renamed.kind", "provider");
            CheckMemoryLifecycle(state, report, ConfigGuiState.MemoryBindingsCategoryId, "memory.bindings.default.space", "memory.bindings.default-copy.space", "memory.bindings.default-copy-renamed.space", "binding");

            CheckWorkspaceLifecycle(state, report, "workspace_profiles.default.trust_policy", "workspace_profiles.workspace.root_markers", "workspace_profiles.workspace-copy.root_markers", "workspace-profile");
            CheckWorkspaceLifecycle(state, report, "projects.config_gui_smoke.path", "projects.project.path", "projects.project-copy.path", "project");

            CheckSkillPackageLifecycle(state, report, isolatedHome);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
        }
    }

    private static void CheckAgentLifecycle(ConfigGuiState state, ConfigGuiSmokeReport report, string sourceKey, string createdKey, string copyKey, string name)
    {
        var source = SelectFieldByKey(state, ConfigurationCategoryIds.AgentBehavior, sourceKey);
        if (source is null)
        {
            Check(report, $"agent-lifecycle-{name}-source", false, $"未找到源字段：{sourceKey}");
            return;
        }

        state.CreateAgentConfigurationForCurrentSelection();
        var created = SelectFieldByKey(state, ConfigurationCategoryIds.AgentBehavior, createdKey) is not null;
        state.CopyAgentConfigurationForCurrentSelection();
        var copied = SelectFieldByKey(state, ConfigurationCategoryIds.AgentBehavior, copyKey) is not null;
        state.DeleteAgentConfigurationForCurrentSelection();
        var deleted = SelectFieldByKey(state, ConfigurationCategoryIds.AgentBehavior, copyKey) is null;
        Check(report, $"agent-lifecycle-{name}", created && copied && deleted,
            $"created={created}; copied={copied}; deleted={deleted}; status={state.StatusText.Value}");
    }

    private static void CheckMemoryLifecycle(ConfigGuiState state, ConfigGuiSmokeReport report, string categoryId, string sourceKey, string copyKey, string renamedKey, string name)
    {
        var source = SelectFieldByKey(state, categoryId, sourceKey);
        if (source is null)
        {
            Check(report, $"memory-lifecycle-{name}-source", false, $"未找到源字段：{sourceKey}");
            return;
        }

        state.CopyMemoryConfigurationForCurrentPage();
        var copied = SelectFieldByKey(state, categoryId, copyKey) is not null;
        state.RenameMemoryConfigurationForCurrentPage();
        var renamed = SelectFieldByKey(state, categoryId, renamedKey) is not null
                      && SelectFieldByKey(state, categoryId, copyKey) is null;
        SelectFieldByKey(state, categoryId, renamedKey);
        state.DeleteMemoryConfigurationForCurrentPage();
        var deleted = SelectFieldByKey(state, categoryId, renamedKey) is null;
        Check(report, $"memory-lifecycle-{name}", copied && renamed && deleted,
            $"copied={copied}; renamed={renamed}; deleted={deleted}; status={state.StatusText.Value}");
    }

    private static void CheckWorkspaceLifecycle(ConfigGuiState state, ConfigGuiSmokeReport report, string sourceKey, string createdKey, string copyKey, string name)
    {
        const string workspaceCategoryId = "__collection.workspace";
        var source = SelectFieldByKey(state, workspaceCategoryId, sourceKey);
        if (source is null)
        {
            Check(report, $"workspace-lifecycle-{name}-source", false, $"未找到源字段：{sourceKey}");
            return;
        }

        state.CreateWorkspaceConfigurationForCurrentSelection();
        var created = SelectFieldByKey(state, workspaceCategoryId, createdKey) is not null;
        state.CopyWorkspaceConfigurationForCurrentSelection();
        var copied = SelectFieldByKey(state, workspaceCategoryId, copyKey) is not null;
        state.DeleteWorkspaceConfigurationForCurrentSelection();
        var deleted = SelectFieldByKey(state, workspaceCategoryId, copyKey) is null;
        Check(report, $"workspace-lifecycle-{name}", created && copied && deleted,
            $"created={created}; copied={copied}; deleted={deleted}; status={state.StatusText.Value}");
    }

    private static void CheckSkillPackageLifecycle(ConfigGuiState state, ConfigGuiSmokeReport report, string home)
    {
        var skillPath = Path.Combine(home, "modules", "skills", "smoke-created", "SKILL.md");
        state.SkillPackageNewId.Value = "smoke-created";
        state.CreateSkillPackage();
        var created = File.Exists(skillPath)
                      && ReadFileOrEmpty(skillPath).Contains("name: smoke-created", StringComparison.Ordinal);
        state.DeleteSelectedSkillPackage();
        var deleted = !Directory.Exists(Path.Combine(home, "modules", "skills", "smoke-created"));
        Check(report, "skill-package-lifecycle-create-delete", created && deleted,
            $"created={created}; deleted={deleted}; status={state.SkillPackageStatusText.Value}");
    }

    private static ConfigFieldRow? SelectFieldByKey(ConfigGuiState state, string categoryId, string key)
    {
        state.SearchText.Value = string.Empty;
        state.SelectCategory(categoryId);
        var fields = string.Equals(categoryId, "__collection.workspace", StringComparison.OrdinalIgnoreCase)
            ? state.WorkspaceFields
            : state.FilteredFields;
        var field = fields.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase));
        if (field is not null)
        {
            state.SelectField(field);
        }

        return field;
    }

    private static bool IsMemoryInstancePageEmpty(ConfigGuiState state, string categoryId)
    {
        state.SelectCategory(categoryId);
        return state.FilteredFields.Count == 0;
    }

    private static bool MemoryPageContainsKey(ConfigGuiState state, string categoryId, string key)
    {
        state.SelectCategory(categoryId);
        return state.FilteredFields.Any(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static string MemoryKeys(ConfigGuiState state, string categoryId)
    {
        state.SelectCategory(categoryId);
        return string.Join(", ", state.FilteredFields.Select(static field => field.Key));
    }

    private static string[] MemoryProfileInstanceKeys(ConfigGuiState state)
    {
        state.SelectCategory(ConfigGuiState.MemoryProfilesCategoryId);
        return state.FilteredFields
            .Where(static field => field.Key.StartsWith("memory_profiles.", StringComparison.OrdinalIgnoreCase))
            .Select(static field => field.Key)
            .ToArray();
    }

    private static void CheckMemoryPage(
        ConfigGuiState state,
        ConfigGuiSmokeReport report,
        string categoryId,
        string checkName,
        IReadOnlyList<string> allowedPrefixes,
        IReadOnlyList<string> expectedKeys)
    {
        state.SelectCategory(categoryId);
        var keys = state.FilteredFields.Select(static field => field.Key).ToArray();
        var category = state.Categories.FirstOrDefault(row => string.Equals(row.Id, categoryId, StringComparison.OrdinalIgnoreCase));
        var hasEnabledField = state.FilteredFields.Any(static field => IsEnabledFieldKey(field.Key));
        var enabledFieldFirst = !hasEnabledField || state.FilteredFields.FirstOrDefault() is { } firstField && IsEnabledFieldKey(firstField.Key);
        var ok = expectedKeys.All(expectedKey => keys.Any(key => MatchesExpectedKey(key, expectedKey)))
                 && keys.All(key => allowedPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                 && category?.FieldCount == keys.Length
                 && enabledFieldFirst;
        Check(report, checkName, ok, $"keys={string.Join(", ", keys)}; count={category?.FieldCount}; enabledFirst={enabledFieldFirst}");
    }

    private static bool IsEnabledFieldKey(string key)
        => string.Equals(key, "memory.enabled", StringComparison.OrdinalIgnoreCase)
           || key.EndsWith(".enabled", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesExpectedKey(string key, string expectedKey)
    {
        var wildcardIndex = expectedKey.IndexOf('*', StringComparison.Ordinal);
        if (wildcardIndex < 0)
        {
            return string.Equals(key, expectedKey, StringComparison.OrdinalIgnoreCase);
        }

        var prefix = expectedKey[..wildcardIndex];
        var suffix = expectedKey[(wildcardIndex + 1)..];
        return key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static void SaveProvider(ConfigGuiState state, ConfigGuiSmokeReport report, string baseUrl)
    {
        state.ProviderId.Value = SmokeProviderId;
        state.ProviderBaseUrl.Value = baseUrl;
        state.ProviderApiKeyEnv.Value = "TIANSHU_CONFIG_GUI_SMOKE_API_KEY";
        var protocolIndex = state.ProviderProtocols
            .Select((protocol, index) => new { protocol, index })
            .FirstOrDefault(item => string.Equals(item.protocol, "openai_chat_completions", StringComparison.OrdinalIgnoreCase))
            ?.index ?? 0;
        state.ProviderProtocolIndex.Value = protocolIndex;
        state.SaveProvider();

        var providerInstancePath = ModulePath(state.ConfigPath, "model", "provider-instances", "default.toml");
        var config = ReadFileOrEmpty(state.ConfigPath);
        var providerInstances = ReadFileOrEmpty(providerInstancePath);
        var saved = config.Contains("provider_instances = \"default\"", StringComparison.Ordinal)
            && providerInstances.Contains("[providers.config_gui_smoke]", StringComparison.Ordinal)
            && providerInstances.Contains($"base_url = \"{baseUrl}\"", StringComparison.Ordinal)
            && providerInstances.Contains("api_key_env = \"TIANSHU_CONFIG_GUI_SMOKE_API_KEY\"", StringComparison.Ordinal)
            && providerInstances.Contains("default_protocol = \"openai_chat_completions\"", StringComparison.Ordinal);
        Check(report, "provider-save", saved, state.ProviderStatusText.Value);
    }

    private static void SaveModelProtocolMappings(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        var categoryId = state.Categories.FirstOrDefault(static category => category.DisplayName == "模型协议适配")?.Id;
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            Check(report, "model-protocol-category", false, "未找到模型协议适配分类。");
            return;
        }

        state.SelectCategory(categoryId);
        Check(report, "model-protocol-context-collection", state.IsDetailViewVisible.Value && !state.IsFieldDetailEditorVisible.Value,
            $"title={state.ContextTitle.Value}; target={state.ContextSaveTarget.Value}");
        Check(report, "model-protocol-context-target", ContainsPathSegments(state.ContextSaveTarget.Value, "modules", "model", "provider-instances"),
            $"target={state.ContextSaveTarget.Value}");

        var providerIndex = state.ModelProtocolProviderLabels
            .Select((label, index) => new { label, index })
            .FirstOrDefault(item => string.Equals(item.label, SmokeProviderId, StringComparison.OrdinalIgnoreCase))
            ?.index ?? 0;
        state.SelectModelProtocolProviderIndex(providerIndex);
        Check(report, "model-protocol-detected-list-initial-no-placeholder",
            state.ModelProtocolModelListItems.Count == 0
            && !state.ModelProtocolModelListItems.Any(static item => item.Contains("尚未检测模型", StringComparison.OrdinalIgnoreCase)),
            $"list={string.Join(", ", state.ModelProtocolModelListItems)}");
        state.TestModelProtocolProviderModels();
        var detectedModels = state.ModelProtocolModelLabels.ToArray();
        Check(report, "model-protocol-detect-models", detectedModels.Contains("openai-compatible-default", StringComparer.OrdinalIgnoreCase)
                                                          && detectedModels.Contains("qwen-plus", StringComparer.OrdinalIgnoreCase),
            $"models={string.Join(", ", detectedModels)}; status={state.ModelProtocolStatusText.Value}");
        var detectedListItems = state.ModelProtocolModelListItems.ToArray();
        var listItemsClean = detectedListItems.SequenceEqual(detectedModels, StringComparer.Ordinal)
            && !detectedListItems.Any(static item => item.Contains("尚未检测模型", StringComparison.OrdinalIgnoreCase));
        Check(report, "model-protocol-detected-list-no-placeholder", listItemsClean,
            $"labels={string.Join(", ", detectedModels)}; list={string.Join(", ", detectedListItems)}");
        state.BeginNewModelProtocolOverride();
        state.DeleteSelectedModelProtocolOverride();
        Check(report, "model-protocol-delete-override-clears-list", state.ModelProtocolOverrideLabels.Count == 0,
            $"overrides={string.Join(", ", state.ModelProtocolOverrideLabels)}; status={state.ModelProtocolStatusText.Value}");
        state.BeginNewModelProtocolOverride();
        SelectModelRouteSetOption(state.ModelProtocolModelLabels, static (s, index) => s.SelectModelProtocolDetectedModelIndex(index), state, "openai-compatible-default");
        state.ModelProtocolOverrideProtocols.Value = "anthropic_messages, openai_chat_completions";
        state.BeginNewModelProtocolRule();
        state.ModelProtocolRuleMatch.Value = "qwen*";
        state.ModelProtocolRuleProtocols.Value = "anthropic_messages, openai_chat_completions";
        state.SaveModelProtocolMappings();

        var providerInstancePath = ModulePath(state.ConfigPath, "model", "provider-instances", "default.toml");
        var providerInstances = ReadFileOrEmpty(providerInstancePath);
        var saved = providerInstances.Contains("model_overrides = [", StringComparison.Ordinal)
            && providerInstances.Contains("protocol_rules = [", StringComparison.Ordinal)
            && providerInstances.Contains("name = \"openai-compatible-default\"", StringComparison.Ordinal)
            && providerInstances.Contains("match = \"qwen*\"", StringComparison.Ordinal)
            && CountOccurrences(providerInstances, "protocols = [\"anthropic_messages\", \"openai_chat_completions\"]") >= 2;
        Check(report, "model-protocol-save", saved, state.ModelProtocolStatusText.Value);

        state.Refresh();
        var projectionVisible = state.ModelProtocolProviderLabels.Contains(SmokeProviderId, StringComparer.OrdinalIgnoreCase)
            && state.ModelProtocolOverrideLabels.Any(static label => label.Contains("openai-compatible-default", StringComparison.OrdinalIgnoreCase))
            && state.ModelProtocolRuleLabels.Any(static label => label.Contains("qwen*", StringComparison.OrdinalIgnoreCase));
        Check(report, "model-protocol-projection-refresh", projectionVisible,
            $"providers={string.Join(", ", state.ModelProtocolProviderLabels)}; overrides={string.Join(", ", state.ModelProtocolOverrideLabels)}; rules={string.Join(", ", state.ModelProtocolRuleLabels)}");

        var coldState = new ConfigGuiState();
        coldState.Refresh();
        var coldProviderIndex = coldState.ModelProtocolProviderLabels
            .Select((label, index) => new { label, index })
            .FirstOrDefault(item => string.Equals(item.label, SmokeProviderId, StringComparison.OrdinalIgnoreCase))
            ?.index ?? 0;
        coldState.SelectModelProtocolProviderIndex(coldProviderIndex, syncCurrentEditor: false);
        var coldStartPreserved = coldState.ModelProtocolOverrideLabels.Any(static label => label.Contains("openai-compatible-default", StringComparison.OrdinalIgnoreCase))
            && coldState.ModelProtocolRuleLabels.Any(static label => label.Contains("qwen*", StringComparison.OrdinalIgnoreCase))
            && !coldState.ModelProtocolOverrideLabels.Any(static label => label.Contains("未填写模型名 ->", StringComparison.OrdinalIgnoreCase))
            && !coldState.ModelProtocolRuleLabels.Any(static label => label.EndsWith(".  ->", StringComparison.OrdinalIgnoreCase));
        Check(report, "model-protocol-cold-start-preserves-existing-rules", coldStartPreserved,
            $"providers={string.Join(", ", coldState.ModelProtocolProviderLabels)}; overrides={string.Join(", ", coldState.ModelProtocolOverrideLabels)}; rules={string.Join(", ", coldState.ModelProtocolRuleLabels)}");
        report.ModelProtocolProviders.Clear();
        report.ModelProtocolProviders.AddRange(state.ModelProtocolProviderLabels);
    }

    private static void SaveDefaultModelProtocolRules(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        var categoryId = state.Categories.FirstOrDefault(static category => category.DisplayName == "默认协议规则")?.Id;
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            Check(report, "default-model-protocol-rules-category", false, "未找到默认协议规则分类。");
            return;
        }

        state.SelectCategory(categoryId);
        Check(report, "default-model-protocol-rules-context-collection", state.IsDetailViewVisible.Value && !state.IsFieldDetailEditorVisible.Value,
            $"title={state.ContextTitle.Value}; target={state.ContextSaveTarget.Value}");
        Check(report, "default-model-protocol-rules-context-target", ContainsPathSegments(state.ContextSaveTarget.Value, "modules", "model", "protocol-rules"),
            $"target={state.ContextSaveTarget.Value}");

        state.RestoreDefaultModelProtocolRules();
        var restored = state.DefaultModelProtocolRuleLabels.Any(static label => label.Contains("qwen*", StringComparison.OrdinalIgnoreCase))
            && state.DefaultModelProtocolRuleLabels.Any(static label => label.Contains("deepseek*", StringComparison.OrdinalIgnoreCase));
        Check(report, "default-model-protocol-rules-restore", restored,
            $"rules={string.Join(", ", state.DefaultModelProtocolRuleLabels)}; status={state.DefaultModelProtocolRuleStatusText.Value}");
        var deepSeekDefaultProtocolPriority = state.DefaultModelProtocolRuleLabels.Any(static label =>
            label.Contains("deepseek*", StringComparison.OrdinalIgnoreCase)
            && label.Contains("anthropic_messages", StringComparison.OrdinalIgnoreCase)
            && label.IndexOf("anthropic_messages", StringComparison.OrdinalIgnoreCase) < label.IndexOf("openai_chat_completions", StringComparison.OrdinalIgnoreCase));
        Check(report, "default-model-protocol-rules-deepseek-prefers-anthropic", deepSeekDefaultProtocolPriority,
            $"rules={string.Join(", ", state.DefaultModelProtocolRuleLabels)}");

        var labelsBeforeSelection = string.Join("\n", state.DefaultModelProtocolRuleLabels);
        state.DefaultModelProtocolRuleIndex.Value = 1;
        state.SelectDefaultModelProtocolRuleIndex(1);
        var labelsAfterSelection = string.Join("\n", state.DefaultModelProtocolRuleLabels);
        Check(report, "default-model-protocol-rules-selection-preserves-labels", string.Equals(labelsBeforeSelection, labelsAfterSelection, StringComparison.Ordinal),
            $"before={labelsBeforeSelection}; after={labelsAfterSelection}");
        var listSelection = string.Equals(state.DefaultModelProtocolRuleMatch.Value, "claude*", StringComparison.OrdinalIgnoreCase)
            && state.DefaultModelProtocolRuleProtocols.Value.Contains("anthropic_messages", StringComparison.OrdinalIgnoreCase);
        Check(report, "default-model-protocol-rules-list-selection", listSelection,
            $"index={state.DefaultModelProtocolRuleIndex.Value}; match={state.DefaultModelProtocolRuleMatch.Value}; protocols={state.DefaultModelProtocolRuleProtocols.Value}");

        state.BeginNewDefaultModelProtocolRule();
        state.DefaultModelProtocolRuleMatch.Value = "config-gui-smoke-*";
        state.DefaultModelProtocolRuleProtocols.Value = "openai_chat_completions, anthropic_messages";
        state.SaveDefaultModelProtocolRuleSet();

        var config = ReadFileOrEmpty(state.ConfigPath);
        var ruleSetPath = ModulePath(state.ConfigPath, "model", "protocol-rules", "default.toml");
        var ruleSetConfig = ReadFileOrEmpty(ruleSetPath);
        var saved = config.Contains("model_protocol_rule_set = \"default\"", StringComparison.Ordinal)
            && !config.Contains("model_protocol_rule_sets", StringComparison.Ordinal)
            && ruleSetConfig.Contains("[model_protocol_rule_sets.default]", StringComparison.Ordinal)
            && ruleSetConfig.Contains("match = \"config-gui-smoke-*\"", StringComparison.Ordinal)
            && ruleSetConfig.Contains("protocols = [\"openai_chat_completions\", \"anthropic_messages\"]", StringComparison.Ordinal);
        Check(report, "default-model-protocol-rules-save", saved, state.DefaultModelProtocolRuleStatusText.Value);

        state.Refresh();
        var projectionVisible = state.DefaultModelProtocolRuleSetLabels.Any(static label => label.Contains("default", StringComparison.OrdinalIgnoreCase))
            && state.DefaultModelProtocolRuleLabels.Any(static label => label.Contains("config-gui-smoke-*", StringComparison.OrdinalIgnoreCase));
        Check(report, "default-model-protocol-rules-projection-refresh", projectionVisible,
            $"ruleSets={string.Join(", ", state.DefaultModelProtocolRuleSetLabels)}; rules={string.Join(", ", state.DefaultModelProtocolRuleLabels)}");
    }

    private static void SaveModelRouteSet(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        var categoryId = state.Categories.FirstOrDefault(static category => category.DisplayName == "模型路由方案")?.Id;
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            Check(report, "model-route-set-category", false, "未找到模型路由方案分类。");
            return;
        }

        state.SelectCategory(categoryId);
        Check(report, "model-route-set-context-collection", state.IsDetailViewVisible.Value && !state.IsFieldDetailEditorVisible.Value,
            $"title={state.ContextTitle.Value}; target={state.ContextSaveTarget.Value}");
        Check(report, "model-route-set-context-target", ContainsPathSegments(state.ContextSaveTarget.Value, "modules", "model", "route-sets"),
            $"target={state.ContextSaveTarget.Value}");

        state.BeginNewModelRouteSet();
        state.ModelRouteSetId.Value = "config_gui_smoke_route_set";
        state.ModelRouteSetDisplayName.Value = "ConfigGUI Smoke Route Set";
        state.ModelRouteSetDescription.Value = "ConfigGUI smoke model route set";
        SelectModelRouteSetRoute(state, "coding");
        state.ModelRouteSetRouteFallback.Value = "review";
        SelectModelRouteSetOption(state.ModelRouteSetProviderOptions, static (s, index) => s.SelectModelRouteSetCandidateProviderIndex(index), state, SmokeProviderId);
        SelectModelRouteSetOption(state.ModelRouteSetModelOptions, static (s, index) => s.SelectModelRouteSetCandidateModelIndex(index), state, "config-gui-smoke-model-updated");
        SelectModelRouteSetOption(state.ModelRouteSetProtocolOptions, static (s, index) => s.SelectModelRouteSetCandidateProtocolIndex(index), state, "openai_chat_completions");
        state.ModelRouteSetCandidateCapabilities.Value = "code";

        state.BeginNewModelRouteSetCandidate();
        SelectModelRouteSetOption(state.ModelRouteSetProviderOptions, static (s, index) => s.SelectModelRouteSetCandidateProviderIndex(index), state, SmokeProviderId);
        SelectModelRouteSetOption(state.ModelRouteSetModelOptions, static (s, index) => s.SelectModelRouteSetCandidateModelIndex(index), state, "config-gui-smoke-fast");
        SelectModelRouteSetOption(state.ModelRouteSetProtocolOptions, static (s, index) => s.SelectModelRouteSetCandidateProtocolIndex(index), state, "openai_chat_completions");
        state.ModelRouteSetCandidateCapabilities.Value = "fast";
        state.MoveSelectedModelRouteSetCandidate(-1);

        SelectModelRouteSetRoute(state, "review");
        state.ModelRouteSetRouteFallback.Value = string.Empty;
        SelectModelRouteSetOption(state.ModelRouteSetProviderOptions, static (s, index) => s.SelectModelRouteSetCandidateProviderIndex(index), state, SmokeProviderId);
        SelectModelRouteSetOption(state.ModelRouteSetModelOptions, static (s, index) => s.SelectModelRouteSetCandidateModelIndex(index), state, "config-gui-smoke-fast");
        SelectModelRouteSetOption(state.ModelRouteSetProtocolOptions, static (s, index) => s.SelectModelRouteSetCandidateProtocolIndex(index), state, "openai_chat_completions");
        state.ModelRouteSetCandidateCapabilities.Value = "review";
        state.SaveModelRouteSet();

        var config = ReadFileOrEmpty(state.ConfigPath);
        var routeSetPath = ModulePath(state.ConfigPath, "model", "route-sets", "config_gui_smoke_route_set.toml");
        var routeSetSection = ReadFileOrEmpty(routeSetPath);
        var codingIndex = routeSetSection.IndexOf("kind = \"coding\"", StringComparison.Ordinal);
        var fastIndex = routeSetSection.IndexOf("model = \"config-gui-smoke-fast\"", StringComparison.Ordinal);
        var slowIndex = routeSetSection.IndexOf("model = \"config-gui-smoke-model-updated\"", StringComparison.Ordinal);
        var saved = config.Contains("model_route_set = \"config_gui_smoke_route_set\"", StringComparison.Ordinal)
            && !config.Contains("[model_route_sets.config_gui_smoke_route_set]", StringComparison.Ordinal)
            && routeSetSection.Contains("[model_route_sets.config_gui_smoke_route_set]", StringComparison.Ordinal)
            && codingIndex >= 0
            && routeSetSection.Contains("kind = \"review\"", StringComparison.Ordinal)
            && fastIndex >= 0
            && slowIndex > fastIndex
            && CountOccurrences(routeSetSection, "model = \"config-gui-smoke-fast\"") >= 2;
        Check(report, "model-route-set-save", saved, state.ModelRouteSetStatusText.Value);
        Check(report, "model-route-set-candidate-order", fastIndex >= 0 && slowIndex > fastIndex,
            $"fastIndex={fastIndex}; slowIndex={slowIndex}");
        Check(report, "model-route-set-route-overlap", CountOccurrences(routeSetSection, "model = \"config-gui-smoke-fast\"") >= 2,
            "同一模型应可同时作为 coding 首选和 review 首选。");

        state.Refresh();
        var projectionVisible = state.ModelRouteSetLabels.Any(static label => label.Contains("config_gui_smoke_route_set", StringComparison.OrdinalIgnoreCase))
            && state.ModelRouteSetModelOptions.Contains("config-gui-smoke-fast", StringComparer.OrdinalIgnoreCase);
        Check(report, "model-route-set-projection-refresh", projectionVisible,
            $"routeSets={string.Join(", ", state.ModelRouteSetLabels)}; models={string.Join(", ", state.ModelRouteSetModelOptions)}");
        var secretHidden = !state.ModelRouteSetStatusText.Value.Contains("TIANSHU_CONFIG_GUI_SMOKE_API_KEY", StringComparison.Ordinal)
            && !state.ContextDiagnostics.Value.Contains("TIANSHU_CONFIG_GUI_SMOKE_API_KEY", StringComparison.Ordinal)
            && !string.Join("|", state.ModelRouteSetLabels).Contains("TIANSHU_CONFIG_GUI_SMOKE_API_KEY", StringComparison.Ordinal);
        Check(report, "model-route-set-secret-hidden", secretHidden, "模型路由方案页不得显示 provider secret env。");
        report.ModelRouteSets.Clear();
        report.ModelRouteSets.AddRange(state.ModelRouteSetLabels);
    }

    private static void SelectModelRouteSetOption(
        IReadOnlyList<string> options,
        Action<ConfigGuiState, int> select,
        ConfigGuiState state,
        string value)
    {
        var index = options.ToList().FindIndex(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase));
        select(state, index < 0 ? 0 : index);
    }

    private static void SelectModelRouteSetRoute(ConfigGuiState state, string kind)
    {
        var index = state.ModelRouteSetRouteLabels
            .Select((label, itemIndex) => new { label, itemIndex })
            .FirstOrDefault(item => labelRouteKindEquals(item.label, kind))
            ?.itemIndex;
        state.SelectModelRouteSetRouteIndex(index ?? 0);

        static bool labelRouteKindEquals(string label, string kind)
        {
            var trimmed = label.Trim();
            var dotIndex = trimmed.IndexOf('.');
            if (dotIndex >= 0)
            {
                trimmed = trimmed[(dotIndex + 1)..].TrimStart();
            }

            var parenIndex = trimmed.IndexOf(" (", StringComparison.Ordinal);
            var routeKind = parenIndex > 0 ? trimmed[..parenIndex].Trim() : trimmed;
            return string.Equals(routeKind, kind, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void SavePrompt(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        if (state.PromptSectionLabels.Count == 0)
        {
            Check(report, "prompt-section", false, "未发现可编辑 prompt 段。");
            return;
        }

        var promptPackIndex = state.PromptFileLabels
            .Select((label, index) => new { label, index })
            .FirstOrDefault(item => item.label.Contains("prompts", StringComparison.OrdinalIgnoreCase))
            ?.index;
        if (promptPackIndex is not null)
        {
            state.SelectPromptFileIndex(promptPackIndex.Value);
        }

        state.SelectPromptSectionIndex(0);
        state.PromptText.Value = $"{SmokeMarker} {DateTimeOffset.UtcNow:O}";
        state.PromptEnabledIndex.Value = 0;
        state.PromptModeIndex.Value = 1;
        state.SavePromptSection();

        var targetPath = state.PromptSaveTargetText.Value;
        var saved = File.Exists(targetPath)
            && ReadFileOrEmpty(targetPath).Contains(SmokeMarker, StringComparison.Ordinal);
        Check(report, "prompt-save", saved, state.PromptStatusText.Value);
        Check(report, "prompt-pack-scan", targetPath.Contains($"{Path.DirectorySeparatorChar}prompts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase),
            $"target={targetPath}");
        report.PromptSaveTarget = targetPath;
    }

    private static void SaveToolManifest(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        if (state.ToolPackageLabels.Count == 0)
        {
            Check(report, "tool-manifest-package", false, "未发现可编辑工具包 manifest。");
            return;
        }

        state.SelectToolPackageIndex(0);
        state.ToolProviderId.Value = "config_gui_smoke";
        state.ToolProviderEnabledIndex.Value = 0;
        state.ToolProviderTypeIndex.Value = 0;
        state.ToolProviderAssemblyPath.Value = "./smoke/TianShu.Tools.Smoke.dll";
        state.ToolProviderTypeName.Value = "TianShu.Tools.Smoke.SmokeToolProvider";
        state.ToolProviderPriority.Value = "99";
        state.ToolProviderReplaceExistingIndex.Value = 0;
        state.SaveToolPackage();

        var manifestPath = state.ToolManifestPathText.Value.Replace("Manifest：", string.Empty, StringComparison.Ordinal).Trim();
        var saved = File.Exists(manifestPath)
            && ReadFileOrEmpty(manifestPath).Contains("id = \"config_gui_smoke\"", StringComparison.Ordinal)
            && ReadFileOrEmpty(manifestPath).Contains("provider_type = \"TianShu.Tools.Smoke.SmokeToolProvider\"", StringComparison.Ordinal);
        Check(report, "tool-manifest-save", saved, state.ToolStatusText.Value);
        report.ToolManifestPath = manifestPath;
    }

    private static void SaveProviderManifest(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        if (state.ModelProviderPackageLabels.Count == 0)
        {
            Check(report, "provider-manifest-package", false, "未发现可编辑协议适配器包 manifest。");
            return;
        }

        state.SelectModelProviderPackageIndex(0);
        state.ModelProviderAdapterId.Value = "config_gui_smoke";
        state.ModelProviderAdapterDisplayName.Value = "ConfigGUI Smoke Provider";
        state.ModelProviderAdapterEnabledIndex.Value = 0;
        state.ModelProviderAdapterTypeIndex.Value = 0;
        state.ModelProviderAdapterAssemblyPath.Value = "./smoke/TianShu.Provider.Smoke.dll";
        state.ModelProviderAdapterPriority.Value = "99";
        state.SaveModelProviderPackage();

        var manifestPath = state.ModelProviderPackageManifestPathText.Value.Replace("Manifest：", string.Empty, StringComparison.Ordinal).Trim();
        var saved = File.Exists(manifestPath)
            && ReadFileOrEmpty(manifestPath).Contains("id = \"config_gui_smoke\"", StringComparison.Ordinal)
            && ReadFileOrEmpty(manifestPath).Contains("assembly_path = \"./smoke/TianShu.Provider.Smoke.dll\"", StringComparison.Ordinal);
        Check(report, "provider-manifest-save", saved, state.ModelProviderPackageStatusText.Value);
        report.ProviderManifestPath = manifestPath;
    }

    private static void SaveMcpServerManifest(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        if (state.McpServerPackageLabels.Count == 0)
        {
            Check(report, "mcp-server-manifest-package", false, "未发现可编辑 MCP Server 包 manifest。");
            return;
        }

        state.SelectMcpServerPackageIndex(0);
        state.McpServerId.Value = "config_gui_smoke";
        state.McpServerDisplayName.Value = "ConfigGUI Smoke MCP";
        state.McpServerEnabledIndex.Value = 0;
        state.McpServerRequiredIndex.Value = 1;
        state.McpServerTransportIndex.Value = 1;
        state.McpServerUrl.Value = "https://smoke.example.com/mcp";
        state.McpServerBearerTokenEnvVar.Value = "SMOKE_MCP_TOKEN";
        state.McpServerStartupTimeoutMs.Value = "5000";
        state.McpServerToolTimeoutMs.Value = "60000";
        state.McpServerEnabledTools.Value = "search";
        state.SaveMcpServerPackage();

        var manifestPath = state.McpServerPackageManifestPathText.Value.Replace("Manifest：", string.Empty, StringComparison.Ordinal).Trim();
        var saved = File.Exists(manifestPath)
            && ReadFileOrEmpty(manifestPath).Contains("id = \"config_gui_smoke\"", StringComparison.Ordinal)
            && ReadFileOrEmpty(manifestPath).Contains("url = \"https://smoke.example.com/mcp\"", StringComparison.Ordinal);
        Check(report, "mcp-server-manifest-save", saved, state.McpServerPackageStatusText.Value);
        report.McpServerManifestPath = manifestPath;
    }

    private static void SaveArtifactStoreManifest(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        if (state.ArtifactStorePackageLabels.Count == 0)
        {
            Check(report, "artifact-store-manifest-package", false, "未发现可编辑工件存储包 manifest。");
            return;
        }

        state.SelectArtifactStorePackageIndex(0);
        state.ArtifactStoreId.Value = "config_gui_smoke";
        state.ArtifactStoreDisplayName.Value = "ConfigGUI Smoke Store";
        state.ArtifactStoreEnabledIndex.Value = 0;
        state.ArtifactStoreTypeIndex.Value = 0;
        state.ArtifactStoreRoot.Value = "./smoke-data";
        state.ArtifactStoreMaxHistoryVersions.Value = "7";
        state.ArtifactStoreCrossProcessSyncIndex.Value = 0;
        state.SaveArtifactStorePackage();

        var manifestPath = state.ArtifactStorePackageManifestPathText.Value.Replace("Manifest：", string.Empty, StringComparison.Ordinal).Trim();
        var saved = File.Exists(manifestPath)
            && ReadFileOrEmpty(manifestPath).Contains("id = \"config_gui_smoke\"", StringComparison.Ordinal)
            && ReadFileOrEmpty(manifestPath).Contains("root = \"./smoke-data\"", StringComparison.Ordinal);
        Check(report, "artifact-store-manifest-save", saved, state.ArtifactStorePackageStatusText.Value);
        report.ArtifactStoreManifestPath = manifestPath;
    }

    private static void SaveDiagnosticSinkManifest(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        if (state.DiagnosticSinkPackageLabels.Count == 0)
        {
            Check(report, "diagnostic-sink-manifest-package", false, "未发现可编辑诊断输出包 manifest。");
            return;
        }

        state.SelectDiagnosticSinkPackageIndex(0);
        state.DiagnosticSinkId.Value = "config_gui_smoke";
        state.DiagnosticSinkDisplayName.Value = "ConfigGUI Smoke Sink";
        state.DiagnosticSinkEnabledIndex.Value = 0;
        state.DiagnosticSinkTypeIndex.Value = 1;
        state.DiagnosticSinkLevelIndex.Value = 3;
        state.DiagnosticSinkTarget.Value = "./smoke-diagnostics";
        state.DiagnosticSinkModules.Value = "provider, runtime";
        state.DiagnosticSinkMaxBytes.Value = "4096";
        state.SaveDiagnosticSinkPackage();

        var manifestPath = state.DiagnosticSinkPackageManifestPathText.Value.Replace("Manifest：", string.Empty, StringComparison.Ordinal).Trim();
        var saved = File.Exists(manifestPath)
            && ReadFileOrEmpty(manifestPath).Contains("id = \"config_gui_smoke\"", StringComparison.Ordinal)
            && ReadFileOrEmpty(manifestPath).Contains("target = \"./smoke-diagnostics\"", StringComparison.Ordinal)
            && ReadFileOrEmpty(manifestPath).Contains("max_bytes = 4096", StringComparison.Ordinal);
        Check(report, "diagnostic-sink-manifest-save", saved, state.DiagnosticSinkPackageStatusText.Value);
        report.DiagnosticSinkManifestPath = manifestPath;
    }

    private static void SaveWorkspaceResolverManifest(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        if (state.WorkspaceResolverPackageLabels.Count == 0)
        {
            Check(report, "workspace-resolver-manifest-package", false, "未发现可编辑工作空间解析包 manifest。");
            return;
        }

        state.SelectWorkspaceResolverPackageIndex(0);
        state.WorkspaceResolverId.Value = "config_gui_smoke";
        state.WorkspaceResolverDisplayName.Value = "ConfigGUI Smoke Workspace Resolver";
        state.WorkspaceResolverEnabledIndex.Value = 0;
        state.WorkspaceResolverTypeIndex.Value = 0;
        state.WorkspaceResolverRootMarkers.Value = ".git, .tianshu, .config-gui-smoke-root";
        state.WorkspaceResolverProfile.Value = "default";
        state.WorkspaceResolverTrustPolicy.Value = "prompt";
        state.WorkspaceResolverArtifactRoot.Value = ".tianshu/smoke-artifacts";
        state.WorkspaceResolverStateRoot.Value = ".tianshu/smoke-state";
        state.WorkspaceResolverIgnoreGlobs.Value = "bin/**, obj/**";
        state.WorkspaceResolverLanguageMarkers.Value = "*.sln, *.csproj";
        state.WorkspaceResolverFrameworkMarkers.Value = "Directory.Build.props";
        state.SaveWorkspaceResolverPackage();

        var manifestPath = state.WorkspaceResolverPackageManifestPathText.Value.Replace("Manifest：", string.Empty, StringComparison.Ordinal).Trim();
        var manifestText = ReadFileOrEmpty(manifestPath);
        var saved = File.Exists(manifestPath)
            && manifestText.Contains("id = \"config_gui_smoke\"", StringComparison.Ordinal)
            && manifestText.Contains(".config-gui-smoke-root", StringComparison.Ordinal)
            && manifestText.Contains("artifact_root = \".tianshu/smoke-artifacts\"", StringComparison.Ordinal);
        Check(report, "workspace-resolver-manifest-save", saved, state.WorkspaceResolverPackageStatusText.Value);
        report.WorkspaceResolverManifestPath = manifestPath;
    }

    private static void SavePolicyStrategyManifest(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        if (state.PolicyStrategyPackageLabels.Count == 0)
        {
            Check(report, "policy-strategy-manifest-scan", false, "未扫描到审批策略包 manifest。");
            return;
        }

        state.SelectPolicyStrategyPackageIndex(0);
        state.PolicyStrategyId.Value = "config_gui_smoke";
        state.PolicyStrategyDisplayName.Value = "ConfigGUI Smoke Policy Strategy";
        state.PolicyStrategyEnabledIndex.Value = 0;
        state.PolicyStrategyTypeIndex.Value = 0;
        state.PolicyStrategyApprovalPolicyIndex.Value = 1;
        state.PolicyStrategySandboxModeIndex.Value = 1;
        state.PolicyStrategyNetworkAccessIndex.Value = 1;
        state.PolicyStrategyAllowLoginShellIndex.Value = 0;
        state.PolicyStrategyWriteApprovalGlobs.Value = "**/*";
        state.PolicyStrategyDangerousCommandPatterns.Value = "git reset, git clean";
        state.PolicyStrategyCommandRules.Value = "ask:git reset; ask:git clean";
        state.PolicyStrategyNetworkRules.Value = "deny:https:example.com";
        state.SavePolicyStrategyPackage();

        var manifestPath = state.PolicyStrategyPackageManifestPathText.Value.Replace("Manifest：", string.Empty, StringComparison.Ordinal).Trim();
        var text = ReadFileOrEmpty(manifestPath);
        var saved = text.Contains("id = \"config_gui_smoke\"", StringComparison.Ordinal)
                    && text.Contains("approval_policy = \"on-request\"", StringComparison.Ordinal)
                    && text.Contains("prefix = [\"git\", \"reset\"]", StringComparison.Ordinal);
        Check(report, "policy-strategy-manifest-save", saved, state.PolicyStrategyPackageStatusText.Value);
        report.PolicyStrategyManifestPath = manifestPath;
    }

    private static void SaveSkillPackageState(ConfigGuiState state, ConfigGuiSmokeReport report)
    {
        if (state.SkillPackageLabels.Count == 0)
        {
            Check(report, "skill-package-scan", false, "未发现可管理的技能包。");
            return;
        }

        var skillIndex = state.SkillPackageLabels
            .Select((label, index) => new { label, index })
            .FirstOrDefault(item => item.label.Contains("Smoke", StringComparison.OrdinalIgnoreCase)
                                    || item.label.Contains("smoke", StringComparison.OrdinalIgnoreCase))
            ?.index ?? 0;
        state.SelectSkillPackageIndex(skillIndex);
        var skillPath = state.SkillPackagePathText.Value
            .Split('；', 2, StringSplitOptions.TrimEntries)
            .FirstOrDefault()
            ?.Replace("SKILL.md：", string.Empty, StringComparison.Ordinal)
            .Trim() ?? string.Empty;
        var originalSkillDocument = ReadFileOrEmpty(skillPath);

        state.SkillPackageEnabledIndex.Value = 1;
        state.SaveSkillPackageEnabled();

        var config = ReadFileOrEmpty(state.ConfigPath);
        var skillDocumentUnchanged = string.Equals(originalSkillDocument, ReadFileOrEmpty(skillPath), StringComparison.Ordinal);
        var saved = config.Contains("[[skills.config]]", StringComparison.Ordinal)
            && config.Contains("enabled = false", StringComparison.Ordinal)
            && config.Contains("SKILL.md", StringComparison.Ordinal);
        Check(report, "skill-package-save", saved, state.SkillPackageStatusText.Value);
        Check(report, "skill-package-does-not-touch-skill-md", skillDocumentUnchanged, "保存技能包启用状态不应修改 SKILL.md。");
        report.SkillPackagePath = skillPath;
    }

    private static bool ContainsAll(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
        => expected.All(expectedValue => actual.Any(actualValue => string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase)));

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var startIndex = 0;
        while (true)
        {
            var index = text.IndexOf(value, startIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            startIndex = index + value.Length;
        }
    }

    private static void Check(ConfigGuiSmokeReport report, string name, bool passed, string detail)
        => report.Checks.Add(new ConfigGuiSmokeCheck(name, passed, detail));

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return arg[(name.Length + 1)..];
            }

            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static string ReadFileOrEmpty(string path)
        => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    private static string ModulePath(string configPath, params string[] segments)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();
        var path = Path.Combine(root, "modules");
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    private static bool ContainsPathSegments(string path, params string[] segments)
    {
        var normalized = path
            .Replace(Path.DirectorySeparatorChar.ToString(), "/", StringComparison.Ordinal)
            .Replace(Path.AltDirectorySeparatorChar.ToString(), "/", StringComparison.Ordinal);
        var needle = string.Join("/", segments);
        return normalized.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteReport(string? reportPath, ConfigGuiSmokeReport report)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, ConfigGuiSmokeJsonSerializerContext.Default.ConfigGuiSmokeReport));
    }
}
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ConfigGuiSmokeReport))]
internal sealed partial class ConfigGuiSmokeJsonSerializerContext : JsonSerializerContext;

internal sealed class ConfigGuiSmokeReport
{
    public string ConfigPath { get; set; } = string.Empty;

    public string PromptSaveTarget { get; set; } = string.Empty;

    public List<string> Modules { get; } = [];

    public List<string> ExpandedModules { get; } = [];

    public List<string> Categories { get; } = [];

    public List<string> ProviderLabels { get; } = [];

    public List<string> FoundationPages { get; } = [];

    public List<string> ProfileCompositionPages { get; } = [];

    public List<string> AgentPages { get; } = [];

    public List<string> ModelConfigurationPages { get; } = [];

    public List<string> MemoryPages { get; } = [];

    public List<string> ToolPages { get; } = [];

    public List<string> DiagnosticPages { get; } = [];

    public List<string> WorkspacePages { get; } = [];

    public List<string> PolicyPages { get; } = [];

    public List<string> ModelProtocolProviders { get; } = [];

    public List<string> ModelRouteSets { get; } = [];

    public List<string> PromptFiles { get; } = [];

    public List<string> PromptSections { get; } = [];

    public List<string> SkillPackages { get; } = [];

    public string SkillPackagePath { get; set; } = string.Empty;

    public List<string> ToolPackages { get; } = [];

    public string ToolManifestPath { get; set; } = string.Empty;

    public List<string> ProviderPackages { get; } = [];

    public string ProviderManifestPath { get; set; } = string.Empty;

    public List<string> McpServerPackages { get; } = [];

    public string McpServerManifestPath { get; set; } = string.Empty;

    public List<string> ArtifactStorePackages { get; } = [];

    public string ArtifactStoreManifestPath { get; set; } = string.Empty;

    public List<string> DiagnosticSinkPackages { get; } = [];

    public string DiagnosticSinkManifestPath { get; set; } = string.Empty;

    public List<string> WorkspaceResolverPackages { get; } = [];

    public string WorkspaceResolverManifestPath { get; set; } = string.Empty;

    public List<string> PolicyStrategyPackages { get; } = [];

    public string PolicyStrategyManifestPath { get; set; } = string.Empty;

    public List<ConfigGuiSmokeCheck> Checks { get; } = [];
}

internal sealed record ConfigGuiSmokeCheck(string Name, bool Passed, string Detail);

internal sealed class SmokeModelEndpoint : IDisposable
{
    private readonly TcpListener listener;
    private readonly Task serverTask;

    private SmokeModelEndpoint(TcpListener listener, string baseUrl)
    {
        this.listener = listener;
        BaseUrl = baseUrl;
        serverTask = Task.Run(ServeOneRequest);
    }

    public string BaseUrl { get; }

    public static SmokeModelEndpoint Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        return new SmokeModelEndpoint(listener, $"http://127.0.0.1:{endpoint.Port}");
    }

    public void Dispose()
    {
        listener.Stop();
        try
        {
            serverTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
    }

    private void ServeOneRequest()
    {
        try
        {
            using var client = listener.AcceptTcpClient();
            using var stream = client.GetStream();
            stream.ReadTimeout = 5000;
            var buffer = new byte[2048];
            _ = stream.Read(buffer, 0, buffer.Length);

            var body = Encoding.UTF8.GetBytes(
                """
                {"data":[{"id":"openai-compatible-default"},{"id":"qwen-plus"}]}
                """);
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            stream.Write(header, 0, header.Length);
            stream.Write(body, 0, body.Length);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
    }
}
