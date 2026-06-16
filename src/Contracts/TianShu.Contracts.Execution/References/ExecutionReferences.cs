using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Execution;

/// <summary>
/// 执行引用快照，用于跨域传递最小执行身份信息。
/// Execution reference snapshot used to move minimal execution identity across domains.
/// </summary>
public sealed record ExecutionRef(
    ExecutionId ExecutionId,
    ExecutionKind Kind,
    ThreadId? ThreadId = null,
    TurnId? TurnId = null);

/// <summary>
/// 执行输出引用，表达执行产出的稳定可索引对象。
/// Execution output reference describing the stable indexable output produced by an execution.
/// </summary>
public sealed record ExecutionOutputRef(ArtifactRef? Artifact = null, string? Channel = null, string? Summary = null);
