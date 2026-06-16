namespace TianShu.AppHost.Tests;

public sealed class ContextSlicingArchitectureTests
{
    [Fact]
    public void ProviderProjects_ShouldNotReferenceContextSlicingPlannerOrSegments()
    {
        var root = FindRepositoryRoot();
        var providerFiles = Directory.EnumerateFiles(
            Path.Combine(root, "src", "Provider"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var file in providerFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("ContextSlicePlanner", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ContextSegment", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DroppedContextReason", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TurnExecutionResponsesInput_ShouldRouteThroughSlicedBuilder()
    {
        var root = FindRepositoryRoot();
        var helperPath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelTurnExecutionRuntimeHelpers.cs");
        var text = File.ReadAllText(helperPath);

        Assert.Contains("public static List<object> BuildResponsesConversationInput(", text, StringComparison.Ordinal);
        Assert.Contains("=> BuildSlicedResponsesConversationInput(", text, StringComparison.Ordinal);
        Assert.Contains("new ContextSlicePlanner", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostProviderMessages_ShouldRouteThroughContextSlicingFacade()
    {
        var root = FindRepositoryRoot();
        var appHostPath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "AppHostServer.cs");
        var helperPath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelTurnExecutionRuntimeHelpers.cs");
        var appHostSource = File.ReadAllText(appHostPath);
        var helperSource = File.ReadAllText(helperPath);

        Assert.DoesNotContain("=> KernelTurnExecutionRuntimeHelpers.BuildProviderMessages(", appHostSource, StringComparison.Ordinal);
        Assert.Contains("public static List<Dictionary<string, object?>> BuildProviderMessages(", helperSource, StringComparison.Ordinal);
        Assert.Contains("ContextSlicingRuntimeHelpers.SliceProviderMessages(", helperSource, StringComparison.Ordinal);
        Assert.Contains("int.MaxValue,", helperSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsStats_ShouldUseUnifiedSinkAndOperationScope()
    {
        var root = FindRepositoryRoot();
        var runtimePath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelTurnExecutionAppHostRuntime.cs");
        var providerRuntimePath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelProviderRequestDiagnosticsRuntime.cs");
        var contextRuntimePath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelContextSlicingDiagnosticsRuntime.cs");
        var runtimeSource = File.ReadAllText(runtimePath);
        var providerRuntimeSource = File.ReadAllText(providerRuntimePath);
        var contextRuntimeSource = File.ReadAllText(contextRuntimePath);
        var contextReportMethod = ExtractMethodBody(contextRuntimeSource, "EmitReportAsync");
        var providerCaptureMethod = ExtractMethodBody(providerRuntimeSource, "CaptureAsync");

        Assert.Contains("diagnosticOperationScopeFactory.BeginOperation", contextReportMethod, StringComparison.Ordinal);
        Assert.Contains("diagnosticEventSink.EmitAsync", contextReportMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("PersistTurnLogAsync(", contextReportMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteNotificationAsync(", contextReportMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("DiagnosticEventEnvelopeFactory.FromStats(", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ContextSlicingRuntimeHelpers.DiagnosticsNotificationMethod", runtimeSource, StringComparison.Ordinal);

        Assert.Contains("ProviderRequestDiagnosticsCapture.BuildOperationStart", providerCaptureMethod, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestDiagnosticsCapture.WritePayloadArtifactAsync", providerCaptureMethod, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestDiagnosticsCapture.BuildContextStats", providerCaptureMethod, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestDiagnosticsCapture.EmitContextStatsAsync", providerCaptureMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("PersistTurnLogAsync(", providerCaptureMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteNotificationAsync(", providerCaptureMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderRequestDiagnosticsCapture.", runtimeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostDiagnosticsComposition_ShouldUseFilteringSinkScopeAndArtifactWriter()
    {
        var root = FindRepositoryRoot();
        var appHostPath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "AppHostServer.cs");
        var compositionPath = Path.Combine(
            root,
            "src",
            "Core",
            "TianShu.RuntimeComposition",
            "DiagnosticRuntimeComposition.cs");
        var text = File.ReadAllText(appHostPath);
        var composition = File.ReadAllText(compositionPath);

        Assert.Contains("DiagnosticRuntimeComposition.CreateDiagnosticSinks(", text, StringComparison.Ordinal);
        Assert.Contains("new DefaultDiagnosticOperationScopeFactory(diagnosticEventSink)", text, StringComparison.Ordinal);
        Assert.Contains("new FilteringDiagnosticEventSink(", composition, StringComparison.Ordinal);
        Assert.Contains("new FilteringDiagnosticArtifactWriter(", composition, StringComparison.Ordinal);
        Assert.Contains("EmitRuntimeNotificationStatsAsync(method, @params, cancellationToken)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostCompositionRoot_ShouldOwnRuntimeConfigDiagnosticsMemoryToolsAndThreadSessionWiring()
    {
        var root = FindRepositoryRoot();
        var appHostPath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "AppHostServer.cs");
        var text = File.ReadAllText(appHostPath);
        var constructor = ExtractMethodBody(text, "AppHostServer");

        Assert.Contains("DiagnosticCollectionOptionsReader.FromConfig(BuildConfigReadSnapshotForRuntime(cwd: null).Config)", constructor, StringComparison.Ordinal);
        Assert.Contains("DiagnosticRuntimeComposition.CreateDiagnosticSinks(", constructor, StringComparison.Ordinal);
        Assert.Contains("new DefaultTianShuIdentityMemoryPlane(", constructor, StringComparison.Ordinal);
        Assert.Contains("TianShuHomePathUtilities.ResolveDataPathFromHome(", constructor, StringComparison.Ordinal);
        Assert.Contains("LoadExternalMemoryProviderOptions()", constructor, StringComparison.Ordinal);
        Assert.Contains("ToolRuntimeComposition.CreateDefaultToolRegistry(", constructor, StringComparison.Ordinal);
        Assert.Contains("new KernelToolRuntimeServicesAppHostRuntime(", constructor, StringComparison.Ordinal);
        Assert.Contains("new KernelThreadManager(", constructor, StringComparison.Ordinal);
        Assert.Contains("BuildThreadSessionStateForNewThread", constructor, StringComparison.Ordinal);
        Assert.Contains("BuildThreadSessionStateWithConfigLoadHandling", constructor, StringComparison.Ordinal);
        Assert.Contains("new KernelTurnExecutionAppHostRuntime(", constructor, StringComparison.Ordinal);
        Assert.Contains("diagnosticEventSink", constructor, StringComparison.Ordinal);
        Assert.Contains("diagnosticOperationScopeFactory", constructor, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeNotificationDiagnostics_ShouldExposeDirectModuleEventSurface()
    {
        var root = FindRepositoryRoot();
        var appHostPath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "AppHostServer.cs");
        var text = File.ReadAllText(appHostPath);
        var method = ExtractMethodBody(text, "EmitRuntimeNotificationStatsAsync");

        Assert.Contains("OperationCategory = operationCategory", method, StringComparison.Ordinal);
        Assert.Contains("ParameterSummary = BuildDiagnosticParameterSummary(payloadElement)", method, StringComparison.Ordinal);
        Assert.Contains("PermissionDecision =", method, StringComparison.Ordinal);
        Assert.Contains("RiskSource =", method, StringComparison.Ordinal);
        Assert.Contains("MemoryAuditId =", method, StringComparison.Ordinal);
        Assert.Contains("DiagnosticOperationId = operation.Context.OperationId", method, StringComparison.Ordinal);
        Assert.Contains("OperationName = operationCategory", method, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHostMemoryOverlay_ShouldUseIdentityMemoryPlaneBeforeRawThreadMemoryFallback()
    {
        var root = FindRepositoryRoot();
        var appHostPath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "AppHostServer.cs");
        var text = File.ReadAllText(appHostPath);
        var method = ExtractMethodBody(text, "ResolveContextOverlaySegmentsAsync");

        Assert.Contains("identityMemoryPlane.ResolveMemoryOverlayAsync", method, StringComparison.Ordinal);
        Assert.Contains("RecordMemoryOverlayCitationAsync", method, StringComparison.Ordinal);
        Assert.Contains("StateStore.GetMemoryAsync", method, StringComparison.Ordinal);
        Assert.True(
            method.IndexOf("identityMemoryPlane.ResolveMemoryOverlayAsync", StringComparison.Ordinal)
            < method.IndexOf("StateStore.GetMemoryAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void TurnExecutionRuntime_ShouldNotKeepGoogleGenerativeWireParserMethods()
    {
        var root = FindRepositoryRoot();
        var runtimePath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelTurnExecutionAppHostRuntime.cs");
        var streamProcessingRuntimePath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelResponsesStreamProcessingRuntime.cs");
        var httpTransportRuntimePath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelResponsesHttpStreamTransportRuntime.cs");
        var webSocketTransportRuntimePath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelResponsesWebSocketStreamTransportRuntime.cs");
        var runtimeSource = File.ReadAllText(runtimePath);
        var streamProcessingRuntimeSource = File.ReadAllText(streamProcessingRuntimePath);
        var transportRuntimeSource = File.ReadAllText(httpTransportRuntimePath)
            + Environment.NewLine
            + File.ReadAllText(webSocketTransportRuntimePath);

        Assert.DoesNotContain("TryReadGoogleGenerativeChunk", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildGoogleGenerativeErrorMessage", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IsRetryableGoogleGenerativeError", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AnthropicThinkingBlockAccumulator", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AnthropicToolUseBlockAccumulator", runtimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderResponsesStreamChunkParsers.Resolve", transportRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderResponsesThinkingBlockAccumulator", streamProcessingRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderResponsesToolUseBlockAccumulator", streamProcessingRuntimeSource, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
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

        throw new InvalidOperationException("repository root was not found");
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        var methodIndex = source.IndexOf($"private async Task {methodName}", StringComparison.Ordinal);
        if (methodIndex < 0)
        {
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
}
