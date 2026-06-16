namespace TianShu.AppHost.Tests;

public sealed class KernelFuzzyFileSearchUtilitiesTests
{
    [Fact]
    public void NormalizeFuzzyFileSearchRoots_ShouldFallbackAndDeduplicate()
    {
        var roots = KernelFuzzyFileSearchUtilities.NormalizeFuzzyFileSearchRoots(
            [" ", "src", "src", "tests"],
            fallbackRoot: "fallback");

        Assert.Equal(["src", "tests"], roots);

        var fallbackOnly = KernelFuzzyFileSearchUtilities.NormalizeFuzzyFileSearchRoots([], "fallback");
        Assert.Equal(["fallback"], fallbackOnly);
    }

    [Fact]
    public void CreateAndUpdateFuzzyFileSearchSession_ShouldNormalizeRootsAndQuery()
    {
        var session = KernelFuzzyFileSearchUtilities.CreateFuzzyFileSearchSession(
            "session-001",
            [" ", "src", "src"],
            query: "  kernel ",
            fallbackRoot: "fallback");

        Assert.Equal("session-001", session.SessionId);
        Assert.Equal(["src"], session.Roots);
        Assert.Equal("kernel", session.Query);

        var updated = KernelFuzzyFileSearchUtilities.UpdateFuzzyFileSearchSessionQuery(session, "  parity ");
        Assert.Equal("parity", updated.Query);
        Assert.Equal(session.Roots, updated.Roots);
    }

    [Fact]
    public void SearchFilesAcrossRoots_ShouldScoreAndReturnRelativeMatches()
    {
        var root = Directory.CreateTempSubdirectory("tianshu-fuzzy-search-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "src", "Kernel"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "docs"));
            File.WriteAllText(Path.Combine(root.FullName, "src", "Kernel", "AppHostServer.Parity.cs"), "// parity");
            File.WriteAllText(Path.Combine(root.FullName, "docs", "architecture.md"), "# docs");

            var results = KernelFuzzyFileSearchUtilities.SearchFilesAcrossRoots("Parity", [root.FullName], limit: 10);

            var match = Assert.Single(results);
            Assert.Equal(root.FullName, match.Root);
            Assert.Equal("src/Kernel/AppHostServer.Parity.cs", match.Path);
            Assert.Equal("AppHostServer.Parity.cs", match.FileName);
            Assert.True(match.Score > 0);
            Assert.NotNull(match.Indices);
            Assert.NotEmpty(match.Indices!);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void SearchFilesAcrossRoots_ShouldReturnEmpty_WhenRootsMissing()
    {
        var results = KernelFuzzyFileSearchUtilities.SearchFilesAcrossRoots("kernel", ["Z:/definitely-missing-tianshu-root"], limit: 5);

        Assert.Empty(results);
    }
}
