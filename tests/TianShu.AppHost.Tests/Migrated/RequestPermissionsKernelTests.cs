using System.Collections.Concurrent;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Contracts.Tools;
using TianShu.Tools.Interaction;

namespace TianShu.AppHost.Tests;

[Collection("EnvironmentVariables")]
public sealed class RequestPermissionsKernelTests
{
    [Fact]
    public async Task RequestPermissionsProvider_WhenFeatureDisabled_ShouldFailWithoutCallingRequester()
    {
        var root = CreateTempDirectory();
        try
        {
            var requesterCalled = false;
            using var args = JsonDocument.Parse(
                """
                {
                  "permissions": {
                    "network": { "enabled": true },
                    "file_system": { "write": ["docs"] }
                  }
                }
                """);

            var handler = CreateInteractionRuntimeHandler("request_permissions");
            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext(
                    "thread_request_permissions_001",
                    "turn_request_permissions_001",
                    root,
                    RequestPermissionsEnabled: false,
                    PermissionRequester: (_, _) =>
                    {
                        requesterCalled = true;
                        return Task.FromResult(
                            new KernelRequestPermissionsResponse(
                                new KernelPermissionGrantProfile
                                {
                                    NetworkEnabled = true,
                                },
                                KernelPermissionGrantScope.Session));
                    }),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.False(requesterCalled);
            Assert.Contains("disabled", result.OutputText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RequestPermissionsProvider_ShouldRejectMacOsPermissionPayload()
    {
        var root = CreateTempDirectory();
        try
        {
            using var args = JsonDocument.Parse(
                """
                {
                  "permissions": {
                    "macos": {
                      "accessibility": true
                    }
                  }
                }
                """);

            var handler = CreateInteractionRuntimeHandler("request_permissions");
            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext(
                    "thread_request_permissions_unsupported_001",
                    "turn_request_permissions_unsupported_001",
                    root,
                    PermissionRequester: (_, _) => throw new InvalidOperationException("should not request permissions")),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("only supports network", result.OutputText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void PermissionGrantProfile_TryParseAdditionalPermissions_ShouldTreatContactsNoneAsNoMacOsPermission()
    {
        using var payload = JsonDocument.Parse(
            """
            {
              "macos": {
                "contacts": "none"
              }
            }
            """);

        var succeeded = KernelPermissionGrantProfile.TryParseAdditionalPermissions(
            payload.RootElement,
            Environment.CurrentDirectory,
            out var profile,
            out var errorMessage);

        Assert.True(succeeded, errorMessage);
        Assert.True(profile.IsEmpty);
        Assert.False(profile.HasMacOsPermissions);
    }

    [Fact]
    public void PermissionGrantProfile_TryParseAdditionalPermissions_ShouldRecognizeLaunchServicesAsMacOsPermission()
    {
        using var payload = JsonDocument.Parse(
            """
            {
              "macos": {
                "launch_services": true
              }
            }
            """);

        var succeeded = KernelPermissionGrantProfile.TryParseAdditionalPermissions(
            payload.RootElement,
            Environment.CurrentDirectory,
            out var profile,
            out var errorMessage);

        if (OperatingSystem.IsMacOS())
        {
            Assert.True(succeeded, errorMessage);
            Assert.False(profile.IsEmpty);
            Assert.True(profile.HasMacOsPermissions);
            return;
        }

        Assert.False(succeeded);
        Assert.Contains("only supported on macOS", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_ShouldPersistIntersectedPermissionGrantForSessionScope()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const long requestId = 2048;
        const string callId = "permission-request-001";
        const string threadId = "thread_permission_request_001";
        const string turnId = "turn_permission_request_001";
        var requestedPath = Path.Combine(root, "allowed");
        var extraGrantedPath = Path.Combine(root, "extra-granted");

        try
        {
            Directory.CreateDirectory(requestedPath);
            Directory.CreateDirectory(extraGrantedPath);

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/approval/respond",
                @params = new
                {
                    callId,
                    scope = "session",
                    permissions = new
                    {
                        network = new { enabled = true },
                        file_system = new
                        {
                            write = new[]
                            {
                                requestedPath.Replace("\\", "/"),
                                extraGrantedPath.Replace("\\", "/"),
                            },
                        },
                    },
                },
            })));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            var pendingResponses = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var approvalIdsByCall = GetPrivateField<ConcurrentDictionary<string, long>>(server, "approvalRequestIdsByCallId");
            var approvalCallsById = GetPrivateField<ConcurrentDictionary<long, string>>(server, "approvalCallIdsByRequestId");
            var pendingPermissionRequests = GetPrivateField<ConcurrentDictionary<string, KernelPendingPermissionRequest>>(server, "pendingPermissionRequestsByCallId");
            var grantedSessionPermissions = GetPrivateField<ConcurrentDictionary<string, KernelPermissionGrantProfile>>(server, "grantedPermissionSessionByThread");
            var grantedTurnPermissions = GetPrivateField<ConcurrentDictionary<string, KernelPermissionGrantProfile>>(server, "grantedPermissionTurnByTurn");

            var pendingResponse = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(pendingResponses.TryAdd(requestId, pendingResponse));
            approvalIdsByCall[callId] = requestId;
            approvalCallsById[requestId] = callId;
            pendingPermissionRequests[callId] = new KernelPendingPermissionRequest(
                callId,
                threadId,
                turnId,
                root,
                new KernelPermissionGrantProfile
                {
                    NetworkEnabled = true,
                    WriteRoots = [requestedPath],
                });

            await server.RunAsync(CancellationToken.None);

            var resolved = await pendingResponse.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("session", resolved.GetProperty("scope").GetString());
            var resolvedPermissions = resolved.GetProperty("permissions");
            Assert.True(resolvedPermissions.GetProperty("network").GetProperty("enabled").GetBoolean());
            var resolvedWriteRoots = resolvedPermissions
                .GetProperty("file_system")
                .GetProperty("write")
                .EnumerateArray()
                .Select(static item => item.GetString())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();
            Assert.Equal([requestedPath], resolvedWriteRoots);

            Assert.True(grantedSessionPermissions.TryGetValue(threadId, out var storedGrant));
            Assert.NotNull(storedGrant);
            Assert.True(storedGrant!.NetworkEnabled);
            Assert.Equal([requestedPath], storedGrant.WriteRoots);
            Assert.False(grantedTurnPermissions.ContainsKey(turnId));
            Assert.False(pendingPermissionRequests.ContainsKey(callId));

            var lines = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            using var response = JsonDocument.Parse(lines.Single(static line => line.Contains("\"id\":1", StringComparison.Ordinal)));
            var result = response.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("ok").GetBoolean());
            Assert.Equal("session", result.GetProperty("scope").GetString());
            var responseWriteRoots = result
                .GetProperty("permissions")
                .GetProperty("file_system")
                .GetProperty("write")
                .EnumerateArray()
                .Select(static item => item.GetString())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();
            Assert.Equal([requestedPath], responseWriteRoots);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteToolCallAsync_ShouldSkipFileChangeApprovalWhenApplyPatchPathAlreadyGranted()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const string threadId = "thread_permission_granted_patch_001";
        const string turnId = "turn_permission_granted_patch_001";
        var repoRoot = Path.Combine(root, "repo");
        var patchedFile = Path.Combine(repoRoot, "nested", "granted.txt");

        try
        {
            Directory.CreateDirectory(repoRoot);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var writer = new StringWriter();
            var server = new AppHostServer(new StringReader(string.Empty), writer, new KernelThreadStore(storePath));
            var grantedSessionPermissions = GetPrivateField<ConcurrentDictionary<string, KernelPermissionGrantProfile>>(server, "grantedPermissionSessionByThread");
            grantedSessionPermissions[threadId] = new KernelPermissionGrantProfile
            {
                WriteRoots = [repoRoot],
            };

            var patch = string.Join(
                "\n",
                "*** Begin Patch",
                "*** Add File: nested/granted.txt",
                "+granted-content",
                "*** End Patch");

            var result = await server.ExecuteToolCallAsync(
                threadId,
                turnId,
                "tool_apply_patch_granted_001",
                "apply_patch",
                JsonSerializer.SerializeToElement(new
                {
                    input = patch,
                }),
                new TurnRequestContext(
                    Model: null,
                    ModelProvider: null,
                    ServiceTier: null,
                    ApprovalPolicy: "on-request",
                    SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly" }),
                    SandboxMode: null,
                    Cwd: repoRoot,
                    ProviderBaseUrl: null,
                    ProviderApiKeyEnvironmentVariable: null,
                    ProviderWireApi: null,
                    IsReview: false,
                    ReviewDisplayText: null),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);
            Assert.True(File.Exists(patchedFile));
            Assert.Equal("granted-content", (await File.ReadAllTextAsync(patchedFile)).TrimEnd('\r', '\n'));

            Assert.DoesNotContain("item/fileChange/requestApproval", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SessionScopedPermissionGrant_ShouldCarryAcrossTurnsForApplyPatch()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        const long requestId = 4096;
        const string callId = "permission-session-carry-001";
        const string threadId = "thread_permission_session_carry_001";
        const string grantTurnId = "turn_permission_session_grant_001";
        const string nextTurnId = "turn_permission_session_apply_patch_001";
        var repoRoot = Path.Combine(root, "repo");
        var grantedFile = Path.Combine(repoRoot, "nested", "session-granted.txt");

        try
        {
            Directory.CreateDirectory(repoRoot);

            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, repoRoot, CancellationToken.None);

            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/approval/respond",
                @params = new
                {
                    callId,
                    scope = "session",
                    permissions = new
                    {
                        file_system = new
                        {
                            write = new[] { repoRoot.Replace("\\", "/") },
                        },
                    },
                },
            })));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));

