using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Contracts.Tools;
using TianShu.Tools.Artifacts;

namespace TianShu.AppHost.Tests;

public sealed class KernelArtifactsRuntimeManagerTests
{
    [Fact]
    public void ParseFreeformInput_ShouldSupportArtifactToolPragmas()
    {
        var result = KernelArtifactsRuntimeSupport.ParseFreeformInput("// tianshu-artifact-tool: timeout_ms=45000\nconsole.log(marker);");

        Assert.True(result.Success);
        Assert.NotNull(result.Request);
        Assert.Equal("console.log(marker);", result.Request!.Source);
        Assert.Equal(45_000, result.Request.TimeoutMs);
    }

    [Fact]
    public void ParseFreeformInput_ShouldRejectJsonWrappedCode()
    {
        var result = KernelArtifactsRuntimeSupport.ParseFreeformInput("{\"code\":\"console.log('ok')\"}");

        Assert.False(result.Success);
        Assert.Contains("artifacts is a freeform tool and expects raw JavaScript source", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteBuildAsync_ShouldRunAgainstCachedArtifactRuntime()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu");
        Directory.CreateDirectory(tianShuHome);
        KernelArtifactsRuntimeTestHelper.CreateFakeArtifactRuntime(tianShuHome);

        try
        {
            var manager = new KernelArtifactsRuntimeManager();
            var output = await manager.ExecuteBuildAsync(
                new KernelArtifactsExecutionRequest("console.log(marker);", null),
                new KernelArtifactsRuntimeOptions(tianShuHome, KernelArtifactsRuntimeManager.PinnedRuntimeVersion),
                root,
                CancellationToken.None);

            Assert.Equal(0, output.ExitCode);
            Assert.Contains("artifact-runtime-ok", output.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void FormatOutput_ShouldIncludeSuccessMessageWhenSilent()
    {
        var formatted = KernelArtifactsRuntimeManager.FormatOutput(new KernelArtifactCommandOutput(0, string.Empty, string.Empty));

        Assert.Contains("artifact JS completed successfully.", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildProviderResponsesToolList_ShouldHideArtifactsWhenDisabled_AndIncludeWhenEnabled()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new ArtifactToolProvider());

        using var disabledJson = JsonDocument.Parse(JsonSerializer.Serialize(registry.BuildProviderResponsesToolList()));
        Assert.DoesNotContain(disabledJson.RootElement.EnumerateArray(), static tool =>
            tool.GetProperty("name").GetString() == "artifacts");

        using var enabledJson = JsonDocument.Parse(JsonSerializer.Serialize(registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            WebSearchSupportsImageContent: false,
            ArtifactToolEnabled: true))));
        Assert.Contains(enabledJson.RootElement.EnumerateArray(), static tool =>
            tool.GetProperty("type").GetString() == "custom"
            && tool.GetProperty("name").GetString() == "artifacts"
            && tool.GetProperty("format").GetProperty("type").GetString() == "grammar");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-artifacts-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void RegisterProviderTools(KernelToolRegistry registry, ITianShuToolProvider provider)
    {
        var registrationContext = new TianShuToolRegistrationContext();
        var activationContext = new TianShuToolActivationContext();
        foreach (var descriptor in provider.DescribeTools(registrationContext))
        {
            registry.Register(new KernelContractToolHandlerAdapter(provider.CreateHandler(descriptor.Key, activationContext)));
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(120);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(120);
            }
        }

        Directory.Delete(path, recursive: true);
    }
}
