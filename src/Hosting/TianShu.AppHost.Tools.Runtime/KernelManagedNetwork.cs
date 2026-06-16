using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelManagedNetworkManager : IAsyncDisposable
{
    private readonly KernelExecPolicyManager execPolicyManager;
    private readonly Func<KernelManagedNetworkApprovalRequest, CancellationToken, Task<KernelManagedNetworkApprovalResponse>> approvalRequester;
    private readonly Func<KernelManagedNetworkExecutionRequest, KernelManagedNetworkSideEffect, CancellationToken, Task>? sideEffectSink;
    private readonly ConcurrentDictionary<string, KernelManagedNetworkSessionState> sessions = new(StringComparer.Ordinal);

    public KernelManagedNetworkManager(
        KernelExecPolicyManager execPolicyManager,
        Func<KernelManagedNetworkApprovalRequest, CancellationToken, Task<KernelManagedNetworkApprovalResponse>> approvalRequester,
        Func<KernelManagedNetworkExecutionRequest, KernelManagedNetworkSideEffect, CancellationToken, Task>? sideEffectSink = null)
    {
        this.execPolicyManager = execPolicyManager;
        this.approvalRequester = approvalRequester;
        this.sideEffectSink = sideEffectSink;
    }

    public async Task<KernelManagedNetworkExecutionLease> BeginExecutionAsync(
        KernelManagedNetworkSettings settings,
        KernelManagedNetworkExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!settings.IsActive)
        {
            return KernelManagedNetworkExecutionLease.Inactive();
        }

        var threadKey = KernelManagedNetworkHelpers.Normalize(request.ThreadId) ?? request.ThreadId;
        var session = sessions.AddOrUpdate(
            threadKey,
            static (_, state) => state,
            static (_, existing, state) => existing.IsCompatibleWith(state.Settings) ? existing : state,
            new KernelManagedNetworkSessionState(settings, execPolicyManager, approvalRequester, sideEffectSink));

        var lease = new KernelManagedNetworkExecutionLease(session, request);
        await lease.StartAsync(cancellationToken).ConfigureAwait(false);
        return lease;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in sessions.Values)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        sessions.Clear();
    }
}
internal sealed class KernelManagedNetworkSessionState : IAsyncDisposable
{
    private readonly ConcurrentDictionary<KernelManagedNetworkHostKey, byte> approvedHosts = new();
    private readonly ConcurrentDictionary<KernelManagedNetworkHostKey, byte> deniedHosts = new();
    private readonly ConcurrentDictionary<KernelManagedNetworkHostKey, Task<KernelManagedNetworkAuthorizationResult>> pendingApprovals = new();
    private readonly KernelExecPolicyManager execPolicyManager;
        private readonly Func<KernelManagedNetworkApprovalRequest, CancellationToken, Task<KernelManagedNetworkApprovalResponse>> approvalRequester;
    private readonly Func<KernelManagedNetworkExecutionRequest, KernelManagedNetworkSideEffect, CancellationToken, Task>? sideEffectSink;

    public KernelManagedNetworkSessionState(
        KernelManagedNetworkSettings settings,
        KernelExecPolicyManager execPolicyManager,
        Func<KernelManagedNetworkApprovalRequest, CancellationToken, Task<KernelManagedNetworkApprovalResponse>> approvalRequester,
        Func<KernelManagedNetworkExecutionRequest, KernelManagedNetworkSideEffect, CancellationToken, Task>? sideEffectSink = null)
    {
        Settings = settings;
        this.execPolicyManager = execPolicyManager;
        this.approvalRequester = approvalRequester;
        this.sideEffectSink = sideEffectSink;
    }

    public KernelManagedNetworkSettings Settings { get; }

    public bool IsCompatibleWith(KernelManagedNetworkSettings other)
        => EqualityComparer<KernelManagedNetworkSettings>.Default.Equals(Settings, other);

