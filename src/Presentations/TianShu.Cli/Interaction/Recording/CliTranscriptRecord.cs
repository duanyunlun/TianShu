namespace TianShu.Cli.Interaction.Recording;

/// <summary>
/// Classifies persisted chat transcript entries for filtering and future UI projection.
/// 对持久化 chat transcript 条目做类型区分，便于后续筛选与 UI 投影。
/// </summary>
internal enum CliTranscriptRecordKind
{
    UserMessage,
    AssistantText,
    ActionableStatus,
    LifecycleDebug,
    Error,
}

/// <summary>
/// Represents one typed transcript entry emitted by the CLI chat runner.
/// 表示 CLI chat runner 生成的一条带类型 transcript 记录。
/// </summary>
internal sealed record CliTranscriptRecord(
    DateTimeOffset Timestamp,
    string Kind,
    string Text,
    bool AppendNewLine,
    bool IsError)
{
    public static CliTranscriptRecord Create(CliTranscriptRecordKind kind, string text, bool appendNewLine, bool isError)
        => new(DateTimeOffset.Now, ToWireKind(kind), text, appendNewLine, isError);

    private static string ToWireKind(CliTranscriptRecordKind kind)
        => kind switch
        {
            CliTranscriptRecordKind.UserMessage => "user_message",
            CliTranscriptRecordKind.AssistantText => "assistant_text",
            CliTranscriptRecordKind.ActionableStatus => "actionable_status",
            CliTranscriptRecordKind.LifecycleDebug => "lifecycle_debug",
            CliTranscriptRecordKind.Error => "error",
            _ => kind.ToString(),
        };
}
