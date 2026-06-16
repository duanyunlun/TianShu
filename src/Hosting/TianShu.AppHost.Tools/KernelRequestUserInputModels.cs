namespace TianShu.AppHost.Tools;

/// <summary>
/// `request_user_input` 工具在宿主交互层之间传递的题目与答案载荷。
/// Prompt and answer payloads exchanged by the `request_user_input` tool.
/// </summary>
internal sealed record KernelRequestUserInputOption(string Label, string Description);

internal sealed record KernelRequestUserInputQuestion(
    string Id,
    string Header,
    string Question,
    IReadOnlyList<KernelRequestUserInputOption>? Options,
    bool IsOther = false,
    bool IsSecret = false);

internal sealed record KernelRequestUserInputRequest(
    string ItemId,
    IReadOnlyList<KernelRequestUserInputQuestion> Questions);

internal sealed record KernelRequestUserInputAnswer(IReadOnlyList<string> Answers);

internal sealed record KernelRequestUserInputResponse(
    IReadOnlyDictionary<string, KernelRequestUserInputAnswer> Answers);
