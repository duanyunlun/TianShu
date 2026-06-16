using System.Collections.Concurrent;
using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelPendingInteractiveServerRequest(
    long RequestId,
    string Method,
    string ThreadId,
    string? TurnId,
    string CallId,
    DateTimeOffset RequestedAt,
    JsonElement Params);

internal sealed class KernelPendingServerRequestResolvedException(string message) : Exception(message);

internal sealed class KernelPendingInteractiveReplayAppHostRuntime
{
    private readonly JsonSerializerOptions jsonOptions;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pendingServerResponses;
    private readonly ConcurrentDictionary<string, KernelPendingPermissionRequest> pendingPermissionRequestsByCallId;
    private readonly ConcurrentDictionary<long, KernelPendingInteractiveServerRequest> pendingInteractiveRequestsByRequestId = new();
    private readonly ConcurrentDictionary<string, long> pendingInteractiveRequestIdsByCallId = new(StringComparer.Ordinal);
    private readonly Action<long> cleanupApprovalRequestMapping;
    private readonly Action<long> cleanupPendingUserInputRequestMapping;
    private readonly Action<string, string, KernelPermissionGrantProfile, KernelPermissionGrantScope> recordGrantedPermissions;
    private readonly Func<string, string, bool> hasPendingTurnInterrupt;
    private readonly Func<object, CancellationToken, Task> writeMessageAsync;
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;

    public KernelPendingInteractiveReplayAppHostRuntime(
        JsonSerializerOptions jsonOptions,
        ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pendingServerResponses,
        ConcurrentDictionary<string, KernelPendingPermissionRequest> pendingPermissionRequestsByCallId,
        Action<long> cleanupApprovalRequestMapping,
        Action<long> cleanupPendingUserInputRequestMapping,
        Action<string, string, KernelPermissionGrantProfile, KernelPermissionGrantScope> recordGrantedPermissions,
        Func<string, string, bool> hasPendingTurnInterrupt,
        Func<object, CancellationToken, Task> writeMessageAsync,
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync)
    {
        this.jsonOptions = jsonOptions;
        this.pendingServerResponses = pendingServerResponses;
        this.pendingPermissionRequestsByCallId = pendingPermissionRequestsByCallId;
        this.cleanupApprovalRequestMapping = cleanupApprovalRequestMapping;
        this.cleanupPendingUserInputRequestMapping = cleanupPendingUserInputRequestMapping;
        this.recordGrantedPermissions = recordGrantedPermissions;
        this.hasPendingTurnInterrupt = hasPendingTurnInterrupt;
        this.writeMessageAsync = writeMessageAsync;
        this.writeErrorAsync = writeErrorAsync;
        this.writeResultAsync = writeResultAsync;
        this.writeNotificationAsync = writeNotificationAsync;
    }

    public void TryTrackPendingInteractiveRequest(string method, object @params, string threadId, long requestId)
    {
        if (!KernelPendingInteractiveReplayHelpers.IsPendingInteractiveRequestMethod(method))
        {
            return;
        }

        var serializedParams = JsonSerializer.SerializeToElement(@params, jsonOptions);
        var callId = KernelPendingInteractiveReplayHelpers.TryReadPendingInteractiveCallId(method, serializedParams, requestId);
        var normalizedThreadId = KernelToolJsonHelpers.Normalize(ReadString(serializedParams, "threadId"))
            ?? KernelToolJsonHelpers.Normalize(threadId);
        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        pendingInteractiveRequestsByRequestId[requestId] = new KernelPendingInteractiveServerRequest(
            requestId,
            method,
            normalizedThreadId!,
            KernelToolJsonHelpers.Normalize(ReadString(serializedParams, "turnId")),
            callId!,
            DateTimeOffset.UtcNow,
            serializedParams.Clone());
        pendingInteractiveRequestIdsByCallId[callId!] = requestId;
    }

