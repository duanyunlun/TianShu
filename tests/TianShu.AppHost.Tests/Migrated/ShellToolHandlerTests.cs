using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.Provider.Abstractions;
using TianShu.Provider.OpenAI;

namespace TianShu.AppHost.Tests;

public sealed class ShellToolHandlerTests
{
    [Fact]
    public async Task ShellTool_ShouldReturnStructuredJsonOutput()
    {
        var handler = new TestShellToolInvoker();
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo hello" },
            timeout_ms = 5000,
        });

        var context = new KernelToolCallContext(
            ThreadId: "thread_shell_1",
            TurnId: "turn_shell_1",
            Cwd: Environment.CurrentDirectory);

        var result = await handler.ExecuteAsync(args, context, CancellationToken.None);
        Assert.True(result.Success);

        using var doc = JsonDocument.Parse(result.OutputText);
        Assert.True(doc.RootElement.TryGetProperty("output", out var output));
        Assert.Contains("hello", output.GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var metadata = doc.RootElement.GetProperty("metadata");
        Assert.Equal(0, metadata.GetProperty("exit_code").GetInt32());
        Assert.True(metadata.GetProperty("duration_seconds").GetDouble() >= 0);
    }

    [Fact]
    public async Task ShellTool_NonZeroExitCode_ShouldReportFailure()
    {
        var handler = new TestShellToolInvoker();
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "exit 3" },
            timeout_ms = 5000,
        });

        var context = new KernelToolCallContext(
            ThreadId: "thread_shell_2",
            TurnId: "turn_shell_2",
            Cwd: Environment.CurrentDirectory);

        var result = await handler.ExecuteAsync(args, context, CancellationToken.None);
        Assert.False(result.Success);

        using var doc = JsonDocument.Parse(result.OutputText);
        var metadata = doc.RootElement.GetProperty("metadata");
        Assert.Equal(3, metadata.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public async Task ShellTool_Timeout_ShouldIncludeTimeoutMessage()
    {
        var handler = new TestShellToolInvoker();
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "powershell.exe", "-NoLogo", "-NonInteractive", "-Command", "Start-Sleep -Milliseconds 3000" },
            timeout_ms = 100,
        });

        var context = new KernelToolCallContext(
            ThreadId: "thread_shell_3",
            TurnId: "turn_shell_3",
            Cwd: Environment.CurrentDirectory);

        var result = await handler.ExecuteAsync(args, context, CancellationToken.None);
        Assert.False(result.Success);

        using var doc = JsonDocument.Parse(result.OutputText);
        var output = doc.RootElement.GetProperty("output").GetString() ?? string.Empty;
        Assert.Contains("command timed out after", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellTool_WithAdditionalPermissions_ShouldAllowNetworkClassifiedCommandWithinReadOnlySandbox()
    {
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "https://example.com", string.Empty, "https://example.com", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo https://example.com" },
            sandbox_permissions = "with_additional_permissions",
            additional_permissions = new
            {
                network = new
                {
                    enabled = true,
                },
            },
            timeout_ms = 5000,
        });

        var context = CreateReadOnlyContext(Environment.CurrentDirectory, "on-request", execPermissionApprovalsEnabled: true);
        var result = await handler.ExecuteAsync(args, context, CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        Assert.Equal(1, runner.CallCount);
        Assert.Equal(Environment.CurrentDirectory, runner.LastCwd);
        Assert.Equal("cmd.exe", runner.LastCommand![0]);

        using var doc = JsonDocument.Parse(result.OutputText);
        Assert.Contains("https://example.com", doc.RootElement.GetProperty("output").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, doc.RootElement.GetProperty("metadata").GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public async Task ShellTool_AdditionalPermissionsWithoutOptIn_ShouldReject()
    {
        var handler = new TestShellToolInvoker();
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo hello" },
            additional_permissions = new
            {
                network = new
                {
                    enabled = true,
                },
            },
        });

        var result = await handler.ExecuteAsync(args, CreateReadOnlyContext(Environment.CurrentDirectory, "on-request"), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("requires `sandbox_permissions` set to `with_additional_permissions`", result.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellTool_WithAdditionalPermissions_ShouldRejectWhenExecPermissionApprovalsDisabled()
    {
        var handler = new TestShellToolInvoker();
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo hello" },
            sandbox_permissions = "with_additional_permissions",
            additional_permissions = new
            {
                network = new
                {
                    enabled = true,
                },
            },
        });

        var result = await handler.ExecuteAsync(args, CreateReadOnlyContext(Environment.CurrentDirectory, "on-request"), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("disabled by config", result.OutputText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellTool_WithAdditionalPermissions_ShouldAllowWhenPermissionsAlreadyGranted()
    {
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "granted", string.Empty, "granted", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo granted" },
            sandbox_permissions = "with_additional_permissions",
            additional_permissions = new
            {
                network = new
                {
                    enabled = true,
                },
            },
        });

        var result = await handler.ExecuteAsync(
            args,
            CreateReadOnlyContext(
                Environment.CurrentDirectory,
                "never",
                requestPermissionsToolEnabled: true,
                grantedPermissions: new KernelPermissionGrantProfile
                {
                    NetworkEnabled = true,
                }),
            CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        Assert.Equal(1, runner.CallCount);
        Assert.Contains("granted", result.OutputText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellTool_WithAdditionalPermissions_ShouldRejectGrantedPermissions_WhenNoFeatureAllowsReuse()
    {
        var handler = new TestShellToolInvoker();
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo granted" },
            sandbox_permissions = "with_additional_permissions",
            additional_permissions = new
            {
                network = new
                {
                    enabled = true,
                },
            },
        });

        var result = await handler.ExecuteAsync(
            args,
            CreateReadOnlyContext(
                Environment.CurrentDirectory,
                "never",
                grantedPermissions: new KernelPermissionGrantProfile
                {
                    NetworkEnabled = true,
                }),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("disabled by config", result.OutputText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellTool_RequireEscalated_ShouldBypassReadOnlyNetworkRestrictionWhenApprovalPolicyAllows()
    {
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "escalated ok", string.Empty, "escalated ok", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo https://example.com" },
            sandbox_permissions = "require_escalated",
            timeout_ms = 5000,
        });

        var result = await handler.ExecuteAsync(args, CreateReadOnlyContext(Environment.CurrentDirectory, "on-request"), CancellationToken.None);
        Assert.True(result.Success, result.OutputText);
        Assert.Equal(1, runner.CallCount);

        using var doc = JsonDocument.Parse(result.OutputText);
        Assert.Equal(0, doc.RootElement.GetProperty("metadata").GetProperty("exit_code").GetInt32());
        Assert.Contains("escalated ok", doc.RootElement.GetProperty("output").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellTool_RequireEscalated_ShouldRejectWhenApprovalPolicyDoesNotAllowPrompt()
    {
        var handler = new TestShellToolInvoker();
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo https://example.com" },
            sandbox_permissions = "require_escalated",
        });

        var result = await handler.ExecuteAsync(args, CreateReadOnlyContext(Environment.CurrentDirectory, "never"), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("approval policy is never", result.OutputText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("escalated permissions", result.OutputText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellTool_RequireEscalated_ShouldNoOpWhenAlreadyDangerFullAccess()
    {
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "full access ok", string.Empty, "full access ok", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo https://example.com" },
            sandbox_permissions = "require_escalated",
            timeout_ms = 5000,
        });

        var result = await handler.ExecuteAsync(args, CreateDangerFullAccessContext(Environment.CurrentDirectory, "never"), CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        Assert.Equal(1, runner.CallCount);
        Assert.Contains("full access ok", result.OutputText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellTool_WithAdditionalPermissions_ShouldNoOpWhenAlreadyDangerFullAccess()
    {
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "full access ok", string.Empty, "full access ok", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo https://example.com" },
            sandbox_permissions = "with_additional_permissions",
            additional_permissions = new
            {
                network = new
                {
                    enabled = true,
                },
            },
            timeout_ms = 5000,
        });

        var result = await handler.ExecuteAsync(args, CreateDangerFullAccessContext(Environment.CurrentDirectory, "never"), CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        Assert.Equal(1, runner.CallCount);
        Assert.Contains("full access ok", result.OutputText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShellTool_PublicSchema_ShouldHideAdditionalPermissions_WhenExecPermissionApprovalsDisabled()
    {
        var spec = new TestShellToolInvoker().BuildModelToolDefinition(execPermissionApprovalsEnabled: false);
        using var json = CompileProviderTool(spec);
        var properties = json.RootElement.GetProperty("parameters").GetProperty("properties");

        var sandboxPermissions = properties.GetProperty("sandbox_permissions").GetProperty("description").GetString() ?? string.Empty;
        Assert.DoesNotContain("with_additional_permissions", sandboxPermissions, StringComparison.Ordinal);
        Assert.DoesNotContain("additional_permissions", properties.EnumerateObject().Select(static property => property.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void ShellCommand_PublicSchema_ShouldExposeOnlyNetworkAndFileSystem_WhenExecPermissionApprovalsEnabled()
    {
        var spec = new TestShellCommandToolInvoker().BuildModelToolDefinition(execPermissionApprovalsEnabled: true);
        using var json = CompileProviderTool(spec);
        var properties = json.RootElement.GetProperty("parameters").GetProperty("properties");
        var sandboxPermissions = properties.GetProperty("sandbox_permissions").GetProperty("description").GetString() ?? string.Empty;

        var additionalPermissions = properties.GetProperty("additional_permissions");
        Assert.False(additionalPermissions.TryGetProperty("required", out _));

        var additionalProperties = additionalPermissions.GetProperty("properties");
        Assert.True(additionalProperties.TryGetProperty("network", out var network));
        Assert.Equal("boolean", network.GetProperty("properties").GetProperty("enabled").GetProperty("type").GetString());
        Assert.True(additionalProperties.TryGetProperty("file_system", out _));
        Assert.Contains("with_additional_permissions", sandboxPermissions, StringComparison.Ordinal);
        Assert.DoesNotContain("macos", additionalProperties.EnumerateObject().Select(static property => property.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void ShellTool_InternalSchema_ShouldIncludeExtendedMacOsPermissionFields()
    {
        var schema = new TestShellToolInvoker().InputSchema;
        var macosProperties = schema
            .GetProperty("properties")
            .GetProperty("additional_permissions")
            .GetProperty("properties")
            .GetProperty("macos")
            .GetProperty("properties")
            .EnumerateObject()
            .Select(static property => property.Name)
            .ToArray();

        Assert.Contains("launch_services", macosProperties);
        Assert.Contains("reminders", macosProperties);
        Assert.Contains("contacts", macosProperties);
    }

    [Fact]
    public async Task ShellCommandTool_WithAdditionalPermissions_ShouldAllowNetworkClassifiedCommandWithinReadOnlySandbox()
    {
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "https://example.com", string.Empty, "https://example.com", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellCommandToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = "Write-Output https://example.com",
            sandbox_permissions = "with_additional_permissions",
            additional_permissions = new
            {
                network = new
                {
                    enabled = true,
                },
            },
            timeout_ms = 5000,
        });

        var result = await handler.ExecuteAsync(
            args,
            CreateReadOnlyContext(Environment.CurrentDirectory, "on-request", execPermissionApprovalsEnabled: true),
            CancellationToken.None);
        Assert.True(result.Success, result.OutputText);
        Assert.Equal(1, runner.CallCount);
        Assert.Contains("Write-Output https://example.com", runner.LastCommand![2], StringComparison.Ordinal);
        Assert.Contains("https://example.com", result.OutputText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellCommandTool_ShouldReturnFreeformOutput()
    {
        var handler = new TestShellCommandToolInvoker();
        var args = JsonSerializer.SerializeToElement(new
        {
            command = "Write-Output hello",
            timeout_ms = 5000,
        });

        var context = new KernelToolCallContext(
            ThreadId: "thread_shell_4",
            TurnId: "turn_shell_4",
            Cwd: Environment.CurrentDirectory);

        var result = await handler.ExecuteAsync(args, context, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Contains("Exit code: 0", result.OutputText, StringComparison.Ordinal);
        Assert.Contains("hello", result.OutputText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellTool_ShouldPassConfiguredEnvironmentToRunner()
    {
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "env ok", string.Empty, "env ok", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo env" },
        });

        var context = CreateReadOnlyContext(
            Environment.CurrentDirectory,
            "on-request",
            allowLoginShell: true,
            shellEnvironmentPolicy: new KernelShellEnvironmentPolicy(
                KernelShellEnvironmentPolicyInherit.None,
                ignoreDefaultExcludes: true,
                excludePatterns: Array.Empty<string>(),
                setVariables: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["TIANSHU_TEST_ENV"] = "present",
                },
                includeOnlyPatterns: Array.Empty<string>(),
                useProfile: false));

        var result = await handler.ExecuteAsync(args, context, CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        Assert.NotNull(runner.LastEnvironment);
        Assert.Equal("present", runner.LastEnvironment!["TIANSHU_TEST_ENV"]);
        Assert.Equal("thread_shell_policy", runner.LastEnvironment[KernelShellEnvironmentBuilder.ThreadIdEnvironmentVariable]);
        Assert.DoesNotContain("PATH", runner.LastEnvironment.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellTool_ShouldNormalizeExplicitPowerShellCommand_ToNoProfileAndUtf8()
    {
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "hello", string.Empty, "hello", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "powershell.exe", "-Command", "Write-Output hello" },
        });

        var result = await handler.ExecuteAsync(args, CreateReadOnlyContext(Environment.CurrentDirectory, "on-request", execPermissionApprovalsEnabled: true), CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        Assert.NotNull(runner.LastCommand);
        Assert.Equal("powershell.exe", runner.LastCommand![0]);
        Assert.Contains("-NoProfile", runner.LastCommand, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("-Command", runner.LastCommand, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;", runner.LastCommand.Last(), StringComparison.Ordinal);
        Assert.Contains("Write-Output hello", runner.LastCommand.Last(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellTool_ShouldAcceptStringifiedJsonCommandArray()
    {
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "hello", string.Empty, "hello", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = JsonSerializer.Serialize(new[] { "powershell.exe", "-Command", "Write-Output hello" }),
        });

        var result = await handler.ExecuteAsync(args, CreateReadOnlyContext(Environment.CurrentDirectory, "on-request", execPermissionApprovalsEnabled: true), CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        Assert.NotNull(runner.LastCommand);
        Assert.Equal("powershell.exe", runner.LastCommand![0]);
        Assert.Contains("Write-Output hello", runner.LastCommand.Last(), StringComparison.Ordinal);
    }

    [Fact]
    public void ShellTool_InternalSchema_ShouldAcceptStringifiedJsonCommandArray()
    {
        var handler = new TestShellToolInvoker();
        var args = JsonSerializer.SerializeToElement(new
        {
            command = JsonSerializer.Serialize(new[] { "powershell.exe", "-Command", "Write-Output hello" }),
        });

        Assert.True(KernelToolSchemaValidator.TryValidate(handler.InputSchema, args, out var error), error);
    }

    [Fact]
    public async Task ShellTool_ShouldNotDuplicateExplicitPowerShellNoProfile_OrUtf8Prefix()
    {
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "hello", string.Empty, "hello", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[]
            {
                "powershell.exe",
                "-NoProfile",
                "-Command",
                "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;\nWrite-Output hello",
            },
        });

        var result = await handler.ExecuteAsync(args, CreateReadOnlyContext(Environment.CurrentDirectory, "on-request", execPermissionApprovalsEnabled: true), CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        Assert.NotNull(runner.LastCommand);
        Assert.Equal(1, runner.LastCommand!.Count(static arg => string.Equals(arg, "-NoProfile", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, runner.LastCommand.Last().Split("[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task ShellCommandTool_ShouldDefaultToNonLoginShellWhenDisabledByContext()
    {
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "hello", string.Empty, "hello", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellCommandToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = "Write-Output hello",
        });

        var result = await handler.ExecuteAsync(args, CreateReadOnlyContext(Environment.CurrentDirectory, "on-request", allowLoginShell: false), CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        Assert.Contains("-NoProfile", runner.LastCommand!, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellCommandTool_ShouldRejectExplicitLoginWhenDisabledByContext()
    {
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "should not run", string.Empty, "should not run", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellCommandToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = "Write-Output hello",
            login = true,
        });

        var result = await handler.ExecuteAsync(args, CreateReadOnlyContext(Environment.CurrentDirectory, "on-request", allowLoginShell: false), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("login shell is disabled by config", result.OutputText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task ShellTool_ShouldInjectManagedNetworkProxyEnvironment()
    {
        using var scope = new ManagedNetworkLeaseScope();
        var lease = await scope.CreateStartedLeaseAsync("item_managed_network_shell");
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "ok", string.Empty, "ok", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo managed-network" },
        });
        var runtimeServices = new KernelToolRuntimeServices(
            BeginManagedNetworkExecution: (_, _) => Task.FromResult<IKernelManagedNetworkExecutionLease>(lease));

        var result = await handler.ExecuteAsync(
            args,
            CreateReadOnlyContext(Environment.CurrentDirectory, "on-request", runtimeServices: runtimeServices, itemId: "item_managed_network_shell"),
            CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        Assert.NotNull(runner.LastEnvironment);
        Assert.StartsWith("http://127.0.0.1:", runner.LastEnvironment!["HTTP_PROXY"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(runner.LastEnvironment["HTTP_PROXY"], runner.LastEnvironment["HTTPS_PROXY"]);
        Assert.Equal(runner.LastEnvironment["HTTP_PROXY"], runner.LastEnvironment["http_proxy"]);
        Assert.Equal(runner.LastEnvironment["HTTP_PROXY"], runner.LastEnvironment["https_proxy"]);
        Assert.Equal("localhost,127.0.0.1,::1,*.local,.local,169.254.0.0/16,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16", runner.LastEnvironment["NO_PROXY"]);
        Assert.Equal(runner.LastEnvironment["NO_PROXY"], runner.LastEnvironment["no_proxy"]);
        Assert.Equal(runner.LastEnvironment["HTTP_PROXY"], runner.LastEnvironment["WS_PROXY"]);
        Assert.Equal(runner.LastEnvironment["HTTP_PROXY"], runner.LastEnvironment["WSS_PROXY"]);
        Assert.StartsWith("socks5h://127.0.0.1:", runner.LastEnvironment["ALL_PROXY"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(runner.LastEnvironment["ALL_PROXY"], runner.LastEnvironment["FTP_PROXY"]);
        Assert.Equal(runner.LastEnvironment["ALL_PROXY"], runner.LastEnvironment["all_proxy"]);
        Assert.Equal("0", runner.LastEnvironment["TIANSHU_NETWORK_ALLOW_LOCAL_BINDING"]);
        Assert.Equal("true", runner.LastEnvironment["ELECTRON_GET_USE_PROXY"]);
    }

    [Fact]
    public async Task ShellTool_ShouldFallbackAllProxyToHttpWhenSocksDisabled()
    {
        using var scope = new ManagedNetworkLeaseScope();
        var lease = await scope.CreateStartedLeaseAsync("item_managed_network_shell_http_only", enableSocks5: false, allowLocalBinding: true);
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "ok", string.Empty, "ok", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo managed-network" },
        });
        var runtimeServices = new KernelToolRuntimeServices(
            BeginManagedNetworkExecution: (_, _) => Task.FromResult<IKernelManagedNetworkExecutionLease>(lease));

        var result = await handler.ExecuteAsync(
            args,
            CreateReadOnlyContext(Environment.CurrentDirectory, "on-request", runtimeServices: runtimeServices, itemId: "item_managed_network_shell_http_only"),
            CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        Assert.NotNull(runner.LastEnvironment);
        Assert.StartsWith("http://127.0.0.1:", runner.LastEnvironment!["HTTP_PROXY"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(runner.LastEnvironment["HTTP_PROXY"], runner.LastEnvironment["ALL_PROXY"]);
        Assert.Equal(runner.LastEnvironment["ALL_PROXY"], runner.LastEnvironment["FTP_PROXY"]);
        Assert.Equal("1", runner.LastEnvironment["TIANSHU_NETWORK_ALLOW_LOCAL_BINDING"]);
    }

    [Fact]
    public async Task ShellTool_ShouldHonorConfiguredManagedNetworkBindHosts()
    {
        using var scope = new ManagedNetworkLeaseScope();
        var lease = await scope.CreateStartedLeaseAsync(
            "item_managed_network_shell_custom_host",
            httpHost: "127.0.0.2",
            socksHost: "127.0.0.2");
        var runner = new RecordingExecRunner
        {
            Response = new KernelExecToolCallOutput(0, "ok", string.Empty, "ok", TimeSpan.FromMilliseconds(5), false),
        };
        var handler = new TestShellToolInvoker(runner.ExecuteAsync);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = new[] { "cmd.exe", "/c", "echo managed-network" },
        });
        var runtimeServices = new KernelToolRuntimeServices(
            BeginManagedNetworkExecution: (_, _) => Task.FromResult<IKernelManagedNetworkExecutionLease>(lease));

        var result = await handler.ExecuteAsync(
            args,
            CreateReadOnlyContext(Environment.CurrentDirectory, "on-request", runtimeServices: runtimeServices, itemId: "item_managed_network_shell_custom_host"),
            CancellationToken.None);

        Assert.True(result.Success, result.OutputText);
        Assert.NotNull(runner.LastEnvironment);
        Assert.StartsWith("http://127.0.0.2:", runner.LastEnvironment!["HTTP_PROXY"], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("socks5h://127.0.0.2:", runner.LastEnvironment["ALL_PROXY"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellTool_ShouldPassSkillManagedNetworkOverrideToRuntimeServices()
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-shell-skill-override-tests", Guid.NewGuid().ToString("N"));
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
            KernelManagedNetworkExecutionRequest? capturedRequest = null;
            var runner = new RecordingExecRunner
            {
                Response = new KernelExecToolCallOutput(0, "ok", string.Empty, "ok", TimeSpan.FromMilliseconds(5), false),
            };
            var handler = new TestShellToolInvoker(runner.ExecuteAsync);
            var args = JsonSerializer.SerializeToElement(new
            {
                command = new[] { scriptPath },
            });
            var runtimeServices = new KernelToolRuntimeServices(
                BeginManagedNetworkExecution: (request, _) =>
                {
                    capturedRequest = request;
                    return Task.FromResult<IKernelManagedNetworkExecutionLease>(KernelManagedNetworkExecutionLease.Inactive());
                });

            var result = await handler.ExecuteAsync(
                args,
                CreateReadOnlyContext(root, "on-request", runtimeServices: runtimeServices, itemId: "item_managed_network_shell_skill"),
                CancellationToken.None);

            Assert.True(result.Success, result.OutputText);
            Assert.NotNull(capturedRequest);
            Assert.Equal(["skill.example.com"], capturedRequest!.SkillAllowedDomains);
            Assert.Equal(["blocked.skill.example.com"], capturedRequest.SkillDeniedDomains);
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

    private sealed class TestShellToolInvoker
    {
        private readonly KernelExecRunnerDelegate execRunner;

        public TestShellToolInvoker(KernelExecRunnerDelegate? execRunner = null)
        {
            this.execRunner = execRunner ?? KernelExecRunner.ExecuteAsync;
        }

        public JsonElement InputSchema => KernelShellRuntimeSupport.BuildShellInternalInputSchema();

        public ProviderResponsesToolDefinition BuildModelToolDefinition(bool execPermissionApprovalsEnabled)
            => KernelShellRuntimeSupport.BuildShellProviderToolDefinition(execPermissionApprovalsEnabled);

        public Task<KernelToolResult> ExecuteAsync(
            JsonElement arguments,
            KernelToolCallContext context,
            CancellationToken cancellationToken)
            => ShellToolExecutor.ExecuteShellAsync(arguments, context, execRunner, cancellationToken);
    }

    private sealed class TestShellCommandToolInvoker
    {
        private readonly KernelExecRunnerDelegate execRunner;

        public TestShellCommandToolInvoker(KernelExecRunnerDelegate? execRunner = null)
        {
            this.execRunner = execRunner ?? KernelExecRunner.ExecuteAsync;
        }

        public ProviderResponsesToolDefinition BuildModelToolDefinition(bool execPermissionApprovalsEnabled)
            => KernelShellRuntimeSupport.BuildShellCommandProviderToolDefinition(execPermissionApprovalsEnabled);

        public Task<KernelToolResult> ExecuteAsync(
            JsonElement arguments,
            KernelToolCallContext context,
            CancellationToken cancellationToken)
            => ShellToolExecutor.ExecuteShellCommandAsync(arguments, context, execRunner, cancellationToken);
    }

    private static KernelToolCallContext CreateReadOnlyContext(string cwd, string approvalPolicy, bool allowLoginShell = true, KernelShellEnvironmentPolicy? shellEnvironmentPolicy = null, KernelToolRuntimeServices? runtimeServices = null, string? itemId = null, bool execPermissionApprovalsEnabled = false, bool requestPermissionsToolEnabled = false, KernelPermissionGrantProfile? grantedPermissions = null)
    {
        return new KernelToolCallContext(
            ThreadId: "thread_shell_policy",
            TurnId: "turn_shell_policy",
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
            GrantedPermissions: grantedPermissions,
            ExecPermissionApprovalsEnabled: execPermissionApprovalsEnabled,
            RequestPermissionsToolEnabled: requestPermissionsToolEnabled);
    }

    private static KernelToolCallContext CreateDangerFullAccessContext(string cwd, string approvalPolicy)
    {
        return new KernelToolCallContext(
            ThreadId: "thread_shell_policy",
            TurnId: "turn_shell_policy",
            Cwd: cwd,
            SandboxPolicy: JsonSerializer.SerializeToElement(new
            {
                type = "danger-full-access",
            }),
            SandboxMode: "danger-full-access",
            ApprovalPolicy: approvalPolicy,
            AllowLoginShell: true,
            ShellEnvironmentPolicy: KernelShellEnvironmentPolicy.Default);
    }

    private static JsonDocument CompileProviderTool(ProviderResponsesToolDefinition definition)
    {
        var tools = new OpenAiResponsesToolSurfaceBuilder().Build(
            new ProviderResponsesToolSurfaceBuilderContext([definition]));
        var tool = Assert.Single(tools);
        return JsonDocument.Parse(JsonSerializer.Serialize(tool));
    }

    private sealed class ManagedNetworkLeaseScope : IDisposable
    {
        private readonly List<KernelManagedNetworkSessionState> sessions = new();

        public ManagedNetworkLeaseScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "tianshu-shell-managed-network-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ExecPolicyManager = new KernelExecPolicyManager(Root);
        }

        public string Root { get; }

        public KernelExecPolicyManager ExecPolicyManager { get; }

        public async Task<KernelManagedNetworkExecutionLease> CreateStartedLeaseAsync(string itemId, bool enableSocks5 = true, bool allowLocalBinding = false, string httpHost = "127.0.0.1", string socksHost = "127.0.0.1")
        {
            var session = new KernelManagedNetworkSessionState(
                new KernelManagedNetworkSettings(
                    RequirementsPresent: true,
                    Enabled: true,
                    HttpHost: httpHost,
                    HttpPort: 0,
                    SocksHost: socksHost,
                    SocksPort: 0,
                    EnableSocks5: enableSocks5,
                    EnableSocks5Udp: false,
                    AllowUpstreamProxy: false,
                    DangerouslyAllowNonLoopbackProxy: false,
                    DangerouslyAllowAllUnixSockets: false,
                    Mode: "limited",
                    AllowedDomains: Array.Empty<string>(),
                    DeniedDomains: Array.Empty<string>(),
                    AllowUnixSockets: Array.Empty<string>(),
                    AllowLocalBinding: allowLocalBinding),
                ExecPolicyManager,
                (_, _) => Task.FromResult(new KernelManagedNetworkApprovalResponse("accept")));
            sessions.Add(session);

            var lease = new KernelManagedNetworkExecutionLease(
                session,
                new KernelManagedNetworkExecutionRequest(
                    ThreadId: "thread_shell_policy",
                    TurnId: "turn_shell_policy",
                    ItemId: itemId,
                    Command: "shell",
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
    private sealed class RecordingExecRunner
    {
        public KernelExecToolCallOutput Response { get; init; } = new(0, string.Empty, string.Empty, string.Empty, TimeSpan.Zero, false);

        public int CallCount { get; private set; }

        public IReadOnlyList<string>? LastCommand { get; private set; }

        public string? LastCwd { get; private set; }

        public IReadOnlyDictionary<string, string>? LastEnvironment { get; private set; }

        public Task<KernelExecToolCallOutput> ExecuteAsync(
            IReadOnlyList<string> command,
            string cwd,
            int timeoutMs,
            IReadOnlyDictionary<string, string>? environment,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCommand = command.ToArray();
            LastCwd = cwd;
            LastEnvironment = environment is null ? null : new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(Response);
        }
    }
}

