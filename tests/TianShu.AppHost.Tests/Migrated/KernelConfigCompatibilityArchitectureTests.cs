using System.IO;

namespace TianShu.AppHost.Tests;

public sealed class KernelConfigCompatibilityArchitectureTests
{
    [Fact]
    public void KernelSpawnAgentGuardRuntime_DoesNotKeepExplicitCamelCaseSpawnLimitPathsInKernel()
    {
        var repoRoot = FindRepoRoot();
        var kernelAgentGuardsPath = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "AppHostServer.AgentGuards.cs");
        var runtimeSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.State",
            "KernelSpawnAgentGuardAppHostRuntime.cs"));

        Assert.False(File.Exists(kernelAgentGuardsPath));
        Assert.DoesNotContain("camelCasePath:", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"agents\", \"maxThreads\"]", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"agents\", \"maxDepth\"]", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelRoleAndNetworkReaders_CoreSource_DelegateLegacyCamelCaseCompatibilityToHelpers()
    {
        var kernelAppServer = ReadKernelSource("AppHostServer.cs");

        Assert.DoesNotContain("ReadConfiguredString(roleConfig, \"config_file\", \"configFile\")", kernelAppServer, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadConfiguredString(config, \"model_reasoning_effort\", \"modelReasoningEffort\")", kernelAppServer, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadConfiguredString(config, \"developer_instructions\", \"developerInstructions\")", kernelAppServer, StringComparison.Ordinal);

        Assert.DoesNotContain("ReadConfiguredString(snapshot.Config, \"default_permissions\", \"defaultPermissions\")", kernelAppServer, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadConfiguredBoolean(network, \"allow_local_binding\", \"allowLocalBinding\")", kernelAppServer, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadConfiguredStringArrayValue(network, out var allowedDomains, [\"allowed_domains\"], [\"allowedDomains\"])", kernelAppServer, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelRealtimeInstructionAndPermissionReaders_CoreSource_DoNotKeepExplicitCamelCaseFallbackPaths()
    {
        var realtime = ReadRuntimeSource("KernelRealtimeContextRuntimeHelpers.cs");
        var kernelAppServer = ReadKernelSource("AppHostServer.cs");
        var tools = AssertDeletedKernelSource("KernelTools.cs");

        Assert.DoesNotContain("[\"experimentalFeatures\", \"realtime_conversation_v2\"]", realtime, StringComparison.Ordinal);
        Assert.DoesNotContain("\"experimentalRealtimeWsBaseUrl\"", realtime, StringComparison.Ordinal);
        Assert.DoesNotContain("\"experimentalRealtimeStartInstructions\"", realtime, StringComparison.Ordinal);

        Assert.DoesNotContain("TryReadConfiguredNestedValue(scopedConfig, [\"projectRootMarkers\"]", kernelAppServer, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadConfiguredNestedValue(scopedConfig, [\"projectDocMaxBytes\"]", kernelAppServer, StringComparison.Ordinal);

        Assert.DoesNotContain("ReadConfiguredBoolean(snapshot.Config, \"allow_login_shell\", \"allowLoginShell\")", kernelAppServer, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadConfiguredApprovalPolicy(config, out approvalPolicy, \"approval_policy\", \"approvalPolicy\")", kernelAppServer, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"permissions\", \"approvalPolicy\"]", kernelAppServer, StringComparison.Ordinal);
        Assert.DoesNotContain("private static KernelShellEnvironmentPolicy CreateShellEnvironmentPolicy(", kernelAppServer, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly record struct KernelResolvedPermissionSettings(", kernelAppServer, StringComparison.Ordinal);

        Assert.DoesNotContain("[\"approvalPolicy\", \"granular\", \"requestPermissions\"]", tools, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadConfiguredNestedValue(snapshot.Config, [\"approvalPolicy\", \"granular\"]", tools, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginManifestCamelCaseSurface_StaysInsideExternalJsonBridge()
    {
        var pluginsManagerSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelPluginsManager.cs"));

        Assert.Contains("PluginManifestRelativePath = \".tianshu-plugin/plugin.json\"", pluginsManagerSource, StringComparison.Ordinal);
        Assert.Contains("DefaultMcpConfigFileName = \".mcp.json\"", pluginsManagerSource, StringComparison.Ordinal);
        Assert.Contains("DefaultAppConfigFileName = \".app.json\"", pluginsManagerSource, StringComparison.Ordinal);
        Assert.Contains("TryGetProperty(rootObject, out var serversElement, \"mcpServers\", \"mcp_servers\")", pluginsManagerSource, StringComparison.Ordinal);
        Assert.Contains("ReadStringArray(element, \"envVars\", \"env_vars\")", pluginsManagerSource, StringComparison.Ordinal);
        Assert.Contains("ReadString(property.Value, \"installUrl\") ?? ReadString(property.Value, \"install_url\")", pluginsManagerSource, StringComparison.Ordinal);

        Assert.DoesNotContain("KernelConfigReadSnapshot", pluginsManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TianShuTomlConfigurationLoader", pluginsManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityReaders", pluginsManagerSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".RawConfig", pluginsManagerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelConfigSnapshotUtilities_ShouldNotReadCompatibilityConfigDirectly()
    {
        var configSnapshotSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelConfigSnapshotUtilities.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", configSnapshotSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", configSnapshotSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelPermissionProfileResolver_ShouldNotReadCompatibilityConfigDirectly()
    {
        var permissionResolverSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelPermissionProfileResolver.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", permissionResolverSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", permissionResolverSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelInstructionConfigUtilities_ShouldNotReadCompatibilityConfigDirectly()
    {
        var instructionConfigSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelInstructionConfigUtilities.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", instructionConfigSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", instructionConfigSource, StringComparison.Ordinal);
        Assert.DoesNotContain("experimentalInstructionsFile", instructionConfigSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelSpawnAgentRoleConfigurationUtilities_ShouldNotReadCompatibilityConfigDirectly()
    {
        var spawnAgentRoleConfigSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "KernelSpawnAgentRoleConfigurationUtilities.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", spawnAgentRoleConfigSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", spawnAgentRoleConfigSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelAutoCompactionRuntimeHelpers_ShouldNotReadCompatibilityConfigDirectly()
    {
        var autoCompactionRuntimeSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelAutoCompactionRuntimeHelpers.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", autoCompactionRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", autoCompactionRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelSpawnAgentsOnCsvRuntimeHelpers_ShouldNotReadCompatibilityConfigDirectly()
    {
        var spawnAgentsOnCsvRuntimeSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelSpawnAgentsOnCsvRuntimeHelpers.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", spawnAgentsOnCsvRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", spawnAgentsOnCsvRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelToolRuntimeApprovalHelpers_ShouldNotReadCompatibilityConfigDirectly()
    {
        var approvalRuntimeSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelToolRuntimeApprovalHelpers.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", approvalRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", approvalRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelInstructionScopeUtilities_ShouldNotReadCompatibilityConfigDirectly()
    {
        var instructionScopeSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelInstructionScopeUtilities.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", instructionScopeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", instructionScopeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("projectDocFallbackFilenames", instructionScopeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelManagedNetworkSettingsUtilities_ShouldNotReadCompatibilityConfigDirectly()
    {
        var managedNetworkSettingsSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "KernelManagedNetworkSettingsUtilities.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", managedNetworkSettingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", managedNetworkSettingsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextSlicingRuntimeHelpers_ShouldNotReadCompatibilityConfigDirectly()
    {
        var contextSlicingRuntimeSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "ContextSlicing",
            "ContextSlicingRuntimeHelpers.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", contextSlicingRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", contextSlicingRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelRealtimeContextRuntimeHelpers_ShouldNotReadCompatibilityConfigDirectly()
    {
        var realtimeContextRuntimeSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelRealtimeContextRuntimeHelpers.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", realtimeContextRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", realtimeContextRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("experimental_features", realtimeContextRuntimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void McpServerAuthUtilities_ShouldNotReadCompatibilityConfigDirectly()
    {
        var mcpServerAuthSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools",
            "McpServerAuthUtilities.cs"));

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", mcpServerAuthSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", mcpServerAuthSource, StringComparison.Ordinal);
    }

    private static string ReadKernelSource(string fileName)
    {
        if (string.Equals(fileName, "AppHostServer.cs", StringComparison.Ordinal))
        {
            return File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "src",
                "Hosting",
                "TianShu.AppHost",
                "AppHostServer.cs"));
        }

        return File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            fileName));
    }

    private static string AssertDeletedKernelSource(string fileName)
    {
        var path = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            fileName);
        Assert.False(File.Exists(path), $"旧文件不应继续存在: {path}");
        return string.Empty;
    }

    private static string ReadRuntimeSource(string fileName)
    {
        return File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            fileName));
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
}
