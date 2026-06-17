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

    [Fact]
    public void FinalAcceptanceScript_ShouldReportSubAgentLiveEffectivenessAndFalsePositiveRates()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "tools", "Run-TianShuFinalAcceptance.ps1");
        var source = File.ReadAllText(scriptPath);

        Assert.Contains("EffectiveSpawnObservedCount", source, StringComparison.Ordinal);
        Assert.Contains("FalsePositiveSpawnObservedCount", source, StringComparison.Ordinal);
        Assert.Contains("OverallEffectiveRate", source, StringComparison.Ordinal);
        Assert.Contains("OverallFalsePositiveRate", source, StringComparison.Ordinal);
        Assert.Contains("live-observation-matrix-complete-with-effective-autonomous-spawn-observed", source, StringComparison.Ordinal);
        Assert.Contains("live-observation-matrix-complete-with-spawn-signal-without-effective-return", source, StringComparison.Ordinal);
        Assert.Contains("SubAgentLiveEffectiveSpawnObservedCount", source, StringComparison.Ordinal);
        Assert.Contains("SubAgentLiveFalsePositiveSpawnObservedCount", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalAcceptance_ShouldRequireDeterministicMultiAgentMechanismCaseSeparatelyFromLiveObservation()
    {
        var repoRoot = FindRepoRoot();
        var finalAcceptancePath = Path.Combine(repoRoot, "docs", "天枢最终验收案例.md");
        var scriptPath = Path.Combine(repoRoot, "tools", "Run-TianShuFinalAcceptance.ps1");
        var docSource = File.ReadAllText(finalAcceptancePath);
        var scriptSource = File.ReadAllText(scriptPath);

        Assert.Contains("多 Agent 确定性最终验收案例", docSource, StringComparison.Ordinal);
        Assert.Contains("确定性多 Agent 案例是最终验收必过项；live 自主触发观察矩阵是产品行为证据", docSource, StringComparison.Ordinal);
        Assert.Contains("并行 fanout、子树治理、预算切分、结果 fan-in、失败隔离和整树复盘", docSource, StringComparison.Ordinal);
        Assert.Contains("不能否定机制门禁", docSource, StringComparison.Ordinal);
        Assert.DoesNotContain("live 矩阵通过即可替代多 Agent 确定性门禁", docSource, StringComparison.Ordinal);

        var requiredScriptTerms = new[]
        {
            "multiAgentFinalCaseObserved",
            "parallelFanoutObserved",
            "subtreeGovernanceObserved",
            "budgetSplitObserved",
            "fanInObserved",
            "failureIsolationObserved",
            "wholeTreeDiagnosticsObserved",
            "plannedSubTaskCount",
            "maxConcurrentAgents",
            "completedChildCount",
            "failedChildCount",
            "treeNodeCount",
            "treeEdgeCount",
            "SubAgentMultiAgentFinalCaseAccepted",
        };
        Assert.All(requiredScriptTerms, term => Assert.Contains(term, scriptSource, StringComparison.Ordinal));
    }

    [Fact]
    public void V09MultiAgentReleaseGate_ShouldBeDocumentedAndWiredIntoCi()
    {
        var repoRoot = FindRepoRoot();
        var gateScriptPath = Path.Combine(repoRoot, "tools", "Test-TianShuV09MultiAgentReleaseGate.ps1");
        var ciPath = Path.Combine(repoRoot, ".github", "workflows", "ci-release.yml");
        var releaseSmokePath = Path.Combine(repoRoot, "docs", "publishing", "tianshu-release-smoke.md");
        var readmePath = Path.Combine(repoRoot, "README.md");
        var subAgentDesignPath = Path.Combine(repoRoot, "docs", "architecture", "tianshu-subagent-design.md");
        var finalAcceptanceScriptPath = Path.Combine(repoRoot, "tools", "Run-TianShuFinalAcceptance.ps1");
        var finalAcceptanceDocPath = Path.Combine(repoRoot, "docs", "天枢最终验收案例.md");

        var gateScript = File.ReadAllText(gateScriptPath);
        var ci = File.ReadAllText(ciPath);
        var releaseSmoke = File.ReadAllText(releaseSmokePath);
        var readme = File.ReadAllText(readmePath);
        var subAgentDesign = File.ReadAllText(subAgentDesignPath);
        var finalAcceptanceScript = File.ReadAllText(finalAcceptanceScriptPath);
        var finalAcceptanceDoc = File.ReadAllText(finalAcceptanceDocPath);

        Assert.Contains("Test-TianShuV09MultiAgentReleaseGate.ps1", ci, StringComparison.Ordinal);
        Assert.Contains("Test-TianShuV09MultiAgentReleaseGate.ps1", releaseSmoke, StringComparison.Ordinal);
        Assert.Contains("v0.9 multi-agent release gate", subAgentDesign, StringComparison.Ordinal);
        Assert.Contains("v0.9 多 Agent 发布门禁", readme, StringComparison.Ordinal);
        Assert.Contains("v0.9 multi-agent release gate", readme, StringComparison.Ordinal);

        var requiredGateTerms = new[]
        {
            "--enable-subagents[\\s\\S]*--approve-all[\\s\\S]*HostMutation",
            "DefaultTurnGraphKeepsSpawnAgentFailClosedWhenModuleIsNotGoverned",
            "RunAsync_ShouldHideMultiAgentAndFanoutTools_WhenFeaturesAreDisabled",
            "RunAsync_ShouldIncludeConfiguredMultiAgentAndFanoutTools_ButKeepWorkerToolHiddenForRegularTurns",
            "multiAgentFinalCaseObserved",
            "parallelFanoutObserved",
            "SubAgentLiveObservationProtocol",
            "SubAgentLiveTriggerRates",
            "SubAgentLiveOverallEffectiveRate",
            "SubAgentLiveOverallFalsePositiveRate",
        };
        Assert.All(requiredGateTerms, term => Assert.Contains(term, gateScript, StringComparison.Ordinal));

        var requiredFinalReportTerms = new[]
        {
            "SubAgentLiveObservationProtocol",
            "SubAgentLiveTriggerRates",
            "SubAgentLiveOverallTriggerRate",
            "SubAgentLiveOverallEffectiveRate",
            "SubAgentLiveOverallFalsePositiveRate",
            "SubAgentLiveConclusion",
            "SubAgentLiveMatrix",
            "SubAgentLiveArtifactsRoot",
            "SubAgentMultiAgentFinalCaseAccepted",
        };
        Assert.All(requiredFinalReportTerms, term => Assert.Contains(term, finalAcceptanceScript, StringComparison.Ordinal));

        Assert.Contains("确定性多 Agent 案例是最终验收必过项；live 自主触发观察矩阵是产品行为证据", finalAcceptanceDoc, StringComparison.Ordinal);
        Assert.Contains("live 工具面证据以 `runtimeDiagnosticsProjection.providerToolSurface` 为准", finalAcceptanceDoc, StringComparison.Ordinal);
    }

    [Fact]
    public void P31_3_ProductionDocumentationGate_ShouldBeDocumentedAndWiredIntoCi()
    {
        var repoRoot = FindRepoRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");
        var quickstartPath = Path.Combine(repoRoot, "docs", "usage", "quickstart.md");
        var moduleGuidePath = Path.Combine(repoRoot, "docs", "usage", "modules.md");
        var architectureSpecPath = Path.Combine(repoRoot, "docs", "tianshu-architecture-spec.md");
        var troubleshootingPath = Path.Combine(repoRoot, "docs", "usage", "troubleshooting.md");
        var securityModelPath = Path.Combine(repoRoot, "docs", "security", "tianshu-security-model.md");
        var releaseNotesPath = Path.Combine(repoRoot, "docs", "publishing", "release-notes.md");
        var releaseSmokePath = Path.Combine(repoRoot, "docs", "publishing", "tianshu-release-smoke.md");
        var ciPath = Path.Combine(repoRoot, ".github", "workflows", "ci-release.yml");
        var gateScriptPath = Path.Combine(repoRoot, "tools", "Test-TianShuV10DocumentationReleaseGate.ps1");

        var requiredFiles = new[]
        {
            readmePath,
            quickstartPath,
            moduleGuidePath,
            architectureSpecPath,
            troubleshootingPath,
            securityModelPath,
            releaseNotesPath,
            gateScriptPath,
        };
        Assert.All(requiredFiles, path => Assert.True(File.Exists(path), $"缺少生产级文档门禁文件：{NormalizePath(Path.GetRelativePath(repoRoot, path))}"));

        var readme = File.ReadAllText(readmePath);
        var quickstart = File.ReadAllText(quickstartPath);
        var moduleGuide = File.ReadAllText(moduleGuidePath);
        var architectureSpec = File.ReadAllText(architectureSpecPath);
        var troubleshooting = File.ReadAllText(troubleshootingPath);
        var securityModel = File.ReadAllText(securityModelPath);
        var releaseNotes = File.ReadAllText(releaseNotesPath);
        var releaseSmoke = File.ReadAllText(releaseSmokePath);
        var ci = File.ReadAllText(ciPath);
        var gateScript = File.ReadAllText(gateScriptPath);

        var requiredReadmeLinks = new[]
        {
            "docs/usage/quickstart.md",
            "docs/usage/modules.md",
            "docs/tianshu-architecture-spec.md",
            "docs/usage/troubleshooting.md",
            "docs/security/tianshu-security-model.md",
            "docs/publishing/release-notes.md",
        };
        Assert.All(requiredReadmeLinks, link => Assert.Contains(link, readme, StringComparison.Ordinal));

        Assert.Contains("tianshu init", quickstart, StringComparison.Ordinal);
        Assert.Contains("tianshu doctor", quickstart, StringComparison.Ordinal);
        Assert.Contains("tianshu send", quickstart, StringComparison.Ordinal);
        Assert.Contains("Provider", moduleGuide, StringComparison.Ordinal);
        Assert.Contains("Tool", moduleGuide, StringComparison.Ordinal);
        Assert.Contains("Memory", moduleGuide, StringComparison.Ordinal);
        Assert.Contains("Experience Plane", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("Host Gateway", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("Control Plane", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("Kernel / Core Loop", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("Execution Runtime", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("Module Plane", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("provider_api_key_missing", troubleshooting, StringComparison.Ordinal);
        Assert.Contains("doctor --probe", troubleshooting, StringComparison.Ordinal);
        Assert.Contains("Sub-Agent", troubleshooting, StringComparison.Ordinal);
        Assert.Contains("secret", securityModel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("governance", securityModel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RuntimeStep", securityModel, StringComparison.Ordinal);
        Assert.Contains("Remote Module", securityModel, StringComparison.Ordinal);
        Assert.Contains("v0.9.1", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("验证状态", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("已知限制", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Unreleased", releaseNotes, StringComparison.Ordinal);

        Assert.Contains("Test-TianShuV10DocumentationReleaseGate.ps1", ci, StringComparison.Ordinal);
        Assert.Contains("Test-TianShuV10DocumentationReleaseGate.ps1", releaseSmoke, StringComparison.Ordinal);
        Assert.Contains("v1.0 production documentation gate", releaseSmoke, StringComparison.Ordinal);
        Assert.Contains("docs/usage/quickstart.md", gateScript, StringComparison.Ordinal);
        Assert.Contains("docs/security/tianshu-security-model.md", gateScript, StringComparison.Ordinal);
        Assert.Contains("docs/publishing/release-notes.md", gateScript, StringComparison.Ordinal);
    }

    [Fact]
    public void P31_4_PublicReleaseSafetyScan_ShouldBeDocumentedAndWiredIntoCi()
    {
        var repoRoot = FindRepoRoot();
        var scanScriptPath = Path.Combine(repoRoot, "tools", "Test-TianShuPublicReleaseSafetyScan.ps1");
        var packageSmokePath = Path.Combine(repoRoot, "tools", "Test-TianShuCliReleasePackage.ps1");
        var releaseSmokePath = Path.Combine(repoRoot, "docs", "publishing", "tianshu-release-smoke.md");
        var ciPath = Path.Combine(repoRoot, ".github", "workflows", "ci-release.yml");
        var gitIgnorePath = Path.Combine(repoRoot, ".gitignore");

        Assert.True(File.Exists(scanScriptPath), "缺少公开发布安全扫描脚本。");

        var scanScript = File.ReadAllText(scanScriptPath);
        var packageSmoke = File.ReadAllText(packageSmokePath);
        var releaseSmoke = File.ReadAllText(releaseSmokePath);
        var ci = File.ReadAllText(ciPath);
        var gitIgnore = File.ReadAllText(gitIgnorePath);

        var requiredScanTerms = new[]
        {
            "Test-SecretAndPrivatePathScan",
            "Test-TrackedRuntimeAndTestArtifacts",
            "Test-IgnoreRules",
            "Test-MarkdownRelativeLinks",
            "private_path.repo",
            "private_path.user_profile",
            "private_path.codex_clipboard",
            "secret.openai_literal",
            "secret.github_token",
            "runtime_state_file",
            "test_artifact_root",
            "Release package must not contain AGENTS\\.md",
        };
        Assert.All(requiredScanTerms, term => Assert.Contains(term, scanScript, StringComparison.Ordinal));

        var requiredIgnoreTerms = new[]
        {
            "[Tt]est/",
            "/artifacts/",
            ".codex/",
            ".claude/",
            ".serena/",
            "request-body*.json",
        };
        Assert.All(requiredIgnoreTerms, term => Assert.Contains(term, gitIgnore, StringComparison.Ordinal));

        Assert.Contains("Release package must not contain AGENTS.md", packageSmoke, StringComparison.Ordinal);
        Assert.Contains("Test-TianShuPublicReleaseSafetyScan.ps1", ci, StringComparison.Ordinal);
        Assert.Contains("Test-TianShuPublicReleaseSafetyScan.ps1", releaseSmoke, StringComparison.Ordinal);
        Assert.Contains("secret literals", releaseSmoke, StringComparison.Ordinal);
        Assert.Contains("private local paths", releaseSmoke, StringComparison.Ordinal);
        Assert.Contains("runtime state", releaseSmoke, StringComparison.Ordinal);
        Assert.Contains("tracked test artifacts", releaseSmoke, StringComparison.Ordinal);
        Assert.Contains("relative dead links", releaseSmoke, StringComparison.Ordinal);
    }

    [Fact]
    public void P31_5_CrossPlatformCiMatrix_ShouldCoverSourceAndPackageSmoke()
    {
        var repoRoot = FindRepoRoot();
        var ciPath = Path.Combine(repoRoot, ".github", "workflows", "ci-release.yml");
        var sourceSmokeScriptPath = Path.Combine(repoRoot, "tools", "Test-TianShuCrossPlatformCliSourceSmoke.ps1");
        var packageSmokeScriptPath = Path.Combine(repoRoot, "tools", "Test-TianShuCliReleasePackage.ps1");
        var releaseSmokePath = Path.Combine(repoRoot, "docs", "publishing", "tianshu-release-smoke.md");

        Assert.True(File.Exists(sourceSmokeScriptPath), "缺少跨平台源码 smoke 脚本。");

        var ci = File.ReadAllText(ciPath);
        var sourceSmokeScript = File.ReadAllText(sourceSmokeScriptPath);
        var packageSmokeScript = File.ReadAllText(packageSmokeScriptPath);
        var releaseSmoke = File.ReadAllText(releaseSmokePath);

        var requiredMatrixTerms = new[]
        {
            "cross-platform-source-smoke",
            "Test-TianShuCrossPlatformCliSourceSmoke.ps1",
            "windows-latest",
            "ubuntu-latest",
            "macos-26",
            "win-x64",
            "linux-x64",
            "osx-arm64",
            "release-smoke",
            "Test-TianShuCliReleasePackage.ps1",
            "cross-platform-source-smoke",
            "package-cli",
        };
        Assert.All(requiredMatrixTerms, term => Assert.Contains(term, ci, StringComparison.Ordinal));

        Assert.Contains("dotnet", sourceSmokeScript, StringComparison.Ordinal);
        Assert.Contains("'restore'", sourceSmokeScript, StringComparison.Ordinal);
        Assert.Contains("'build'", sourceSmokeScript, StringComparison.Ordinal);
        Assert.Contains("'init'", sourceSmokeScript, StringComparison.Ordinal);
        Assert.Contains("'doctor'", sourceSmokeScript, StringComparison.Ordinal);
        Assert.Contains("provider_api_key_missing", sourceSmokeScript, StringComparison.Ordinal);
        Assert.Contains("discoveredCount", sourceSmokeScript, StringComparison.Ordinal);
        Assert.Contains("registeredCount", sourceSmokeScript, StringComparison.Ordinal);

        Assert.Contains("RuntimeIdentifier", packageSmokeScript, StringComparison.Ordinal);
        Assert.Contains("init", packageSmokeScript, StringComparison.Ordinal);
        Assert.Contains("doctor", packageSmokeScript, StringComparison.Ordinal);
        Assert.Contains("provider_api_key_missing", packageSmokeScript, StringComparison.Ordinal);

        Assert.Contains("Test-TianShuCrossPlatformCliSourceSmoke.ps1", releaseSmoke, StringComparison.Ordinal);
        Assert.Contains("Windows, Linux, and macOS", releaseSmoke, StringComparison.Ordinal);
        Assert.Contains("source restore/build", releaseSmoke, StringComparison.Ordinal);
        Assert.Contains("package smoke", releaseSmoke, StringComparison.Ordinal);
    }

    [Fact]
    public void P31_6_ReleaseAcceptanceGate_ShouldBeDocumentedAndWiredIntoCi()
    {
        var repoRoot = FindRepoRoot();
        var ciPath = Path.Combine(repoRoot, ".github", "workflows", "ci-release.yml");
        var releaseAcceptancePath = Path.Combine(repoRoot, "docs", "publishing", "tianshu-release-acceptance.md");
        var releaseSmokePath = Path.Combine(repoRoot, "docs", "publishing", "tianshu-release-smoke.md");
        var releaseNotesPath = Path.Combine(repoRoot, "docs", "publishing", "release-notes.md");
        var gateScriptPath = Path.Combine(repoRoot, "tools", "Test-TianShuReleaseAcceptanceGate.ps1");
        var publishScriptPath = Path.Combine(repoRoot, "tools", "Publish-TianShuCliRelease.ps1");
        var manifestScriptPath = Path.Combine(repoRoot, "tools", "Test-TianShuReleaseManifest.ps1");
        var packageSmokeScriptPath = Path.Combine(repoRoot, "tools", "Test-TianShuCliReleasePackage.ps1");

        Assert.True(File.Exists(releaseAcceptancePath), "缺少 release 验收基线文档。");
        Assert.True(File.Exists(gateScriptPath), "缺少 release 验收门禁脚本。");

        var ci = File.ReadAllText(ciPath);
        var releaseAcceptance = File.ReadAllText(releaseAcceptancePath);
        var releaseSmoke = File.ReadAllText(releaseSmokePath);
        var releaseNotes = File.ReadAllText(releaseNotesPath);
        var gateScript = File.ReadAllText(gateScriptPath);
        var publishScript = File.ReadAllText(publishScriptPath);
        var manifestScript = File.ReadAllText(manifestScriptPath);
        var packageSmokeScript = File.ReadAllText(packageSmokeScriptPath);

        var requiredCiTerms = new[]
        {
            "tags:",
            "\"v*\"",
            "Test-TianShuReleaseAcceptanceGate.ps1",
            "package-cli",
            "release-smoke",
            "github-release",
            "startsWith(github.ref, 'refs/tags/v')",
            "softprops/action-gh-release@v2",
        };
        Assert.All(requiredCiTerms, term => Assert.Contains(term, ci, StringComparison.Ordinal));

        var requiredAcceptanceTerms = new[]
        {
            "Tag 发布",
            "tianshu-<version>-win-x64.zip",
            "tianshu-<version>-linux-x64.tar.gz",
            "tianshu-<version>-osx-arm64.tar.gz",
            "release-manifest.json",
            "SHA-256",
            "schemaVersion",
            "assetName",
            "sizeBytes",
            "layout=portable-tianshu-home",
            "entryPath",
            "configPath",
            "modulesPath",
            "appHostPath",
            "selfContained=true",
            "Windows smoke",
            "provider_api_key_missing",
            "升级说明",
            "卸载说明",
        };
        Assert.All(requiredAcceptanceTerms, term => Assert.Contains(term, releaseAcceptance, StringComparison.Ordinal));

        var requiredPublishTerms = new[]
        {
            "RuntimeIdentifiers = @(\"win-x64\", \"linux-x64\", \"osx-arm64\")",
            "tianshu-$Version-$rid",
            "README.md",
            "LICENSE",
            "VERSION.txt",
            "portable-tianshu-home",
            "entryPath",
            "configPath",
            "modulesPath",
            "appHostPath",
            "selfContained = $true",
            "sha256",
            "release-manifest.json",
        };
        Assert.All(requiredPublishTerms, term => Assert.Contains(term, publishScript, StringComparison.Ordinal));

        var requiredManifestTerms = new[]
        {
            "schemaVersion -ne 1",
            "assetName",
            "sizeBytes",
            "sha256",
            "portable-tianshu-home",
            "entryPath",
            "configPath",
            "modulesPath",
            "appHostPath",
            "selfContained",
            "GitHubRepository",
            "ReleaseTag",
            "release-manifest.json",
        };
        Assert.All(requiredManifestTerms, term => Assert.Contains(term, manifestScript, StringComparison.Ordinal));

        var requiredPackageSmokeTerms = new[]
        {
            "README.md",
            "LICENSE",
            "VERSION.txt",
            "tianshu.toml",
            "AGENTS.md",
            "--help",
            "init",
            "doctor",
            "send",
            "portableMode",
            "provider_api_key_missing",
            "packaged_assembly_missing",
        };
        Assert.All(requiredPackageSmokeTerms, term => Assert.Contains(term, packageSmokeScript, StringComparison.Ordinal));

        Assert.Contains("P31_6_ReleaseAcceptanceGate", gateScript, StringComparison.Ordinal);
        Assert.Contains("TianShu release acceptance gate passed.", gateScript, StringComparison.Ordinal);
        Assert.Contains("Test-TianShuReleaseAcceptanceGate.ps1", releaseSmoke, StringComparison.Ordinal);
        Assert.Contains("tag-triggered publishing", releaseSmoke, StringComparison.Ordinal);
        Assert.Contains("tianshu-release-acceptance.md", releaseNotes, StringComparison.Ordinal);
    }

    [Fact]
    public void SelfEvolutionDocs_ShouldDeclareExploratoryBoundaryAndStableKernelCoreGate()
    {
        var repoRoot = FindRepoRoot();
        var selfEvolutionPath = Path.Combine(repoRoot, "docs", "architecture", "tianshu-self-evolution-design.md");
        var selfEvolutionDraftReportPath = Path.Combine(repoRoot, "docs", "audit", "tianshu-self-evolution-feasibility-draft.md");
        var selfEvolutionReportPath = Path.Combine(repoRoot, "docs", "audit", "tianshu-self-evolution-feasibility-report.md");
        var architectureSpecPath = Path.Combine(repoRoot, "docs", "tianshu-architecture-spec.md");
        var planesPath = Path.Combine(repoRoot, "docs", "architecture", "tianshu-planes-architecture.md");
        var kernelLoopPath = Path.Combine(repoRoot, "docs", "architecture", "tianshu-kernel-core-loop-design.md");
        var releaseNotesPath = Path.Combine(repoRoot, "docs", "publishing", "release-notes.md");
        var readmePath = Path.Combine(repoRoot, "README.md");

        Assert.True(File.Exists(selfEvolutionPath), "缺少自演化专项设计文档。");
        Assert.True(File.Exists(selfEvolutionDraftReportPath), "缺少 P30.11 自演化阶段性可行性报告草案。");
        Assert.True(File.Exists(selfEvolutionReportPath), "缺少 P31.8 自演化正式可行性报告。");

        var selfEvolution = File.ReadAllText(selfEvolutionPath);
        var selfEvolutionDraftReport = File.ReadAllText(selfEvolutionDraftReportPath);
        var selfEvolutionReport = File.ReadAllText(selfEvolutionReportPath);
        var architectureSpec = File.ReadAllText(architectureSpecPath);
        var planes = File.ReadAllText(planesPath);
        var kernelLoop = File.ReadAllText(kernelLoopPath);
        var releaseNotes = File.ReadAllText(releaseNotesPath);
        var readme = File.ReadAllText(readmePath);

        Assert.Contains("tianshu-self-evolution-design.md", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("tianshu-self-evolution-feasibility-report.md", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("tianshu-self-evolution-design.md", planes, StringComparison.Ordinal);
        Assert.Contains("探索性能力", selfEvolution, StringComparison.Ordinal);
        Assert.Contains("不承诺成功", selfEvolution, StringComparison.Ordinal);
        Assert.Contains("Stable Kernel Core", selfEvolution, StringComparison.Ordinal);
        Assert.Contains("不可被模型绕过", selfEvolution, StringComparison.Ordinal);
        Assert.Contains("KernelTool 只能返回 `KernelProposal` 或 `KernelOperation`，不得直接返回 RuntimeStep", selfEvolution, StringComparison.Ordinal);
        Assert.Contains("trace、evaluation evidence、metric refs、rollback plan", selfEvolution, StringComparison.Ordinal);
        Assert.Contains("human gate", selfEvolution, StringComparison.Ordinal);
        Assert.Contains("P31.8 正式报告", selfEvolution, StringComparison.Ordinal);
        Assert.Contains("tianshu-self-evolution-feasibility-report.md", selfEvolution, StringComparison.Ordinal);
        Assert.Contains("tianshu-self-evolution-feasibility-draft.md", selfEvolution, StringComparison.Ordinal);
        Assert.Contains("当前能度量什么", selfEvolutionDraftReport, StringComparison.Ordinal);
        Assert.Contains("当前不能度量什么", selfEvolutionDraftReport, StringComparison.Ordinal);
        Assert.Contains("当前失败样例", selfEvolutionDraftReport, StringComparison.Ordinal);
        Assert.Contains("下一步实验边界", selfEvolutionDraftReport, StringComparison.Ordinal);
        Assert.Contains("不是最终结论", selfEvolutionDraftReport, StringComparison.Ordinal);
        Assert.Contains("不用于宣称 TianShu 已经实现可靠自主进化", selfEvolutionDraftReport, StringComparison.Ordinal);
        Assert.Contains("P31.8 的正式结论是：**部分可行**", selfEvolutionReport, StringComparison.Ordinal);
        Assert.Contains("已证明的能力", selfEvolutionReport, StringComparison.Ordinal);
        Assert.Contains("未证明的能力", selfEvolutionReport, StringComparison.Ordinal);
        Assert.Contains("真实长期收益", selfEvolutionReport, StringComparison.Ordinal);
        Assert.Contains("真实 usage / cost", selfEvolutionReport, StringComparison.Ordinal);
        Assert.Contains("不允许模型绕过 Stable Kernel Core", selfEvolutionReport, StringComparison.Ordinal);
        Assert.Contains("不允许文档、README、release notes 或验收报告宣称 TianShu 已经实现可靠自主进化", selfEvolutionReport, StringComparison.Ordinal);
        Assert.Contains("不承诺成功", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("部分可行", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("P31.8 正式报告结论为部分可行", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("不承诺已经实现可靠自主进化", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("当前结论:部分可行", readme, StringComparison.Ordinal);
        Assert.Contains("partially feasible", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("自主进化已成功", selfEvolution, StringComparison.Ordinal);
        Assert.DoesNotContain("已经证明有效", selfEvolution, StringComparison.Ordinal);
        Assert.DoesNotContain("自主进化已成功", selfEvolutionDraftReport, StringComparison.Ordinal);
        Assert.DoesNotContain("已经证明有效", selfEvolutionDraftReport, StringComparison.Ordinal);
        Assert.DoesNotContain("自主进化已成功", selfEvolutionReport, StringComparison.Ordinal);
        Assert.DoesNotContain("已经证明有效", selfEvolutionReport, StringComparison.Ordinal);
        Assert.DoesNotContain("自主进化已成功", releaseNotes, StringComparison.Ordinal);
        Assert.DoesNotContain("已经证明有效", releaseNotes, StringComparison.Ordinal);
        Assert.DoesNotContain("autonomous evolution has succeeded", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("未来新建", kernelLoop, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreApiStabilityDocs_ShouldDeclareStableBoundariesAndCompatibilityPolicy()
    {
        var repoRoot = FindRepoRoot();
        var stabilityPath = Path.Combine(repoRoot, "docs", "architecture", "tianshu-core-api-stability-design.md");
        var architectureSpecPath = Path.Combine(repoRoot, "docs", "tianshu-architecture-spec.md");
        var contractsPath = Path.Combine(repoRoot, "docs", "architecture", "tianshu-contracts-architecture.md");
        var modulePlanePath = Path.Combine(repoRoot, "docs", "architecture", "tianshu-module-plane-design.md");

        Assert.True(File.Exists(stabilityPath), "缺少核心 API 稳定承诺文档。");

        var stability = File.ReadAllText(stabilityPath);
        var architectureSpec = File.ReadAllText(architectureSpecPath);
        var contracts = File.ReadAllText(contractsPath);
        var modulePlane = File.ReadAllText(modulePlanePath);

        var requiredStableSurfaces = new[]
        {
            "Contracts",
            "Module SDK",
            "Host Gateway",
            "Control Plane",
            "RuntimeStep",
            "StageGraph",
        };
        Assert.All(requiredStableSurfaces, term => Assert.Contains(term, stability, StringComparison.Ordinal));

        var requiredCompatibilityTerms = new[]
        {
            "Stable public contract",
            "Versioned public schema",
            "additive",
            "fail closed",
            "schema / contract version",
            "破坏性",
            "迁移",
            "deprecated",
            "未知主版本",
            "secret",
            "raw provider request body",
            "Implementation detail",
            "P31.2",
        };
        Assert.All(requiredCompatibilityTerms, term => Assert.Contains(term, stability, StringComparison.OrdinalIgnoreCase));

        Assert.Contains("旧配置", stability, StringComparison.Ordinal);
        Assert.Contains("旧 module manifest", stability, StringComparison.Ordinal);
        Assert.Contains("旧 StageGraph fixture", stability, StringComparison.Ordinal);
        Assert.Contains("旧 release package", stability, StringComparison.Ordinal);
        Assert.Contains("第三方模块只能依赖公开 contracts / abstractions", stability, StringComparison.Ordinal);
        Assert.Contains("estimated=true", stability, StringComparison.Ordinal);
        Assert.Contains("不得计入真实 provider usage 或 strategy promotion 成本", stability, StringComparison.Ordinal);

        Assert.Contains("tianshu-core-api-stability-design.md", architectureSpec, StringComparison.Ordinal);
        Assert.Contains("tianshu-core-api-stability-design.md", contracts, StringComparison.Ordinal);
        Assert.Contains("tianshu-core-api-stability-design.md", modulePlane, StringComparison.Ordinal);
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
