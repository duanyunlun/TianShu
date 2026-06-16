using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

[Collection("EnvironmentVariables")]
public sealed class KernelManagedNetworkTests
{
    [Fact]
    public async Task AuthorizeAsync_ShouldCacheAcceptForSessionPerProtocolHostPort()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var approvalCount = 0;
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(new KernelManagedNetworkApprovalResponse("acceptForSession"));
            });

        try
        {
            var request = CreateRequest(scope.Root);
            var first = await session.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);
            var second = await session.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);
            var third = await session.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 8443, CancellationToken.None);

            Assert.True(first.Allowed);
            Assert.True(second.Allowed);
            Assert.True(third.Allowed);
            Assert.Equal(2, approvalCount);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldDenySocksRequestsInLimitedModeWithoutPrompt()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var approvalCount = 0;
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(new KernelManagedNetworkApprovalResponse("accept"));
            });

        try
        {
            var result = await session.AuthorizeAsync(
                CreateRequest(scope.Root),
                KernelManagedNetworkProtocol.Socks5Tcp,
                "example.com",
                1080,
                CancellationToken.None);

            Assert.False(result.Allowed);
            Assert.Equal(KernelManagedNetworkOutcomeKind.DeniedByPolicy, result.Outcome.Kind);
            Assert.Contains("method policy", result.Outcome.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, approvalCount);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldPersistDenyAmendmentAcrossSessions()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var approvalCount = 0;
        var firstSession = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(
                    new KernelManagedNetworkApprovalResponse(
                        "applyNetworkPolicyAmendment",
                        new KernelManagedNetworkPolicyAmendment("example.com", KernelManagedNetworkRuleAction.Deny)));
            });

        try
        {
            var request = CreateRequest(scope.Root);
            var denied = await firstSession.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);

            Assert.False(denied.Allowed);
            Assert.Equal(KernelManagedNetworkOutcomeKind.DeniedByUser, denied.Outcome.Kind);
            Assert.Equal(KernelExecPolicyRuleDecision.Deny, execPolicyManager.EvaluateNetwork(KernelManagedNetworkProtocol.Https, "example.com"));
            Assert.Contains("network_rule(host=\"example.com\", protocol=\"https\", decision=\"deny\", justification=\"Deny https_connect access to example.com\")", File.ReadAllText(execPolicyManager.PolicyFilePath), StringComparison.Ordinal);
        }
        finally
        {
            await firstSession.DisposeAsync();
        }

        var secondSession = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(new KernelManagedNetworkApprovalResponse("accept"));
            });

        try
        {
            var request = CreateRequest(scope.Root);
            var deniedByPolicy = await secondSession.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 8443, CancellationToken.None);

            Assert.False(deniedByPolicy.Allowed);
            Assert.Equal(KernelManagedNetworkOutcomeKind.DeniedByPolicy, deniedByPolicy.Outcome.Kind);
            Assert.Equal(1, approvalCount);
        }
        finally
        {
            await secondSession.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldEmitDeveloperMessageSideEffectWhenPersistentAllowSucceeds()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var effects = new List<KernelManagedNetworkSideEffect>();
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) => Task.FromResult(
                new KernelManagedNetworkApprovalResponse(
                    "applyNetworkPolicyAmendment",
                    new KernelManagedNetworkPolicyAmendment("example.com", KernelManagedNetworkRuleAction.Allow))),
            (_, effect, _) =>
            {
                effects.Add(effect);
                return Task.CompletedTask;
            });

        try
        {
            var result = await session.AuthorizeAsync(CreateRequest(scope.Root), KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);

            Assert.True(result.Allowed);
            var effect = Assert.Single(effects);
            Assert.Equal(KernelManagedNetworkSideEffectKind.DeveloperMessage, effect.Kind);
            Assert.Equal("Allowed network rule saved in execpolicy (allowlist): example.com", effect.Text);
            Assert.Equal(KernelExecPolicyRuleDecision.Allow, execPolicyManager.EvaluateNetwork(KernelManagedNetworkProtocol.Https, "example.com"));
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldEmitWarningSideEffectWhenPersistentAmendmentHostMismatches()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var approvalCount = 0;
        var effects = new List<KernelManagedNetworkSideEffect>();
        var firstSession = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(
                    new KernelManagedNetworkApprovalResponse(
                        "applyNetworkPolicyAmendment",
                        new KernelManagedNetworkPolicyAmendment("other.example.com", KernelManagedNetworkRuleAction.Allow)));
            },
            (_, effect, _) =>
            {
                effects.Add(effect);
                return Task.CompletedTask;
            });

        try
        {
            var request = CreateRequest(scope.Root);
            var first = await firstSession.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);
            var second = await firstSession.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);

            Assert.True(first.Allowed);
            Assert.True(second.Allowed);
            Assert.Equal(1, approvalCount);
            var effect = Assert.Single(effects);
            Assert.Equal(KernelManagedNetworkSideEffectKind.Warning, effect.Kind);
            Assert.Equal("Failed to apply network policy amendment: network policy amendment host 'other.example.com' does not match approved host 'example.com'", effect.Text);
            Assert.Null(execPolicyManager.EvaluateNetwork(KernelManagedNetworkProtocol.Https, "example.com"));
        }
        finally
        {
            await firstSession.DisposeAsync();
        }

        var secondSession = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(new KernelManagedNetworkApprovalResponse("accept"));
            });

        try
        {
            var result = await secondSession.AuthorizeAsync(CreateRequest(scope.Root), KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);

            Assert.True(result.Allowed);
            Assert.Equal(2, approvalCount);
        }
        finally
        {
            await secondSession.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldEmitWarningSideEffectWhenPersistentAmendmentPersistenceFails()
    {
        using var scope = new TestDirectoryScope();
        var invalidRoot = Path.Combine(scope.Root, "root-file");
        File.WriteAllText(invalidRoot, "x");
        var execPolicyManager = new KernelExecPolicyManager(invalidRoot);
        var approvalCount = 0;
        var effects = new List<KernelManagedNetworkSideEffect>();
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(
                    new KernelManagedNetworkApprovalResponse(
                        "applyNetworkPolicyAmendment",
                        new KernelManagedNetworkPolicyAmendment("example.com", KernelManagedNetworkRuleAction.Deny)));
            },
            (_, effect, _) =>
            {
                effects.Add(effect);
                return Task.CompletedTask;
            });

        try
        {
            var request = CreateRequest(scope.Root);
            var first = await session.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);
            var second = await session.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);

            Assert.False(first.Allowed);
            Assert.False(second.Allowed);
            Assert.Equal(1, approvalCount);
            var effect = Assert.Single(effects);
            Assert.Equal(KernelManagedNetworkSideEffectKind.Warning, effect.Kind);
            Assert.StartsWith(
                "Failed to apply network policy amendment: failed to persist network policy amendment to execpolicy:",
                effect.Text,
                StringComparison.Ordinal);
            Assert.Null(execPolicyManager.EvaluateNetwork(KernelManagedNetworkProtocol.Https, "example.com"));
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
    [Fact]
    public async Task AuthorizeAsync_ShouldTreatMismatchedPersistentAmendmentAsSessionOnlyAllow()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var approvalCount = 0;
        var firstSession = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(
                    new KernelManagedNetworkApprovalResponse(
                        "applyNetworkPolicyAmendment",
                        new KernelManagedNetworkPolicyAmendment("other.example.com", KernelManagedNetworkRuleAction.Allow)));
            });

        try
        {
            var request = CreateRequest(scope.Root);
            var first = await firstSession.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);
            var second = await firstSession.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);

            Assert.True(first.Allowed);
            Assert.True(second.Allowed);
            Assert.Equal(1, approvalCount);
            Assert.Null(execPolicyManager.EvaluateNetwork(KernelManagedNetworkProtocol.Https, "example.com"));
            Assert.Null(execPolicyManager.EvaluateNetwork(KernelManagedNetworkProtocol.Https, "other.example.com"));
        }
        finally
        {
            await firstSession.DisposeAsync();
        }

        var secondSession = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(new KernelManagedNetworkApprovalResponse("accept"));
            });

        try
        {
            var request = CreateRequest(scope.Root);
            var result = await secondSession.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);

            Assert.True(result.Allowed);
            Assert.Equal(2, approvalCount);
        }
        finally
        {
            await secondSession.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldTreatPersistenceFailureAsSessionOnlyDeny()
    {
        using var scope = new TestDirectoryScope();
        var invalidRoot = Path.Combine(scope.Root, "root-file");
        File.WriteAllText(invalidRoot, "x");
        var execPolicyManager = new KernelExecPolicyManager(invalidRoot);
        var approvalCount = 0;
        var firstSession = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(
                    new KernelManagedNetworkApprovalResponse(
                        "applyNetworkPolicyAmendment",
                        new KernelManagedNetworkPolicyAmendment("example.com", KernelManagedNetworkRuleAction.Deny)));
            });

        try
        {
            var request = CreateRequest(scope.Root);
            var first = await firstSession.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);
            var second = await firstSession.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);

            Assert.False(first.Allowed);
            Assert.False(second.Allowed);
            Assert.Equal(KernelManagedNetworkOutcomeKind.DeniedByUser, first.Outcome.Kind);
            Assert.Equal(1, approvalCount);
            Assert.Null(execPolicyManager.EvaluateNetwork(KernelManagedNetworkProtocol.Https, "example.com"));
        }
        finally
        {
            await firstSession.DisposeAsync();
        }

        var secondSession = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(new KernelManagedNetworkApprovalResponse("accept"));
            });

        try
        {
            var request = CreateRequest(scope.Root);
            var result = await secondSession.AuthorizeAsync(request, KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);

            Assert.True(result.Allowed);
            Assert.Equal(2, approvalCount);
        }
        finally
        {
            await secondSession.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldRequestApprovalWithCurrentPayload()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        KernelManagedNetworkApprovalRequest? captured = null;
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (request, _) =>
            {
                captured = request;
                return Task.FromResult(new KernelManagedNetworkApprovalResponse("cancel"));
            });

        try
        {
            var result = await session.AuthorizeAsync(CreateRequest(scope.Root), KernelManagedNetworkProtocol.Https, "example.com", 443, CancellationToken.None);

            Assert.False(result.Allowed);
            Assert.NotNull(captured);
            Assert.Equal(
                KernelManagedNetworkHelpers.BuildApprovalId(new KernelManagedNetworkHostKey("example.com", KernelManagedNetworkProtocol.Https, 443)),
                captured!.ApprovalId);
            Assert.Equal("example.com", captured.NetworkApprovalContext.Host);
            Assert.Equal(KernelManagedNetworkProtocol.Https, captured.NetworkApprovalContext.Protocol);
            Assert.Equal(2, captured.ProposedNetworkPolicyAmendments.Count);

            using var decisions = JsonDocument.Parse(JsonSerializer.Serialize(captured.AvailableDecisions));
            Assert.Equal("accept", decisions.RootElement[0].GetString());
            Assert.Equal("acceptForSession", decisions.RootElement[1].GetString());
            var allowNetworkPolicyAmendment = decisions.RootElement[2]
                .GetProperty("applyNetworkPolicyAmendment")
                .GetProperty("network_policy_amendment");
            Assert.Equal("example.com", allowNetworkPolicyAmendment.GetProperty("host").GetString());
            Assert.Equal("allow", allowNetworkPolicyAmendment.GetProperty("action").GetString());
            var denyNetworkPolicyAmendment = decisions.RootElement[3]
                .GetProperty("applyNetworkPolicyAmendment")
                .GetProperty("network_policy_amendment");
            Assert.Equal("example.com", denyNetworkPolicyAmendment.GetProperty("host").GetString());
            Assert.Equal("deny", denyNetworkPolicyAmendment.GetProperty("action").GetString());
            Assert.Equal("cancel", decisions.RootElement[4].GetString());
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldDenyWithoutPrompt_WhenApprovalPolicyIsNever()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var approvalCount = 0;
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(new KernelManagedNetworkApprovalResponse("accept"));
            });

        try
        {
            var result = await session.AuthorizeAsync(
                CreateRequest(scope.Root, approvalPolicy: KernelApprovalPolicy.Never),
                KernelManagedNetworkProtocol.Https,
                "example.com",
                443,
                CancellationToken.None);

            Assert.False(result.Allowed);
            Assert.Equal(KernelManagedNetworkOutcomeKind.DeniedByPolicy, result.Outcome.Kind);
            Assert.Equal(0, approvalCount);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthorizeAsync_ShouldStillPrompt_WhenGranularSandboxApprovalIsFalse()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var approvalCount = 0;
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(new KernelManagedNetworkApprovalResponse("accept"));
            });

        try
        {
            var result = await session.AuthorizeAsync(
                CreateRequest(
                    scope.Root,
                    approvalPolicy: KernelApprovalPolicy.FromGranular(new KernelApprovalGranularPolicy
                    {
                        SandboxApproval = false,
                        Rules = false,
                        SkillApproval = false,
                        RequestPermissions = false,
                        McpElicitations = false,
                    })),
                KernelManagedNetworkProtocol.Https,
                "example.com",
                443,
                CancellationToken.None);

            Assert.True(result.Allowed);
            Assert.Equal(1, approvalCount);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthorizeAsync_WhenNetworkPolicyAmendmentPayloadMissing_ShouldDenyConservatively()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var approvalCount = 0;
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(new KernelManagedNetworkApprovalResponse("applyNetworkPolicyAmendment"));
            });

        try
        {
            var result = await session.AuthorizeAsync(
                CreateRequest(scope.Root),
                KernelManagedNetworkProtocol.Https,
                "example.com",
                443,
                CancellationToken.None);

            Assert.False(result.Allowed);
            Assert.Equal(KernelManagedNetworkOutcomeKind.DeniedByUser, result.Outcome.Kind);
            Assert.Equal(1, approvalCount);
            Assert.Null(execPolicyManager.EvaluateNetwork(KernelManagedNetworkProtocol.Https, "example.com"));
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ManagedHttpProxy_ShouldDenyPostInLimitedModeWithoutPrompt()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var approvalCount = 0;
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(new KernelManagedNetworkApprovalResponse("accept"));
            });
        var lease = new KernelManagedNetworkExecutionLease(session, CreateRequest(scope.Root));

        try
        {
            await lease.StartAsync(CancellationToken.None);

            var response = await SendHttpProxyRequestAsync(
                lease.HttpProxyUrl!,
                "POST http://example.com/upload HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");

            Assert.StartsWith("HTTP/1.1 403 Forbidden", response, StringComparison.Ordinal);
            Assert.Equal("blocked-by-method-policy", GetHttpResponseHeaderValue(response, "x-proxy-error"));
            Assert.Equal("application/json", GetHttpResponseHeaderValue(response, "Content-Type"));
            using (var body = JsonDocument.Parse(GetHttpResponseBody(response)))
            {
                Assert.Equal("blocked", body.RootElement.GetProperty("status").GetString());
                Assert.Equal("example.com", body.RootElement.GetProperty("host").GetString());
                Assert.Equal("method_not_allowed", body.RootElement.GetProperty("reason").GetString());
                Assert.Equal("deny", body.RootElement.GetProperty("decision").GetString());
                Assert.Equal("mode_guard", body.RootElement.GetProperty("source").GetString());
                Assert.Equal("http", body.RootElement.GetProperty("protocol").GetString());
                Assert.Equal(80, body.RootElement.GetProperty("port").GetInt32());
                Assert.Contains("TianShu blocked this request", body.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
                Assert.DoesNotContain("Codex", body.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
            }

            var blocked = Assert.Single(lease.GetBlockedRequestSnapshot());
            Assert.Equal("example.com", blocked.Host);
            Assert.Equal("method_not_allowed", blocked.Reason);
            Assert.NotNull(blocked.Client);
            Assert.Contains("127.0.0.1", blocked.Client, StringComparison.Ordinal);
            Assert.Equal("POST", blocked.Method);
            Assert.Equal("limited", blocked.Mode);
            Assert.Equal("http", blocked.Protocol);
            Assert.Equal("deny", blocked.Decision);
            Assert.Equal("mode_guard", blocked.Source);
            Assert.Equal(80, blocked.Port);
            Assert.True(blocked.Timestamp > 0);
            Assert.Equal(1, lease.GetBlockedRequestTotal());

            Assert.Contains("method policy", lease.ConsumeOutcomeMessage(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, approvalCount);
        }
        finally
        {
            await lease.DisposeAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ManagedHttpProxy_ShouldDenyConnectInLimitedModeWithoutPrompt()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var approvalCount = 0;
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(new KernelManagedNetworkApprovalResponse("accept"));
            });
        var lease = new KernelManagedNetworkExecutionLease(session, CreateRequest(scope.Root));

        try
        {
            await lease.StartAsync(CancellationToken.None);

            var response = await SendHttpProxyRequestAsync(
                lease.HttpProxyUrl!,
                "CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\n\r\n");

            Assert.StartsWith("HTTP/1.1 403 Forbidden", response, StringComparison.Ordinal);
            Assert.Equal("blocked-by-mitm-required", GetHttpResponseHeaderValue(response, "x-proxy-error"));
            Assert.Equal("text/plain", GetHttpResponseHeaderValue(response, "Content-Type"));
            Assert.Equal(
                "TianShu blocked this request: MITM required for limited HTTPS.",
                GetHttpResponseBody(response));
            var blocked = Assert.Single(lease.GetBlockedRequestSnapshot());
            Assert.Equal("example.com", blocked.Host);
            Assert.Equal("mitm_required", blocked.Reason);
            Assert.NotNull(blocked.Client);
            Assert.Contains("127.0.0.1", blocked.Client, StringComparison.Ordinal);
            Assert.Equal("CONNECT", blocked.Method);
            Assert.Equal("limited", blocked.Mode);
            Assert.Equal("http-connect", blocked.Protocol);
            Assert.Equal("deny", blocked.Decision);
            Assert.Equal("mode_guard", blocked.Source);
            Assert.Equal(443, blocked.Port);
            Assert.True(blocked.Timestamp > 0);
            Assert.Equal(1, lease.GetBlockedRequestTotal());

            Assert.Contains("MITM required", lease.ConsumeOutcomeMessage(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, approvalCount);
        }
        finally
        {
            await lease.DisposeAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ManagedHttpProxy_ShouldUseConfiguredUpstreamProxyForPlainRequests()
    {
        using var proxyServer = new SingleRequestTcpServer(async client =>
        {
            var (header, _) = await ReadHttpHeaderAsync(client.GetStream());
            return (header, "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok");
        });
        using var env = new EnvironmentVariableScope("HTTP_PROXY", $"http://127.0.0.1:{proxyServer.Port}");
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(mode: "full", allowUpstreamProxy: true),
            execPolicyManager,
            (_, _) => Task.FromResult(new KernelManagedNetworkApprovalResponse("accept")));
        var lease = new KernelManagedNetworkExecutionLease(session, CreateRequest(scope.Root));

        try
        {
            await lease.StartAsync(CancellationToken.None);

            var response = await SendHttpProxyRequestAsync(
                lease.HttpProxyUrl!,
                "GET http://unresolvable.invalid/test HTTP/1.1\r\nHost: unresolvable.invalid\r\nConnection: keep-alive, x-hop\r\nProxy-Connection: keep-alive\r\nProxy-Authorization: Basic dGVzdA==\r\nX-Hop: secret\r\n\r\n");

            Assert.StartsWith("HTTP/1.1 200 OK", response, StringComparison.Ordinal);
            var forwardedHeader = await proxyServer.HeaderTask;
            Assert.StartsWith("GET http://unresolvable.invalid/test HTTP/1.1", forwardedHeader, StringComparison.Ordinal);
            Assert.DoesNotContain("Proxy-Connection:", forwardedHeader, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Proxy-Authorization:", forwardedHeader, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("X-Hop:", forwardedHeader, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await lease.DisposeAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ManagedHttpProxy_ShouldUseConfiguredUpstreamProxyForConnectRequests()
    {
        using var proxyServer = new SingleRequestTcpServer(async client =>
        {
            var (header, stream) = await ReadHttpHeaderWithStreamAsync(client);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"));
            var buffer = new byte[4];
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
            if (read > 0)
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes("pong"));
            }

            return (header, (string?)null);
        });
        using var env = new EnvironmentVariableScope("HTTPS_PROXY", $"http://127.0.0.1:{proxyServer.Port}");
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(mode: "full", allowUpstreamProxy: true),
            execPolicyManager,
            (_, _) => Task.FromResult(new KernelManagedNetworkApprovalResponse("accept")));
        var lease = new KernelManagedNetworkExecutionLease(session, CreateRequest(scope.Root));

        try
        {
            await lease.StartAsync(CancellationToken.None);
            var (responseHeader, payload) = await SendConnectProxyRequestAsync(
                lease.HttpProxyUrl!,
                "CONNECT unresolvable.invalid:443 HTTP/1.1\r\nHost: unresolvable.invalid:443\r\n\r\n",
                "ping");

            Assert.StartsWith("HTTP/1.1 200 Connection Established", responseHeader, StringComparison.Ordinal);
            Assert.Equal("pong", payload);
            var forwardedHeader = await proxyServer.HeaderTask;
            Assert.StartsWith("CONNECT unresolvable.invalid:443 HTTP/1.1", forwardedHeader, StringComparison.Ordinal);
        }
        finally
        {
            await lease.DisposeAsync();
            await session.DisposeAsync();
        }
    }

        [Fact]
    public async Task ManagedHttpProxy_ShouldReturnStructuredBlockedJsonWhenUnixSocketMethodIsRejected()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var approvalCount = 0;
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(),
            execPolicyManager,
            (_, _) =>
            {
                Interlocked.Increment(ref approvalCount);
                return Task.FromResult(new KernelManagedNetworkApprovalResponse("accept"));
            });
        var lease = new KernelManagedNetworkExecutionLease(session, CreateRequest(scope.Root));

        try
        {
            await lease.StartAsync(CancellationToken.None);
            var response = await SendHttpProxyRequestAsync(
                lease.HttpProxyUrl!,
                "POST /ping HTTP/1.1\r\nHost: localhost\r\nx-unix-socket: /tmp/test.sock\r\nContent-Length: 0\r\n\r\n");

            Assert.StartsWith("HTTP/1.1 403 Forbidden", response, StringComparison.Ordinal);
            Assert.Equal("blocked-by-method-policy", GetHttpResponseHeaderValue(response, "x-proxy-error"));
            Assert.Equal("application/json", GetHttpResponseHeaderValue(response, "Content-Type"));
            using var body = JsonDocument.Parse(GetHttpResponseBody(response));
            Assert.Equal("blocked", body.RootElement.GetProperty("status").GetString());
            Assert.Equal("unix-socket", body.RootElement.GetProperty("host").GetString());
            Assert.Equal("method_not_allowed", body.RootElement.GetProperty("reason").GetString());
            Assert.False(body.RootElement.TryGetProperty("decision", out _));
            Assert.False(body.RootElement.TryGetProperty("source", out _));
            Assert.False(body.RootElement.TryGetProperty("protocol", out _));
            Assert.False(body.RootElement.TryGetProperty("port", out _));
            Assert.False(body.RootElement.TryGetProperty("message", out _));
            Assert.Contains("Unix socket access was blocked by method policy.", lease.ConsumeOutcomeMessage(), StringComparison.Ordinal);
            Assert.Equal(0, approvalCount);
        }
        finally
        {
            await lease.DisposeAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ManagedHttpProxy_ShouldReturnNotImplementedForUnixSocketRequestsOnUnsupportedPlatforms()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(mode: "full", allowUnixSockets: new[] { "/tmp/test.sock" }),
            execPolicyManager,
            (_, _) => Task.FromResult(new KernelManagedNetworkApprovalResponse("accept")));
        var lease = new KernelManagedNetworkExecutionLease(session, CreateRequest(scope.Root));

        try
        {
            await lease.StartAsync(CancellationToken.None);
            var response = await SendHttpProxyRequestAsync(
                lease.HttpProxyUrl!,
                "GET /ping HTTP/1.1\r\nHost: localhost\r\nx-unix-socket: /tmp/test.sock\r\n\r\n");

            Assert.StartsWith("HTTP/1.1 501 Not Implemented", response, StringComparison.Ordinal);
            Assert.Contains("unix sockets unsupported", response, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await lease.DisposeAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ManagedNetworkProxy_ShouldClampBindHostsToLoopbackWhenUnixSocketsEnabled()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(
                mode: "full",
                httpHost: "0.0.0.0",
                socksHost: "0.0.0.0",
                dangerouslyAllowNonLoopbackProxy: true,
                allowUnixSockets: new[] { "/tmp/test.sock" }),
            execPolicyManager,
            (_, _) => Task.FromResult(new KernelManagedNetworkApprovalResponse("accept")));
        var lease = new KernelManagedNetworkExecutionLease(session, CreateRequest(scope.Root));

        try
        {
            await lease.StartAsync(CancellationToken.None);
            Assert.Equal("127.0.0.1", new Uri(lease.HttpProxyUrl!, UriKind.Absolute).Host);
            Assert.Equal("127.0.0.1", new Uri(lease.SocksProxyUrl!, UriKind.Absolute).Host);
        }
        finally
        {
            await lease.DisposeAsync();
            await session.DisposeAsync();
        }
    }
    [Fact]
    public async Task ManagedSocksProxy_ShouldRelayUdpPacketsWhenEnabled()
    {
        using var echoServer = new UdpEchoServer();
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(mode: "full", enableSocks5Udp: true, allowLocalBinding: true),
            execPolicyManager,
            (_, _) => Task.FromResult(new KernelManagedNetworkApprovalResponse("accept")));
        var lease = new KernelManagedNetworkExecutionLease(session, CreateRequest(scope.Root));

        try
        {
            await lease.StartAsync(CancellationToken.None);
            var socksUri = new Uri(lease.SocksProxyUrl!, UriKind.Absolute);
            using var controlClient = new TcpClient();
            await controlClient.ConnectAsync(socksUri.Host, socksUri.Port);
            using var controlStream = controlClient.GetStream();

            await controlStream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 });
            var greetingReply = await ReadExactAsync(controlStream, 2);
            Assert.Equal(new byte[] { 0x05, 0x00 }, greetingReply);

            await controlStream.WriteAsync(new byte[] { 0x05, 0x03, 0x00, 0x01, 0, 0, 0, 0, 0, 0 });
            var associateReply = await ReadExactAsync(controlStream, 10);
            Assert.Equal((byte)0x00, associateReply[1]);
            var relayPort = (associateReply[8] << 8) | associateReply[9];

            using var udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var payload = Encoding.ASCII.GetBytes("ping");
            var packet = BuildSocksUdpPacket(new IPEndPoint(IPAddress.Loopback, echoServer.Port), payload);
            await udpClient.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, relayPort));

            var response = await udpClient.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var (host, port, echoedPayload) = ParseSocksUdpPacket(response.Buffer);
            Assert.Equal(IPAddress.Loopback.ToString(), host);
            Assert.Equal(echoServer.Port, port);
            Assert.Equal("ping", Encoding.ASCII.GetString(echoedPayload));
        }
        finally
        {
            await lease.DisposeAsync();
            await session.DisposeAsync();
        }
    }
    [Fact]
    public async Task ManagedHttpProxy_ShouldReturnBadGatewayWhenPlainUpstreamConnectionFails()
    {
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var unreachablePort = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();

        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(mode: "full", allowLocalBinding: true),
            execPolicyManager,
            (_, _) => Task.FromResult(new KernelManagedNetworkApprovalResponse("accept")));
        var lease = new KernelManagedNetworkExecutionLease(session, CreateRequest(scope.Root));

        try
        {
            await lease.StartAsync(CancellationToken.None);
            var response = await SendHttpProxyRequestAsync(
                lease.HttpProxyUrl!,
                $"GET http://127.0.0.1:{unreachablePort}/ HTTP/1.1\r\nHost: 127.0.0.1:{unreachablePort}\r\n\r\n");

            Assert.StartsWith("HTTP/1.1 502 Bad Gateway", response, StringComparison.Ordinal);
            Assert.Contains("upstream failure", response, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await lease.DisposeAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ManagedSocksProxy_ShouldRecordBlockedTcpRequestWhenLimitedModeRejectsCommand()
    {
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(allowLocalBinding: true),
            execPolicyManager,
            (_, _) => Task.FromResult(new KernelManagedNetworkApprovalResponse("accept")));
        var lease = new KernelManagedNetworkExecutionLease(session, CreateRequest(scope.Root));

        try
        {
            await lease.StartAsync(CancellationToken.None);
            var socksUri = new Uri(lease.SocksProxyUrl!, UriKind.Absolute);
            using var controlClient = new TcpClient();
            await controlClient.ConnectAsync(socksUri.Host, socksUri.Port);
            using var controlStream = controlClient.GetStream();

            await controlStream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 });
            var greetingReply = await ReadExactAsync(controlStream, 2);
            Assert.Equal(new byte[] { 0x05, 0x00 }, greetingReply);

            await controlStream.WriteAsync(new byte[] { 0x05, 0x01, 0x00, 0x01, 127, 0, 0, 1, 0, 80 });
            var connectReply = await ReadExactAsync(controlStream, 10);
            Assert.Equal((byte)0x02, connectReply[1]);

            var blocked = Assert.Single(await WaitForBlockedRequestsAsync(lease, 1, TimeSpan.FromSeconds(2)));
            Assert.Equal(IPAddress.Loopback.ToString(), blocked.Host);
            Assert.Equal("method_not_allowed", blocked.Reason);
            Assert.NotNull(blocked.Client);
            Assert.Contains("127.0.0.1", blocked.Client, StringComparison.Ordinal);
            Assert.Null(blocked.Method);
            Assert.Equal("limited", blocked.Mode);
            Assert.Equal("socks5", blocked.Protocol);
            Assert.Equal("deny", blocked.Decision);
            Assert.Equal("mode_guard", blocked.Source);
            Assert.Equal(80, blocked.Port);
            Assert.True(blocked.Timestamp > 0);
            Assert.Equal(1, lease.GetBlockedRequestTotal());
        }
        finally
        {
            await lease.DisposeAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ManagedSocksProxy_ShouldRecordBlockedUdpRequestWhenLimitedModeRejectsPacket()
    {
        using var echoServer = new UdpEchoServer();
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(enableSocks5Udp: true, allowLocalBinding: true),
            execPolicyManager,
            (_, _) => Task.FromResult(new KernelManagedNetworkApprovalResponse("accept")));
        var lease = new KernelManagedNetworkExecutionLease(session, CreateRequest(scope.Root));

        try
        {
            await lease.StartAsync(CancellationToken.None);
            var socksUri = new Uri(lease.SocksProxyUrl!, UriKind.Absolute);
            using var controlClient = new TcpClient();
            await controlClient.ConnectAsync(socksUri.Host, socksUri.Port);
            using var controlStream = controlClient.GetStream();

            await controlStream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 });
            var greetingReply = await ReadExactAsync(controlStream, 2);
            Assert.Equal(new byte[] { 0x05, 0x00 }, greetingReply);

            await controlStream.WriteAsync(new byte[] { 0x05, 0x03, 0x00, 0x01, 0, 0, 0, 0, 0, 0 });
            var associateReply = await ReadExactAsync(controlStream, 10);
            Assert.Equal((byte)0x00, associateReply[1]);
            var relayPort = (associateReply[8] << 8) | associateReply[9];

            using var udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var payload = Encoding.ASCII.GetBytes("blocked");
            var packet = BuildSocksUdpPacket(new IPEndPoint(IPAddress.Loopback, echoServer.Port), payload);
            await udpClient.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, relayPort));

            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await udpClient.ReceiveAsync().WaitAsync(TimeSpan.FromMilliseconds(750)));

            var blocked = Assert.Single(await WaitForBlockedRequestsAsync(lease, 1, TimeSpan.FromSeconds(2)));
            Assert.Equal(IPAddress.Loopback.ToString(), blocked.Host);
            Assert.Equal("method_not_allowed", blocked.Reason);
            Assert.NotNull(blocked.Client);
            Assert.Contains("127.0.0.1", blocked.Client, StringComparison.Ordinal);
            Assert.Null(blocked.Method);
            Assert.Equal("limited", blocked.Mode);
            Assert.Equal("socks5-udp", blocked.Protocol);
            Assert.Equal("deny", blocked.Decision);
            Assert.Equal("mode_guard", blocked.Source);
            Assert.Equal(echoServer.Port, blocked.Port);
            Assert.True(blocked.Timestamp > 0);
            Assert.Equal(1, lease.GetBlockedRequestTotal());

            var drained = lease.DrainBlockedRequests();
            Assert.Single(drained);
            Assert.Empty(lease.GetBlockedRequestSnapshot());
            Assert.Equal(1, lease.GetBlockedRequestTotal());
        }
        finally
        {
            await lease.DisposeAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ManagedSocksProxy_ShouldRecordBlockedUdpRequestWhenPolicyRejectsPacket()
    {
        using var echoServer = new UdpEchoServer();
        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(
                mode: "full",
                enableSocks5Udp: true,
                deniedDomains: new[] { IPAddress.Loopback.ToString() },
                allowLocalBinding: true),
            execPolicyManager,
            (_, _) => Task.FromResult(new KernelManagedNetworkApprovalResponse("accept")));
        var lease = new KernelManagedNetworkExecutionLease(session, CreateRequest(scope.Root));

        try
        {
            await lease.StartAsync(CancellationToken.None);
            var socksUri = new Uri(lease.SocksProxyUrl!, UriKind.Absolute);
            using var controlClient = new TcpClient();
            await controlClient.ConnectAsync(socksUri.Host, socksUri.Port);
            using var controlStream = controlClient.GetStream();

            await controlStream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 });
            var greetingReply = await ReadExactAsync(controlStream, 2);
            Assert.Equal(new byte[] { 0x05, 0x00 }, greetingReply);

            await controlStream.WriteAsync(new byte[] { 0x05, 0x03, 0x00, 0x01, 0, 0, 0, 0, 0, 0 });
            var associateReply = await ReadExactAsync(controlStream, 10);
            Assert.Equal((byte)0x00, associateReply[1]);
            var relayPort = (associateReply[8] << 8) | associateReply[9];

            using var udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var payload = Encoding.ASCII.GetBytes("blocked");
            var packet = BuildSocksUdpPacket(new IPEndPoint(IPAddress.Loopback, echoServer.Port), payload);
            await udpClient.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, relayPort));

            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await udpClient.ReceiveAsync().WaitAsync(TimeSpan.FromMilliseconds(750)));

            var blocked = Assert.Single(await WaitForBlockedRequestsAsync(lease, 1, TimeSpan.FromSeconds(2)));
            Assert.Equal(IPAddress.Loopback.ToString(), blocked.Host);
            Assert.Equal("denied", blocked.Reason);
            Assert.NotNull(blocked.Client);
            Assert.Contains("127.0.0.1", blocked.Client, StringComparison.Ordinal);
            Assert.Null(blocked.Method);
            Assert.Null(blocked.Mode);
            Assert.Equal("socks5-udp", blocked.Protocol);
            Assert.Equal("deny", blocked.Decision);
            Assert.Equal("baseline_policy", blocked.Source);
            Assert.Equal(echoServer.Port, blocked.Port);
            Assert.True(blocked.Timestamp > 0);
            Assert.Equal(1, lease.GetBlockedRequestTotal());
        }
        finally
        {
            await lease.DisposeAsync();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ManagedHttpProxy_ShouldReturnBadGatewayWhenConnectTargetFailsBeforeTunnelEstablished()
    {
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var unreachablePort = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();

        using var scope = new TestDirectoryScope();
        var execPolicyManager = new KernelExecPolicyManager(scope.Root);
        var session = new KernelManagedNetworkSessionState(
            CreateSettings(mode: "full", allowLocalBinding: true),
            execPolicyManager,
            (_, _) => Task.FromResult(new KernelManagedNetworkApprovalResponse("accept")));
        var lease = new KernelManagedNetworkExecutionLease(session, CreateRequest(scope.Root));

        try
        {
            await lease.StartAsync(CancellationToken.None);
            var response = await SendHttpProxyRequestAsync(
                lease.HttpProxyUrl!,
                $"CONNECT 127.0.0.1:{unreachablePort} HTTP/1.1\r\nHost: 127.0.0.1:{unreachablePort}\r\n\r\n");

            Assert.StartsWith("HTTP/1.1 502 Bad Gateway", response, StringComparison.Ordinal);
            Assert.Contains("upstream failure", response, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await lease.DisposeAsync();
            await session.DisposeAsync();
        }
    }

    private static KernelManagedNetworkSettings CreateSettings(
        string mode = "limited",
        string httpHost = "127.0.0.1",
        string socksHost = "127.0.0.1",
        bool enableSocks5 = true,
        bool enableSocks5Udp = false,
        bool allowUpstreamProxy = false,
        bool dangerouslyAllowNonLoopbackProxy = false,
        bool dangerouslyAllowAllUnixSockets = false,
        IReadOnlyList<string>? allowUnixSockets = null,
        IReadOnlyList<string>? deniedDomains = null,
        bool allowLocalBinding = false)
    {
        return new KernelManagedNetworkSettings(
            RequirementsPresent: true,
            Enabled: true,
            HttpHost: httpHost,
            HttpPort: 0,
            SocksHost: socksHost,
            SocksPort: 0,
            EnableSocks5: enableSocks5,
            EnableSocks5Udp: enableSocks5Udp,
            AllowUpstreamProxy: allowUpstreamProxy,
            DangerouslyAllowNonLoopbackProxy: dangerouslyAllowNonLoopbackProxy,
            DangerouslyAllowAllUnixSockets: dangerouslyAllowAllUnixSockets,
            Mode: mode,
            AllowedDomains: Array.Empty<string>(),
            DeniedDomains: deniedDomains ?? Array.Empty<string>(),
            AllowUnixSockets: allowUnixSockets ?? Array.Empty<string>(),
            AllowLocalBinding: allowLocalBinding);
    }

    private static KernelManagedNetworkExecutionRequest CreateRequest(string cwd, KernelApprovalPolicy? approvalPolicy = null)
    {
        return new KernelManagedNetworkExecutionRequest(
            ThreadId: "thread_managed_network_tests",
            TurnId: "turn_managed_network_tests",
            ItemId: "item_managed_network_tests",
            Command: "exec_command",
            Cwd: cwd,
            SandboxPolicy: JsonSerializer.SerializeToElement(new
            {
                type = "readOnly",
                networkAccess = false,
            }),
            SandboxMode: "readOnly",
            ApprovalPolicy: approvalPolicy ?? KernelApprovalPolicy.OnRequest);
    }

    private static async Task<string> SendHttpProxyRequestAsync(string proxyUrl, string request)
    {
        var uri = new Uri(proxyUrl, UriKind.Absolute);
        using var client = new TcpClient();
        await client.ConnectAsync(uri.Host, uri.Port);
        using var stream = client.GetStream();
        var requestBytes = Encoding.ASCII.GetBytes(request);
        await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
        client.Client.Shutdown(SocketShutdown.Send);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        return await reader.ReadToEndAsync();
    }

        private static string GetHttpResponseHeaderValue(string response, string headerName)
    {
        var headerEnd = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(headerEnd >= 0, "HTTP response terminator not found.");
        var header = response[..headerEnd];
        foreach (var line in header.Split("\r\n", StringSplitOptions.None))
        {
            if (line.StartsWith(headerName + ':', StringComparison.OrdinalIgnoreCase))
            {
                return line[(headerName.Length + 1)..].Trim();
            }
        }

        throw new Xunit.Sdk.XunitException($"HTTP header '{headerName}' not found.\n{response}");
    }

    private static string GetHttpResponseBody(string response)
    {
        var headerEnd = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(headerEnd >= 0, "HTTP response terminator not found.");
        return response[(headerEnd + 4)..];
    }

    private static async Task<(string Header, string Payload)> SendConnectProxyRequestAsync(string proxyUrl, string request, string payload)
    {
        var uri = new Uri(proxyUrl, UriKind.Absolute);
        using var client = new TcpClient();
        await client.ConnectAsync(uri.Host, uri.Port);
        using var stream = client.GetStream();
        var requestBytes = Encoding.ASCII.GetBytes(request);
        await stream.WriteAsync(requestBytes, 0, requestBytes.Length);

        var (header, extraBytes) = await ReadHttpHeaderAsync(stream);
        var response = new List<byte>(extraBytes);
        if (!string.IsNullOrEmpty(payload))
        {
            var payloadBytes = Encoding.ASCII.GetBytes(payload);
            await stream.WriteAsync(payloadBytes, 0, payloadBytes.Length);
            var buffer = new byte[payloadBytes.Length];
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead));
                if (read <= 0)
                {
                    break;
                }

                totalRead += read;
            }

            response.AddRange(buffer.AsSpan(0, totalRead).ToArray());
        }

        client.Client.Shutdown(SocketShutdown.Both);
        return (header, Encoding.ASCII.GetString(response.ToArray()));
    }

    private static async Task<(string Header, byte[] ExtraBytes)> ReadHttpHeaderAsync(NetworkStream stream)
    {
        using var buffer = new MemoryStream();
        var scratch = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(scratch.AsMemory(0, scratch.Length));
            if (read <= 0)
            {
                break;
            }

            await buffer.WriteAsync(scratch.AsMemory(0, read));
            var bytes = buffer.ToArray();
            for (var i = 0; i <= bytes.Length - 4; i++)
            {
                if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10)
                {
                    var headerLength = i + 4;
                    return (Encoding.ASCII.GetString(bytes, 0, headerLength), bytes[headerLength..]);
                }
            }
        }

        throw new IOException("HTTP header terminator not found.");
    }

    private static async Task<(string Header, NetworkStream Stream)> ReadHttpHeaderWithStreamAsync(TcpClient client)
    {
        var stream = client.GetStream();
        var (header, _) = await ReadHttpHeaderAsync(stream);
        return (header, stream);
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset));
            if (read <= 0)
            {
                throw new IOException("Unexpected end of stream.");
            }

            offset += read;
        }

        return buffer;
    }

    private static async Task<IReadOnlyList<KernelManagedNetworkBlockedRequest>> WaitForBlockedRequestsAsync(
        KernelManagedNetworkExecutionLease lease,
        int expectedCount,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = lease.GetBlockedRequestSnapshot();
            if (snapshot.Count >= expectedCount)
            {
                return snapshot;
            }

            await Task.Delay(50);
        }

        return lease.GetBlockedRequestSnapshot();
    }

    private static byte[] BuildSocksUdpPacket(IPEndPoint endpoint, byte[] payload)
    {
        var addressBytes = endpoint.Address.GetAddressBytes();
        var packet = new byte[4 + addressBytes.Length + 2 + payload.Length];
        packet[3] = endpoint.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)0x04 : (byte)0x01;
        Buffer.BlockCopy(addressBytes, 0, packet, 4, addressBytes.Length);
        var portOffset = 4 + addressBytes.Length;
        packet[portOffset] = (byte)((endpoint.Port >> 8) & 0xFF);
        packet[portOffset + 1] = (byte)(endpoint.Port & 0xFF);
        Buffer.BlockCopy(payload, 0, packet, portOffset + 2, payload.Length);
        return packet;
    }

    private static (string Host, int Port, byte[] Payload) ParseSocksUdpPacket(byte[] packet)
    {
        var atyp = packet[3];
        var index = 4;
        string host;
        switch (atyp)
        {
            case 0x01:
                host = new IPAddress(packet.AsSpan(index, 4)).ToString();
                index += 4;
                break;
            case 0x04:
                host = new IPAddress(packet.AsSpan(index, 16)).ToString();
                index += 16;
                break;
            default:
                throw new InvalidOperationException($"Unsupported ATYP: {atyp}");
        }

        var port = (packet[index] << 8) | packet[index + 1];
        index += 2;
        return (host, port, packet[index..]);
    }

    private sealed class UdpEchoServer : IDisposable
    {
        private readonly UdpClient udpClient = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly CancellationTokenSource cts = new();
        private readonly Task loopTask;

        public UdpEchoServer()
        {
            Port = ((IPEndPoint)udpClient.Client.LocalEndPoint!).Port;
            loopTask = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    UdpReceiveResult result;
                    try
                    {
                        result = await udpClient.ReceiveAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        if (cts.IsCancellationRequested)
                        {
                            break;
                        }

                        continue;
                    }

                    await udpClient.SendAsync(result.Buffer, result.RemoteEndPoint, cts.Token);
                }
            }, cts.Token);
        }

        public int Port { get; }

        public void Dispose()
        {
            cts.Cancel();
            udpClient.Dispose();
            try
            {
                loopTask.GetAwaiter().GetResult();
            }
            catch
            {
            }

            cts.Dispose();
        }
    }
    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string key;
        private readonly string? originalValue;

        public EnvironmentVariableScope(string key, string? value)
        {
            this.key = key;
            originalValue = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(key, originalValue);
        }
    }

    private sealed class SingleRequestTcpServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource cts = new();
        private readonly Task serverTask;
        private readonly TaskCompletionSource<string> headerSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SingleRequestTcpServer(Func<TcpClient, Task<(string Header, string? Response)>> handler)
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            serverTask = Task.Run(async () =>
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync(cts.Token);
                    var (header, response) = await handler(client);
                    headerSource.TrySetResult(header);
                    if (!string.IsNullOrEmpty(response))
                    {
                        var bytes = Encoding.ASCII.GetBytes(response);
                        await client.GetStream().WriteAsync(bytes.AsMemory(0, bytes.Length), cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    headerSource.TrySetException(ex);
                }
            }, cts.Token);
        }

        public int Port { get; }

        public Task<string> HeaderTask => headerSource.Task;

        public void Dispose()
        {
            cts.Cancel();
            listener.Stop();
            try
            {
                serverTask.GetAwaiter().GetResult();
            }
            catch
            {
            }

            cts.Dispose();
        }
    }

    private sealed class TestDirectoryScope : IDisposable
    {
        public TestDirectoryScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "tianshu-kernel-managed-network-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}




















