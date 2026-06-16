using System.IO;
using System.Text.RegularExpressions;

namespace TianShu.Execution.Integration.Tests;

public sealed partial class DocumentationTerminologyRegressionTests
{
    private static readonly string[] FormalDocumentationRoots =
    [
        "docs/architecture",
        "docs/cli",
        "docs/config",
        "docs/diagnostics",
        "docs/hosting",
        "docs/memory",
        "docs/model",
        "docs/policy",
        "docs/provider",
        "docs/tools",
        "docs/workspace",
    ];

    private static readonly ForbiddenDocumentationTerm[] ForbiddenTerms =
    [
        new(
            "legacy-six-plane",
            "正式文档不得回流旧六平面命名。",
            LegacySixPlanePattern()),
        new(
            "fixed-stage-registry",
            "正式文档不得把固定 Stage Registry 作为终态编排中心。",
            FixedStageRegistryPattern()),
        new(
            "session-orchestrator-terminal-center",
            "正式文档不得把 SessionOrchestrator 描述为终态编排中心。",
            SessionOrchestratorPattern()),
        new(
            "adapter-plane",
            "正式文档不得把 Module Plane 回命名为 Adapter / Adapter Plane。",
            AdapterPlanePattern()),
        new(
            "single-stage-core-loop-default",
            "正式文档不得把单 stage core_loop 描述为当前默认 turn 编排基线。",
            SingleStageCoreLoopDefaultPattern(),
            AllowsHistoricalCoreLoopReference),
    ];

