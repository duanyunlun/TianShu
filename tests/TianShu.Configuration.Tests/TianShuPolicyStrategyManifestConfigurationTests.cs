namespace TianShu.Configuration.Tests;

public sealed class TianShuPolicyStrategyManifestConfigurationTests
{
    [Fact]
    public void Load_ScansPolicyStrategyPackageManifests()
    {
        using var temp = TempTianShuHome.Create();
        WriteManifest(Path.Combine(temp.Root, "modules", "policies", "strategies", "builtin", "policy.toml"), "builtin", "default", "on-request");
        WriteManifest(Path.Combine(temp.Root, "modules", "policies", "strategies", "company", "policy.toml"), "company", "strict", "on-failure");

        var projection = new TianShuPolicyStrategyManifestConfiguration().Load(temp.Root);

        Assert.Equal(2, projection.Files.Count);
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("builtin", "policy.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Files, static file => file.DisplayName.EndsWith(Path.Combine("company", "policy.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Equal("builtin", projection.SelectedPackage?.Id);
        Assert.Equal("default", Assert.Single(projection.SelectedPackage!.Strategies).Id);
    }

    [Fact]
    public void SavePackage_UpdatesPolicyManifestWithoutTouchingTianShuToml()
    {
        using var temp = TempTianShuHome.Create();
        var configPath = Path.Combine(temp.Root, "tianshu.toml");
        var manifestPath = Path.Combine(temp.Root, "modules", "policies", "strategies", "builtin", "policy.toml");
        File.WriteAllText(configPath, "model = \"gpt-test\"\n");
        WriteManifest(manifestPath, "builtin", "default", "on-request");

        var configuration = new TianShuPolicyStrategyManifestConfiguration();
        var package = configuration.Load(temp.Root, manifestPath).SelectedPackage!;
        package.Version = "1.4.0";
        package.MinTianShuVersion = "0.1.0";
        package.Capabilities = ["policy:approval", "policy:sandbox"];
        package.Diagnostics = ["policy:load"];
        package.Strategies =
        [
            .. package.Strategies,
            new PolicyStrategyManifestValue
            {
                Id = "strict",
                DisplayName = "Strict Policy Strategy",
                Enabled = true,
                Type = "rules",
                ApprovalPolicy = "on-request",
                SandboxMode = "read-only",
                NetworkAccess = false,
                AllowLoginShell = false,
                WriteRequiresApprovalGlobs = ["src/**"],
                DangerousCommandPatterns = ["git reset"],
                CommandRules =
                [
                    new PolicyStrategyCommandRuleValue(["git", "reset"], "ask", "需要审批"),
                ],
                NetworkRules =
                [
                    new PolicyStrategyNetworkRuleValue("https", "example.com", "deny", "禁止示例域名"),
                ],
                Priority = 10,
            },
        ];

        configuration.SavePackage(manifestPath, package);

        var saved = File.ReadAllText(manifestPath);
        Assert.Contains("id = \"strict\"", saved, StringComparison.Ordinal);
        Assert.Contains("version = \"1.4.0\"", saved, StringComparison.Ordinal);
        Assert.Contains("min_tianshu_version = \"0.1.0\"", saved, StringComparison.Ordinal);
        Assert.Contains("capabilities = [\"policy:approval\", \"policy:sandbox\"]", saved, StringComparison.Ordinal);
        Assert.Contains("diagnostics = [\"policy:load\"]", saved, StringComparison.Ordinal);
        Assert.Contains("approval_policy = \"on-request\"", saved, StringComparison.Ordinal);
        Assert.Contains("sandbox_mode = \"read-only\"", saved, StringComparison.Ordinal);
        Assert.Contains("prefix = [\"git\", \"reset\"]", saved, StringComparison.Ordinal);
        Assert.Contains("host = \"example.com\"", saved, StringComparison.Ordinal);
        Assert.Equal("model = \"gpt-test\"\n", File.ReadAllText(configPath));

        var reloaded = configuration.Load(temp.Root, manifestPath).SelectedPackage!;
        Assert.Equal("1.4.0", reloaded.Version);
        Assert.Equal("0.1.0", reloaded.MinTianShuVersion);
        Assert.Equal(["policy:approval", "policy:sandbox"], reloaded.Capabilities);
        Assert.Equal(["policy:load"], reloaded.Diagnostics);
    }

    [Fact]
    public void ResolveEffectiveDefaults_UsesEnabledStrategyValuesInPriorityOrder()
    {
        using var temp = TempTianShuHome.Create();
        WriteManifest(Path.Combine(temp.Root, "modules", "policies", "strategies", "builtin", "policy.toml"), "builtin", "default", "on-request", priority: 0);
        WriteManifest(Path.Combine(temp.Root, "modules", "policies", "strategies", "company", "policy.toml"), "company", "strict", "on-failure", priority: 10);

        var defaults = TianShuPolicyStrategyManifestConfiguration.ResolveEffectiveDefaults(temp.Root);

        Assert.Equal("on-failure", defaults.ApprovalPolicy);
        Assert.Equal("workspace-write", defaults.SandboxMode);
        Assert.False(defaults.NetworkAccess);
    }

    [Fact]
    public void LoadEnabledPackages_WhenVersionIncompatible_SkipsPackage()
    {
        using var temp = TempTianShuHome.Create();
        var manifestPath = Path.Combine(temp.Root, "modules", "policies", "strategies", "future", "policy.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(
            manifestPath,
            """
            id = "future"
            display_name = "future"
            enabled = true
            type = "package"
            priority = 0
            min_tianshu_version = "99.0.0"

            [[strategies]]
            id = "future"
            enabled = true
            type = "rules"
            approval_policy = "never"
            sandbox_mode = "danger-full-access"
            """);

        var configuration = new TianShuPolicyStrategyManifestConfiguration();
        var projection = configuration.Load(temp.Root, manifestPath);
        var defaults = TianShuPolicyStrategyManifestConfiguration.ResolveEffectiveDefaults(temp.Root);

        Assert.Equal("unavailable", projection.SelectedPackage!.LoadStatus);
        Assert.Contains(projection.Issues, static issue => issue.Code == "policy_strategy_manifest.version_incompatible");
        Assert.Null(defaults.ApprovalPolicy);
        Assert.Null(defaults.SandboxMode);
    }

    [Fact]
    public void ResolveEffectiveCommandAndNetworkRules_ReadsEnabledRules()
    {
        using var temp = TempTianShuHome.Create();
        WriteManifest(Path.Combine(temp.Root, "modules", "policies", "strategies", "builtin", "policy.toml"), "builtin", "default", "on-request");

        var commandRule = Assert.Single(TianShuPolicyStrategyManifestConfiguration.ResolveEffectiveCommandRules(temp.Root));
        Assert.Equal(["git", "reset"], commandRule.Prefix);
        Assert.Equal("ask", commandRule.Decision);

        var networkRule = Assert.Single(TianShuPolicyStrategyManifestConfiguration.ResolveEffectiveNetworkRules(temp.Root));
        Assert.Equal("https", networkRule.Protocol);
        Assert.Equal("example.com", networkRule.Host);
        Assert.Equal("deny", networkRule.Decision);
    }

    [Fact]
    public void CreateCopyAndDeletePackage_OnlyWritesPolicyStrategiesDirectory()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuPolicyStrategyManifestConfiguration();

        var createdPath = configuration.CreatePackage(temp.Root, "company-policy");
        Assert.Equal(Path.Combine(temp.Root, "modules", "policies", "strategies", "company-policy", "policy.toml"), createdPath);
        Assert.True(File.Exists(createdPath));

        var copiedPath = configuration.CopyPackage(temp.Root, createdPath, "company-policy-copy");
        Assert.Equal(Path.Combine(temp.Root, "modules", "policies", "strategies", "company-policy-copy", "policy.toml"), copiedPath);
        Assert.True(File.Exists(copiedPath));

        configuration.DeletePackage(temp.Root, copiedPath);
        Assert.False(File.Exists(copiedPath));
    }

