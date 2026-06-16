using TianShu.Contracts.Kernel;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Strategies;

/// <summary>
/// 进程内 Strategy registry 默认实现，管理 draft、validated、trial、promoted、deprecated 和 rolled_back。
/// In-process default Strategy registry managing draft, validated, trial, promoted, deprecated, and rolled_back.
/// </summary>
public sealed class StrategyRegistry : IStrategyRegistry
{
    private readonly Dictionary<StrategyId, StrategyRecord> strategies = new();
    private readonly List<StrategyRollbackRecord> rollbackRecords = new();

    public IReadOnlyList<StrategyRollbackRecord> RollbackRecords => rollbackRecords;

    public Task<StrategyRecord?> GetAsync(StrategyId strategyId, CancellationToken cancellationToken = default)
        => Task.FromResult(strategies.GetValueOrDefault(strategyId));

    public Task<StrategyRecord?> GetPromotedAsync(CoreIntentKind intentKind, CancellationToken cancellationToken = default)
        => Task.FromResult(strategies.Values
            .Where(static item => item.LifecycleState == StrategyLifecycleState.Promoted)
            .OrderByDescending(static item => item.UpdatedAt)
            .FirstOrDefault());

    public Task<IReadOnlyList<StrategyRecord>> ListCandidatesAsync(CoreIntentKind intentKind, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<StrategyRecord>>(strategies.Values
            .Where(static item => item.LifecycleState is StrategyLifecycleState.Validated or StrategyLifecycleState.Trial or StrategyLifecycleState.Promoted)
            .OrderByDescending(static item => item.UpdatedAt)
            .ToArray());

    public Task<KernelValidationResult> ValidateTransitionAsync(
        StrategyRecord strategy,
        StrategyLifecycleState targetState,
        IReadOnlyList<StrategyTransitionEvidence> evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(strategy);

        if (!IsAllowedTransition(strategy.LifecycleState, targetState))
        {
            return Task.FromResult(Rejected("kernel.strategy.illegal_transition", $"Strategy 不能从 {strategy.LifecycleState} 转换到 {targetState}。"));
        }

        if (targetState is StrategyLifecycleState.Validated or StrategyLifecycleState.Trial or StrategyLifecycleState.Promoted
            && (evidence is null || evidence.Count == 0))
        {
            return Task.FromResult(Rejected("kernel.strategy.missing_evidence", "Strategy lifecycle transition 必须包含 evidence。"));
        }

        if (targetState == StrategyLifecycleState.Promoted)
        {
            if (evidence is null || evidence.Count == 0)
            {
                return Task.FromResult(Rejected("kernel.strategy.missing_trace", "Strategy 晋升必须包含 trace evidence。"));
            }

            if (evidence.Any(static item => item.MetricRefs.Count == 0))
            {
                return Task.FromResult(Rejected("kernel.strategy.missing_evaluation", "Strategy 晋升必须包含 evaluation metric evidence。"));
            }

            if (evidence.All(static item => !item.HumanApproved))
            {
                return Task.FromResult(Rejected("kernel.strategy.missing_human_gate", "高风险 Strategy 晋升必须经过人工 gate。"));
            }
        }

        return Task.FromResult(new KernelValidationResult(KernelValidationDecision.Approved));
    }

    public Task<StrategyRecord> SaveDraftAsync(StrategyRecord strategy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        if (strategy.LifecycleState != StrategyLifecycleState.Draft)
        {
            throw new ArgumentException("SaveDraftAsync 只接受 Draft strategy。", nameof(strategy));
        }

        strategies[strategy.StrategyId] = strategy;
        return Task.FromResult(strategy);
    }

    public async Task<StrategyRecord> TransitionAsync(
        StrategyId strategyId,
        StrategyLifecycleState targetState,
        IReadOnlyList<StrategyTransitionEvidence> evidence,
        CancellationToken cancellationToken = default)
    {
        if (!strategies.TryGetValue(strategyId, out var strategy))
        {
            throw new KeyNotFoundException($"Strategy not found: {strategyId.Value}");
        }

        var validation = await ValidateTransitionAsync(strategy, targetState, evidence, cancellationToken).ConfigureAwait(false);
        if (!validation.IsApproved)
        {
            throw new InvalidOperationException(validation.Issues.Count == 0 ? "Strategy transition rejected." : validation.Issues[0].Message);
        }

        var updated = new StrategyRecord(
            strategy.StrategyId,
            strategy.Name,
            strategy.GraphId,
            targetState,
            strategy.TransitionEvidence.Concat(evidence ?? Array.Empty<StrategyTransitionEvidence>()).ToArray());

        strategies[strategyId] = updated;
        if (targetState == StrategyLifecycleState.RolledBack)
        {
            rollbackRecords.Add(new StrategyRollbackRecord(strategyId, strategy.LifecycleState, DateTimeOffset.UtcNow, evidence?.FirstOrDefault()?.EvidenceRef));
        }

        return updated;
    }

    private static bool IsAllowedTransition(StrategyLifecycleState current, StrategyLifecycleState target)
        => (current, target) switch
        {
            (StrategyLifecycleState.Draft, StrategyLifecycleState.Validated) => true,
            (StrategyLifecycleState.Validated, StrategyLifecycleState.Trial) => true,
            (StrategyLifecycleState.Trial, StrategyLifecycleState.Promoted) => true,
            (StrategyLifecycleState.Promoted, StrategyLifecycleState.Deprecated) => true,
            (_, StrategyLifecycleState.RolledBack) when current is not StrategyLifecycleState.Unspecified => true,
            _ => false,
        };

    private static KernelValidationResult Rejected(string code, string message)
        => new(
            KernelValidationDecision.Rejected,
            new[] { new KernelValidationIssue(code, message) });
}

/// <summary>
/// Strategy rollback 记录。
/// Strategy rollback record.
/// </summary>
public sealed record StrategyRollbackRecord(
    StrategyId StrategyId,
    StrategyLifecycleState PreviousState,
    DateTimeOffset RolledBackAt,
    string? ReasonRef);
