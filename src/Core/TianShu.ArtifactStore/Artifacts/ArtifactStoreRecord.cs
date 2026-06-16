using System.Text.Json.Serialization;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Primitives;

namespace TianShu.ArtifactStore;

/// <summary>
/// Artifact store 记录，表示当前 artifact 状态及其运行时挂接信息。
/// Artifact store record representing the current artifact state and its runtime attachments.
/// </summary>
public sealed record ArtifactStoreRecord
{
    /// <summary>
    /// 初始化 artifact store 记录。
    /// Initializes an artifact store record.
    /// </summary>
    [JsonConstructor]
    public ArtifactStoreRecord(
        Artifact artifact,
        IReadOnlyList<string>? promotionChannels = null,
        IReadOnlyList<TaskId>? attachedTaskIds = null,
        long version = 1,
        DateTimeOffset? updatedAt = null)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "版本号必须大于零。");
        }

        Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
        PromotionChannels = promotionChannels ?? Array.Empty<string>();
        AttachedTaskIds = attachedTaskIds ?? Array.Empty<TaskId>();
        Version = version;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 当前 artifact。
    /// Current artifact state.
    /// </summary>
    public Artifact Artifact { get; }

    /// <summary>
    /// 已提升到的目标通道。
    /// Target channels that already received the artifact.
    /// </summary>
    public IReadOnlyList<string> PromotionChannels { get; }

    /// <summary>
    /// 已挂接到的任务标识集合。
    /// Task identifiers the artifact has been attached to.
    /// </summary>
    public IReadOnlyList<TaskId> AttachedTaskIds { get; }

    /// <summary>
    /// 当前记录版本。
    /// Current record version.
    /// </summary>
    public long Version { get; }

    /// <summary>
    /// 最后更新时间。
    /// Last update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// 基于新的 artifact 当前态创建下一版本。
    /// Creates the next version from a new current artifact state.
    /// </summary>
    public ArtifactStoreRecord WithArtifact(Artifact artifact)
        => new(artifact, PromotionChannels, AttachedTaskIds, Version + 1, DateTimeOffset.UtcNow);

    /// <summary>
    /// 基于新的提升通道创建下一版本。
    /// Creates the next version after adding a promotion channel.
    /// </summary>
    public ArtifactStoreRecord WithPromotionChannel(string targetChannel)
    {
        if (PromotionChannels.Any(existing => string.Equals(existing, targetChannel, StringComparison.Ordinal)))
        {
            return this;
        }

        return new(
            Artifact,
            PromotionChannels.Concat(new[] { targetChannel }).ToArray(),
            AttachedTaskIds,
            Version + 1,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// 基于新的任务挂接创建下一版本。
    /// Creates the next version after attaching the artifact to a task.
    /// </summary>
    public ArtifactStoreRecord WithTaskAttachment(TaskId taskId)
    {
        if (AttachedTaskIds.Any(existing => string.Equals(existing.Value, taskId.Value, StringComparison.Ordinal)))
        {
            return this;
        }

        return new(
            Artifact,
            PromotionChannels,
            AttachedTaskIds.Concat(new[] { taskId }).ToArray(),
            Version + 1,
            DateTimeOffset.UtcNow);
    }
}
