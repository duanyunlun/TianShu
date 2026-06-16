using TianShu.Configuration;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.RuntimeComposition;

/// <summary>
/// Policy Strategy 运行时组合入口。
/// Runtime composition entry point for policy strategy packages.
/// </summary>
internal static class PolicyStrategyRuntimeComposition
{
    /// <summary>
    /// 解析 AppHost 运行时需要消费的完整策略包。
    /// Resolves the complete strategy package consumed by the AppHost runtime.
    /// </summary>
    public static PolicyStrategyRuntimePackage ResolveEffectivePackage(string tianShuHome)
        => new(ResolveEffectiveDefaults(tianShuHome), ResolveEffectiveRules(tianShuHome));

    /// <summary>
    /// 用组合后的策略包创建执行策略管理器。
    /// Creates the execution policy manager from the composed strategy package.
    /// </summary>
    public static KernelExecPolicyManager CreateExecPolicyManager(
        string stateDirectory,
        PolicyStrategyRuntimePackage package)
        => new(stateDirectory, package.CommandRules, package.NetworkRules);

    /// <summary>
    /// 合并启用策略包提供的默认审批、沙箱、网络和 shell 策略。
    /// Merges default approval, sandbox, network, and shell policies from enabled strategy packages.
    /// </summary>
    public static PolicyStrategyEffectiveDefaults ResolveEffectiveDefaults(string tianShuHome)
    {
        try
        {
            return TianShuPolicyStrategyManifestConfiguration.ResolveEffectiveDefaults(tianShuHome);
        }
        catch
        {
            return new PolicyStrategyEffectiveDefaults(null, null, null, null);
        }
    }

    /// <summary>
    /// 合并启用策略包提供的命令与网络规则。
    /// Merges command and network rules from enabled strategy packages.
    /// </summary>
    public static PolicyStrategyRuntimeRules ResolveEffectiveRules(string tianShuHome)
    {
        try
        {
            return new PolicyStrategyRuntimeRules(
                TianShuPolicyStrategyManifestConfiguration.ResolveEffectiveCommandRules(tianShuHome),
                TianShuPolicyStrategyManifestConfiguration.ResolveEffectiveNetworkRules(tianShuHome));
        }
        catch
        {
            return PolicyStrategyRuntimeRules.Empty;
        }
    }

    /// <summary>
    /// 从用户配置路径解析 TianShu home 后合并默认策略。
    /// Resolves TianShu home from the user config path and merges default strategies.
    /// </summary>
    public static PolicyStrategyEffectiveDefaults ResolveEffectiveDefaultsFromConfigPath(string userConfigPath)
    {
        try
        {
            var rootDirectory = TianShuPolicyStrategyManifestConfiguration.ResolveRootDirectory(userConfigPath);
            return ResolveEffectiveDefaults(rootDirectory);
        }
        catch
        {
            return new PolicyStrategyEffectiveDefaults(null, null, null, null);
        }
    }
}

internal sealed record PolicyStrategyRuntimeRules(
    IReadOnlyList<PolicyStrategyCommandRuleValue> CommandRules,
    IReadOnlyList<PolicyStrategyNetworkRuleValue> NetworkRules)
{
    public static PolicyStrategyRuntimeRules Empty { get; } = new([], []);
}

internal sealed record PolicyStrategyRuntimePackage(
    PolicyStrategyEffectiveDefaults PermissionDefaults,
    IReadOnlyList<PolicyStrategyCommandRuleValue> CommandRules,
    IReadOnlyList<PolicyStrategyNetworkRuleValue> NetworkRules)
{
    public PolicyStrategyRuntimePackage(
        PolicyStrategyEffectiveDefaults permissionDefaults,
        PolicyStrategyRuntimeRules rules)
        : this(permissionDefaults, rules.CommandRules, rules.NetworkRules)
    {
    }
}
