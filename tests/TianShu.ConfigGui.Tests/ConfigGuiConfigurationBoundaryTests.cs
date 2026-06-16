namespace TianShu.ConfigGui.Tests;

public sealed class ConfigGuiConfigurationBoundaryTests
{
    private static readonly string[] ForbiddenRuntimeTokens =
    [
        "TianShu.Kernel",
        "TianShu.RuntimeComposition",
        "TianShu.Execution.Runtime",
        "TianShu.HostGateway",
        "CoreIntent",
        "StageGraph",
        "RuntimeStep",
        "StableKernelCore",
        "AdaptiveRuntimeExecutionLoop",
        "KernelRuntimeProductTerminalProjection",
    ];

    [Fact]
    public void ConfigGui_UsesProjectionAndPreviewApplyContractsForSchemaEditing()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Presentations", "TianShu.ConfigGui", "Program.cs"));

        Assert.Contains("loader.LoadUserFileWithModules(ConfigPath)", programSource, StringComparison.Ordinal);
        Assert.Contains("applier.ApplyRouted(ConfigPath", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityReaders", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelConfigCompatibilityUtilities", programSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigGui_DoesNotAllowRawUnmappedFieldsToBeEdited()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Presentations", "TianShu.ConfigGui", "Program.cs"));

        Assert.Contains("RawUnmappedGroupId", programSource, StringComparison.Ordinal);
        Assert.Contains("public bool CanEdit => EditMode != ConfigurationFieldEditMode.ReadOnly", programSource, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(GroupId, TianShuConfigurationSchemaCatalog.RawUnmappedGroupId", programSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigGui_DoesNotWriteProviderSecretPlaintextFields()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src", "Presentations", "TianShu.ConfigGui");
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, path);
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.Contains("api_key =", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("api_key_secret =", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{relativePath}:{index + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ConfigGUI 不得写入 provider secret 明文字段；只能写入 *_env / *_ref 这类 secret reference。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ConfigGui_DoesNotReferenceHostGatewayKernelOrRuntimeInternals()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src", "Presentations", "TianShu.ConfigGui");
        var projectPath = Path.Combine(sourceRoot, "TianShu.ConfigGui.csproj");
        var violations = FindForbiddenTokens(repositoryRoot, sourceRoot, ForbiddenRuntimeTokens).ToList();

        var projectSource = File.ReadAllText(projectPath);
        foreach (var token in ForbiddenRuntimeTokens)
        {
            if (projectSource.Contains(token, StringComparison.Ordinal))
            {
                violations.Add($"{Path.GetRelativePath(repositoryRoot, projectPath)}: project reference contains forbidden token '{token}'.");
            }
        }

        Assert.True(
            violations.Count == 0,
            "ConfigGUI 是配置编辑宿主，只能消费配置 projection / preview / apply 契约，不得引用 HostGateway、Kernel 或 Runtime 内部对象。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> FindForbiddenTokens(string repositoryRoot, string sourceRoot, IReadOnlyList<string> forbiddenTokens)
    {
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGeneratedOutput(path))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(repositoryRoot, path);
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                foreach (var token in forbiddenTokens)
                {
                    if (line.Contains(token, StringComparison.Ordinal))
                    {
                        yield return $"{relativePath}:{index + 1}: forbidden token '{token}' in '{line.Trim()}'.";
                    }
                }
            }
        }
    }

    private static bool IsGeneratedOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase));
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
}