    [Fact]
    public void CreatePackage_RejectsPathsOutsidePolicyStrategyRoot()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuPolicyStrategyManifestConfiguration();

        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "..\\outside"));
        Assert.Throws<InvalidOperationException>(() => configuration.CreatePackage(temp.Root, "nested\\policy"));
    }

    private static void WriteManifest(string path, string packageId, string strategyId, string approvalPolicy, int priority = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $$"""
            id = "{{packageId}}"
            display_name = "{{packageId}}"
            enabled = true
            type = "builtin"
            priority = {{priority}}

            [[strategies]]
            id = "{{strategyId}}"
            display_name = "{{strategyId}}"
            enabled = true
            type = "rules"
            priority = 0
            approval_policy = "{{approvalPolicy}}"
            sandbox_mode = "workspace-write"
            network_access = false
            allow_login_shell = true
            write_requires_approval_globs = ["**/*"]
            dangerous_command_patterns = ["git reset"]

            [[strategies.command_rules]]
            prefix = ["git", "reset"]
            decision = "ask"
            reason = "需要审批"

            [[strategies.network_rules]]
            protocol = "https"
            host = "example.com"
            decision = "deny"
            reason = "禁止示例域名"
            """);
    }

    private sealed class TempTianShuHome : IDisposable
    {
        private TempTianShuHome(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TempTianShuHome Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"tianshu-policy-strategy-manifest-config-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new TempTianShuHome(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

