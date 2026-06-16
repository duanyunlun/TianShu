using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Participants;

/// <summary>
/// 绑定参与者到会话的命令。
/// Command that binds a participant to a session.
/// </summary>
public sealed record BindParticipantToSession(SessionId SessionId, ParticipantId ParticipantId);

/// <summary>
/// 绑定参与者到工作流的命令。
/// Command that binds a participant to a workflow.
/// </summary>
public sealed record BindParticipantToWorkflow(WorkflowId WorkflowId, ParticipantId ParticipantId);

/// <summary>
/// 更新参与者角色的命令。
/// Command that updates a participant role.
/// </summary>
public sealed record UpdateParticipantRole(ParticipantId ParticipantId, string Role);
