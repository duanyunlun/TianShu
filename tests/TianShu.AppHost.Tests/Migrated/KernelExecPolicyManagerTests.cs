using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using TianShu.AppHost.Tools;
using TianShu.RuntimeComposition;

namespace TianShu.AppHost.Tests;

public sealed class KernelExecPolicyManagerTests
{
    [Fact]
    public void KernelExecPolicyManager_ShouldAllowAndDenyByPrefixRules()
    {
        using var scope = new TestDirectoryScope();
        var policyDir = Path.Combine(scope.Root, "exec-policy");
        Directory.CreateDirectory(policyDir);
        File.WriteAllLines(
            Path.Combine(policyDir, "default.rules"),
            [
                "prefix_rule(pattern=[\"git\", \"status\"], decision=\"allow\")",
                "prefix_rule(pattern=[\"git\", \"push\"], decision=\"forbidden\")",
            ]);

        var manager = new KernelExecPolicyManager(scope.Root);

        var allowed = manager.EvaluateCommand(["git", "status"], "git status", "never", "readOnly", alreadyApproved: false);
        var denied = manager.EvaluateCommand(["git", "push"], "git push", "on-request", "workspaceWrite", alreadyApproved: false);

        Assert.Equal(KernelExecPolicyDecisionKind.Allow, allowed.Kind);
        Assert.True(allowed.BypassSandbox);
        Assert.Equal(KernelExecPolicyDecisionKind.Forbidden, denied.Kind);
        Assert.Equal("exec_policy_rule_denied", denied.Reason);
    }

    [Fact]
    public void KernelExecPolicyManager_ShouldContinueToReadLegacyPrefixRules()
    {
        using var scope = new TestDirectoryScope();
        var policyDir = Path.Combine(scope.Root, "exec-policy");
        Directory.CreateDirectory(policyDir);
        File.WriteAllLines(
            Path.Combine(policyDir, "default.rules"),
            [
                "allow [\"git\",\"status\"]",
                "deny [\"git\",\"push\"]",
            ]);

        var manager = new KernelExecPolicyManager(scope.Root);

        Assert.Equal(
            KernelExecPolicyDecisionKind.Allow,
            manager.EvaluateCommand(["git", "status"], "git status", "never", "readOnly", alreadyApproved: false).Kind);
        Assert.Equal(
            KernelExecPolicyDecisionKind.Forbidden,
            manager.EvaluateCommand(["git", "push"], "git push", "on-request", "workspaceWrite", alreadyApproved: false).Kind);
    }

