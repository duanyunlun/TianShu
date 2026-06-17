using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Strategy registry 抽象，负责 strategy 生命周期和候选读取，不直接执行策略。
/// Strategy registry abstraction for lifecycle and candidate lookup without executing strategies.
/// </summary>
public interface IStrategyRegistry
{
    Task<StrategyRecord?> GetAsync(StrategyId strategyId, CancellationToken cancellationToken = default);

    Task<StrategyRecord?> GetPromotedAsync(CoreIntentKind intentKind, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StrategyRecord>> ListCandidatesAsync(CoreIntentKind intentKind, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StrategyLifecycleAuditRecord>> ListAuditRecordsAsync(
        StrategyId strategyId,
        CancellationToken cancellationToken = default);

    Task<KernelValidationResult> ValidateTransitionAsync(
        StrategyRecord strategy,
        StrategyLifecycleState targetState,
        IReadOnlyList<StrategyTransitionEvidence> evidence,
        CancellationToken cancellationToken = default);

    Task<StrategyRecord> SaveCandidateAsync(
        StrategyRecord strategy,
        IReadOnlyList<StrategyTransitionEvidence> evidence,
        CancellationToken cancellationToken = default);

    Task<StrategyRecord> SaveDraftAsync(StrategyRecord strategy, CancellationToken cancellationToken = default);

    Task<StrategyRecord> TransitionAsync(
        StrategyId strategyId,
        StrategyLifecycleState targetState,
        IReadOnlyList<StrategyTransitionEvidence> evidence,
        CancellationToken cancellationToken = default);
}
