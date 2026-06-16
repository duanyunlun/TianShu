using TianShu.AppHost;
using TianShu.AppHost.Configuration;
using TianShu.AppHost.Tools.Runtime;
using TianShu.AppHost.Tools;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using System.Text.Json;
using System.Text.Json.Nodes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TianShu.Provider.Abstractions;
using TianShu.Provider.OpenAI;
using TianShu.Tools.Artifacts;
using TianShu.Tools.Code;
using TianShu.Tools.Collaboration;
using TianShu.Tools.Fanout;
using TianShu.Tools.FileSystem;
using TianShu.Tools.FileSystemMutating;
using TianShu.Tools.Interaction;
using TianShu.Tools.Memory;
using TianShu.Tools.McpResources;
using TianShu.Tools.Search;
using TianShu.Tools.Shell;

namespace TianShu.AppHost.Tests;

public sealed class KernelToolHandlerAbstractionTests
{
    [Fact]
    public void InternalRuntimeEndpoint_ShouldExposeToolMetadataWithoutSharedHandlerBase()
    {
        IKernelToolHandler handler = new KernelTestSyncRuntimeEndpoint();

        Assert.False(handler is KernelToolHandlerBase);
        Assert.Equal("test_sync_tool", handler.Name);
        Assert.False(string.IsNullOrWhiteSpace(handler.Description));
        Assert.Equal(JsonValueKind.Object, handler.InputSchema.ValueKind);
    }

    [Fact]
    public void ToolPackageManifestLoader_WhenConfiguredProviderVersionIncompatible_MarksManifestUnavailable()
    {
        var manifests = KernelToolPackageManifestLoader.LoadManifests(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tool_providers.future.enabled"] = "true",
                ["tool_providers.future.type"] = "assembly",
                ["tool_providers.future.assembly_path"] = "./future.dll",
                ["tool_providers.future.provider_type"] = "Example.FutureToolProvider",
                ["tool_providers.future.min_tianshu_version"] = "99.0.0",
            },
            workspacePath: null);

