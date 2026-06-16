using TianShu.Contracts.Memory;

namespace TianShu.IdentityMemory;

/// <summary>
/// 从输入中抽取候选记忆，不直接写入事实存储。
/// </summary>
public interface IMemoryExtractor
{
    Task<IReadOnlyList<MemoryCandidate>> ExtractAsync(
        ExtractMemory command,
        MemoryOperationContext context,
        CancellationToken cancellationToken);
}
