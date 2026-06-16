using System.IO;
using System.Text.RegularExpressions;

namespace TianShu.Execution.Integration.Tests;

public sealed class RepositoryStructureArchitectureTests
{
    private static readonly SolutionProjectAllowlistItem[] SolutionProjectAllowlist =
    [
        new(
            "src/Presentations/TianShu.VSSDK.VSExtension/TianShu.VSSDK.VSExtension.csproj",
            "vsix-product-track",
            "tools/Build-TianShuVsix.ps1",
            "VSIX 扩展需要 Visual Studio SDK / VSIX 专用构建链，暂不纳入 TianShu.sln 的普通 Release baseline。"),
        new(
            "src/Features/WindowsAgent/src/Windows.Agent/Windows.Agent.csproj",
            "experimental-feature",
            "tools/Run-TianShuVsixUiSmoke.ps1 或 WindowsAgent 专项 solution",
            "WindowsAgent 仍是独立实验 feature 子树，未进入 TianShu 主线 solution。"),
        new(
            "src/Features/WindowsAgent/src/Windows.Agent.Cli/Windows.Agent.Cli.csproj",
            "experimental-feature",
            "tools/Run-TianShuVsixUiSmoke.ps1 或 WindowsAgent 专项 solution",
            "WindowsAgent CLI 随 WindowsAgent 实验轨道验证，未进入 TianShu 主线 solution。"),
        new(
            "src/Features/WindowsAgent/src/Windows.Agent.Cli.Alias/Windows.Agent.Cli.Alias.csproj",
            "experimental-feature",
            "WindowsAgent 专项 solution",
            "WindowsAgent CLI alias 属于实验 feature 附属入口，未进入 TianShu 主线 solution。"),
        new(
            "src/Features/WindowsAgent/src/Windows.Agent.Cli.Test/Windows.Agent.Cli.Test.csproj",
            "experimental-feature-test",
            "WindowsAgent 专项 test baseline",
            "WindowsAgent CLI 测试随实验 feature 专项基线验证，未进入 TianShu 主线 solution。"),
        new(
            "src/Features/WindowsAgent/src/Windows.Agent.Test/Windows.Agent.Test.csproj",
            "experimental-feature-test",
            "WindowsAgent 专项 test baseline",
            "WindowsAgent 核心测试随实验 feature 专项基线验证，未进入 TianShu 主线 solution。"),
        new(
            "src/Features/WindowsAgent/src/Windows.Agent.UiaTestApp/Windows.Agent.UiaTestApp.csproj",
            "experimental-feature-fixture",
            "WindowsAgent UIA 专项 smoke test",
            "WindowsAgent UIA fixture 只服务实验 feature 的 UI 自动化验证，未进入 TianShu 主线 solution。"),
    ];

