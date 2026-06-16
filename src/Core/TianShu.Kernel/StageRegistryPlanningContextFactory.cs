using TianShu.Contracts.Orchestration;

namespace TianShu.Kernel;

/// <summary>
/// Stage Registry 入口规划上下文工厂，负责把 Stage Registry 快照固化为 Kernel 入口规划上下文。
/// Stage Registry entry-planning context factory that pins registry snapshots into Kernel entry-planning context.
/// </summary>
public static class StageRegistryPlanningContextFactory
{
    /// <summary>
    /// 从原始运行时配置创建 Stage Registry 入口规划上下文。
    /// Creates a Stage Registry entry-planning context from raw runtime configuration.
    /// </summary>
    public static StageRegistryPlanningContext CreateContext(Dictionary<string, object?> rawConfig)
    {
        ArgumentNullException.ThrowIfNull(rawConfig);

        var registry = StageRegistryRuntimeComposition.CreateRegistryFromConfig(rawConfig);
        if (!registry.IsValid)
        {
            var issue = registry.Issues.First(static item => item.Severity == RuntimeStageRegistryIssueSeverity.Error);
            throw new InvalidOperationException($"Stage Registry 无效：{issue.Code}，{issue.Message}");
        }

        return new StageRegistryPlanningContext(
            registry.Stages,
            new SessionCoreLoopEntryPlanner(registry.Stages),
            registry.Issues);
    }
}

/// <summary>
/// Stage Registry 入口规划上下文。
/// Stage Registry entry-planning context.
/// </summary>
public sealed record StageRegistryPlanningContext(
    IReadOnlyList<StageDefinition> Stages,
    SessionCoreLoopEntryPlanner EntryPlanner,
    IReadOnlyList<RuntimeStageRegistryIssue> Issues);
