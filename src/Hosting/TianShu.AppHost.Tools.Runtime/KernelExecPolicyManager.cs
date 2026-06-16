using System.Text;
using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.Configuration;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelExecPolicyManager
{
    private readonly string policyFilePath;
    private readonly IReadOnlyList<PolicyStrategyCommandRuleValue> policyStrategyCommandRules;
    private readonly IReadOnlyList<PolicyStrategyNetworkRuleValue> policyStrategyNetworkRules;
    private readonly object gate = new();
    private List<KernelExecPolicyRule> rules = new();
    private List<KernelExecPolicyNetworkRule> networkRules = new();

    public KernelExecPolicyManager(
        string rootDirectory,
        IReadOnlyList<PolicyStrategyCommandRuleValue>? policyStrategyCommandRules = null,
        IReadOnlyList<PolicyStrategyNetworkRuleValue>? policyStrategyNetworkRules = null)
    {
        policyFilePath = Path.Combine(rootDirectory, "exec-policy", "default.rules");
        this.policyStrategyCommandRules = policyStrategyCommandRules ?? [];
        this.policyStrategyNetworkRules = policyStrategyNetworkRules ?? [];
        Reload();
    }

    public string PolicyFilePath => policyFilePath;

    public IReadOnlyList<KernelExecPolicyRule> CurrentRules
    {
        get
        {
            lock (gate)
            {
                return rules.ToArray();
            }
        }
    }

    public IReadOnlyList<KernelExecPolicyNetworkRule> CurrentNetworkRules
    {
        get
        {
            lock (gate)
            {
                return networkRules.ToArray();
            }
        }
    }

    public KernelExecPolicyDecision EvaluateCommand(
        IReadOnlyList<string> commandArgs,
        string commandPreview,
        KernelApprovalPolicy? approvalPolicy,
        string? sandboxMode,
        bool alreadyApproved,
        bool requestsSandboxOverride = false)
    {
        var matchedRule = FindMatchingRule(commandArgs);
        if (matchedRule is not null)
        {
            return matchedRule.Decision switch
            {
                KernelExecPolicyRuleDecision.Allow => new KernelExecPolicyDecision(
                    KernelExecPolicyDecisionKind.Allow,
                    "exec_policy_rule_allow",
                    BypassSandbox: true,
                    ProposedAmendment: null),
                KernelExecPolicyRuleDecision.Deny => new KernelExecPolicyDecision(
                    KernelExecPolicyDecisionKind.Forbidden,
                    "exec_policy_rule_denied",
                    BypassSandbox: false,
                    ProposedAmendment: null),
                KernelExecPolicyRuleDecision.Ask when alreadyApproved => new KernelExecPolicyDecision(
                    KernelExecPolicyDecisionKind.Allow,
                    "exec_policy_rule_approved",
                    BypassSandbox: false,
                    ProposedAmendment: null),
                KernelExecPolicyRuleDecision.Ask => BuildPromptDecision(
                    approvalPolicy,
                    reason: "exec_policy_rule_requires_confirmation",
                    promptIsRule: true,
                    commandArgs),
                _ => new KernelExecPolicyDecision(KernelExecPolicyDecisionKind.Forbidden, "exec_policy_unknown_rule", false, null),
            };
        }

        if (ShouldPromptForUnmatchedCommand(
                commandArgs,
                approvalPolicy,
                sandboxMode,
                requestsSandboxOverride))
        {
            if (alreadyApproved)
            {
                return new KernelExecPolicyDecision(
                    KernelExecPolicyDecisionKind.Allow,
                    "approval_policy_approved",
                    BypassSandbox: false,
                    ProposedAmendment: null);
            }

            return BuildPromptDecision(
                approvalPolicy,
                reason: "approval_policy_requires_confirmation",
                promptIsRule: false,
                commandArgs);
        }

        return new KernelExecPolicyDecision(
            KernelExecPolicyDecisionKind.Allow,
            "exec_policy_default_allow",
            BypassSandbox: false,
            ProposedAmendment: null);
    }

    public KernelExecPolicyDecision EvaluateMutatingTool(
        string toolName,
        KernelApprovalPolicy? approvalPolicy,
        string? sandboxMode,
        bool alreadyApproved)
    {
        if (ShouldPromptForMutatingTool(approvalPolicy, sandboxMode))
        {
            if (alreadyApproved)
            {
                return new KernelExecPolicyDecision(
                    KernelExecPolicyDecisionKind.Allow,
                    "approval_policy_approved",
                    BypassSandbox: false,
                    ProposedAmendment: null);
            }

            return BuildPromptDecision(
                approvalPolicy,
                reason: "mutating_tool_requires_approval",
                promptIsRule: false,
                commandArgs: [$"tool:{toolName}"]);
        }

        return new KernelExecPolicyDecision(
            KernelExecPolicyDecisionKind.Allow,
            "exec_policy_default_allow",
            BypassSandbox: false,
            ProposedAmendment: null);
    }

    public KernelExecPolicyRuleDecision? EvaluateNetwork(KernelManagedNetworkProtocol protocol, string host)
    {
        var normalizedHost = KernelManagedNetworkHelpers.NormalizeHost(host);
        lock (gate)
        {
            var matched = networkRules.FirstOrDefault(rule => rule.Protocol == protocol && string.Equals(rule.Host, normalizedHost, StringComparison.OrdinalIgnoreCase));
            return matched?.Decision;
        }
    }

    public async Task AppendAmendmentAndUpdateAsync(KernelExecPolicyAmendment amendment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(amendment);
        var normalizedPrefix = amendment.CommandPrefix
            .Select(Normalize)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();
        if (normalizedPrefix.Length == 0)
        {
            throw new ArgumentException("Command prefix cannot be empty.", nameof(amendment));
        }

        lock (gate)
        {
            if (rules.Any(rule => rule.Decision == KernelExecPolicyRuleDecision.Allow
                                  && rule.CommandPrefix.Count == normalizedPrefix.Length
                                  && PrefixMatches(rule.CommandPrefix, normalizedPrefix)))
            {
                return;
            }
        }

        var line = BuildRuleLine(new KernelExecPolicyRule(KernelExecPolicyRuleDecision.Allow, normalizedPrefix));
        await AppendRuleLineAsync(line, cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendNetworkRuleAndUpdateAsync(
        KernelManagedNetworkProtocol protocol,
        string host,
        KernelManagedNetworkRuleAction action,
        CancellationToken cancellationToken)
    {
        var normalizedHost = KernelManagedNetworkHelpers.NormalizeHost(host);
        var decision = action == KernelManagedNetworkRuleAction.Allow
            ? KernelExecPolicyRuleDecision.Allow
            : KernelExecPolicyRuleDecision.Deny;

        lock (gate)
        {
            if (networkRules.Any(rule => rule.Protocol == protocol
                                         && rule.Decision == decision
                                         && string.Equals(rule.Host, normalizedHost, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
        }

        var line = BuildNetworkRuleLine(
            new KernelExecPolicyNetworkRule(decision, protocol, normalizedHost),
            BuildNetworkRuleJustification(protocol, normalizedHost, action));
        await AppendRuleLineAsync(line, cancellationToken).ConfigureAwait(false);
    }

    public void Reload()
    {
        lock (gate)
        {
            rules = LoadRules(policyFilePath)
                .Concat(LoadPolicyStrategyRules(policyStrategyCommandRules))
                .ToList();
            networkRules = LoadNetworkRules(policyFilePath)
                .Concat(LoadPolicyStrategyNetworkRules(policyStrategyNetworkRules))
                .ToList();
        }
    }

    private async Task AppendRuleLineAsync(string line, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(policyFilePath)!);
        if (!File.Exists(policyFilePath))
        {
            await File.WriteAllTextAsync(policyFilePath, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
            Reload();
            return;
        }

        var existing = await File.ReadAllTextAsync(policyFilePath, cancellationToken).ConfigureAwait(false);
        var prefix = existing.Length == 0 || existing[^1] == '\n' ? string.Empty : Environment.NewLine;
        await File.AppendAllTextAsync(policyFilePath, prefix + line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        Reload();
    }

    private KernelExecPolicyDecision BuildPromptDecision(
        KernelApprovalPolicy? approvalPolicy,
        string reason,
        bool promptIsRule,
        IReadOnlyList<string> commandArgs)
    {
        var rejectedReason = KernelApprovalPolicyHelpers.PromptRejectedByPolicy(approvalPolicy, promptIsRule);
        if (!string.IsNullOrWhiteSpace(rejectedReason))
        {
            return new KernelExecPolicyDecision(
                KernelExecPolicyDecisionKind.Forbidden,
                rejectedReason!,
                BypassSandbox: false,
                ProposedAmendment: null);
        }

        return new KernelExecPolicyDecision(
            KernelExecPolicyDecisionKind.NeedsApproval,
            reason,
            BypassSandbox: false,
            ProposedAmendment: TryDeriveAmendment(commandArgs));
    }

    private static bool ShouldPromptForUnmatchedCommand(
        IReadOnlyList<string> commandArgs,
        KernelApprovalPolicy? approvalPolicy,
        string? sandboxMode,
        bool requestsSandboxOverride)
    {
        if (KernelCommandSafetyClassifier.IsKnownSafeCommand(commandArgs))
        {
            return false;
        }

        if (KernelApprovalPolicyHelpers.IsUntrusted(approvalPolicy))
        {
            return true;
        }

        if (KernelApprovalPolicyHelpers.IsNever(approvalPolicy))
        {
            return false;
        }

        if (KernelCommandSafetyClassifier.CommandMightBeDangerous(commandArgs)
            || IsWindowsReadOnlySandbox(sandboxMode))
        {
            return true;
        }

        if (HasUnrestrictedFilesystem(sandboxMode) || KernelApprovalPolicyHelpers.IsOnFailure(approvalPolicy))
        {
            return false;
        }

        return (KernelApprovalPolicyHelpers.IsOnRequest(approvalPolicy)
                || KernelApprovalPolicyHelpers.IsGranular(approvalPolicy))
               && requestsSandboxOverride;
    }

    private static bool ShouldPromptForMutatingTool(KernelApprovalPolicy? approvalPolicy, string? sandboxMode)
    {
        if (KernelApprovalPolicyHelpers.IsUntrusted(approvalPolicy))
        {
            return true;
        }

        if (KernelApprovalPolicyHelpers.IsNever(approvalPolicy)
            || KernelApprovalPolicyHelpers.IsOnFailure(approvalPolicy)
            || HasUnrestrictedFilesystem(sandboxMode))
        {
            return false;
        }

        return KernelApprovalPolicyHelpers.IsOnRequest(approvalPolicy)
               || KernelApprovalPolicyHelpers.IsGranular(approvalPolicy);
    }

    private KernelExecPolicyRule? FindMatchingRule(IReadOnlyList<string> commandArgs)
    {
        lock (gate)
        {
            return rules.FirstOrDefault(rule => PrefixMatches(rule.CommandPrefix, commandArgs));
        }
    }

    private static bool PrefixMatches(IReadOnlyList<string> prefix, IReadOnlyList<string> commandArgs)
    {
        if (prefix.Count == 0 || commandArgs.Count < prefix.Count)
        {
            return false;
        }

        for (var i = 0; i < prefix.Count; i++)
        {
            if (!string.Equals(prefix[i], commandArgs[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static List<KernelExecPolicyRule> LoadRules(string policyFilePath)
    {
        var loaded = new List<KernelExecPolicyRule>();
        foreach (var statement in LoadPolicyStatements(policyFilePath))
        {
            if (TryParsePrefixRule(statement, out var rule) || TryParseLegacyPrefixRule(statement, out rule))
            {
                loaded.Add(rule);
            }
        }

        return loaded;
    }

    private static List<KernelExecPolicyRule> LoadPolicyStrategyRules(IReadOnlyList<PolicyStrategyCommandRuleValue> rules)
    {
        var loaded = new List<KernelExecPolicyRule>();
        foreach (var rule in rules)
        {
            if (rule.Prefix.Count == 0 || !TryParseDecision(rule.Decision, out var decision))
            {
                continue;
            }

            var normalizedPrefix = rule.Prefix
                .Select(Normalize)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .ToArray();
            if (normalizedPrefix.Length == 0)
            {
                continue;
            }

            loaded.Add(new KernelExecPolicyRule(decision, normalizedPrefix));
        }

        return loaded;
    }

    private static List<KernelExecPolicyNetworkRule> LoadNetworkRules(string policyFilePath)
    {
        var loaded = new List<KernelExecPolicyNetworkRule>();
        foreach (var statement in LoadPolicyStatements(policyFilePath))
        {
            if (TryParseNetworkRule(statement, out var rule) || TryParseLegacyNetworkRule(statement, out rule))
            {
                loaded.Add(rule);
            }
        }

        return loaded;
    }

    private static List<KernelExecPolicyNetworkRule> LoadPolicyStrategyNetworkRules(IReadOnlyList<PolicyStrategyNetworkRuleValue> rules)
    {
        var loaded = new List<KernelExecPolicyNetworkRule>();
        foreach (var rule in rules)
        {
            if (!TryParseNetworkProtocol(rule.Protocol, out var protocol)
                || !TryParseDecision(rule.Decision, out var decision))
            {
                continue;
            }

            var normalizedHost = KernelManagedNetworkHelpers.NormalizeHost(rule.Host);
            if (string.IsNullOrWhiteSpace(normalizedHost))
            {
                continue;
            }

            loaded.Add(new KernelExecPolicyNetworkRule(decision, protocol, normalizedHost));
        }

        return loaded;
    }

    private static List<string> LoadPolicyStatements(string policyFilePath)
    {
        var statements = new List<string>();
        if (!File.Exists(policyFilePath))
        {
            return statements;
        }

        var builder = new StringBuilder();
        var parenthesisDepth = 0;
        foreach (var rawLine in File.ReadLines(policyFilePath))
        {
            var trimmed = Normalize(rawLine);
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(trimmed);
            parenthesisDepth += CountParenthesisDelta(trimmed);
            if (parenthesisDepth > 0)
            {
                continue;
            }

            var statement = Normalize(builder.ToString());
            if (!string.IsNullOrWhiteSpace(statement))
            {
                statements.Add(statement!);
            }

            builder.Clear();
            parenthesisDepth = 0;
        }

        var tail = Normalize(builder.ToString());
        if (!string.IsNullOrWhiteSpace(tail))
        {
            statements.Add(tail!);
        }

        return statements;
    }

    private static int CountParenthesisDelta(string text)
    {
        var delta = 0;
        var inString = false;
        var escaped = false;
        foreach (var ch in text)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '(')
            {
                delta++;
            }
            else if (ch == ')')
            {
                delta--;
            }
        }

        return delta;
    }

    private static bool TryParsePrefixRule(string statement, out KernelExecPolicyRule rule)
    {
        rule = null!;
        if (!TryParseFunctionCall(statement, "prefix_rule", out var arguments))
        {
            return false;
        }

        var namedArguments = ParseNamedArguments(arguments);
        if (!TryGetArgumentValue(namedArguments, "pattern", out var patternRaw))
        {
            return false;
        }

        try
        {
            var prefix = JsonSerializer.Deserialize<string[]>(patternRaw) ?? Array.Empty<string>();
            var normalizedPrefix = prefix
                .Select(Normalize)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .ToArray();
            if (normalizedPrefix.Length == 0)
            {
                return false;
            }

            var decision = KernelExecPolicyRuleDecision.Ask;
            if (TryGetArgumentValue(namedArguments, "decision", out var decisionRaw))
            {
                if (!TryParseJsonString(decisionRaw, out var decisionToken)
                    || !TryParseDecision(decisionToken, out decision))
                {
                    return false;
                }
            }

            rule = new KernelExecPolicyRule(decision, normalizedPrefix);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseNetworkRule(string statement, out KernelExecPolicyNetworkRule rule)
    {
        rule = null!;
        if (!TryParseFunctionCall(statement, "network_rule", out var arguments))
        {
            return false;
        }

        var namedArguments = ParseNamedArguments(arguments);
        if (!TryGetArgumentValue(namedArguments, "host", out var hostRaw)
            || !TryParseJsonString(hostRaw, out var host)
            || !TryGetArgumentValue(namedArguments, "protocol", out var protocolRaw)
            || !TryParseJsonString(protocolRaw, out var protocolName)
            || !TryParseNetworkProtocol(protocolName, out var protocol)
            || !TryGetArgumentValue(namedArguments, "decision", out var decisionRaw)
            || !TryParseJsonString(decisionRaw, out var decisionName)
            || !TryParseDecision(decisionName, out var decision))
        {
            return false;
        }

        var normalizedHost = KernelManagedNetworkHelpers.NormalizeHost(host);
        if (string.IsNullOrWhiteSpace(normalizedHost))
        {
            return false;
        }

        rule = new KernelExecPolicyNetworkRule(decision, protocol, normalizedHost);
        return true;
    }

    private static bool TryParseLegacyPrefixRule(string statement, out KernelExecPolicyRule rule)
    {
        rule = null!;
        var space = statement.IndexOf(' ');
        if (space <= 0 || space + 1 >= statement.Length)
        {
            return false;
        }

        var decisionToken = statement[..space].Trim();
        var payload = statement[(space + 1)..].Trim();
        if (!TryParseDecision(decisionToken, out var decision))
        {
            return false;
        }

        try
        {
            var prefix = JsonSerializer.Deserialize<string[]>(payload) ?? Array.Empty<string>();
            var normalizedPrefix = prefix
                .Select(Normalize)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .ToArray();
            if (normalizedPrefix.Length == 0)
            {
                return false;
            }

            rule = new KernelExecPolicyRule(decision, normalizedPrefix);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseLegacyNetworkRule(string statement, out KernelExecPolicyNetworkRule rule)
    {
        rule = null!;
        var space = statement.IndexOf(' ');
        if (space <= 0 || space + 1 >= statement.Length)
        {
            return false;
        }

        var decisionToken = statement[..space].Trim();
        var payload = statement[(space + 1)..].Trim();
        if (!decisionToken.EndsWith("-network", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var decisionName = decisionToken[..^"-network".Length];
        if (!TryParseDecision(decisionName, out var decision))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var protocolName = Normalize(document.RootElement.GetProperty("protocol").GetString());
            var host = KernelManagedNetworkHelpers.NormalizeHost(document.RootElement.GetProperty("host").GetString() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(protocolName)
                || string.IsNullOrWhiteSpace(host)
                || !TryParseNetworkProtocol(protocolName!, out var protocol))
            {
                return false;
            }

            rule = new KernelExecPolicyNetworkRule(decision, protocol, host);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseDecision(string token, out KernelExecPolicyRuleDecision decision)
    {
        decision = token.ToLowerInvariant() switch
        {
            "allow" => KernelExecPolicyRuleDecision.Allow,
            "ask" or "prompt" => KernelExecPolicyRuleDecision.Ask,
            "deny" or "forbidden" => KernelExecPolicyRuleDecision.Deny,
            _ => default,
        };

        return token.Equals("allow", StringComparison.OrdinalIgnoreCase)
               || token.Equals("ask", StringComparison.OrdinalIgnoreCase)
               || token.Equals("prompt", StringComparison.OrdinalIgnoreCase)
               || token.Equals("deny", StringComparison.OrdinalIgnoreCase)
               || token.Equals("forbidden", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseNetworkProtocol(string token, out KernelManagedNetworkProtocol protocol)
    {
        protocol = token.ToLowerInvariant() switch
        {
            "http" => KernelManagedNetworkProtocol.Http,
            "https" => KernelManagedNetworkProtocol.Https,
            "https_connect" => KernelManagedNetworkProtocol.Https,
            "http-connect" => KernelManagedNetworkProtocol.Https,
            "socks5tcp" => KernelManagedNetworkProtocol.Socks5Tcp,
            "socks5_tcp" => KernelManagedNetworkProtocol.Socks5Tcp,
            "socks5-tcp" => KernelManagedNetworkProtocol.Socks5Tcp,
            "socks5udp" => KernelManagedNetworkProtocol.Socks5Udp,
            "socks5_udp" => KernelManagedNetworkProtocol.Socks5Udp,
            "socks5-udp" => KernelManagedNetworkProtocol.Socks5Udp,
            _ => default,
        };

        return token.Equals("http", StringComparison.OrdinalIgnoreCase)
               || token.Equals("https", StringComparison.OrdinalIgnoreCase)
               || token.Equals("https_connect", StringComparison.OrdinalIgnoreCase)
               || token.Equals("http-connect", StringComparison.OrdinalIgnoreCase)
               || token.Equals("socks5tcp", StringComparison.OrdinalIgnoreCase)
               || token.Equals("socks5_tcp", StringComparison.OrdinalIgnoreCase)
               || token.Equals("socks5-tcp", StringComparison.OrdinalIgnoreCase)
               || token.Equals("socks5udp", StringComparison.OrdinalIgnoreCase)
               || token.Equals("socks5_udp", StringComparison.OrdinalIgnoreCase)
               || token.Equals("socks5-udp", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRuleLine(KernelExecPolicyRule rule)
    {
        return $"prefix_rule(pattern={JsonSerializer.Serialize(rule.CommandPrefix)}, decision={JsonSerializer.Serialize(ToPrefixRuleDecisionToken(rule.Decision))})";
    }

    private static string BuildNetworkRuleLine(KernelExecPolicyNetworkRule rule, string? justification)
    {
        var arguments = new List<string>
        {
            $"host={JsonSerializer.Serialize(rule.Host)}",
            $"protocol={JsonSerializer.Serialize(ToPolicyProtocolString(rule.Protocol))}",
            $"decision={JsonSerializer.Serialize(ToNetworkRuleDecisionToken(rule.Decision))}",
        };

        if (!string.IsNullOrWhiteSpace(justification))
        {
            arguments.Add($"justification={JsonSerializer.Serialize(justification)}");
        }

        return $"network_rule({string.Join(", ", arguments)})";
    }

    private static string ToPrefixRuleDecisionToken(KernelExecPolicyRuleDecision decision)
    {
        return decision switch
        {
            KernelExecPolicyRuleDecision.Allow => "allow",
            KernelExecPolicyRuleDecision.Ask => "prompt",
            KernelExecPolicyRuleDecision.Deny => "forbidden",
            _ => throw new InvalidOperationException("Unknown exec policy rule decision."),
        };
    }

    private static string ToNetworkRuleDecisionToken(KernelExecPolicyRuleDecision decision)
    {
        return decision switch
        {
            KernelExecPolicyRuleDecision.Allow => "allow",
            KernelExecPolicyRuleDecision.Ask => "prompt",
            KernelExecPolicyRuleDecision.Deny => "deny",
            _ => throw new InvalidOperationException("Unknown network exec policy rule decision."),
        };
    }

    private static string ToPolicyProtocolString(KernelManagedNetworkProtocol protocol)
    {
        return protocol switch
        {
            KernelManagedNetworkProtocol.Http => "http",
            KernelManagedNetworkProtocol.Https => "https",
            KernelManagedNetworkProtocol.Socks5Tcp => "socks5_tcp",
            KernelManagedNetworkProtocol.Socks5Udp => "socks5_udp",
            _ => throw new InvalidOperationException("Unknown network protocol."),
        };
    }

    private static string BuildNetworkRuleJustification(
        KernelManagedNetworkProtocol protocol,
        string host,
        KernelManagedNetworkRuleAction action)
    {
        var verb = action == KernelManagedNetworkRuleAction.Allow ? "Allow" : "Deny";
        var protocolLabel = protocol switch
        {
            KernelManagedNetworkProtocol.Http => "http",
            KernelManagedNetworkProtocol.Https => "https_connect",
            KernelManagedNetworkProtocol.Socks5Tcp => "socks5_tcp",
            KernelManagedNetworkProtocol.Socks5Udp => "socks5_udp",
            _ => throw new InvalidOperationException("Unknown network protocol."),
        };

        return $"{verb} {protocolLabel} access to {host}";
    }

    private static bool TryParseFunctionCall(string statement, string functionName, out string arguments)
    {
        arguments = string.Empty;
        var normalized = Normalize(statement);
        if (string.IsNullOrWhiteSpace(normalized)
            || !normalized.StartsWith(functionName, StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith(')'))
        {
            return false;
        }

        var openParenthesis = normalized.IndexOf('(');
        var closeParenthesis = normalized.LastIndexOf(')');
        if (openParenthesis <= 0
            || closeParenthesis <= openParenthesis
            || !normalized[..openParenthesis].Trim().Equals(functionName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        arguments = normalized[(openParenthesis + 1)..closeParenthesis];
        return true;
    }

    private static Dictionary<string, string> ParseNamedArguments(string arguments)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in SplitFunctionArguments(arguments))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0 || separator + 1 >= segment.Length)
            {
                continue;
            }

            var key = Normalize(segment[..separator]);
            var value = Normalize(segment[(separator + 1)..]);
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                result[key!] = value!;
            }
        }

        return result;
    }

    private static IEnumerable<string> SplitFunctionArguments(string arguments)
    {
        var current = new StringBuilder();
        var squareBracketDepth = 0;
        var parenthesisDepth = 0;
        var inString = false;
        var escaped = false;

        foreach (var ch in arguments)
        {
            if (escaped)
            {
                current.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                current.Append(ch);
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                current.Append(ch);
                continue;
            }

            if (!inString)
            {
                if (ch == '[')
                {
                    squareBracketDepth++;
                }
                else if (ch == ']')
                {
                    squareBracketDepth--;
                }
                else if (ch == '(')
                {
                    parenthesisDepth++;
                }
                else if (ch == ')')
                {
                    parenthesisDepth--;
                }
                else if (ch == ',' && squareBracketDepth == 0 && parenthesisDepth == 0)
                {
                    var part = Normalize(current.ToString());
                    if (!string.IsNullOrWhiteSpace(part))
                    {
                        yield return part!;
                    }

                    current.Clear();
                    continue;
                }
            }

            current.Append(ch);
        }

        var tail = Normalize(current.ToString());
        if (!string.IsNullOrWhiteSpace(tail))
        {
            yield return tail!;
        }
    }

    private static bool TryGetArgumentValue(IReadOnlyDictionary<string, string> arguments, string key, out string value)
    {
        if (arguments.TryGetValue(key, out value!))
        {
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryParseJsonString(string raw, out string value)
    {
        value = string.Empty;
        try
        {
            value = JsonSerializer.Deserialize<string>(raw) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    private static KernelExecPolicyAmendment? TryDeriveAmendment(IReadOnlyList<string> commandArgs)
    {
        if (commandArgs.Count == 0)
        {
            return null;
        }

        var first = Normalize(commandArgs[0]);
        if (string.IsNullOrWhiteSpace(first))
        {
            return null;
        }

        var banned = new[]
        {
            "python", "python3", "py", "powershell", "powershell.exe", "pwsh", "bash", "sh", "zsh", "cmd.exe",
        };
        if (banned.Contains(first!, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var prefixLength = Math.Min(commandArgs.Count, first.Equals("git", StringComparison.OrdinalIgnoreCase) ? 2 : 3);
        var prefix = commandArgs
            .Take(prefixLength)
            .Select(Normalize)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!)
            .ToArray();
        return prefix.Length == 0 ? null : new KernelExecPolicyAmendment(prefix);
    }

    private static bool HasUnrestrictedFilesystem(string? sandboxMode)
    {
        var mode = Normalize(sandboxMode) ?? "workspaceWrite";
        return mode.Equals("danger-full-access", StringComparison.OrdinalIgnoreCase)
               || mode.Equals("dangerFullAccess", StringComparison.OrdinalIgnoreCase)
               || mode.Equals("externalSandbox", StringComparison.OrdinalIgnoreCase)
               || mode.Equals("external-sandbox", StringComparison.OrdinalIgnoreCase)
               || mode.Equals("external_sandbox", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsReadOnlySandbox(string? sandboxMode)
    {
        return OperatingSystem.IsWindows()
               && (Normalize(sandboxMode)?.Contains("read", StringComparison.OrdinalIgnoreCase) == true);
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










