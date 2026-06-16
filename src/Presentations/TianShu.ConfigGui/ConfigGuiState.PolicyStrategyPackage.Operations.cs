using TianShu.Configuration;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.ConfigGui;

internal sealed partial class ConfigGuiState
{
    private void RefreshPolicyStrategyPackageProjection(string? selectedManifestPath)
    {
        var root = TianShuPolicyStrategyManifestConfiguration.ResolveRootDirectory(ConfigPath);
        policyStrategyPackageProjection = policyStrategyManifestConfiguration.Load(root, selectedManifestPath);
        PolicyStrategyPackageRootText.Value = $"扫描目录：{policyStrategyPackageProjection.PolicyStrategyRootDirectory}";
        PolicyStrategyPackageLabels = policyStrategyPackageProjection.Files.Select(static file => file.DisplayName).ToArray();
        selectedPolicyStrategyManifestPath = policyStrategyPackageProjection.SelectedManifestPath;

        var packageIndex = policyStrategyPackageProjection.SelectedManifestPath is null
            ? 0
            : policyStrategyPackageProjection.Files
                .Select((file, index) => new { file, index })
                .FirstOrDefault(item => string.Equals(item.file.Path, policyStrategyPackageProjection.SelectedManifestPath, StringComparison.OrdinalIgnoreCase))
                ?.index ?? 0;
        PolicyStrategyPackageIndex.Value = packageIndex;

        if (policyStrategyPackageProjection.SelectedPackage is null)
        {
            BeginNewPolicyStrategyPackage();
            PolicyStrategyPackageStatusText.Value = policyStrategyPackageProjection.Issues.Count == 0
                ? "未发现审批策略包 manifest；可新建用户策略包。"
                : string.Join(Environment.NewLine, policyStrategyPackageProjection.Issues.Select(static issue => issue.Message));
            return;
        }

        LoadPolicyStrategyPackageIntoEditor(policyStrategyPackageProjection.SelectedPackage);
        var issueText = policyStrategyPackageProjection.Issues.Count == 0
            ? string.Empty
            : $"，{policyStrategyPackageProjection.Issues.Count} 条问题";
        PolicyStrategyPackageStatusText.Value = $"已读取 {policyStrategyPackageProjection.Files.Count} 个审批策略包 manifest{issueText}。";
    }

    public void SelectPolicyStrategyPackageIndex(int index)
    {
        if (policyStrategyPackageProjection is null || policyStrategyPackageProjection.Files.Count == 0)
        {
            BeginNewPolicyStrategyPackage();
            return;
        }

        var packageIndex = Math.Clamp(index, 0, policyStrategyPackageProjection.Files.Count - 1);
        RefreshPolicyStrategyPackageProjection(policyStrategyPackageProjection.Files[packageIndex].Path);
    }

    public void BeginNewPolicyStrategyPackage()
    {
        selectedPolicyStrategyManifestPath = null;
        selectedPolicyStrategyId = null;
        PolicyStrategyPackageIndex.Value = 0;
        PolicyStrategyPackageId.Value = CreateUniquePolicyStrategyPackageId("custom-policy");
        PolicyStrategyPackageDisplayName.Value = PolicyStrategyPackageId.Value;
        PolicyStrategyPackageEnabledIndex.Value = 0;
        SelectPolicyStrategyPackageType("package");
        PolicyStrategyPackagePriority.Value = "0";
        PolicyStrategyLabels = [];
        BeginNewPolicyStrategy();
        PolicyStrategyPackageManifestPathText.Value = "保存目标：新建后写入 modules/policies/strategies/<package-id>/policy.toml";
        PolicyStrategyPackageStatusText.Value = "正在新建审批策略包；保存时会写入 modules/policies/strategies 目录。";
    }

    public void CopySelectedPolicyStrategyPackageToDraft()
    {
        if (policyStrategyPackageProjection?.SelectedPackage is not { } package)
        {
            PolicyStrategyPackageStatusText.Value = "没有可复制的审批策略包。";
            return;
        }

        selectedPolicyStrategyManifestPath = null;
        PolicyStrategyPackageId.Value = CreateUniquePolicyStrategyPackageId(package.Id);
        PolicyStrategyPackageDisplayName.Value = package.DisplayName;
        PolicyStrategyPackageManifestPathText.Value = "保存目标：复制后写入 modules/policies/strategies/<package-id>/policy.toml";
        PolicyStrategyPackageStatusText.Value = $"已复制审批策略包到草稿：{package.Id} -> {PolicyStrategyPackageId.Value}";
    }

