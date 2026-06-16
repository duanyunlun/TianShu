using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;
using TianShu.Contracts.Primitives;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelManagedNetworkAppHostRuntime : IAsyncDisposable
{
    private readonly KernelRolloutRecorder rolloutRecorder;
    private readonly Func<string?, IReadOnlyList<string>?, IReadOnlyList<string>?, KernelManagedNetworkSettings> resolveManagedNetworkSettingsWithSkillOverride;
    private readonly Func<string, object, string, CancellationToken, Task<JsonElement>> sendServerRequestAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly KernelManagedNetworkManager managedNetworkManager;
    private long sideEffectSequence;

    public KernelManagedNetworkAppHostRuntime(
        KernelExecPolicyManager execPolicyManager,
        KernelRolloutRecorder rolloutRecorder,
        Func<string?, IReadOnlyList<string>?, IReadOnlyList<string>?, KernelManagedNetworkSettings> resolveManagedNetworkSettingsWithSkillOverride,
        Func<string, object, string, CancellationToken, Task<JsonElement>> sendServerRequestAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync)
    {
        this.rolloutRecorder = rolloutRecorder;
        this.resolveManagedNetworkSettingsWithSkillOverride = resolveManagedNetworkSettingsWithSkillOverride;
        this.sendServerRequestAsync = sendServerRequestAsync;
        this.writeNotificationAsync = writeNotificationAsync;
        managedNetworkManager = new KernelManagedNetworkManager(execPolicyManager, RequestManagedNetworkApprovalAsync, EmitManagedNetworkSideEffectAsync);
    }

    public async Task<KernelManagedNetworkExecutionLease> BeginExecutionAsync(
        KernelManagedNetworkExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var executionRequest = KernelExecutionEnvelopeFactory.CreateManagedNetworkRequest(
            new KernelManagedNetworkExecutionEnvelopeRequest(
                request.ThreadId,
                request.TurnId,
                request.ItemId,
                request.Command,
                request.Cwd,
                request.SandboxPolicy,
                request.SandboxMode,
                request.ApprovalPolicy is null
                    ? null
                    : StructuredValue.FromPlainObject(request.ApprovalPolicy.ToPlainObject()),
                request.SkillAllowedDomains,
                request.SkillDeniedDomains,
                request.InteractionEnvelope));
        await rolloutRecorder.AppendExecutionRequestAsync(request.ThreadId, executionRequest, cancellationToken).ConfigureAwait(false);
        await rolloutRecorder.AppendExecutionEventAsync(
            request.ThreadId,
            KernelExecutionEnvelopeFactory.CreateStartedEvent(executionRequest, "managed network execution started"),
            cancellationToken).ConfigureAwait(false);

        try
        {
            var sandboxMode = Normalize(request.SandboxMode)
                ?? (request.SandboxPolicy is { ValueKind: JsonValueKind.Object } policy ? Normalize(ReadString(policy, "type")) : null);
            if (string.Equals(sandboxMode, "danger-full-access", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sandboxMode, "dangerFullAccess", StringComparison.OrdinalIgnoreCase))
            {
                var inactiveLease = KernelManagedNetworkExecutionLease.Inactive();
                await rolloutRecorder.AppendExecutionEventAsync(
                    request.ThreadId,
                    KernelExecutionEnvelopeFactory.CreateCompletedEvent(
                        executionRequest,
                        "managed network execution bypassed",
                        KernelExecutionEnvelopeFactory.CreateManagedNetworkData(
                            executionRequest,
                            KernelManagedNetworkAppHostUtilities.CreateManagedNetworkLeaseSnapshot(inactiveLease))),
                    cancellationToken).ConfigureAwait(false);
                return inactiveLease;
            }

            if (request.SandboxPolicy is { ValueKind: JsonValueKind.Object } sandboxPolicy
                && KernelManagedNetworkAppHostUtilities.IsSandboxPolicyNetworkEnabled(sandboxPolicy))
            {
                var inactiveLease = KernelManagedNetworkExecutionLease.Inactive();
                await rolloutRecorder.AppendExecutionEventAsync(
                    request.ThreadId,
                    KernelExecutionEnvelopeFactory.CreateCompletedEvent(
                        executionRequest,
                        "managed network execution bypassed",
                        KernelExecutionEnvelopeFactory.CreateManagedNetworkData(
                            executionRequest,
                            KernelManagedNetworkAppHostUtilities.CreateManagedNetworkLeaseSnapshot(inactiveLease))),
                    cancellationToken).ConfigureAwait(false);
                return inactiveLease;
            }

            var settings = resolveManagedNetworkSettingsWithSkillOverride(
                request.Cwd,
                request.SkillAllowedDomains,
                request.SkillDeniedDomains);
            var lease = await managedNetworkManager.BeginExecutionAsync(settings, request, cancellationToken).ConfigureAwait(false);
            await rolloutRecorder.AppendExecutionEventAsync(
                request.ThreadId,
                KernelExecutionEnvelopeFactory.CreateCompletedEvent(
                    executionRequest,
                    lease.IsActive ? "managed network execution initialized" : "managed network execution inactive",
                    KernelExecutionEnvelopeFactory.CreateManagedNetworkData(
                        executionRequest,
                        KernelManagedNetworkAppHostUtilities.CreateManagedNetworkLeaseSnapshot(lease))),
                cancellationToken).ConfigureAwait(false);
            return lease;
        }
        catch (Exception ex)
        {
            await rolloutRecorder.AppendExecutionEventAsync(
                request.ThreadId,
                KernelExecutionEnvelopeFactory.CreateFailedEvent(
                    executionRequest,
                    ex.Message,
                    KernelExecutionEnvelopeFactory.CreateFailureData(ex.Message)),
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync() => managedNetworkManager.DisposeAsync();

    internal async Task<KernelManagedNetworkApprovalResponse> RequestManagedNetworkApprovalAsync(
        KernelManagedNetworkApprovalRequest request,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            threadId = request.ThreadId,
            turnId = request.TurnId,
            itemId = request.ItemId,
            approvalId = request.ApprovalId,
            reason = request.Reason,
            networkApprovalContext = request.NetworkApprovalContext.ToPayload(),
            proposedNetworkPolicyAmendments = request.ProposedNetworkPolicyAmendments.Select(static amendment => amendment.ToPayload()).ToArray(),
            availableDecisions = request.AvailableDecisions.ToArray(),
        };

        var response = await sendServerRequestAsync(
            "item/commandExecution/requestApproval",
            payload,
            request.ThreadId,
            cancellationToken).ConfigureAwait(false);

        return new KernelManagedNetworkApprovalResponse(
            Decision: KernelManagedNetworkAppHostUtilities.ExtractApprovalDecision(response),
            NetworkPolicyAmendment: KernelManagedNetworkAppHostUtilities.TryReadNetworkPolicyAmendment(response, out var amendment) ? amendment : null,
            ApplyProposedExecPolicyAmendment: ReadBool(response, "applyProposedExecPolicyAmendment") == true);
    }

    internal async Task EmitManagedNetworkSideEffectAsync(
        KernelManagedNetworkExecutionRequest request,
        KernelManagedNetworkSideEffect sideEffect,
        CancellationToken cancellationToken)
    {
        var text = Normalize(sideEffect.Text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        switch (sideEffect.Kind)
        {
            case KernelManagedNetworkSideEffectKind.DeveloperMessage:
                var sequence = Interlocked.Increment(ref sideEffectSequence);
                var itemId = $"managed_network_message_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}_{sequence}";
                await writeNotificationAsync("rawResponseItem/completed", new
                {
                    threadId = request.ThreadId,
                    turnId = request.TurnId,
                    item = new
                    {
                        id = itemId,
                        type = "message",
                        role = "developer",
                        content = new object[]
                        {
                            new
                            {
                                type = "input_text",
                                text,
                            },
                        },
                    },
                }, cancellationToken).ConfigureAwait(false);
                break;

            case KernelManagedNetworkSideEffectKind.Warning:
            default:
                break;
        }
    }

    private static bool? ReadBool(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
