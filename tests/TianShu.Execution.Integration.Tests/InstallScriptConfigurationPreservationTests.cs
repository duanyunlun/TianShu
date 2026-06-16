using System.IO;
using System.Text.RegularExpressions;

namespace TianShu.Execution.Integration.Tests;

public sealed partial class InstallScriptConfigurationPreservationTests
{
    [Fact]
    public void InstallScript_PreservesExistingMainConfigurationAndStillBackfillsSafeReferences()
    {
        var script = ReadInstallScript();

        Assert.Contains("[switch]$PreserveConfig", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$OverwriteConfig", script, StringComparison.Ordinal);
        Assert.Contains("$mainConfigurationWasWritten = $false", script, StringComparison.Ordinal);
        Assert.Contains("if ($OverwriteConfig -and -not $PreserveConfig)", script, StringComparison.Ordinal);
        Assert.Contains("Write-Host \"保留已有配置：$userConfigPath\"", script, StringComparison.Ordinal);
        Assert.Contains("$mainConfigurationWasWritten = $true", script, StringComparison.Ordinal);
        Assert.Contains("tianshu.toml.bak-main-refs-$timestamp", script, StringComparison.Ordinal);
        Assert.Contains("已补齐主配置入口引用，并备份旧配置：$backupPath", script, StringComparison.Ordinal);

        var referencesCallMatches = MainConfigurationReferenceCallPattern().Matches(script);
        Assert.Single(referencesCallMatches);

        Assert.DoesNotContain("跳过已有主配置入口引用补齐", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallScript_DoesNotManageLegacyPromptFilesAsInstallTemplates()
    {
        var script = ReadInstallScript();

        Assert.DoesNotContain("default_prompt.toml", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(SingularPromptRootPattern(), script);
        Assert.DoesNotMatch(SingularPromptRemovalPattern(), script);
    }

    [Fact]
    public void InstallScript_ProtectsDefaultModuleConfigurationTemplates()
    {
        var script = ReadInstallScript();

        Assert.Matches(TemplateFileOverwriteGuardPattern(), script);
        Assert.Contains("Write-Host \"保留已有$Label：$Path\"", script, StringComparison.Ordinal);

        var templateCalls = DefaultModuleTemplateCallPattern().Matches(script)
            .Select(static match => match.Value)
            .ToArray();

        Assert.True(
            templateCalls.Length >= 20,
            "默认模块配置必须统一通过 Ensure-TemplateFile 安装，从而继承 PreserveConfig/OverwriteConfig 保留语义。");
        Assert.Contains(templateCalls, static call => call.Contains("$defaultAgentTemplatePath", StringComparison.Ordinal));
        Assert.Contains(templateCalls, static call => call.Contains("$defaultExecutionProfileTemplatePath", StringComparison.Ordinal));
        Assert.Contains(templateCalls, static call => call.Contains("$defaultMemoryProfileTemplatePath", StringComparison.Ordinal));
        Assert.Contains(templateCalls, static call => call.Contains("$defaultToolProfileTemplatePath", StringComparison.Ordinal));
        Assert.Contains(templateCalls, static call => call.Contains("$defaultWorkspaceProfileTemplatePath", StringComparison.Ordinal));
        Assert.Contains(templateCalls, static call => call.Contains("$defaultPermissionProfileTemplatePath", StringComparison.Ordinal));
        Assert.Contains(templateCalls, static call => call.Contains("$defaultGovernanceProfileTemplatePath", StringComparison.Ordinal));
        Assert.Contains("RefreshWhenRequiredMarkersMissing", script, StringComparison.Ordinal);
        Assert.Contains("-RefreshWhenRequiredMarkersMissing `", script, StringComparison.Ordinal);
    }

    private static string ReadInstallScript()
    {
        var repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot, "tools", "Install-TianShuCli.ps1"));
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

    [GeneratedRegex(@"Ensure-MainConfigurationReferences\s+-Path\s+\$userConfigPath", RegexOptions.CultureInvariant)]
    private static partial Regex MainConfigurationReferenceCallPattern();

    [GeneratedRegex(@"Join-Path\s+\$tianShuHomeFullPath\s+""prompt""", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SingularPromptRootPattern();

    [GeneratedRegex(@"Remove-InstallManagedStalePath\s+-Path\s+\$[A-Za-z0-9_]*Prompt(?!Packs)[A-Za-z0-9_]*Directory", RegexOptions.CultureInvariant)]
    private static partial Regex SingularPromptRemovalPattern();

    [GeneratedRegex(@"function\s+Ensure-TemplateFile\s*\{[\s\S]*?if\s*\(\$OverwriteConfig\s+-and\s+-not\s+\$PreserveConfig\)[\s\S]*?Write-Host\s+""保留已有\$Label：\$Path""[\s\S]*?\}", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateFileOverwriteGuardPattern();

    [GeneratedRegex(@"Ensure-TemplateFile\s+-Path\s+\$default[A-Za-z0-9]+(?:TemplatePath|ProfileTemplatePath|SandboxTemplatePath|AccountTemplatePath|DeviceTemplatePath|CollaborationProfileTemplatePath|WorkflowProfileTemplatePath)\s+-Content\s+\$[A-Za-z0-9]+(?:TemplateContent|Content)\s+-Label\s+""默认[^""]+""", RegexOptions.CultureInvariant)]
    private static partial Regex DefaultModuleTemplateCallPattern();
}
