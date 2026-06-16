using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tests;

public sealed class KernelCommandApprovalUtilitiesTests
{
    [Fact]
    public void BuildCommandApprovalSessionKey_ShouldNormalizeSandboxPermissionsAndCwd()
    {
        var cwd = Path.Combine(Path.GetTempPath(), "tianshu-command-approval", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cwd);

        try
        {
            var request = new KernelCommandApprovalRequest(
                [" pwsh ", " -NoLogo ", " Write-Output ok "],
                "Write-Output ok",
                cwd,
                Tty: true,
                SandboxPermissionsValue: "Require-Escalated");

            var key = KernelCommandApprovalUtilities.BuildCommandApprovalSessionKey(request);

            Assert.NotNull(key);
            using var payload = JsonDocument.Parse(key!);
            Assert.Equal(Path.GetFullPath(cwd), payload.RootElement.GetProperty("cwd").GetString());
            Assert.True(payload.RootElement.GetProperty("tty").GetBoolean());
            Assert.Equal("require_escalated", payload.RootElement.GetProperty("sandbox_permissions").GetString());

            var command = payload.RootElement.GetProperty("command").EnumerateArray().Select(static item => item.GetString()!).ToArray();
            Assert.Equal(["pwsh", "-NoLogo", "Write-Output ok"], command);
        }
        finally
        {
            Directory.Delete(cwd, recursive: true);
        }
    }

    [Fact]
    public void BuildCommandExecutionAvailableDecisions_ShouldIncludeExecPolicyAmendmentDecision()
    {
        var decisions = KernelCommandApprovalUtilities.BuildCommandExecutionAvailableDecisions(
            new KernelExecPolicyAmendment(["git", "status"]));

        Assert.Equal("accept", decisions[0]);
        Assert.Equal("acceptForSession", decisions[1]);
        var amendmentDecision = JsonSerializer.SerializeToElement(decisions[2]);
        Assert.True(amendmentDecision.TryGetProperty("acceptWithExecpolicyAmendment", out var payload));
        var prefix = payload.GetProperty("execpolicy_amendment").EnumerateArray().Select(static item => item.GetString()!).ToArray();
        Assert.Equal(["git", "status"], prefix);
        Assert.Equal("decline", decisions[3]);
        Assert.Equal("cancel", decisions[4]);
    }

    [Theory]
    [InlineData("accept_for_session", true, true)]
    [InlineData("acceptWithExecpolicyAmendment", true, false)]
    [InlineData("decline", false, false)]
    public void TryResolveCommandApprovalDecision_ShouldNormalizeAliases(
        string decision,
        bool accepted,
        bool approvedForSession)
    {
        var resolved = KernelCommandApprovalUtilities.TryResolveCommandApprovalDecision(decision, out var resolvedForSession);

        Assert.Equal(accepted, resolved);
        Assert.Equal(approvedForSession, resolvedForSession);
    }

    [Theory]
    [InlineData("on-request", true)]
    [InlineData("onfailure", false)]
    [InlineData("on-failure", true)]
    [InlineData("never", false)]
    public void RequiresCommandApproval_ShouldMatchConfiguredPolicies(string? approvalPolicy, bool expected)
    {
        var requiresApproval = KernelCommandApprovalUtilities.RequiresCommandApproval(approvalPolicy);

        Assert.Equal(expected, requiresApproval);
    }

    [Fact]
    public void IsCommandAllowedBySandbox_ShouldRejectMutatingReadOnlyShellPreview()
    {
        var allowed = KernelCommandApprovalUtilities.IsCommandAllowedBySandbox(
            ["pwsh"],
            "mkdir demo",
            "readOnly");

        Assert.False(allowed);
    }

    [Fact]
    public void BuildCommandApprovalDeclinedMessage_ShouldExplainSandboxDenial()
    {
        var message = KernelCommandApprovalUtilities.BuildCommandApprovalDeclinedMessage(
            "sandbox_policy_denied:readOnly",
            sandboxMode: null);

        Assert.Equal("命令被沙箱策略阻止：readOnly", message);
    }

    [Fact]
    public void TryResolveCommandExecutionApprovalSkillMetadata_ShouldProjectSkillPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-command-approval-skill", Guid.NewGuid().ToString("N"));
        var skillDir = Path.Combine(root, "demo-skill");
        Directory.CreateDirectory(skillDir);
        var skillDoc = Path.Combine(skillDir, "SKILL.md");
        var commandPath = Path.Combine(skillDir, OperatingSystem.IsWindows() ? "run.cmd" : "run.sh");

        try
        {
            File.WriteAllText(skillDoc, "# Demo Skill");
            File.WriteAllText(commandPath, OperatingSystem.IsWindows() ? "@echo off\r\necho demo\r\n" : "#!/bin/sh\necho demo\n");

            var metadata = KernelCommandApprovalUtilities.TryResolveCommandExecutionApprovalSkillMetadata([commandPath], root);

            Assert.NotNull(metadata);
            Assert.Equal(KernelPathUtilities.NormalizeSkillDocumentPath(skillDoc), metadata!.PathToSkillsMd);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
