using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Artifacts;

/// <summary>
/// 产物种类。
/// Artifact kind.
/// </summary>
public enum ArtifactKind
{
    Document = 0,
    File = 1,
    Image = 2,
    StructuredData = 3,
    Trace = 4,
}

/// <summary>
/// 产物生命周期状态。
/// Artifact lifecycle state.
/// </summary>
public enum ArtifactLifecycleState
{
    Draft = 0,
    Published = 1,
    Promoted = 2,
    Archived = 3,
}

/// <summary>
/// 执行追踪引用。
/// Execution-trace reference.
/// </summary>
public sealed record ExecutionTraceRef(ExecutionTraceId TraceId, string? Summary = null);

/// <summary>
/// 产物谱系。
/// Artifact lineage.
/// </summary>
public sealed record ArtifactLineage(
    ArtifactRef? ParentArtifact = null,
    ExecutionId? ProducedByExecutionId = null,
    ArtifactRef? SourceArtifact = null);

/// <summary>
/// 产物模型。
/// Artifact model.
/// </summary>
public sealed record Artifact
{
    /// <summary>
    /// 初始化产物模型。
    /// Initializes an artifact model.
    /// </summary>
    public Artifact(
        ArtifactId id,
        CollaborationSpaceRef collaborationSpace,
        string name,
        ArtifactKind kind,
        ParticipantRef? producedByParticipant = null,
        ArtifactLineage? lineage = null,
        ArtifactLifecycleState state = ArtifactLifecycleState.Draft,
        ExecutionTraceRef? executionTrace = null,
        MetadataBag? metadata = null)
    {
        Id = id;
        CollaborationSpace = collaborationSpace ?? throw new ArgumentNullException(nameof(collaborationSpace));
        Name = IdentifierGuard.AgainstNullOrWhiteSpace(name, nameof(name));
        Kind = kind;
        ProducedByParticipant = producedByParticipant;
        Lineage = lineage;
        State = state;
        ExecutionTrace = executionTrace;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public ArtifactId Id { get; }

    public CollaborationSpaceRef CollaborationSpace { get; }

    public string Name { get; }

    public ArtifactKind Kind { get; }

    public ParticipantRef? ProducedByParticipant { get; }

    public ArtifactLineage? Lineage { get; }

    public ArtifactLifecycleState State { get; }

    public ExecutionTraceRef? ExecutionTrace { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 产物摘要。
/// Artifact summary.
/// </summary>
public sealed record ArtifactSummary(ArtifactId Id, string Name, ArtifactKind Kind, ArtifactLifecycleState State)
{
    public string Name { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Name, nameof(Name));
}
