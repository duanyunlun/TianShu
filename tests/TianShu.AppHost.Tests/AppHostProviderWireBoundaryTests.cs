namespace TianShu.AppHost.Tests;

public sealed class AppHostProviderWireBoundaryTests
{
    [Fact]
    public void AppHostServer_ShouldNotKeepDirectProviderWirePath()
    {
        var root = FindRepositoryRoot();
        var appHostServerPath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "AppHostServer.cs");
        var diagnosticsRuntimePath = Path.Combine(
            root,
            "src",
            "Hosting",
            "TianShu.AppHost.Tools.Runtime",
            "KernelProviderRequestDiagnosticsRuntime.cs");
        var appHostServerSource = File.ReadAllText(appHostServerPath);
        var diagnosticsRuntimeSource = File.ReadAllText(diagnosticsRuntimePath);

        Assert.DoesNotContain("BuildAssistantTextAsync", appHostServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGenerateAssistantFromProviderAsync", appHostServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderRequestDiagnosticsCapture.", appHostServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderResponsesTransportProtocolBindings.Resolve(", appHostServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("providerHttpClient.SendAsync(", appHostServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtractResponsesText(", appHostServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtractChatCompletionsText(", appHostServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtractAnthropicMessagesText(", appHostServerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtractGoogleGenerativeText(", appHostServerSource, StringComparison.Ordinal);

        Assert.Contains("internal sealed class KernelProviderRequestDiagnosticsRuntime", diagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestDiagnosticsCapture.BuildOperationStart(", diagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestDiagnosticsCapture.WritePayloadArtifactAsync(", diagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestDiagnosticsCapture.BuildContextStats(", diagnosticsRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ProviderRequestDiagnosticsCapture.EmitContextStatsAsync(", diagnosticsRuntimeSource, StringComparison.Ordinal);
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
}
