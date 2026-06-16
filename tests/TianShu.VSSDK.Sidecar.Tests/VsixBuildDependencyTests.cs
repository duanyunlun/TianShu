using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace TianShu.VSSDK.Sidecar.Tests;

public sealed class VsixBuildDependencyTests
{
    [Fact]
    public void VsixProject_DeclaresSidecarBuildTarget()
    {
        var projectPath = Path.Combine(
            FindRepoRoot(),
            "src",
            "Presentations",
            "TianShu.VSSDK.VSExtension",
            "TianShu.VSSDK.VSExtension.csproj");

        var document = XDocument.Load(projectPath);
        var target = document
            .Descendants()
            .FirstOrDefault(
                element => element.Name.LocalName == "Target"
                    && string.Equals((string?)element.Attribute("Name"), "BuildTianShuSidecar", StringComparison.Ordinal));

        Assert.NotNull(target);

        var msbuildTask = target!
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "MSBuild");

        Assert.NotNull(msbuildTask);
        Assert.Equal(
            @"..\TianShu.VSSDK.Sidecar\TianShu.VSSDK.Sidecar.csproj",
            (string?)msbuildTask!.Attribute("Projects"));
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
}
