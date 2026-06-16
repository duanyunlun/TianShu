using System.Collections.Concurrent;
using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelToolRuntimeApprovalHelpersTests
{
    [Fact]
    public void BuildDynamicToolApprovalAvailableDecisions_WhenConnectorTool_ShouldExposeRememberOptions()
    {
        var descriptor = CreateDynamicToolDescriptor(connectorId: "search-demo", shortName: "search");

        var decisions = KernelToolRuntimeApprovalHelpers.BuildDynamicToolApprovalAvailableDecisions(descriptor);

        Assert.Equal(["accept", "acceptForSession", "acceptAndRemember", "decline", "cancel"], decisions);
    }

    [Fact]
    public void ResolveDynamicToolApprovalDecision_WhenMetaRequestsPersistentRemember_ShouldReturnRememberDecision()
    {
        using var response = JsonDocument.Parse(
            """
            {
              "decision": "accept",
              "_meta": {
                "persist": "always"
              }
            }
            """);

        var decision = KernelToolRuntimeApprovalHelpers.ResolveDynamicToolApprovalDecision(response.RootElement);

        Assert.Equal("acceptAndRemember", decision);
    }

    [Fact]
    public void IsDynamicToolApprovalRememberedPersistently_WhenConfigApprovesTool_ShouldReturnTrue()
    {
        var config = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["apps"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["search-demo"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["tools"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["search"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["approval_mode"] = "approve",
                        },
                    },
                },
            },
        };

        var descriptor = CreateDynamicToolDescriptor(connectorId: "search-demo", shortName: "search");

        var remembered = KernelToolRuntimeApprovalHelpers.IsDynamicToolApprovalRememberedPersistently(config, descriptor);

        Assert.True(remembered);
    }

    [Fact]
    public void MarkFileChangesApprovedForSession_ShouldAllowSubsequentLookup()
    {
        var approvals = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.Ordinal);
        var path = Path.Combine(Environment.CurrentDirectory, "docs", "tracker.md");

        KernelToolRuntimeApprovalHelpers.MarkFileChangesApprovedForSession(approvals, " thread-1 ", [path]);

        Assert.True(KernelToolRuntimeApprovalHelpers.AreFileChangesApprovedForSession(approvals, "thread-1", [path]));
    }

    [Fact]
    public void TryResolveFileChangePaths_WhenApplyPatch_ShouldCollectAffectedFullPaths()
    {
        var cwd = Path.Combine(Environment.CurrentDirectory, "temp-root");
        var arguments = JsonSerializer.SerializeToElement(new
        {
            input =
                """
                *** Begin Patch
                *** Add File: docs/new-note.txt
                +hello
                *** End Patch
                """,
        });

        var paths = KernelToolRuntimeApprovalHelpers.TryResolveFileChangePaths("apply_patch", arguments, cwd);

        Assert.NotNull(paths);
        Assert.Equal(Path.GetFullPath(Path.Combine(cwd, "docs", "new-note.txt")), Assert.Single(paths!));
    }

    [Fact]
    public void AreGrantedPermissionsApproved_WhenWriteRootCoversPath_ShouldReturnTrue()
    {
        var writeRoot = Path.Combine(Environment.CurrentDirectory, "docs");
        var granted = new KernelPermissionGrantProfile
        {
            WriteRoots = [writeRoot],
        };

        var approved = KernelToolRuntimeApprovalHelpers.AreGrantedPermissionsApproved(
            granted,
            [Path.Combine(writeRoot, "nested", "note.txt")]);

        Assert.True(approved);
    }

    [Fact]
    public void ResolveRequestPermissionsEnabled_WhenGranularConfigPresentWithoutFlag_ShouldReturnFalse()
    {
        var config = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["approval_policy"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["granular"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["rules"] = true,
                },
            },
        };

        var enabled = KernelToolRuntimeApprovalHelpers.ResolveRequestPermissionsEnabled(config, KernelApprovalPolicy.OnRequest);

        Assert.False(enabled);
    }

    [Fact]
    public void IsBuiltInToolExecutionEnabled_ShouldRespectShellModeAndPermissionFlag()
    {
        var nativeToolOptions = new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            ShellToolType: KernelShellToolType.Disabled,
            RequestPermissionsToolEnabled: false);

        Assert.False(KernelToolRuntimeApprovalHelpers.IsBuiltInToolExecutionEnabled("shell_command", nativeToolOptions));
        Assert.False(KernelToolRuntimeApprovalHelpers.IsBuiltInToolExecutionEnabled("request_permissions", nativeToolOptions));
        Assert.True(KernelToolRuntimeApprovalHelpers.IsBuiltInToolExecutionEnabled("read_file", nativeToolOptions));
    }

    [Fact]
    public void TryResolveFileChangeApprovalDecision_WhenAcceptForSessionAliasProvided_ShouldMarkSessionApproval()
    {
        var accepted = KernelToolRuntimeApprovalHelpers.TryResolveFileChangeApprovalDecision("accept_for_session", out var approvedForSession);

        Assert.True(accepted);
        Assert.True(approvedForSession);
    }

    private static KernelDynamicToolDescriptor CreateDynamicToolDescriptor(string? connectorId, string shortName)
    {
        return new KernelDynamicToolDescriptor(
            FullName: $"mcp__demo__{shortName}",
            ShortName: shortName,
            Namespace: "mcp__demo",
            Description: "demo tool",
            Title: "Demo Tool",
            Server: "demo",
            ConnectorName: connectorId is null ? null : "Demo Connector",
            ConnectorDescription: connectorId is null ? null : "Connector description",
            ConnectorId: connectorId,
            InputSchema: null,
            OutputSchema: null,
            Meta: null,
            Annotations: null);
    }
}
