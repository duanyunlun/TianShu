namespace TianShu.Cli.Interaction;

internal abstract record ChatPresentationBlock;

internal sealed record AssistantMessageBlock(
    string Text,
    bool IsComplete) : ChatPresentationBlock;

internal sealed record UserMessageBlock(
    string Text,
    string? Label = null) : ChatPresentationBlock;

internal sealed record SystemNoticeBlock(
    string Text) : ChatPresentationBlock;

internal sealed record ToolInvocationBlock(
    ToolPresentationKind Kind,
    ToolInvocationTitle Title,
    ToolInvocationSubject? Subject,
    ToolInvocationResult? Result,
    ToolPresentationStatus Status,
    string? ErrorText = null) : ChatPresentationBlock
{
    public ToolInvocationBlock(
        string title,
        string? subject,
        string? summary,
        ToolPresentationStatus status,
        string? errorText = null)
        : this(
            ToolPresentationKind.Generic,
            new ToolInvocationTitle(title),
            string.IsNullOrWhiteSpace(subject) ? null : new ToolInvocationSubject(subject),
            string.IsNullOrWhiteSpace(summary) ? null : new ToolInvocationResult(summary),
            status,
            errorText)
    {
    }

    public string TitleText => Title.Text;

    public string? SubjectText => Subject?.Text;

    public string? Summary => Result?.Summary;
}

internal sealed record ToolInvocationTitle(string Text)
{
    public override string ToString() => Text;
}

internal sealed record ToolInvocationSubject(
    string Text,
    bool AlwaysShow = false)
{
    public override string ToString() => Text;
}

internal sealed record ToolInvocationResult(string Summary)
{
    public override string ToString() => Summary;
}

internal sealed record PlanProgressBlock(
    string? Title,
    int CompletedCount,
    int TotalCount,
    string? CurrentStep,
    IReadOnlyList<PlanProgressStep> Steps) : ChatPresentationBlock;

internal sealed record PlanProgressStep(
    string Sequence,
    string Text,
    PlanStepPresentationStatus Status,
    string? RawStatus);

internal enum ToolPresentationStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2,
    Canceled = 3,
}

internal enum ToolPresentationKind
{
    Generic = 0,
    Command = 1,
    FileChange = 2,
    CodePatch = 3,
    PlanUpdate = 4,
    WebSearch = 5,
    ImageGeneration = 6,
    ImageView = 7,
    Search = 8,
}

internal enum PlanStepPresentationStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
}
