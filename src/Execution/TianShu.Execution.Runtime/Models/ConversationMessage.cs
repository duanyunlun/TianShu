namespace TianShu.Execution.Runtime.Models;

using TianShu.Execution.Runtime;

/// <summary>
/// runtime 内部会话角色载体。
/// Runtime-internal conversation role carrier.
/// </summary>
internal enum ConversationRole
{
    System = 0,
    User = 1,
    Assistant = 2,
}

/// <summary>
/// runtime 内部会话历史载体，仅用于 southbound 解析、provider 请求组装与测试夹具。
/// Runtime-internal conversation history carrier used only for southbound parsing, provider request assembly, and test fixtures.
/// </summary>
internal sealed class ConversationMessage
{
    public ConversationRole Role { get; set; }

    public string Content { get; set; } = string.Empty;

    public IReadOnlyList<AgentUserInput> ContentItems { get; set; } = Array.Empty<AgentUserInput>();

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public bool IsStreaming { get; set; }

    public void AppendContent(string delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        Content = string.Concat(Content, delta);
    }
}
