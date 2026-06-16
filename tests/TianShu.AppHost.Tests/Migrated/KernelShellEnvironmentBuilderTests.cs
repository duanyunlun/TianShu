using System.Diagnostics;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelShellEnvironmentBuilderTests
{
    [Fact]
    public void CreateEnvironment_ShouldApplyDefaultExcludesWhenEnabled()
    {
        var policy = new KernelShellEnvironmentPolicy(
            KernelShellEnvironmentPolicyInherit.All,
            ignoreDefaultExcludes: false,
            excludePatterns: Array.Empty<string>(),
            setVariables: new Dictionary<string, string>(StringComparer.Ordinal),
            includeOnlyPatterns: Array.Empty<string>(),
            useProfile: false);

        var environment = KernelShellEnvironmentBuilder.CreateEnvironment(
            policy,
            threadId: "thread_env_001",
            sourceEnvironment: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PATH"] = "/usr/bin",
                ["API_KEY"] = "secret",
                ["SECRET_TOKEN"] = "hidden",
            });

        Assert.Equal("/usr/bin", environment["PATH"]);
        Assert.False(environment.ContainsKey("API_KEY"));
        Assert.False(environment.ContainsKey("SECRET_TOKEN"));
        Assert.Equal("thread_env_001", environment[KernelShellEnvironmentBuilder.ThreadIdEnvironmentVariable]);
    }

    [Fact]
    public void CreateEnvironment_ShouldSupportIncludeOnlyAndSet()
    {
        var policy = new KernelShellEnvironmentPolicy(
            KernelShellEnvironmentPolicyInherit.All,
            ignoreDefaultExcludes: true,
            excludePatterns: Array.Empty<string>(),
            setVariables: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TIANSHU_ONLY"] = "42",
            },
            includeOnlyPatterns: new[] { "*PATH", "TIANSHU_*" },
            useProfile: false);

        var environment = KernelShellEnvironmentBuilder.CreateEnvironment(
            policy,
            threadId: null,
            sourceEnvironment: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PATH"] = "/usr/bin",
                ["HOME"] = "/home/demo",
            });

        Assert.Equal("/usr/bin", environment["PATH"]);
        Assert.Equal("42", environment["TIANSHU_ONLY"]);
        Assert.False(environment.ContainsKey("HOME"));
    }

    [Fact]
    public void CreateEnvironment_ShouldHonorCoreInheritance()
    {
        var policy = new KernelShellEnvironmentPolicy(
            KernelShellEnvironmentPolicyInherit.Core,
            ignoreDefaultExcludes: true,
            excludePatterns: Array.Empty<string>(),
            setVariables: new Dictionary<string, string>(StringComparer.Ordinal),
            includeOnlyPatterns: Array.Empty<string>(),
            useProfile: false);

        var environment = KernelShellEnvironmentBuilder.CreateEnvironment(
            policy,
            threadId: null,
            sourceEnvironment: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PATH"] = "/usr/bin",
                ["HOME"] = "/home/demo",
                ["CUSTOM_VAR"] = "nope",
            });

        Assert.Equal("/usr/bin", environment["PATH"]);
        Assert.Equal("/home/demo", environment["HOME"]);
        Assert.False(environment.ContainsKey("CUSTOM_VAR"));
    }

    [Fact]
    public void CreateEnvironment_ShouldIncludeWindowsShellBootstrapVariablesForCoreInheritance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var policy = new KernelShellEnvironmentPolicy(
            KernelShellEnvironmentPolicyInherit.Core,
            ignoreDefaultExcludes: true,
            excludePatterns: Array.Empty<string>(),
            setVariables: new Dictionary<string, string>(StringComparer.Ordinal),
            includeOnlyPatterns: Array.Empty<string>(),
            useProfile: false);

        var environment = KernelShellEnvironmentBuilder.CreateEnvironment(
            policy,
            threadId: null,
            sourceEnvironment: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PATH"] = @"C:\Windows\System32",
                ["SystemRoot"] = @"C:\Windows",
                ["USERPROFILE"] = @"C:\Users\demo",
                ["APPDATA"] = @"C:\Users\demo\AppData\Roaming",
                ["CUSTOM_VAR"] = "nope",
            });

        Assert.Equal(@"C:\Windows", environment["SystemRoot"]);
        Assert.Equal(@"C:\Users\demo", environment["USERPROFILE"]);
        Assert.Equal(@"C:\Users\demo\AppData\Roaming", environment["APPDATA"]);
        Assert.False(environment.ContainsKey("CUSTOM_VAR"));
    }

    [Fact]
    public void CreateEnvironment_WithWindowsCorePolicy_ShouldAllowPowerShellStartup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var environment = KernelShellEnvironmentBuilder.CreateEnvironment(
            new KernelShellEnvironmentPolicy(
                KernelShellEnvironmentPolicyInherit.Core,
                ignoreDefaultExcludes: true,
                excludePatterns: Array.Empty<string>(),
                setVariables: new Dictionary<string, string>(StringComparer.Ordinal),
                includeOnlyPatterns: Array.Empty<string>(),
                useProfile: false),
            threadId: "thread_env_windows_ps");

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command Write-Output core-env-ready",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };

        startInfo.Environment.Clear();
        foreach (var variable in environment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var stdout = process!.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("core-env-ready", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
    }

    [Fact]
    public void CreateEnvironment_ShouldOverlayTurnScopedDependencyEnvironment()
    {
        var policy = new KernelShellEnvironmentPolicy(
            KernelShellEnvironmentPolicyInherit.All,
            ignoreDefaultExcludes: true,
            excludePatterns: Array.Empty<string>(),
            setVariables: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TIANSHU_TEST"] = "base",
            },
            includeOnlyPatterns: Array.Empty<string>(),
            useProfile: false);
        var dependencyEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TIANSHU_TEST"] = "override",
            ["TURN_SECRET"] = "from-scope",
        };

        using var _ = KernelDependencyEnvironmentScope.Push(dependencyEnvironment);
        var environment = KernelShellEnvironmentBuilder.CreateEnvironment(
            policy,
            threadId: "thread_env_scope",
            sourceEnvironment: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PATH"] = "/usr/bin",
            });

        Assert.Equal("override", environment["TIANSHU_TEST"]);
        Assert.Equal("from-scope", environment["TURN_SECRET"]);
    }
}
