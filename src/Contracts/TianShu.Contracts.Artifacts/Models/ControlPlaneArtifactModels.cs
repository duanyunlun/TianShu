namespace TianShu.Contracts.Artifacts;

/// <summary>
/// 控制平面对话摘要产物。
/// Control-plane conversation summary artifact.
/// </summary>
public sealed record ControlPlaneConversationArtifact
{
    /// <summary>
    /// 会话摘要标识。
    /// Conversation summary identifier.
    /// </summary>
    public string ConversationId { get; init; } = string.Empty;

    /// <summary>
    /// 摘要文件路径。
    /// Summary file path.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// 摘要预览文本。
    /// Preview text.
    /// </summary>
    public string Preview { get; init; } = string.Empty;

    /// <summary>
    /// 原始时间戳文本。
    /// Original timestamp text.
    /// </summary>
    public string? Timestamp { get; init; }

    /// <summary>
    /// 最近更新时间文本。
    /// Last updated timestamp text.
    /// </summary>
    public string? UpdatedAt { get; init; }

    /// <summary>
    /// 模型提供方。
    /// Model provider.
    /// </summary>
    public string ModelProvider { get; init; } = string.Empty;

    /// <summary>
    /// 工作目录。
    /// Working directory.
    /// </summary>
    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>
    /// CLI 版本。
    /// CLI version.
    /// </summary>
    public string CliVersion { get; init; } = string.Empty;

    /// <summary>
    /// 会话来源。
    /// Session source.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Git SHA。
    /// Git SHA.
    /// </summary>
    public string? GitSha { get; init; }

    /// <summary>
    /// Git 分支。
    /// Git branch.
    /// </summary>
    public string? GitBranch { get; init; }

    /// <summary>
    /// Git 远端地址。
    /// Git remote URL.
    /// </summary>
    public string? GitOriginUrl { get; init; }
}

/// <summary>
/// 控制平面远端差异产物。
/// Control-plane artifact that carries the diff against remote.
/// </summary>
public sealed record ControlPlaneGitDiffArtifact
{
    /// <summary>
    /// 是否存在变更。
    /// Whether changes exist.
    /// </summary>
    public bool HasChanges { get; init; }

    /// <summary>
    /// 差异文本。
    /// Diff text.
    /// </summary>
    public string Diff { get; init; } = string.Empty;
}
