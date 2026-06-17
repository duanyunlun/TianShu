using System.Reflection;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Remote;

namespace TianShu.Contracts.Remote.Tests;

public sealed class RemoteModuleContractTests
{
    [Fact]
    public void RemoteTransportDescriptor_RejectsPublicBindByDefault()
    {
        Assert.Throws<ArgumentException>(() => new RemoteTransportDescriptor(
            RemoteModuleTransportKind.WebSocket,
            "remote://local/ws",
            RemoteTransportSecurityMode.TlsRequired,
            bindAddress: "0.0.0.0"));

        var descriptor = new RemoteTransportDescriptor(
            RemoteModuleTransportKind.WebSocket,
            "remote://local/ws",
            RemoteTransportSecurityMode.TlsRequired,
            bindAddress: "0.0.0.0",
            allowsPublicNetwork: true);

        Assert.True(descriptor.AllowsPublicNetwork);
        Assert.True(descriptor.RequiresPairing);
    }

    [Fact]
    public void RemoteTransportDescriptor_RejectsUnspecifiedKindSecurityAndNoPairing()
    {
        Assert.Throws<ArgumentException>(() => new RemoteTransportDescriptor(
            RemoteModuleTransportKind.Unspecified,
            "remote://local"));
        Assert.Throws<ArgumentException>(() => new RemoteTransportDescriptor(
            RemoteModuleTransportKind.NamedPipe,
            "remote://pipe",
            RemoteTransportSecurityMode.Unspecified));
        Assert.Throws<ArgumentException>(() => new RemoteTransportDescriptor(
            RemoteModuleTransportKind.NamedPipe,
            "remote://pipe",
            requiresPairing: false));
    }

    [Fact]
    public void RemotePairingGrant_BindsDeviceTrustScopeAndRevocation()
    {
        var issuedAt = new DateTimeOffset(2026, 6, 17, 0, 0, 0, TimeSpan.Zero);
        var grant = new RemotePairingGrant(
            "pairing-001",
            new DeviceId("device-001"),
            "Phone",
            RemoteDeviceTrustLevel.InteractiveOperator,
            new RemoteCommandScope(
                [RemoteCommandKind.SubmitMessage, RemoteCommandKind.ApprovalDecision],
                SideEffectLevel.HostMutation,
                threadRefs: ["thread-001"]),
            issuedAt,
            issuedAt.AddDays(7),
            "revocation://pairing-001",
            [RemoteModuleTransportKind.ServerSentEvents]);

        Assert.Equal(RemotePairingStatus.Granted, grant.Status);
        Assert.Equal("device-001", grant.DeviceId.Value);
        Assert.Equal(RemoteDeviceTrustLevel.InteractiveOperator, grant.TrustLevel);
        Assert.True(grant.Scope.Allows(RemoteCommandKind.SubmitMessage));
        Assert.Equal("revocation://pairing-001", grant.RevocationRef);
    }

    [Fact]
    public void RemoteSessionTokenDescriptor_RejectsLongLivedTokenUnknownAudienceAndEmptyCommandScope()
    {
        var issuedAt = new DateTimeOffset(2026, 6, 17, 0, 0, 0, TimeSpan.Zero);
        var readOnlyScope = new RemoteCommandScope([], SideEffectLevel.ReadOnly);

        Assert.Throws<ArgumentException>(() => new RemoteSessionTokenDescriptor(
            "token://too-long",
            "pairing-001",
            new DeviceId("device-001"),
            [RemoteTokenAudience.Snapshot],
            readOnlyScope,
            issuedAt,
            issuedAt.AddHours(25),
            "revocation://token"));

        Assert.Throws<ArgumentException>(() => new RemoteSessionTokenDescriptor(
            "token://no-audience",
            "pairing-001",
            new DeviceId("device-001"),
            [RemoteTokenAudience.Unspecified],
            readOnlyScope,
            issuedAt,
            issuedAt.AddHours(1),
            "revocation://token"));

        Assert.Throws<ArgumentException>(() => new RemoteSessionTokenDescriptor(
            "token://command-empty",
            "pairing-001",
            new DeviceId("device-001"),
            [RemoteTokenAudience.Command],
            readOnlyScope,
            issuedAt,
            issuedAt.AddHours(1),
            "revocation://token"));
    }

