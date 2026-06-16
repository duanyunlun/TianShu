using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Runtime;

internal enum ApprovalResponseDecision
{
    Accept = 0,
    AcceptForSession = 1,
    AcceptAndRemember = 2,
    AcceptWithExecPolicyAmendment = 3,
    ApplyNetworkPolicyAmendment = 4,
    Decline = 5,
    Cancel = 6,
}

internal sealed record ApprovalResponse(
    ApprovalResponseDecision Decision,
    string? Note = null,
    ExecPolicyAmendmentPayload? ExecPolicyAmendment = null,
    NetworkPolicyAmendmentPayload? NetworkPolicyAmendment = null)
{
    public ApprovalResponseDecision EffectiveDecision
        => HasRequiredPayload()
            ? Decision
            : ApprovalResponseDecision.Decline;

    public bool IsApproved
        => EffectiveDecision switch
        {
            ApprovalResponseDecision.Accept => true,
            ApprovalResponseDecision.AcceptForSession => true,
            ApprovalResponseDecision.AcceptAndRemember => true,
            ApprovalResponseDecision.AcceptWithExecPolicyAmendment => true,
            ApprovalResponseDecision.ApplyNetworkPolicyAmendment => !string.Equals(NetworkPolicyAmendment?.Action, "deny", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    public string ToProtocolDecisionToken()
        => EffectiveDecision switch
        {
            ApprovalResponseDecision.Accept => "accept",
            ApprovalResponseDecision.AcceptForSession => "acceptForSession",
            ApprovalResponseDecision.AcceptAndRemember => "acceptAndRemember",
            ApprovalResponseDecision.AcceptWithExecPolicyAmendment => "acceptWithExecpolicyAmendment",
            ApprovalResponseDecision.ApplyNetworkPolicyAmendment => "applyNetworkPolicyAmendment",
            ApprovalResponseDecision.Decline => "decline",
            ApprovalResponseDecision.Cancel => "cancel",
            _ => "decline",
        };

    public object ToProtocolDecisionPayload()
        => EffectiveDecision switch
        {
            ApprovalResponseDecision.Accept => "accept",
            ApprovalResponseDecision.AcceptForSession => "acceptForSession",
            ApprovalResponseDecision.AcceptAndRemember => "acceptAndRemember",
            ApprovalResponseDecision.AcceptWithExecPolicyAmendment => new Dictionary<string, object?>
            {
                ["acceptWithExecpolicyAmendment"] = new Dictionary<string, object?>
                {
                    ["execpolicy_amendment"] = ExecPolicyAmendment?.CommandPrefix?.ToArray() ?? Array.Empty<string>(),
                },
            },
            ApprovalResponseDecision.ApplyNetworkPolicyAmendment => new Dictionary<string, object?>
            {
                ["applyNetworkPolicyAmendment"] = new Dictionary<string, object?>
                {
                    ["network_policy_amendment"] = new Dictionary<string, object?>
                    {
                        ["host"] = NetworkPolicyAmendment?.Host ?? string.Empty,
                        ["action"] = NetworkPolicyAmendment?.Action ?? "allow",
                    },
                },
            },
            ApprovalResponseDecision.Decline => "decline",
            ApprovalResponseDecision.Cancel => "cancel",
            _ => "decline",
        };

    public static ApprovalResponse Accept(string? note = null)
        => new(ApprovalResponseDecision.Accept, note);

    public static ApprovalResponse AcceptForSession(string? note = null)
        => new(ApprovalResponseDecision.AcceptForSession, note);

    public static ApprovalResponse AcceptAndRemember(string? note = null)
        => new(ApprovalResponseDecision.AcceptAndRemember, note);

    public static ApprovalResponse AcceptWithExecPolicyAmendment(
        ExecPolicyAmendmentPayload amendment,
        string? note = null)
        => new(ApprovalResponseDecision.AcceptWithExecPolicyAmendment, note, amendment);

    public static ApprovalResponse ApplyNetworkPolicyAmendment(
        NetworkPolicyAmendmentPayload amendment,
        string? note = null)
        => new(ApprovalResponseDecision.ApplyNetworkPolicyAmendment, note, null, amendment);

    public static ApprovalResponse Decline(string? note = null)
        => new(ApprovalResponseDecision.Decline, note);

    public static ApprovalResponse Cancel(string? note = null)
        => new(ApprovalResponseDecision.Cancel, note);

    private bool HasRequiredPayload()
        => Decision switch
        {
            ApprovalResponseDecision.AcceptWithExecPolicyAmendment
                => ExecPolicyAmendment?.CommandPrefix is { Count: > 0 },
            ApprovalResponseDecision.ApplyNetworkPolicyAmendment
                => !string.IsNullOrWhiteSpace(NetworkPolicyAmendment?.Host)
                   && IsSupportedNetworkPolicyAction(NetworkPolicyAmendment?.Action),
            _ => true,
        };

    private static bool IsSupportedNetworkPolicyAction(string? action)
        => string.Equals(action, "allow", StringComparison.OrdinalIgnoreCase)
           || string.Equals(action, "deny", StringComparison.OrdinalIgnoreCase);
}
