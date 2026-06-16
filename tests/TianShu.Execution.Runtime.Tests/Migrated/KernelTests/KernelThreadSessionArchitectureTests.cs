using System.IO;

namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelThreadSessionArchitectureTests
{
    [Fact]
    public void AppHostServer_ShouldNotRetainLegacySessionSourceNormalizationHelper()
    {
        var appServerFile = Path.Combine(
            FindRepoRoot(),
            "src",
            "Hosting",
            "TianShu.AppHost",
            "AppHostServer.cs");

        var source = File.ReadAllText(appServerFile);

        Assert.DoesNotContain("NormalizeLegacySessionSource(", source, StringComparison.Ordinal);
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
