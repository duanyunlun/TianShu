using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelCommittedUnifiedExecExecutorTests
{
    [Fact]
    public async Task ExecCommandToolHandler_ShouldReturnSessionIdAndInitialOutput()
    {
        var manager = new KernelCommittedUnifiedExecProcessManager();
        var arguments = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "powershell.exe", "-Command", "Write-Output unified-ok; Start-Sleep -Milliseconds 300" },
            cwd = Path.GetTempPath(),
        });

        var result = await ExecuteCommandAsync(manager, arguments, CreateDangerFullAccessContext(Path.GetTempPath()), CancellationToken.None);

        Assert.True(result.Success);
        using var payload = JsonDocument.Parse(result.OutputText);
        Assert.True(payload.RootElement.GetProperty("session_id").GetInt32() > 0);
        Assert.True(payload.RootElement.GetProperty("chunk_id").GetString()!.Length == 6);
        Assert.True(payload.RootElement.TryGetProperty("wall_time_seconds", out _));
        Assert.False(payload.RootElement.TryGetProperty("exit_code", out _));
    }

    [Fact]
    public async Task WriteStdinToolHandler_ShouldWriteToExistingSession()
    {
        var manager = new KernelCommittedUnifiedExecProcessManager();
        var execArgs = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "powershell.exe", "-Command", "$line=[Console]::In.ReadLine(); Write-Output $line" },
            cwd = Path.GetTempPath(),
        });

        var execResult = await ExecuteCommandAsync(manager, execArgs, CreateDangerFullAccessContext(Path.GetTempPath()), CancellationToken.None);
        using var execPayload = JsonDocument.Parse(execResult.OutputText);
        var sessionId = execPayload.RootElement.GetProperty("session_id").GetInt32();

        var writeArgs = JsonSerializer.SerializeToElement(new
        {
            session_id = sessionId,
            text = "hello from stdin\n",
            close = true,
        });
        var writeResult = await WriteStdinAsync(manager, writeArgs, CreateDangerFullAccessContext(Path.GetTempPath()), CancellationToken.None);

        Assert.True(writeResult.Success);
        using var writePayload = JsonDocument.Parse(writeResult.OutputText);
        Assert.False(writePayload.RootElement.TryGetProperty("session_id", out _));
        Assert.Equal(0, writePayload.RootElement.GetProperty("exit_code").GetInt32());
        Assert.Contains("hello from stdin", writePayload.RootElement.GetProperty("output").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecCommandToolHandler_WithAdditionalPermissions_ShouldAllowNetworkClassifiedCommandWithinReadOnlySandbox()
    {
        var cwd = Path.GetTempPath();
        var arguments = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo https://example.com" },
            cwd,
            sandbox_permissions = "with_additional_permissions",
            additional_permissions = new
            {
                network = new
                {
                    enabled = true,
                },
            },
        });

        var resolved = KernelToolSandboxResolver.TryResolve(
            arguments,
            CreateReadOnlyContext(cwd, "on-request", execPermissionApprovalsEnabled: true),
            cwd,
            out var sandboxPolicy,
            out var sandboxMode,
            out var errorMessage);
        Assert.True(resolved, errorMessage);
        Assert.True(sandboxPolicy.GetProperty("networkAccess").GetBoolean());

        var decision = KernelSandboxEnforcer.EvaluateCommand(
            ["cmd.exe", "/c", "echo https://example.com"],
            "cmd.exe /c echo https://example.com",
            cwd,
            sandboxPolicy,
            sandboxMode,
            bypassSandbox: false);

        Assert.True(decision.Allowed, decision.Reason);
    }

    [Fact]
    public async Task ExecCommandToolHandler_ShouldRejectExplicitLoginWhenDisabledByContext()
    {
        var manager = new KernelCommittedUnifiedExecProcessManager();
        var arguments = JsonSerializer.SerializeToElement(new
        {
            cmd = "Write-Output unified-ok",
            login = true,
            cwd = Path.GetTempPath(),
        });

        var result = await ExecuteCommandAsync(manager, arguments, CreateReadOnlyContext(Path.GetTempPath(), "on-request", allowLoginShell: false), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("login shell is disabled by config", result.OutputText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecCommandToolHandler_ShouldApplyShellEnvironmentPolicyToProcess()
    {
        var manager = new KernelCommittedUnifiedExecProcessManager();
        var arguments = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo %TIANSHU_EXEC_ENV% & echo %TIANSHU_THREAD_ID%" },
            cwd = Path.GetTempPath(),
        });

        var context = CreateDangerFullAccessContext(
            Path.GetTempPath(),
            shellEnvironmentPolicy: new KernelShellEnvironmentPolicy(
                KernelShellEnvironmentPolicyInherit.None,
                ignoreDefaultExcludes: true,
                excludePatterns: Array.Empty<string>(),
                setVariables: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["TIANSHU_EXEC_ENV"] = "exec-present",
                },
                includeOnlyPatterns: Array.Empty<string>(),
                useProfile: false));

        var result = await ExecuteCommandAsync(manager, arguments, context, CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        using var payload = JsonDocument.Parse(result.OutputText);
        var output = payload.RootElement.GetProperty("output").GetString() ?? string.Empty;
        Assert.Contains("exec-present", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("thread_exec_policy", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecCommandToolHandler_ShouldInjectManagedNetworkProxyEnvironment()
    {
        using var scope = new ManagedNetworkLeaseScope();
        var manager = new KernelCommittedUnifiedExecProcessManager();
        var lease = await scope.CreateStartedLeaseAsync("item_managed_network_exec");
        var arguments = JsonSerializer.SerializeToElement(new
        {
            command = OperatingSystem.IsWindows()
                ? new[] { "cmd.exe", "/c", "echo %HTTP_PROXY% & echo %ALL_PROXY%" }
                : new[] { "/bin/sh", "-lc", "printf '%s\n%s\n' \"$HTTP_PROXY\" \"$ALL_PROXY\"" },
            cwd = scope.Root,
        });
        var runtimeServices = new KernelToolRuntimeServices(
            BeginManagedNetworkExecution: (_, _) => Task.FromResult<IKernelManagedNetworkExecutionLease>(lease));
        var context = CreateDangerFullAccessContext(scope.Root, runtimeServices: runtimeServices, itemId: "item_managed_network_exec");

        var result = await ExecuteCommandAsync(manager, arguments, context, CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        using var payload = JsonDocument.Parse(result.OutputText);
        var output = payload.RootElement.GetProperty("output").GetString() ?? string.Empty;
        if (payload.RootElement.TryGetProperty("session_id", out var sessionElement))
        {
            var sessionId = sessionElement.GetInt32();
            if (manager.TryGetSession(sessionId, out var session) && session is not null)
            {
                await session.WaitForOutputOrExitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
                output += session.ReadNewOutput(null, out _);
            }

            manager.RemoveSession(sessionId);
        }

        Assert.Contains("http://127.0.0.1:", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("socks5h://127.0.0.1:", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecCommandToolHandler_ShouldReportOriginalTokenCountWhenOutputIsTruncated()
    {
        var manager = new KernelCommittedUnifiedExecProcessManager();
        var arguments = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "powershell.exe", "-Command", "Write-Output 'token one token two token three token four token five token six'" },
            cwd = Path.GetTempPath(),
            max_output_tokens = 2,
        });

        var result = await ExecuteCommandAsync(manager, arguments, CreateDangerFullAccessContext(Path.GetTempPath()), CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        using var payload = JsonDocument.Parse(result.OutputText);
        var output = payload.RootElement.GetProperty("output").GetString() ?? string.Empty;
        if (payload.RootElement.TryGetProperty("session_id", out var sessionElement))
        {
            var sessionId = sessionElement.GetInt32();
            if (manager.TryGetSession(sessionId, out var session) && session is not null)
            {
                await session.WaitForOutputOrExitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
                output += session.ReadNewOutput(null, out _);
            }

            manager.RemoveSession(sessionId);
        }

        Assert.Contains("tokens truncated", output, StringComparison.Ordinal);
        Assert.True(payload.RootElement.GetProperty("original_token_count").GetInt32() > 0);
    }

    [Fact]
    public async Task ExecCommandToolHandler_ShouldBlockUnifiedExec_WhenWindowsSandboxingIsEnabled()
    {
        var manager = new KernelCommittedUnifiedExecProcessManager();
        var arguments = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo blocked" },
            cwd = Path.GetTempPath(),
        });

        var result = await ExecuteCommandAsync(manager, 
            arguments,
            CreateReadOnlyContext(
                Path.GetTempPath(),
                "on-request",
                windowsSandboxLevel: KernelWindowsSandboxLevel.Unelevated),
            CancellationToken.None);

        if (OperatingSystem.IsWindows())
        {
            Assert.False(result.Success);
            Assert.Contains("unified exec is unavailable when Windows sandboxing is enabled", result.OutputText, StringComparison.OrdinalIgnoreCase);
            return;
        }

        Assert.True(result.Success, result.OutputText);
        using var payload = JsonDocument.Parse(result.OutputText);
        if (payload.RootElement.TryGetProperty("session_id", out var sessionIdElement))
        {
            manager.RemoveSession(sessionIdElement.GetInt32());
        }
    }

    [Fact]
    public async Task ExecCommandToolHandler_ShouldAllowUnifiedExec_WhenWindowsSandboxLevelIsDisabled()
    {
        var manager = new KernelCommittedUnifiedExecProcessManager();
        var arguments = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo allowed" },
            cwd = Path.GetTempPath(),
        });

        var result = await ExecuteCommandAsync(manager, 
            arguments,
            CreateReadOnlyContext(
                Path.GetTempPath(),
                "on-request",
                windowsSandboxLevel: KernelWindowsSandboxLevel.Disabled),
            CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        using var payload = JsonDocument.Parse(result.OutputText);
        if (payload.RootElement.TryGetProperty("session_id", out var sessionIdElement))
        {
            manager.RemoveSession(sessionIdElement.GetInt32());
        }
    }

    [Fact]
    public async Task ExecCommandToolHandler_ShouldPassSkillManagedNetworkOverrideToRuntimeServices()
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-committed-exec-skill-override-tests", Guid.NewGuid().ToString("N"));
        var skillDir = Path.Combine(root, ".tianshu", "skills", "managed_network_skill");
        var scriptPath = Path.Combine(skillDir, "scripts", "hello.cmd");
        var metadataPath = Path.Combine(skillDir, "agents", "tianshu.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "# managed_network_skill" + Environment.NewLine);
        File.WriteAllText(scriptPath, "@echo off\r\necho hello\r\n");
        File.WriteAllText(
            metadataPath,
            """
            permissions:
              network:
                allowed_domains:
                  - "skill.example.com"
                denied_domains:
                  - "blocked.skill.example.com"
            """);

        try
        {
            var manager = new KernelCommittedUnifiedExecProcessManager();
            KernelManagedNetworkExecutionRequest? capturedRequest = null;
            var arguments = JsonSerializer.SerializeToElement(new
            {
                command = new[] { scriptPath },
                cwd = root,
            });
            var runtimeServices = new KernelToolRuntimeServices(
                BeginManagedNetworkExecution: (request, _) =>
                {
                    capturedRequest = request;
                    return Task.FromResult<IKernelManagedNetworkExecutionLease>(KernelManagedNetworkExecutionLease.Inactive());
                });
            var context = CreateDangerFullAccessContext(root, runtimeServices: runtimeServices, itemId: "item_managed_network_exec_skill");

            var result = await ExecuteCommandAsync(manager, arguments, context, CancellationToken.None);

            Assert.NotNull(capturedRequest);
            Assert.Equal(["skill.example.com"], capturedRequest!.SkillAllowedDomains);
            Assert.Equal(["blocked.skill.example.com"], capturedRequest.SkillDeniedDomains);
            if (result.Success)
            {
                using var payload = JsonDocument.Parse(result.OutputText);
                if (payload.RootElement.TryGetProperty("session_id", out var sessionIdElement))
                {
                    manager.RemoveSession(sessionIdElement.GetInt32());
                }
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task WriteStdinToolHandler_ShouldAllowExternalSandboxOnWindows()
    {
        var manager = new KernelCommittedUnifiedExecProcessManager();
        var execArgs = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "powershell.exe", "-Command", "$line=[Console]::In.ReadLine(); Write-Output $line" },
            cwd = Path.GetTempPath(),
        });

        var externalSandboxContext = CreateExternalSandboxContext(Path.GetTempPath());
        var execResult = await ExecuteCommandAsync(manager, execArgs, externalSandboxContext, CancellationToken.None);

        Assert.True(execResult.Success, execResult.OutputText);
        using var execPayload = JsonDocument.Parse(execResult.OutputText);
        var sessionId = execPayload.RootElement.GetProperty("session_id").GetInt32();

        var writeArgs = JsonSerializer.SerializeToElement(new
        {
            session_id = sessionId,
            text = "hello external sandbox\n",
            close = true,
        });
        var writeResult = await WriteStdinAsync(manager, writeArgs, externalSandboxContext, CancellationToken.None);

        Assert.True(writeResult.Success, writeResult.OutputText);
        manager.RemoveSession(sessionId);
    }

    private static Task<KernelToolResult> ExecuteCommandAsync(
        KernelCommittedUnifiedExecProcessManager manager,
        JsonElement arguments,
        KernelToolCallContext context,
        CancellationToken cancellationToken)
        => KernelCommittedUnifiedExecExecutor.ExecuteCommandAsync(arguments, context, manager, cancellationToken);

    private static Task<KernelToolResult> WriteStdinAsync(
        KernelCommittedUnifiedExecProcessManager manager,
        JsonElement arguments,
        KernelToolCallContext context,
        CancellationToken cancellationToken)
        => KernelCommittedUnifiedExecExecutor.WriteStdinAsync(arguments, context, manager, cancellationToken);

    private static KernelToolCallContext CreateReadOnlyContext(
        string cwd,
        string approvalPolicy,
        bool allowLoginShell = true,
        KernelShellEnvironmentPolicy? shellEnvironmentPolicy = null,
        KernelToolRuntimeServices? runtimeServices = null,
        string? itemId = null,
        bool execPermissionApprovalsEnabled = false,
        KernelWindowsSandboxLevel windowsSandboxLevel = KernelWindowsSandboxLevel.Disabled)
    {
        return new KernelToolCallContext(
            ThreadId: "thread_exec_policy",
            TurnId: "turn_exec_policy",
            Cwd: cwd,
            SandboxPolicy: JsonSerializer.SerializeToElement(new
            {
                type = "readOnly",
                readOnlyAccess = new
                {
                    type = "fullAccess",
                },
                networkAccess = false,
            }),
            SandboxMode: "readOnly",
            ApprovalPolicy: approvalPolicy,
            AllowLoginShell: allowLoginShell,
            ShellEnvironmentPolicy: shellEnvironmentPolicy ?? KernelShellEnvironmentPolicy.Default,
            RuntimeServices: runtimeServices,
            ItemId: itemId,
            ExecPermissionApprovalsEnabled: execPermissionApprovalsEnabled,
            WindowsSandboxLevel: windowsSandboxLevel);
    }

    private static KernelToolCallContext CreateDangerFullAccessContext(
        string cwd,
        bool allowLoginShell = true,
        KernelShellEnvironmentPolicy? shellEnvironmentPolicy = null,
        KernelToolRuntimeServices? runtimeServices = null,
        string? itemId = null)
    {
        return new KernelToolCallContext(
            ThreadId: "thread_exec_policy",
            TurnId: "turn_exec_policy",
            Cwd: cwd,
            SandboxPolicy: JsonSerializer.SerializeToElement(new
            {
                type = "danger-full-access",
            }),
            SandboxMode: "danger-full-access",
            ApprovalPolicy: "never",
            AllowLoginShell: allowLoginShell,
            ShellEnvironmentPolicy: shellEnvironmentPolicy ?? KernelShellEnvironmentPolicy.Default,
            RuntimeServices: runtimeServices,
            ItemId: itemId);
    }

    private static KernelToolCallContext CreateExternalSandboxContext(string cwd)
    {
        return new KernelToolCallContext(
            ThreadId: "thread_exec_policy",
            TurnId: "turn_exec_policy",
            Cwd: cwd,
            SandboxPolicy: JsonSerializer.SerializeToElement(new
            {
                type = "externalSandbox",
                networkAccess = false,
            }),
            SandboxMode: "externalSandbox",
            ApprovalPolicy: "never",
            AllowLoginShell: true,
            ShellEnvironmentPolicy: KernelShellEnvironmentPolicy.Default);
    }

    private sealed class ManagedNetworkLeaseScope : IDisposable
    {
        private readonly List<KernelManagedNetworkSessionState> sessions = new();

        public ManagedNetworkLeaseScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "tianshu-committed-exec-managed-network-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ExecPolicyManager = new KernelExecPolicyManager(Root);
        }

        public string Root { get; }

        public KernelExecPolicyManager ExecPolicyManager { get; }

        public async Task<KernelManagedNetworkExecutionLease> CreateStartedLeaseAsync(string itemId)
        {
            var session = new KernelManagedNetworkSessionState(
                new KernelManagedNetworkSettings(
                    RequirementsPresent: true,
                    Enabled: true,
                    HttpHost: "127.0.0.1",
                    HttpPort: 0,
                    SocksHost: "127.0.0.1",
                    SocksPort: 0,
                    EnableSocks5: true,
                    EnableSocks5Udp: false,
                    AllowUpstreamProxy: false,
                    DangerouslyAllowNonLoopbackProxy: false,
                    DangerouslyAllowAllUnixSockets: false,
                    Mode: "limited",
                    AllowedDomains: Array.Empty<string>(),
                    DeniedDomains: Array.Empty<string>(),
                    AllowUnixSockets: Array.Empty<string>(),
                    AllowLocalBinding: false),
                ExecPolicyManager,
                (_, _) => Task.FromResult(new KernelManagedNetworkApprovalResponse("accept")));
            sessions.Add(session);

            var lease = new KernelManagedNetworkExecutionLease(
                session,
                new KernelManagedNetworkExecutionRequest(
                    ThreadId: "thread_exec_policy",
                    TurnId: "turn_exec_policy",
                    ItemId: itemId,
                    Command: "exec_command",
                    Cwd: Root,
                    SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly", networkAccess = false }),
                    SandboxMode: "readOnly",
                    ApprovalPolicy: "on-request"));
            await lease.StartAsync(CancellationToken.None);
            return lease;
        }

        public void Dispose()
        {
            foreach (var session in sessions)
            {
                session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

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


