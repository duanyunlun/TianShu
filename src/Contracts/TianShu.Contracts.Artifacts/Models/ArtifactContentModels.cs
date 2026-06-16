using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Artifacts;

/// <summary>
/// 产物内容种类。
/// Artifact content kind.
/// </summary>
public enum ArtifactContentKind
{
    Text = 0,
    Structured = 1,
    BinaryReference = 2,
}

/// <summary>
/// 产物内容的抽象基类。
/// Abstract base type for artifact content.
/// </summary>
public abstract record ArtifactContent
{
    /// <summary>
    /// 初始化产物内容。
    /// Initializes artifact content.
    /// </summary>
    protected ArtifactContent(
        ArtifactContentKind kind,
        string mediaType,
        MetadataBag? metadata = null)
    {
        Kind = kind;
        MediaType = IdentifierGuard.AgainstNullOrWhiteSpace(mediaType, nameof(mediaType));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    /// <summary>
    /// 内容种类。
    /// Content kind.
    /// </summary>
    public ArtifactContentKind Kind { get; }

    /// <summary>
    /// 内容媒体类型。
    /// Content media type.
    /// </summary>
    public string MediaType { get; }

    /// <summary>
    /// 内容元数据。
    /// Content metadata.
    /// </summary>
    public MetadataBag Metadata { get; }
}

/// <summary>
/// 文本型产物内容。
/// Text artifact content.
/// </summary>
public sealed record ArtifactTextContent : ArtifactContent
{
    /// <summary>
    /// 初始化文本型产物内容。
    /// Initializes text artifact content.
    /// </summary>
    public ArtifactTextContent(
        string text,
        string mediaType = "text/plain",
        string encoding = "utf-8",
        MetadataBag? metadata = null)
        : base(ArtifactContentKind.Text, mediaType, metadata)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        Encoding = IdentifierGuard.AgainstNullOrWhiteSpace(encoding, nameof(encoding));
    }

    /// <summary>
    /// 文本正文。
    /// Text payload.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// 文本编码。
    /// Text encoding.
    /// </summary>
    public string Encoding { get; }
}

/// <summary>
/// 结构化产物内容。
/// Structured artifact content.
/// </summary>
public sealed record ArtifactStructuredContent : ArtifactContent
{
    /// <summary>
    /// 初始化结构化产物内容。
    /// Initializes structured artifact content.
    /// </summary>
    public ArtifactStructuredContent(
        StructuredValue value,
        string mediaType = "application/json",
        string? schema = null,
        MetadataBag? metadata = null)
        : base(ArtifactContentKind.Structured, mediaType, metadata)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Schema = string.IsNullOrWhiteSpace(schema) ? null : schema.Trim();
    }

    /// <summary>
    /// 结构化值正文。
    /// Structured payload value.
    /// </summary>
    public StructuredValue Value { get; }

    /// <summary>
    /// 可选 schema 标识。
    /// Optional schema identifier.
    /// </summary>
    public string? Schema { get; }
}

/// <summary>
/// 二进制内容引用。
/// Binary-content reference.
/// </summary>
public sealed record ArtifactBinaryContentReference : ArtifactContent
{
    /// <summary>
    /// 初始化二进制内容引用。
    /// Initializes the binary-content reference.
    /// </summary>
    public ArtifactBinaryContentReference(
        string reference,
        string mediaType,
        long? sizeInBytes = null,
        string? digest = null,
        MetadataBag? metadata = null)
        : base(ArtifactContentKind.BinaryReference, mediaType, metadata)
    {
        if (sizeInBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes), "大小必须大于零。");
        }

        Reference = IdentifierGuard.AgainstNullOrWhiteSpace(reference, nameof(reference));
        SizeInBytes = sizeInBytes;
        Digest = string.IsNullOrWhiteSpace(digest) ? null : digest.Trim();
    }

    /// <summary>
    /// 外部内容引用。
    /// External content reference.
    /// </summary>
    public string Reference { get; }

    /// <summary>
    /// 可选大小摘要。
    /// Optional size summary.
    /// </summary>
    public long? SizeInBytes { get; }

    /// <summary>
    /// 可选摘要值。
    /// Optional digest value.
    /// </summary>
    public string? Digest { get; }
}

/// <summary>
/// 产物当前内容绑定。
/// Current artifact-content binding.
/// </summary>
public sealed record ArtifactContentBinding
{
    /// <summary>
    /// 初始化产物当前内容绑定。
    /// Initializes the current artifact-content binding.
    /// </summary>
    public ArtifactContentBinding(
        ArtifactId artifactId,
        ArtifactContent content,
        long version = 1,
        DateTimeOffset? updatedAt = null)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "版本号必须大于零。");
        }

        ArtifactId = artifactId;
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Version = version;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 关联的产物标识。
    /// Bound artifact identifier.
    /// </summary>
    public ArtifactId ArtifactId { get; }

    /// <summary>
    /// 当前内容。
    /// Current content.
    /// </summary>
    public ArtifactContent Content { get; }

    /// <summary>
    /// 当前版本号。
    /// Current version.
    /// </summary>
    public long Version { get; }

    /// <summary>
    /// 最后更新时间。
    /// Last update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// 基于新内容创建下一版本绑定。
    /// Creates the next binding version from new content.
    /// </summary>
    public ArtifactContentBinding WithContent(ArtifactContent content)
        => new(ArtifactId, content, Version + 1, DateTimeOffset.UtcNow);
}