    private static readonly LegacyBrandAllowlistItem[] LegacyBrandAllowlist =
    [
        new(
            "AGENTS.md",
            ["Codex", "codex", "Claude", "claude"],
            "agent-operator-guidance",
            "仓库代理协作规则会区分 Codex 记忆与参考材料，属于开发者协作说明，不是 TianShu 产品身份。"),
        new(
            "docs/reference/codex-architecture-reference.md",
            ["Codex", "codex"],
            "historical-architecture-reference",
            "Codex 只作为历史架构参考文档保留，文档已声明不得依赖本地子仓库源码路径。"),
        new(
            "docs/reference/claudecode-architecture-reference.md",
            ["Claude", "claude", "Codex", "codex"],
            "historical-architecture-reference",
            "Claude Code 只作为历史架构参考文档保留，文档已声明不得依赖本地子仓库源码路径。"),
        new(
            "docs/tianshu-architecture-spec.md",
            ["Codex", "codex", "Claude", "claude", "OpenAgent", "openagent"],
            "architecture-compatibility-and-prohibition",
            "主架构文档只允许在历史参考、外部协议命名、真实模型名、禁止旧配置来源等语境中出现旧品牌词。"),
        new(
            "docs/architecture/tianshu-planes-architecture.md",
            ["Codex", "codex", "Claude", "claude", "OpenAgent", "openagent"],
            "architecture-compatibility-and-prohibition",
            "架构分层文档只允许在历史参考、外部协议命名、真实模型名、禁止旧配置来源等语境中出现旧品牌词。"),
        new(
            "docs/architecture/tianshu-contracts-architecture.md",
            ["Codex", "codex", "Claude", "claude", "OpenAgent", "openagent"],
            "contracts-compatibility-and-prohibition",
            "Contracts 文档只允许在历史参考、外部协议命名、真实模型名、禁止旧配置来源等语境中出现旧品牌词。"),
        new(
            "docs/config/tianshu-config-schema.md",
            ["Codex", "codex", "Claude", "claude", "OpenAgent", "openagent"],
            "configuration-compatibility-and-prohibition",
            "配置 schema 文档只允许在禁止旧配置来源、真实模型/协议兼容说明和迁移历史说明中出现旧品牌词。"),
        new(
            "docs/cli/tianshu-cli-interaction-design.md",
            ["Codex", "codex", "Claude", "claude"],
            "interaction-reference-and-prohibition",
            "CLI 交互设计只允许在明确禁止沿用旧视觉/业务逻辑或说明历史参考时出现旧品牌词。"),
        new(
            "docs/tianshu-implementation-tracker.md",
            ["Codex", "codex", "Claude", "claude", "OpenAgent", "openagent"],
            "tracker-history",
            "tracker 会记录已清理的旧品牌残留、合法保留规则和历史验证命令。"),
        new(
            "docs/天枢最终验收案例.md",
            ["Codex", "codex"],
            "historical-acceptance-case",
            "验收案例保留早期迁移/对比任务描述，作为历史样例，不作为当前产品身份。"),
        new(
            "src/Provider/**",
            ["Codex", "codex", "Claude", "claude"],
            "provider-wire-compatibility",
            "Provider 层允许保留真实模型名、OpenAI wire 字段、codex_apps 兼容端点和 Claude/Anthropic 模型族启发。"),
        new(
            "src/Core/TianShu.Configuration/TianShuConfigurationSchemaCatalog.cs",
            ["Claude", "claude"],
            "model-family-description",
            "配置 schema 中的 Claude 只作为真实 Anthropic 模型族说明出现，用于解释 protocol 选项适用范围。"),
        new(
            "src/Hosting/TianShu.AppHost/AppHostServer.cs",
            ["Codex", "codex", "Claude", "claude"],
            "runtime-model-compatibility",
            "AppHost 中旧品牌词只允许作为真实模型名或 provider/model 兼容判断出现。"),
        new(
            "src/Hosting/TianShu.AppHost/Program.cs",
            ["Codex", "codex"],
            "legacy-protocol-generator-path",
            "app-server schema 生成器仍保留历史协议参考路径作为显式开发工具入口，不参与默认运行路径。"),
        new(
            "src/Core/TianShu.Configuration/KernelModelProtocolResolver.cs",
            ["Claude", "claude"],
            "model-family-detection",
            "Claude 作为真实 Anthropic 模型族名称参与 protocol 解析。"),
        new(
            "src/Hosting/TianShu.AppHost.Tools.Runtime/**",
            ["Codex", "codex", "Claude", "claude"],
            "tool-runtime-wire-compatibility",
            "工具运行时只允许保留 codex_apps、codex_approval_kind 等外部 wire 兼容字段和 Claude 模型族启发。"),
        new(
            "src/Presentations/TianShu.Cli/Interaction/Commands/ModelStatus/ModelStatusCommandHandler.cs",
            ["Claude", "claude"],
            "model-status-family-detection",
            "Claude 作为真实模型族名称参与模型状态探测展示。"),
        new(
            "src/Presentations/TianShu.VSSDK.VSExtension/Services/TianShuSidecarBridge.cs",
            ["Codex", "codex"],
            "sidecar-wire-compatibility",
            "VSSDK sidecar 仅保留上游 wire payload 中的真实字段名。"),
        new(
            "tests/**",
            ["Codex", "codex", "Claude", "claude", "OpenAgent", "openagent"],
            "test-fixture-or-migration-baseline",
            "测试中允许保留真实模型名、wire 兼容字段、历史迁移 fixture 和负向断言。"),
    ];

    [Fact]
    public void TianShuSolution_ShouldOnlyCarryFormalMainlineProjects()
    {
        var solutionPath = Path.Combine(FindRepoRoot(), "TianShu.sln");
        var solutionText = File.ReadAllText(solutionPath);

        Assert.DoesNotContain(@"src\Features\", solutionText, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenWorkbench.", solutionText, StringComparison.Ordinal);
        Assert.Contains(@"src\Provider\TianShu.Provider.Abstractions\TianShu.Provider.Abstractions.csproj", solutionText, StringComparison.Ordinal);
        Assert.Contains(@"src\Provider\TianShu.Provider.OpenAI\TianShu.Provider.OpenAI.csproj", solutionText, StringComparison.Ordinal);
    }

