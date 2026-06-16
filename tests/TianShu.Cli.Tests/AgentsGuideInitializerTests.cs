using TianShu.Cli.Interaction.Commands.Init;

namespace TianShu.Cli.Tests;

public sealed class AgentsGuideInitializerTests
{
    [Fact]
    public void BuildRequest_WhenAgentsGuideExists_SkipsWithoutOverwriting()
    {
        using var workspace = new TempWorkspace();
        var existingPath = Path.Combine(workspace.RootPath, AgentsGuideInitializer.AgentsGuideFileName);
        File.WriteAllText(existingPath, "existing-guide");

        var request = new AgentsGuideInitializer().BuildRequest(workspace.RootPath, CancellationToken.None);

        Assert.False(request.ShouldSubmitPrompt);
        Assert.Null(request.Prompt);
        Assert.Equal(existingPath, request.TargetPath);
        Assert.Equal("AGENTS.md already exists here. Skipping /init to avoid overwriting it.", request.Message);
        Assert.Equal("existing-guide", File.ReadAllText(existingPath));
    }

    [Fact]
    public void BuildRequest_WhenAgentsGuideDoesNotExist_ReturnsInitializationPrompt()
    {
        using var workspace = new TempWorkspace();

        var request = new AgentsGuideInitializer().BuildRequest(workspace.RootPath, CancellationToken.None);

        Assert.True(request.ShouldSubmitPrompt);
        Assert.Equal(Path.Combine(workspace.RootPath, AgentsGuideInitializer.AgentsGuideFileName), request.TargetPath);
        Assert.Null(request.Message);
        Assert.Contains("Generate a file named AGENTS.md", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("- Title the document \"Repository Guidelines\".", request.Prompt, StringComparison.Ordinal);
        Assert.False(File.Exists(request.TargetPath));
    }

    [Fact]
    public void BuildRequest_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var workspace = new TempWorkspace();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new AgentsGuideInitializer().BuildRequest(workspace.RootPath, cancellation.Token));
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "tianshu-cli-init-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
