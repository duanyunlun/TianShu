using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Artifacts;

/// <summary>
/// 发布产物命令。
/// Command that publishes an artifact.
/// </summary>
public sealed record PublishArtifact(Artifact Artifact);

/// <summary>
/// 提升产物命令。
/// Command that promotes an artifact.
/// </summary>
public sealed record PromoteArtifact(ArtifactId ArtifactId, string TargetChannel);

/// <summary>
/// 将产物挂接到任务命令。
/// Command that attaches an artifact to a task.
/// </summary>
public sealed record AttachArtifactToTask(ArtifactId ArtifactId, TaskId TaskId);
