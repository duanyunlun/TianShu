using System.Text.Json;
using TianShu.Cli;

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
            requestedProvider: "anthropic");

        Assert.Equal("anthropic", result.Provider);
        Assert.True(File.Exists(configPath));

        var providerInstancesPath = Path.Combine(root, "home", "modules", "model", "provider-instances", "default.toml");
        var routeSetPath = Path.Combine(root, "home", "modules", "model", "route-sets", "default.toml");
        var providerInstances = File.ReadAllText(providerInstancesPath);
        var routeSet = File.ReadAllText(routeSetPath);

        Assert.Contains("https://api.openai.com", providerInstances, StringComparison.Ordinal);
        Assert.Contains("https://api.anthropic.com", providerInstances, StringComparison.Ordinal);
        Assert.Contains("OPENAI_COMPATIBLE_API_KEY", providerInstances, StringComparison.Ordinal);
        Assert.Contains("provider = \"anthropic\"", routeSet, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.", providerInstances, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENAI_API_KEY" + "_ST", providerInstances, StringComparison.Ordinal);
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
