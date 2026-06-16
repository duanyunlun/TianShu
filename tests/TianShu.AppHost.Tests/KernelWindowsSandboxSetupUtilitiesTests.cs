using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tests;

public sealed class KernelWindowsSandboxSetupUtilitiesTests
{
    [Theory]
    [InlineData("elevated", "elevated")]
    [InlineData("ElEvAtEd", "elevated")]
    [InlineData("unelevated", "unelevated")]
    public void TryNormalizeSetupMode_ShouldNormalizeKnownModes(string rawMode, string expected)
    {
        var parsed = KernelWindowsSandboxSetupUtilities.TryNormalizeSetupMode(rawMode, out var mode);

        Assert.True(parsed);
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    public void TryNormalizeSetupMode_ShouldRejectUnknownModes(string? rawMode)
    {
        var parsed = KernelWindowsSandboxSetupUtilities.TryNormalizeSetupMode(rawMode, out var mode);

        Assert.False(parsed);
        Assert.Null(mode);
    }

    [Fact]
    public void BuildWindowsSandboxConfigXml_ShouldEscapeHostFolderAndToggleNetworking()
    {
        var setupRoot = Path.Combine(Path.GetTempPath(), "sandbox&root", "<demo>");

        var xml = KernelWindowsSandboxSetupUtilities.BuildWindowsSandboxConfigXml(setupRoot, "elevated");

        Assert.Contains("<Networking>Enable</Networking>", xml, StringComparison.Ordinal);
        Assert.Contains("&amp;", xml, StringComparison.Ordinal);
        Assert.Contains("&lt;demo&gt;", xml, StringComparison.Ordinal);
        Assert.Contains("C:\\TianShuSandbox\\bootstrap.ps1", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareWindowsSandboxArtifactsAsync_ShouldWriteBootstrapMetadataAndWsb()
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-wsb-artifacts", Guid.NewGuid().ToString("N"));
        var storageRoot = Path.Combine(root, "storage");
        var windowsRoot = Path.Combine(root, "windows");
        Directory.CreateDirectory(Path.Combine(windowsRoot, "System32"));
        File.WriteAllText(Path.Combine(windowsRoot, "System32", "WindowsSandbox.exe"), string.Empty);

        try
        {
            var setupRoot = await KernelWindowsSandboxSetupUtilities.PrepareWindowsSandboxArtifactsAsync(
                storageRoot,
                "unelevated",
                @"D:\Demo",
                CancellationToken.None,
                windowsRoot);

            var bootstrapPath = Path.Combine(setupRoot, "bootstrap.ps1");
            var wsbPath = Path.Combine(setupRoot, "tianshu.wsb");
            var metadataPath = Path.Combine(setupRoot, "setup-metadata.json");

            Assert.True(File.Exists(bootstrapPath));
            Assert.True(File.Exists(wsbPath));
            Assert.True(File.Exists(metadataPath));
            Assert.Contains("Mode: unelevated", await File.ReadAllTextAsync(bootstrapPath));
            Assert.Contains("<Networking>Disable</Networking>", await File.ReadAllTextAsync(wsbPath));

            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
            Assert.Equal("unelevated", metadata.RootElement.GetProperty("mode").GetString());
            Assert.Equal(@"D:\Demo", metadata.RootElement.GetProperty("cwd").GetString());
            Assert.EndsWith("WindowsSandbox.exe", metadata.RootElement.GetProperty("sandboxExe").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProbeWindowsSandboxAvailabilityAsync_ShouldUseExpectedCommandAndReturnSuccess()
    {
        IReadOnlyList<string>? observedCommand = null;

        var result = await KernelWindowsSandboxSetupUtilities.ProbeWindowsSandboxAvailabilityAsync(
            (command, _) =>
            {
                observedCommand = command;
                return Task.FromResult(new KernelWindowsSandboxProbeCommandResult(0, "Enabled", string.Empty));
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(observedCommand);
        Assert.Equal(KernelWindowsSandboxSetupUtilities.BuildWindowsSandboxProbeCommand(), observedCommand);
    }

    [Fact]
    public async Task RunWindowsSandboxSetupAsync_ShouldPropagateProbeFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-wsb-run", Guid.NewGuid().ToString("N"));

        try
        {
            var result = await KernelWindowsSandboxSetupUtilities.RunWindowsSandboxSetupAsync(
                "elevated",
                cwd: null,
                storageRootDirectory: root,
                (_, _) => Task.FromResult(new KernelWindowsSandboxProbeCommandResult(1, string.Empty, "boom")),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("probe_failed:boom", result.Error);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