    [Fact]
    public void SolutionExternalProjects_ShouldBeExplicitlyClassified()
    {
        var repoRoot = FindRepoRoot();
        var solutionProjects = ReadSolutionProjects(repoRoot);
        var allowlist = ReadSolutionProjectAllowlist();
        var allProjects = Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !IsBuildResiduePath(path))
            .Select(path => NormalizeRepoPath(repoRoot, path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        var missing = allProjects
            .Where(path => !solutionProjects.Contains(path))
            .Where(path => !allowlist.ContainsKey(path))
            .ToArray();
        var staleAllowlist = allowlist.Keys
            .Where(path => !allProjects.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var invalidAllowlist = allowlist
            .Where(static pair =>
                string.IsNullOrWhiteSpace(pair.Value.Classification)
                || string.IsNullOrWhiteSpace(pair.Value.Verification)
                || string.IsNullOrWhiteSpace(pair.Value.Reason))
            .Select(static pair => pair.Key)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Solution 外项目必须登记到结构守卫测试内置分类基线：{string.Join(", ", missing)}");
        Assert.True(
            staleAllowlist.Length == 0,
            $"Solution 外项目分类基线存在失效路径：{string.Join(", ", staleAllowlist)}");
        Assert.True(
            invalidAllowlist.Length == 0,
            $"Solution 外项目分类基线必须说明 classification / verification / reason：{string.Join(", ", invalidAllowlist)}");
    }

    [Fact]
    public void LegacyBrandTerms_ShouldBeClassifiedByAllowlist()
    {
        var repoRoot = FindRepoRoot();
        var allowlist = ReadLegacyBrandAllowlist();
        var terms = new[] { "Codex", "codex", "Claude", "claude", "OpenAgent", "openagent" };
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(repoRoot, "*", SearchOption.AllDirectories)
                     .Where(static path => !IsBuildResiduePath(path))
                     .Where(static path => IsTextFileForBrandScan(path))
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            var repoPath = NormalizeRepoPath(repoRoot, file);
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var matchedTerms = terms
                    .Where(term => lines[index].Contains(term, StringComparison.Ordinal))
                    .ToArray();
                if (matchedTerms.Length == 0)
                {
                    continue;
                }

                foreach (var term in matchedTerms)
                {
                    if (!allowlist.Any(item => item.Matches(repoPath, term)))
                    {
                        violations.Add($"{repoPath}:{index + 1}:{term}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "旧品牌词必须登记到结构守卫测试内置分类基线，避免用户可见身份或内部命名继续扩散："
            + string.Join(", ", violations.Take(40)));

        var invalidAllowlist = allowlist
            .Where(static item =>
                string.IsNullOrWhiteSpace(item.PathPattern)
                || item.Terms.Count == 0
                || string.IsNullOrWhiteSpace(item.Classification)
                || string.IsNullOrWhiteSpace(item.Reason))
            .Select(static item => item.PathPattern)
            .ToArray();
        Assert.True(
            invalidAllowlist.Length == 0,
            $"旧品牌分类基线必须说明 path_pattern / terms / classification / reason：{string.Join(", ", invalidAllowlist)}");
    }

    [Fact]
    public void ProviderDirectory_ShouldOnlyRetainFormalTopLevelProjects()
    {
        var providerRoot = Path.Combine(FindRepoRoot(), "src", "Provider");
        var directories = Directory.GetDirectories(providerRoot)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "TianShu.Provider.Abstractions",
                "TianShu.Provider.Anthropic",
                "TianShu.Provider.Google",
                "TianShu.Provider.OpenAI",
                "TianShu.Provider.OpenAICompatible",
            },
            directories);
    }

    [Fact]
    public void FeaturesDirectory_ShouldOnlyKeepWindowsAgentAsTrackedFeatureSubtree()
    {
        var featuresRoot = Path.Combine(FindRepoRoot(), "src", "Features");
        var readmePath = Path.Combine(featuresRoot, "Readme.md");
        var readmeText = File.ReadAllText(readmePath);

        Assert.Contains("WindowsAgent/", readmeText, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenWorkbench.Feature.CommonCapabilities", readmeText, StringComparison.Ordinal);

        var extraFeatureDirectories = Directory.GetDirectories(featuresRoot)
            .Where(static path => !string.Equals(Path.GetFileName(path), "WindowsAgent", StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        foreach (var directory in extraFeatureDirectories)
        {
            var nonBuildResidueFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => !IsBuildResidue(path, directory))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                nonBuildResidueFiles.Length == 0,
                $"额外 feature 目录 `{Path.GetFileName(directory)}` 不应残留真实源码/项目文件：{string.Join(", ", nonBuildResidueFiles.Select(path => Path.GetRelativePath(featuresRoot, path)))}");
        }
    }

    [Fact]
    public void PresentationsDirectory_ShouldOnlyKeepFormalTianShuProjectsOrBuildResidue()
    {
        var presentationsRoot = Path.Combine(FindRepoRoot(), "src", "Presentations");
        var formalDirectories = new HashSet<string>(StringComparer.Ordinal)
        {
            "TianShu.Cli",
            "TianShu.ConfigGui",
            "TianShu.VSSDK.Sidecar",
            "TianShu.VSSDK.VSExtension",
        };

        var extraDirectories = Directory.GetDirectories(presentationsRoot)
            .Where(path => !formalDirectories.Contains(Path.GetFileName(path)))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        foreach (var directory in extraDirectories)
        {
            var nonBuildResidueFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => !IsBuildResidue(path, directory))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                nonBuildResidueFiles.Length == 0,
                $"额外 presentation 目录 `{Path.GetFileName(directory)}` 不应残留真实源码/项目文件：{string.Join(", ", nonBuildResidueFiles.Select(path => Path.GetRelativePath(presentationsRoot, path)))}");
        }
    }

    [Fact]
    public void HostingDirectory_ShouldOnlyKeepCurrentAppHostFamily()
    {
        var hostingRoot = Path.Combine(FindRepoRoot(), "src", "Hosting");
        var directories = Directory.GetDirectories(hostingRoot)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "TianShu.AppHost",
                "TianShu.AppHost.Configuration",
                "TianShu.AppHost.State",
                "TianShu.AppHost.Tools",
                "TianShu.AppHost.Tools.Runtime",
            },
            directories);
    }

    private static bool IsBuildResidue(string path, string featureDirectory)
    {
        var relativePath = Path.GetRelativePath(featureDirectory, path)
            .Replace(Path.DirectorySeparatorChar, '/');

        return relativePath.StartsWith("bin/", StringComparison.Ordinal)
               || relativePath.StartsWith("obj/", StringComparison.Ordinal);
    }

    private static bool IsBuildResiduePath(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/tools/tmp/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/artifacts/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/.serena/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/.vs/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/Test/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextFileForBrandScan(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".cs" or ".md" or ".json" or ".props" or ".csproj" or ".res" or ".yml" or ".yaml" or ".toml";
    }

    private static HashSet<string> ReadSolutionProjects(string repoRoot)
    {
        var solutionPath = Path.Combine(repoRoot, "TianShu.sln");
        var solutionText = File.ReadAllText(solutionPath);
        var matches = Regex.Matches(
            solutionText,
            "\"(?<path>[^\"]+\\.csproj)\"",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return matches
            .Select(match => NormalizeRepoPath(match.Groups["path"].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, SolutionProjectAllowlistItem> ReadSolutionProjectAllowlist()
        => SolutionProjectAllowlist.ToDictionary(
            static item => NormalizeRepoPath(item.Path),
            static item => item,
            StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<LegacyBrandAllowlistItem> ReadLegacyBrandAllowlist()
        => LegacyBrandAllowlist;

    private static string NormalizeRepoPath(string repoRoot, string fullPath)
        => NormalizeRepoPath(Path.GetRelativePath(repoRoot, fullPath));

    private static string NormalizeRepoPath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');

    private sealed record SolutionProjectAllowlistItem(
        string Path,
        string Classification,
        string Verification,
        string Reason);

    private sealed record LegacyBrandAllowlistItem(
        string PathPattern,
        IReadOnlyList<string> Terms,
        string Classification,
        string Reason)
    {
        public bool Matches(string repoPath, string term)
            => Terms.Any(allowedTerm => string.Equals(allowedTerm, term, StringComparison.Ordinal))
               && GlobMatches(PathPattern, repoPath);

        private static bool GlobMatches(string pattern, string path)
        {
            var regex = "^" + Regex.Escape(NormalizeRepoPath(pattern))
                .Replace("\\*\\*", ".*", StringComparison.Ordinal)
                .Replace("\\*", "[^/]*", StringComparison.Ordinal) + "$";
            return Regex.IsMatch(path, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
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
