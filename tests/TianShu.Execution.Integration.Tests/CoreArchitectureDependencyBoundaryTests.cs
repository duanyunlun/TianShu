using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TianShu.Execution.Integration.Tests;

public sealed class CoreArchitectureDependencyBoundaryTests
{
    private const int ExpectedLegacyKernelNamedFileCount = 206;
    private const string ExpectedLegacyKernelNamedFileSetHash = "21b03f2a266d4b7732f89973c526901c3c9ee05c0748dc66f2bb7590ef23a0e7";

    private static readonly string[] GuardedSourceRoots =
    [
        "src/Hosting",
        "src/Presentations",
        "src/Core/TianShu.RuntimeComposition",
    ];

    private static readonly string[] KernelOwnedSemanticTokens =
    [
        "CoreIntent",
        "StageGraph",
        "RuntimeStep",
        "ExecutionPlan",
        "KernelOperation",
        "StageRegistry",
        "RuntimeStageRegistry",
        "StageContextPackage",
        "StageExecutionRequest",
        "StageCheckpoint",
        "OrchestratorDecision",
        "StageExecutor",
        "StageDefinition",
        "SessionOrchestrator",
    ];

    private static readonly KernelSemanticBridgeAllowlistItem[] KernelSemanticBridgeAllowlist =
    [
        new(
            "src/Core/TianShu.RuntimeComposition/AppHostCoreLoopOrchestrationStateStore.cs",
            "runtime-composition-state-store-migration-bridge",
            "RuntimeComposition 当前仍提交 orchestration decision 与 context package 到宿主状态。"),
        new(
            "src/Core/TianShu.RuntimeComposition/AdaptiveRuntimeExecutionLoop.cs",
            "adaptive-kernel-runtime-composition-entrypoint",
            "RuntimeComposition 在该文件中只登记 Kernel -> Execution Runtime 受控交接入口，不拥有 Kernel 或 Runtime 内部语义。"),
        new(
            "src/Core/TianShu.RuntimeComposition/ConfiguredResponsesProviderModule.cs",
            "kernel-runtime-provider-module-bridge",
            "RuntimeComposition 在该文件中只把 provider 请求绑定到已批准 runtime step 的 trace / metadata，不拥有 Kernel 编排语义。"),
        new(
            "src/Core/TianShu.RuntimeComposition/KernelRuntimeTurnLoopBridge.cs",
            "kernel-runtime-product-bridge",
            "RuntimeComposition 在该文件中只登记 CLI/HostGateway 产品请求到 Kernel -> Runtime typed bridge 的迁移入口。"),
        new(
            "src/Core/TianShu.RuntimeComposition/KernelRuntimeTurnLoopComposition.cs",
            "kernel-runtime-binding-composition-bridge",
            "RuntimeComposition 在该文件中只组合 provider/tool/runtime 绑定，不拥有 Kernel 编排语义。"),
        new(
            "src/Hosting/TianShu.AppHost.State/KernelRolloutModels.cs",
            "apphost-state-persistence-migration-bridge",
            "AppHost.State 当前仍持久化 orchestration projection record。"),
        new(
            "src/Hosting/TianShu.AppHost.State/KernelRolloutStateMapper.cs",
            "apphost-state-persistence-migration-bridge",
            "AppHost.State 当前仍映射 rollout 与 thread orchestration state。"),
        new(
            "src/Hosting/TianShu.AppHost.State/KernelThreadStore.cs",
            "apphost-state-persistence-migration-bridge",
            "AppHost.State 当前仍保存线程级 orchestration state。"),
    ];

