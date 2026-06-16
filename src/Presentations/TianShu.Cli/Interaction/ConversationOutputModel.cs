namespace TianShu.Cli.Interaction;

internal sealed record ConversationOutputModel(
    IReadOnlyList<ConversationOutputItem> Items)
{
    public static ConversationOutputModel Empty { get; } = new([]);

    public static ConversationOutputModel FromBlocks(IReadOnlyList<ChatPresentationBlock> blocks)
    {
        var builder = new ConversationOutputBuilder();

        foreach (var block in blocks)
        {
            builder.AddBlock(block);
        }

        return builder.ToModel();
    }
}

/// <summary>
/// Builds the typed conversation output projection incrementally from committed blocks.
/// 从已提交的展示块增量构建 typed conversation output 投影，避免 renderer 反推分组语义。
/// </summary>
internal sealed class ConversationOutputBuilder
{
    private readonly List<ConversationOutputItem> items = [];
    private AssistantStepItem? currentStep;

    public void AddBlock(ChatPresentationBlock block)
    {
        switch (block)
        {
            case UserMessageBlock user:
                AddUserMessageBlock(user);
                break;

            case AssistantMessageBlock assistant:
                AddAssistantBlock(assistant);
                break;

            case ToolInvocationBlock tool:
                AddToolBlock(tool);
                break;

            case SystemNoticeBlock notice:
                AddNoticeBlock(notice);
                break;

            case PlanProgressBlock plan:
                AddPlanBlock(plan);
                break;
        }
    }

    public void AddAssistantBlock(AssistantMessageBlock assistant)
    {
        currentStep = new AssistantStepItem(assistant, []);
        items.Add(currentStep);
    }

    public void AddToolBlock(ToolInvocationBlock tool)
    {
        if (currentStep is not null)
        {
            currentStep.ToolInvocations.Add(tool);
            return;
        }

        items.Add(new ToolOutputItem(tool));
    }

    public void AddNoticeBlock(SystemNoticeBlock notice)
    {
        currentStep = null;
        items.Add(new NoticeOutputItem(notice));
    }

    public void AddPlanBlock(PlanProgressBlock plan)
    {
        currentStep = null;
        items.Add(new PlanOutputItem(plan));
    }

    public void AddUserMessageBlock(UserMessageBlock user)
    {
        currentStep = null;
        items.Add(new UserMessageOutputItem(user));
    }

    public ConversationOutputModel ToModel()
        => new(items.ToArray());
}

internal abstract record ConversationOutputItem;

internal sealed record AssistantStepItem(
    AssistantMessageBlock Summary,
    List<ToolInvocationBlock> ToolInvocations) : ConversationOutputItem
{
    public IReadOnlyList<ToolInvocationBlock> Tools => ToolInvocations;
}

internal sealed record UserMessageOutputItem(
    UserMessageBlock Message) : ConversationOutputItem;

internal sealed record ToolOutputItem(
    ToolInvocationBlock Tool) : ConversationOutputItem;

internal sealed record NoticeOutputItem(
    SystemNoticeBlock Notice) : ConversationOutputItem;

internal sealed record PlanOutputItem(
    PlanProgressBlock Plan) : ConversationOutputItem;
