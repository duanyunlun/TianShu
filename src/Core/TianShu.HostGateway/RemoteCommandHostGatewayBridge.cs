using TianShu.Contracts.Host;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Remote;

namespace TianShu.HostGateway;

/// <summary>
/// 远程命令到 Host Gateway 的桥接器；它只做入口转换，不解释 Runtime 或 workspace 状态。
/// Bridge from remote commands to Host Gateway; it only adapts ingress and does not interpret Runtime or workspace state.
/// </summary>
public sealed class RemoteCommandHostGatewayBridge : IRemoteCommandIngress
{
    private readonly IHostGateway hostGateway;

    /// <summary>
    /// 初始化远程命令桥接器。
    /// Initializes the remote command bridge.
    /// </summary>
    public RemoteCommandHostGatewayBridge(IHostGateway hostGateway)
    {
        this.hostGateway = hostGateway ?? throw new ArgumentNullException(nameof(hostGateway));
    }

    /// <inheritdoc />
    public async ValueTask<RemoteCommandResult> SubmitCommandAsync<TPayload>(
        RemoteCommandEnvelope<TPayload> command,
        CancellationToken cancellationToken)
        where TPayload : IRemoteCommandPayload
    {
        ArgumentNullException.ThrowIfNull(command);

        var hostRequest = ToHostOperationRequest(command);
        var hostResult = await hostGateway.InvokeAsync(hostRequest, cancellationToken).ConfigureAwait(false);
        return ToRemoteCommandResult(command, hostResult);
    }