    [Fact]
    public void RemoteSessionTokenDescriptor_DoesNotExposeRawTokenProperty()
    {
        var forbiddenPropertyNames = typeof(RemoteSessionTokenDescriptor)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .Where(static name =>
                name.Contains("RawToken", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                || name.Contains("TokenValue", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(forbiddenPropertyNames);
    }

    [Fact]
    public void RemoteModuleActivationContext_RejectsDeviceMismatchExpiredTokenTransportMismatchAndScopeExpansion()
    {
        var issuedAt = new DateTimeOffset(2026, 6, 17, 0, 0, 0, TimeSpan.Zero);
        var pairing = CreatePairing(issuedAt);
        var token = CreateToken(
            issuedAt,
            new RemoteCommandScope([RemoteCommandKind.SubmitMessage], SideEffectLevel.ReadOnly, threadRefs: ["thread-001"]));
        var transport = new RemoteTransportDescriptor(
            RemoteModuleTransportKind.ServerSentEvents,
            "remote://sse",
            RemoteTransportSecurityMode.LocalOnly);

        Assert.Throws<ArgumentException>(() => new RemoteModuleActivationContext(
            "module.remote",
            new DeviceId("device-other"),
            transport,
            pairing,
            token,
            issuedAt.AddMinutes(1)));

        var expiredToken = CreateToken(
            issuedAt,
            new RemoteCommandScope([RemoteCommandKind.SubmitMessage], SideEffectLevel.ReadOnly, threadRefs: ["thread-001"]),
            expiresAt: issuedAt.AddMinutes(30));
        Assert.Throws<ArgumentException>(() => new RemoteModuleActivationContext(
            "module.remote",
            new DeviceId("device-001"),
            transport,
            pairing,
            expiredToken,
            issuedAt.AddMinutes(31)));

        var wrongTransport = new RemoteTransportDescriptor(
            RemoteModuleTransportKind.WebSocket,
            "remote://ws",
            RemoteTransportSecurityMode.LocalOnly);
        Assert.Throws<ArgumentException>(() => new RemoteModuleActivationContext(
            "module.remote",
            new DeviceId("device-001"),
            wrongTransport,
            pairing,
            token,
            issuedAt.AddMinutes(1)));

        var expandedScopeToken = CreateToken(
            issuedAt,
            new RemoteCommandScope([RemoteCommandKind.SubmitMessage], SideEffectLevel.Privileged, threadRefs: ["thread-001"]));
        Assert.Throws<ArgumentException>(() => new RemoteModuleActivationContext(
            "module.remote",
            new DeviceId("device-001"),
            transport,
            pairing,
            expandedScopeToken,
            issuedAt.AddMinutes(1)));
    }

    [Fact]
    public void RemoteModuleActivationContext_AcceptsMatchedPairingTokenTransportAndScope()
    {
        var issuedAt = new DateTimeOffset(2026, 6, 17, 0, 0, 0, TimeSpan.Zero);
        var tokenScope = new RemoteCommandScope(
            [RemoteCommandKind.SubmitMessage],
            SideEffectLevel.ReadOnly,
            threadRefs: ["thread-001"]);
        var context = new RemoteModuleActivationContext(
            "module.remote",
            new DeviceId("device-001"),
            new RemoteTransportDescriptor(
                RemoteModuleTransportKind.ServerSentEvents,
                "remote://sse",
                RemoteTransportSecurityMode.LocalOnly),
            CreatePairing(issuedAt),
            CreateToken(issuedAt, tokenScope),
            issuedAt.AddMinutes(1));

        Assert.Equal("module.remote", context.ModuleId);
        Assert.Equal("device-001", context.DeviceId.Value);
        Assert.Equal(RemoteModuleTransportKind.ServerSentEvents, context.Transport.Kind);
        Assert.Equal("token://token-001", context.Token.TokenRef);
    }

    [Fact]
    public void RemoteSessionRevocation_RejectsUnspecifiedReason()
    {
        Assert.Throws<ArgumentException>(() => new RemoteSessionRevocation(
            "revocation-001",
            "pairing-001",
            new DeviceId("device-001"),
            "token://token-001",
            RemoteSessionRevocationReason.Unspecified,
            DateTimeOffset.UtcNow,
            "audit://revocation-001"));
    }

    private static RemotePairingGrant CreatePairing(DateTimeOffset issuedAt)
        => new(
            "pairing-001",
            new DeviceId("device-001"),
            "Phone",
            RemoteDeviceTrustLevel.InteractiveOperator,
            new RemoteCommandScope(
                [RemoteCommandKind.SubmitMessage, RemoteCommandKind.ApprovalDecision],
                SideEffectLevel.HostMutation,
                threadRefs: ["thread-001"]),
            issuedAt,
            issuedAt.AddDays(7),
            "revocation://pairing-001",
            [RemoteModuleTransportKind.ServerSentEvents]);

    private static RemoteSessionTokenDescriptor CreateToken(
        DateTimeOffset issuedAt,
        RemoteCommandScope scope,
        DateTimeOffset? expiresAt = null)
        => new(
            "token://token-001",
            "pairing-001",
            new DeviceId("device-001"),
            [RemoteTokenAudience.Snapshot, RemoteTokenAudience.EventStream, RemoteTokenAudience.Command],
            scope,
            issuedAt,
            expiresAt ?? issuedAt.AddHours(1),
            "revocation://token-001");
}
