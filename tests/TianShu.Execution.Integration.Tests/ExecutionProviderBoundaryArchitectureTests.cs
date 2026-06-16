using System.IO;
using System.Linq;
using System.Reflection;
using TianShu.Execution.Runtime;
using TianShu.Contracts.Conversations;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Integration.Tests;

public sealed class ExecutionProviderBoundaryArchitectureTests
{
    [Fact]
    public void TianShuExecutionRuntime_CoreSource_DoesNotHardCodeOpenAiSouthboundBootstrap()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("OpenAiResponsesProtocolAdapter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProtocolAdapterFactory.Create", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppendCodexCliArguments", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuExecutionRuntime_CoreSource_DoesNotConstructProviderCompletionOrFailureEvents()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("new ProviderCompletionEvent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProviderFailureEvent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuExecutionRuntime_CoreSource_DoesNotConstructProviderTypedItemEvents()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("new ProviderTextDeltaEvent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProviderReasoningDeltaEvent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProviderToolDirectiveEvent(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuExecutionRuntime_CoreSource_DoesNotKeepLocalProviderToolHelperBuilders()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("BuildProviderToolDirectiveEvent(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildProviderToolOutputDeltaEvent(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildProviderToolResultEvent(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuExecutionRuntime_CoreSource_DoesNotHardCodeProviderServerRequestMethods()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("item/tool/requestApproval", source, StringComparison.Ordinal);
        Assert.DoesNotContain("item/tool/requestUserInput", source, StringComparison.Ordinal);
        Assert.DoesNotContain("item/tool/call", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mcpServer/elicitation/request", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuExecutionRuntime_CoreSource_DoesNotHardCodeProviderSpecificConfigKeys()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("\"chatgpt_base_url\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"forced_chatgpt_workspace_id\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"forced_login_method\"", source, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityKeys.ChatGptBaseUrlConfigKey", source, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityKeys.ForcedChatGptWorkspaceIdConfigKey", source, StringComparison.Ordinal);
        Assert.Contains("OpenAiAppCatalogCompatibilityKeys.ForcedLoginMethodConfigKey", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuExecutionRuntime_CoreSource_DoesNotDeserializeProviderServerRequestDtos()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("CommandExecutionRequestApprovalParamsDto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyExecCommandApprovalParamsDto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FileChangeRequestApprovalParamsDto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyApplyPatchApprovalParamsDto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolRequestApprovalParamsDto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("McpServerElicitationRequestParamsDto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PermissionRequestParamsDto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToolRequestUserInputParamsDto", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DynamicToolCallRequestParamsDto", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuExecutionRuntime_CoreSource_DoesNotKeepLocalServerRequestResponseBuilders()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("BuildApprovalRequestResponsePayload(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildLegacyApprovalRequestResponsePayload(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildMcpServerElicitationApprovalResponsePayload(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildMcpServerElicitationUserInputResponsePayload(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildToolRequestUserInputResponse(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildPermissionRequestResponse(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SerializeDynamicToolContentItem(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MaterializeDynamicToolOutputItems(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuExecutionRuntime_CoreSource_DoesNotKeepLegacyApprovalCompatibilityBranches()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("HandleLegacyCommandExecutionRequestApprovalAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleLegacyApplyPatchApprovalAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderLegacyCommandExecutionApprovalRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderLegacyApplyPatchApprovalRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderServerRequestKind.LegacyCommandExecutionApproval", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderServerRequestKind.LegacyApplyPatchApproval", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuExecutionRuntime_CoreSource_UsesSingleProviderRuntimeStateInitializationPath()
    {
        var runtimeFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "TianShuExecutionRuntime.cs");

        var source = File.ReadAllText(runtimeFile);

        Assert.DoesNotContain("ProviderRuntimeBootstrapRegistry.Resolve(null)", source, StringComparison.Ordinal);
        Assert.Contains("ApplyProviderRuntimeState(ProviderRuntimeBootstrapRegistry.CreateRuntimeState(null));", source, StringComparison.Ordinal);
        Assert.Contains("ApplyProviderRuntimeState(ProviderRuntimeBootstrapRegistry.CreateRuntimeState(options.ProtocolAdapter));", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuExecutionRuntime_DefaultConstruction_LoadsProtocolAdapterFromProviderAssembly()
    {
        var expectedRuntimeState = ProviderRuntimeBootstrapRegistry.CreateRuntimeState(null);
        var runtime = new TianShuExecutionRuntime();
        var adapterField = typeof(TianShuExecutionRuntime).GetField("protocolAdapter", BindingFlags.Instance | BindingFlags.NonPublic);

        var adapter = Assert.IsAssignableFrom<object>(adapterField?.GetValue(runtime));
        Assert.Equal(expectedRuntimeState.ProtocolAdapter.GetType(), adapter.GetType());
    }

    [Fact]
    public void TianShuExecutionRuntime_DefaultConstruction_LoadsNotificationInterpreterFromProviderAssembly()
    {
        var expectedRuntimeState = ProviderRuntimeBootstrapRegistry.CreateRuntimeState(null);
        var runtime = new TianShuExecutionRuntime();
        var interpreterField = typeof(TianShuExecutionRuntime).GetField("providerNotificationInterpreter", BindingFlags.Instance | BindingFlags.NonPublic);

        var interpreter = Assert.IsAssignableFrom<object>(interpreterField?.GetValue(runtime));
        Assert.Equal(expectedRuntimeState.NotificationInterpreter.GetType(), interpreter.GetType());
    }

    [Fact]
    public void TianShuExecutionRuntime_DefaultConstruction_LoadsToolEventFactoryFromProviderAssembly()
    {
        var expectedRuntimeState = ProviderRuntimeBootstrapRegistry.CreateRuntimeState(null);
        var runtime = new TianShuExecutionRuntime();
        var factoryField = typeof(TianShuExecutionRuntime).GetField("providerToolEventFactory", BindingFlags.Instance | BindingFlags.NonPublic);

        var factory = Assert.IsAssignableFrom<object>(factoryField?.GetValue(runtime));
        Assert.Equal(expectedRuntimeState.ToolEventFactory.GetType(), factory.GetType());
    }

    [Fact]
    public void TianShuExecutionRuntime_DefaultConstruction_LoadsServerRequestRouterFromProviderAssembly()
    {
        var expectedRuntimeState = ProviderRuntimeBootstrapRegistry.CreateRuntimeState(null);
        var runtime = new TianShuExecutionRuntime();
        var routerField = typeof(TianShuExecutionRuntime).GetField("providerServerRequestRouter", BindingFlags.Instance | BindingFlags.NonPublic);

        var router = Assert.IsAssignableFrom<object>(routerField?.GetValue(runtime));
        Assert.Equal(expectedRuntimeState.ServerRequestRouter.GetType(), router.GetType());
    }

    [Fact]
    public void TianShuExecutionRuntime_DefaultConstruction_LoadsServerRequestInterpreterFromProviderAssembly()
    {
        var expectedRuntimeState = ProviderRuntimeBootstrapRegistry.CreateRuntimeState(null);
        var runtime = new TianShuExecutionRuntime();
        var interpreterField = typeof(TianShuExecutionRuntime).GetField("providerServerRequestInterpreter", BindingFlags.Instance | BindingFlags.NonPublic);

        var interpreter = Assert.IsAssignableFrom<object>(interpreterField?.GetValue(runtime));
        Assert.Equal(expectedRuntimeState.ServerRequestInterpreter.GetType(), interpreter.GetType());
    }

    [Fact]
    public void TianShuExecutionRuntime_DefaultConstruction_LoadsServerRequestResponseSerializerFromProviderAssembly()
    {
        var expectedRuntimeState = ProviderRuntimeBootstrapRegistry.CreateRuntimeState(null);
        var runtime = new TianShuExecutionRuntime();
        var serializerField = typeof(TianShuExecutionRuntime).GetField("providerServerRequestResponseSerializer", BindingFlags.Instance | BindingFlags.NonPublic);

        var serializer = Assert.IsAssignableFrom<object>(serializerField?.GetValue(runtime));
        Assert.Equal(expectedRuntimeState.ServerRequestResponseSerializer.GetType(), serializer.GetType());
    }

    [Fact]
    public void ProviderFacingExecutionAbstractions_ShouldLiveInProviderAbstractionsAssembly()
    {
        var types = new[]
        {
            typeof(IProviderRuntimeBootstrap),
            typeof(IProtocolAdapter),
            typeof(IProviderNotificationInterpreter),
            typeof(IProviderToolEventFactory),
            typeof(IProviderServerRequestRouter),
            typeof(IProviderServerRequestInterpreter),
            typeof(IProviderServerRequestResponseSerializer),
            typeof(ApprovalRequestPayload),
            typeof(PermissionRequestPayload),
            typeof(UserInputRequestPayload),
            typeof(ApprovalDecisionOptionPayload),
            typeof(ExecPolicyAmendmentPayload),
            typeof(NetworkPolicyAmendmentPayload),
        };

        Assert.All(
            types,
            static abstraction => Assert.Equal("TianShu.Provider.Abstractions", abstraction.Assembly.GetName().Name));
    }

    [Fact]
    public void ProviderExecutionAbstractions_ShouldNotExposeLegacyApprovalContracts()
    {
        var routerContractFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Provider",
            "TianShu.Provider.Abstractions",
            "Execution",
            "IProviderServerRequestRouter.cs");
        var interpreterContractFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Provider",
            "TianShu.Provider.Abstractions",
            "Execution",
            "IProviderServerRequestInterpreter.cs");

        var routerSource = File.ReadAllText(routerContractFile);
        var interpreterSource = File.ReadAllText(interpreterContractFile);

        Assert.DoesNotContain("LegacyCommandExecutionApproval", routerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyApplyPatchApproval", routerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderLegacyCommandExecutionApprovalRequest", interpreterSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderLegacyApplyPatchApprovalRequest", interpreterSource, StringComparison.Ordinal);

        Assert.DoesNotContain(
            Enum.GetNames(typeof(ProviderServerRequestKind)),
            static name => string.Equals(name, "LegacyCommandExecutionApproval", StringComparison.Ordinal));
        Assert.DoesNotContain(
            Enum.GetNames(typeof(ProviderServerRequestKind)),
            static name => string.Equals(name, "LegacyApplyPatchApproval", StringComparison.Ordinal));

        var providerAbstractionTypes = typeof(IProviderServerRequestInterpreter).Assembly
            .GetTypes()
            .Select(static type => type.Name)
            .ToArray();

        Assert.DoesNotContain("ProviderLegacyCommandExecutionApprovalRequest", providerAbstractionTypes);
        Assert.DoesNotContain("ProviderLegacyApplyPatchApprovalRequest", providerAbstractionTypes);
    }

    [Fact]
    public void RuntimeProvidersFolder_ShouldNotKeepRelocatedProviderFacingExecutionAbstractions()
    {
        var providersDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime",
            "Runtime",
            "Providers");

        Assert.False(File.Exists(Path.Combine(providersDirectory, "IProviderNotificationInterpreter.cs")));
        Assert.False(File.Exists(Path.Combine(providersDirectory, "IProviderToolEventFactory.cs")));
        Assert.False(File.Exists(Path.Combine(providersDirectory, "IProviderServerRequestRouter.cs")));
        Assert.False(File.Exists(Path.Combine(providersDirectory, "IProviderServerRequestInterpreter.cs")));
        Assert.False(File.Exists(Path.Combine(providersDirectory, "IProviderServerRequestResponseSerializer.cs")));
    }

    [Fact]
    public void RuntimeFolder_ShouldNotKeepRelocatedProviderApprovalDecisionModels()
    {
        var runtimeDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime",
            "Runtime");

        Assert.False(File.Exists(Path.Combine(runtimeDirectory, "ApprovalDecisionModels.cs")));
    }

    [Fact]
    public void RuntimeFolders_ShouldNotKeepRelocatedProviderBootstrapAndProtocolAdapterContracts()
    {
        var repoRoot = FindRepoRoot();
        var providersDirectory = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime",
            "Runtime",
            "Providers");
        var endpointAdaptersDirectory = Path.Combine(
            repoRoot,
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime",
            "Runtime",
            "EndpointAdapters");

        Assert.False(File.Exists(Path.Combine(providersDirectory, "IAgentRuntimeProviderBootstrap.cs")));
        Assert.False(File.Exists(Path.Combine(endpointAdaptersDirectory, "IProtocolAdapter.cs")));
        Assert.False(File.Exists(Path.Combine(endpointAdaptersDirectory, "AnthropicMessagesProtocolAdapter.cs")));
        Assert.False(File.Exists(Path.Combine(endpointAdaptersDirectory, "ProtocolAdapterFactory.cs")));
    }

    [Fact]
    public void ProviderCliArgumentContext_ShouldLiveInProviderAbstractionsAssembly()
        => Assert.Equal("TianShu.Provider.Abstractions", typeof(ProviderRuntimeCliArguments).Assembly.GetName().Name);

    [Fact]
    public void ProtocolAdapterBoundary_ShouldUseControlPlaneInputItemContract()
    {
        var buildUserInput = typeof(IProtocolAdapter).GetMethod(nameof(IProtocolAdapter.BuildUserInput));

        Assert.NotNull(buildUserInput);
        var parameter = Assert.Single(buildUserInput!.GetParameters());
        Assert.Equal(typeof(ControlPlaneInputItem), parameter.ParameterType);
    }

    [Fact]
    public void RuntimeProvidersFolder_ShouldNotKeepRelocatedProviderCliArgumentContext()
    {
        var providersDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime",
            "Runtime",
            "Providers");

        Assert.False(File.Exists(Path.Combine(providersDirectory, "AgentRuntimeProviderCliArguments.cs")));
    }

    [Fact]
    public void ProviderRuntimeBootstrapRegistry_CoreSource_UsesSharedProviderBootstrapLoader()
    {
        var registryFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Provider",
            "TianShu.Provider.Abstractions",
            "BootstrapRuntime",
            "ProviderRuntimeBootstrapRegistry.cs");

        var source = File.ReadAllText(registryFile);

        Assert.Contains("ProviderBootstrapLoader.LoadBootstraps<IProviderRuntimeBootstrap>(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KnownBootstrapTypeNames", source, StringComparison.Ordinal);
        Assert.Contains("public static ProviderRuntimeState CreateRuntimeState(string? protocolAdapterId)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderBootstrapLoader_CoreSource_UsesProviderSelfDeclaredRegistration()
    {
        var loaderFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Provider",
            "TianShu.Provider.Abstractions",
            "ProviderBootstrapLoader.cs");

        var source = File.ReadAllText(loaderFile);

        Assert.Contains("ProviderBootstrapRegistrationAttribute", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KnownBootstrapTypeNames", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TianShu.Provider.OpenAI.OpenAiProviderBootstrap", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenAiProviderAssembly_ShouldDeclareBootstrapRegistrationAttribute()
    {
        var providerRegistrationFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Provider",
            "TianShu.Provider.OpenAI",
            "ProviderBootstrapRegistration.cs");

        var source = File.ReadAllText(providerRegistrationFile);

        Assert.Contains("[assembly: ProviderBootstrapRegistration(typeof(OpenAiProviderBootstrap))]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderRuntimeBootstrapRegistry_CoreSource_ExposesOptionalProtocolAdapterNormalizationSurface()
    {
        var registryFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Provider",
            "TianShu.Provider.Abstractions",
            "BootstrapRuntime",
            "ProviderRuntimeBootstrapRegistry.cs");

        var source = File.ReadAllText(registryFile);

        Assert.Contains("public static string? NormalizeOptionalProtocolAdapterId(string? protocolAdapterId, string source)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderRuntimeBootstrapRegistry_CoreSource_ExposesSupportedProtocolAdapterQuerySurface()
    {
        var registryFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Provider",
            "TianShu.Provider.Abstractions",
            "BootstrapRuntime",
            "ProviderRuntimeBootstrapRegistry.cs");

        var source = File.ReadAllText(registryFile);

        Assert.Contains("public static string GetDefaultProtocolAdapterId()", source, StringComparison.Ordinal);
        Assert.Contains("public static IReadOnlyList<string> GetSupportedProtocolAdapterIds()", source, StringComparison.Ordinal);
        Assert.Contains("public static string BuildUnsupportedProtocolAdapterMessage(string? protocolAdapterId)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeProvidersFolder_ShouldNotKeepProtocolAdapterFactoryFacade()
    {
        var factoryFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Infrastructure",
            "TianShu.AgentRuntime",
            "Runtime",
            "Providers",
            "ProtocolAdapterFactory.cs");

        Assert.False(File.Exists(factoryFile));
    }

    [Fact]
    public void OpenAiProviderProject_ShouldNotReferenceAgentRuntimeProject()
    {
        var providerProjectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Provider",
            "TianShu.Provider.OpenAI",
            "TianShu.Provider.OpenAI.csproj");

        var source = File.ReadAllText(providerProjectFile);

        Assert.DoesNotContain("..\\..\\Infrastructure\\TianShu.AgentRuntime\\TianShu.AgentRuntime.csproj", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CliProject_ShouldCarryOpenAiProvider_AsPackagingOnlyReference()
    {
        var cliProjectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "TianShu.Cli.csproj");

        var source = File.ReadAllText(cliProjectFile);

        Assert.Contains(
            "<ProjectReference Include=\"..\\..\\Provider\\TianShu.Provider.OpenAI\\TianShu.Provider.OpenAI.csproj\" ExcludeAssets=\"compile\" />",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SidecarProject_ShouldCarryOpenAiProvider_AsPackagingOnlyReference()
    {
        var sidecarProjectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Presentations",
            "TianShu.VSSDK.Sidecar",
            "TianShu.VSSDK.Sidecar.csproj");

        var source = File.ReadAllText(sidecarProjectFile);

        Assert.Contains(
            "<ProjectReference Include=\"..\\..\\Provider\\TianShu.Provider.OpenAI\\TianShu.Provider.OpenAI.csproj\" ExcludeAssets=\"compile\" />",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OpenAiProviderProject_ShouldNotKeepRuntimeShapedProtocolAdapterPath()
    {
        var runtimeShapedAdapterFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Provider",
            "TianShu.Provider.OpenAI",
            "Runtime",
            "EndpointAdapters",
            "OpenAiResponsesProtocolAdapter.cs");

        Assert.False(File.Exists(runtimeShapedAdapterFile));
    }

    [Fact]
    public void TianShuTomlConfigurationLoader_CoreSource_DelegatesOptionalProtocolAdapterNormalizationToRegistry()
    {
        var loaderFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Core",
            "TianShu.RuntimeComposition",
            "TianShuTomlConfigurationLoader.cs");

        var source = File.ReadAllText(loaderFile);

        Assert.Contains("ProviderRuntimeBootstrapRegistry.NormalizeOptionalProtocolAdapterId(adapterId, source);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("return ProviderRuntimeBootstrapRegistry.NormalizeProtocolAdapterId(normalized, source);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuTomlConfigurationLoader_CoreSource_DelegatesDefaultProtocolAdapterQueryToRegistry()
    {
        var loaderFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Core",
            "TianShu.RuntimeComposition",
            "TianShuTomlConfigurationLoader.cs");

        var source = File.ReadAllText(loaderFile);

        Assert.Contains("=> ProviderRuntimeBootstrapRegistry.GetDefaultProtocolAdapterId();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("=> ProviderRuntimeBootstrapRegistry.DefaultProtocolAdapterId;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostConfigurationSources_ShouldUseExpectedConfigurationNamespaces()
    {
        var configurationFiles = new[]
        {
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "TianShuSkillRootPaths.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "TianShuProjectRootResolver.cs"),
            Path.Combine(FindRepoRoot(), "src", "Hosting", "TianShu.AppHost.Configuration", "TianShuConfigTomlPathResolver.cs"),
        };

        foreach (var file in configurationFiles)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.AppHost.Configuration;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.AgentRuntime.Configuration;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.Kernel.AppServer;", source, StringComparison.Ordinal);
        }

        var runtimeCompositionFiles = new[]
        {
            Path.Combine(FindRepoRoot(), "src", "Core", "TianShu.RuntimeComposition", "TianShuTomlConfigurationLoader.cs"),
        };

        foreach (var file in runtimeCompositionFiles)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.RuntimeComposition;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.AppHost.Configuration;", source, StringComparison.Ordinal);
        }

        var neutralConfigurationFiles = new[]
        {
            Path.Combine(FindRepoRoot(), "src", "Core", "TianShu.Configuration", "ResolvedTianShuConfig.cs"),
            Path.Combine(FindRepoRoot(), "src", "Core", "TianShu.Configuration", "ResolvedTianShuConfigLayer.cs"),
            Path.Combine(FindRepoRoot(), "src", "Core", "TianShu.Configuration", "KernelModelProtocolResolver.cs"),
            Path.Combine(FindRepoRoot(), "src", "Core", "TianShu.Configuration", "TianShuPromptConfigUtilities.cs"),
            Path.Combine(FindRepoRoot(), "src", "Core", "TianShu.Configuration", "TianShuConfigObjectUtilities.cs"),
        };

        foreach (var file in neutralConfigurationFiles)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.Configuration;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.AppHost.Configuration;", source, StringComparison.Ordinal);
        }

        var runtimeCompositionProjectFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Core",
            "TianShu.RuntimeComposition",
            "TianShu.RuntimeComposition.csproj");
        var runtimeCompositionProjectSource = File.ReadAllText(runtimeCompositionProjectFile);
        Assert.DoesNotContain("TianShu.AppHost.Configuration.csproj", runtimeCompositionProjectSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderBootstrapRuntimeSources_ShouldUseProviderAbstractionsNamespace()
    {
        var bootstrapRuntimeFiles = new[]
        {
            Path.Combine(FindRepoRoot(), "src", "Provider", "TianShu.Provider.Abstractions", "BootstrapRuntime", "ProviderRuntimeBootstrapRegistry.cs"),
            Path.Combine(FindRepoRoot(), "src", "Provider", "TianShu.Provider.Abstractions", "BootstrapRuntime", "ProviderRuntimeState.cs"),
        };

        foreach (var file in bootstrapRuntimeFiles)
        {
            var source = File.ReadAllText(file);

            Assert.Contains("namespace TianShu.Provider.Abstractions;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace TianShu.AgentRuntime.Runtime.Providers;", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DefaultModelRouter_ShouldNotReadCompatibilityConfigDirectly()
    {
        var routerPath = Path.Combine(
            FindRepoRoot(),
            "src",
            "Execution",
            "TianShu.Execution.Runtime",
            "ModelRouting",
            "DefaultModelRouter.cs");
        var source = File.ReadAllText(routerPath);

        Assert.Contains("StructuredValue Config", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityReaders", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<string, object?> Config", source, StringComparison.Ordinal);
    }

    [Fact]
    public void KernelModelProtocolResolver_ShouldNotReadCompatibilityConfigDirectly()
    {
        var resolverPath = Path.Combine(
            FindRepoRoot(),
            "src",
            "Core",
            "TianShu.Configuration",
            "KernelModelProtocolResolver.cs");
        var source = File.ReadAllText(resolverPath);

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TianShuModelRouteSetDefaults_ShouldNotReadCompatibilityConfigDirectly()
    {
        var routeSetDefaultsPath = Path.Combine(
            FindRepoRoot(),
            "src",
            "Core",
            "TianShu.Configuration",
            "TianShuModelRouteSetDefaults.cs");
        var source = File.ReadAllText(routeSetDefaultsPath);

        Assert.DoesNotContain("KernelConfigCompatibilityReaders", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderAbstractions_ShouldNotKeepAgentRuntimeShapedBootstrapFileNames()
    {
        var repoRoot = FindRepoRoot();
        var executionDirectory = Path.Combine(repoRoot, "src", "Provider", "TianShu.Provider.Abstractions", "Execution");
        var bootstrapRuntimeDirectory = Path.Combine(repoRoot, "src", "Provider", "TianShu.Provider.Abstractions", "BootstrapRuntime");

        Assert.False(File.Exists(Path.Combine(executionDirectory, "IAgentRuntimeProviderBootstrap.cs")));
        Assert.False(File.Exists(Path.Combine(executionDirectory, "AgentRuntimeProviderCliArguments.cs")));
        Assert.False(File.Exists(Path.Combine(bootstrapRuntimeDirectory, "AgentRuntimeProviderBootstrapRegistry.cs")));
        Assert.False(File.Exists(Path.Combine(bootstrapRuntimeDirectory, "AgentRuntimeProviderRuntimeState.cs")));
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
