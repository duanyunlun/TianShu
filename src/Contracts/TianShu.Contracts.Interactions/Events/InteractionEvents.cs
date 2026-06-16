using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Interactions;

/// <summary>
/// 交互包络已被控制平面接受。
/// Event emitted when an interaction envelope has been accepted by the control plane.
/// </summary>
public sealed record InteractionEnvelopeAccepted(
    InteractionEnvelopeId EnvelopeId,
    InteractionTarget? Target);

/// <summary>
/// 交互包络已被控制平面拒绝。
/// Event emitted when an interaction envelope has been rejected by the control plane.
/// </summary>
public sealed record InteractionEnvelopeRejected(
    InteractionEnvelopeId EnvelopeId,
    ProblemDetails Problem);
