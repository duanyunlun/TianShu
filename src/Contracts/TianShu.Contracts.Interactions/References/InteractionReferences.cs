using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Interactions;

/// <summary>
/// 交互包络引用快照，用于跨域携带最小交互身份信息。
/// Interaction-envelope reference snapshot used to carry minimal interaction identity across domains.
/// </summary>
public sealed record InteractionEnvelopeRef(
    InteractionEnvelopeId Id,
    InteractionSourceKind SourceKind,
    string Surface,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// 从完整交互包络生成最小引用。
    /// Creates a minimal reference from a full interaction envelope.
    /// </summary>
    public static InteractionEnvelopeRef From(InteractionEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new InteractionEnvelopeRef(
            envelope.Id,
            envelope.Source.Kind,
            envelope.Source.Surface,
            envelope.CreatedAt);
    }
}