    public void CleanupPendingInteractiveRequestMapping(long requestId)
    {
        if (!pendingInteractiveRequestsByRequestId.TryRemove(requestId, out var request))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.CallId)
            && pendingInteractiveRequestIdsByCallId.TryGetValue(request.CallId, out var mappedRequestId)
            && mappedRequestId == requestId)
        {
            pendingInteractiveRequestIdsByCallId.TryRemove(request.CallId, out _);
        }
    }

    public async Task ResolvePendingInteractiveRequestsForThreadLifecycleAsync(
        string? threadId,
        string? lifecycleTurnId,
        string lifecyclePhase,
        CancellationToken cancellationToken,
        bool includeLifecycleTurn = true)
    {
        var normalizedThreadId = KernelToolJsonHelpers.Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        var pendingRequests = pendingInteractiveRequestsByRequestId.Values
            .Where(static request => !string.IsNullOrWhiteSpace(request.ThreadId))
            .Where(request => string.Equals(request.ThreadId, normalizedThreadId, StringComparison.Ordinal))
            .Where(request => includeLifecycleTurn || !string.Equals(request.TurnId, lifecycleTurnId, StringComparison.Ordinal))
            .OrderBy(request => request.RequestId)
            .ToArray();

        foreach (var pendingRequest in pendingRequests)
        {
            if (!pendingServerResponses.TryRemove(pendingRequest.RequestId, out var pendingResponse))
            {
                CleanupPendingInteractiveRequestState(pendingRequest);
                continue;
            }

            CleanupPendingInteractiveRequestState(pendingRequest);
            pendingResponse.TrySetException(
                new KernelPendingServerRequestResolvedException(
                    $"pending server request resolved during {lifecyclePhase}: requestId={pendingRequest.RequestId}, threadId={pendingRequest.ThreadId}, turnId={pendingRequest.TurnId ?? string.Empty}, method={pendingRequest.Method}"));

            await writeNotificationAsync("serverRequest/resolved", new
            {
                threadId = pendingRequest.ThreadId,
                requestId = pendingRequest.RequestId,
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> TryResolvePendingInteractiveRequestOnInterruptAsync(long requestId, CancellationToken cancellationToken)
    {
        if (!pendingInteractiveRequestsByRequestId.TryGetValue(requestId, out var pendingRequest)
            || string.IsNullOrWhiteSpace(pendingRequest.ThreadId)
            || string.IsNullOrWhiteSpace(pendingRequest.TurnId)
            || !hasPendingTurnInterrupt(pendingRequest.ThreadId, pendingRequest.TurnId))
        {
            return false;
        }

        _ = pendingServerResponses.TryRemove(requestId, out _);
        CleanupPendingInteractiveRequestState(pendingRequest);
        await writeNotificationAsync("serverRequest/resolved", new
        {
            threadId = pendingRequest.ThreadId,
            requestId,
        }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public object[] BuildPendingInteractiveRequestPayloads(string? threadId)
    {
        var normalizedThreadId = KernelToolJsonHelpers.Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return Array.Empty<object>();
        }

        return pendingInteractiveRequestsByRequestId.Values
            .Where(request => string.Equals(request.ThreadId, normalizedThreadId, StringComparison.Ordinal))
            .OrderBy(request => request.RequestId)
            .Select(static request => BuildPendingInteractiveRequestPayload(request))
            .ToArray();
    }

    public object[] BuildAllPendingInteractiveRequestPayloads()
        => pendingInteractiveRequestsByRequestId.Values
            .OrderBy(static request => request.RequestId)
            .Select(static request => BuildPendingInteractiveRequestPayload(request))
            .ToArray();

    public async Task ReplayPendingInteractiveRequestsAsync(string? threadId, CancellationToken cancellationToken)
    {
        var normalizedThreadId = KernelToolJsonHelpers.Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        var pendingRequests = pendingInteractiveRequestsByRequestId.Values
            .Where(request => string.Equals(request.ThreadId, normalizedThreadId, StringComparison.Ordinal))
            .OrderBy(request => request.RequestId)
            .ToArray();

        foreach (var pendingRequest in pendingRequests)
        {
            if (!pendingServerResponses.ContainsKey(pendingRequest.RequestId))
            {
                CleanupPendingInteractiveRequestState(pendingRequest);
                continue;
            }

            await writeMessageAsync(new Dictionary<string, object?>
            {
                ["id"] = pendingRequest.RequestId,
                ["method"] = pendingRequest.Method,
                ["params"] = pendingRequest.Params.Clone(),
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleServerRequestRespondAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var explicitRequestId = ReadLong(@params, "requestId");
        var callId = KernelToolJsonHelpers.Normalize(ReadString(@params, "callId"))
            ?? KernelToolJsonHelpers.Normalize(ReadString(@params, "approvalId"))
            ?? KernelToolJsonHelpers.Normalize(ReadString(@params, "itemId"));
        if (!TryResolvePendingInteractiveRequestId(explicitRequestId, callId, out var requestId))
        {
            await writeErrorAsync(id, -32004, "未找到待处理的交互请求。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!KernelPendingInteractiveReplayHelpers.TryGetProperty(@params, "result", out var resultElement))
        {
            await writeErrorAsync(id, -32602, "serverRequest/respond 缺少 result。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!pendingServerResponses.TryRemove(requestId, out var pendingResponse))
        {
            CleanupPendingInteractiveRequestMapping(requestId);
            await writeErrorAsync(id, -32004, "交互请求已失效或不存在。", cancellationToken).ConfigureAwait(false);
            return;
        }

        pendingInteractiveRequestsByRequestId.TryGetValue(requestId, out var trackedRequest);
        if (trackedRequest is not null
            && string.Equals(trackedRequest.Method, "item/permissions/requestApproval", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(callId)
            && pendingPermissionRequestsByCallId.TryRemove(callId, out var permissionRequest))
        {
            var scope = KernelPermissionGrantScope.Turn;
            var grantedPermissions = KernelPermissionGrantProfile.Empty;
            if (KernelPermissionGrantProfile.TryParseResponse(resultElement, permissionRequest.Cwd, out var requestedPermissions, out var parsedScope, out _))
            {
                scope = parsedScope;
                grantedPermissions = KernelPermissionGrantProfile.Intersect(permissionRequest.RequestedPermissions, requestedPermissions);
            }

            if (!grantedPermissions.IsEmpty)
            {
                recordGrantedPermissions(permissionRequest.ThreadId, permissionRequest.TurnId, grantedPermissions, scope);
            }

            pendingResponse.TrySetResult(JsonSerializer.SerializeToElement(grantedPermissions.BuildResponsePayload(scope), jsonOptions));
            cleanupApprovalRequestMapping(requestId);
            cleanupPendingUserInputRequestMapping(requestId);
            CleanupPendingInteractiveRequestMapping(requestId);

            await writeResultAsync(id, new
            {
                ok = true,
                requestId,
                callId,
                scope = scope == KernelPermissionGrantScope.Session ? "session" : "turn",
                permissions = grantedPermissions.BuildServerPayload(),
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        pendingResponse.TrySetResult(resultElement.Clone());
        cleanupApprovalRequestMapping(requestId);
        cleanupPendingUserInputRequestMapping(requestId);
        CleanupPendingInteractiveRequestMapping(requestId);

        await writeResultAsync(id, new
        {
            ok = true,
            requestId,
            callId,
        }, cancellationToken).ConfigureAwait(false);
    }

    private void CleanupPendingInteractiveRequestState(KernelPendingInteractiveServerRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.CallId))
        {
            pendingPermissionRequestsByCallId.TryRemove(request.CallId, out _);
        }

        cleanupApprovalRequestMapping(request.RequestId);
        cleanupPendingUserInputRequestMapping(request.RequestId);
        CleanupPendingInteractiveRequestMapping(request.RequestId);
    }

    private bool TryResolvePendingInteractiveRequestId(long? explicitRequestId, string? callId, out long requestId)
    {
        if (explicitRequestId.HasValue)
        {
            requestId = explicitRequestId.Value;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(callId)
            && pendingInteractiveRequestIdsByCallId.TryGetValue(callId, out var mappedRequestId))
        {
            requestId = mappedRequestId;
            return true;
        }

        requestId = 0;
        return false;
    }

    private static object BuildPendingInteractiveRequestPayload(KernelPendingInteractiveServerRequest request)
        => KernelPendingInteractiveReplayHelpers.BuildPendingInteractiveRequestPayload(
            request.RequestId,
            request.Method,
            request.CallId,
            request.ThreadId,
            request.TurnId,
            request.Params,
            request.RequestedAt);

    private static string? ReadString(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                JsonValueKind.Null => null,
                _ => null,
            }
            : null;

    private static long? ReadLong(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.Number when property.TryGetInt64(out var value) => value,
                JsonValueKind.String when long.TryParse(property.GetString(), out var parsed) => parsed,
                _ => null,
            }
            : null;
}
