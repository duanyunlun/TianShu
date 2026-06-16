using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Governance;

/// <summary>
/// 解析审批命令。
/// Command that resolves an approval.
/// </summary>
public sealed record ResolveApproval(
    ApprovalId ApprovalId,
    bool Approved,
    ParticipantRef ResolvedByParticipant,
    string? Note = null);

/// <summary>
/// 提交用户补录命令。
/// Command that submits requested user input.
/// </summary>
public sealed record SubmitUserInput(
    UserInputRequestId RequestId,
    ParticipantRef SubmittedByParticipant,
    StructuredValue Payload);

/// <summary>
/// 授予权限命令。
/// Command that grants permission.
/// </summary>
public sealed record GrantPermission(PermissionGrant Grant);

/// <summary>
/// 风险确认命令。
/// Command that acknowledges a risk.
/// </summary>
public sealed record AcknowledgeRisk(CallId CallId, ParticipantRef AcknowledgedByParticipant, string Note);