    [Fact]
    public void HostingPresentationsAndRuntimeComposition_DoNotOwnNewKernelSemantics()
    {
        var repoRoot = FindRepoRoot();
        var allowlist = ReadKernelSemanticBridgeAllowlist();
        var violations = new List<string>();

        foreach (var file in EnumerateGuardedSourceFiles(repoRoot, "*.cs"))
        {
            var relativePath = NormalizePath(Path.GetRelativePath(repoRoot, file));
            if (allowlist.ContainsKey(relativePath))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].Trim();
                if (IsCommentOrBlank(line))
                {
                    continue;
                }

                var token = KernelOwnedSemanticTokens.FirstOrDefault(item => line.Contains(item, StringComparison.Ordinal));
                if (token is not null)
                {
                    violations.Add($"{relativePath}:{index + 1}: {token}: {line}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Hosting、Presentations、RuntimeComposition 不得新增 Kernel-owned 编排语义；请迁入 Kernel/Contracts/Execution，或显式登记为迁移 bridge。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void KernelSemanticBridgeAllowlist_ShouldRemainCurrentAndExplained()
    {
        var repoRoot = FindRepoRoot();
        var invalidEntries = KernelSemanticBridgeAllowlist
            .Where(static item => string.IsNullOrWhiteSpace(item.Classification) || string.IsNullOrWhiteSpace(item.Reason))
            .Select(static item => item.Path)
            .ToArray();
        var staleEntries = KernelSemanticBridgeAllowlist
            .Where(item => !File.Exists(Path.Combine(repoRoot, item.Path.Replace('/', Path.DirectorySeparatorChar))))
            .Select(static item => item.Path)
            .ToArray();

        Assert.True(
            invalidEntries.Length == 0,
            $"Kernel semantic bridge allowlist 必须说明 classification / reason：{string.Join(", ", invalidEntries)}");
        Assert.True(
            staleEntries.Length == 0,
            $"Kernel semantic bridge allowlist 存在失效路径：{string.Join(", ", staleEntries)}");
    }

    [Fact]
    public void HostingAndPresentations_DoNotReferenceKernelImplementationProjects()
    {
        var repoRoot = FindRepoRoot();
        var guardedProjectRoots = new[]
        {
            "src/Hosting",
            "src/Presentations",
        };
        var forbiddenProjectReferenceFragments = new[]
        {
            "Core\\TianShu.Kernel\\TianShu.Kernel.csproj",
            "Core\\TianShu.Kernel.Abstractions\\TianShu.Kernel.Abstractions.csproj",
            "Core\\TianShu.Kernel.Adaptive\\TianShu.Kernel.Adaptive.csproj",
            "Core\\TianShu.Kernel.Strategies\\TianShu.Kernel.Strategies.csproj",
        };
        var violations = new List<string>();

        foreach (var root in guardedProjectRoots)
        {
            var absoluteRoot = Path.Combine(repoRoot, root.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var projectFile in Directory.EnumerateFiles(absoluteRoot, "*.csproj", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(projectFile);
                var matchedFragment = forbiddenProjectReferenceFragments.FirstOrDefault(fragment => content.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (matchedFragment is not null)
                {
                    violations.Add($"{NormalizePath(Path.GetRelativePath(repoRoot, projectFile))}: {matchedFragment}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Hosting / Presentations 不得直接引用 Kernel 实现项目；应通过 HostGateway / ControlPlane / Contracts typed surface 消费。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void KernelImplementation_DoesNotOwnExecutionRuntimeLoop()
    {
        var repoRoot = FindRepoRoot();
        var kernelRoot = Path.Combine(repoRoot, "src", "Core", "TianShu.Kernel");
        var forbiddenTokens = new[]
        {
            "IExecutionRuntime",
            "TianShu.Execution.Runtime",
        };
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(kernelRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(static file => !IsBuildResiduePath(file)))
        {
            var relativePath = NormalizePath(Path.GetRelativePath(repoRoot, file));
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].Trim();
                if (IsCommentOrBlank(line))
                {
                    continue;
                }

                var token = forbiddenTokens.FirstOrDefault(item => line.Contains(item, StringComparison.Ordinal));
                if (token is not null)
                {
                    violations.Add($"{relativePath}:{index + 1}: {token}: {line}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Stable Kernel Core 不得直接拥有 IExecutionRuntime 执行循环；反应式 stage transition loop 应落在 RuntimeComposition。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void KernelTargetProjects_ShouldExistAndLegacyKernelNamedFiles_ShouldNotGrowInHostingOrRuntimeComposition()
    {
        var repoRoot = FindRepoRoot();
        var requiredKernelProjects = new[]
        {
            "src/Core/TianShu.Kernel/TianShu.Kernel.csproj",
            "src/Core/TianShu.Kernel.Abstractions/TianShu.Kernel.Abstractions.csproj",
            "src/Core/TianShu.Kernel.Adaptive/TianShu.Kernel.Adaptive.csproj",
            "src/Core/TianShu.Kernel.Strategies/TianShu.Kernel.Strategies.csproj",
        };
        var missingKernelProjects = requiredKernelProjects
            .Where(project => !File.Exists(Path.Combine(repoRoot, project.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        var legacyKernelNamedFiles = EnumerateLegacyKernelNamedFiles(repoRoot).ToArray();
        var fileSetHash = ComputeSha256Hex(string.Join('\n', legacyKernelNamedFiles));

        Assert.True(
            missingKernelProjects.Length == 0,
            $"Kernel 目标项目必须存在，未来新增 Kernel 类型应进入这些项目：{string.Join(", ", missingKernelProjects)}");
        Assert.Equal(ExpectedLegacyKernelNamedFileCount, legacyKernelNamedFiles.Length);
        Assert.Equal(ExpectedLegacyKernelNamedFileSetHash, fileSetHash);
    }

    private static Dictionary<string, KernelSemanticBridgeAllowlistItem> ReadKernelSemanticBridgeAllowlist()
        => KernelSemanticBridgeAllowlist.ToDictionary(static item => item.Path, StringComparer.Ordinal);

    private static IEnumerable<string> EnumerateGuardedSourceFiles(string repoRoot, string searchPattern)
    {
        foreach (var guardedRoot in GuardedSourceRoots)
        {
            var root = Path.Combine(repoRoot, guardedRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, searchPattern, SearchOption.AllDirectories)
                         .Where(static file => !IsBuildResiduePath(file)))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateLegacyKernelNamedFiles(string repoRoot)
    {
        var guardedRoots = new[]
        {
            "src/Hosting",
            "src/Core/TianShu.RuntimeComposition",
        };

        return guardedRoots
            .Select(root => Path.Combine(repoRoot, root.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "Kernel*.cs", SearchOption.AllDirectories))
            .Where(static file => !IsBuildResiduePath(file))
            .Select(file => NormalizePath(Path.GetRelativePath(repoRoot, file)))
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ComputeSha256Hex(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsCommentOrBlank(string line)
        => string.IsNullOrWhiteSpace(line)
           || line.StartsWith("//", StringComparison.Ordinal)
           || line.StartsWith("/*", StringComparison.Ordinal)
           || line.StartsWith("*", StringComparison.Ordinal);

    private static bool IsBuildResiduePath(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
           || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

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

    private sealed record KernelSemanticBridgeAllowlistItem(
        string Path,
        string Classification,
        string Reason);
}
