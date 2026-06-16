using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelToolExecApprovalParityTests
{
    [Fact]
    public async Task ExecuteToolCallAsync_ShouldExecuteSafeShellCommandWithoutApproval_WhenPolicyIsOnRequest()
    {
        const string threadId = "thread_tool_exec_policy_safe_on_request_001";
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        KernelThreadStore? threadStore = null;

        try
        {
            threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            Directory.CreateDirectory(Path.Combine(root, ".tianshu"));
            await File.WriteAllTextAsync(
                Path.Combine(root, ".tianshu", "tianshu.toml"),
                "[features]\nrequest_permissions_tool = true\n",
                CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);

            var result = await server.ExecuteToolCallAsync(
                threadId,
                "turn_tool_exec_policy_safe_on_request_001",
                "tool_shell_command_safe_on_request_001",
                "shell_command",
                JsonSerializer.SerializeToElement(new
                {
                    command = "Write-Output safe-on-request",
                }),
                CreateTurnContext(root, "on-request"),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success, result.OutputText);
            Assert.Contains("safe-on-request", result.OutputText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("item/tool/requestApproval", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_ShouldExecuteSafeShellCommandWithoutApproval_WhenPolicyIsOnFailure()
    {
        const string threadId = "thread_tool_exec_policy_safe_on_failure_001";
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        KernelThreadStore? threadStore = null;

        try
        {
            threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);

            var result = await server.ExecuteToolCallAsync(
                threadId,
                "turn_tool_exec_policy_safe_on_failure_001",
                "tool_shell_command_safe_on_failure_001",
                "shell_command",
                JsonSerializer.SerializeToElement(new
                {
                    command = "Write-Output safe-on-failure",
                }),
                CreateTurnContext(root, "on-failure"),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success, result.OutputText);
            Assert.Contains("safe-on-failure", result.OutputText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("item/tool/requestApproval", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_ShouldReuseGrantedAdditionalPermissionsWithoutApproval()
    {
        const string threadId = "thread_tool_exec_policy_pregranted_permissions_001";
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        KernelThreadStore? threadStore = null;

        try
        {
            threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            Directory.CreateDirectory(Path.Combine(root, ".tianshu"));
            await File.WriteAllTextAsync(
                Path.Combine(root, ".tianshu", "tianshu.toml"),
                "[features]\nrequest_permissions_tool = true\n",
                CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var grantedPermissionSessionByThread = GetPrivateField<ConcurrentDictionary<string, KernelPermissionGrantProfile>>(
                server,
                "grantedPermissionSessionByThread");
            grantedPermissionSessionByThread[threadId] = new KernelPermissionGrantProfile
            {
                NetworkEnabled = true,
            };

            var result = await server.ExecuteToolCallAsync(
                threadId,
                "turn_tool_exec_policy_pregranted_permissions_001",
                "tool_shell_command_pregranted_permissions_001",
                "shell_command",
                JsonSerializer.SerializeToElement(new
                {
                    command = "Write-Output granted-inline",
                    sandbox_permissions = "with_additional_permissions",
                    additional_permissions = new
                    {
                        network = new
                        {
                            enabled = true,
                        },
                    },
                }),
                CreateTurnContext(root, "never"),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success, result.OutputText);
            Assert.Contains("granted-inline", result.OutputText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("item/tool/requestApproval", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_ShouldRequestCommandExecutionApprovalForShellCommand()
    {
        const string threadId = "thread_tool_exec_policy_shell_command_approval_001";
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        KernelThreadStore? threadStore = null;

        try
        {
            threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var commandText = OperatingSystem.IsWindows()
                ? "New-Item probe.txt -ItemType File"
                : "mkdir probe";

            var executionTask = server.ExecuteToolCallAsync(
                threadId,
                "turn_tool_exec_policy_shell_command_approval_001",
                "tool_shell_command_approval_001",
                "shell_command",
                JsonSerializer.SerializeToElement(new
                {
                    command = commandText,
                }),
                CreateTurnContext(root, "on-request"),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            await WaitForWriterContainsAsync(writer, "\"method\":\"item/commandExecution/requestApproval\"", TimeSpan.FromSeconds(5));
            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var pendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));
            pendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                decision = "decline",
            }));

            var result = await executionTask;

            Assert.False(result.Success);
            Assert.DoesNotContain("item/tool/requestApproval", writer.ToString(), StringComparison.Ordinal);

            var messages = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var approvalRequest = Assert.Single(messages, static x => IsRequestMethod(x.RootElement, "item/commandExecution/requestApproval"));
                var payload = approvalRequest.RootElement.GetProperty("params");
                Assert.Equal(threadId, payload.GetProperty("threadId").GetString());
                Assert.Equal("turn_tool_exec_policy_shell_command_approval_001", payload.GetProperty("turnId").GetString());
                Assert.Equal("tool_shell_command_approval_001", payload.GetProperty("itemId").GetString());
                Assert.Equal(commandText, payload.GetProperty("command").GetString());
                Assert.Equal(root, payload.GetProperty("cwd").GetString());
                Assert.True(payload.TryGetProperty("approvalId", out var approvalId));
                Assert.Equal(JsonValueKind.Null, approvalId.ValueKind);
                Assert.False(payload.TryGetProperty("callId", out _));
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_ShouldCacheAcceptForSessionByCommandApprovalKey()
    {
        const string threadId = "thread_tool_exec_policy_shell_command_session_key_001";
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        KernelThreadStore? threadStore = null;

        try
        {
            threadStore = new KernelThreadStore(storePath);
            await threadStore.InitializeAsync(CancellationToken.None);
            _ = await threadStore.CreateThreadAsync(threadId, root, CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, threadStore);
            var firstCommand = OperatingSystem.IsWindows()
                ? "New-Item probe-one.txt -ItemType File"
                : "mkdir probe-one";
            var secondCommand = OperatingSystem.IsWindows()
                ? "New-Item probe-two.txt -ItemType File"
                : "mkdir probe-two";

            var firstExecutionTask = server.ExecuteToolCallAsync(
                threadId,
                "turn_tool_exec_policy_shell_command_session_key_001",
                "tool_shell_command_session_key_001",
                "shell_command",
                JsonSerializer.SerializeToElement(new
                {
                    command = firstCommand,
                }),
                CreateTurnContext(root, "on-request"),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            await WaitForWriterContainsAsync(writer, "\"method\":\"item/commandExecution/requestApproval\"", TimeSpan.FromSeconds(5));
            var pending = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var firstPendingRequest = await WaitForSinglePendingServerRequestAsync(pending, TimeSpan.FromSeconds(5));
            firstPendingRequest.Value.TrySetResult(JsonSerializer.SerializeToElement(new
            {
                decision = "acceptForSession",
            }));

            var firstResult = await firstExecutionTask;
            Assert.True(firstResult.Success, firstResult.OutputText);

            var approvals = GetPrivateField<ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>>(
                server,
                "commandApprovalSessionKeysByThread");
            Assert.True(approvals.TryGetValue(threadId, out var threadApprovals));
            Assert.Single(threadApprovals!);

            Assert.True(KernelToolCommandApprovalResolver.TryResolve(
                "shell_command",
                JsonSerializer.SerializeToElement(new
                {
                    command = firstCommand,
                }),
                allowLoginShell: true,
                root,
                out var firstRequest,
                out var firstError), firstError);
            Assert.True(KernelToolCommandApprovalResolver.TryResolve(
                "shell_command",
                JsonSerializer.SerializeToElement(new
                {
                    command = secondCommand,
                }),
                allowLoginShell: true,
                root,
                out var secondRequest,
                out var secondError), secondError);

            var firstKey = Assert.IsType<string>(KernelCommandApprovalUtilities.BuildCommandApprovalSessionKey(firstRequest)!);
            var secondKey = Assert.IsType<string>(KernelCommandApprovalUtilities.BuildCommandApprovalSessionKey(secondRequest)!);

            Assert.Contains(firstKey, threadApprovals!.Keys);
            Assert.DoesNotContain(secondKey, threadApprovals.Keys);
        }
        finally
        {
            if (threadStore is not null)
            {
                await threadStore.RolloutRecorder.CloseThreadWriterAsync(threadId, CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    private static TurnRequestContext CreateTurnContext(string cwd, string approvalPolicy)
    {
        return new TurnRequestContext(
            Model: null,
            ModelProvider: null,
            ServiceTier: null,
            ApprovalPolicy: approvalPolicy,
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
            Cwd: cwd,
            ProviderBaseUrl: null,
            ProviderApiKeyEnvironmentVariable: null,
            ProviderWireApi: null,
            IsReview: false,
            ReviewDisplayText: null);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(instance);
        Assert.NotNull(value);
        return Assert.IsType<T>(value);
    }

    private static bool IsRequestMethod(JsonElement json, string method)
    {
        if (!json.TryGetProperty("method", out var methodElement)
            || !json.TryGetProperty("id", out _))
        {
            return false;
        }

        return methodElement.ValueKind == JsonValueKind.String
               && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal);
    }

    private static async Task WaitForWriterContainsAsync(StringWriter writer, string expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (writer.ToString().Contains(expected, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"未在指定时间内观察到文本：{expected}");
    }

    private static async Task<KeyValuePair<long, TaskCompletionSource<JsonElement>>> WaitForSinglePendingServerRequestAsync(
        ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= deadline)
        {
            if (pending.Count == 1)
            {
                return pending.Single();
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("在指定时间内未观察到 pending server request。");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-tool-approval-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(120);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(120);
            }
        }

        Directory.Delete(path, recursive: true);
    }
}
