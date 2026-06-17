using TianShu.Contracts.Kernel;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Strategies;

/// <summary>
/// 进程内 Strategy registry 默认实现，管理 candidate、trial、promoted、deprecated 和 rolled_back，并记录生命周期审计。
/// In-process default Strategy registry managing candidate, trial, promoted, deprecated, and rolled_back with lifecycle audit records.
/// </summary>
public sealed class StrategyRegistry : IStrategyRegistry
{
    private readonly Dictionary<StrategyId, StrategyRecord> strategies = new();
    private readonly Dictionary<StrategyId, List<StrategyLifecycleAuditRecord>> auditRecords = new();
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
            .Where(static item => item.LifecycleState is StrategyLifecycleState.Candidate or StrategyLifecycleState.Validated or StrategyLifecycleState.Trial or StrategyLifecycleState.Promoted)
            .OrderByDescending(static item => item.UpdatedAt)
            .ToArray());

    public Task<IReadOnlyList<StrategyLifecycleAuditRecord>> ListAuditRecordsAsync(
        StrategyId strategyId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<StrategyLifecycleAuditRecord>>(
            auditRecords.TryGetValue(strategyId, out var records)
                ? records.OrderBy(static item => item.OccurredAt).ToArray()
                : Array.Empty<StrategyLifecycleAuditRecord>());

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

        if (targetState is StrategyLifecycleState.Candidate or StrategyLifecycleState.Validated or StrategyLifecycleState.Trial or StrategyLifecycleState.Promoted or StrategyLifecycleState.Deprecated or StrategyLifecycleState.RolledBack
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

    public async Task<StrategyRecord> SaveCandidateAsync(
        StrategyRecord strategy,
        IReadOnlyList<StrategyTransitionEvidence> evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        if (strategy.LifecycleState != StrategyLifecycleState.Candidate)
        {
            throw new ArgumentException("SaveCandidateAsync 只接受 Candidate strategy。", nameof(strategy));
        }

        var validation = await ValidateTransitionAsync(
            new StrategyRecord(strategy.StrategyId, strategy.Name, strategy.GraphId, StrategyLifecycleState.Draft),
            StrategyLifecycleState.Candidate,
            evidence,
            cancellationToken).ConfigureAwait(false);
        if (!validation.IsApproved)
        {
            throw new InvalidOperationException(validation.Issues.Count == 0 ? "Strategy candidate registration rejected." : validation.Issues[0].Message);
        }

        var audit = CreateAuditRecord(strategy.StrategyId, StrategyLifecycleState.Unspecified, StrategyLifecycleState.Candidate, evidence);
        var stored = new StrategyRecord(
            strategy.StrategyId,
            strategy.Name,
            strategy.GraphId,
            StrategyLifecycleState.Candidate,
            strategy.TransitionEvidence.Concat(evidence).ToArray(),
            lifecycleAuditRecords: AppendAudit(strategy.StrategyId, audit));

        strategies[strategy.StrategyId] = stored;
        return stored;
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
            strategy.TransitionEvidence.Concat(evidence ?? Array.Empty<StrategyTransitionEvidence>()).ToArray(),
            lifecycleAuditRecords: AppendAudit(
                strategyId,
                CreateAuditRecord(strategyId, strategy.LifecycleState, targetState, evidence ?? Array.Empty<StrategyTransitionEvidence>())));

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
            (StrategyLifecycleState.Draft, StrategyLifecycleState.Candidate) => true,
            (StrategyLifecycleState.Draft, StrategyLifecycleState.Validated) => true,
            (StrategyLifecycleState.Validated, StrategyLifecycleState.Candidate) => true,
            (StrategyLifecycleState.Candidate, StrategyLifecycleState.Trial) => true,
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

    private IReadOnlyList<StrategyLifecycleAuditRecord> AppendAudit(StrategyId strategyId, StrategyLifecycleAuditRecord record)
    {
        if (!auditRecords.TryGetValue(strategyId, out var records))
        {
            records = [];
            auditRecords[strategyId] = records;
        }

        records.Add(record);
        return records.ToArray();
    }

    private static StrategyLifecycleAuditRecord CreateAuditRecord(
        StrategyId strategyId,
        StrategyLifecycleState previousState,
        StrategyLifecycleState targetState,
        IReadOnlyList<StrategyTransitionEvidence> evidence)
    {
        var evidenceArray = evidence ?? Array.Empty<StrategyTransitionEvidence>();
        return new StrategyLifecycleAuditRecord(
            $"strategy.lifecycle.{strategyId.Value}.{previousState.ToString().ToLowerInvariant()}.{targetState.ToString().ToLowerInvariant()}.{Guid.NewGuid():N}",
            strategyId,
            previousState,
            targetState,
            evidenceArray.Select(static item => item.EvidenceRef).ToArray(),
            evidenceArray.SelectMany(static item => item.MetricRefs).Distinct(StringComparer.Ordinal).ToArray(),
            humanApproved: evidenceArray.Any(static item => item.HumanApproved),
            reasonRef: evidenceArray.FirstOrDefault()?.EvidenceRef);
    }
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