            var pendingResponses = GetPrivateField<ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>>(server, "pendingServerResponses");
            var approvalIdsByCall = GetPrivateField<ConcurrentDictionary<string, long>>(server, "approvalRequestIdsByCallId");
            var approvalCallsById = GetPrivateField<ConcurrentDictionary<long, string>>(server, "approvalCallIdsByRequestId");
            var pendingPermissionRequests = GetPrivateField<ConcurrentDictionary<string, KernelPendingPermissionRequest>>(server, "pendingPermissionRequestsByCallId");

            var pendingResponse = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(pendingResponses.TryAdd(requestId, pendingResponse));
            approvalIdsByCall[callId] = requestId;
            approvalCallsById[requestId] = callId;
            pendingPermissionRequests[callId] = new KernelPendingPermissionRequest(
                callId,
                threadId,
                grantTurnId,
                repoRoot,
                new KernelPermissionGrantProfile
                {
                    WriteRoots = [repoRoot],
                });

            await server.RunAsync(CancellationToken.None);
            _ = await pendingResponse.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var patch = string.Join(
                "\n",
                "*** Begin Patch",
                "*** Add File: nested/session-granted.txt",
                "+session-granted-content",
                "*** End Patch");

            var result = await server.ExecuteToolCallAsync(
                threadId,
                nextTurnId,
                "tool_apply_patch_session_001",
                "apply_patch",
                JsonSerializer.SerializeToElement(new
                {
                    input = patch,
                }),
                new TurnRequestContext(
                    Model: null,
                    ModelProvider: null,
                    ServiceTier: null,
                    ApprovalPolicy: "on-request",
                    SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly" }),
                    SandboxMode: null,
                    Cwd: repoRoot,
                    ProviderBaseUrl: null,
                    ProviderApiKeyEnvironmentVariable: null,
                    ProviderWireApi: null,
                    IsReview: false,
                    ReviewDisplayText: null),
                toolCallGate: null,
                cancellationToken: CancellationToken.None);