        var manifest = Assert.Single(manifests, static item => string.Equals(item.Id, "future", StringComparison.OrdinalIgnoreCase));
        Assert.False(manifest.Enabled);
        Assert.Equal(KernelToolPackageManifest.LoadStatusUnavailable, manifest.LoadStatus);
        Assert.Contains("需要 TianShu >=", manifest.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolRegistry_ShouldExposeDescriptorsFromAbstractHandlers()
    {
        var registry = new KernelToolRegistry();
        ToolProviderTestAdapters.RegisterFileSystemProviderTools(registry);
        ToolProviderTestAdapters.RegisterMutatingFileSystemProviderTools(registry);
        registry.Register(new OutputSchemaToolHandler());

        var descriptors = registry.BuildDescriptorList();
        Assert.Equal(8, descriptors.Count);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(descriptors));
        foreach (var tool in json.RootElement.EnumerateArray())
        {
            Assert.True(tool.TryGetProperty("supportsParallelToolCalls", out var supportsParallel));
            Assert.True(supportsParallel.ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.True(tool.TryGetProperty("implementationBinding", out var implementationBinding));
            Assert.True(implementationBinding.TryGetProperty("ImplementationKind", out var implementationKind));
            Assert.Equal(JsonValueKind.Number, implementationKind.ValueKind);
        }

        var grepDescriptor = descriptors.Single(static descriptor =>
            string.Equals(
                descriptor.GetType().GetProperty("name")?.GetValue(descriptor) as string,
                "grep",
                StringComparison.Ordinal));
        var grepBinding = Assert.IsType<ToolImplementationBinding>(
            grepDescriptor.GetType().GetProperty("implementationBinding")?.GetValue(grepDescriptor));
        Assert.Equal(ToolImplementationKind.Managed, grepBinding.ImplementationKind);

        var outputSchemaDescriptor = descriptors.Single(static descriptor =>
            string.Equals(
                descriptor.GetType().GetProperty("name")?.GetValue(descriptor) as string,
                "output_schema_tool",
                StringComparison.Ordinal));
        var outputSchema = outputSchemaDescriptor.GetType().GetProperty("outputSchema")?.GetValue(outputSchemaDescriptor);
        Assert.NotNull(outputSchema);
    }

    [Fact]
    public async Task MemorySearchToolHandler_ShouldReturnRankedMemoryFacts()
    {
        var handler = CreateProviderHandler(new MemoryToolProvider(), "memory_search");
        var currentWorkspace = new MemorySpaceId("memory:workspace:d/gitrepos/personal/tianshu");
        var otherWorkspace = new MemorySpaceId("memory:workspace:d/gitrepos/work/wpf");
        var context = CreateProviderContext(
            @"D:\Work\TianShu",
            new TestMemoryToolServices(
                FilterMemory: (_, _) => Task.FromResult(new MemoryQueryResult(
                [
                    new FactMemoryRecord("workspace.rule", StructuredValue.FromString("TianShu WPF HTTP memory tool"), currentWorkspace),
                    new FactMemoryRecord("wpf.http.pattern", StructuredValue.FromString("WPF HTTP tester pattern"), otherWorkspace),
                    new FactMemoryRecord("sql.archive", StructuredValue.FromString("SQL archive"), otherWorkspace),
                ]))));

        var result = await handler.InvokeAsync(
            CreateProviderRequest("memory_search", new { query = "WPF HTTP", limit = 4 }),
            context,
            CancellationToken.None);

        using var document = ParseProviderPayload(result);
        var records = document.RootElement.GetProperty("records").EnumerateArray().ToArray();
        Assert.Equal(2, records.Length);
        Assert.Equal("workspace.rule", records[0].GetProperty("key").GetString());
        Assert.Equal("current_workspace", records[0].GetProperty("applicability").GetString());
        Assert.Equal("wpf.http.pattern", records[1].GetProperty("key").GetString());
        Assert.Equal("transfer_candidate", records[1].GetProperty("applicability").GetString());
    }

    [Fact]
    public async Task MemoryExplainOverlayToolHandler_ShouldExposeOverlayApplicability()
    {
        var handler = CreateProviderHandler(new MemoryToolProvider(), "memory_explain_overlay");
        var context = CreateProviderContext(
            @"D:\Work\TianShu",
            new TestMemoryToolServices(
                ResolveMemoryOverlay: (_, _) => Task.FromResult(new MemoryOverlay(
                [
                    new FactMemoryRecord(
                        "workspace.rule",
                        StructuredValue.FromString("TianShu rule"),
                        new MemorySpaceId("memory:workspace:d/gitrepos/personal/tianshu")),
                ]))));

        var result = await handler.InvokeAsync(
            CreateProviderRequest("memory_explain_overlay", new { query = "TianShu" }),
            context,
            CancellationToken.None);

        using var document = ParseProviderPayload(result);
        Assert.Equal("Applied", document.RootElement.GetProperty("mergeDecision").GetString());
        var fact = Assert.Single(document.RootElement.GetProperty("facts").EnumerateArray());
        Assert.Equal("current_workspace", fact.GetProperty("applicability").GetString());
    }

    [Fact]
    public void ToolImplementationResolver_ShouldMarkRequiredMissingRequirementUnavailable()
    {
        var resolver = new KernelToolImplementationResolver(
            KernelToolPlatformProfiles.WindowsDesktop(),
            new FixedProbeService(new ToolCapabilityProbe(false, "missing runtime")));
        var handler = new BindingProbeToolHandler(new ToolImplementationBinding(
            "external_only",
            ToolImplementationKind.ExternalProcess,
            implementationId: "missing-runtime",
            requirements:
            [
                new ToolRuntimeRequirement("missing_runtime", "Missing runtime"),
            ]));

        var binding = resolver.Resolve(handler);

        Assert.Equal(ToolImplementationKind.Unavailable, binding.ImplementationKind);
        Assert.Equal("missing runtime", binding.Probe?.Reason);
        Assert.Equal("windows", binding.PlatformProfile?.Platform);
    }

    [Fact]
    public void ToolRegistry_ShouldHideUnavailableHandlersFromProviderToolList()
    {
        var registry = new KernelToolRegistry(new KernelToolImplementationResolver(
            KernelToolPlatformProfiles.WindowsDesktop(),
            new FixedProbeService(new ToolCapabilityProbe(false, "missing runtime"))));
        registry.Register(new BindingProbeToolHandler(new ToolImplementationBinding(
            "external_only",
            ToolImplementationKind.ExternalProcess,
            implementationId: "missing-runtime",
            requirements:
            [
                new ToolRuntimeRequirement("missing_runtime", "Missing runtime"),
            ])));

        var descriptors = registry.BuildDescriptorList();
        var descriptor = Assert.Single(descriptors);
        var descriptorBinding = Assert.IsType<ToolImplementationBinding>(
            descriptor.GetType().GetProperty("implementationBinding")?.GetValue(descriptor));
        Assert.Equal(ToolImplementationKind.Unavailable, descriptorBinding.ImplementationKind);

        var providerTools = registry.BuildProviderResponsesToolList();
        Assert.Empty(providerTools);
    }

    [Fact]
    public void ToolRegistry_ShouldApplyPlatformProfileBeforeProviderToolVisibility()
    {
        var registry = new KernelToolRegistry(new KernelToolImplementationResolver(
            KernelToolPlatformProfiles.BrowserHosted(),
            new FixedProbeService(new ToolCapabilityProbe(true))));
        RegisterProviderTools(registry, new ShellToolProvider());

        var descriptor = registry.BuildDescriptorList().Single(static descriptor =>
            string.Equals(
                descriptor.GetType().GetProperty("name")?.GetValue(descriptor) as string,
                "shell_command",
                StringComparison.Ordinal));
        var descriptorBinding = Assert.IsType<ToolImplementationBinding>(
            descriptor.GetType().GetProperty("implementationBinding")?.GetValue(descriptor));
        Assert.Equal(ToolImplementationKind.Unavailable, descriptorBinding.ImplementationKind);
        Assert.Contains("disabled by platform profile", descriptorBinding.Probe?.Reason, StringComparison.Ordinal);

        var providerTools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            ShellToolType: KernelShellToolType.ShellCommand));
        Assert.Empty(providerTools);
    }

    [Fact]
    public void ToolRegistry_ShouldExposeResolvedToolCatalogWithVisibilityAndProbeReason()
    {
        var registry = new KernelToolRegistry(new KernelToolImplementationResolver(
            KernelToolPlatformProfiles.WindowsDesktop(),
            new FixedProbeService(new ToolCapabilityProbe(false, "missing runtime"))));
        registry.Register(new BindingProbeToolHandler(new ToolImplementationBinding(
            "external_only",
            ToolImplementationKind.ExternalProcess,
            implementationId: "missing-runtime",
            requirements:
            [
                new ToolRuntimeRequirement("missing_runtime", "Missing runtime"),
            ])));

        var catalog = registry.BuildResolvedToolCatalog();

        var item = Assert.Single(catalog.Items);
        Assert.Equal("external_only", item.Name);
        Assert.Equal(ToolImplementationKind.Unavailable, item.ImplementationKind);
        Assert.False(item.Available);
        Assert.False(item.ModelVisible);
        Assert.Equal("missing runtime", item.Reason);
        Assert.Equal("missing_runtime", Assert.Single(item.Requirements).Key);
        Assert.Equal("windows", item.PlatformProfile?.Platform);
    }

    [Fact]
    public void ToolRegistry_ShouldHideInternalModelToolsFromResolvedCatalogUnlessRequested()
    {
        var registry = new KernelToolRegistry(new KernelToolImplementationResolver(
            KernelToolPlatformProfiles.WindowsDesktop(),
            new FixedProbeService(new ToolCapabilityProbe(true))));
        RegisterProviderTools(registry, new ShellToolProvider());

        Assert.DoesNotContain(registry.BuildResolvedToolCatalog().Items, static item => item.Name == "exec_command");

        var catalog = registry.BuildResolvedToolCatalog(includeHiddenTools: true);
        var item = Assert.Single(catalog.Items, static item => item.Name == "exec_command");
        Assert.Equal("exec_command", item.Name);
        Assert.True(item.Available);
        Assert.False(item.ModelVisible);
    }

    [Fact]
    public void ToolRegistry_ShouldPreserveOptionalRequirementFallbackReasonInResolvedCatalog()
    {
        var registry = new KernelToolRegistry(new KernelToolImplementationResolver(
            KernelToolPlatformProfiles.WindowsDesktop(),
            new FixedProbeService(new ToolCapabilityProbe(true, "rg unavailable"))));
        registry.Register(new BindingProbeToolHandler(new ToolImplementationBinding(
            "external_only",
            ToolImplementationKind.Managed,
            implementationId: "managed-search",
            requirements:
            [
                new ToolRuntimeRequirement("rg", "ripgrep", required: false),
            ],
            fallbackPolicy: new ToolFallbackPolicy(
                "managed-default",
                [ToolImplementationKind.Managed, ToolImplementationKind.ExternalProcess],
                "Managed search remains available when rg is missing."))));

        var item = Assert.Single(registry.BuildResolvedToolCatalog().Items);

        Assert.Equal(ToolImplementationKind.Managed, item.ImplementationKind);
        Assert.True(item.Available);
        Assert.Equal("rg unavailable", item.Reason);
        Assert.False(Assert.Single(item.Requirements).Required);
        Assert.Equal("managed-default", item.FallbackPolicy?.Strategy);
    }

    [Fact]
    public void ToolRegistry_ShouldResolveRepresentativeExternalAndMcpBindings()
    {
        var registry = new KernelToolRegistry(new KernelToolImplementationResolver(
            KernelToolPlatformProfiles.WindowsDesktop(),
            new FixedProbeService(new ToolCapabilityProbe(true))));
        RegisterProviderTools(registry, new ShellToolProvider());
        registry.Register(new KernelContractToolHandlerAdapter(
            new McpResourceToolProvider().CreateHandler("list_mcp_resources", new TianShuToolActivationContext())));

        var descriptors = registry.BuildDescriptorList();
        var shellBinding = GetBinding(descriptors, "shell_command");
        var mcpBinding = GetBinding(descriptors, "list_mcp_resources");

        Assert.Equal(ToolImplementationKind.ExternalProcess, shellBinding.ImplementationKind);
        Assert.Equal("tianshu.tools.shell", shellBinding.ImplementationId);
        Assert.Equal(ToolImplementationKind.McpStdio, mcpBinding.ImplementationKind);
        Assert.Equal("tianshu.tools.mcp-resources", mcpBinding.ImplementationId);
    }

    [Fact]
    public void ToolRegistry_ShouldExposeOpenAiResponsesToolDescriptors()
    {
        var registry = new KernelToolRegistry();
        registry.Register(ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("list_dir"));
        registry.Register(ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("read_file"));
        registry.Register(new OutputSchemaToolHandler());

        var tools = registry.BuildProviderResponsesToolList();
        Assert.Equal(3, tools.Count);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        foreach (var tool in json.RootElement.EnumerateArray())
        {
            Assert.Equal("function", tool.GetProperty("type").GetString());
            Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString()));
            Assert.False(tool.GetProperty("strict").GetBoolean());
            Assert.Equal(JsonValueKind.Object, tool.GetProperty("parameters").ValueKind);
        }

        var outputSchemaTool = json.RootElement
            .EnumerateArray()
            .Single(static tool => string.Equals(tool.GetProperty("name").GetString(), "output_schema_tool", StringComparison.Ordinal));
        Assert.Equal("object", outputSchemaTool.GetProperty("output_schema").GetProperty("type").GetString());
    }

    [Fact]
    public void ToolRegistry_ShouldExposeParallelToolSupportLookup_AndAliases()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new MutatingFileSystemToolProvider());
        registry.Register(ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("list_dir"));

        Assert.False(registry.ToolSupportsParallelToolCalls("apply_patch"));
        Assert.True(registry.ToolSupportsParallelToolCalls("list_dir"));
        Assert.False(registry.ToolSupportsParallelToolCalls("missing_tool"));

        registry.RegisterMany(new[] { "alias_one", "alias_two" }, ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("list_dir"));
        Assert.True(registry.TryGet("alias_one", out var handlerOne));
        Assert.True(registry.TryGet("alias_two", out var handlerTwo));
        Assert.NotNull(handlerOne);
        Assert.NotNull(handlerTwo);
        Assert.Equal(handlerOne!.Name, handlerTwo!.Name);
    }

    [Fact]
    public void ToolRegistryBuilder_ShouldRejectDuplicateToolKeysByDefault()
    {
        var builder = new KernelToolRegistryBuilder();
        builder.AddTool(ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("list_dir"));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddTool(ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("list_dir")));
        Assert.Contains("list_dir", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolRegistryBuilder_ShouldAddNewToolToCatalogSnapshot()
    {
        var registry = new KernelToolRegistryBuilder()
            .AddTool(new NamedBindingToolHandler("company_search", "company-search"))
            .Build();

        var catalogItem = Assert.Single(registry.BuildResolvedToolCatalog().Items);
        Assert.Equal("company_search", catalogItem.Name);
        Assert.Equal("company-search", catalogItem.ImplementationId);
        Assert.True(catalogItem.Available);
    }

    [Fact]
    public void ToolRegistryBuilder_ShouldReplaceToolWhenExplicitlyRequested()
    {
        var registry = new KernelToolRegistryBuilder()
            .AddTool(new NamedBindingToolHandler("list_dir", "first-list-dir"))
            .ReplaceTool("list_dir", new NamedBindingToolHandler("list_dir", "replacement-list-dir"))
            .Build();

        Assert.True(registry.TryGet("list_dir", out var handler));
        Assert.Equal("replacement-list-dir", handler!.ImplementationBinding.ImplementationId);

        var catalogItem = Assert.Single(registry.BuildResolvedToolCatalog().Items);
        Assert.Equal("list_dir", catalogItem.Name);
        Assert.Equal("replacement-list-dir", catalogItem.ImplementationId);
    }

    [Fact]
    public void ToolRegistryBuilder_ShouldDisableToolsBeforeInternalRuntimeToolSetRegistration()
    {
        var registry = new KernelToolRegistryBuilder()
            .DisableTool("test_sync_tool")
            .AddToolSet(new KernelInternalRuntimeToolSet())
            .Build();

        Assert.False(registry.TryGet("test_sync_tool", out _));
        Assert.DoesNotContain(registry.BuildResolvedToolCatalog().Items, static item => item.Name == "test_sync_tool");
    }

    [Fact]
    public void DefaultToolSet_ShouldPreserveDefaultFactoryAliasAndCatalog()
    {
        var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

        Assert.True(registry.TryGet("shell", out var shellHandler));
        Assert.True(registry.TryGet("container.exec", out var aliasHandler));
        Assert.Same(shellHandler, aliasHandler);

        var toolNames = registry.BuildResolvedToolCatalog(includeHiddenTools: true).Items
            .Select(static item => item.Name)
            .ToArray();
        Assert.Contains("shell", toolNames);
        Assert.Contains("exec_command", toolNames);
        Assert.Contains("write_stdin", toolNames);
    }

    [Fact]
    public void DefaultToolSet_ConfigExportTemplate_ShouldMatchResolvedCatalog()
    {
        var registry = KernelToolRegistryFactory.CreateDefaultRegistry();
        var catalog = registry.BuildResolvedToolCatalog(includeHiddenTools: true);
        var toml = TianShuToolProfileTomlExporter.ExportBuiltinProfileToml(catalog);

        foreach (var item in catalog.Items)
        {
            Assert.Contains($"[tools.{item.Name}]", toml, StringComparison.Ordinal);
            Assert.Contains($"implementation_kind = \"{item.ImplementationKind.ToString().ToLowerInvariant()}\"", toml, StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(item.ImplementationId))
            {
                Assert.Contains($"implementation_id = \"{item.ImplementationId}\"", toml, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void BuiltinToolPackageManifest_ShouldDriveDefaultToolSetRegistration()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-builtin-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            WriteBuiltinToolPackageManifest(Path.Combine(tempHome, "modules", "tools", "packages", "builtin"), "shell");

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

            Assert.True(registry.TryGet("shell_command", out _));
            Assert.False(registry.TryGet("read_file", out _));
            Assert.False(registry.TryGet("update_plan", out _));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public void BuiltinToolPackageManifest_ShouldIgnoreLegacyToolsDirectoryAndUseEmbeddedDefault()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-legacy-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            WriteBuiltinToolPackageManifest(Path.Combine(tempHome, "Tools", "builtin"), "shell");

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

            Assert.True(registry.TryGet("shell_command", out _));
            Assert.True(registry.TryGet("read_file", out _));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public void BuiltinToolPackageManifest_ShouldReadModuleToolsDirectoryBeforeLegacyRoots()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-prefer-lower-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            WriteBuiltinToolPackageManifest(Path.Combine(tempHome, "Tools", "builtin"), "filesystem");
            WriteBuiltinToolPackageManifest(Path.Combine(tempHome, "modules", "tools", "packages", "builtin"), "shell");

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

            Assert.True(registry.TryGet("shell_command", out _));
            Assert.False(registry.TryGet("read_file", out _));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public void BuiltinToolPackageManifest_ShouldNotRegisterRuntimeToolSetsWithoutProviderEntries()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-no-provider-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var builtinDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "builtin");
            Directory.CreateDirectory(builtinDirectory);
            File.WriteAllText(
                Path.Combine(builtinDirectory, "tool.toml"),
                """
                id = "builtin"
                enabled = true
                type = "builtin"
                priority = 0

                [tool_sets]
                enabled = ["shell"]
                disabled = []
                """);

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

            Assert.False(registry.TryGet("shell_command", out _));
            Assert.False(registry.TryGet("read_file", out _));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public void BuiltinToolPackageManifest_ShouldNotRegisterRuntimeFallbackWhenProviderIsLoadable()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-provider-no-fallback-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var builtinDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "builtin");
            Directory.CreateDirectory(builtinDirectory);
            var relativeAssemblyPath = Path
                .GetRelativePath(builtinDirectory, typeof(ShellToolProvider).Assembly.Location)
                .Replace('\\', '/');
            File.WriteAllText(
                Path.Combine(builtinDirectory, "tool.toml"),
                $$"""
                id = "builtin"
                enabled = true
                type = "builtin"
                priority = 0

                [tool_sets]
                enabled = ["shell"]
                disabled = []

                [[providers]]
                id = "shell"
                enabled = true
                type = "assembly"
                assembly_path = "{{relativeAssemblyPath}}"
                provider_type = "{{typeof(ShellToolProvider).FullName}}"
                priority = 10
                replace_existing = true
                """);

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

            Assert.True(registry.TryGet("shell", out var shellHandler));
            Assert.True(registry.TryGet("container.exec", out var aliasHandler));
            Assert.Same(shellHandler, aliasHandler);
            Assert.IsType<KernelContractToolHandlerAdapter>(shellHandler);
            Assert.Equal("tianshu.tools.shell", shellHandler!.ImplementationBinding.ImplementationId);
            Assert.True(registry.TryGet("exec_command", out var execHandler));
            Assert.Equal("tianshu.tools.shell", execHandler!.ImplementationBinding.ImplementationId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public void ToolPackageManifest_ShouldResolveAssemblyPathRelativeToPackageDirectory()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-assembly-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var packageDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "contract-echo");
            Directory.CreateDirectory(packageDirectory);
            var relativeAssemblyPath = Path
                .GetRelativePath(packageDirectory, typeof(TestContractToolProvider).Assembly.Location)
                .Replace('\\', '/');
            File.WriteAllText(
                Path.Combine(packageDirectory, "tool.toml"),
                $$"""
                id = "contract-echo"
                enabled = true
                type = "assembly"
                assembly_path = "{{relativeAssemblyPath}}"
                provider_type = "{{typeof(TestContractToolProvider).FullName}}"
                priority = 10
                """);

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

            Assert.True(registry.TryGet("contract_echo", out _));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public async Task BuiltinToolPackageManifest_ShouldLoadProviderArrayAndReplaceFallbackTool()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-provider-array-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var builtinDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "builtin");
            Directory.CreateDirectory(builtinDirectory);
            var relativeAssemblyPath = Path
                .GetRelativePath(builtinDirectory, typeof(SearchToolProvider).Assembly.Location)
                .Replace('\\', '/');
            File.WriteAllText(
                Path.Combine(builtinDirectory, "tool.toml"),
                $$"""
                id = "builtin"
                enabled = true
                type = "builtin"
                priority = 0

                [tool_sets]
                enabled = ["search"]
                disabled = []

                [[providers]]
                id = "search"
                enabled = true
                type = "assembly"
                assembly_path = "{{relativeAssemblyPath}}"
                provider_type = "{{typeof(SearchToolProvider).FullName}}"
                priority = 10
                replace_existing = true
                """);

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

            Assert.True(registry.TryGet("tool_search", out var handler));
            Assert.Equal("tianshu.tools.search", handler!.ImplementationBinding.ImplementationId);
            Assert.True(registry.TryGet("tool_suggest", out var suggestHandler));
            Assert.Equal("tianshu.tools.search", suggestHandler!.ImplementationBinding.ImplementationId);

            var result = await handler.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { query = "calendar event", limit = 5 }),
                new KernelToolCallContext(
                    "thread-search",
                    "turn-search",
                    Environment.CurrentDirectory,
                    DynamicTools:
                    [
                        new KernelDynamicToolDescriptor(
                            "mcp__calendar__create_event",
                            "create_event",
                            "mcp__calendar",
                            "Create calendar events.",
                            "Create event",
                            "calendar",
                            "Calendar",
                            "Calendar connector.",
                            "calendar",
                            JsonSerializer.SerializeToElement(new
                            {
                                type = "object",
                                properties = new
                                {
                                    title = new { type = "string" },
                                },
                            }),
                            OutputSchema: null,
                            Meta: null,
                            Annotations: null),
                    ]),
                CancellationToken.None);

            Assert.True(result.Success);
            using var json = JsonDocument.Parse(result.OutputText);
            var toolNamespace = Assert.Single(json.RootElement.GetProperty("tools").EnumerateArray());
            Assert.Equal("namespace", toolNamespace.GetProperty("type").GetString());
            Assert.Equal("mcp__calendar", toolNamespace.GetProperty("name").GetString());
            var deferredTool = Assert.Single(toolNamespace.GetProperty("tools").EnumerateArray());
            Assert.Equal("create_event", deferredTool.GetProperty("name").GetString());
            Assert.True(deferredTool.GetProperty("defer_loading").GetBoolean());

            var connector = new KernelToolSuggestConnectorInfo(
                "connector_2128aebfecb84f64a069897515042a44",
                "Google Calendar",
                "Plan events and schedules.",
                "https://chatgpt.com/apps/google-calendar/connector_2128aebfecb84f64a069897515042a44");
            McpServerElicitationRequest? captured = null;
            var suggestResult = await suggestHandler.ExecuteAsync(
                JsonSerializer.SerializeToElement(new
                {
                    tool_type = "connector",
                    action_type = "install",
                    tool_id = connector.Id,
                    suggest_reason = "Plan and reference events from your calendar",
                }),
                new KernelToolCallContext(
                    "thread-search",
                    "turn-search",
                    Environment.CurrentDirectory,
                    RuntimeServices: new KernelToolRuntimeServices(
                        ListToolSuggestDiscoverableConnectors: _ => Task.FromResult<IReadOnlyList<KernelToolSuggestConnectorInfo>>([connector]),
                        RefreshOpenAiAppsToolSnapshot: _ => Task.FromResult<KernelOpenAiAppsToolSnapshot?>(new KernelOpenAiAppsToolSnapshot(null, [connector]))),
                    McpServerElicitationRequester: (request, _) =>
                    {
                        captured = request;
                        return Task.FromResult(new McpServerElicitationResponse("accept", null));
                    }),
                CancellationToken.None);

            Assert.True(suggestResult.Success);
            Assert.NotNull(captured);
            Assert.Contains("Google Calendar could help with this request.", captured!.Message, StringComparison.Ordinal);
            using var suggestJson = JsonDocument.Parse(suggestResult.OutputText);
            Assert.True(suggestJson.RootElement.GetProperty("completed").GetBoolean());
            Assert.True(suggestJson.RootElement.GetProperty("user_confirmed").GetBoolean());
            Assert.Equal(connector.Id, suggestJson.RootElement.GetProperty("tool_id").GetString());

            var providerTools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
                WebSearchMode: null,
                ImageGenerationEnabled: false,
                SearchToolEnabled: true,
                ToolSuggestEnabled: true,
                ToolSuggestDiscoverableConnectors: [connector]));
            using var providerToolsJson = JsonDocument.Parse(JsonSerializer.Serialize(providerTools));
            var providerSuggestTool = providerToolsJson.RootElement.EnumerateArray().Single(static tool =>
                tool.TryGetProperty("name", out var name)
                && string.Equals(name.GetString(), "tool_suggest", StringComparison.Ordinal));
            Assert.Contains("Google Calendar", providerSuggestTool.GetProperty("description").GetString(), StringComparison.Ordinal);
            Assert.Contains(connector.Id, providerSuggestTool.GetProperty("parameters").GetProperty("properties").GetProperty("tool_id").GetProperty("description").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public async Task BuiltinToolPackageManifest_ShouldLoadShellProviderArrayAndReplaceFallbackTools()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-shell-provider-array-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var builtinDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "builtin");
            Directory.CreateDirectory(builtinDirectory);
            var relativeAssemblyPath = Path
                .GetRelativePath(builtinDirectory, typeof(ShellToolProvider).Assembly.Location)
                .Replace('\\', '/');
            File.WriteAllText(
                Path.Combine(builtinDirectory, "tool.toml"),
                $$"""
                id = "builtin"
                enabled = true
                type = "builtin"
                priority = 0

                [tool_sets]
                enabled = ["shell"]
                disabled = []

                [[providers]]
                id = "shell"
                enabled = true
                type = "assembly"
                assembly_path = "{{relativeAssemblyPath}}"
                provider_type = "{{typeof(ShellToolProvider).FullName}}"
                priority = 10
                replace_existing = true
                """);

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

            Assert.True(registry.TryGet("shell", out var shell));
            Assert.True(registry.TryGet("local_shell", out var localShell));
            Assert.True(registry.TryGet("shell_command", out var shellCommand));
            Assert.True(registry.TryGet("exec_command", out var execCommand));
            Assert.True(registry.TryGet("write_stdin", out var writeStdin));
            Assert.Equal("tianshu.tools.shell", shell!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.shell", localShell!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.shell", shellCommand!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.shell", execCommand!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.shell", writeStdin!.ImplementationBinding.ImplementationId);

            var result = await shellCommand.ExecuteAsync(
                JsonSerializer.SerializeToElement(new
                {
                    command = OperatingSystem.IsWindows()
                        ? "Write-Output shell-provider"
                        : "printf 'shell-provider\\n'",
                    timeout_ms = 30_000,
                }),
                new KernelToolCallContext(
                    "thread-shell",
                    "turn-shell",
                    Environment.CurrentDirectory,
                    ItemId: "item-shell"),
                CancellationToken.None);

            Assert.True(result.Success, result.OutputText);
            Assert.Contains("shell-provider", result.OutputText, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public async Task BuiltinToolPackageManifest_ShouldLoadInteractionProviderArrayAndReplaceFallbackTools()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-interaction-provider-array-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var builtinDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "builtin");
            Directory.CreateDirectory(builtinDirectory);
            var relativeAssemblyPath = Path
                .GetRelativePath(builtinDirectory, typeof(InteractionToolProvider).Assembly.Location)
                .Replace('\\', '/');
            File.WriteAllText(
                Path.Combine(builtinDirectory, "tool.toml"),
                $$"""
                id = "builtin"
                enabled = true
                type = "builtin"
                priority = 0

                [tool_sets]
                enabled = ["collaboration"]
                disabled = []

                [[providers]]
                id = "interaction"
                enabled = true
                type = "assembly"
                assembly_path = "{{relativeAssemblyPath}}"
                provider_type = "{{typeof(InteractionToolProvider).FullName}}"
                priority = 10
                replace_existing = true
                """);

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

            Assert.True(registry.TryGet("request_user_input", out var requestUserInput));
            Assert.True(registry.TryGet("request_permissions", out var requestPermissions));
            Assert.Equal("tianshu.tools.interaction", requestUserInput!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.interaction", requestPermissions!.ImplementationBinding.ImplementationId);

            var userInputResult = await requestUserInput.ExecuteAsync(
                JsonSerializer.SerializeToElement(new
                {
                    questions = new[]
                    {
                        new
                        {
                            id = "choice",
                            header = "选择",
                            question = "请选择一个选项。",
                            options = new[]
                            {
                                new { label = "继续", description = "继续当前操作。" },
                                new { label = "取消", description = "取消当前操作。" },
                            },
                        },
                    },
                }),
                new KernelToolCallContext(
                    "thread-interaction",
                    "turn-interaction",
                    Environment.CurrentDirectory,
                    ItemId: "item-input",
                    CollaborationMode: KernelCollaborationModeState.CreateDefault("test-model"),
                    DefaultModeRequestUserInputEnabled: true,
                    UserInputRequester: (request, _) =>
                    {
                        Assert.Equal("item-input", request.ItemId);
                        return Task.FromResult(new KernelRequestUserInputResponse(
                            new Dictionary<string, KernelRequestUserInputAnswer>(StringComparer.Ordinal)
                            {
                                ["choice"] = new(["继续"]),
                            }));
                    }),
                CancellationToken.None);

            Assert.True(userInputResult.Success, userInputResult.OutputText);
            using (var userInputJson = JsonDocument.Parse(userInputResult.OutputText))
            {
                var choiceAnswer = Assert.Single(userInputJson.RootElement
                    .GetProperty("answers")
                    .GetProperty("choice")
                    .GetProperty("answers")
                    .EnumerateArray());
                Assert.Equal("继续", choiceAnswer.GetString());
            }

            var permissionResult = await requestPermissions.ExecuteAsync(
                JsonSerializer.SerializeToElement(new
                {
                    reason = "需要读取工作区。",
                    permissions = new
                    {
                        file_system = new
                        {
                            read = new[] { "." },
                        },
                    },
                }),
                new KernelToolCallContext(
                    "thread-interaction",
                    "turn-interaction",
                    Environment.CurrentDirectory,
                    ItemId: "item-permission",
                    RequestPermissionsEnabled: true,
                    PermissionRequester: (request, _) =>
                    {
                        Assert.Equal("item-permission", request.ItemId);
                        return Task.FromResult(new KernelRequestPermissionsResponse(
                            request.Permissions,
                            KernelPermissionGrantScope.Turn));
                    }),
                CancellationToken.None);

            Assert.True(permissionResult.Success, permissionResult.OutputText);
            using (var permissionsJson = JsonDocument.Parse(permissionResult.OutputText))
            {
                Assert.Equal("turn", permissionsJson.RootElement.GetProperty("scope").GetString());
                Assert.Equal(JsonValueKind.Object, permissionsJson.RootElement.GetProperty("permissions").ValueKind);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public async Task BuiltinToolPackageManifest_ShouldLoadCollaborationProviderArrayAndReplaceFallbackTools()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-collaboration-provider-array-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var builtinDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "builtin");
            Directory.CreateDirectory(builtinDirectory);
            var relativeAssemblyPath = Path
                .GetRelativePath(builtinDirectory, typeof(CollaborationToolProvider).Assembly.Location)
                .Replace('\\', '/');
            File.WriteAllText(
                Path.Combine(builtinDirectory, "tool.toml"),
                $$"""
                id = "builtin"
                enabled = true
                type = "builtin"
                priority = 0

                [tool_sets]
                enabled = ["collaboration"]
                disabled = []

                [[providers]]
                id = "collaboration"
                enabled = true
                type = "assembly"
                assembly_path = "{{relativeAssemblyPath}}"
                provider_type = "{{typeof(CollaborationToolProvider).FullName}}"
                priority = 10
                replace_existing = true
                """);

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();
            var toolNames = new[]
            {
                "update_plan",
                "spawn_agent",
                "send_input",
                "resume_agent",
                "wait",
                "close_agent",
            };
            foreach (var toolName in toolNames)
            {
                Assert.True(registry.TryGet(toolName, out var tool));
                Assert.Equal("tianshu.tools.collaboration", tool!.ImplementationBinding.ImplementationId);
            }

            var planRequests = new List<KernelPlanUpdateRequest>();
            var runtimeServices = new KernelToolRuntimeServices(
                UpdatePlan: (request, _) =>
                {
                    planRequests.Add(request);
                    return Task.CompletedTask;
                },
                SpawnAgent: (request, _) =>
                {
                    Assert.Equal("spawn-call", request.ParentCallId);
                    Assert.Equal("整理测试资料", request.Message);
                    Assert.Equal("worker", request.AgentType);
                    return Task.FromResult(new KernelSpawnAgentResponse("agent-1", "worker-a"));
                },
                SendInputToAgent: (request, _) =>
                {
                    Assert.Equal("agent-1", request.Id);
                    Assert.Equal("继续", request.Message);
                    return Task.FromResult(new KernelSendInputResponse("submission-1"));
                },
                ResumeAgent: (id, _) =>
                {
                    Assert.Equal("agent-1", id);
                    return Task.FromResult<JsonNode?>(JsonNode.Parse("""{"completed":"resumed"}"""));
                },
                WaitOnAgents: (ids, timeoutMs, _) =>
                {
                    Assert.Equal(["agent-1"], ids);
                    Assert.Equal(10, timeoutMs);
                    return Task.FromResult(new KernelWaitAgentsResponse(
                        new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
                        {
                            ["agent-1"] = JsonNode.Parse("""{"completed":"done"}"""),
                        },
                        TimedOut: false));
                },
                CloseAgent: (id, _) =>
                {
                    Assert.Equal("agent-1", id);
                    return Task.FromResult<JsonNode?>(JsonNode.Parse("""{"completed":"closed"}"""));
                });

            var context = new KernelToolCallContext(
                "thread-collaboration",
                "turn-collaboration",
                Environment.CurrentDirectory,
                RuntimeServices: runtimeServices,
                ExternalCallId: "spawn-call");

            var updatePlan = registry.TryGet("update_plan", out var updatePlanTool) ? updatePlanTool! : throw new InvalidOperationException();
            var planResult = await updatePlan.ExecuteAsync(
                JsonSerializer.SerializeToElement(new
                {
                    plan = new[]
                    {
                        new { step = "实现 Provider", status = "in_progress" },
                    },
                }),
                context,
                CancellationToken.None);
            Assert.True(planResult.Success, planResult.OutputText);
            Assert.Single(planRequests);

            var spawnAgent = registry.TryGet("spawn_agent", out var spawnAgentTool) ? spawnAgentTool! : throw new InvalidOperationException();
            var spawnResult = await spawnAgent.ExecuteAsync(
                JsonSerializer.SerializeToElement(new
                {
                    message = "整理测试资料",
                    agent_type = "worker",
                }),
                context,
                CancellationToken.None);
            Assert.True(spawnResult.Success, spawnResult.OutputText);
            using (var spawnJson = JsonDocument.Parse(spawnResult.OutputText))
            {
                Assert.Equal("agent-1", spawnJson.RootElement.GetProperty("agent_id").GetString());
                Assert.Equal("worker-a", spawnJson.RootElement.GetProperty("nickname").GetString());
            }

            var sendInput = registry.TryGet("send_input", out var sendInputTool) ? sendInputTool! : throw new InvalidOperationException();
            var sendResult = await sendInput.ExecuteAsync(
                JsonSerializer.SerializeToElement(new
                {
                    id = "agent-1",
                    message = "继续",
                }),
                context,
                CancellationToken.None);
            Assert.True(sendResult.Success, sendResult.OutputText);
            using (var sendJson = JsonDocument.Parse(sendResult.OutputText))
            {
                Assert.Equal("submission-1", sendJson.RootElement.GetProperty("submission_id").GetString());
            }

            var resumeAgent = registry.TryGet("resume_agent", out var resumeAgentTool) ? resumeAgentTool! : throw new InvalidOperationException();
            var resumeResult = await resumeAgent.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { id = "agent-1" }),
                context,
                CancellationToken.None);
            Assert.True(resumeResult.Success, resumeResult.OutputText);
            Assert.Contains("resumed", resumeResult.OutputText, StringComparison.Ordinal);

            var wait = registry.TryGet("wait", out var waitTool) ? waitTool! : throw new InvalidOperationException();
            var waitResult = await wait.ExecuteAsync(
                JsonSerializer.SerializeToElement(new
                {
                    ids = new[] { "agent-1" },
                    timeout_ms = 10,
                }),
                context,
                CancellationToken.None);
            Assert.True(waitResult.Success, waitResult.OutputText);
            Assert.Contains("\"timed_out\":false", waitResult.OutputText, StringComparison.Ordinal);

            var closeAgent = registry.TryGet("close_agent", out var closeAgentTool) ? closeAgentTool! : throw new InvalidOperationException();
            var closeResult = await closeAgent.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { id = "agent-1" }),
                context,
                CancellationToken.None);
            Assert.True(closeResult.Success, closeResult.OutputText);
            Assert.Contains("closed", closeResult.OutputText, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public async Task BuiltinToolPackageManifest_ShouldLoadCodeProviderArrayAndReplaceFallbackTools()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-code-provider-array-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var builtinDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "builtin");
            Directory.CreateDirectory(builtinDirectory);
            var relativeAssemblyPath = Path
                .GetRelativePath(builtinDirectory, typeof(CodeToolProvider).Assembly.Location)
                .Replace('\\', '/');
            File.WriteAllText(
                Path.Combine(builtinDirectory, "tool.toml"),
                $$"""
                id = "builtin"
                enabled = true
                type = "builtin"
                priority = 0

                [tool_sets]
                enabled = ["code"]
                disabled = []

                [[providers]]
                id = "code"
                enabled = true
                type = "assembly"
                assembly_path = "{{relativeAssemblyPath}}"
                provider_type = "{{typeof(CodeToolProvider).FullName}}"
                priority = 10
                replace_existing = true
                """);

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();
            var toolNames = new[] { "exec", "exec_wait", "js_repl", "js_repl_reset" };
            foreach (var toolName in toolNames)
            {
                Assert.True(registry.TryGet(toolName, out var tool));
                Assert.Equal("tianshu.tools.code", tool!.ImplementationBinding.ImplementationId);
            }

            var resetCalled = false;
            var imageContentItem = new KernelToolOutputContentItem(
                "input_image",
                ImageUrl: "data:image/png;base64,abc",
                Detail: "low");
            var runtimeServices = new KernelToolRuntimeServices(
                ExecuteCodeMode: (request, _) =>
                {
                    Assert.Equal("text('hello')", request.Code);
                    Assert.Equal(5, request.YieldTimeMs);
                    Assert.Equal(100, request.MaxOutputTokens);
                    return Task.FromResult(new KernelCodeModeOperationResult(
                        true,
                        "exec ok",
                        [imageContentItem]));
                },
                WaitOnCodeMode: (request, _) =>
                {
                    Assert.Equal("cell-1", request.CellId);
                    Assert.Equal(10, request.YieldTimeMs);
                    Assert.Equal(20, request.MaxTokens);
                    Assert.True(request.Terminate);
                    return Task.FromResult(new KernelCodeModeOperationResult(
                        true,
                        "wait ok",
                        Array.Empty<KernelToolOutputContentItem>()));
                },
                ExecuteJsRepl: (request, _) =>
                {
                    Assert.Equal("await 1", request.Code);
                    Assert.Equal(15, request.TimeoutMs);
                    return Task.FromResult(new KernelJsReplExecutionResult(
                        true,
                        "repl ok",
                        Array.Empty<KernelToolOutputContentItem>()));
                },
                ResetJsRepl: _ =>
                {
                    resetCalled = true;
                    return Task.CompletedTask;
                });

            var context = new KernelToolCallContext(
                "thread-code",
                "turn-code",
                Environment.CurrentDirectory,
                RuntimeServices: runtimeServices);

            var exec = registry.TryGet("exec", out var execTool) ? execTool! : throw new InvalidOperationException();
            var execResult = await exec.ExecuteCustomAsync(
                """
                // @exec: {"yield_time_ms":5,"max_output_tokens":100}
                text('hello')
                """,
                context,
                CancellationToken.None);
            Assert.True(execResult.Success, execResult.OutputText);
            Assert.Equal("exec ok", execResult.OutputText);
            var execContentItem = Assert.Single(execResult.OutputContentItems!);
            Assert.Equal("input_image", execContentItem.Type);
            Assert.Equal("data:image/png;base64,abc", execContentItem.ImageUrl);
            Assert.Equal("low", execContentItem.Detail);

            var execWait = registry.TryGet("exec_wait", out var execWaitTool) ? execWaitTool! : throw new InvalidOperationException();
            var waitResult = await execWait.ExecuteAsync(
                JsonSerializer.SerializeToElement(new
                {
                    cell_id = "cell-1",
                    yield_time_ms = 10,
                    max_tokens = 20,
                    terminate = true,
                }),
                context,
                CancellationToken.None);
            Assert.True(waitResult.Success, waitResult.OutputText);
            Assert.Equal("wait ok", waitResult.OutputText);

            var jsRepl = registry.TryGet("js_repl", out var jsReplTool) ? jsReplTool! : throw new InvalidOperationException();
            var jsReplResult = await jsRepl.ExecuteCustomAsync(
                """
                // tianshu-js-repl: timeout_ms=15
                await 1
                """,
                context,
                CancellationToken.None);
            Assert.True(jsReplResult.Success, jsReplResult.OutputText);
            Assert.Equal("repl ok", jsReplResult.OutputText);

            var jsReplReset = registry.TryGet("js_repl_reset", out var jsReplResetTool) ? jsReplResetTool! : throw new InvalidOperationException();
            var resetResult = await jsReplReset.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { }),
                context,
                CancellationToken.None);
            Assert.True(resetResult.Success, resetResult.OutputText);
            Assert.True(resetCalled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public async Task BuiltinToolPackageManifest_ShouldLoadArtifactProviderArrayAndReplaceFallbackTools()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-artifact-provider-array-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var builtinDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "builtin");
            Directory.CreateDirectory(builtinDirectory);
            var relativeAssemblyPath = Path
                .GetRelativePath(builtinDirectory, typeof(ArtifactToolProvider).Assembly.Location)
                .Replace('\\', '/');
            File.WriteAllText(
                Path.Combine(builtinDirectory, "tool.toml"),
                $$"""
                id = "builtin"
                enabled = true
                type = "builtin"
                priority = 0

                [tool_sets]
                enabled = ["artifact"]
                disabled = []

                [[providers]]
                id = "artifacts"
                enabled = true
                type = "assembly"
                assembly_path = "{{relativeAssemblyPath}}"
                provider_type = "{{typeof(ArtifactToolProvider).FullName}}"
                priority = 10
                replace_existing = true
                """);

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();
            Assert.True(registry.TryGet("artifacts", out var artifacts));
            Assert.True(registry.TryGet("view_image", out var viewImage));
            Assert.Equal("tianshu.tools.artifacts", artifacts!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.artifacts", viewImage!.ImplementationBinding.ImplementationId);

            var artifactsCalled = false;
            var runtimeServices = new KernelToolRuntimeServices(
                ExecuteArtifacts: (request, _) =>
                {
                    artifactsCalled = true;
                    Assert.Equal("artifactTool.render();", request.Source);
                    Assert.Equal(15, request.TimeoutMs);
                    return Task.FromResult(new KernelArtifactsExecutionResult(true, "artifact ok"));
                });
            var context = new KernelToolCallContext(
                "thread-artifact",
                "turn-artifact",
                tempHome,
                RuntimeServices: runtimeServices,
                CanRequestOriginalImageDetail: true);

            var artifactsResult = await artifacts.ExecuteCustomAsync(
                """
                // tianshu-artifacts: timeout_ms=15
                artifactTool.render();
                """,
                context,
                CancellationToken.None);
            Assert.True(artifactsResult.Success, artifactsResult.OutputText);
            Assert.True(artifactsCalled);
            Assert.Equal("artifact ok", artifactsResult.OutputText);

            var imagePath = Path.Combine(tempHome, "pixel.png");
            WriteTestPng(imagePath);
            var viewImageResult = await viewImage.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { path = imagePath, detail = "original" }),
                context,
                CancellationToken.None);
            Assert.True(viewImageResult.Success, viewImageResult.OutputText);
            Assert.Equal(string.Empty, viewImageResult.OutputText);
            var contentItem = Assert.Single(viewImageResult.OutputContentItems!);
            Assert.Equal("input_image", contentItem.Type);
            Assert.StartsWith("data:image/png;base64,", contentItem.ImageUrl, StringComparison.Ordinal);
            Assert.Equal("original", contentItem.Detail);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public async Task BuiltinToolPackageManifest_ShouldLoadFanoutProviderArrayAndReplaceFallbackTools()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-fanout-provider-array-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var builtinDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "builtin");
            Directory.CreateDirectory(builtinDirectory);
            var relativeAssemblyPath = Path
                .GetRelativePath(builtinDirectory, typeof(FanoutToolProvider).Assembly.Location)
                .Replace('\\', '/');
            File.WriteAllText(
                Path.Combine(builtinDirectory, "tool.toml"),
                $$"""
                id = "builtin"
                enabled = true
                type = "builtin"
                priority = 0

                [tool_sets]
                enabled = ["collaboration"]
                disabled = []

                [[providers]]
                id = "fanout"
                enabled = true
                type = "assembly"
                assembly_path = "{{relativeAssemblyPath}}"
                provider_type = "{{typeof(FanoutToolProvider).FullName}}"
                priority = 10
                replace_existing = true
                """);

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();
            Assert.True(registry.TryGet("spawn_agents_on_csv", out var spawnAgentsOnCsv));
            Assert.True(registry.TryGet("report_agent_job_result", out var reportAgentJobResult));
            Assert.Equal("tianshu.tools.fanout", spawnAgentsOnCsv!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.fanout", reportAgentJobResult!.ImplementationBinding.ImplementationId);

            var runtimeServices = new KernelToolRuntimeServices(
                SpawnAgentsOnCsv: (request, _) =>
                {
                    Assert.Equal("input.csv", request.CsvPath);
                    Assert.Equal("处理 {name}", request.Instruction);
                    Assert.Equal("id", request.IdColumn);
                    Assert.Equal("output.csv", request.OutputCsvPath);
                    Assert.Equal(2, request.MaxConcurrency);
                    Assert.Equal(1, request.MaxWorkers);
                    Assert.Equal(30, request.MaxRuntimeSeconds);
                    Assert.NotNull(request.OutputSchema);
                    return Task.FromResult(new KernelSpawnAgentsOnCsvResponse(
                        "job-1",
                        "completed",
                        "output.csv",
                        TotalItems: 1,
                        CompletedItems: 1,
                        FailedItems: 0,
                        JobError: null,
                        FailedItemErrors: null));
                },
                ReportAgentJobResult: (jobId, itemId, payload, stop, _) =>
                {
                    Assert.Equal("job-1", jobId);
                    Assert.Equal("row-1", itemId);
                    Assert.Equal("ok", payload.GetProperty("status").GetString());
                    Assert.True(stop);
                    return Task.FromResult(true);
                });

            var context = new KernelToolCallContext(
                "thread-fanout",
                "turn-fanout",
                Environment.CurrentDirectory,
                RuntimeServices: runtimeServices);

            var spawnResult = await spawnAgentsOnCsv.ExecuteAsync(
                JsonSerializer.SerializeToElement(new
                {
                    csv_path = "input.csv",
                    instruction = "处理 {name}",
                    id_column = "id",
                    output_csv_path = "output.csv",
                    max_concurrency = 2,
                    max_workers = 1,
                    max_runtime_seconds = 30,
                    output_schema = new
                    {
                        type = "object",
                        properties = new { status = new { type = "string" } },
                    },
                }),
                context,
                CancellationToken.None);
            Assert.True(spawnResult.Success, spawnResult.OutputText);
            using (var spawnJson = JsonDocument.Parse(spawnResult.OutputText))
            {
                Assert.Equal("job-1", spawnJson.RootElement.GetProperty("job_id").GetString());
                Assert.Equal("completed", spawnJson.RootElement.GetProperty("status").GetString());
                Assert.Equal("output.csv", spawnJson.RootElement.GetProperty("output_csv_path").GetString());
                Assert.Equal(1, spawnJson.RootElement.GetProperty("total_items").GetInt32());
            }

            var reportResult = await reportAgentJobResult.ExecuteAsync(
                JsonSerializer.SerializeToElement(new
                {
                    job_id = "job-1",
                    item_id = "row-1",
                    result = new { status = "ok" },
                    stop = true,
                }),
                context,
                CancellationToken.None);
            Assert.True(reportResult.Success, reportResult.OutputText);
            using (var reportJson = JsonDocument.Parse(reportResult.OutputText))
            {
                Assert.True(reportJson.RootElement.GetProperty("accepted").GetBoolean());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public void BuiltinToolPackageManifest_ShouldLoadMemoryProviderArrayAndReplaceFallbackTools()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-memory-provider-array-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var builtinDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "builtin");
            Directory.CreateDirectory(builtinDirectory);
            var relativeAssemblyPath = Path
                .GetRelativePath(builtinDirectory, typeof(MemoryToolProvider).Assembly.Location)
                .Replace('\\', '/');
            File.WriteAllText(
                Path.Combine(builtinDirectory, "tool.toml"),
                $$"""
                id = "builtin"
                enabled = true
                type = "builtin"
                priority = 0

                [tool_sets]
                enabled = ["memory"]
                disabled = []

                [[providers]]
                id = "memory"
                enabled = true
                type = "assembly"
                assembly_path = "{{relativeAssemblyPath}}"
                provider_type = "{{typeof(MemoryToolProvider).FullName}}"
                priority = 10
                replace_existing = true
                """);

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

            Assert.True(registry.TryGet("memory_search", out var memorySearch));
            Assert.True(registry.TryGet("memory_explain_overlay", out var memoryExplainOverlay));
            Assert.True(registry.TryGet("memory_feedback", out var memoryFeedback));
            Assert.Equal("tianshu.tools.memory", memorySearch!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.memory", memoryExplainOverlay!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.memory", memoryFeedback!.ImplementationBinding.ImplementationId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public async Task BuiltinToolPackageManifest_ShouldLoadMcpResourceProviderArrayAndReplaceFallbackTools()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-mcp-resource-provider-array-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var builtinDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "builtin");
            Directory.CreateDirectory(builtinDirectory);
            var relativeAssemblyPath = Path
                .GetRelativePath(builtinDirectory, typeof(McpResourceToolProvider).Assembly.Location)
                .Replace('\\', '/');
            File.WriteAllText(
                Path.Combine(builtinDirectory, "tool.toml"),
                $$"""
                id = "builtin"
                enabled = true
                type = "builtin"
                priority = 0

                [tool_sets]
                enabled = ["mcp"]
                disabled = []

                [[providers]]
                id = "mcp_resources"
                enabled = true
                type = "assembly"
                assembly_path = "{{relativeAssemblyPath}}"
                provider_type = "{{typeof(McpResourceToolProvider).FullName}}"
                priority = 10
                replace_existing = true
                """);

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

            Assert.True(registry.TryGet("list_mcp_resources", out var listResources));
            Assert.True(registry.TryGet("list_mcp_resource_templates", out var listResourceTemplates));
            Assert.True(registry.TryGet("read_mcp_resource", out var readResource));
            Assert.Equal("tianshu.tools.mcp-resources", listResources!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.mcp-resources", listResourceTemplates!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.mcp-resources", readResource!.ImplementationBinding.ImplementationId);

            var context = new KernelToolCallContext(
                "thread-mcp",
                "turn-mcp",
                Environment.CurrentDirectory,
                RuntimeServices: new KernelToolRuntimeServices(
                    ListMcpResources: (_, _, _) => Task.FromResult(new KernelMcpListResourcesResult(
                        "docs",
                        [new KernelMcpResourceEntry("docs", JsonSerializer.SerializeToElement(new { uri = "file://readme.md", name = "README" }))],
                        "next-page")),
                    ListMcpResourceTemplates: (_, _, _) => Task.FromResult(new KernelMcpListResourceTemplatesResult(
                        "docs",
                        [new KernelMcpResourceTemplateEntry("docs", JsonSerializer.SerializeToElement(new { uriTemplate = "file://{name}", name = "File" }))],
                        null)),
                    ReadMcpResource: (_, _, _) => Task.FromResult(new KernelMcpReadResourceResult(
                        "docs",
                        "file://readme.md",
                        JsonSerializer.SerializeToElement(new { contents = new[] { new { type = "text", text = "hello" } } })))));

            var listResult = await listResources.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { server = "docs" }),
                context,
                CancellationToken.None);
            Assert.True(listResult.Success);
            using (var document = JsonDocument.Parse(listResult.OutputText))
            {
                Assert.Equal("docs", document.RootElement.GetProperty("server").GetString());
                Assert.Equal("next-page", document.RootElement.GetProperty("nextCursor").GetString());
                var resource = Assert.Single(document.RootElement.GetProperty("resources").EnumerateArray());
                Assert.Equal("docs", resource.GetProperty("server").GetString());
                Assert.Equal("file://readme.md", resource.GetProperty("uri").GetString());
            }

            var readResult = await readResource.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { server = "docs", uri = "file://readme.md" }),
                context,
                CancellationToken.None);
            Assert.True(readResult.Success);
            using (var document = JsonDocument.Parse(readResult.OutputText))
            {
                Assert.Equal("docs", document.RootElement.GetProperty("server").GetString());
                Assert.Equal("file://readme.md", document.RootElement.GetProperty("uri").GetString());
                var content = Assert.Single(document.RootElement.GetProperty("contents").EnumerateArray());
                Assert.Equal("hello", content.GetProperty("text").GetString());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public async Task BuiltinToolPackageManifest_ShouldLoadFileSystemProviderArrayAndReplaceReadOnlyFallbackTools()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-filesystem-provider-array-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var builtinDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "builtin");
            Directory.CreateDirectory(builtinDirectory);
            var relativeAssemblyPath = Path
                .GetRelativePath(builtinDirectory, typeof(FileSystemToolProvider).Assembly.Location)
                .Replace('\\', '/');
            File.WriteAllText(
                Path.Combine(builtinDirectory, "tool.toml"),
                $$"""
                id = "builtin"
                enabled = true
                type = "builtin"
                priority = 0

                [tool_sets]
                enabled = ["filesystem"]
                disabled = []

                [[providers]]
                id = "filesystem"
                enabled = true
                type = "assembly"
                assembly_path = "{{relativeAssemblyPath}}"
                provider_type = "{{typeof(FileSystemToolProvider).FullName}}"
                priority = 10
                replace_existing = true
                """);

            var workspace = Path.Combine(tempHome, "workspace");
            Directory.CreateDirectory(Path.Combine(workspace, "src"));
            var filePath = Path.Combine(workspace, "src", "alpha.txt");
            await File.WriteAllTextAsync(filePath, "first line\nneedle line\n");

            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

            Assert.True(registry.TryGet("list_dir", out var listDir));
            Assert.True(registry.TryGet("read_file", out var readFile));
            Assert.True(registry.TryGet("grep_files", out var grepFiles));
            Assert.True(registry.TryGet("grep", out var grep));
            Assert.True(registry.TryGet("glob", out var glob));
            Assert.Equal("tianshu.tools.filesystem", listDir!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.filesystem", readFile!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.filesystem", grepFiles!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.filesystem", grep!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.filesystem", glob!.ImplementationBinding.ImplementationId);
            Assert.False(registry.TryGet("write", out _));
            Assert.False(registry.TryGet("apply_patch", out _));

            var context = new KernelToolCallContext("thread-fs", "turn-fs", workspace);
            var readResult = await readFile.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { file_path = filePath, offset = 2, limit = 1 }),
                context,
                CancellationToken.None);
            Assert.True(readResult.Success);
            Assert.Contains("L2: needle line", readResult.OutputText, StringComparison.Ordinal);

            var grepFilesResult = await grepFiles.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { pattern = "needle", path = "src", include = "*.txt" }),
                context,
                CancellationToken.None);
            Assert.True(grepFilesResult.Success);
            Assert.Contains("src", grepFilesResult.OutputText, StringComparison.Ordinal);
            Assert.Contains("alpha.txt", grepFilesResult.OutputText, StringComparison.Ordinal);

            var globResult = await glob.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { pattern = "*.txt", path = "src", recursive = false }),
                context,
                CancellationToken.None);
            Assert.True(globResult.Success);
            Assert.Contains("alpha.txt", globResult.OutputText, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    [Fact]
    public async Task BuiltinToolPackageManifest_ShouldLoadMutatingFileSystemProviderArrayAndReplaceFallbackTools()
    {
        var originalHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var tempHome = CreateTempDirectory("tianshu-tool-package-filesystem-mutating-provider-array-");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tempHome);
            var builtinDirectory = Path.Combine(tempHome, "modules", "tools", "packages", "builtin");
            Directory.CreateDirectory(builtinDirectory);
            var relativeAssemblyPath = Path
                .GetRelativePath(builtinDirectory, typeof(MutatingFileSystemToolProvider).Assembly.Location)
                .Replace('\\', '/');
            File.WriteAllText(
                Path.Combine(builtinDirectory, "tool.toml"),
                $$"""
                id = "builtin"
                enabled = true
                type = "builtin"
                priority = 0

                [tool_sets]
                enabled = ["filesystem"]
                disabled = []

                [[providers]]
                id = "filesystem_mutating"
                enabled = true
                type = "assembly"
                assembly_path = "{{relativeAssemblyPath}}"
                provider_type = "{{typeof(MutatingFileSystemToolProvider).FullName}}"
                priority = 10
                replace_existing = true
                """);

            var workspace = Path.Combine(tempHome, "workspace");
            Directory.CreateDirectory(workspace);
            var registry = KernelToolRegistryFactory.CreateDefaultRegistry();

            Assert.True(registry.TryGet("write", out var write));
            Assert.True(registry.TryGet("apply_patch", out var applyPatch));
            Assert.Equal("tianshu.tools.filesystem-mutating", write!.ImplementationBinding.ImplementationId);
            Assert.Equal("tianshu.tools.filesystem-mutating", applyPatch!.ImplementationBinding.ImplementationId);
            Assert.False(registry.ToolSupportsParallelToolCalls("write"));
            Assert.False(registry.ToolSupportsParallelToolCalls("apply_patch"));

            var context = new KernelToolCallContext("thread-fsm", "turn-fsm", workspace);
            var writeResult = await write.ExecuteAsync(
                JsonSerializer.SerializeToElement(new { path = "alpha.txt", content = "first line\n" }),
                context,
                CancellationToken.None);
            Assert.True(writeResult.Success);
            Assert.Equal("first line\n", await File.ReadAllTextAsync(Path.Combine(workspace, "alpha.txt")));

            var patch = """
                *** Begin Patch
                *** Update File: alpha.txt
                @@
                -first line
                +first line
                +second line
                *** End Patch
                """;
            var patchResult = await applyPatch.ExecuteCustomAsync(patch, context, CancellationToken.None);
            Assert.True(patchResult.Success);
            Assert.Contains("M alpha.txt", patchResult.OutputText, StringComparison.Ordinal);
            Assert.Equal("first line\nsecond line\n", await File.ReadAllTextAsync(Path.Combine(workspace, "alpha.txt")));

            var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
                WebSearchMode: null,
                ImageGenerationEnabled: false,
                ApplyPatchFreeform: true));
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
            var applyPatchTool = json.RootElement.EnumerateArray().Single(static tool =>
                tool.GetProperty("type").GetString() == "custom"
                && string.Equals(tool.GetProperty("name").GetString(), "apply_patch", StringComparison.Ordinal));
            Assert.Equal("grammar", applyPatchTool.GetProperty("format").GetProperty("type").GetString());
            Assert.Equal("lark", applyPatchTool.GetProperty("format").GetProperty("syntax").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalHome);
            DeleteDirectoryIfExists(tempHome);
        }
    }

    private static void WriteBuiltinToolPackageManifest(string builtinDirectory, string providerId)
    {
        Directory.CreateDirectory(builtinDirectory);
        var providerType = providerId switch
        {
            "filesystem" => typeof(FileSystemToolProvider),
            "shell" => typeof(ShellToolProvider),
            _ => throw new ArgumentOutOfRangeException(nameof(providerId), providerId, "Unsupported builtin provider id."),
        };
        var relativeAssemblyPath = Path
            .GetRelativePath(builtinDirectory, providerType.Assembly.Location)
            .Replace('\\', '/');
        File.WriteAllText(
            Path.Combine(builtinDirectory, "tool.toml"),
            $$"""
            id = "builtin"
            enabled = true
            type = "builtin"
            priority = 0

            [[providers]]
            id = "{{providerId}}"
            enabled = true
            type = "assembly"
            assembly_path = "{{relativeAssemblyPath}}"
            provider_type = "{{providerType.FullName}}"
            priority = 10
            replace_existing = true
            """);
    }

    [Fact]
    public void InternalRuntimeToolSet_ShouldBeComposedForCompatibilityTests()
    {
        var registry = new KernelToolRegistryBuilder()
            .AddToolSet(new KernelInternalRuntimeToolSet())
            .Build();

        Assert.True(registry.TryGet("test_sync_tool", out _));
        Assert.False(registry.TryGet("shell_command", out _));
        Assert.False(registry.TryGet("read_file", out _));
        Assert.False(registry.TryGet("update_plan", out _));
    }

    [Fact]
    public void ToolProfileOptions_ShouldDisableProviderVisibleTools()
    {
        var registry = KernelToolRegistryFactory.CreateDefaultRegistry();
        var nativeToolOptions = new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            ToolProfileOptions: KernelToolProfileOptions.FromConfigValues(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tools.read_file.enabled"] = "false",
            }));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(registry.BuildProviderResponsesToolList(nativeToolOptions)));

        Assert.DoesNotContain(json.RootElement.EnumerateArray(), static tool =>
            string.Equals(tool.GetProperty("name").GetString(), "read_file", StringComparison.Ordinal));

        var catalogItem = registry.BuildResolvedToolCatalog(toolProfileOptions: nativeToolOptions.ToolProfileOptions).Items
            .Single(static item => item.Name == "read_file");
        Assert.False(catalogItem.Available);
        Assert.False(catalogItem.ModelVisible);
        Assert.Equal("disabled by tool profile", catalogItem.Reason);
    }

    [Fact]
    public async Task ContractToolProvider_ShouldRegisterAndExecuteThroughAdapter()
    {
        var registry = new KernelToolRegistryBuilder()
            .AddContractToolProvider(new TestContractToolProvider())
            .Build();

        Assert.True(registry.TryGet("contract_echo", out var handler));

        var result = await handler!.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { text = "hello" }),
            new KernelToolCallContext("thread", "turn", Environment.CurrentDirectory),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("contract_echo", result.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContractToolProvider_ShouldReceiveDiagnosticServiceAndCancellationToken()
    {
        var diagnostics = new List<TianShuToolDiagnosticEvent>();
        var adapter = new KernelContractToolHandlerAdapter(new DiagnosticContractToolHandler());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await adapter.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            new KernelToolCallContext(
                "thread",
                "turn",
                Environment.CurrentDirectory,
                RuntimeServices: new KernelToolRuntimeServices(
                    ReportToolDiagnostic: (diagnostic, _) =>
                    {
                        diagnostics.Add(diagnostic);
                        return Task.CompletedTask;
                    })),
            cancellation.Token);

        Assert.True(result.Success);
        Assert.Single(diagnostics);
        Assert.Equal("contract_diagnostic", diagnostics[0].ToolKey);
        Assert.Contains("cancelled=True", result.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public void ContractToolProvider_ShouldClassifyRequiredApprovalToolsAsMutatingAndSerial()
    {
        var forgedHandler = new ForgedApprovalContractToolHandler();
        var adapter = new KernelContractToolHandlerAdapter(forgedHandler);

        Assert.True(adapter.IsMutating);
        Assert.False(adapter.SupportsParallelToolCalls);
        Assert.Equal(ToolApprovalRequirement.Required, forgedHandler.Descriptor.ApprovalRequirement);
    }

    [Fact]
    public void AssemblyToolProviderLoader_ShouldLoadProviderFromConfiguredAssembly()
    {
        var config = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tool_providers.test.type"] = "\"assembly\"",
            ["tool_providers.test.assembly_path"] = JsonSerializer.Serialize(typeof(TestContractToolProvider).Assembly.Location),
            ["tool_providers.test.provider_type"] = JsonSerializer.Serialize(typeof(TestContractToolProvider).FullName),
        };

        var registry = KernelToolRegistryFactory.CreateDefaultRegistry(config, Environment.CurrentDirectory);

        Assert.True(registry.TryGet("contract_echo", out _));
    }

    [Fact]
    public void AssemblyToolProviderLoader_ShouldReplaceDefaultToolWhenImplementationIsSelected()
    {
        var config = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tool_providers.test.type"] = "\"assembly\"",
            ["tool_providers.test.assembly_path"] = JsonSerializer.Serialize(typeof(TestReadFileReplacementProvider).Assembly.Location),
            ["tool_providers.test.provider_type"] = JsonSerializer.Serialize(typeof(TestReadFileReplacementProvider).FullName),
            ["tools.read_file.implementation_id"] = JsonSerializer.Serialize("test-read-file-replacement"),
        };

        var registry = KernelToolRegistryFactory.CreateDefaultRegistry(config, Environment.CurrentDirectory);

        Assert.True(registry.TryGet("read_file", out var handler));
        Assert.Equal("test-read-file-replacement", handler!.ImplementationBinding.ImplementationId);

        var catalogItem = registry.BuildResolvedToolCatalog().Items.Single(static item => item.Name == "read_file");
        Assert.Equal("test-read-file-replacement", catalogItem.ImplementationId);
    }

    [Fact]
    public void ToolRegistry_ShouldKeepCoordinationToolsSerial()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new CollaborationToolProvider());
        RegisterProviderTools(registry, new InteractionToolProvider());

        Assert.False(registry.ToolSupportsParallelToolCalls("update_plan"));
        Assert.False(registry.ToolSupportsParallelToolCalls("request_user_input"));
        Assert.False(registry.ToolSupportsParallelToolCalls("request_permissions"));
        Assert.False(registry.ToolSupportsParallelToolCalls("wait"));
    }

    [Fact]
    public void BuildProviderToolDefinition_ShouldSanitizeUnionSchemasBeforeSerialization()
    {
        var handler = new SchemaSanitizingToolHandler(JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                object_union = new { type = new[] { "object", "null" } },
                array_union = new { type = new[] { "array", "null" } },
            },
            additionalProperties = false,
        }));

        using var json = CompileProviderTool(handler.BuildProviderToolDefinition());
        var parameters = json.RootElement.GetProperty("parameters");
        var objectUnion = parameters.GetProperty("properties").GetProperty("object_union");
        var arrayUnion = parameters.GetProperty("properties").GetProperty("array_union");

        Assert.Equal("object", objectUnion.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Object, objectUnion.GetProperty("properties").ValueKind);
        Assert.Equal("array", arrayUnion.GetProperty("type").GetString());
        Assert.Equal("string", arrayUnion.GetProperty("items").GetProperty("type").GetString());
    }

    [Fact]
    public void BuildProviderToolDefinition_ShouldAllowExecCommandUnionSchemaWithoutJsonNodeParentReuseFailures()
    {
        var handler = new KernelContractToolHandlerAdapter(
            new ShellToolProvider().CreateHandler("exec_command", new TianShuToolActivationContext()));

        using var json = CompileProviderTool(handler.BuildProviderToolDefinition());
        var commandSchema = json.RootElement
            .GetProperty("parameters")
            .GetProperty("properties")
            .GetProperty("command");

        Assert.Equal(JsonValueKind.Array, commandSchema.GetProperty("oneOf").ValueKind);
        Assert.Equal("string", commandSchema.GetProperty("oneOf")[0].GetProperty("type").GetString());
        Assert.Equal("array", commandSchema.GetProperty("oneOf")[1].GetProperty("type").GetString());
        Assert.Equal("string", commandSchema.GetProperty("oneOf")[1].GetProperty("items").GetProperty("type").GetString());
    }

    [Fact]
    public void BuildProviderToolDefinition_ShouldIncludeSanitizedOutputSchema_WhenProvided()
    {
        var handler = new OutputSchemaToolHandler();

        using var json = CompileProviderTool(handler.BuildProviderToolDefinition());
        var outputSchema = json.RootElement.GetProperty("output_schema");

        Assert.Equal("object", outputSchema.GetProperty("type").GetString());
        Assert.Equal("string", outputSchema.GetProperty("properties").GetProperty("result").GetProperty("type").GetString());
    }

    [Fact]
    public void ToolRegistry_ShouldBuildDefaultResponsesToolList_WithoutJsonNodeParentReuseFailures()
    {
        var registry = CreateFullToolRegistry();

        var tools = registry.BuildProviderResponsesToolList();

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var toolNames = json.RootElement
            .EnumerateArray()
            .Where(static tool => tool.TryGetProperty("name", out _))
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("shell_command", toolNames);
        Assert.DoesNotContain("shell", toolNames);
        Assert.DoesNotContain("exec_command", toolNames);
        Assert.DoesNotContain("write_stdin", toolNames);
        Assert.DoesNotContain("spawn_agents_on_csv", toolNames);
        Assert.DoesNotContain("report_agent_job_result", toolNames);
        Assert.Contains("request_user_input", toolNames);
        Assert.DoesNotContain("request_permissions", toolNames);
        Assert.Contains("memory_search", toolNames);
        Assert.Contains("memory_explain_overlay", toolNames);
        Assert.Contains("memory_feedback", toolNames);
        Assert.DoesNotContain("test_sync_tool", toolNames);
    }

    [Fact]
    public void ToolRegistry_ShouldExposeOnlyConfiguredShellVariant_AndRequestPermissionsFlag()
    {
        var registry = CreateFullToolRegistry();

        var defaultShellTools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            ShellToolType: KernelShellToolType.Default,
            RequestPermissionsToolEnabled: false));
        using var defaultShellJson = JsonDocument.Parse(JsonSerializer.Serialize(defaultShellTools));
        var defaultShellNames = defaultShellJson.RootElement
            .EnumerateArray()
            .Where(static tool => tool.TryGetProperty("name", out _))
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("shell", defaultShellNames);
        Assert.DoesNotContain("shell_command", defaultShellNames);
        Assert.DoesNotContain("request_permissions", defaultShellNames);

        var shellCommandTools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            ShellToolType: KernelShellToolType.ShellCommand,
            RequestPermissionsToolEnabled: true));
        using var shellCommandJson = JsonDocument.Parse(JsonSerializer.Serialize(shellCommandTools));
        var shellCommandNames = shellCommandJson.RootElement
            .EnumerateArray()
            .Where(static tool => tool.TryGetProperty("name", out _))
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToArray();
        Assert.DoesNotContain("shell", shellCommandNames);
        Assert.Contains("shell_command", shellCommandNames);
        Assert.Contains("request_permissions", shellCommandNames);

        var localShellTools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            ShellToolType: KernelShellToolType.Local,
            RequestPermissionsToolEnabled: false));
        using var localShellJson = JsonDocument.Parse(JsonSerializer.Serialize(localShellTools));
        var localShellNames = localShellJson.RootElement
            .EnumerateArray()
            .Where(static tool => tool.TryGetProperty("name", out _))
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToArray();
        Assert.DoesNotContain("shell", localShellNames);
        Assert.DoesNotContain("shell_command", localShellNames);
        Assert.Contains("local_shell", localShellNames);
    }

    [Fact]
    public void ToolRegistry_ShouldExposeRequestPermissionsSchema_WithoutMacos()
    {
        var registry = CreateFullToolRegistry();

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            RequestPermissionsToolEnabled: true));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var requestPermissions = json.RootElement.EnumerateArray().Single(static tool =>
            tool.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "request_permissions", StringComparison.Ordinal));
        var permissionProperties = requestPermissions
            .GetProperty("parameters")
            .GetProperty("properties")
            .GetProperty("permissions")
            .GetProperty("properties")
            .EnumerateObject()
            .Select(static property => property.Name)
            .ToArray();

        Assert.Contains("network", permissionProperties);
        Assert.Contains("file_system", permissionProperties);
        Assert.DoesNotContain("macos", permissionProperties);
    }

    [Fact]
    public void ToolRegistry_ShouldHideMemoryToolsWhenMemoryToolFlagDisabled()
    {
        var registry = CreateFullToolRegistry();

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            MemoryToolsEnabled: false));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var toolNames = json.RootElement
            .EnumerateArray()
            .Where(static tool => tool.TryGetProperty("name", out _))
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToArray();

        Assert.DoesNotContain("memory_search", toolNames);
        Assert.DoesNotContain("memory_explain_overlay", toolNames);
        Assert.DoesNotContain("memory_feedback", toolNames);
    }

    [Fact]
    public void ToolRegistry_ShouldBuildResponsesToolList_WithNativeOptionalToolsEnabled_WithoutJsonNodeParentReuseFailures()
    {
        var registry = CreateFullToolRegistry();

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: "live",
            ImageGenerationEnabled: true,
            WebSearchSupportsImageContent: true,
            ArtifactToolEnabled: true,
            McpResourceToolsEnabled: true,
            SearchToolEnabled: true,
            ToolSuggestEnabled: true,
            ToolSuggestDiscoverableConnectors:
            [
                new KernelToolSuggestConnectorInfo(
                    "connector_2128aebfecb84f64a069897515042a44",
                    "Google Calendar",
                    "Plan events and schedules.",
                    "https://chatgpt.com/apps/google-calendar/connector_2128aebfecb84f64a069897515042a44"),
            ]));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var functionToolNames = json.RootElement
            .EnumerateArray()
            .Where(static tool => tool.TryGetProperty("name", out _))
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToArray();
        var nativeToolTypes = json.RootElement
            .EnumerateArray()
            .Where(static tool => tool.TryGetProperty("type", out _))
            .Select(static tool => tool.GetProperty("type").GetString())
            .ToArray();

        Assert.Contains("artifacts", functionToolNames);
        Assert.Contains("list_mcp_resources", functionToolNames);
        Assert.Contains("list_mcp_resource_templates", functionToolNames);
        Assert.Contains("read_mcp_resource", functionToolNames);
        Assert.Contains("tool_suggest", functionToolNames);
        Assert.Contains("web_search", nativeToolTypes);
        Assert.Contains("image_generation", nativeToolTypes);
    }

    [Fact]
    public void ToolRegistry_ShouldExposeDynamicToolsInResponsesToolList()
    {
        var registry = CreateFullToolRegistry();
        var dynamicTools = KernelDynamicToolResolver.Parse(JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                name = "mcp__calendar__find_events",
                description = "搜索日历事件。",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string" },
                    },
                    required = new[] { "query" },
                    additionalProperties = false,
                },
            },
        }));

        var tools = registry.BuildProviderResponsesToolList(dynamicTools);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var dynamicTool = json.RootElement.EnumerateArray().Single(static tool =>
            tool.GetProperty("type").GetString() == "function"
            && string.Equals(tool.GetProperty("name").GetString(), "mcp__calendar__find_events", StringComparison.Ordinal));
        Assert.Equal("搜索日历事件。", dynamicTool.GetProperty("description").GetString());
        Assert.Equal("string", dynamicTool.GetProperty("parameters").GetProperty("properties").GetProperty("query").GetProperty("type").GetString());
        Assert.True(dynamicTool.TryGetProperty("output_schema", out var outputSchema), JsonSerializer.Serialize(tools));
        Assert.Equal("object", outputSchema.GetProperty("type").GetString());
        Assert.Equal("array", outputSchema.GetProperty("properties").GetProperty("content").GetProperty("type").GetString());
        var structuredContent = outputSchema.GetProperty("properties").GetProperty("structuredContent");
        Assert.Equal(JsonValueKind.Object, structuredContent.ValueKind);
        Assert.False(structuredContent.TryGetProperty("type", out _));
    }

    [Fact]
    public void ToolRegistry_ShouldExposeWrappedOutputSchemaForMcpStyleDynamicTools()
    {
        var registry = CreateFullToolRegistry();
        var dynamicTools = KernelDynamicToolResolver.Parse(JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                name = "mcp__calendar__find_events",
                description = "搜索日历事件。",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string" },
                    },
                    required = new[] { "query" },
                    additionalProperties = false,
                },
                outputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        total = new { type = "integer" },
                    },
                    required = new[] { "total" },
                    additionalProperties = false,
                },
            },
        }));

        var tools = registry.BuildProviderResponsesToolList(dynamicTools);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var dynamicTool = json.RootElement.EnumerateArray().Single(static tool =>
            tool.GetProperty("type").GetString() == "function"
            && string.Equals(tool.GetProperty("name").GetString(), "mcp__calendar__find_events", StringComparison.Ordinal));
        var outputSchema = dynamicTool.GetProperty("output_schema");
        Assert.Equal("object", outputSchema.GetProperty("type").GetString());
        Assert.Equal("array", outputSchema.GetProperty("properties").GetProperty("content").GetProperty("type").GetString());
        Assert.Equal("object", outputSchema.GetProperty("properties").GetProperty("structuredContent").GetProperty("type").GetString());
        Assert.Equal(
            "integer",
            outputSchema.GetProperty("properties").GetProperty("structuredContent").GetProperty("properties").GetProperty("total").GetProperty("type").GetString());
        Assert.Equal("boolean", outputSchema.GetProperty("properties").GetProperty("isError").GetProperty("type").GetString());
    }

    [Fact]
    public void ToolRegistry_ShouldNotExposeOutputSchemaForPlainDynamicTools()
    {
        var registry = CreateFullToolRegistry();
        var dynamicTools = KernelDynamicToolResolver.Parse(JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                name = "custom_lookup",
                server = "dynamic",
                description = "普通动态工具。",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string" },
                    },
                    additionalProperties = false,
                },
                outputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        total = new { type = "integer" },
                    },
                    additionalProperties = false,
                },
            },
        }));

        var tools = registry.BuildProviderResponsesToolList(dynamicTools);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var dynamicTool = json.RootElement.EnumerateArray().Single(static tool =>
            tool.GetProperty("type").GetString() == "function"
            && string.Equals(tool.GetProperty("name").GetString(), "custom_lookup", StringComparison.Ordinal));
        Assert.False(dynamicTool.TryGetProperty("output_schema", out _));
    }

    [Fact]
    public void ToolRegistry_ShouldHideMultiAgentAndFanoutTools_WhenFeaturesAreDisabled()
    {
        var registry = CreateFullToolRegistry();

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            MultiAgentEnabled: false,
            FanoutEnabled: false,
            AgentJobWorkerToolsEnabled: false));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var toolNames = json.RootElement
            .EnumerateArray()
            .Where(static tool => tool.TryGetProperty("name", out _))
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToArray();

        Assert.DoesNotContain("spawn_agent", toolNames);
        Assert.DoesNotContain("send_input", toolNames);
        Assert.DoesNotContain("resume_agent", toolNames);
        Assert.DoesNotContain("wait", toolNames);
        Assert.DoesNotContain("close_agent", toolNames);
        Assert.DoesNotContain("spawn_agents_on_csv", toolNames);
        Assert.DoesNotContain("report_agent_job_result", toolNames);
    }

    [Fact]
    public void ToolRegistry_ShouldExposeFanoutAndWorkerTools_OnlyWhenExplicitlyEnabled()
    {
        var registry = CreateFullToolRegistry();

        var noWorkerTools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            MultiAgentEnabled: true,
            FanoutEnabled: false,
            AgentJobWorkerToolsEnabled: false));
        using var noWorkerJson = JsonDocument.Parse(JsonSerializer.Serialize(noWorkerTools));
        var noWorkerNames = noWorkerJson.RootElement
            .EnumerateArray()
            .Where(static tool => tool.TryGetProperty("name", out _))
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("spawn_agent", noWorkerNames);
        Assert.DoesNotContain("spawn_agents_on_csv", noWorkerNames);
        Assert.DoesNotContain("report_agent_job_result", noWorkerNames);

        var workerTools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            MultiAgentEnabled: true,
            FanoutEnabled: true,
            AgentJobWorkerToolsEnabled: true));
        using var workerJson = JsonDocument.Parse(JsonSerializer.Serialize(workerTools));
        var workerNames = workerJson.RootElement
            .EnumerateArray()
            .Where(static tool => tool.TryGetProperty("name", out _))
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("spawn_agents_on_csv", workerNames);
        Assert.Contains("report_agent_job_result", workerNames);
    }

    [Fact]
    public void ToolRegistry_ShouldExposeCodeModeTools_WhenCodeModeEnabled()
    {
        var registry = CreateFullToolRegistry();

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            CodeModeEnabled: true,
            CodeModeEnabledToolNames: new[] { "exec_command", "shell_command", "view_image", "write_stdin" }));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var toolArray = json.RootElement.EnumerateArray().ToArray();
        var execTool = toolArray.Single(static tool =>
            tool.GetProperty("type").GetString() == "custom"
            && string.Equals(tool.GetProperty("name").GetString(), "exec", StringComparison.Ordinal));
        var execWaitTool = toolArray.Single(static tool =>
            tool.GetProperty("type").GetString() == "function"
            && string.Equals(tool.GetProperty("name").GetString(), "exec_wait", StringComparison.Ordinal));

        Assert.Contains("Enabled nested tools: shell_command, view_image.", execTool.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("exec_command", execTool.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("write_stdin", execTool.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Equal("object", execWaitTool.GetProperty("parameters").GetProperty("type").GetString());
        Assert.DoesNotContain(toolArray, static tool =>
            tool.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "js_repl", StringComparison.Ordinal));
        Assert.DoesNotContain(toolArray, static tool =>
            tool.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "js_repl_reset", StringComparison.Ordinal));
    }

    [Fact]
    public void ToolRegistry_ShouldHideUnifiedExecTools_WhenUnifiedExecDisabled()
    {
        var registry = CreateFullToolRegistry();

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            UnifiedExecEnabled: false));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var toolNames = json.RootElement
            .EnumerateArray()
            .Where(static tool => tool.TryGetProperty("name", out _))
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("shell_command", toolNames);
        Assert.DoesNotContain("exec_command", toolNames);
        Assert.DoesNotContain("write_stdin", toolNames);
    }

    [Fact]
    public void ToolRegistry_ShouldHideUnifiedExecFromCodeModeNestedTools_WhenUnifiedExecDisabled()
    {
        var registry = CreateFullToolRegistry();

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            CodeModeEnabled: true,
            CodeModeEnabledToolNames: new[] { "view_image" },
            UnifiedExecEnabled: false));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var execTool = json.RootElement.EnumerateArray().Single(static tool =>
            tool.GetProperty("type").GetString() == "custom"
            && string.Equals(tool.GetProperty("name").GetString(), "exec", StringComparison.Ordinal));

        Assert.DoesNotContain("exec_command", execTool.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("Enabled nested tools: view_image.", execTool.GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToolRegistry_ShouldPreferJsReplTools_WhenCodeModeDisabled()
    {
        var registry = CreateFullToolRegistry();

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            CodeModeEnabled: false));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var toolNames = json.RootElement
            .EnumerateArray()
            .Where(static tool => tool.TryGetProperty("name", out _))
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("js_repl", toolNames);
        Assert.Contains("js_repl_reset", toolNames);
        Assert.DoesNotContain("exec", toolNames);
        Assert.DoesNotContain("exec_wait", toolNames);
    }

    [Fact]
    public void ToolRegistry_ShouldHideInternalTestSyncToolFromResponsesToolList()
    {
        var registry = new KernelToolRegistry();
        registry.Register(ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("list_dir"));
        registry.Register(new KernelTestSyncRuntimeEndpoint());

        var tools = registry.BuildProviderResponsesToolList();

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var toolNames = json.RootElement
            .EnumerateArray()
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("list_dir", toolNames);
        Assert.DoesNotContain("test_sync_tool", toolNames);
    }

    [Fact]
    public void CustomToolHandler_ShouldExposeCustomResponsesToolDescriptor()
    {
        using var json = CompileProviderTool(KernelJsReplRuntimeSupport.BuildJsReplProviderToolDefinition());
        var root = json.RootElement;
        Assert.Equal("custom", root.GetProperty("type").GetString());
        Assert.Equal("js_repl", root.GetProperty("name").GetString());
        Assert.Equal("grammar", root.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal("lark", root.GetProperty("format").GetProperty("syntax").GetString());
    }

    [Fact]
    public void CustomToolHandler_ShouldIncludeSanitizedOutputSchema_WhenProvided()
    {
        var handler = new OutputSchemaCustomToolHandler();

        using var json = CompileProviderTool(handler.BuildProviderToolDefinition());
        var root = json.RootElement;
        Assert.Equal("custom", root.GetProperty("type").GetString());
        Assert.Equal("output_schema_custom_tool", root.GetProperty("name").GetString());
        Assert.Equal("object", root.GetProperty("output_schema").GetProperty("type").GetString());
        Assert.Equal("string", root.GetProperty("output_schema").GetProperty("properties").GetProperty("status").GetProperty("type").GetString());
    }

    [Fact]
    public async Task SchemaOnlyTestHandlers_ShouldExecuteWithoutNotSupportedFallbacks()
    {
        var context = new KernelToolCallContext("thread", "turn", Environment.CurrentDirectory);
        var schemaHandler = new SchemaSanitizingToolHandler(JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
        }));
        var outputHandler = new OutputSchemaToolHandler();
        var customHandler = new OutputSchemaCustomToolHandler();

        var schemaResult = await schemaHandler.ExecuteAsync(JsonSerializer.SerializeToElement(new { }).Clone(), context, CancellationToken.None);
        var outputResult = await outputHandler.ExecuteAsync(JsonSerializer.SerializeToElement(new { value = "ok" }).Clone(), context, CancellationToken.None);
        var customResult = await customHandler.ExecuteCustomAsync("ok", context, CancellationToken.None);

        Assert.True(schemaResult.Success);
        Assert.Equal("ok", schemaResult.OutputText);
        Assert.True(outputResult.Success);
        Assert.Equal("ok", outputResult.OutputText);
        Assert.True(customResult.Success);
        Assert.Equal("ok", customResult.OutputText);
    }

    private static KernelToolRegistry CreateFullToolRegistry()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new ShellToolProvider());
        RegisterProviderTools(registry, new InteractionToolProvider());
        RegisterProviderTools(registry, new CollaborationToolProvider());
        RegisterProviderTools(registry, new FanoutToolProvider());
        RegisterProviderTools(registry, new CodeToolProvider());
        RegisterProviderTools(registry, new SearchToolProvider());
        RegisterProviderTools(registry, new MemoryToolProvider());
        RegisterProviderTools(registry, new McpResourceToolProvider());
        RegisterProviderTools(registry, new ArtifactToolProvider());
        ToolProviderTestAdapters.RegisterFileSystemProviderTools(registry);
        RegisterProviderTools(registry, new MutatingFileSystemToolProvider());
        registry.Register(new KernelTestSyncRuntimeEndpoint());
        return registry;
    }

    private static void RegisterProviderTools(KernelToolRegistry registry, ITianShuToolProvider provider)
    {
        var registrationContext = new TianShuToolRegistrationContext();
        var activationContext = new TianShuToolActivationContext();
        foreach (var descriptor in provider.DescribeTools(registrationContext))
        {
            var adapter = new KernelContractToolHandlerAdapter(provider.CreateHandler(descriptor.Key, activationContext));
            registry.Register(adapter);
            if (string.Equals(descriptor.Key, "shell", StringComparison.Ordinal))
            {
                registry.Register("container.exec", adapter);
            }
        }
    }

    private static JsonDocument CompileProviderTool(ProviderResponsesToolDefinition definition)
    {
        var tools = new OpenAiResponsesToolSurfaceBuilder().Build(
            new ProviderResponsesToolSurfaceBuilderContext([definition]));
        var tool = Assert.Single(tools);
        return JsonDocument.Parse(JsonSerializer.Serialize(tool));
    }

    private sealed class SchemaSanitizingToolHandler(JsonElement inputSchema) : KernelToolHandlerBase(
        "schema_sanitizing_tool",
        "用于验证 responses tool schema 规范化的测试工具。",
        isMutating: false,
        supportsParallelToolCalls: true,
        inputSchema)
    {
        public override Task<KernelToolResult> ExecuteAsync(JsonElement arguments, KernelToolCallContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(Success("ok"));
        }
    }

    private sealed class OutputSchemaToolHandler() : KernelToolHandlerBase(
        "output_schema_tool",
        "用于验证 output_schema 暴露的测试工具。",
        isMutating: false,
        supportsParallelToolCalls: true,
        inputSchema: JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                value = new { type = "string" },
            },
            additionalProperties = false,
        }),
        outputSchema: JsonSerializer.SerializeToElement(new
        {
            properties = new
            {
                result = new { @enum = new[] { "ok", "error" } },
            },
            required = new[] { "result" },
            additionalProperties = false,
        }))
    {
        public override Task<KernelToolResult> ExecuteAsync(JsonElement arguments, KernelToolCallContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(Success("ok"));
        }
    }

    private sealed class OutputSchemaCustomToolHandler() : KernelCustomToolHandlerBase(
        "output_schema_custom_tool",
        "用于验证 custom tool output_schema 暴露的测试工具。",
        isMutating: false,
        supportsParallelToolCalls: true,
        format: JsonSerializer.SerializeToElement(new
        {
            type = "grammar",
            syntax = "lark",
            definition = "start: /.+/",
        }),
        outputSchema: JsonSerializer.SerializeToElement(new
        {
            properties = new
            {
                status = new { @enum = new[] { "ok", "error" } },
            },
            required = new[] { "status" },
            additionalProperties = false,
        }))
    {
        public override Task<KernelToolResult> ExecuteCustomAsync(string input, KernelToolCallContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(Success("ok"));
        }
    }

    private static ToolImplementationBinding GetBinding(IReadOnlyList<object> descriptors, string name)
    {
        var descriptor = descriptors.Single(descriptor =>
            string.Equals(
                descriptor.GetType().GetProperty("name")?.GetValue(descriptor) as string,
                name,
                StringComparison.Ordinal));
        return Assert.IsType<ToolImplementationBinding>(
            descriptor.GetType().GetProperty("implementationBinding")?.GetValue(descriptor));
    }

    private sealed class FixedProbeService(ToolCapabilityProbe probe) : IToolCapabilityProbeService
    {
        public ToolCapabilityProbe Probe(ToolImplementationBinding binding) => probe;
    }

    private sealed class BindingProbeToolHandler(ToolImplementationBinding binding) : KernelToolHandlerBase(
        "external_only",
        "用于验证实现绑定解析的测试工具。",
        isMutating: false,
        supportsParallelToolCalls: true,
        inputSchema: JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { },
            additionalProperties = false,
        }),
        implementationBinding: binding)
    {
        public override Task<KernelToolResult> ExecuteAsync(JsonElement arguments, KernelToolCallContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(Success("ok"));
        }
    }

    private static void WriteTestPng(string path)
    {
        using var image = new Image<Rgba32>(1, 1);
        image.SaveAsPng(path);
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class NamedBindingToolHandler(string name, string implementationId) : KernelToolHandlerBase(
        name,
        "用于验证工具注册替换的测试工具。",
        isMutating: false,
        supportsParallelToolCalls: true,
        inputSchema: JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { },
            additionalProperties = false,
        }),
        implementationBinding: new ToolImplementationBinding(name, ToolImplementationKind.Managed, implementationId))
    {
        public override Task<KernelToolResult> ExecuteAsync(JsonElement arguments, KernelToolCallContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(Success("ok"));
        }
    }

    public sealed class TestContractToolProvider : ITianShuToolProvider
    {
        public IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context)
        {
            _ = context;
            return
            [
                new ToolDescriptor(
                    "contract_echo",
                    "Contract Echo",
                    "用于验证第三方工具 provider adapter 的测试工具。",
                    implementationBinding: new ToolImplementationBinding(
                        "contract_echo",
                        ToolImplementationKind.Managed,
                        implementationId: "test-contract-provider"),
                    inputSchema: JsonSerializer.SerializeToElement(new
                    {
                        type = "object",
                        properties = new
                        {
                            text = new { type = "string" },
                        },
                        additionalProperties = false,
                    })),
            ];
        }

        public ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context)
        {
            _ = context;
            if (!string.Equals(toolKey, "contract_echo", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unknown test tool: {toolKey}");
            }

            return new TestContractToolHandler();
        }
    }

    private static ITianShuToolHandler CreateProviderHandler(ITianShuToolProvider provider, string toolKey)
        => provider.CreateHandler(toolKey, new TianShuToolActivationContext());

    private static ToolInvocationRequest CreateProviderRequest(string toolKey, object input)
        => new(
            new CallId("call_test"),
            toolKey,
            "invoke",
            StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(input)));

    private static TianShuToolInvocationContext CreateProviderContext(
        string workingDirectory,
        ITianShuMemoryToolServices? memoryServices = null)
        => new(
            ThreadId: "thread-test",
            TurnId: "turn-test",
            WorkingDirectory: workingDirectory,
            MemoryServices: memoryServices);

    private static JsonDocument ParseProviderPayload(ToolInvocationResult result)
    {
        Assert.Null(result.Failure);
        var streamItem = Assert.Single(result.StreamItems);
        return JsonDocument.Parse(JsonSerializer.Serialize(streamItem.Payload));
    }

    private sealed class TestMemoryToolServices(
        Func<FilterMemory, CancellationToken, Task<MemoryQueryResult>>? FilterMemory = null,
        Func<ResolveMemoryOverlay, CancellationToken, Task<MemoryOverlay>>? ResolveMemoryOverlay = null,
        Func<RecordMemoryFeedback, CancellationToken, Task<MemoryMutationResult>>? RecordMemoryFeedback = null)
        : ITianShuMemoryToolServices
    {
        public Task<MemoryQueryResult> FilterMemoryAsync(FilterMemory command, CancellationToken cancellationToken)
            => FilterMemory is not null
                ? FilterMemory(command, cancellationToken)
                : throw new NotSupportedException();

        public Task<MemoryOverlay> ResolveMemoryOverlayAsync(ResolveMemoryOverlay command, CancellationToken cancellationToken)
            => ResolveMemoryOverlay is not null
                ? ResolveMemoryOverlay(command, cancellationToken)
                : throw new NotSupportedException();

        public Task<MemoryMutationResult> RecordMemoryFeedbackAsync(RecordMemoryFeedback command, CancellationToken cancellationToken)
            => RecordMemoryFeedback is not null
                ? RecordMemoryFeedback(command, cancellationToken)
                : throw new NotSupportedException();
    }

    private sealed class TestContractToolHandler : ITianShuToolHandler
    {
        public ToolDescriptor Descriptor { get; } = new(
            "contract_echo",
            "Contract Echo",
            "用于验证第三方工具 handler adapter 的测试工具。",
            implementationBinding: new ToolImplementationBinding(
                "contract_echo",
                ToolImplementationKind.Managed,
                implementationId: "test-contract-provider"),
            inputSchema: JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    text = new { type = "string" },
                },
                additionalProperties = false,
            }));

        public ValueTask<ToolInvocationResult> InvokeAsync(
            ToolInvocationRequest request,
            TianShuToolInvocationContext context,
            CancellationToken cancellationToken)
        {
            _ = context;
            _ = cancellationToken;
            return ValueTask.FromResult(new ToolInvocationResult(
                request.CallId,
                request.ToolKey,
                [
                    new ToolStreamItem("text", StructuredValue.FromString(request.Input.TryGetProperty("text", out var value) ? value?.GetString() ?? string.Empty : string.Empty), isTerminal: true),
                ]));
        }
    }

    private sealed class DiagnosticContractToolHandler : ITianShuToolHandler
    {
        public ToolDescriptor Descriptor { get; } = new(
            "contract_diagnostic",
            "Contract Diagnostic",
            "用于验证第三方工具诊断与取消 token 传递的测试工具。",
            implementationBinding: new ToolImplementationBinding(
                "contract_diagnostic",
                ToolImplementationKind.Managed,
                implementationId: "test-contract-provider"),
            inputSchema: JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { },
                additionalProperties = false,
            }));

        public async ValueTask<ToolInvocationResult> InvokeAsync(
            ToolInvocationRequest request,
            TianShuToolInvocationContext context,
            CancellationToken cancellationToken)
        {
            if (context.DiagnosticServices is not null)
            {
                await context.DiagnosticServices.ReportDiagnosticAsync(
                    new TianShuToolDiagnosticEvent(
                        request.ToolKey,
                        "contract.diagnostic",
                        "diagnostic service reached"),
                    cancellationToken).ConfigureAwait(false);
            }

            return new ToolInvocationResult(
                request.CallId,
                request.ToolKey,
                [
                    new ToolStreamItem(
                        "text",
                        StructuredValue.FromString($"cancelled={cancellationToken.IsCancellationRequested}"),
                        isTerminal: true),
                ]);
        }
    }

    private sealed class ForgedApprovalContractToolHandler : ITianShuToolHandler
    {
        public ToolDescriptor Descriptor { get; } = new(
            "contract_forged_approval",
            "Contract Forged Approval",
            "用于验证第三方工具不能通过结果反向改变 Runtime 治理分类。",
            approvalRequirement: ToolApprovalRequirement.Required,
            concurrencyClass: ToolConcurrencyClass.Exclusive,
            implementationBinding: new ToolImplementationBinding(
                "contract_forged_approval",
                ToolImplementationKind.Managed,
                implementationId: "test-contract-provider"),
            inputSchema: JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { },
                additionalProperties = false,
            }));

        public ValueTask<ToolInvocationResult> InvokeAsync(
            ToolInvocationRequest request,
            TianShuToolInvocationContext context,
            CancellationToken cancellationToken)
        {
            _ = context;
            _ = cancellationToken;
            return ValueTask.FromResult(new ToolInvocationResult(
                request.CallId,
                request.ToolKey,
                [
                    new ToolStreamItem("text", StructuredValue.FromString("success cannot grant approval"), isTerminal: true),
                ]));
        }
    }

    public sealed class TestReadFileReplacementProvider : ITianShuToolProvider
    {
        public IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context)
        {
            _ = context;
            return
            [
                TestReadFileReplacementHandler.StaticDescriptor,
            ];
        }

        public ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context)
        {
            _ = context;
            if (!string.Equals(toolKey, "read_file", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unknown test tool: {toolKey}");
            }

            return new TestReadFileReplacementHandler();
        }
    }

    private sealed class TestReadFileReplacementHandler : ITianShuToolHandler
    {
        public static ToolDescriptor StaticDescriptor { get; } = new(
            "read_file",
            "Read File Replacement",
            "用于验证第三方 provider 替换默认工具的测试工具。",
            implementationBinding: new ToolImplementationBinding(
                "read_file",
                ToolImplementationKind.Managed,
                implementationId: "test-read-file-replacement"),
            inputSchema: JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { },
                additionalProperties = false,
            }));

        public ToolDescriptor Descriptor => StaticDescriptor;

        public ValueTask<ToolInvocationResult> InvokeAsync(
            ToolInvocationRequest request,
            TianShuToolInvocationContext context,
            CancellationToken cancellationToken)
        {
            _ = context;
            _ = cancellationToken;
            return ValueTask.FromResult(new ToolInvocationResult(
                request.CallId,
                request.ToolKey,
                [
                    new ToolStreamItem("text", StructuredValue.FromString("replacement"), isTerminal: true),
                ]));
        }
    }
}
