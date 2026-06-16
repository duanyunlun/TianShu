using System.IO;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI.Tests;

public sealed class KernelExecutionProviderBoundaryArchitectureTests
{
    [Fact]
    public void AppHostServer_CoreSource_DoesNotKeepLocalResponsesRequestComposerBuilders()
    {
        var kernelFile = GetAppHostServerSourcePath(FindRepoRoot());

        var source = File.ReadAllText(kernelFile);

        Assert.DoesNotContain("BuildResponsesReasoningPayload(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildResponsesTextPayload(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildResponsesIncludeFields(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"tool_choice\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"parallel_tool_calls\"]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsesRequestCompositionRuntime_CoreSource_DelegatesResponsesRequestCompositionToProviderComposer()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelResponsesRequestCompositionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.Contains("ProviderResponsesRequestComposers.Resolve(", source, StringComparison.Ordinal);
        Assert.Contains("new ProviderResponsesRequestComposerContext(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostServer_CoreSource_DelegatesResponsesToolSurfaceCompilationToProviderBuilder()
    {
        var registryFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelResponsesToolRegistry.cs");

        var source = File.ReadAllText(registryFile);

        Assert.Contains("BuildProviderResponsesToolList(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildOpenAiResponsesToolList(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelToolRegistry_CoreSource_DoesNotKeepOpenAiSpecificResponsesToolBuilders()
    {
        var registryFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelResponsesToolRegistry.cs");

        var source = File.ReadAllText(registryFile);

        Assert.Contains("ProviderResponsesToolSurfaceBuilders.Resolve(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildOpenAiResponsesToolList(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelResponsesJsonSchemaSanitizer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelCoreSources_DelegateModelCapabilityLookupsToProviderCatalog()
    {
        var repoRoot = FindRepoRoot();
        var kernelAppServerSource = File.ReadAllText(GetAppHostServerSourcePath(repoRoot));
        var catalogUtilitiesSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Catalog",
            "KernelCatalogSurfaceUtilities.cs"));
        var responsesRequestCompositionRuntimeSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelResponsesRequestCompositionRuntime.cs"));
        var kernelParityPath = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "AppHostServer.Parity.cs");
        var otherSourceFiles = new[]
        {
            Path.Combine(repoRoot, "src", "Hosting", "TianShu.AppHost.Tools.Runtime", "KernelToolRuntimeServicesAppHostRuntime.cs"),
        };

        Assert.DoesNotContain("GetDefaultReasoningEffort(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("GetDefaultReasoningSummary(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("GetDefaultVerbosity(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("GetBaseInstructions(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderModelCatalogs.GetDefaultReasoningEffort(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderModelCatalogs.GetDefaultReasoningSummary(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderModelCatalogs.GetDefaultVerbosity(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderModelCatalogs.GetBaseInstructions(", kernelAppServerSource, StringComparison.Ordinal);
        Assert.Contains("ProviderModelCatalogs.GetDefaultReasoningEffort(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderModelCatalogs.GetDefaultReasoningSummary(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderModelCatalogs.GetDefaultVerbosity(", responsesRequestCompositionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderModelCatalogs.", catalogUtilitiesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelCodexModelCatalog", kernelAppServerSource, StringComparison.Ordinal);
        Assert.False(File.Exists(kernelParityPath), $"旧文件不应继续存在: {kernelParityPath}");

        foreach (var sourceFile in otherSourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            Assert.DoesNotContain("KernelCodexModelCatalog", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProviderNeutralResponsesRegistries_CoreSource_DoNotConstructOpenAiSpecificComponentsDirectly()
    {
        var repoRoot = FindRepoRoot();
        var registryFiles = new[]
        {
            "ProviderResponsesRequestComposers.cs",
            "ProviderResponsesTransportProtocolBindings.cs",
            "ProviderResponsesTransportRetryStrategies.cs",
            "ProviderResponsesToolSurfaceBuilders.cs",
        };

        foreach (var fileName in registryFiles)
        {
            var source = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Provider",
                "TianShu.Provider.Abstractions",
                fileName));

            Assert.DoesNotContain("using TianShu.Provider.OpenAI;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new OpenAi", source, StringComparison.Ordinal);
            Assert.Contains("ProviderResponsesComponentBootstraps.BuildComponents(", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProviderResponsesComponentBootstrapRegistry_CoreSource_AvoidsProviderAssemblyTypeSweep()
    {
        var registryFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Provider",
            "TianShu.Provider.Abstractions",
            "ProviderResponsesComponentBootstraps.cs");

        var source = File.ReadAllText(registryFile);

        Assert.DoesNotContain(".GetTypes()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppDomain.CurrentDomain.GetAssemblies()", source, StringComparison.Ordinal);
        Assert.Contains(
            "ProviderBootstrapLoader.LoadBootstraps<IProviderResponsesComponentBootstrap>(",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderResponsesComponentBootstrapRegistry_CoreSource_UsesSharedProviderBootstrapLoader()
    {
        var registryFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Provider",
            "TianShu.Provider.Abstractions",
            "ProviderResponsesComponentBootstraps.cs");

        var source = File.ReadAllText(registryFile);

        Assert.Contains("ProviderBootstrapLoader.LoadBootstraps<IProviderResponsesComponentBootstrap>(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KnownBootstrapTypeNames", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelProject_ShouldCarryOpenAiProvider_AsPackagingOnlyReference()
    {
        var repoRoot = FindRepoRoot();
        var appHostProjectFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "TianShu.AppHost.csproj");
        var deletedKernelProjectFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "TianShu.Kernel.csproj");

        var source = File.ReadAllText(appHostProjectFile);

        Assert.Contains(
            "<ProjectReference Include=\"..\\..\\Provider\\TianShu.Provider.OpenAI\\TianShu.Provider.OpenAI.csproj\" ExcludeAssets=\"compile\" />",
            source,
            StringComparison.Ordinal);
        Assert.False(File.Exists(deletedKernelProjectFile), $"旧工程文件不应继续存在: {deletedKernelProjectFile}");
    }

    [Fact]
    public void ProviderResponsesRequestComposer_DefaultBinding_LoadsComposerFromProviderAssembly()
    {
        var composer = ProviderResponsesRequestComposers.Resolve("responses", "test.providerWireApi");

        Assert.Equal("TianShu.Provider.OpenAI", composer.GetType().Assembly.GetName().Name);
    }

    [Fact]
    public void ProviderResponsesToolSurfaceBuilder_DefaultBinding_LoadsBuilderFromProviderAssembly()
    {
        var builder = ProviderResponsesToolSurfaceBuilders.Resolve("responses", "test.providerWireApi");

        Assert.Equal("TianShu.Provider.OpenAI", builder.GetType().Assembly.GetName().Name);
    }

    [Fact]
    public void ProviderModelCatalog_DefaultBinding_LoadsCatalogFromProviderAssembly()
    {
        var catalog = ProviderModelCatalogs.Resolve();

        Assert.Equal("TianShu.Provider.OpenAI", catalog.GetType().Assembly.GetName().Name);
    }

    [Fact]
    public void ProviderNeutralResponsesRegistries_ShouldLiveUnderProviderAbstractionsProject()
    {
        var repoRoot = FindRepoRoot();
        var oldRegistryFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelProviderResponsesComponentBootstraps.cs");
        var newRegistryFile = Path.Combine(
            repoRoot,
            "src",
            "Provider",
            "TianShu.Provider.Abstractions",
            "ProviderResponsesComponentBootstraps.cs");
        var oldWireApiFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelProviderWireApi.cs");
        var newWireApiFile = Path.Combine(
            repoRoot,
            "src",
            "Provider",
            "TianShu.Provider.Abstractions",
            "ProviderWireApi.cs");

        Assert.False(File.Exists(oldRegistryFile));
        Assert.True(File.Exists(newRegistryFile));
        Assert.False(File.Exists(oldWireApiFile));
        Assert.True(File.Exists(newWireApiFile));
    }

    [Fact]
    public void ProviderSpecificModelCatalog_ShouldLiveUnderProviderOpenAiProject()
    {
        var repoRoot = FindRepoRoot();
        var oldCatalogFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "AppServer",
            "KernelCodexModelCatalog.cs");
        var newCatalogFile = Path.Combine(
            repoRoot,
            "src",
            "Provider",
            "TianShu.Provider.OpenAI",
            "OpenAiModelCatalog.cs");

        Assert.False(File.Exists(oldCatalogFile));
        Assert.True(File.Exists(newCatalogFile));
    }

    [Fact]
    public void OpenAiAppCatalogCompatibilityAdapter_ShouldLiveUnderProviderAbstractionsProject_AndRuntimeShouldConsumeItWithoutProviderCompileTimeDependency()
    {
        var repoRoot = FindRepoRoot();
        var adapterFile = Path.Combine(
            repoRoot,
            "src",
            "Provider",
            "TianShu.Provider.Abstractions",
            "OpenAiAppCatalogCompatibilityAdapter.cs");
        var runtimeProjectFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "TianShu.AppHost.Tools.Runtime.csproj");
        var runtimeSourceFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelPluginsAppHostRuntime.cs");
        var approvalHelpersFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelToolRuntimeApprovalHelpers.cs");
        var toolSuggestHandlerFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelToolDiscoveryRuntimeSupport.cs");

        Assert.True(File.Exists(adapterFile));

        var runtimeProjectSource = File.ReadAllText(runtimeProjectFile);
        var runtimeSource = File.ReadAllText(runtimeSourceFile);
        var approvalHelpersSource = File.ReadAllText(approvalHelpersFile);
        var toolSuggestHandlerSource = File.ReadAllText(toolSuggestHandlerFile);

        Assert.Contains(
            "<ProjectReference Include=\"..\\..\\Provider\\TianShu.Provider.Abstractions\\TianShu.Provider.Abstractions.csproj\" />",
            runtimeProjectSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<ProjectReference Include=\"..\\..\\Provider\\TianShu.Provider.OpenAI\\TianShu.Provider.OpenAI.csproj\"",
            runtimeProjectSource,
            StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityAdapter.TryReadConfiguredBaseUrl(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("TryReadAuthContextAsync(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityAdapter.IsDisallowedConnector(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityAdapter.IsToolSuggestDiscoverableConnector(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityAdapter.BuildCatalogUri(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityAdapter.ApplyAuthHeaders(", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityKeys.ChatGptAccountIdHeaderName", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("using TianShu.Provider.OpenAI;", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("using TianShu.Provider.OpenAI;", approvalHelpersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("using TianShu.Provider.OpenAI;", toolSuggestHandlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultChatGptBaseUrl", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolSuggestDiscoverableConnectorIds", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DisallowedConnectorIds", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"chatgpt-account-id\"", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityAdapter.BuildConnectorApprovalSessionKey(", approvalHelpersSource, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityKeys.CodexAppsMcpServerName", toolSuggestHandlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAiAppCatalogCompatibilityAdapter.CodexAppsMcpServerName", toolSuggestHandlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelProject_ShouldNotEmbedProviderSpecificModelsResource()
    {
        var repoRoot = FindRepoRoot();
        var appHostProjectFile = Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "TianShu.AppHost.csproj");
        var deletedKernelProjectFile = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.Kernel",
            "TianShu.Kernel.csproj");

        var source = File.ReadAllText(appHostProjectFile);

        Assert.DoesNotContain("Resources\\Codex\\models.json", source, StringComparison.Ordinal);
        Assert.False(File.Exists(deletedKernelProjectFile), $"旧工程文件不应继续存在: {deletedKernelProjectFile}");
    }

    [Fact]
    public void ProviderAbstractionsProject_ShouldExposeInternalsToProviderOpenAiTests_AndDropLegacyKernelTestsFriendAssembly()
    {
        var projectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Provider",
            "TianShu.Provider.Abstractions",
            "TianShu.Provider.Abstractions.csproj");

        var source = File.ReadAllText(projectFile);

        Assert.Contains(
            "<InternalsVisibleTo Include=\"TianShu.Provider.OpenAI.Tests\" />",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<InternalsVisibleTo Include=\"TianShu.Kernel.Tests\" />",
            source,
            StringComparison.Ordinal);
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

    private static string GetAppHostServerSourcePath(string repoRoot)
    {
        return Path.Combine(
            repoRoot,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "AppHostServer.cs");
    }
}