    [Fact]
    public async Task KernelExecPolicyManager_ShouldAppendAmendmentAndReloadRules()
    {
        using var scope = new TestDirectoryScope();
        var manager = new KernelExecPolicyManager(scope.Root);

        await manager.AppendAmendmentAndUpdateAsync(
            new KernelExecPolicyAmendment(["dotnet", "test"]),
            CancellationToken.None);

        var decision = manager.EvaluateCommand(["dotnet", "test"], "dotnet test", "on-request", "readOnly", alreadyApproved: false);

        Assert.Equal(KernelExecPolicyDecisionKind.Allow, decision.Kind);
        Assert.True(File.Exists(manager.PolicyFilePath));
        Assert.Contains(
            "prefix_rule(pattern=[\"dotnet\",\"test\"], decision=\"allow\")",
            File.ReadAllText(manager.PolicyFilePath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void KernelExecPolicyManager_ShouldAllowSafeCommandWithoutPrompt_WhenPolicyIsOnRequest()
    {
        using var scope = new TestDirectoryScope();
        var manager = new KernelExecPolicyManager(scope.Root);

        var decision = manager.EvaluateCommand(
            ["git", "status"],
            "git status",
            "on-request",
            "readOnly",
            alreadyApproved: false);

        Assert.Equal(KernelExecPolicyDecisionKind.Allow, decision.Kind);
        Assert.Equal("exec_policy_default_allow", decision.Reason);
    }

    [Fact]
    public void KernelExecPolicyManager_ShouldAllowSafeCommandWithoutPrompt_WhenPolicyIsOnFailure()
    {
        using var scope = new TestDirectoryScope();
        var manager = new KernelExecPolicyManager(scope.Root);

        var decision = manager.EvaluateCommand(
            ["git", "status"],
            "git status",
            "on-failure",
            "readOnly",
            alreadyApproved: false);

        Assert.Equal(KernelExecPolicyDecisionKind.Allow, decision.Kind);
        Assert.Equal("exec_policy_default_allow", decision.Reason);
    }

    [Fact]
    public void KernelExecPolicyManager_ShouldStillPromptMutatingCommand_WhenPolicyIsOnRequest()
    {
        using var scope = new TestDirectoryScope();
        var manager = new KernelExecPolicyManager(scope.Root);

        var decision = manager.EvaluateCommand(
            ["git", "add", "note.txt"],
            "git add note.txt",
            "on-request",
            "workspaceWrite",
            alreadyApproved: false);

        Assert.Equal(KernelExecPolicyDecisionKind.Allow, decision.Kind);
        Assert.Equal("exec_policy_default_allow", decision.Reason);
    }

    [Fact]
    public void KernelExecPolicyManager_ShouldPromptFreshSandboxOverride_WhenPolicyIsOnRequest()
    {
        using var scope = new TestDirectoryScope();
        var manager = new KernelExecPolicyManager(scope.Root);

        var decision = manager.EvaluateCommand(
            ["git", "add", "note.txt"],
            "git add note.txt",
            "on-request",
            "workspaceWrite",
            alreadyApproved: false,
            requestsSandboxOverride: true);

        Assert.Equal(KernelExecPolicyDecisionKind.NeedsApproval, decision.Kind);
        Assert.Equal("approval_policy_requires_confirmation", decision.Reason);
    }

    [Fact]
    public void KernelExecPolicyManager_ShouldAllowMutatingCommandWithoutPrompt_WhenPolicyIsOnFailure()
    {
        using var scope = new TestDirectoryScope();
        var manager = new KernelExecPolicyManager(scope.Root);

        var decision = manager.EvaluateCommand(
            ["git", "add", "note.txt"],
            "git add note.txt",
            "on-failure",
            "workspaceWrite",
            alreadyApproved: false);

        Assert.Equal(KernelExecPolicyDecisionKind.Allow, decision.Kind);
        Assert.Equal("exec_policy_default_allow", decision.Reason);
    }

    [Fact]
    public void KernelExecPolicyManager_ShouldPromptUnknownCommandWithinWindowsReadOnlySandbox_WhenPolicyIsOnRequest()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var scope = new TestDirectoryScope();
        var manager = new KernelExecPolicyManager(scope.Root);

        var decision = manager.EvaluateCommand(
            ["git", "add", "note.txt"],
            "git add note.txt",
            "on-request",
            "readOnly",
            alreadyApproved: false);

        Assert.Equal(KernelExecPolicyDecisionKind.NeedsApproval, decision.Kind);
        Assert.Equal("approval_policy_requires_confirmation", decision.Reason);
    }

    [Fact]
    public void KernelExecPolicyManager_ShouldAllowMutatingToolWithoutPrompt_WhenPolicyIsOnFailure()
    {
        using var scope = new TestDirectoryScope();
        var manager = new KernelExecPolicyManager(scope.Root);

        var decision = manager.EvaluateMutatingTool(
            "apply_patch",
            "on-failure",
            "workspaceWrite",
            alreadyApproved: false);

        Assert.Equal(KernelExecPolicyDecisionKind.Allow, decision.Kind);
        Assert.Equal("exec_policy_default_allow", decision.Reason);
    }

    [Fact]
    public void KernelExecPolicyManager_ShouldPromptMutatingToolWithinRestrictedSandbox_WhenPolicyIsOnRequest()
    {
        using var scope = new TestDirectoryScope();
        var manager = new KernelExecPolicyManager(scope.Root);

        var decision = manager.EvaluateMutatingTool(
            "apply_patch",
            "on-request",
            "workspaceWrite",
            alreadyApproved: false);

        Assert.Equal(KernelExecPolicyDecisionKind.NeedsApproval, decision.Kind);
        Assert.Equal("mutating_tool_requires_approval", decision.Reason);
    }

    [Fact]
    public void KernelExecPolicyManager_ShouldReadNetworkRules()
    {
        using var scope = new TestDirectoryScope();
        var policyDir = Path.Combine(scope.Root, "exec-policy");
        Directory.CreateDirectory(policyDir);
        File.WriteAllLines(
            Path.Combine(policyDir, "default.rules"),
            [
                "network_rule(host=\"example.com\", protocol=\"https\", decision=\"allow\")",
                "network_rule(host=\"blocked.example.com\", protocol=\"http\", decision=\"deny\")",
            ]);

        var manager = new KernelExecPolicyManager(scope.Root);

        Assert.Equal(KernelExecPolicyRuleDecision.Allow, manager.EvaluateNetwork(KernelManagedNetworkProtocol.Https, "example.com"));
        Assert.Equal(KernelExecPolicyRuleDecision.Deny, manager.EvaluateNetwork(KernelManagedNetworkProtocol.Http, "blocked.example.com"));
        Assert.Null(manager.EvaluateNetwork(KernelManagedNetworkProtocol.Https, "missing.example.com"));
    }

    [Fact]
    public void KernelExecPolicyManager_ShouldContinueToReadLegacyNetworkRules()
    {
        using var scope = new TestDirectoryScope();
        var policyDir = Path.Combine(scope.Root, "exec-policy");
        Directory.CreateDirectory(policyDir);
        File.WriteAllLines(
            Path.Combine(policyDir, "default.rules"),
            [
                "allow-network {\"protocol\":\"https\",\"host\":\"example.com\"}",
                "deny-network {\"protocol\":\"http\",\"host\":\"blocked.example.com\"}",
            ]);

        var manager = new KernelExecPolicyManager(scope.Root);

        Assert.Equal(KernelExecPolicyRuleDecision.Allow, manager.EvaluateNetwork(KernelManagedNetworkProtocol.Https, "example.com"));
        Assert.Equal(KernelExecPolicyRuleDecision.Deny, manager.EvaluateNetwork(KernelManagedNetworkProtocol.Http, "blocked.example.com"));
    }

    [Fact]
    public void KernelExecPolicyManager_ShouldMergePolicyStrategyRules()
    {
        using var scope = new TestDirectoryScope();
        var manifestPath = Path.Combine(scope.Root, "modules", "policies", "strategies", "builtin", "policy.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(
            manifestPath,
            """
            id = "builtin"
            enabled = true
            type = "builtin"
            priority = 0

            [[strategies]]
            id = "default"
            enabled = true
            type = "rules"
            priority = 0

            [[strategies.command_rules]]
            prefix = ["git", "reset"]
            decision = "ask"
            reason = "危险命令需要确认"

            [[strategies.network_rules]]
            protocol = "https"
            host = "blocked.example.com"
            decision = "deny"
            reason = "禁止访问测试域名"
            """);

        var policyStrategyRules = PolicyStrategyRuntimeComposition.ResolveEffectiveRules(scope.Root);
        var manager = new KernelExecPolicyManager(
            scope.Root,
            policyStrategyRules.CommandRules,
            policyStrategyRules.NetworkRules);

        var commandDecision = manager.EvaluateCommand(
            ["git", "reset", "--hard"],
            "git reset --hard",
            "on-request",
            "workspaceWrite",
            alreadyApproved: false);

        Assert.Equal(KernelExecPolicyDecisionKind.NeedsApproval, commandDecision.Kind);
        Assert.Equal("exec_policy_rule_requires_confirmation", commandDecision.Reason);
        Assert.Equal(KernelExecPolicyRuleDecision.Deny, manager.EvaluateNetwork(KernelManagedNetworkProtocol.Https, "blocked.example.com"));
    }

    [Fact]
    public async Task KernelExecPolicyManager_ShouldAppendNetworkRuleAndReloadRules()
    {
        using var scope = new TestDirectoryScope();
        var manager = new KernelExecPolicyManager(scope.Root);

        await manager.AppendNetworkRuleAndUpdateAsync(
            KernelManagedNetworkProtocol.Https,
            "example.com",
            KernelManagedNetworkRuleAction.Allow,
            CancellationToken.None);
        await manager.AppendNetworkRuleAndUpdateAsync(
            KernelManagedNetworkProtocol.Http,
            "blocked.example.com",
            KernelManagedNetworkRuleAction.Deny,
            CancellationToken.None);

        Assert.Equal(KernelExecPolicyRuleDecision.Allow, manager.EvaluateNetwork(KernelManagedNetworkProtocol.Https, "example.com"));
        Assert.Equal(KernelExecPolicyRuleDecision.Deny, manager.EvaluateNetwork(KernelManagedNetworkProtocol.Http, "blocked.example.com"));

        var content = File.ReadAllText(manager.PolicyFilePath);
        Assert.Contains(
            "network_rule(host=\"example.com\", protocol=\"https\", decision=\"allow\", justification=\"Allow https_connect access to example.com\")",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "network_rule(host=\"blocked.example.com\", protocol=\"http\", decision=\"deny\", justification=\"Deny http access to blocked.example.com\")",
            content,
            StringComparison.Ordinal);
    }

    private sealed class TestDirectoryScope : IDisposable
    {
        public TestDirectoryScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "tianshu-kernel-execpolicy-tests", Guid.NewGuid().ToString("N"));
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

