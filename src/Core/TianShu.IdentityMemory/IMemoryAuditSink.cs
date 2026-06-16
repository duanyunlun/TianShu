namespace TianShu.IdentityMemory;

/// <summary>
/// 记忆审计写入入口，用于记录不一定直接修改事实载荷的语义动作。
/// Memory audit sink for semantic operations that may not directly mutate fact payloads.
/// </summary>
public interface IMemoryAuditSink
{
    Task AppendAsync(
        TianShuMemoryAuditRecord auditRecord,
        CancellationToken cancellationToken);
}

/// <summary>
/// 空记忆审计 sink，适合未配置持久化审计的嵌入场景。
/// No-op memory audit sink for embedded scenarios without persistent audit.
/// </summary>
public sealed class NullMemoryAuditSink : IMemoryAuditSink
{
    public static NullMemoryAuditSink Instance { get; } = new();

    private NullMemoryAuditSink()
    {
    }

    public Task AppendAsync(
        TianShuMemoryAuditRecord auditRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditRecord);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

/// <summary>
/// 将记忆审计记录写入 TianShu 本地记忆 store。
/// Writes memory audit entries into the TianShu local memory store.
/// </summary>
public sealed class TianShuLocalMemoryAuditSink : IMemoryAuditSink
{
    private readonly ITianShuLocalMemoryStore store;

    public TianShuLocalMemoryAuditSink(ITianShuLocalMemoryStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task AppendAsync(
        TianShuMemoryAuditRecord auditRecord,
        CancellationToken cancellationToken)
        => store.AppendAuditRecordAsync(auditRecord, cancellationToken);
}
