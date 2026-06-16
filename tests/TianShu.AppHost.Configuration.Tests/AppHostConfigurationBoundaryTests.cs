namespace TianShu.AppHost.Configuration.Tests;

public sealed class AppHostConfigurationBoundaryTests
{
    [Fact]
    public void AppHostConfiguration_ProjectReferencesStayWithinConfigurationAndDiagnosticContracts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectFile = Path.Combine(
            repositoryRoot,
            "src",
            "Hosting",
            "TianShu.AppHost.Configuration",
            "TianShu.AppHost.Configuration.csproj");
        var allowedReferences = new[]
        {
            "src/Contracts/TianShu.Contracts.Catalog/TianShu.Contracts.Catalog.csproj",
            "src/Contracts/TianShu.Contracts.Sessions/TianShu.Contracts.Sessions.csproj",
            "src/Core/TianShu.Configuration/TianShu.Configuration.csproj",
        };
        var references = File.ReadAllLines(projectFile)
            .Select(TryReadProjectReferenceInclude)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Select(reference => NormalizePath(Path.GetRelativePath(
                repositoryRoot,
                Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, reference)))))
            .ToArray();

        var violations = references
            .Where(reference => !allowedReferences.Contains(reference, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "AppHost.Configuration 只能依赖配置核心与配置所需 northbound contract，不得依赖 Kernel、Execution、Provider 实现、Provider 诊断抽象或 Tool Runtime。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AppHostConfiguration_DoesNotOwnKernelOrExecutionDecisions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectRoot = Path.Combine(repositoryRoot, "src", "Hosting", "TianShu.AppHost.Configuration");
        var forbiddenTerms = new[]
        {
            "CoreIntent",
            "StageGraph",
            "RuntimeStep",
            "SourceGraphId",
            "SourceStageId",
            "SourceKernelOperationId",
            "IExecutionRuntime",
            "ExecuteAsync",
            "ProviderRuntimeBootstrapRegistry",
            "ProviderModelConnectivityProbe",
            "ToolRegistry",
            "ToolBridge",
            "ContextPolicy",
        };
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, path);
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (forbiddenTerms.Any(term => line.Contains(term, StringComparison.Ordinal)))
                {
                    violations.Add($"{relativePath}:{index + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "AppHost.Configuration 只能加载、解析、投影和迁移配置，不得拥有 Kernel 或 Execution Runtime 决策类型。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ProviderConnectivityProbe_IsOnlyConsumedByDiagnosticModelStatusSurface()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var guardedTerms = new[]
        {
            "ProviderModelConnectivityProbe",
            "ProviderModelConnectivityProbeOptions",
            "ProviderModelConnectivityProbeResult",
            "ProviderModelConnectivityProbeItem",
            "ProviderSmokeTestPlan",
        };
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, path));
            if (IsAllowedProviderDiagnosticSurface(relativePath))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (guardedTerms.Any(term => line.Contains(term, StringComparison.Ordinal)))
                {
                    violations.Add($"{relativePath}:{index + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Provider connectivity probe / smoke plan 只能作为诊断 bridge 被 model-status surface 消费，不得进入正式运行决策。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static bool IsAllowedProviderDiagnosticSurface(string relativePath)
        => relativePath.StartsWith("src/Hosting/TianShu.AppHost.Catalog/", StringComparison.Ordinal)
           || relativePath.StartsWith("src/Presentations/TianShu.Cli/Interaction/Commands/ModelStatus/", StringComparison.Ordinal);

    private static string? TryReadProjectReferenceInclude(string line)
    {
        if (!line.Contains("<ProjectReference", StringComparison.Ordinal))
        {
            return null;
        }

        const string marker = "Include=\"";
        var start = line.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = line.IndexOf('"', start);
        return end <= start ? null : line[start..end];
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

        throw new InvalidOperationException("无法从测试运行目录定位 TianShu 仓库根目录。");
    }

    private static string NormalizePath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
}