    public void DeleteSelectedPolicyStrategyPackage()
    {
        if (string.IsNullOrWhiteSpace(selectedPolicyStrategyManifestPath))
        {
            PolicyStrategyPackageStatusText.Value = "没有可删除的审批策略包 manifest。";
            return;
        }

        try
        {
            var root = TianShuPolicyStrategyManifestConfiguration.ResolveRootDirectory(ConfigPath);
            policyStrategyManifestConfiguration.DeletePackage(root, selectedPolicyStrategyManifestPath);
            selectedPolicyStrategyManifestPath = null;
            RefreshPolicyStrategyPackageProjection(null);
            PolicyStrategyPackageStatusText.Value = "已删除用户审批策略包 manifest。";
        }
        catch (Exception ex)
        {
            PolicyStrategyPackageStatusText.Value = $"删除审批策略包失败：{ex.Message}";
        }
    }

    public void SelectPolicyStrategyIndex(int index)
    {
        var strategies = policyStrategyPackageProjection?.SelectedPackage?.Strategies ?? [];
        if (strategies.Count == 0)
        {
            BeginNewPolicyStrategy();
            return;
        }

        var strategyIndex = Math.Clamp(index, 0, strategies.Count - 1);
        LoadPolicyStrategyIntoEditor(strategies[strategyIndex], strategyIndex);
    }

    public void BeginNewPolicyStrategy()
    {
        selectedPolicyStrategyId = null;
        PolicyStrategyIndex.Value = 0;
        PolicyStrategyId.Value = CreateUniquePolicyStrategyId("strategy");
        PolicyStrategyDisplayName.Value = PolicyStrategyId.Value;
        PolicyStrategyEnabledIndex.Value = 0;
        SelectPolicyStrategyType("rules");
        SelectPolicyStrategyApprovalPolicy("on-request");
        SelectPolicyStrategySandboxMode("workspace-write");
        PolicyStrategyNetworkAccessIndex.Value = 1;
        PolicyStrategyAllowLoginShellIndex.Value = 0;
        PolicyStrategyWriteApprovalGlobs.Value = "**/*";
        PolicyStrategyDangerousCommandPatterns.Value = "rm, del, Remove-Item, git reset, git clean";
        PolicyStrategyCommandRules.Value = "ask:git reset; ask:git clean";
        PolicyStrategyNetworkRules.Value = string.Empty;
        PolicyStrategyPriority.Value = "0";
        PolicyStrategyResolvedPathText.Value = "策略保存后会写入当前审批策略包的 [[strategies]]。";
    }

