using System.Text.RegularExpressions;
using Xunit;

namespace TianShu.ArchitectureAnalyzers.Tests;

public sealed partial class RuntimeConfigurationBoundaryTests
{
    private static readonly string[] AllowedSourcePrefixes =
    [
        "src/Core/TianShu.Configuration/",
        "src/Contracts/TianShu.Contracts.Configuration/",
        "src/Hosting/TianShu.AppHost.Configuration/",
        "src/Core/TianShu.RuntimeComposition/",
        "src/Presentations/TianShu.ConfigGui/",
        "src/Presentations/TianShu.VSSDK.VSExtension/",
    ];

    [Fact]
    public void RuntimeSourceFiles_DoNotHardcodeUserLevelConfigurationLayout()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, path));
            if (IsAllowedSource(relativePath))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (!HardcodedUserConfigLayoutPattern().IsMatch(line)
                    && !HardcodedTianShuHomeConfigPathPattern().IsMatch(line)
                    && !HardcodedDotTianShuModuleLayoutPattern().IsMatch(line))
                {
                    continue;
                }

                violations.Add($"{relativePath}:{index + 1}: {line.Trim()}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "非配置运行时项目不得自行硬编码用户级配置布局；请改用 TianShu.Configuration / TianShu.Contracts.Configuration helper。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuntimeSourceFiles_DoNotConsumeRawUnmappedConfigurationAsExecutionInput()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, path));
            if (IsAllowedRawUnmappedSource(relativePath))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.Contains("raw.unmapped", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("RawUnmappedGroupId", StringComparison.Ordinal))
                {
                    violations.Add($"{relativePath}:{index + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Provider、Tool、Execution、Kernel 不得从 raw.unmapped 读取正式执行输入；未知字段只能停留在配置投影、迁移或展示边界。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuntimeSourceFiles_DoNotReadLegacyConfigurationAliasesAsFormalInputs()
    {
        var repositoryRoot = FindRepositoryRoot();
        var guardedRoots = new[]
        {
            Path.Combine(repositoryRoot, "src", "Provider"),
            Path.Combine(repositoryRoot, "src", "Tools"),
            Path.Combine(repositoryRoot, "src", "Execution"),
            Path.Combine(repositoryRoot, "src", "Core", "TianShu.Kernel"),
            Path.Combine(repositoryRoot, "src", "Core", "TianShu.Kernel.Abstractions"),
            Path.Combine(repositoryRoot, "src", "Core", "TianShu.Kernel.Adaptive"),
            Path.Combine(repositoryRoot, "src", "Core", "TianShu.Kernel.Strategies"),
            Path.Combine(repositoryRoot, "src", "Core", "TianShu.RuntimeComposition"),
        };
        var legacyAliases = new[]
        {
            "modelProvider",
            "mcpServers",
            "apiKey",
            "enabledTools",
            "disabledTools",
            "sandboxPolicy",
            "sandboxWorkspaceWrite",
            "networkAccess",
            "experimentalFeatures",
            "features.plugins",
            "plugins.marketplaceTrust",
        };
        var violations = new List<string>();

        foreach (var root in guardedRoots.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, path));
                var lines = File.ReadAllLines(path);
                for (var index = 0; index < lines.Length; index++)
                {
                    var line = lines[index];
                    if (!LooksLikeConfigurationRead(line))
                    {
                        continue;
                    }

                    var matchedAlias = legacyAliases.FirstOrDefault(alias => line.Contains($"\"{alias}\"", StringComparison.Ordinal));
                    if (matchedAlias is not null)
                    {
                        violations.Add($"{relativePath}:{index + 1}: {line.Trim()}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Provider、Tool、Execution、Kernel 与 RuntimeComposition 不得把旧配置字段别名读取为正式执行输入。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuntimeCompositionConfigOverrides_DoNotCanonicalizeLegacyAliasKeys()
    {
        var repositoryRoot = FindRepositoryRoot();
        var guardedSources = new[]
        {
            Path.Combine(
                repositoryRoot,
                "src",
                "Core",
                "TianShu.RuntimeComposition",
                "TianShuTomlConfigurationLoader.cs"),
            Path.Combine(
                repositoryRoot,
                "src",
                "Hosting",
                "TianShu.AppHost.Configuration",
                "KernelConfigPersistenceUtilities.cs"),
        };
        var legacyAliases = new[]
        {
            "\"mcp" + "Servers\" => \"mcp_servers\"",
            "\"model" + "Provider\" => \"provider\"",
            "\"default" + "Permissions\" => \"default_permissions\"",
            "\"api" + "Key\" => \"api_key\"",
            "\"enabled" + "Tools\" => \"enabled_tools\"",
            "\"disabled" + "Tools\" => \"disabled_tools\"",
            "\"web" + "Search\" => \"web_search\"",
        };

        foreach (var sourcePath in guardedSources)
        {
            var source = File.ReadAllText(sourcePath);
            foreach (var legacyAlias in legacyAliases)
            {
                Assert.DoesNotContain(legacyAlias, source, StringComparison.Ordinal);
            }
        }
    }

    private static bool IsAllowedSource(string relativePath)
        => relativePath.Contains("/bin/", StringComparison.Ordinal)
           || relativePath.Contains("/obj/", StringComparison.Ordinal)
           || AllowedSourcePrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.Ordinal));

    private static bool IsAllowedRawUnmappedSource(string relativePath)
        => relativePath.Contains("/bin/", StringComparison.Ordinal)
           || relativePath.Contains("/obj/", StringComparison.Ordinal)
           || relativePath.StartsWith("src/Core/TianShu.Configuration/", StringComparison.Ordinal)
           || relativePath.StartsWith("src/Contracts/TianShu.Contracts.Configuration/", StringComparison.Ordinal)
           || relativePath.StartsWith("src/Hosting/TianShu.AppHost.Configuration/", StringComparison.Ordinal)
           || relativePath.StartsWith("src/Presentations/TianShu.ConfigGui/", StringComparison.Ordinal);

    private static bool LooksLikeConfigurationRead(string line)
        => line.Contains("ReadConfig", StringComparison.Ordinal)
           || line.Contains("ReadConfigured", StringComparison.Ordinal)
           || line.Contains("ReadMerged", StringComparison.Ordinal)
           || line.Contains("ReadStructured", StringComparison.Ordinal)
           || line.Contains("TryRead", StringComparison.Ordinal);

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

    [GeneratedRegex(
        "Path\\.Combine\\([^\\r\\n;]*(?:homePath|tianShuHome|TianShuHome|TianShuHomePathUtilities|TianShuRuntimeLayoutPaths)[^\\r\\n;]*,\\s*\"(?:modules|data|runtime)",
        RegexOptions.CultureInvariant)]
    private static partial Regex HardcodedUserConfigLayoutPattern();

    [GeneratedRegex(
        "Path\\.Combine\\(\\s*(?:TianShuHomePathUtilities|TianShuRuntimeLayoutPaths)\\.ResolveTianShuHomePath\\(\\)\\s*,\\s*\"tianshu\\.toml\"\\s*\\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex HardcodedTianShuHomeConfigPathPattern();

    [GeneratedRegex(
        @"""[^""]*\.tianshu[\\/](?:modules|data|runtime)[^""]*""",
        RegexOptions.CultureInvariant)]
    private static partial Regex HardcodedDotTianShuModuleLayoutPattern();
}
