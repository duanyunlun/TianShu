using System.Collections.Concurrent;
using System.Text.Json;
using TianShu.AppHost.Configuration;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;
using TianShu.Execution.Runtime;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class McpServerSurfaceAppHostRuntime
{
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<CancellationToken, Task<Dictionary<string, string>>> loadEffectiveConfigValuesAsync;
    private readonly Func<string> resolveTianShuHomePath;
    private readonly KernelThreadStore threadStore;
    private readonly KernelThreadManager threadManager;
    private readonly KernelMcpManager mcpManager;
    private readonly ConcurrentDictionary<string, Task> runningTurnTasks;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> pendingMcpOauthLogins = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> pendingMcpOauthLoginTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string> nextThreadId;
    private readonly Func<string, KernelThreadStartRequest, KernelThreadSessionState> buildThreadSessionStateForNewThread;
    private readonly Func<KernelThreadRecord, KernelThreadSessionState, CancellationToken, Task> ensureThreadRolloutMaterializedAsync;
    private readonly Func<string?, CancellationToken, Task> emitExperimentalInstructionsDeprecationNoticeIfNeededAsync;
    private readonly Func<string, CancellationToken, Task<KernelThreadRecord?>> loadThreadRecordPreferringRolloutAsync;
    private readonly Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession;
    private readonly Func<KernelRuntimeThread, KernelThreadSessionState, CancellationToken, Task> updateTurnSandboxStateAsync;
    private readonly Func<KernelThreadRecord, KernelRuntimeThread, string, KernelThreadSessionState, CancellationToken, Task<string>> startBackgroundTurnAsync;
    private readonly Action<string, string, string> seedTrackedTurnUserMessage;

    public McpServerSurfaceAppHostRuntime(
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<CancellationToken, Task<Dictionary<string, string>>> loadEffectiveConfigValuesAsync,
        Func<string> resolveTianShuHomePath,
        KernelThreadStore threadStore,
        KernelThreadManager threadManager,
        KernelMcpManager mcpManager,
        ConcurrentDictionary<string, Task> runningTurnTasks,
        Func<string> nextThreadId,
        Func<string, KernelThreadStartRequest, KernelThreadSessionState> buildThreadSessionStateForNewThread,
        Func<KernelThreadRecord, KernelThreadSessionState, CancellationToken, Task> ensureThreadRolloutMaterializedAsync,
        Func<string?, CancellationToken, Task> emitExperimentalInstructionsDeprecationNoticeIfNeededAsync,
        Func<string, CancellationToken, Task<KernelThreadRecord?>> loadThreadRecordPreferringRolloutAsync,
        Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession,
        Func<KernelRuntimeThread, KernelThreadSessionState, CancellationToken, Task> updateTurnSandboxStateAsync,
        Func<KernelThreadRecord, KernelRuntimeThread, string, KernelThreadSessionState, CancellationToken, Task<string>> startBackgroundTurnAsync,
        Action<string, string, string> seedTrackedTurnUserMessage)
    {
        this.writeErrorAsync = writeErrorAsync;
        this.writeResultAsync = writeResultAsync;
        this.writeNotificationAsync = writeNotificationAsync;
        this.loadEffectiveConfigValuesAsync = loadEffectiveConfigValuesAsync;
        this.resolveTianShuHomePath = resolveTianShuHomePath;
        this.threadStore = threadStore;
        this.threadManager = threadManager;
        this.mcpManager = mcpManager;
        this.runningTurnTasks = runningTurnTasks;
        this.nextThreadId = nextThreadId;
        this.buildThreadSessionStateForNewThread = buildThreadSessionStateForNewThread;
        this.ensureThreadRolloutMaterializedAsync = ensureThreadRolloutMaterializedAsync;
        this.emitExperimentalInstructionsDeprecationNoticeIfNeededAsync = emitExperimentalInstructionsDeprecationNoticeIfNeededAsync;
        this.loadThreadRecordPreferringRolloutAsync = loadThreadRecordPreferringRolloutAsync;
        this.buildDefaultThreadSession = buildDefaultThreadSession;
        this.updateTurnSandboxStateAsync = updateTurnSandboxStateAsync;
        this.startBackgroundTurnAsync = startBackgroundTurnAsync;
        this.seedTrackedTurnUserMessage = seedTrackedTurnUserMessage;
    }

    public async Task HandleMcpServerOauthLoginAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var name = Normalize(ReadString(@params, "name"));
        if (string.IsNullOrWhiteSpace(name))
        {
            await writeErrorAsync(id, -32602, "name 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var tianShuHomePath = resolveTianShuHomePath();
        var names = await McpServerAuthUtilities.ListMcpServerNamesAsync(
                tianShuHomePath,
                loadEffectiveConfigValuesAsync,
                cancellationToken)
            .ConfigureAwait(false);
        if (!names.Contains(name!, StringComparer.OrdinalIgnoreCase))
        {
            await writeErrorAsync(id, -32600, $"No MCP server named '{name}' found.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var authUrl = await McpServerAuthUtilities.ResolveMcpServerAuthorizationUrlAsync(
                name,
                tianShuHomePath,
                loadEffectiveConfigValuesAsync,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(authUrl))
        {
            await writeErrorAsync(id, -32600, $"No MCP server named '{name}' found.", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!Uri.TryCreate(authUrl, UriKind.Absolute, out var uri))
        {
            await writeErrorAsync(
                id,
                -32600,
                $"MCP 服务器 '{name}' 的 OAuth 地址无效：{authUrl}",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            await writeErrorAsync(
                id,
                -32600,
                "OAuth login is only supported for streamable HTTP servers.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var timeoutSecs = ReadLong(@params, "timeoutSecs") ?? 300;
        if (timeoutSecs <= 0)
        {
            await writeErrorAsync(id, -32600, "timeoutSecs must be greater than 0.", cancellationToken).ConfigureAwait(false);
            return;
        }

        await writeResultAsync(id, new
        {
            authorizationUrl = authUrl,
        }, cancellationToken).ConfigureAwait(false);

        if (pendingMcpOauthLogins.TryRemove(name!, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        var loginCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pendingMcpOauthLogins[name!] = loginCts;
        var loginTask = Task.Run(async () =>
        {
            try
            {
                await WaitAndEmitMcpOauthCompletionAsync(name!, TimeSpan.FromSeconds(timeoutSecs), loginCts.Token).ConfigureAwait(false);
            }
            catch
            {
                // 忽略后台通知任务异常，避免影响主请求链路。
            }
            finally
            {
                pendingMcpOauthLoginTasks.TryRemove(name!, out _);
            }
        }, loginCts.Token);
        pendingMcpOauthLoginTasks[name!] = loginTask;
    }

    public async Task HandleMcpServerToolsListAsync(JsonElement id, CancellationToken cancellationToken)
    {
        await writeResultAsync(
                id,
                new
                {
                    tools = new object[]
                    {
                        McpServerSurfaceHelpers.CreateMcpServerTianShuToolDefinition(),
                        McpServerSurfaceHelpers.CreateMcpServerTianShuReplyToolDefinition(),
                    },
                    nextCursor = (string?)null,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task HandleMcpServerToolsCallAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var name = ReadString(@params, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            await writeErrorAsync(id, -32602, "name 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        JsonElement? arguments = TryReadJsonProperty(@params, "arguments", out var parsedArguments)
            ? parsedArguments
            : null;
        McpServerToolCallResult result = Normalize(name) switch
        {
            McpServerSurfaceHelpers.McpServerTianShuToolName => await ExecuteMcpTianShuToolAsync(arguments, cancellationToken).ConfigureAwait(false),
            McpServerSurfaceHelpers.McpServerTianShuReplyToolName => await ExecuteMcpTianShuReplyToolAsync(arguments, cancellationToken).ConfigureAwait(false),
            _ => new McpServerToolCallResult($"Unknown tool '{name}'", true),
        };

        await writeResultAsync(id, McpServerSurfaceHelpers.CreateMcpServerToolCallPayload(result), cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleConfigMcpServerReloadAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        _ = @params;
        var result = await mcpManager.ReloadAsync(cancellationToken).ConfigureAwait(false);

        await writeResultAsync(id, new
        {
            reloaded = result.Reloaded,
            serverCount = result.ServerCount,
        }, cancellationToken).ConfigureAwait(false);

        await writeNotificationAsync("mcpServerStatus/list/updated", new
        {
            data = result.Data,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleMcpServerStatusListAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var cursor = ReadString(@params, "cursor");
        var limit = ReadInt(@params, "limit");
        var names = await mcpManager.ListServerNamesAsync(cancellationToken).ConfigureAwait(false);
        var total = names.Count;

        if (total == 0)
        {
            await writeResultAsync(id, new
            {
                data = Array.Empty<object>(),
                nextCursor = (string?)null,
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var start = 0;
        if (!string.IsNullOrWhiteSpace(cursor) && !int.TryParse(cursor, out start))
        {
            await writeErrorAsync(id, -32600, $"invalid cursor: {cursor}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (start < 0 || start > total)
        {
            await writeErrorAsync(id, -32600, $"cursor {start} exceeds total MCP servers {total}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var effectiveLimit = Math.Clamp(limit ?? total, 1, total);
        var end = Math.Min(start + effectiveLimit, total);
        var data = await mcpManager.BuildStatusDataAsync(
            names.Skip(start).Take(end - start),
            cancellationToken).ConfigureAwait(false);
        var nextCursor = end < total ? end.ToString() : null;

        await writeResultAsync(id, new
        {
            data,
            nextCursor,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<McpServerToolCallResult> ExecuteMcpTianShuToolAsync(
        JsonElement? arguments,
        CancellationToken cancellationToken)
    {
        if (arguments is not JsonElement args || args.ValueKind != JsonValueKind.Object)
        {
            return new McpServerToolCallResult(McpServerSurfaceHelpers.MissingTianShuToolArgumentsMessage, true);
        }

        var prompt = ReadString(args, "prompt");
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new McpServerToolCallResult(McpServerSurfaceHelpers.MissingTianShuToolArgumentsMessage, true);
        }

        KernelThreadStartRequest request;
        try
        {
            request = BuildMcpTianShuThreadStartRequest(args);
        }
        catch (Exception ex)
        {
            return new McpServerToolCallResult(
                $"Failed to parse configuration for TianShu tool: {ex.Message}",
                true);
        }

        try
        {
            var threadId = nextThreadId();
            var session = buildThreadSessionStateForNewThread(threadId, request);
            var record = await threadStore.CreateThreadAsync(threadId, session.Cwd, cancellationToken, session.Ephemeral).ConfigureAwait(false);
            var runtimeThread = threadManager.AttachThread(record, session, loaded: true, publishCreated: true);
            await ensureThreadRolloutMaterializedAsync(record, session, cancellationToken).ConfigureAwait(false);
            await UpdateMcpSandboxStateAsync(session, cancellationToken).ConfigureAwait(false);
            await emitExperimentalInstructionsDeprecationNoticeIfNeededAsync(record.Cwd, cancellationToken).ConfigureAwait(false);
            return await ExecuteMcpToolTurnAsync(record, runtimeThread, session, prompt!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new McpServerToolCallResult(
                $"Failed to start TianShu session: {ex.Message}",
                true);
        }
    }

    public async Task<McpServerToolCallResult> ExecuteMcpTianShuReplyToolAsync(
        JsonElement? arguments,
        CancellationToken cancellationToken)
    {
        if (arguments is not JsonElement args || args.ValueKind != JsonValueKind.Object)
        {
            return new McpServerToolCallResult(McpServerSurfaceHelpers.MissingTianShuReplyToolArgumentsMessage, true);
        }

        var prompt = ReadString(args, "prompt");
        var threadIdText = McpServerSurfaceHelpers.ReadMcpTianShuReplyThreadId(args);
        if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(threadIdText))
        {
            return new McpServerToolCallResult(McpServerSurfaceHelpers.MissingTianShuReplyToolArgumentsMessage, true);
        }

        var threadId = Normalize(threadIdText);
        if (threadId is null)
        {
            return new McpServerToolCallResult(
                $"Failed to parse thread_id: invalid thread id",
                true);
        }

        var record = await loadThreadRecordPreferringRolloutAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return new McpServerToolCallResult(
                $"Session not found for thread_id: {threadId}",
                true,
                threadId);
        }

        var runtimeThread = threadManager.GetOrAttachThread(record, buildDefaultThreadSession, loaded: true);
        var session = runtimeThread.Session;
        await ensureThreadRolloutMaterializedAsync(record, session, cancellationToken).ConfigureAwait(false);
        await UpdateMcpSandboxStateAsync(session, cancellationToken).ConfigureAwait(false);
        return await ExecuteMcpToolTurnAsync(record, runtimeThread, session, prompt!, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateMcpSandboxStateAsync(
        KernelThreadSessionState session,
        CancellationToken cancellationToken)
    {
        var sandboxState = KernelMcpSandboxState.Create(session.SandboxPolicy, session.Cwd);
        await mcpManager.UpdateSandboxStateAsync(sandboxState, cancellationToken).ConfigureAwait(false);
    }

    public async Task WaitForPendingOauthNotificationsAsync(CancellationToken cancellationToken)
    {
        var pendingOauthTasks = pendingMcpOauthLoginTasks.Values.ToArray();
        if (pendingOauthTasks.Length == 0)
        {
            return;
        }

        await Task.WhenAll(pendingOauthTasks).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpServerToolCallResult> ExecuteMcpToolTurnAsync(
        KernelThreadRecord record,
        KernelRuntimeThread runtimeThread,
        KernelThreadSessionState session,
        string prompt,
        CancellationToken cancellationToken)
    {
        await updateTurnSandboxStateAsync(runtimeThread, session, cancellationToken).ConfigureAwait(false);
        var turnId = await startBackgroundTurnAsync(record, runtimeThread, prompt, session, cancellationToken).ConfigureAwait(false);
        seedTrackedTurnUserMessage(record.Id, turnId, prompt);

        if (runningTurnTasks.TryGetValue(turnId, out var task))
        {
            await task.ConfigureAwait(false);
        }

        var persisted = await threadStore.GetThreadAsync(record.Id, cancellationToken).ConfigureAwait(false);
        if (persisted is null)
        {
            return new McpServerToolCallResult("Session not found after tool execution.", true, record.Id);
        }

        KernelTurnRecord? completedTurn = null;
        for (var index = persisted.Turns.Count - 1; index >= 0; index--)
        {
            var candidate = persisted.Turns[index];
            if (string.Equals(candidate.Id, turnId, StringComparison.Ordinal))
            {
                completedTurn = candidate;
                break;
            }
        }

        if (completedTurn is null)
        {
            return new McpServerToolCallResult(
                McpServerSurfaceHelpers.NormalizeMcpServerToolContent(prompt, Normalize(persisted.LastAssistantMessage) ?? string.Empty),
                true,
                record.Id);
        }

        var text = Normalize(completedTurn.AssistantMessage)
                   ?? Normalize(completedTurn.Error?.Message)
                   ?? Normalize(persisted.LastAssistantMessage)
                   ?? string.Empty;
        text = McpServerSurfaceHelpers.NormalizeMcpServerToolContent(prompt, text);
        var isError = !string.Equals(completedTurn.Status, "completed", StringComparison.OrdinalIgnoreCase);
        return new McpServerToolCallResult(text, isError, record.Id);
    }

    private KernelThreadStartRequest BuildMcpTianShuThreadStartRequest(JsonElement arguments)
    {
        var profile = ReadString(arguments, "profile");
        var compactPrompt = ReadString(arguments, "compact-prompt") ?? ReadString(arguments, "compact_prompt");
        var config = CreateMcpTianShuConfigOverride(arguments, profile, compactPrompt);

        return new KernelThreadStartRequest
        {
            Model = ReadString(arguments, "model"),
            Cwd = McpServerSurfaceHelpers.ResolveMcpToolCwd(ReadString(arguments, "cwd")),
            ApprovalPolicy = McpServerSurfaceHelpers.TryReadMcpApprovalPolicy(ReadString(arguments, "approval-policy")),
            Sandbox = McpServerSurfaceHelpers.TryReadMcpSandboxOverride(ReadString(arguments, "sandbox")),
            Config = config,
            BaseInstructions = ReadString(arguments, "base-instructions"),
            DeveloperInstructions = ReadString(arguments, "developer-instructions"),
            SessionSource = KernelSessionSource.AppServer,
        };
    }

    private static KernelConfigOverridePayload? CreateMcpTianShuConfigOverride(
        JsonElement arguments,
        string? profile,
        string? compactPrompt)
    {
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (TryReadJsonProperty(arguments, "config", out var configElement))
        {
            if (configElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("config must be an object");
            }

            var configDictionary = KernelConfigReadLayerUtilities.ConvertJsonObjectToConfigDictionary(configElement);
            foreach (var pair in configDictionary)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(profile))
        {
            merged["profile"] = profile;
        }

        if (!string.IsNullOrWhiteSpace(compactPrompt))
        {
            merged["compact_prompt"] = compactPrompt;
        }

        if (merged.Count == 0)
        {
            return null;
        }

        return KernelConfigOverridePayload.FromElement(JsonSerializer.SerializeToElement(merged));
    }

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? ReadInt(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var numericValue) => numericValue,
            JsonValueKind.String when int.TryParse(property.GetString(), out var stringValue) => stringValue,
            _ => null,
        };
    }

    private static long? ReadLong(JsonElement json, string propertyName)
    {
        if (!json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var numericValue) => numericValue,
            JsonValueKind.String when long.TryParse(property.GetString(), out var stringValue) => stringValue,
            _ => null,
        };
    }

    private static bool TryReadJsonProperty(JsonElement json, string propertyName, out JsonElement value)
    {
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out var property))
        {
            value = property;
            return true;
        }

        value = default;
        return false;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private async Task WaitAndEmitMcpOauthCompletionAsync(string name, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var pollUntil = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < pollUntil && !cancellationToken.IsCancellationRequested)
        {
            var values = await loadEffectiveConfigValuesAsync(cancellationToken).ConfigureAwait(false);
            var authStatus = McpServerAuthUtilities.ResolveMcpServerAuthStatus(name, values);
            if (string.Equals(authStatus, "oauth", StringComparison.OrdinalIgnoreCase)
                || string.Equals(authStatus, "bearer_token", StringComparison.OrdinalIgnoreCase))
            {
                await writeNotificationAsync("mcpServer/oauthLogin/completed", new
                {
                    name,
                    success = true,
                    error = (string?)null,
                }, cancellationToken).ConfigureAwait(false);
                pendingMcpOauthLogins.TryRemove(name, out _);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            await writeNotificationAsync("mcpServer/oauthLogin/completed", new
            {
                name,
                success = false,
                error = "oauth_login_timeout_or_not_completed",
            }, cancellationToken).ConfigureAwait(false);
        }

        if (pendingMcpOauthLogins.TryRemove(name, out var loginCts))
        {
            loginCts.Dispose();
        }
    }
}