    public void DeleteSelectedPolicyStrategy()
    {
        if (policyStrategyPackageProjection?.SelectedPackage is not { } package || string.IsNullOrWhiteSpace(selectedPolicyStrategyId))
        {
            PolicyStrategyPackageStatusText.Value = "没有可删除的策略条目。";
            return;
        }

        package.Strategies = package.Strategies
            .Where(strategy => !string.Equals(strategy.Id, selectedPolicyStrategyId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        selectedPolicyStrategyId = null;
        SavePolicyStrategyPackageValue(package);
    }

    public void SavePolicyStrategyPackage()
    {
        try
        {
            var package = BuildPolicyStrategyPackageFromEditor();
            SavePolicyStrategyPackageValue(package);
        }
        catch (Exception ex)
        {
            PolicyStrategyPackageStatusText.Value = $"保存审批策略包失败：{ex.Message}";
            UpdateContextPanel();
        }
    }

    private void SavePolicyStrategyPackageValue(PolicyStrategyPackageManifestValue package)
    {
        var root = TianShuPolicyStrategyManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var targetPath = string.IsNullOrWhiteSpace(selectedPolicyStrategyManifestPath)
            ? policyStrategyManifestConfiguration.CreatePackage(root, package.Id, overwrite: false)
            : selectedPolicyStrategyManifestPath;
        package.ManifestPath = targetPath;
        package.PackageDirectory = Path.GetDirectoryName(targetPath)!;
        policyStrategyManifestConfiguration.SavePackage(targetPath, package);
        RefreshPolicyStrategyPackageProjection(targetPath);
        PolicyStrategyPackageStatusText.Value = $"已保存审批策略包 manifest：{targetPath}";
        UpdateContextPanel();
    }

    private PolicyStrategyPackageManifestValue BuildPolicyStrategyPackageFromEditor()
    {
        var package = policyStrategyPackageProjection?.SelectedPackage is { } selectedPackage
            ? ClonePolicyStrategyPackage(selectedPackage)
            : new PolicyStrategyPackageManifestValue();
        package.Id = PolicyStrategyPackageId.Value.Trim();
        package.DisplayName = PolicyStrategyPackageDisplayName.Value.Trim();
        package.Enabled = PolicyStrategyPackageEnabledIndex.Value != 1;
        package.Type = GetSelectedPolicyStrategyPackageType();
        package.Priority = ParseIntOrDefault(PolicyStrategyPackagePriority.Value, 0);

        var strategy = new PolicyStrategyManifestValue
        {
            Id = PolicyStrategyId.Value.Trim(),
            DisplayName = PolicyStrategyDisplayName.Value.Trim(),
            Enabled = PolicyStrategyEnabledIndex.Value != 1,
            Type = GetSelectedPolicyStrategyType(),
            ApprovalPolicy = GetSelectedPolicyStrategyApprovalPolicy(),
            SandboxMode = GetSelectedPolicyStrategySandboxMode(),
            NetworkAccess = PolicyStrategyNetworkAccessIndex.Value != 1,
            AllowLoginShell = PolicyStrategyAllowLoginShellIndex.Value != 1,
            WriteRequiresApprovalGlobs = SplitCommaList(PolicyStrategyWriteApprovalGlobs.Value),
            DangerousCommandPatterns = SplitCommaList(PolicyStrategyDangerousCommandPatterns.Value),
            CommandRules = ParseCommandRuleList(PolicyStrategyCommandRules.Value),
            NetworkRules = ParseNetworkRuleList(PolicyStrategyNetworkRules.Value),
            Priority = ParseIntOrDefault(PolicyStrategyPriority.Value, 0),
        };

        var strategies = package.Strategies.ToList();
        if (!string.IsNullOrWhiteSpace(strategy.Id))
        {
            var index = strategies.FindIndex(item => string.Equals(item.Id, selectedPolicyStrategyId ?? strategy.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                strategies[index] = strategy;
            }
            else
            {
                strategies.Add(strategy);
            }
        }

        package.Strategies = strategies;
        return package;
    }

    private void LoadPolicyStrategyPackageIntoEditor(PolicyStrategyPackageManifestValue package)
    {
        PolicyStrategyPackageId.Value = package.Id;
        PolicyStrategyPackageDisplayName.Value = package.DisplayName;
        PolicyStrategyPackageEnabledIndex.Value = package.Enabled ? 0 : 1;
        SelectPolicyStrategyPackageType(package.Type);
        PolicyStrategyPackagePriority.Value = package.Priority.ToString();
        PolicyStrategyPackageManifestPathText.Value = $"Manifest：{package.ManifestPath}";
        PolicyStrategyLabels = package.Strategies.Select(static strategy => strategy.Id).ToArray();
        if (package.Strategies.Count == 0)
        {
            BeginNewPolicyStrategy();
        }
        else
        {
            var index = string.IsNullOrWhiteSpace(selectedPolicyStrategyId)
                ? 0
                : package.Strategies
                    .Select((strategy, strategyIndex) => new { strategy, strategyIndex })
                    .FirstOrDefault(item => string.Equals(item.strategy.Id, selectedPolicyStrategyId, StringComparison.OrdinalIgnoreCase))
                    ?.strategyIndex ?? 0;
            LoadPolicyStrategyIntoEditor(package.Strategies[index], index);
        }
    }

    private void LoadPolicyStrategyIntoEditor(PolicyStrategyManifestValue strategy, int index)
    {
        selectedPolicyStrategyId = strategy.Id;
        PolicyStrategyIndex.Value = index;
        PolicyStrategyId.Value = strategy.Id;
        PolicyStrategyDisplayName.Value = strategy.DisplayName;
        PolicyStrategyEnabledIndex.Value = strategy.Enabled ? 0 : 1;
        SelectPolicyStrategyType(strategy.Type);
        SelectPolicyStrategyApprovalPolicy(strategy.ApprovalPolicy);
        SelectPolicyStrategySandboxMode(strategy.SandboxMode);
        PolicyStrategyNetworkAccessIndex.Value = strategy.NetworkAccess == true ? 0 : 1;
        PolicyStrategyAllowLoginShellIndex.Value = strategy.AllowLoginShell != false ? 0 : 1;
        PolicyStrategyWriteApprovalGlobs.Value = JoinList(strategy.WriteRequiresApprovalGlobs);
        PolicyStrategyDangerousCommandPatterns.Value = JoinList(strategy.DangerousCommandPatterns);
        PolicyStrategyCommandRules.Value = JoinCommandRules(strategy.CommandRules);
        PolicyStrategyNetworkRules.Value = JoinNetworkRules(strategy.NetworkRules);
        PolicyStrategyPriority.Value = strategy.Priority.ToString();
        PolicyStrategyResolvedPathText.Value = policyStrategyPackageProjection?.SelectedPackage is { } package
            ? $"程序集解析路径：{TianShuPolicyStrategyManifestConfiguration.ResolveAssemblyFullPath(package, strategy)}"
            : "策略保存后会写入当前审批策略包的 [[strategies]]。";
    }

    private PolicyStrategyPackageManifestValue ClonePolicyStrategyPackage(PolicyStrategyPackageManifestValue source)
        => new()
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Enabled = source.Enabled,
            Type = source.Type,
            Priority = source.Priority,
            ManifestPath = source.ManifestPath,
            PackageDirectory = source.PackageDirectory,
            Strategies = source.Strategies.Select(static strategy => new PolicyStrategyManifestValue
            {
                Id = strategy.Id,
                DisplayName = strategy.DisplayName,
                Enabled = strategy.Enabled,
                Type = strategy.Type,
                Priority = strategy.Priority,
                ApprovalPolicy = strategy.ApprovalPolicy,
                SandboxMode = strategy.SandboxMode,
                NetworkAccess = strategy.NetworkAccess,
                AllowLoginShell = strategy.AllowLoginShell,
                WriteRequiresApprovalGlobs = strategy.WriteRequiresApprovalGlobs.ToArray(),
                DangerousCommandPatterns = strategy.DangerousCommandPatterns.ToArray(),
                CommandRules = strategy.CommandRules.Select(static rule => new PolicyStrategyCommandRuleValue(rule.Prefix.ToArray(), rule.Decision, rule.Reason)).ToArray(),
                NetworkRules = strategy.NetworkRules.Select(static rule => new PolicyStrategyNetworkRuleValue(rule.Protocol, rule.Host, rule.Decision, rule.Reason)).ToArray(),
                AssemblyPath = strategy.AssemblyPath,
                ProviderType = strategy.ProviderType,
            }).ToArray(),
        };

    private void SelectPolicyStrategyPackageType(string? type)
    {
        var index = FindLabelIndex(PolicyStrategyPackageTypeLabels, type, fallbackIndex: 1);
        PolicyStrategyPackageTypeIndex.Value = index;
        PolicyStrategyPackageType.Value = PolicyStrategyPackageTypeLabels[index];
    }

    private void SelectPolicyStrategyType(string? type)
    {
        var index = FindLabelIndex(PolicyStrategyTypeLabels, type, fallbackIndex: 0);
        PolicyStrategyTypeIndex.Value = index;
        PolicyStrategyType.Value = PolicyStrategyTypeLabels[index];
    }

    private void SelectPolicyStrategyApprovalPolicy(string? value)
    {
        PolicyStrategyApprovalPolicyIndex.Value = FindLabelIndex(PolicyStrategyApprovalPolicyLabels, value, fallbackIndex: 1);
    }

    private void SelectPolicyStrategySandboxMode(string? value)
    {
        PolicyStrategySandboxModeIndex.Value = FindLabelIndex(PolicyStrategySandboxModeLabels, value, fallbackIndex: 1);
    }

    private string GetSelectedPolicyStrategyPackageType()
    {
        var index = Math.Clamp(PolicyStrategyPackageTypeIndex.Value, 0, PolicyStrategyPackageTypeLabels.Count - 1);
        PolicyStrategyPackageType.Value = PolicyStrategyPackageTypeLabels[index];
        return PolicyStrategyPackageType.Value;
    }

    private string GetSelectedPolicyStrategyType()
    {
        var index = Math.Clamp(PolicyStrategyTypeIndex.Value, 0, PolicyStrategyTypeLabels.Count - 1);
        PolicyStrategyType.Value = PolicyStrategyTypeLabels[index];
        return PolicyStrategyType.Value;
    }

    private string GetSelectedPolicyStrategyApprovalPolicy()
    {
        var index = Math.Clamp(PolicyStrategyApprovalPolicyIndex.Value, 0, PolicyStrategyApprovalPolicyLabels.Count - 1);
        return PolicyStrategyApprovalPolicyLabels[index];
    }

    private string GetSelectedPolicyStrategySandboxMode()
    {
        var index = Math.Clamp(PolicyStrategySandboxModeIndex.Value, 0, PolicyStrategySandboxModeLabels.Count - 1);
        return PolicyStrategySandboxModeLabels[index];
    }

    private string CreateUniquePolicyStrategyPackageId(string baseId)
    {
        var root = TianShuPolicyStrategyManifestConfiguration.ResolveRootDirectory(ConfigPath);
        var existingIds = policyStrategyPackageProjection?.Files
            .Select(file => Path.GetFileName(Path.GetDirectoryName(file.Path)))
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "custom-policy" : baseId.Trim();
        var packageRoot = TianShuPolicyStrategyManifestConfiguration.ResolvePolicyStrategyRootDirectory(root);
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate) && !Directory.Exists(Path.Combine(packageRoot, candidate)))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private string CreateUniquePolicyStrategyId(string baseId)
    {
        var existingIds = policyStrategyPackageProjection?.SelectedPackage?.Strategies
            .Select(static strategy => strategy.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalized = string.IsNullOrWhiteSpace(baseId) ? "strategy" : baseId.Trim();
        for (var index = 0; index < 1000; index++)
        {
            var candidate = index == 0 ? normalized : $"{normalized}-{index + 1}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private static IReadOnlyList<PolicyStrategyCommandRuleValue> ParseCommandRuleList(string value)
        => value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static item =>
            {
                var separator = item.IndexOf(':');
                var decision = separator > 0 ? item[..separator].Trim() : "ask";
                var prefix = separator > 0 ? item[(separator + 1)..].Trim() : item.Trim();
                return new PolicyStrategyCommandRuleValue(SplitCommandPrefix(prefix), decision, null);
            })
            .Where(static rule => rule.Prefix.Count > 0)
            .ToArray();

    private static IReadOnlyList<PolicyStrategyNetworkRuleValue> ParseNetworkRuleList(string value)
        => value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static item =>
            {
                var parts = item.Split(':', 3, StringSplitOptions.TrimEntries);
                return parts.Length == 3
                    ? new PolicyStrategyNetworkRuleValue(parts[1], parts[2], parts[0], null)
                    : new PolicyStrategyNetworkRuleValue("https", item.Trim(), "ask", null);
            })
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.Host))
            .ToArray();

    private static IReadOnlyList<string> SplitCommandPrefix(string value)
        => value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

    private static string JoinCommandRules(IReadOnlyList<PolicyStrategyCommandRuleValue> rules)
        => string.Join("; ", rules.Select(static rule => $"{rule.Decision}:{string.Join(" ", rule.Prefix)}"));

    private static string JoinNetworkRules(IReadOnlyList<PolicyStrategyNetworkRuleValue> rules)
        => string.Join("; ", rules.Select(static rule => $"{rule.Decision}:{rule.Protocol}:{rule.Host}"));
}