    [Fact]
    public void FormalArchitectureDocs_DoNotReintroduceLegacyTerminology()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in EnumerateFormalDocumentationFiles(repoRoot))
        {
            var relativePath = NormalizePath(Path.GetRelativePath(repoRoot, file));
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                foreach (var term in ForbiddenTerms)
                {
                    if (term.Pattern.IsMatch(line))
                    {
                        if (term.AllowLine?.Invoke(line) == true)
                        {
                            continue;
                        }

                        violations.Add($"{relativePath}:{index + 1}: {term.Id}: {term.Description}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "正式架构文档不得重新引入旧六平面、固定 Stage Registry、SessionOrchestrator 终态中心、Adapter 模块平面命名或单 stage core_loop 默认基线。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void OldNewLoopParityDocs_DoNotLeaveP23KOrP23LAsUndecided()
    {
        var repoRoot = FindRepoRoot();
        var parityDocPath = Path.Combine(repoRoot, "docs", "architecture", "tianshu-old-new-loop-parity-design.md");
        var source = File.ReadAllText(parityDocPath);
        var p23KLine = ExtractTableLine(source, "| P23-K review / plan UI |");
        var p23LLine = ExtractTableLine(source, "| P23-L subagent / agent jobs |");
        var forbiddenUndecidedTerms = new[]
        {
            "暂缓",
            "由 34",
            "由34",
            "后续决策",
            "终局决策",
            "若 34",
            "若34",
        };

        Assert.All(forbiddenUndecidedTerms, term =>
        {
            Assert.DoesNotContain(term, p23KLine, StringComparison.Ordinal);
            Assert.DoesNotContain(term, p23LLine, StringComparison.Ordinal);
        });

        Assert.Contains("已定案", p23KLine, StringComparison.Ordinal);
        Assert.Contains("已定案", p23LLine, StringComparison.Ordinal);
        Assert.Contains("不作为 provider-directed tool", p23KLine, StringComparison.Ordinal);
        Assert.Contains("不进入默认 provider tool allow-list", p23LLine, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalAcceptanceDocs_DoNotRequireProviderDirectedSubagentAsPassGate()
    {
        var repoRoot = FindRepoRoot();
        var finalAcceptancePath = Path.Combine(repoRoot, "docs", "天枢最终验收案例.md");
        var source = File.ReadAllText(finalAcceptancePath);

        Assert.Contains("live 场景只作为真实模型自主触发观察实验", source, StringComparison.Ordinal);
        Assert.Contains("当前任务/模型/tool surface 下未观察到自主触发", source, StringComparison.Ordinal);
        Assert.Contains("提示词含方法诱导", source, StringComparison.Ordinal);
        Assert.DoesNotContain("必须至少真实使用一次子代理", source, StringComparison.Ordinal);
        Assert.DoesNotContain("脚本必须硬性检查至少一次真实子代理", source, StringComparison.Ordinal);
        Assert.DoesNotContain("全程没有真实触发子代理链路", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalAcceptanceSubAgentLivePrompt_ShouldNotContainMethodInductionTerms()
    {
        var repoRoot = FindRepoRoot();
        var finalAcceptancePath = Path.Combine(repoRoot, "docs", "天枢最终验收案例.md");
        var source = File.ReadAllText(finalAcceptancePath);
        var promptMarkers = new[]
        {
            "acceptance-prompt:subagent-live-config-gui",
            "acceptance-prompt:subagent-live-provider-matrix",
            "acceptance-prompt:subagent-live-acceptance-evidence",
        };
        var forbiddenTerms = new[]
        {
            "agent",
            "Agent",
            "子任务",
            "并行",
            "委托",
            "派生",
            "拆分",
            "协作",
            "执行轨道",
            "spawn_agent",
        };

        foreach (var marker in promptMarkers)
        {
            var prompt = ExtractMarkedCodeBlock(source, marker);
            Assert.All(forbiddenTerms, term => Assert.DoesNotContain(term, prompt, StringComparison.Ordinal));
        }
    }

    private static IEnumerable<string> EnumerateFormalDocumentationFiles(string repoRoot)
    {
        yield return Path.Combine(repoRoot, "docs", "tianshu-architecture-spec.md");

        foreach (var root in FormalDocumentationRoots)
        {
            var absoluteRoot = Path.Combine(repoRoot, root.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*.md", SearchOption.AllDirectories)
                         .OrderBy(static item => item, StringComparer.Ordinal))
            {
                yield return file;
            }
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

        throw new InvalidOperationException("无法从测试运行目录定位 TianShu 仓库根目录。");
    }

    private static string NormalizePath(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

    private static string ExtractTableLine(string source, string prefix)
    {
        var line = source.Split(["\r\n", "\n"], StringSplitOptions.None)
            .SingleOrDefault(item => item.StartsWith(prefix, StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(line), $"缺少表格行：{prefix}");
        return line!;
    }

    private static string ExtractMarkedCodeBlock(string source, string marker)
    {
        var markerIndex = source.IndexOf($"<!-- {marker} -->", StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"缺少提示词标记：{marker}");
        var blockStart = source.IndexOf("```text", markerIndex, StringComparison.Ordinal);
        Assert.True(blockStart >= 0, $"提示词标记后缺少 text 代码块：{marker}");
        blockStart += "```text".Length;
        var blockEnd = source.IndexOf("```", blockStart, StringComparison.Ordinal);
        Assert.True(blockEnd >= 0, $"提示词代码块未闭合：{marker}");
        return source[blockStart..blockEnd];
    }

    [GeneratedRegex(@"旧六平面|六平面", RegexOptions.CultureInvariant)]
    private static partial Regex LegacySixPlanePattern();

    [GeneratedRegex(@"固定\s*Stage\s*Registry|StageRegistry\s*作为终态|Stage\s*Registry\s*作为终态|Stage\s*Registry\s*作为.*编排中心", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex FixedStageRegistryPattern();

    [GeneratedRegex(@"SessionOrchestrator(?:\s|`|。|，|,|\.|:|：).*(?:终态|编排中心|核心中心)|(?:终态|编排中心|核心中心).*SessionOrchestrator", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SessionOrchestratorPattern();

    [GeneratedRegex(@"\bAdapter\s*Plane\b|Adapter\s*层|Adapter\s*模块平面|模块平面\s*Adapter|Provider\s*Adapter\s*层|Module\s*Plane\s*Adapter", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AdapterPlanePattern();

    [GeneratedRegex(@"(?:默认|基线|入口|主线|编排中心|终态|退化为|生成).*(?:单\s*stage\s*)?`?core_loop`?|`?core_loop`?.*(?:默认|基线|入口|主线|编排中心|终态|shell|step)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SingleStageCoreLoopDefaultPattern();

    private static bool AllowsHistoricalCoreLoopReference(string line)
        => line.Contains("不再", StringComparison.Ordinal)
           || line.Contains("不得", StringComparison.Ordinal)
           || line.Contains("不复用", StringComparison.Ordinal)
           || line.Contains("不落回", StringComparison.Ordinal)
           || line.Contains("替代", StringComparison.Ordinal)
           || line.Contains("不进入", StringComparison.Ordinal)
           || line.Contains("不允许", StringComparison.Ordinal);

    private sealed record ForbiddenDocumentationTerm(
        string Id,
        string Description,
        Regex Pattern,
        Func<string, bool>? AllowLine = null);
}
