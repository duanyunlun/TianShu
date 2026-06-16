using System.Linq;
using TianShu.Execution.Runtime.Models;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Execution.Runtime;

/// <summary>
/// 会话控制平面契约与 runtime 内部模型之间的映射辅助。
/// Mapping helpers between conversation control-plane contracts and runtime internal models.
/// </summary>
public sealed partial class TianShuExecutionRuntime
{
    private static FollowUpMode ToRuntimeFollowUpMode(ControlPlaneFollowUpMode mode)
        => mode switch
        {
            ControlPlaneFollowUpMode.Queue => FollowUpMode.Queue,
            ControlPlaneFollowUpMode.Steer => FollowUpMode.Steer,
            ControlPlaneFollowUpMode.Interrupt => FollowUpMode.Interrupt,
            _ => FollowUpMode.Queue,
        };

    private static ControlPlaneFollowUpMode? ToControlPlaneFollowUpMode(FollowUpMode? mode)
        => mode switch
        {
            FollowUpMode.Queue => ControlPlaneFollowUpMode.Queue,
            FollowUpMode.Steer => ControlPlaneFollowUpMode.Steer,
            FollowUpMode.Interrupt => ControlPlaneFollowUpMode.Interrupt,
            null => null,
            _ => null,
        };

    private static IReadOnlyList<AgentUserInput> ToRuntimeUserInputs(IReadOnlyList<ControlPlaneInputItem> inputs)
        => inputs.Select(ToRuntimeUserInput).ToArray();

    private static AgentUserInput ToRuntimeUserInput(ControlPlaneInputItem input)
        => input switch
        {
            ControlPlaneTextInput text => new TextUserInput
            {
                Type = text.Type,
                Text = text.Text,
                TextElements = (text.TextElements ?? Array.Empty<ControlPlaneTextElement>())
                    .Select(static element => new AgentTextElement
                    {
                        ByteRange = new AgentByteRange
                        {
                            Start = element.ByteRange.Start,
                            End = element.ByteRange.End,
                        },
                        Placeholder = element.Placeholder,
                    })
                    .ToArray(),
            },
            ControlPlaneImageInput image => new ImageUserInput
            {
                Type = image.Type,
                Url = image.Url,
            },
            ControlPlaneLocalImageInput localImage => new LocalImageUserInput
            {
                Type = localImage.Type,
                Path = localImage.Path,
            },
            ControlPlaneSkillInput skill => new SkillUserInput
            {
                Type = skill.Type,
                Name = skill.Name,
                Path = skill.Path,
            },
            ControlPlaneMentionInput mention => new MentionUserInput
            {
                Type = mention.Type,
                Name = mention.Name,
                Path = mention.Path,
            },
            _ => throw new NotSupportedException($"不支持的控制平面输入类型：{input.GetType().Name}"),
        };

    private static IReadOnlyList<ConversationMessage> ToRuntimeConversationHistory(IReadOnlyList<ControlPlaneConversationMessage> history)
        => history.Select(static message => new ConversationMessage
        {
            Role = message.Role switch
            {
                ControlPlaneConversationRole.System => ConversationRole.System,
                ControlPlaneConversationRole.Assistant => ConversationRole.Assistant,
                _ => ConversationRole.User,
            },
            Content = message.Content,
            ContentItems = ToRuntimeUserInputs(message.ContentItems),
            Timestamp = message.Timestamp,
            IsStreaming = message.IsStreaming,
        }).ToArray();

    private static ControlPlaneTurnSubmissionResult ToControlPlaneTurnSubmissionResult(AgentSendResult result)
        => new()
        {
            Accepted = result.Success,
            Message = result.Message,
            TurnId = string.IsNullOrWhiteSpace(result.TurnId) ? null : new TurnId(result.TurnId),
            TurnStatus = result.TurnStatus,
            CorrelationId = result.CorrelationId,
            RequestedMode = ToControlPlaneFollowUpMode(result.RequestedMode),
            EffectiveMode = ToControlPlaneFollowUpMode(result.EffectiveMode),
        };
}
