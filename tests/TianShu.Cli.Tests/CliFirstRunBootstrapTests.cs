using System.Text.Json;
using TianShu.Cli;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.RuntimeComposition;

namespace TianShu.Cli.Tests;

public sealed class CliFirstRunBootstrapTests
{
    [Fact]
    public void EnsureDefaultConfiguration_WritesPublicProviderTemplates()
    {
        var root = CreateTempRoot();
        var configPath = Path.Combine(root, "home", "tianshu.toml");

        var result = CliFirstRunBootstrapper.EnsureDefaultConfiguration(
            configPath,
            requestedProvider: "openai");

        Assert.Equal("openai", result.Provider);
        Assert.True(File.Exists(configPath));

        var providerInstancesPath = Path.Combine(root, "home", "modules", "model", "provider-instances", "default.toml");
        var routeSetPath = Path.Combine(root, "home", "modules", "model", "route-sets", "default.toml");
        var providerInstances = File.ReadAllText(providerInstancesPath);
        var routeSet = File.ReadAllText(routeSetPath);

        Assert.Contains("https://api.openai.com", providerInstances, StringComparison.Ordinal);
        Assert.Contains("https://api.anthropic.com", providerInstances, StringComparison.Ordinal);
        Assert.Contains("OPENAI_COMPATIBLE_API_KEY", providerInstances, StringComparison.Ordinal);
        Assert.Contains("provider = \"openai\"", routeSet, StringComparison.Ordinal);
        Assert.Contains("protocol = \"openai_responses\"", routeSet, StringComparison.Ordinal);
        Assert.DoesNotContain("protocol = \"responses\"", routeSet, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.", providerInstances, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENAI_API_KEY" + "_ST", providerInstances, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDefaultPath_WhenPortableProgramDirectoryProvided_ReturnsPackageConfig()
    {
        var root = CreateTempRoot();
        var packageRoot = Path.Combine(root, "portable");
        var binRoot = Path.Combine(packageRoot, "bin");
        Directory.CreateDirectory(binRoot);
        Directory.CreateDirectory(Path.Combine(packageRoot, "modules"));
        var configPath = Path.Combine(packageRoot, "tianshu.toml");
        File.WriteAllText(configPath, "profile = \"default\"");

        var resolved = RuntimeConfigurationComposition.ResolveDefaultPath(binRoot);

        Assert.Equal(Path.GetFullPath(configPath), resolved);
    }

    [Fact]
    public async Task Doctor_ReportsMissingApiKeyWithoutProbe()
    {
        using var environment = new EnvironmentVariableScope(("ANTHROPIC_API_KEY", null));
        var root = CreateTempRoot();
        var configPath = Path.Combine(root, "home", "tianshu.toml");
        _ = CliFirstRunBootstrapper.EnsureDefaultConfiguration(configPath, requestedProvider: "anthropic");

        var result = await CliOnboardingCommandRunner.BuildDoctorResultAsync(
            new DoctorCommandOptions
            {
                ConfigFilePath = configPath,
                WorkingDirectory = root,
                Probe = false,
            },
            CancellationToken.None);

        Assert.False(result.Ready);
        Assert.False(result.ProbeRequested);
        Assert.Contains(result.Issues, issue => issue.Code == "provider_api_key_missing");
        Assert.DoesNotContain(result.Issues, issue => issue.Code.StartsWith("probe_", StringComparison.Ordinal));
        Assert.True(result.Modules.DiscoveredCount > 0);
        Assert.True(result.Modules.RegisteredCount > 0);
    }

    [Fact]
    public async Task Doctor_WhenRuntimeRootIsBlocked_ReportsFailClosedRuntimeIssue()
    {
        var root = CreateTempRoot();
        var home = Path.Combine(root, "home");
        var configPath = Path.Combine(home, "tianshu.toml");
        _ = CliFirstRunBootstrapper.EnsureDefaultConfiguration(configPath, requestedProvider: "openai");
        File.WriteAllText(Path.Combine(home, "runtime"), "not a directory");

        var result = await CliOnboardingCommandRunner.BuildDoctorResultAsync(
            new DoctorCommandOptions
            {
                ConfigFilePath = configPath,
                WorkingDirectory = root,
                Probe = false,
            },
            CancellationToken.None);

        Assert.False(result.Ready);
        Assert.False(result.RuntimeWritable);
        var issue = Assert.Single(result.Issues, issue => issue.Code == CliRuntimeWriteGuard.RuntimeNotWritableCode);
        Assert.Contains("not writable", issue.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("不可写", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doctor_WhenPackageRuntimeIdentifierMismatchesCurrentPlatform_ReportsStructuredIssue()
    {
        var root = CreateTempRoot();
        var home = Path.Combine(root, "home");
        var configPath = Path.Combine(home, "tianshu.toml");
        _ = CliFirstRunBootstrapper.EnsureDefaultConfiguration(configPath, requestedProvider: "openai");
        File.WriteAllText(
            Path.Combine(home, "VERSION.txt"),
            """
            version=v0.5.0
            runtimeIdentifier=unknown-rid
            layout=portable-tianshu-home
            """);

        var result = await CliOnboardingCommandRunner.BuildDoctorResultAsync(
            new DoctorCommandOptions
            {
                ConfigFilePath = configPath,
                WorkingDirectory = root,
                Probe = false,
            },
            CancellationToken.None);

        Assert.Equal("unknown-rid", result.PackageRuntimeIdentifier);
        Assert.Equal(CliOnboardingCommandRunner.ResolveCurrentRuntimeIdentifier(), result.CurrentRuntimeIdentifier);
        Assert.False(result.RuntimeIdentifierMatches);
        var issue = Assert.Single(result.Issues, issue => issue.Code == "package_runtime_identifier_mismatch");
        Assert.Equal("error", issue.Severity);
        Assert.Contains("does not match", issue.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("不匹配", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorModuleDiagnostics_ShouldReportDiscoveredManifestAndRepairSuggestion()
    {
        var root = CreateTempRoot();
        WriteModuleManifest(Path.Combine(root, "modules", "acme-tool", "module.toml"), "tool.acme", "Tool");

        var result = await CliOnboardingCommandRunner.BuildModuleDoctorResultAsync(
            root,
            builtInDescriptors: [],
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.DiscoveredCount);
        Assert.Equal(1, result.SelectedCount);
        Assert.Equal(1, result.RejectedCount);
        Assert.Contains(result.Issues, static issue => issue.Code == "module_load.descriptor_missing");
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("tool.acme", candidate.ModuleId);
        Assert.Equal("Selected", candidate.DiscoveryStatus);
        Assert.Equal("Rejected", candidate.LoadStatus);
        Assert.Contains("descriptor_missing", candidate.GovernanceRisks);
        Assert.Contains(candidate.RepairSuggestions, static suggestion => suggestion.Contains("ModuleDescriptor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DoctorModuleDiagnostics_ShouldReportMissingConfigurationAndGovernanceRisk()
    {
        var root = CreateTempRoot();
        var descriptor = new ModuleDescriptor(
            "tool.risky",
            ModuleKind.Tool,
            "Risky Tool",
            "1.0.0",
            permission: new PermissionEnvelope(["tool.risky"], requiresHumanGate: false),
            sideEffects: new SideEffectProfile(SideEffectLevel.HostMutation, ["host"], reversible: false, requiresAudit: true),
            audit: new ModuleAuditProfile(required: true, eventKinds: ["tool.risky.invoked"]),
            trustLevel: ModuleTrustLevel.BuiltIn,
            requiredConfiguration:
            [
                new ModuleConfigurationRequirement("tool.risky.api_key", "Risky tool credential", required: true, secret: true),
            ],
            minimumTianShuVersion: "0.6.0",
            health: new ModuleHealthProbe(ModuleHealthStatus.Healthy),
            implementationBinding: new ModuleImplementationBinding("TianShu.Tests.RiskyTool", "RiskyToolModule"));

        var result = await CliOnboardingCommandRunner.BuildModuleDoctorResultAsync(
            root,
            builtInDescriptors: [descriptor],
            boundConfigurationKeys: new HashSet<string>(StringComparer.Ordinal),
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.MissingConfigurationCount);
        Assert.Equal(1, result.GovernanceRiskCount);
        Assert.Contains(result.Issues, static issue => issue.Code == "module_load.required_configuration_missing");
        var candidate = Assert.Single(result.Candidates);
        Assert.Contains("tool.risky.api_key", candidate.MissingConfigurationKeys);
        Assert.Contains("high_side_effect_without_human_gate", candidate.GovernanceRisks);
        Assert.Contains(candidate.RepairSuggestions, static suggestion => suggestion.Contains("tool.risky.api_key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Send_AutoBootstrapsCleanConfigPath_ToProviderApiKeyMissing()
    {
        using var environment = new EnvironmentVariableScope(
            ("OPENAI_API_KEY", null),
            ("ANTHROPIC_API_KEY", null),
            ("OPENAI_COMPATIBLE_API_KEY", null));
        var root = CreateTempRoot();
        var configPath = Path.Combine(root, "home", "tianshu.toml");
        var runner = new SendCommandRunner();

        var result = await runner.RunAsync(
            new SendCommandOptions
            {
                Message = "ping",
                WorkingDirectory = root,
                ConfigFilePath = configPath,
                OutputJson = true,
                ArtifactsRoot = Path.Combine(root, "artifacts"),
            },
            CancellationToken.None);

        Assert.True(File.Exists(configPath));
        using var document = JsonDocument.Parse(result.SummaryJson);
        var failureCodes = document.RootElement
            .GetProperty("replaySummary")
            .GetProperty("failureCodes")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        Assert.Contains("provider_api_key_missing", failureCodes);
        Assert.DoesNotContain("provider_model_missing", failureCodes);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-cli-first-run-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "TianShu.sln"), string.Empty);
        return root;
    }

    [Fact]
    public async Task Send_WhenDefaultRuntimeRootIsBlocked_FailsClosedWithoutCwdArtifacts()
    {
        var root = CreateTempRoot();
        var workspace = Path.Combine(root, "workspace");
        var home = Path.Combine(root, "home");
        Directory.CreateDirectory(workspace);
        var configPath = Path.Combine(home, "tianshu.toml");
        _ = CliFirstRunBootstrapper.EnsureDefaultConfiguration(configPath, requestedProvider: "openai");
        File.WriteAllText(Path.Combine(home, "runtime"), "not a directory");
        var runner = new SendCommandRunner();

        var result = await runner.RunAsync(
            new SendCommandOptions
            {
                Message = "ping",
                WorkingDirectory = workspace,
                ConfigFilePath = configPath,
                OutputJson = true,
            },
            CancellationToken.None);

        using var document = JsonDocument.Parse(result.SummaryJson);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(CliRuntimeWriteGuard.RuntimeNotWritableCode, document.RootElement.GetProperty("failureCode").GetString());
        Assert.False(Directory.Exists(Path.Combine(workspace, ".tianshu")));
        Assert.False(Directory.Exists(Path.Combine(workspace, ".tianshu-cli")));
    }

    private static void WriteModuleManifest(string path, string moduleId, string kind)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $$"""
            id = "{{moduleId}}"
            kind = "{{kind}}"
            display_name = "{{moduleId}}"
            version = "1.0.0"
            enabled = true
            min_tianshu_version = "0.6.0"
            capabilities = ["tool:echo"]

            [implementation]
            project = "TianShu.Tests.Modules"
            type = "TianShu.Tests.Modules.ToolModule"
            package_id = "{{moduleId}}"
            """);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> previousValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(params (string Name, string? Value)[] values)
        {
            foreach (var (name, value) in values)
            {
                previousValues[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in previousValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