    private static HostOperationRequest ToHostOperationRequest<TPayload>(RemoteCommandEnvelope<TPayload> command)
        where TPayload : IRemoteCommandPayload
        => new(
            command.CommandId,
            $"remote:{command.DeviceId.Value}",
            ToHostOperationKind(command.Kind),
            BuildPayload(command),
            new HostContext(
                HostSurfaceKind.Service,
                $"remote:{command.DeviceId.Value}",
                metadata: new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["remoteCommandId"] = StructuredValue.FromString(command.CommandId),
                    ["remoteDeviceId"] = StructuredValue.FromString(command.DeviceId.Value),
                    ["remotePairingRef"] = StructuredValue.FromString(command.Audit.PairingRef),
                    ["remoteActorRef"] = StructuredValue.FromString(command.Audit.ActorRef),
                })),
            new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["source"] = StructuredValue.FromString("remote.command"),
                ["remoteCommandKind"] = StructuredValue.FromString(command.Kind.ToString()),
                ["remoteIdempotencyKey"] = StructuredValue.FromString(command.IdempotencyKey.Value),
            }));

    private static HostOperationKind ToHostOperationKind(RemoteCommandKind kind)
        => kind switch
        {
            RemoteCommandKind.SubmitMessage or RemoteCommandKind.Interrupt or RemoteCommandKind.Resume
                => HostOperationKind.CoreIntent,
            RemoteCommandKind.ApprovalDecision
                => HostOperationKind.Governance,
            RemoteCommandKind.Steer or RemoteCommandKind.CancelPendingOperation
                => HostOperationKind.Control,
            _ => HostOperationKind.Unspecified,
        };

    private static StructuredValue BuildPayload<TPayload>(RemoteCommandEnvelope<TPayload> command)
        where TPayload : IRemoteCommandPayload
    {
        var properties = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["operation_name"] = StructuredValue.FromString(GetOperationName(command.Kind)),
            ["command_id"] = StructuredValue.FromString(command.CommandId),
            ["thread_id"] = StructuredValue.FromString(command.ThreadId.Value),
            ["device_id"] = StructuredValue.FromString(command.DeviceId.Value),
            ["remote_command_kind"] = StructuredValue.FromString(command.Kind.ToString()),
            ["idempotency_key"] = StructuredValue.FromString(command.IdempotencyKey.Value),
            ["governance_envelope_id"] = StructuredValue.FromString($"remote:{command.CommandId}:governance"),
            ["policy_ids"] = StructuredValue.FromArray([StructuredValue.FromString("policy.remote.command")]),
            ["max_side_effect_level"] = StructuredValue.FromString(command.Scope.MaxSideEffectLevel.ToString()),
            ["requires_human_gate"] = StructuredValue.FromBoolean(true),
            ["audit"] = BuildAudit(command.Audit),
            ["scope"] = BuildScope(command.Scope),
            ["payload"] = BuildCommandPayload(command.Payload),
        };

        if (command.SessionId is { } sessionId)
        {
            properties["session_id"] = StructuredValue.FromString(sessionId.Value);
        }

        AddCoreIntentAliasFields(command, properties);
        return StructuredValue.FromObject(properties);
    }

    private static string GetOperationName(RemoteCommandKind kind)
        => kind switch
        {
            RemoteCommandKind.SubmitMessage => "remote.submit_message",
            RemoteCommandKind.Steer => "remote.steer",
            RemoteCommandKind.Interrupt => "remote.interrupt",
            RemoteCommandKind.Resume => "remote.resume",
            RemoteCommandKind.ApprovalDecision => "remote.approval_decision",
            RemoteCommandKind.CancelPendingOperation => "remote.cancel_pending_operation",
            _ => "remote.unspecified",
        };

    private static StructuredValue BuildAudit(RemoteAuditContext audit)
    {
        var properties = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["pairing_ref"] = StructuredValue.FromString(audit.PairingRef),
            ["actor_ref"] = StructuredValue.FromString(audit.ActorRef),
            ["audit_refs"] = ToStringArray(audit.AuditRefs),
        };

        if (!string.IsNullOrWhiteSpace(audit.NetworkRef))
        {
            properties["network_ref"] = StructuredValue.FromString(audit.NetworkRef);
        }

        return StructuredValue.FromObject(properties);
    }

    private static StructuredValue BuildScope(RemoteCommandScope scope)
        => StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["allowed_commands"] = StructuredValue.FromArray(scope.AllowedCommands.Select(static command => StructuredValue.FromString(command.ToString())).ToArray()),
            ["max_side_effect_level"] = StructuredValue.FromString(scope.MaxSideEffectLevel.ToString()),
            ["thread_refs"] = ToStringArray(scope.ThreadRefs),
            ["scope_refs"] = ToStringArray(scope.ScopeRefs),
        });

    private static StructuredValue BuildCommandPayload(IRemoteCommandPayload payload)
        => payload switch
        {
            RemoteSubmitMessagePayload submit => StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["message_text"] = StructuredValue.FromString(submit.MessageText),
                ["attachment_refs"] = ToStringArray(submit.AttachmentRefs),
            }),
            RemoteSteerPayload steer => StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["instruction"] = StructuredValue.FromString(steer.Instruction),
                ["target_run_ref"] = OptionalString(steer.TargetRunRef),
            }),
            RemoteInterruptPayload interrupt => StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["reason"] = StructuredValue.FromString(interrupt.Reason),
                ["active_run_ref"] = OptionalString(interrupt.ActiveRunRef),
            }),
            RemoteResumePayload resume => StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["checkpoint_ref"] = StructuredValue.FromString(resume.CheckpointRef),
                ["consume_pending_steer"] = StructuredValue.FromBoolean(resume.ConsumePendingSteer),
            }),
            RemoteApprovalDecisionPayload approval => StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["approval_id"] = StructuredValue.FromString(approval.ApprovalId.Value),
                ["decision"] = StructuredValue.FromString(approval.Decision.ToString()),
                ["reason"] = OptionalString(approval.Reason),
            }),
            RemoteCancelPendingOperationPayload cancel => StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["pending_operation_ref"] = StructuredValue.FromString(cancel.PendingOperationRef),
                ["reason"] = OptionalString(cancel.Reason),
            }),
            _ => StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["kind"] = StructuredValue.FromString(payload.Kind.ToString()),
            }),
        };

    private static void AddCoreIntentAliasFields<TPayload>(
        RemoteCommandEnvelope<TPayload> command,
        Dictionary<string, StructuredValue> properties)
        where TPayload : IRemoteCommandPayload
    {
        switch (command.Payload)
        {
            case RemoteSubmitMessagePayload:
                properties["user_input_ref"] = StructuredValue.FromString($"remote-command:{command.CommandId}:message");
                break;
            case RemoteInterruptPayload interrupt:
                properties["interrupt_reason"] = StructuredValue.FromString(interrupt.Reason);
                break;
            case RemoteResumePayload resume:
                properties["resume_token"] = StructuredValue.FromString(command.IdempotencyKey.Value);
                properties["checkpoint_ref"] = StructuredValue.FromString(resume.CheckpointRef);
                break;
        }
    }

    private static StructuredValue ToStringArray(IReadOnlyList<string> values)
        => StructuredValue.FromArray(values.Select(StructuredValue.FromString).ToArray());

    private static StructuredValue OptionalString(string? value)
        => string.IsNullOrWhiteSpace(value) ? StructuredValue.Null : StructuredValue.FromString(value.Trim());

    private static RemoteCommandResult ToRemoteCommandResult<TPayload>(
        RemoteCommandEnvelope<TPayload> command,
        HostOperationResult hostResult)
        where TPayload : IRemoteCommandPayload
    {
        var accepted = hostResult.Status is HostOperationStatus.Accepted or HostOperationStatus.Completed;
        var firstDiagnostic = hostResult.Diagnostics.FirstOrDefault();
        return new RemoteCommandResult(
            command.CommandId,
            command.Kind,
            accepted ? RemoteCommandAdmissionStatus.Accepted : RemoteCommandAdmissionStatus.Rejected,
            command.IdempotencyKey,
            acceptedOperationRef: accepted ? $"host-operation:{hostResult.OperationId}" : null,
            failureCode: accepted ? null : firstDiagnostic?.DiagnosticId ?? "remote.command.rejected",
            diagnosticsRef: firstDiagnostic is null ? null : $"diagnostics://remote-command/{command.CommandId}/{firstDiagnostic.DiagnosticId}");
    }
}
