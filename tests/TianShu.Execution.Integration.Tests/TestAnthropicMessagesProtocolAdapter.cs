using TianShu.Contracts.Conversations;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Integration.Tests;

internal sealed class TestAnthropicMessagesProtocolAdapter : IProtocolAdapter
{
    public const string AdapterId = "anthropic-messages";

    public string Id => AdapterId;

    public bool IsExperimental => true;

    public string CapabilitySummary => "实验性适配器：当前仅转换文本输入形状，未实现 Anthropic 专属完整协议语义。";

    public object BuildTextUserInput(string text)
        => BuildUserInput(
            new ControlPlaneTextInput(text));

    public object BuildUserInput(ControlPlaneInputItem input)
        => input switch
        {
            ControlPlaneTextInput text => new
            {
                type = "text",
                text = text.Text,
                text_elements = (text.TextElements ?? Array.Empty<ControlPlaneTextElement>()).Select(static element => new
                {
                    byte_range = new
                    {
                        start = element.ByteRange.Start,
                        end = element.ByteRange.End,
                    },
                    placeholder = element.Placeholder,
                }).ToArray(),
            },
            ControlPlaneImageInput image => new
            {
                type = "image",
                url = image.Url,
            },
            ControlPlaneLocalImageInput image => new
            {
                type = "local_image",
                path = image.Path,
            },
            ControlPlaneSkillInput skill => new
            {
                type = "skill",
                name = skill.Name,
                path = skill.Path,
            },
            ControlPlaneMentionInput mention => new
            {
                type = "mention",
                name = mention.Name,
                path = mention.Path,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(input), input.GetType(), "不支持的用户输入类型。"),
        };
}
