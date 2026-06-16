using TianShu.Contracts.Conversations;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli;

internal static class CliConversationEnvelopeFactory
{
    private const string CliSurface = "cli";
    private const string TurnIntent = "turn_submission";
    private const string FollowUpIntent = "follow_up_submission";

    public static ControlPlaneSubmitTurnCommand Normalize(ControlPlaneSubmitTurnCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command with
        {
            Envelope = command.Envelope ?? BuildEnvelope(
                interactionId: $"cli-turn-{Guid.NewGuid():N}",
                inputs: command.Inputs,
                intent: TurnIntent),
        };
    }

    public static ControlPlaneSubmitFollowUpCommand Normalize(ControlPlaneSubmitFollowUpCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var envelopeIdSuffix = string.IsNullOrWhiteSpace(command.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : command.CorrelationId!;

        return command with
        {
            Envelope = command.Envelope ?? BuildEnvelope(
                interactionId: $"cli-followup-{envelopeIdSuffix}",
                inputs: command.Inputs,
                intent: FollowUpIntent),
        };
    }

    private static InteractionEnvelope BuildEnvelope(
        string interactionId,
        IReadOnlyList<ControlPlaneInputItem> inputs,
        string intent)
        => new(
            new InteractionEnvelopeId(interactionId),
            new InteractionSource(InteractionSourceKind.Host, CliSurface),
            ToInteractionItems(inputs),
            routingHint: new InteractionRoutingHint(Intent: intent, Surface: CliSurface));

    private static IReadOnlyList<InteractionItem> ToInteractionItems(IReadOnlyList<ControlPlaneInputItem> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var items = new List<InteractionItem>(inputs.Count);
        foreach (var input in inputs)
        {
            switch (input)
            {
                case ControlPlaneTextInput text:
                    items.Add(new TextInteractionItem(
                        text.Text,
                        text.TextElements?.Select(static element => new TextInteractionElement(
                            new InteractionByteRange(element.ByteRange.Start, element.ByteRange.End),
                            element.Placeholder)).ToArray()));
                    break;
                case ControlPlaneImageInput image:
                    items.Add(new ImageInteractionItem(image.Url));
                    break;
                case ControlPlaneLocalImageInput localImage:
                    items.Add(new LocalImageInteractionItem(localImage.Path));
                    break;
                case ControlPlaneSkillInput skill:
                    items.Add(new SkillInteractionItem(skill.Name, skill.Path));
                    break;
                case ControlPlaneMentionInput mention:
                    items.Add(new MentionInteractionItem(mention.Name, mention.Path));
                    break;
                default:
                    throw new NotSupportedException($"不支持的 CLI 会话输入类型：{input.GetType().Name}");
            }
        }

        return items;
    }
}