    public async Task<KernelManagedNetworkAuthorizationResult> AuthorizeAsync(
        KernelManagedNetworkExecutionRequest request,
        KernelManagedNetworkProtocol protocol,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var normalizedHost = KernelManagedNetworkHelpers.NormalizeHost(host);
        var key = new KernelManagedNetworkHostKey(normalizedHost, protocol, port);

        if ((protocol == KernelManagedNetworkProtocol.Socks5Tcp || protocol == KernelManagedNetworkProtocol.Socks5Udp)
            && !KernelManagedNetworkHelpers.IsFullMode(Settings.Mode))
        {
            return new KernelManagedNetworkAuthorizationResult(
                false,
                KernelManagedNetworkHelpers.CreatePolicyDeniedOutcome(protocol, normalizedHost, port, "method_not_allowed", "mode_guard"));
        }

        var policyDecision = execPolicyManager.EvaluateNetwork(protocol, normalizedHost);
        if (policyDecision == KernelExecPolicyRuleDecision.Deny)
        {
            return new KernelManagedNetworkAuthorizationResult(
                false,
                KernelManagedNetworkHelpers.CreatePolicyDeniedOutcome(protocol, normalizedHost, port, "denied"));
        }

        if (deniedHosts.ContainsKey(key))
        {
            return new KernelManagedNetworkAuthorizationResult(
                false,
                new KernelManagedNetworkOutcome(KernelManagedNetworkOutcomeKind.DeniedByUser, "rejected by user"));
        }

        if (approvedHosts.ContainsKey(key) || policyDecision == KernelExecPolicyRuleDecision.Allow)
        {
            return KernelManagedNetworkAuthorizationResult.Allow;
        }

        if (KernelManagedNetworkHelpers.IsLocalOrPrivateHost(normalizedHost) && !Settings.AllowLocalBinding)
        {
            return new KernelManagedNetworkAuthorizationResult(
                false,
                KernelManagedNetworkHelpers.CreatePolicyDeniedOutcome(protocol, normalizedHost, port, "not_allowed_local"));
        }

        if (Settings.DeniedDomains.Any(rule => KernelManagedNetworkHelpers.MatchesDomainRule(normalizedHost, rule)))
        {
            return new KernelManagedNetworkAuthorizationResult(
                false,
                KernelManagedNetworkHelpers.CreatePolicyDeniedOutcome(protocol, normalizedHost, port, "denied"));
        }

        if (Settings.AllowedDomains.Any(rule => KernelManagedNetworkHelpers.MatchesDomainRule(normalizedHost, rule))
            || KernelManagedNetworkHelpers.IsFullMode(Settings.Mode))
        {
            return KernelManagedNetworkAuthorizationResult.Allow;
        }

        if (!AllowsNetworkApprovalFlow(request.ApprovalPolicy))
        {
            return new KernelManagedNetworkAuthorizationResult(
                false,
                new KernelManagedNetworkOutcome(
                    KernelManagedNetworkOutcomeKind.DeniedByPolicy,
                    KernelManagedNetworkHelpers.BuildPolicyDeniedMessage(protocol, normalizedHost, port, "approval_policy_never")));
        }

        var createdTask = RequestApprovalCoreAsync(request, key, cancellationToken);
        var approvalTask = pendingApprovals.GetOrAdd(key, createdTask);
        try
        {
            return await approvalTask.ConfigureAwait(false);
        }
        finally
        {
            if (ReferenceEquals(approvalTask, createdTask))
            {
                pendingApprovals.TryRemove(key, out _);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        pendingApprovals.Clear();
        approvedHosts.Clear();
        deniedHosts.Clear();
        return ValueTask.CompletedTask;
    }

    private static bool AllowsNetworkApprovalFlow(KernelApprovalPolicy? approvalPolicy)
        => !KernelApprovalPolicyHelpers.IsNever(approvalPolicy);

    private Task EmitSideEffectAsync(
        KernelManagedNetworkExecutionRequest request,
        KernelManagedNetworkSideEffectKind kind,
        string? text,
        CancellationToken cancellationToken)
    {
        var normalizedText = KernelManagedNetworkHelpers.Normalize(text);
        if (sideEffectSink is null || string.IsNullOrWhiteSpace(normalizedText))
        {
            return Task.CompletedTask;
        }

        return sideEffectSink(
            request,
            new KernelManagedNetworkSideEffect(kind, normalizedText!),
            cancellationToken);
    }

    private static string BuildNetworkPolicyAmendmentMessage(KernelManagedNetworkPolicyAmendment amendment)
    {
        var (action, listName) = amendment.Action switch
        {
            KernelManagedNetworkRuleAction.Allow => ("Allowed", "allowlist"),
            _ => ("Denied", "denylist"),
        };

        return $"{action} network rule saved in execpolicy ({listName}): {amendment.Host}";
    }

    private static string BuildNetworkPolicyAmendmentHostMismatchMessage(string amendmentHost, string approvedHost)
        => $"network policy amendment host '{amendmentHost}' does not match approved host '{approvedHost}'";

    private async Task<KernelManagedNetworkPersistResult> TryPersistNetworkRuleAsync(
        KernelManagedNetworkHostKey key,
        KernelManagedNetworkPolicyAmendment amendment,
        CancellationToken cancellationToken)
    {
        var normalizedHost = KernelManagedNetworkHelpers.NormalizeHost(amendment.Host);
        if (string.IsNullOrWhiteSpace(normalizedHost)
            || !string.Equals(normalizedHost, key.Host, StringComparison.OrdinalIgnoreCase))
        {
            return new KernelManagedNetworkPersistResult(
                KernelManagedNetworkPersistResultKind.HostMismatch,
                BuildNetworkPolicyAmendmentHostMismatchMessage(amendment.Host, key.Host));
        }

        try
        {
            await execPolicyManager.AppendNetworkRuleAndUpdateAsync(key.Protocol, normalizedHost, amendment.Action, cancellationToken).ConfigureAwait(false);
            return KernelManagedNetworkPersistResult.Success;
        }
        catch (Exception ex)
        {
            var reason = KernelManagedNetworkHelpers.Normalize(ex.Message) ?? ex.GetType().Name;
            return new KernelManagedNetworkPersistResult(
                KernelManagedNetworkPersistResultKind.PersistFailed,
                $"failed to persist network policy amendment to execpolicy: {reason}");
        }
    }

    private async Task<KernelManagedNetworkAuthorizationResult> RequestApprovalCoreAsync(
        KernelManagedNetworkExecutionRequest request,
        KernelManagedNetworkHostKey key,
        CancellationToken cancellationToken)
    {
        var allowAmendment = new KernelManagedNetworkPolicyAmendment(key.Host, KernelManagedNetworkRuleAction.Allow);
        var denyAmendment = new KernelManagedNetworkPolicyAmendment(key.Host, KernelManagedNetworkRuleAction.Deny);
        var approvalRequest = new KernelManagedNetworkApprovalRequest(
            request.ThreadId,
            request.TurnId,
            request.ItemId,
            KernelManagedNetworkHelpers.BuildApprovalId(key),
            request.Command,
            request.Cwd,
            $"{key.Host} is not in the allowed_domains",
            new KernelManagedNetworkApprovalContext(key.Host, key.Protocol),
            new[] { allowAmendment, denyAmendment },
            new object?[]
            {
                "accept",
                "acceptForSession",
                new
                {
                    applyNetworkPolicyAmendment = new
                    {
                        network_policy_amendment = allowAmendment.ToPayload(),
                    },
                },
                new
                {
                    applyNetworkPolicyAmendment = new
                    {
                        network_policy_amendment = denyAmendment.ToPayload(),
                    },
                },
                "cancel",
            });

        KernelManagedNetworkApprovalResponse response;
        try
        {
            response = await approvalRequester(approvalRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new KernelManagedNetworkAuthorizationResult(
                false,
                new KernelManagedNetworkOutcome(
                    KernelManagedNetworkOutcomeKind.DeniedByPolicy,
                    KernelManagedNetworkHelpers.Normalize(ex.Message) ?? "approval request failed"));
        }

        var decision = KernelManagedNetworkHelpers.Normalize(response.Decision)?.ToLowerInvariant();
        switch (decision)
        {
            case "accept":
            case "acceptwithexecpolicyamendment":
                return KernelManagedNetworkAuthorizationResult.Allow;

            case "acceptforsession":
                approvedHosts[key] = 1;
                deniedHosts.TryRemove(key, out _);
                return KernelManagedNetworkAuthorizationResult.Allow;

            case "applynetworkpolicyamendment":
            {
                if (response.NetworkPolicyAmendment is not { } amendment)
                {
                    deniedHosts[key] = 1;
                    approvedHosts.TryRemove(key, out _);
                    return new KernelManagedNetworkAuthorizationResult(
                        false,
                        new KernelManagedNetworkOutcome(KernelManagedNetworkOutcomeKind.DeniedByUser, "rejected by user"));
                }

                var persistResult = await TryPersistNetworkRuleAsync(key, amendment, cancellationToken).ConfigureAwait(false);
                if (persistResult.Kind == KernelManagedNetworkPersistResultKind.Persisted)
                {
                    await EmitSideEffectAsync(
                        request,
                        KernelManagedNetworkSideEffectKind.DeveloperMessage,
                        BuildNetworkPolicyAmendmentMessage(amendment),
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await EmitSideEffectAsync(
                        request,
                        KernelManagedNetworkSideEffectKind.Warning,
                        $"Failed to apply network policy amendment: {persistResult.ErrorMessage}",
                        cancellationToken).ConfigureAwait(false);
                }

                if (amendment.Action == KernelManagedNetworkRuleAction.Allow)
                {
                    approvedHosts[key] = 1;
                    deniedHosts.TryRemove(key, out _);
                    return KernelManagedNetworkAuthorizationResult.Allow;
                }

                deniedHosts[key] = 1;
                approvedHosts.TryRemove(key, out _);
                return new KernelManagedNetworkAuthorizationResult(false, new KernelManagedNetworkOutcome(KernelManagedNetworkOutcomeKind.DeniedByUser, "rejected by user"));
            }

            case "decline":
            case "cancel":
            default:
                return new KernelManagedNetworkAuthorizationResult(false, new KernelManagedNetworkOutcome(KernelManagedNetworkOutcomeKind.DeniedByUser, "rejected by user"));
        }
    }
}

internal sealed class KernelManagedNetworkExecutionLease : IKernelManagedNetworkExecutionLease
{
    private KernelManagedNetworkProxyRuntime? runtime;
    private KernelManagedNetworkOutcome outcome = KernelManagedNetworkOutcome.None;
    private int disposed;
    private int outcomeMessageConsumed;

    private static readonly string[] AllProxyEnvironmentKeys = ["ALL_PROXY", "all_proxy"];
    private static readonly string[] FtpProxyEnvironmentKeys = ["FTP_PROXY", "ftp_proxy"];
    private static readonly string[] HttpProxyEnvironmentKeys =
    [
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "http_proxy",
        "https_proxy",
        "YARN_HTTP_PROXY",
        "YARN_HTTPS_PROXY",
        "npm_config_http_proxy",
        "npm_config_https_proxy",
        "npm_config_proxy",
        "NPM_CONFIG_HTTP_PROXY",
        "NPM_CONFIG_HTTPS_PROXY",
        "NPM_CONFIG_PROXY",
        "BUNDLE_HTTP_PROXY",
        "BUNDLE_HTTPS_PROXY",
        "PIP_PROXY",
        "DOCKER_HTTP_PROXY",
        "DOCKER_HTTPS_PROXY",
    ];
    private static readonly string[] NoProxyEnvironmentKeys =
    [
        "NO_PROXY",
        "no_proxy",
        "npm_config_noproxy",
        "NPM_CONFIG_NOPROXY",
        "YARN_NO_PROXY",
        "BUNDLE_NO_PROXY",
    ];
    private static readonly string[] WebSocketProxyEnvironmentKeys = ["WS_PROXY", "WSS_PROXY", "ws_proxy", "wss_proxy"];

    private const string AllowLocalBindingEnvironmentKey = "TIANSHU_NETWORK_ALLOW_LOCAL_BINDING";
    private const string DefaultNoProxyValue = "localhost,127.0.0.1,::1,*.local,.local,169.254.0.0/16,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16";

    private const int MaxBlockedRequests = 200;

    private readonly object blockedRequestsGate = new();
    private readonly Queue<KernelManagedNetworkBlockedRequest> blockedRequests = new();
    private long blockedRequestTotal;

    private KernelManagedNetworkExecutionLease()
    {
        Request = new KernelManagedNetworkExecutionRequest(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, null, null);
        Session = null;
        IsActive = false;
    }

    internal KernelManagedNetworkExecutionLease(KernelManagedNetworkSessionState session, KernelManagedNetworkExecutionRequest request)
    {
        Session = session;
        Request = request;
        IsActive = true;
    }

    private KernelManagedNetworkSessionState? Session { get; }

    internal KernelManagedNetworkExecutionRequest Request { get; }

    public bool IsActive { get; }

    public string? HttpProxyUrl => runtime?.HttpProxyUrl;

    public string? SocksProxyUrl => runtime?.SocksProxyUrl;

    public bool HasRejectedOutcome => outcome.Kind is KernelManagedNetworkOutcomeKind.DeniedByPolicy or KernelManagedNetworkOutcomeKind.DeniedByUser;

    public static KernelManagedNetworkExecutionLease Inactive() => new();

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsActive || Session is null)
        {
            return;
        }

        runtime = new KernelManagedNetworkProxyRuntime(Session, this);
        await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    internal void RecordOutcome(KernelManagedNetworkOutcome candidate)
    {
        if (candidate.Kind == KernelManagedNetworkOutcomeKind.None)
        {
            return;
        }

        if (outcome.Kind == KernelManagedNetworkOutcomeKind.DeniedByUser)
        {
            return;
        }

        outcome = candidate;
    }

    internal void RecordBlockedRequest(
        KernelManagedNetworkOutcome candidate,
        string protocol,
        string? client,
        string? method,
        string? mode = null)
    {
        if (candidate.BlockedHttpPayload is null)
        {
            return;
        }

        lock (blockedRequestsGate)
        {
            blockedRequests.Enqueue(new KernelManagedNetworkBlockedRequest(
                Host: candidate.BlockedHttpPayload.Host,
                Reason: candidate.BlockedHttpPayload.Reason,
                Client: client,
                Method: method,
                Mode: mode,
                Protocol: protocol,
                Decision: candidate.BlockedHttpPayload.Decision,
                Source: candidate.BlockedHttpPayload.Source,
                Port: candidate.BlockedHttpPayload.Port,
                Timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            blockedRequestTotal++;
            while (blockedRequests.Count > MaxBlockedRequests)
            {
                blockedRequests.Dequeue();
            }
        }
    }

    internal IReadOnlyList<KernelManagedNetworkBlockedRequest> GetBlockedRequestSnapshot()
    {
        lock (blockedRequestsGate)
        {
            return blockedRequests.ToArray();
        }
    }

    internal IReadOnlyList<KernelManagedNetworkBlockedRequest> DrainBlockedRequests()
    {
        lock (blockedRequestsGate)
        {
            var snapshot = blockedRequests.ToArray();
            blockedRequests.Clear();
            return snapshot;
        }
    }

    public long GetBlockedRequestTotal()
    {
        lock (blockedRequestsGate)
        {
            return blockedRequestTotal;
        }
    }

    public IReadOnlyDictionary<string, string> ApplyToEnvironment(IReadOnlyDictionary<string, string>? baseEnvironment)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var environment = baseEnvironment is null
            ? new Dictionary<string, string>(comparer)
            : new Dictionary<string, string>(baseEnvironment, comparer);

        if (!IsActive || string.IsNullOrWhiteSpace(HttpProxyUrl))
        {
            return environment;
        }

        var httpProxyUrl = HttpProxyUrl!;
        var allProxyUrl = !string.IsNullOrWhiteSpace(SocksProxyUrl)
            ? SocksProxyUrl!
            : httpProxyUrl;

        SetEnvironmentKeys(environment, HttpProxyEnvironmentKeys, httpProxyUrl);
        SetEnvironmentKeys(environment, WebSocketProxyEnvironmentKeys, httpProxyUrl);
        SetEnvironmentKeys(environment, NoProxyEnvironmentKeys, DefaultNoProxyValue);
        SetEnvironmentKeys(environment, AllProxyEnvironmentKeys, allProxyUrl);
        SetEnvironmentKeys(environment, FtpProxyEnvironmentKeys, allProxyUrl);

        environment[AllowLocalBindingEnvironmentKey] = Session?.Settings.AllowLocalBinding == true ? "1" : "0";
        environment["ELECTRON_GET_USE_PROXY"] = "true";

        if (OperatingSystem.IsMacOS()
            && !string.IsNullOrWhiteSpace(SocksProxyUrl)
            && !environment.ContainsKey("GIT_SSH_COMMAND"))
        {
            var socksAddress = new Uri(SocksProxyUrl!).Authority;
            environment["GIT_SSH_COMMAND"] = $"ssh -o ProxyCommand='nc -X 5 -x {socksAddress} %h %p'";
        }

        return environment;
    }

    private static void SetEnvironmentKeys(IDictionary<string, string> environment, IEnumerable<string> keys, string value)
    {
        foreach (var key in keys)
        {
            environment[key] = value;
        }
    }

    public KernelExecToolCallOutput ApplyOutcome(KernelExecToolCallOutput output)
    {
        if (!HasRejectedOutcome || string.IsNullOrWhiteSpace(outcome.Message))
        {
            return output;
        }

        var stderr = string.IsNullOrWhiteSpace(output.Stderr)
            ? outcome.Message!
            : output.Stderr + Environment.NewLine + outcome.Message;
        var aggregated = string.IsNullOrWhiteSpace(output.AggregatedOutput)
            ? stderr
            : output.AggregatedOutput + Environment.NewLine + outcome.Message;
        var exitCode = output.ExitCode == 0 ? -1 : output.ExitCode;
        return output with
        {
            ExitCode = exitCode,
            Stderr = stderr,
            AggregatedOutput = aggregated,
        };
    }

    public string ConsumeOutcomeMessage()
    {
        if (!HasRejectedOutcome || string.IsNullOrWhiteSpace(outcome.Message))
        {
            return string.Empty;
        }

        return Interlocked.Exchange(ref outcomeMessageConsumed, 1) == 0
            ? outcome.Message!
            : string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        if (runtime is not null)
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
            runtime = null;
        }
    }
}

internal sealed class KernelManagedNetworkProxyRuntime : IAsyncDisposable
{
    private const int MaxHeaderBytes = 64 * 1024;

    private readonly KernelManagedNetworkSessionState session;
    private readonly KernelManagedNetworkExecutionLease lease;
    private readonly CancellationTokenSource shutdown = new();
    private readonly List<Task> listeners = new();
    private TcpListener? httpListener;
    private TcpListener? socksListener;

    public KernelManagedNetworkProxyRuntime(KernelManagedNetworkSessionState session, KernelManagedNetworkExecutionLease lease)
    {
        this.session = session;
        this.lease = lease;
    }

    public string? HttpProxyUrl { get; private set; }

    public string? SocksProxyUrl { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
        var token = linkedCts.Token;
        var forceLoopback = session.Settings.DangerouslyAllowAllUnixSockets || session.Settings.AllowUnixSockets.Count > 0;

        var httpBindAddress = await ResolveBindAddressAsync(
            session.Settings.HttpHost,
            session.Settings.DangerouslyAllowNonLoopbackProxy,
            forceLoopback,
            cancellationToken).ConfigureAwait(false);

        httpListener = new TcpListener(httpBindAddress, session.Settings.HttpPort > 0 ? session.Settings.HttpPort : 0);
        httpListener.Start();
        HttpProxyUrl = BuildProxyUrl("http", (IPEndPoint)httpListener.LocalEndpoint);
        listeners.Add(AcceptLoopAsync(httpListener, HandleHttpClientAsync, token));

        if (session.Settings.EnableSocks5)
        {
            var socksBindAddress = await ResolveBindAddressAsync(
                session.Settings.SocksHost,
                session.Settings.DangerouslyAllowNonLoopbackProxy,
                forceLoopback,
                cancellationToken).ConfigureAwait(false);

            socksListener = new TcpListener(socksBindAddress, session.Settings.SocksPort > 0 ? session.Settings.SocksPort : 0);
            socksListener.Start();
            SocksProxyUrl = BuildProxyUrl("socks5h", (IPEndPoint)socksListener.LocalEndpoint);
            listeners.Add(AcceptLoopAsync(socksListener, HandleSocksClientAsync, token));
        }
    }

    private static string BuildProxyUrl(string scheme, IPEndPoint endpoint)
    {
        var host = endpoint.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{endpoint.Address}]"
            : endpoint.Address.ToString();

        return $"{scheme}://{host}:{endpoint.Port}";
    }

    private static async Task<IPAddress> ResolveBindAddressAsync(
        string host,
        bool allowNonLoopback,
        bool forceLoopback,
        CancellationToken cancellationToken)
    {
        var normalized = KernelManagedNetworkHelpers.Normalize(host) ?? IPAddress.Loopback.ToString();
        if (string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        if (IPAddress.TryParse(normalized, out var address))
        {
            if (forceLoopback && !IPAddress.IsLoopback(address))
            {
                return IPAddress.Loopback;
            }

            ValidateBindAddress(address, allowNonLoopback);
            return address;
        }

        var candidates = await Dns.GetHostAddressesAsync(normalized, cancellationToken).ConfigureAwait(false);
        var selected = candidates.FirstOrDefault();
        if (selected is null)
        {
            throw new InvalidOperationException($"managed network proxy host '{normalized}' could not be resolved");
        }

        if (forceLoopback && !IPAddress.IsLoopback(selected))
        {
            return IPAddress.Loopback;
        }

        ValidateBindAddress(selected, allowNonLoopback);
        return selected;
    }

    private static void ValidateBindAddress(IPAddress address, bool allowNonLoopback)
    {
        if (IPAddress.IsLoopback(address))
        {
            return;
        }

        if (!allowNonLoopback)
        {
            throw new InvalidOperationException(
                $"managed network proxy host '{address}' requires dangerously_allow_non_loopback_proxy = true");
        }
    }
    public async ValueTask DisposeAsync()
    {
        shutdown.Cancel();
        try
        {
            httpListener?.Stop();
            socksListener?.Stop();
        }
        catch
        {
        }

        var tasks = listeners.ToArray();
        if (tasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, Func<TcpClient, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => handler(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                client?.Dispose();
                break;
            }
            catch
            {
                client?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleHttpClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        using var network = client.GetStream();
        var (headerText, extraBytes) = await ReadHttpHeaderAsync(network, cancellationToken).ConfigureAwait(false);
        var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
        {
            return;
        }

        var requestLine = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 3)
        {
            return;
        }

        var method = requestLine[0];
        var target = requestLine[1];
        var version = requestLine[2];

        var limitedMode = !KernelManagedNetworkHelpers.IsFullMode(session.Settings.Mode);

        if (string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase))
        {
            var (host, port) = ParseConnectTarget(target);
            if (limitedMode)
            {
                var outcome = KernelManagedNetworkHelpers.CreatePolicyDeniedOutcome(
                    KernelManagedNetworkProtocol.Https,
                    host,
                    port,
                    "mitm_required",
                    "mode_guard");
                lease.RecordOutcome(outcome);
                lease.RecordBlockedRequest(outcome, "http-connect", TryGetClientAddress(client), method, mode: "limited");
                await WriteHttpDeniedAsync(network, outcome, connectResponse: true, cancellationToken).ConfigureAwait(false);
                return;
            }

            var authorization = await session.AuthorizeAsync(lease.Request, KernelManagedNetworkProtocol.Https, host, port, cancellationToken).ConfigureAwait(false);
            if (!authorization.Allowed)
            {
                lease.RecordOutcome(authorization.Outcome);
                lease.RecordBlockedRequest(authorization.Outcome, "http-connect", TryGetClientAddress(client), method, TryGetBlockedMode(authorization.Outcome));
                await WriteHttpDeniedAsync(network, authorization.Outcome, connectResponse: true, cancellationToken).ConfigureAwait(false);
                return;
            }
            TcpClient? connectUpstreamClient = null;
            NetworkStream? connectUpstreamStream = null;
            try
            {
                var connectUpstreamProxyUri = TryGetUpstreamProxyUri(connectRequest: true);
                connectUpstreamClient = connectUpstreamProxyUri is null
                    ? await ConnectAsync(host, port, cancellationToken).ConfigureAwait(false)
                    : await ConnectAsync(connectUpstreamProxyUri, cancellationToken).ConfigureAwait(false);
                connectUpstreamStream = connectUpstreamClient.GetStream();

                if (connectUpstreamProxyUri is not null)
                {
                    var connectHeader = BuildConnectRequestHeader(host, port, version, BuildProxyAuthorizationValue(connectUpstreamProxyUri));
                    await connectUpstreamStream.WriteAsync(Encoding.ASCII.GetBytes(connectHeader), cancellationToken).ConfigureAwait(false);

                    var (proxyResponseHeader, proxyExtraBytes) = await ReadHttpHeaderAsync(connectUpstreamStream, cancellationToken).ConfigureAwait(false);
                    if (!TryParseHttpStatusCode(proxyResponseHeader, out var statusCode) || statusCode < 200 || statusCode >= 300)
                    {
                        await network.WriteAsync(Encoding.ASCII.GetBytes(proxyResponseHeader), cancellationToken).ConfigureAwait(false);
                        if (proxyExtraBytes.Length > 0)
                        {
                            await network.WriteAsync(proxyExtraBytes, cancellationToken).ConfigureAwait(false);
                        }

                        return;
                    }

                    await network.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"), cancellationToken).ConfigureAwait(false);
                    if (proxyExtraBytes.Length > 0)
                    {
                        await network.WriteAsync(proxyExtraBytes, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    await network.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"), cancellationToken).ConfigureAwait(false);
                }

                if (extraBytes.Length > 0)
                {
                    await connectUpstreamStream.WriteAsync(extraBytes, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                if (connectUpstreamStream is not null)
                {
                    await connectUpstreamStream.DisposeAsync().ConfigureAwait(false);
                }

                connectUpstreamClient?.Dispose();
                await WriteHttpStatusAsync(network, 502, "Bad Gateway", "upstream failure", cancellationToken).ConfigureAwait(false);
                return;
            }

            using var ownedConnectUpstreamClient = connectUpstreamClient!;
            using var ownedConnectUpstreamStream = connectUpstreamStream!;
            await PumpBidirectionalAsync(network, ownedConnectUpstreamStream, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (await TryHandleUnixSocketRequestAsync(
                network,
                method,
                target,
                version,
                lines,
                extraBytes,
                limitedMode,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var parsed = ParseHttpRequestTarget(target, lines);
        if (limitedMode && !IsHttpMethodAllowedInLimitedMode(method))
        {
            var outcome = KernelManagedNetworkHelpers.CreatePolicyDeniedOutcome(
                KernelManagedNetworkProtocol.Http,
                parsed.Host,
                parsed.Port,
                "method_not_allowed",
                "mode_guard");
            lease.RecordOutcome(outcome);
            lease.RecordBlockedRequest(outcome, "http", TryGetClientAddress(client), method, mode: "limited");
            await WriteHttpDeniedAsync(network, outcome, connectResponse: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        var authorizationResult = await session.AuthorizeAsync(lease.Request, KernelManagedNetworkProtocol.Http, parsed.Host, parsed.Port, cancellationToken).ConfigureAwait(false);
        if (!authorizationResult.Allowed)
        {
            lease.RecordOutcome(authorizationResult.Outcome);
            lease.RecordBlockedRequest(authorizationResult.Outcome, "http", TryGetClientAddress(client), method, TryGetBlockedMode(authorizationResult.Outcome));
            await WriteHttpDeniedAsync(network, authorizationResult.Outcome, connectResponse: false, cancellationToken).ConfigureAwait(false);
            return;
        }
        TcpClient? upstreamClient = null;
        NetworkStream? upstream = null;
        try
        {
            var upstreamProxyUri = TryGetUpstreamProxyUri(connectRequest: false);
            upstreamClient = upstreamProxyUri is null
                ? await ConnectAsync(parsed.Host, parsed.Port, cancellationToken).ConfigureAwait(false)
                : await ConnectAsync(upstreamProxyUri, cancellationToken).ConfigureAwait(false);
            upstream = upstreamClient.GetStream();
            var outboundTarget = upstreamProxyUri is null
                ? parsed.PathAndQuery
                : BuildAbsoluteProxyRequestTarget(parsed.Host, parsed.Port, parsed.PathAndQuery);
            var outboundHeader = RebuildHttpRequestHeader(
                method,
                outboundTarget,
                version,
                lines,
                BuildProxyAuthorizationValue(upstreamProxyUri),
                stripUnixSocketHeader: false);
            await upstream.WriteAsync(Encoding.ASCII.GetBytes(outboundHeader), cancellationToken).ConfigureAwait(false);
            if (extraBytes.Length > 0)
            {
                await upstream.WriteAsync(extraBytes, cancellationToken).ConfigureAwait(false);
            }

            upstreamClient.Client.Shutdown(SocketShutdown.Send);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            if (upstream is not null)
            {
                await upstream.DisposeAsync().ConfigureAwait(false);
            }

            upstreamClient?.Dispose();
            await WriteHttpStatusAsync(network, 502, "Bad Gateway", "upstream failure", cancellationToken).ConfigureAwait(false);
            return;
        }

        using var ownedUpstreamClient = upstreamClient!;
        using var ownedUpstream = upstream!;
        await ownedUpstream.CopyToAsync(network, 81920, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryHandleUnixSocketRequestAsync(
        Stream network,
        string method,
        string target,
        string version,
        string[] lines,
        byte[] extraBytes,
        bool limitedMode,
        CancellationToken cancellationToken)
    {
        var unixSocketPath = TryGetHeaderValue(lines, "x-unix-socket");
        if (string.IsNullOrWhiteSpace(unixSocketPath))
        {
            return false;
        }

        if (limitedMode && !IsHttpMethodAllowedInLimitedMode(method))
        {
            const string message = "Unix socket access was blocked by method policy.";
            var outcome = new KernelManagedNetworkOutcome(
                KernelManagedNetworkOutcomeKind.DeniedByPolicy,
                message,
                KernelManagedNetworkHelpers.BuildUnixSocketBlockedHttpPayload("method_not_allowed"));
            lease.RecordOutcome(outcome);
            await WriteHttpDeniedAsync(network, outcome, connectResponse: false, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (!IsUnixSocketProxySupported())
        {
            await WriteHttpStatusAsync(network, 501, "Not Implemented", "unix sockets unsupported", cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (!IsUnixSocketAllowed(unixSocketPath))
        {
            const string message = "Unix socket access was blocked by policy.";
            var outcome = new KernelManagedNetworkOutcome(
                KernelManagedNetworkOutcomeKind.DeniedByPolicy,
                message,
                KernelManagedNetworkHelpers.BuildUnixSocketBlockedHttpPayload("not_allowed"));
            lease.RecordOutcome(outcome);
            await WriteHttpDeniedAsync(network, outcome, connectResponse: false, cancellationToken).ConfigureAwait(false);
            return true;
        }

        try
        {
            using var upstreamSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await upstreamSocket.ConnectAsync(new UnixDomainSocketEndPoint(unixSocketPath), cancellationToken).ConfigureAwait(false);
            using var upstream = new NetworkStream(upstreamSocket, ownsSocket: true);
            var outboundHeader = RebuildHttpRequestHeader(
                method,
                ExtractHttpPathAndQuery(target),
                version,
                lines,
                proxyAuthorizationValue: null,
                stripUnixSocketHeader: true);
            await upstream.WriteAsync(Encoding.ASCII.GetBytes(outboundHeader), cancellationToken).ConfigureAwait(false);
            if (extraBytes.Length > 0)
            {
                await upstream.WriteAsync(extraBytes, cancellationToken).ConfigureAwait(false);
            }

            upstreamSocket.Shutdown(SocketShutdown.Send);
            await upstream.CopyToAsync(network, 81920, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await WriteHttpStatusAsync(network, 502, "Bad Gateway", "unix socket proxy failed", cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private bool IsUnixSocketAllowed(string socketPath)
    {
        if (session.Settings.DangerouslyAllowAllUnixSockets)
        {
            return true;
        }

        return session.Settings.AllowUnixSockets.Any(allowed =>
            string.Equals(
                KernelManagedNetworkHelpers.Normalize(allowed),
                KernelManagedNetworkHelpers.Normalize(socketPath),
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnixSocketProxySupported()
        => OperatingSystem.IsMacOS();

    private static bool IsHttpMethodAllowedInLimitedMode(string method)
        => string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase);

    private async Task HandleSocksClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        using var network = client.GetStream();
        var greeting = await ReadExactAsync(network, 2, cancellationToken).ConfigureAwait(false);
        if (greeting[0] != 0x05)
        {
            return;
        }

        var methods = await ReadExactAsync(network, greeting[1], cancellationToken).ConfigureAwait(false);
        if (!methods.Contains((byte)0x00))
        {
            await network.WriteAsync(new byte[] { 0x05, 0xFF }, cancellationToken).ConfigureAwait(false);
            return;
        }

        await network.WriteAsync(new byte[] { 0x05, 0x00 }, cancellationToken).ConfigureAwait(false);

        var requestHeader = await ReadExactAsync(network, 4, cancellationToken).ConfigureAwait(false);
        if (requestHeader[0] != 0x05)
        {
            return;
        }

        var command = requestHeader[1];
        var atyp = requestHeader[3];
        var host = await ReadSocksHostAsync(network, atyp, cancellationToken).ConfigureAwait(false);
        var portBytes = await ReadExactAsync(network, 2, cancellationToken).ConfigureAwait(false);
        var port = (portBytes[0] << 8) | portBytes[1];

        if (command == 0x03)
        {
            if (!session.Settings.EnableSocks5Udp)
            {
                await WriteSocksReplyAsync(network, 0x07, cancellationToken).ConfigureAwait(false);
                return;
            }

            using var relay = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            await WriteSocksReplyAsync(network, 0x00, (IPEndPoint)relay.Client.LocalEndPoint!, cancellationToken).ConfigureAwait(false);
            using var relayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var relayTask = RelaySocksUdpAsync(relay, relayCts.Token);
            try
            {
                var probe = new byte[1];
                while (await network.ReadAsync(probe.AsMemory(0, probe.Length), relayCts.Token).ConfigureAwait(false) > 0)
                {
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                relayCts.Cancel();
                try
                {
                    await relayTask.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            return;
        }

        if (command != 0x01)
        {
            await WriteSocksReplyAsync(network, 0x07, cancellationToken).ConfigureAwait(false);
            return;
        }

        var authorization = await session.AuthorizeAsync(lease.Request, KernelManagedNetworkProtocol.Socks5Tcp, host, port, cancellationToken).ConfigureAwait(false);
        if (!authorization.Allowed)
        {
            lease.RecordOutcome(authorization.Outcome);
            lease.RecordBlockedRequest(authorization.Outcome, "socks5", TryGetClientAddress(client), method: null, TryGetBlockedMode(authorization.Outcome));
            await WriteSocksReplyAsync(network, 0x02, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var upstream = await ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        await WriteSocksReplyAsync(network, 0x00, cancellationToken).ConfigureAwait(false);
        await PumpBidirectionalAsync(network, upstream.GetStream(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(string HeaderText, byte[] ExtraBytes)> ReadHttpHeaderAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var scratch = new byte[4096];
        while (buffer.Length < MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(scratch.AsMemory(0, scratch.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            await buffer.WriteAsync(scratch.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            var marker = SearchHeaderTerminator(bytes);
            if (marker >= 0)
            {
                var headerLength = marker + 4;
                return (Encoding.ASCII.GetString(bytes, 0, headerLength), bytes[headerLength..]);
            }
        }

        throw new IOException("HTTP proxy request header exceeds limit.");
    }

    private static int SearchHeaderTerminator(byte[] bytes)
    {
        for (var i = 0; i <= bytes.Length - 4; i++)
        {
            if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10)
            {
                return i;
            }
        }

        return -1;
    }

    private static (string Host, int Port) ParseConnectTarget(string target)
    {
        var separator = target.LastIndexOf(':');
        if (separator <= 0 || separator >= target.Length - 1)
        {
            return (target, 443);
        }

        var host = target[..separator];
        var port = int.TryParse(target[(separator + 1)..], out var parsed) ? parsed : 443;
        return (KernelManagedNetworkHelpers.NormalizeHost(host), port);
    }

    private static (string Host, int Port, string PathAndQuery) ParseHttpRequestTarget(string target, string[] headerLines)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var absoluteUri))
        {
            var defaultPort = absoluteUri.IsDefaultPort ? 80 : absoluteUri.Port;
            return (KernelManagedNetworkHelpers.NormalizeHost(absoluteUri.Host), defaultPort, absoluteUri.PathAndQuery);
        }

        var hostHeader = headerLines.FirstOrDefault(static line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
        var hostValue = hostHeader is null ? string.Empty : hostHeader[5..].Trim();
        var (host, port) = ParseConnectTarget(hostValue);
        return (host, port == 443 ? 80 : port, string.IsNullOrWhiteSpace(target) ? "/" : target);
    }

    private static string ExtractHttpPathAndQuery(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var absoluteUri))
        {
            return string.IsNullOrWhiteSpace(absoluteUri.PathAndQuery) ? "/" : absoluteUri.PathAndQuery;
        }

        return string.IsNullOrWhiteSpace(target) ? "/" : target;
    }

    private static string BuildAbsoluteProxyRequestTarget(string host, int port, string pathAndQuery)
    {
        var authority = port == 80
            ? BuildAuthority(host)
            : BuildAuthority(host, port);
        return $"http://{authority}{(string.IsNullOrWhiteSpace(pathAndQuery) ? "/" : pathAndQuery)}";
    }

    private static string BuildConnectRequestHeader(string host, int port, string version, string? proxyAuthorizationValue)
    {
        var authority = BuildAuthority(host, port);
        var builder = new StringBuilder();
        builder.Append("CONNECT ").Append(authority).Append(' ').Append(string.IsNullOrWhiteSpace(version) ? "HTTP/1.1" : version).Append("\r\n");
        builder.Append("Host: ").Append(authority).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(proxyAuthorizationValue))
        {
            builder.Append("Proxy-Authorization: ").Append(proxyAuthorizationValue).Append("\r\n");
        }

        builder.Append("\r\n");
        return builder.ToString();
    }

    private static string BuildAuthority(string host, int? port = null)
    {
        var formattedHost = host.Contains(':') && !host.StartsWith("[", StringComparison.Ordinal)
            ? $"[{host}]"
            : host;
        return port is > 0 ? $"{formattedHost}:{port.Value}" : formattedHost;
    }

    private static bool TryParseHttpStatusCode(string headerText, out int statusCode)
    {
        statusCode = 0;
        var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        if (lines.Length == 0)
        {
            return false;
        }

        var parts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && int.TryParse(parts[1], out statusCode);
    }

    private Uri? TryGetUpstreamProxyUri(bool connectRequest)
    {
        if (!session.Settings.AllowUpstreamProxy)
        {
            return null;
        }

        return connectRequest
            ? TryGetEnvironmentProxyUri("HTTPS_PROXY", "https_proxy", "HTTP_PROXY", "http_proxy", "ALL_PROXY", "all_proxy")
            : TryGetEnvironmentProxyUri("HTTP_PROXY", "http_proxy", "ALL_PROXY", "all_proxy");
    }

    private static Uri? TryGetEnvironmentProxyUri(params string[] environmentKeys)
    {
        foreach (var key in environmentKeys)
        {
            var value = KernelManagedNetworkHelpers.Normalize(Environment.GetEnvironmentVariable(key));
            if (string.IsNullOrWhiteSpace(value)
                || !Uri.TryCreate(value, UriKind.Absolute, out var proxyUri)
                || !string.Equals(proxyUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(proxyUri.Host))
            {
                continue;
            }

            return proxyUri;
        }

        return null;
    }

    private static string? BuildProxyAuthorizationValue(Uri? proxyUri)
    {
        if (proxyUri is null || string.IsNullOrWhiteSpace(proxyUri.UserInfo))
        {
            return null;
        }

        var credentials = Uri.UnescapeDataString(proxyUri.UserInfo);
        return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
    }

    private static string? TryGetHeaderValue(string[] lines, string headerName)
    {
        foreach (var line in lines.Skip(1))
        {
            if (!TryParseHeaderLine(line, out var name, out var value))
            {
                continue;
            }

            if (string.Equals(name, headerName, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static string RebuildHttpRequestHeader(
        string method,
        string requestTarget,
        string version,
        string[] lines,
        string? proxyAuthorizationValue = null,
        bool stripUnixSocketHeader = false)
    {
        var builder = new StringBuilder();
        var connectionTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (!TryParseHeaderLine(line, out var name, out var value)
                || !string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                connectionTokens.Add(token);
            }
        }

        builder.Append(method).Append(' ').Append(string.IsNullOrWhiteSpace(requestTarget) ? "/" : requestTarget).Append(' ').Append(version).Append("\r\n");
        foreach (var line in lines.Skip(1))
        {
            if (!TryParseHeaderLine(line, out var name, out var value))
            {
                continue;
            }

            if (string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Keep-Alive", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Proxy-Connection", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Trailer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Upgrade", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "TE", StringComparison.OrdinalIgnoreCase)
                || (stripUnixSocketHeader && string.Equals(name, "x-unix-socket", StringComparison.OrdinalIgnoreCase))
                || connectionTokens.Contains(name))
            {
                continue;
            }

            builder.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        if (!string.IsNullOrWhiteSpace(proxyAuthorizationValue))
        {
            builder.Append("Proxy-Authorization: ").Append(proxyAuthorizationValue).Append("\r\n");
        }

        builder.Append("\r\n");
        return builder.ToString();
    }

    private static bool TryParseHeaderLine(string line, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var separator = line.IndexOf(':');
        if (separator <= 0)
        {
            return false;
        }

        name = line[..separator].Trim();
        value = separator >= line.Length - 1 ? string.Empty : line[(separator + 1)..].Trim();
        return name.Length > 0;
    }

    private static string? TryGetClientAddress(TcpClient client)
        => TryGetClientAddress(client.Client.RemoteEndPoint);

    private static string? TryGetClientAddress(EndPoint? endpoint)
        => endpoint?.ToString();

    private static string? TryGetBlockedMode(KernelManagedNetworkOutcome outcome)
        => string.Equals(outcome.BlockedHttpPayload?.Source, "mode_guard", StringComparison.OrdinalIgnoreCase)
            ? "limited"
            : null;


    private static async Task<TcpClient> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        return client;
    }

    private static Task<TcpClient> ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var port = endpoint.Port > 0 ? endpoint.Port : 80;
        return ConnectAsync(endpoint.Host, port, cancellationToken);
    }
    private static async Task PumpBidirectionalAsync(Stream left, Stream right, CancellationToken cancellationToken)
    {
        var first = PumpAsync(left, right, cancellationToken);
        var second = PumpAsync(right, left, cancellationToken);
        await Task.WhenAny(first, second).ConfigureAwait(false);
    }

    private static async Task PumpAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        try
        {
            await source.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            try
            {
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static async Task WriteHttpDeniedAsync(
        Stream stream,
        KernelManagedNetworkOutcome outcome,
        bool connectResponse,
        CancellationToken cancellationToken)
    {
        if (outcome.BlockedHttpPayload is not null)
        {
            if (connectResponse)
            {
                await WriteHttpBlockedTextAsync(stream, outcome.BlockedHttpPayload.Reason, cancellationToken).ConfigureAwait(false);
                return;
            }

            await WriteHttpBlockedJsonAsync(stream, outcome.BlockedHttpPayload, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteHttpStatusAsync(stream, 403, "Forbidden", outcome.Message ?? "blocked by policy", cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteHttpBlockedTextAsync(Stream stream, string reason, CancellationToken cancellationToken)
    {
        var message = KernelManagedNetworkHelpers.BuildBlockedResponseMessage(reason);
        var body = Encoding.UTF8.GetBytes(message);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 403 Forbidden\r\nContent-Type: text/plain\r\nx-proxy-error: {KernelManagedNetworkHelpers.BuildBlockedHeaderValue(reason)}\r\nContent-Length: {body.Length}\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteHttpBlockedJsonAsync(
        Stream stream,
        KernelManagedNetworkBlockedHttpPayload payload,
        CancellationToken cancellationToken)
    {
        var bodyText = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var body = Encoding.UTF8.GetBytes(bodyText);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 403 Forbidden\r\nContent-Type: application/json\r\nx-proxy-error: {KernelManagedNetworkHelpers.BuildBlockedHeaderValue(payload.Reason)}\r\nContent-Length: {body.Length}\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteHttpStatusAsync(
        Stream stream,
        int statusCode,
        string reasonPhrase,
        string message,
        CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(message);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {statusCode} {reasonPhrase}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                throw new IOException("Unexpected end of stream.");
            }

            offset += read;
        }

        return buffer;
    }

    private async Task RelaySocksUdpAsync(UdpClient relay, CancellationToken cancellationToken)
    {
        IPEndPoint? clientEndpoint = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await relay.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            if (clientEndpoint is null || result.RemoteEndPoint.Equals(clientEndpoint))
            {
                clientEndpoint ??= result.RemoteEndPoint;
                if (!TryParseSocksUdpPacket(result.Buffer, out var host, out var port, out var payload))
                {
                    continue;
                }

                var authorization = await session.AuthorizeAsync(lease.Request, KernelManagedNetworkProtocol.Socks5Udp, host, port, cancellationToken).ConfigureAwait(false);
                if (!authorization.Allowed)
                {
                    lease.RecordOutcome(authorization.Outcome);
                    lease.RecordBlockedRequest(authorization.Outcome, "socks5-udp", TryGetClientAddress(result.RemoteEndPoint), method: null, TryGetBlockedMode(authorization.Outcome));
                    continue;
                }

                var targetEndPoint = await ResolveUdpEndpointAsync(host, port, cancellationToken).ConfigureAwait(false);
                await relay.SendAsync(payload, targetEndPoint, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (clientEndpoint is null)
            {
                continue;
            }

            var packet = BuildSocksUdpPacket(result.RemoteEndPoint, result.Buffer);
            await relay.SendAsync(packet, clientEndpoint, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IPEndPoint> ResolveUdpEndpointAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return new IPEndPoint(address, port);
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        var selected = addresses.FirstOrDefault();
        if (selected is null)
        {
            throw new IOException($"Unable to resolve UDP host '{host}'.");
        }

        return new IPEndPoint(selected, port);
    }

    private static bool TryParseSocksUdpPacket(byte[] buffer, out string host, out int port, out byte[] payload)
    {
        host = string.Empty;
        port = 0;
        payload = Array.Empty<byte>();
        if (buffer.Length < 10 || buffer[2] != 0x00)
        {
            return false;
        }

        var index = 3;
        var atyp = buffer[index++];
        switch (atyp)
        {
            case 0x01:
                if (buffer.Length < index + 4 + 2)
                {
                    return false;
                }

                host = new IPAddress(buffer.AsSpan(index, 4)).ToString();
                index += 4;
                break;

            case 0x03:
                if (buffer.Length < index + 1)
                {
                    return false;
                }

                var length = buffer[index++];
                if (buffer.Length < index + length + 2)
                {
                    return false;
                }

                host = Encoding.ASCII.GetString(buffer, index, length);
                index += length;
                break;

            case 0x04:
                if (buffer.Length < index + 16 + 2)
                {
                    return false;
                }

                host = new IPAddress(buffer.AsSpan(index, 16)).ToString();
                index += 16;
                break;

            default:
                return false;
        }

        port = (buffer[index] << 8) | buffer[index + 1];
        index += 2;
        if (buffer.Length < index)
        {
            return false;
        }

        payload = buffer[index..];
        return !string.IsNullOrWhiteSpace(host);
    }

    private static byte[] BuildSocksUdpPacket(IPEndPoint endpoint, byte[] payload)
    {
        var addressBytes = endpoint.Address.GetAddressBytes();
        var atyp = endpoint.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)0x04 : (byte)0x01;
        var packet = new byte[2 + 1 + 1 + addressBytes.Length + 2 + payload.Length];
        packet[2] = 0x00;
        packet[3] = atyp;
        Buffer.BlockCopy(addressBytes, 0, packet, 4, addressBytes.Length);
        var portOffset = 4 + addressBytes.Length;
        packet[portOffset] = (byte)((endpoint.Port >> 8) & 0xFF);
        packet[portOffset + 1] = (byte)(endpoint.Port & 0xFF);
        Buffer.BlockCopy(payload, 0, packet, portOffset + 2, payload.Length);
        return packet;
    }

    private static async Task<string> ReadSocksHostAsync(Stream stream, byte atyp, CancellationToken cancellationToken)
    {
        return atyp switch
        {
            0x01 => new IPAddress(await ReadExactAsync(stream, 4, cancellationToken).ConfigureAwait(false)).ToString(),
            0x03 => Encoding.ASCII.GetString(await ReadExactAsync(stream, (await ReadExactAsync(stream, 1, cancellationToken).ConfigureAwait(false))[0], cancellationToken).ConfigureAwait(false)),
            0x04 => new IPAddress(await ReadExactAsync(stream, 16, cancellationToken).ConfigureAwait(false)).ToString(),
            _ => throw new IOException("Unsupported SOCKS address type."),
        };
    }

    private static async Task WriteSocksReplyAsync(Stream stream, byte code, CancellationToken cancellationToken)
    {
        await WriteSocksReplyAsync(stream, code, new IPEndPoint(IPAddress.Any, 0), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteSocksReplyAsync(Stream stream, byte code, IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        var addressBytes = endpoint.Address.GetAddressBytes();
        var atyp = endpoint.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)0x04 : (byte)0x01;
        var reply = new byte[4 + addressBytes.Length + 2];
        reply[0] = 0x05;
        reply[1] = code;
        reply[2] = 0x00;
        reply[3] = atyp;
        Buffer.BlockCopy(addressBytes, 0, reply, 4, addressBytes.Length);
        var portOffset = 4 + addressBytes.Length;
        reply[portOffset] = (byte)((endpoint.Port >> 8) & 0xFF);
        reply[portOffset + 1] = (byte)(endpoint.Port & 0xFF);
        await stream.WriteAsync(reply, cancellationToken).ConfigureAwait(false);
    }
}






















