using TianShu.Contracts.Conversations;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

/// <summary>
/// OpenAI Responses 风格 southbound 输入适配器。
/// Southbound input adapter for OpenAI Responses-style payloads.
/// </summary>
public sealed class OpenAiResponsesProtocolAdapter : IProtocolAdapter
{
    /// <summary>
    /// 默认 OpenAI Responses 协议适配器标识。
    /// Default OpenAI Responses protocol adapter identifier.
    /// </summary>
    public const string AdapterId = "openai-responses";

    /// <inheritdoc />
    public string Id => AdapterId;

    /// <inheritdoc />
    public bool IsExperimental => false;

    /// <inheritdoc />
    public string CapabilitySummary => "完整支持 OpenAI Responses 风格文本输入。";

    /// <inheritdoc />
    public object BuildTextUserInput(string text)
        => BuildUserInput(
            new ControlPlaneTextInput(text));

    /// <inheritdoc />
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
