using TianShu.Contracts.Governance;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli;

internal static class CliApprovalResponseResolver
{
    public static bool TryParseDecisionToken(string? value, out ControlPlaneApprovalDecision decision)
    {
        decision = ControlPlaneApprovalDecision.Approve;
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return false;
        }

        switch (normalized)
        {
            case "accept":
            case "approve":
                decision = ControlPlaneApprovalDecision.Approve;
                return true;
            case "session":
            case "acceptforsession":
            case "approvesession":
                decision = ControlPlaneApprovalDecision.ApproveForSession;
                return true;
            case "always":
            case "acceptandremember":
            case "approvealways":
                decision = ControlPlaneApprovalDecision.ApproveAndRemember;
                return true;
            case "acceptwithexecpolicyamendment":
            case "acceptwithexecpolicy":
            case "approvewithexecpolicy":
                decision = ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment;
                return true;
            case "applynetworkpolicyamendment":
            case "approvenetworkrule":
            case "applynetworkrule":
                decision = ControlPlaneApprovalDecision.ApplyNetworkPolicyAmendment;
                return true;
            case "decline":
            case "reject":
                decision = ControlPlaneApprovalDecision.Decline;
                return true;
            case "cancel":
                decision = ControlPlaneApprovalDecision.Cancel;
                return true;
            default:
                return false;
        }
    }

    public static ControlPlaneApprovalResolution BuildResolution(
        string callId,
        CliPendingApprovalRequestState? pendingApproval,
        ControlPlaneApprovalDecision preferredDecision,
        string? note,
        out ControlPlaneApprovalDecision resolvedDecision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);

        var resolvedOption = ResolveDecisionOption(
            preferredDecision,
            pendingApproval?.AvailableDecisionOptions,
            pendingApproval?.AvailableDecisions);
        resolvedDecision = resolvedOption?.Type is { } resolvedType && TryParseDecisionToken(resolvedType, out var typedDecision)
            ? typedDecision
            : preferredDecision;

        return CliGovernanceEnvelopeFactory.Normalize(new ControlPlaneApprovalResolution
        {
            CallId = new CallId(callId),
            Decision = resolvedDecision,
            Note = note,
            CommandPrefix = resolvedOption?.ExecPolicyAmendment?.CommandPrefix?.ToArray() ?? Array.Empty<string>(),
            NetworkHost = resolvedOption?.NetworkPolicyAmendment?.Host,
            NetworkAction = resolvedOption?.NetworkPolicyAmendment?.Action,
        });
    }

    public static string ToDisplayToken(ControlPlaneApprovalDecision decision)
        => decision switch
        {
            ControlPlaneApprovalDecision.Approve => "accept",
            ControlPlaneApprovalDecision.ApproveForSession => "acceptForSession",
            ControlPlaneApprovalDecision.ApproveAndRemember => "acceptAndRemember",
            ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment => "acceptWithExecpolicyAmendment",
            ControlPlaneApprovalDecision.ApplyNetworkPolicyAmendment => "applyNetworkPolicyAmendment",
            ControlPlaneApprovalDecision.Decline => "decline",
            ControlPlaneApprovalDecision.Cancel => "cancel",
            _ => "decline",
        };

    private static CliApprovalDecisionOptionPayload? ResolveDecisionOption(
        ControlPlaneApprovalDecision preferredDecision,
        IReadOnlyList<CliApprovalDecisionOptionPayload>? availableDecisionOptions,
        IReadOnlyList<string>? availableDecisions)
    {
        var options = availableDecisionOptions is { Count: > 0 }
            ? availableDecisionOptions
            : BuildDecisionOptionsFromDecisionTokens(availableDecisions);

        if (options is null || options.Count == 0)
        {
            return null;
        }

        return preferredDecision switch
        {
            ControlPlaneApprovalDecision.ApproveAndRemember => ChoosePositiveOption(
                options,
                ControlPlaneApprovalDecision.ApproveAndRemember,
                ControlPlaneApprovalDecision.ApproveForSession,
                ControlPlaneApprovalDecision.Approve),
            ControlPlaneApprovalDecision.ApproveForSession => ChoosePositiveOption(
                options,
                ControlPlaneApprovalDecision.ApproveForSession,
                ControlPlaneApprovalDecision.Approve),
            ControlPlaneApprovalDecision.Approve => ChoosePositiveOption(
                options,
                ControlPlaneApprovalDecision.Approve),
            ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment => ChoosePositiveOption(
                options,
                ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment,
                ControlPlaneApprovalDecision.Approve,
                ControlPlaneApprovalDecision.ApproveForSession),
            ControlPlaneApprovalDecision.ApplyNetworkPolicyAmendment => ChoosePositiveOption(
                options,
                ControlPlaneApprovalDecision.ApplyNetworkPolicyAmendment,
                ControlPlaneApprovalDecision.Approve,
                ControlPlaneApprovalDecision.ApproveForSession),
            ControlPlaneApprovalDecision.Decline => ChooseNegativeOption(
                options,
                ControlPlaneApprovalDecision.Decline,
                ControlPlaneApprovalDecision.Cancel),
            ControlPlaneApprovalDecision.Cancel => ChooseNegativeOption(
                options,
                ControlPlaneApprovalDecision.Cancel,
                ControlPlaneApprovalDecision.Decline),
            _ => options[0],
        };
    }

    private static CliApprovalDecisionOptionPayload ChoosePositiveOption(
        IReadOnlyList<CliApprovalDecisionOptionPayload> options,
        params ControlPlaneApprovalDecision[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var matched = options.FirstOrDefault(option => MatchesDecision(option, candidate));
            if (matched is not null)
            {
                return matched;
            }
        }

        var amendmentFallback = options.FirstOrDefault(IsPositiveAmendmentOption);
        if (amendmentFallback is not null)
        {
            return amendmentFallback;
        }

        var cancel = options.FirstOrDefault(option => MatchesDecision(option, ControlPlaneApprovalDecision.Cancel));
        if (cancel is not null)
        {
            return cancel;
        }

        var decline = options.FirstOrDefault(option => MatchesDecision(option, ControlPlaneApprovalDecision.Decline));
        return decline ?? options[0];
    }

    private static CliApprovalDecisionOptionPayload ChooseNegativeOption(
        IReadOnlyList<CliApprovalDecisionOptionPayload> options,
        params ControlPlaneApprovalDecision[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var matched = options.FirstOrDefault(option => MatchesDecision(option, candidate));
            if (matched is not null)
            {
                return matched;
            }
        }

        var accept = options.FirstOrDefault(option => MatchesDecision(option, ControlPlaneApprovalDecision.Approve));
        if (accept is not null)
        {
            return accept;
        }

        return options[0];
    }

    private static IReadOnlyList<CliApprovalDecisionOptionPayload>? BuildDecisionOptionsFromDecisionTokens(
        IReadOnlyList<string>? availableDecisions)
    {
        if (availableDecisions is not { Count: > 0 })
        {
            return null;
        }

        var options = new List<CliApprovalDecisionOptionPayload>(availableDecisions.Count);
        foreach (var item in availableDecisions)
        {
            var normalized = Normalize(item);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            options.Add(new CliApprovalDecisionOptionPayload(normalized!));
        }

        return options.Count > 0 ? options : null;
    }

    private static bool MatchesDecision(CliApprovalDecisionOptionPayload option, ControlPlaneApprovalDecision decision)
        => string.Equals(option.Type, ToDisplayToken(decision), StringComparison.OrdinalIgnoreCase);

    private static bool IsPositiveAmendmentOption(CliApprovalDecisionOptionPayload option)
    {
        if (string.Equals(option.Type, ToDisplayToken(ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(option.Type, ToDisplayToken(ControlPlaneApprovalDecision.ApplyNetworkPolicyAmendment), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(option.NetworkPolicyAmendment?.Action, "deny", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0
            ? null
            : trimmed.Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
    }
}
