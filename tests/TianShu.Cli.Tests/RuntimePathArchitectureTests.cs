using System.Text.RegularExpressions;

namespace TianShu.Cli.Tests;

public sealed class RuntimePathArchitectureTests
{
    [Fact]
    public void PresentationSources_ShouldNotDirectlyConstructTianShuExecutionRuntime()
    {
        var presentationsRoot = Path.Combine(FindRepoRoot(), "src", "Presentations");
        var offenders = Directory.EnumerateFiles(presentationsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsUnderGeneratedDirectory(path))
            .Select(path => new
            {
                Path = path,
                Source = File.ReadAllText(path),
            })
            .Where(static item => item.Source.Contains("new TianShuExecutionRuntime(", StringComparison.Ordinal))
            .Select(static item => item.Path)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CliDefaultPath_ShouldNotBypassHostGatewayControlPlaneWithDirectKernelRuntimeConstruction()
    {
        var cliRoot = Path.Combine(FindRepoRoot(), "src", "Presentations", "TianShu.Cli");
        var forbiddenTokens = new[]
        {
            "new StableKernelCore(",
            "new AdaptiveRuntimeExecutionLoop(",
            "new ExecutionPlan(",
            "new ModelInvocationStep(",
            "new ToolInvocationStep(",
            "new ModuleCapabilityStep(",
            "new RuntimeStep(",
            "using TianShu.Kernel;",
        };
        var offenders = Directory.EnumerateFiles(cliRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsUnderGeneratedDirectory(path))
            .Select(path => new
            {
                Path = path,
                Source = File.ReadAllText(path),
            })
            .Where(item => forbiddenTokens.Any(token => item.Source.Contains(token, StringComparison.Ordinal)))
            .Select(static item => item.Path)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void PresentationSources_ShouldUseControlPlaneClientFactoryInsteadOfRuntimeBackedAdapters()
    {
        var presentationsRoot = Path.Combine(FindRepoRoot(), "src", "Presentations");
        var offenders = Directory.EnumerateFiles(presentationsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsUnderGeneratedDirectory(path))
            .Select(path => new
            {
                Path = path,
                Source = File.ReadAllText(path),
            })
            .Where(static item =>
                item.Source.Contains("new RuntimeControlPlaneAdapter(", StringComparison.Ordinal)
                || item.Source.Contains(".AsRuntimeControlPlane(", StringComparison.Ordinal)
                || item.Source.Contains(".AsControlPlane(", StringComparison.Ordinal))
            .Select(static item => item.Path)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void PresentationSources_ShouldNotLoadCoreCapabilityPackages()
    {
        var presentationsRoot = Path.Combine(FindRepoRoot(), "src", "Presentations");
        var forbiddenTokens = new[]
        {
            "ProviderPackageAssemblyPreloader.TryLoadProviderPackages",
            "ProviderRuntimeBootstrapRegistry.Reload",
            "ProviderResponsesComponentBootstraps.Reload",
            "KernelToolRegistryFactory.CreateDefaultRegistry",
            "AddConfiguredToolPackages(",
        };
        var offenders = Directory.EnumerateFiles(presentationsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsUnderGeneratedDirectory(path))
            .Select(path => new
            {
                Path = path,
                Source = File.ReadAllText(path),
            })
            .Where(item => forbiddenTokens.Any(token => item.Source.Contains(token, StringComparison.Ordinal)))
            .Select(static item => item.Path)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CliAndSidecarEntryPoints_ShouldUseAppHostRuntimeClientFactory()
    {
        var root = FindRepoRoot();
        var requiredFiles = new[]
        {
            Path.Combine(root, "src", "Presentations", "TianShu.Cli", "InteractiveChatRunner.cs"),
            Path.Combine(root, "src", "Presentations", "TianShu.Cli", "Commands", "Runners", "CliRuntimeCommandRunner.cs"),
            Path.Combine(root, "src", "Presentations", "TianShu.Cli", "Commands", "Runners", "Send", "SendCommandRunner.cs"),
            Path.Combine(root, "src", "Presentations", "TianShu.Cli", "Commands", "Runners", "Exec", "ExecCommandRunner.cs"),
            Path.Combine(root, "src", "Presentations", "TianShu.Cli", "Commands", "Runners", "Conversation", "ConversationTurnCommandRunner.cs"),
            Path.Combine(root, "src", "Presentations", "TianShu.Cli", "Interaction", "Host", "InteractiveChatSessionHost.cs"),
            Path.Combine(root, "src", "Presentations", "TianShu.VSSDK.Sidecar", "StdioSidecarHost.cs"),
        };

        foreach (var file in requiredFiles)
        {
            var source = File.ReadAllText(file);
            Assert.Contains("TianShuAppHostRuntimeClientFactory.Create", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CliBootstrapper_ShouldApplyAppHostLaunchAndTianShuConfigToRuntimeOptions()
    {
        var source = ReadRepoFile("src", "Presentations", "TianShu.Cli", "Runtime", "Bootstrap", "CliRuntimeBootstrapper.cs");

        Assert.Contains("CliAppHostLaunchResolver.Resolve(", source, StringComparison.Ordinal);
        Assert.Contains("CliAppHostLaunchResolver.ApplyToRuntimeOptions(runtimeOptions, appHostResolution)", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeConfigurationComposition.ApplyToOptions(runtimeOptions, resolvedConfig)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostRuntimeClient_ShouldLaunchAppServerCompanionOverStdio()
    {
        var source = ReadRepoFile("src", "Execution", "TianShu.Execution.Runtime", "TianShuExecutionRuntime.cs");
        var method = ExtractMethodBody(source, "BuildArguments");

        Assert.Contains("\"app-server\"", method, StringComparison.Ordinal);
        Assert.Contains("\"--listen\"", method, StringComparison.Ordinal);
        Assert.Contains("\"stdio://\"", method, StringComparison.Ordinal);
        Assert.Contains("ResolveAppHostProjectPath(options)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void SendCommandRunner_DefaultPathSelection_UsesRuntimeCompositionDecision()
    {
        var source = ReadRepoFile(
            "src",
            "Presentations",
            "TianShu.Cli",
            "Commands",
            "Runners",
            "Send",
            "SendCommandRunner.cs");

        Assert.Contains("KernelRuntimeTurnPathSelector.Decide(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (probeOptions.KernelRuntimeLoop", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!probeOptions.KernelRuntimeLoop", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SendCommandRunner_AppHostControlPlanePath_ShouldBeRemovedFromProductPath()
    {
        var source = ReadRepoFile(
            "src",
            "Presentations",
            "TianShu.Cli",
            "Commands",
            "Runners",
            "Send",
            "SendCommandRunner.cs");

        Assert.Contains("if (turnPathDecision.UseKernelRuntimeLoop)", source, StringComparison.Ordinal);
        Assert.Contains("KernelRuntimeTurnPathSelector.Decide(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("explicitAppHostControlPlaneRequested: probeOptions.AppHostControlPlane", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (turnPathDecision.UseAppHostControlPlane)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("apphost-control-plane", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelTurnExecutionAppHostRuntime", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelTurnExecutionRuntimeComposition", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using TianShu.AppHost.Tools.Runtime;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyAppHostTurnLoop_ShouldNotExposeCompatibilityBoundaryMarker()
    {
        var repoRoot = FindRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsUnderGeneratedDirectory(path))
            .Select(path => new
            {
                Path = path,
                Source = File.ReadAllText(path),
            })
            .Where(static item => item.Source.Contains("LEGACY_APPHOST_TURN_LOOP_COMPATIBILITY_BOUNDARY", StringComparison.Ordinal))
            .Select(static item => item.Path)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void RuntimeCompositionNewLoopSources_ShouldNotReferenceLegacyAppHostTurnLoop()
    {
        var repoRoot = FindRepoRoot();
        var runtimeCompositionRoot = Path.Combine(repoRoot, "src", "Core", "TianShu.RuntimeComposition");
        var newLoopSourceFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AdaptiveRuntimeExecutionLoop.cs",
            "KernelRuntimeExecutionLoopModels.cs",
            "KernelRuntimeTurnLoopBridge.cs",
            "KernelRuntimeTurnLoopComposition.cs",
            "KernelRuntimeTurnLoopProjection.cs",
            "KernelRuntimeTurnPathSelector.cs",
        };
        var forbiddenTokens = new[]
        {
            "using TianShu.AppHost.Tools.Runtime;",
            "KernelTurnExecutionAppHostRuntime",
            "KernelTurnExecutionRuntimeComposition",
            "KernelTurnProviderAssistantRuntime",
            "KernelResponsesToolContinuationRuntime",
        };

        var offenders = Directory.EnumerateFiles(runtimeCompositionRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => newLoopSourceFileNames.Contains(Path.GetFileName(path)))
            .Select(path => new
            {
                Path = path,
                Source = File.ReadAllText(path),
            })
            .Where(item => forbiddenTokens.Any(token => item.Source.Contains(token, StringComparison.Ordinal)))
            .Select(static item => item.Path)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SendCommandRunner_DoesNotReferenceKernelRuntimeInternalsDirectly()
    {
        var source = ReadRepoFile(
            "src",
            "Presentations",
            "TianShu.Cli",
            "Commands",
            "Runners",
            "Send",
            "SendCommandRunner.cs");
        var forbiddenIdentifiers = new[]
        {
            "CoreIntent",
            "StageGraph",
            "RuntimeStep",
            "StableKernelCore",
            "AdaptiveRuntimeExecutionLoop",
            "TianShuExecutionRuntime",
        };
        var violations = forbiddenIdentifiers
            .Where(identifier => ContainsIdentifier(source, identifier))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "CLI send runner 不得直接引用 Kernel/Runtime 内部对象："
            + string.Join(", ", violations));
    }

    [Fact]
    public void InteractiveChatSessionHost_ShouldTreatThreadSlashCommandsAsControlPlaneOutput()
    {
        var source = ReadRepoFile(
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Host",
            "InteractiveChatSessionHost.cs");
        var threadsMethod = ExtractMethodBody(source, "HandleThreadsCommandAsync");

        Assert.Contains("WriteControlPlaneLine(\"未找到线程。\")", threadsMethod, StringComparison.Ordinal);
        Assert.Contains("WriteControlPlaneLine(SelectionPickerRowRenderer.BuildThreadListRow(thread, showAll))", threadsMethod, StringComparison.Ordinal);
        Assert.Contains("(message, isError) => WriteControlPlaneLine(message, isError)", source, StringComparison.Ordinal);
    }

    private static bool IsUnderGeneratedDirectory(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(static segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TianShu.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private static string ReadRepoFile(params string[] segments)
        => File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(segments).ToArray()));

    private static string ExtractMethodBody(string source, string methodName)
    {
        var methodIndex = -1;
        var searchIndex = 0;
        while (methodIndex < 0)
        {
            var candidate = source.IndexOf(methodName + "(", searchIndex, StringComparison.Ordinal);
            if (candidate < 0)
            {
                break;
            }

            var prefixStart = Math.Max(0, candidate - 120);
            var prefix = source[prefixStart..candidate];
            if (prefix.Contains("private ", StringComparison.Ordinal)
                || prefix.Contains("public ", StringComparison.Ordinal)
                || prefix.Contains("internal ", StringComparison.Ordinal))
            {
                methodIndex = candidate;
                break;
            }

            searchIndex = candidate + methodName.Length;
        }

        if (methodIndex < 0)
        {
            throw new InvalidOperationException($"Method '{methodName}' was not found.");
        }

        var bodyStart = source.IndexOf('{', methodIndex);
        if (bodyStart < 0)
        {
            throw new InvalidOperationException($"Method '{methodName}' body was not found.");
        }

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[bodyStart..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Method '{methodName}' body was not closed.");
    }

    private static bool ContainsIdentifier(string source, string identifier)
        => Regex.IsMatch(
            source,
            $@"(?<![A-Za-z0-9_]){Regex.Escape(identifier)}(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);
}
