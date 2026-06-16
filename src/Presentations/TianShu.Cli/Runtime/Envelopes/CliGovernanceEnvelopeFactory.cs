using TianShu.Contracts.Governance;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli;

internal static class CliGovernanceEnvelopeFactory
{
    private const string CliSurface = "cli";

    public static ControlPlaneApprovalResolution Normalize(ControlPlaneApprovalResolution command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command with
        {
            Envelope = command.Envelope ?? BuildEnvelope(
                $"cli-approval-{command.CallId.Value}",
                "approval_response",
                BuildApprovalPayload(command)),
        };
    }

    public static ControlPlanePermissionGrant Normalize(ControlPlanePermissionGrant command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command with
        {
            Envelope = command.Envelope ?? BuildEnvelope(
                $"cli-permission-{command.CallId.Value}",
                "permission_response",
                BuildPermissionPayload(command)),
        };
    }

    public static ControlPlaneUserInputSubmission Normalize(ControlPlaneUserInputSubmission command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command with
        {
            Envelope = command.Envelope ?? BuildEnvelope(
                $"cli-userinput-{command.CallId.Value}",
                "user_input_submission",
                BuildUserInputPayload(command)),
        };
    }

    private static InteractionEnvelope BuildEnvelope(string interactionId, string semanticKind, StructuredValue payload)
        => new(
            new InteractionEnvelopeId(interactionId),
            new InteractionSource(InteractionSourceKind.Host, CliSurface),
            [new StructuredInteractionItem(semanticKind, payload)],
            routingHint: new InteractionRoutingHint(Intent: semanticKind, Surface: CliSurface));

    private static StructuredValue BuildApprovalPayload(ControlPlaneApprovalResolution command)
    {
        var payload = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["callId"] = StructuredValue.FromString(command.CallId.Value),
            ["decision"] = StructuredValue.FromString(CliApprovalResponseResolver.ToDisplayToken(command.Decision)),
        };

        if (!string.IsNullOrWhiteSpace(command.Note))
        {
            payload["note"] = StructuredValue.FromString(command.Note);
        }

        if (command.CommandPrefix.Count > 0)
        {
            payload["commandPrefix"] = StructuredValue.FromArray(command.CommandPrefix.Select(StructuredValue.FromString).ToArray());
        }

        if (!string.IsNullOrWhiteSpace(command.NetworkHost))
        {
            payload["networkHost"] = StructuredValue.FromString(command.NetworkHost);
        }

        if (!string.IsNullOrWhiteSpace(command.NetworkAction))
        {
            payload["networkAction"] = StructuredValue.FromString(command.NetworkAction);
        }

        return StructuredValue.FromObject(payload);
    }

    private static StructuredValue BuildPermissionPayload(ControlPlanePermissionGrant command)
        => StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["callId"] = StructuredValue.FromString(command.CallId.Value),
            ["scope"] = StructuredValue.FromString(command.Scope == ControlPlanePermissionScope.Session ? "session" : "turn"),
            ["permissions"] = StructuredValue.FromObject(command.Permissions.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal)),
        });

    private static StructuredValue BuildUserInputPayload(ControlPlaneUserInputSubmission command)
        => StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["callId"] = StructuredValue.FromString(command.CallId.Value),
            ["answers"] = StructuredValue.FromObject(command.Answers.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal)),
        });
}