            Assert.True(result.Success);
            Assert.True(File.Exists(grantedFile));
            Assert.Equal("session-granted-content", (await File.ReadAllTextAsync(grantedFile)).TrimEnd('\r', '\n'));
            Assert.DoesNotContain("item/fileChange/requestApproval", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveRequestPermissionsEnabled_WhenGranularConfigOmitsFlag_DefaultsToFalse()
    {
        using var scope = new PermissionConfigScope(
            """
            [approval_policy.granular]
            sandbox_approval = true
            rules = false
            mcp_elicitations = true
            """);

        var server = CreateServer(scope.Root);
        var enabled = InvokeResolveRequestPermissionsEnabled(server, scope.WorkspacePath, approvalPolicy: null);
        Assert.False(enabled);
    }

    [Fact]
    public void ResolveRequestPermissionsEnabled_WhenGranularConfigExplicitlyAllowsFlag_ReturnsTrue()
    {
        using var scope = new PermissionConfigScope(
            """
            [approval_policy.granular]
            sandbox_approval = true
            rules = false
            request_permissions = true
            mcp_elicitations = true
            """);

        var server = CreateServer(scope.Root);
        var enabled = InvokeResolveRequestPermissionsEnabled(server, scope.WorkspacePath, approvalPolicy: null);
        Assert.True(enabled);
    }

    [Fact]
    public void ResolveRequestPermissionsEnabled_WhenGranularCamelCaseConfigAllowsFlag_IgnoresLegacyConfigAndDefaultsToTrue()
    {
        using var scope = new PermissionConfigScope(
            """
            [approvalPolicy.granular]
            sandboxApproval = true
            rules = false
            requestPermissions = true
            mcpElicitations = true
            """);

        var server = CreateServer(scope.Root);
        var enabled = InvokeResolveRequestPermissionsEnabled(server, scope.WorkspacePath, approvalPolicy: null);
        Assert.True(enabled);
    }

    [Fact]
    public void ResolveRequestPermissionsEnabled_WhenGranularConfigExplicitlyRejectsFlag_ReturnsFalse()
    {
        using var scope = new PermissionConfigScope(
            """
            [approval_policy.granular]
            sandbox_approval = true
            rules = false
            request_permissions = false
            mcp_elicitations = true
            """);

        var server = CreateServer(scope.Root);
        var enabled = InvokeResolveRequestPermissionsEnabled(server, scope.WorkspacePath, approvalPolicy: null);
        Assert.False(enabled);
    }

    [Fact]
    public void ResolveRequestPermissionsEnabled_WhenGranularCamelCaseConfigRejectsFlag_IgnoresLegacyConfigAndDefaultsToTrue()
    {
        using var scope = new PermissionConfigScope(
            """
            [approvalPolicy.granular]
            sandboxApproval = true
            rules = false
            requestPermissions = false
            mcpElicitations = true
            """);

        var server = CreateServer(scope.Root);
        var enabled = InvokeResolveRequestPermissionsEnabled(server, scope.WorkspacePath, approvalPolicy: null);
        Assert.True(enabled);
    }

    [Fact]
    public void ResolveRequestPermissionsEnabled_WhenGranularConfigMissingAndPolicyIsOnRequest_DefaultsToTrue()
    {
        using var scope = new PermissionConfigScope(
            """
            model = "gpt-5-codex"
            approval_policy = "on-request"
            """);

        var server = CreateServer(scope.Root);
        var enabled = InvokeResolveRequestPermissionsEnabled(server, scope.WorkspacePath, approvalPolicy: "on-request");
        Assert.True(enabled);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(instance);
        Assert.NotNull(value);
        return Assert.IsType<T>(value);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static AppHostServer CreateServer(string rootPath)
    {
        var storePath = Path.Combine(rootPath, "threads.json");
        return new AppHostServer(new StringReader(string.Empty), new StringWriter(), new KernelThreadStore(storePath));
    }

    private static bool InvokeResolveRequestPermissionsEnabled(AppHostServer server, string cwd, string? approvalPolicy)
    {
        var method = typeof(AppHostServer).GetMethod(
            "BuildConfigReadSnapshotForRuntime",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);

        var snapshot = method!.Invoke(server, [cwd, null]);
        Assert.NotNull(snapshot);

        var typedApprovalPolicy = approvalPolicy is null ? null : KernelApprovalPolicy.Parse(approvalPolicy);
        var configProperty = snapshot.GetType().GetProperty("Config", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        Assert.NotNull(configProperty);

        var config = configProperty!.GetValue(snapshot);
        Assert.NotNull(config);

        return KernelToolRuntimeApprovalHelpers.ResolveRequestPermissionsEnabled(
            Assert.IsType<Dictionary<string, object?>>(config),
            typedApprovalPolicy);
    }

    private static IKernelToolHandler CreateInteractionRuntimeHandler(string toolKey)
        => new KernelContractToolHandlerAdapter(
            new InteractionToolProvider().CreateHandler(toolKey, new TianShuToolActivationContext()));

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

    private sealed class PermissionConfigScope : IDisposable
    {
        private readonly string? originalTianShuHome;

        public PermissionConfigScope(string configToml)
        {
            Root = CreateTempDirectory();
            TianShuHome = Path.Combine(Root, ".tianshu");
            WorkspacePath = Path.Combine(Root, "workspace");
            Directory.CreateDirectory(TianShuHome);
            Directory.CreateDirectory(WorkspacePath);
            File.WriteAllText(Path.Combine(TianShuHome, "tianshu.toml"), configToml);

            originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
            Environment.SetEnvironmentVariable("TIANSHU_HOME", TianShuHome);
        }

        public string Root { get; }

        public string TianShuHome { get; }

        public string WorkspacePath { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(Root);
        }
    }
}
