using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Primitives;

namespace TianShu.AppHost.State;

/// <summary>
/// 提供基于 JSONL 的 rollout 留痕读写能力，仅处理宿主侧持久化模型。
/// Provides JSONL-based rollout persistence and only works with host-side persisted models.
/// </summary>
internal sealed class KernelRolloutRecorder
{
    private const string ArchivedSessionsSubdirectoryName = "archived_sessions";

    private static readonly JsonSerializerOptions JsonLineSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string sessionsDirectoryPath;
    private readonly string archivedSessionsDirectoryPath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, SessionWriteHandle> openWriters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExecutionRequest> executionRequests = new(StringComparer.Ordinal);

    public KernelRolloutRecorder(string rootDirectory)
    {
        sessionsDirectoryPath = Path.Combine(rootDirectory, "sessions");
        archivedSessionsDirectoryPath = Path.Combine(rootDirectory, ArchivedSessionsSubdirectoryName);
    }

    public KernelRolloutRecorder(string sessionsDirectoryPath, string archivedSessionsDirectoryPath)
    {
        this.sessionsDirectoryPath = Path.GetFullPath(sessionsDirectoryPath);
        this.archivedSessionsDirectoryPath = Path.GetFullPath(archivedSessionsDirectoryPath);
    }

    public string SessionsDirectoryPath => sessionsDirectoryPath;

    public string ArchivedSessionsDirectoryPath => archivedSessionsDirectoryPath;

    public string GetRolloutPath(string threadId)
        => Path.Combine(sessionsDirectoryPath, $"{threadId}.jsonl");

    public string GetArchivedRolloutPath(string threadId)
        => Path.Combine(archivedSessionsDirectoryPath, $"{threadId}.jsonl");

    public string ResolveRolloutPath(string threadId, bool archived = false)
    {
        var activePath = GetRolloutPath(threadId);
        var archivedPath = GetArchivedRolloutPath(threadId);
        if (archived)
        {
            if (File.Exists(archivedPath))
            {
                return archivedPath;
            }

            return File.Exists(activePath) ? activePath : archivedPath;
        }

        if (File.Exists(activePath))
        {
            return activePath;
        }

        return File.Exists(archivedPath) ? archivedPath : activePath;
    }

    public bool? TryResolveArchivedState(string threadId)
    {
        var activeExists = File.Exists(GetRolloutPath(threadId));
        var archivedExists = File.Exists(GetArchivedRolloutPath(threadId));
        if (activeExists == archivedExists)
        {
            return null;
        }

        return archivedExists;
    }

    public async Task EnsureSessionMetaAsync(
        string threadId,
        KernelRolloutThreadRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(record);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ResolveExistingRolloutPath(threadId, preferArchived: record.IsArchived);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var writer = GetOrCreateSessionWriterUnsafe(threadId, path);
            if (writer.Stream.Length > 0)
            {
                return;
            }

            await AppendJsonLineUnsafeAsync(
                    writer,
                    BuildSessionPayload("session_meta", record),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendSessionStateAsync(
        string threadId,
        KernelRolloutThreadRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(record);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ResolveExistingRolloutPath(threadId, preferArchived: record.IsArchived);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var writer = GetOrCreateSessionWriterUnsafe(threadId, path);
            await AppendJsonLineUnsafeAsync(
                    writer,
                    BuildSessionPayload("session_state", record),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendCompactionAsync(
        string threadId,
        string turnId,
        IReadOnlyList<JsonElement> replacementHistoryPayloads,
        IReadOnlyList<KernelRolloutTurnRecord> turns,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        ArgumentNullException.ThrowIfNull(replacementHistoryPayloads);
        ArgumentNullException.ThrowIfNull(turns);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ResolveExistingRolloutPath(threadId, preferArchived: false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var writer = GetOrCreateSessionWriterUnsafe(threadId, path);
            await AppendJsonLineUnsafeAsync(
                    writer,
                    new
                    {
                        type = "compacted",
                        threadId,
                        turnId,
                        replacementHistory = ClonePayloadArray(replacementHistoryPayloads),
                        turns = turns.Select(static turn => SerializeTurnPayload(turn)).ToArray(),
                        timestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task AppendTurnResultAsync(
        string threadId,
        string turnId,
        string status,
        string? userMessage,
        string? assistantMessage,
        CancellationToken cancellationToken)
        => AppendTurnResultAsync(
            threadId,
            turnId,
            status,
            userMessage,
            assistantMessage,
            cancellationToken,
            items: null,
            error: null,
            startedAt: null,
            completedAt: null,
            interactionEnvelope: null);

    public async Task AppendTurnResultAsync(
        string threadId,
        string turnId,
        string status,
        string? userMessage,
        string? assistantMessage,
        CancellationToken cancellationToken,
        IReadOnlyList<KernelRolloutTurnItemRecord>? items,
        KernelRolloutTurnErrorRecord? error,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        InteractionEnvelopeRef? interactionEnvelope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ResolveExistingRolloutPath(threadId, preferArchived: false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var now = DateTimeOffset.UtcNow;
            var writer = GetOrCreateSessionWriterUnsafe(threadId, path);
            await AppendJsonLineUnsafeAsync(
                    writer,
                    new
                    {
                        type = "turn",
                        threadId,
                        turnId,
                        status,
                        userMessage,
                        assistantMessage,
                        interactionEnvelope = SerializeInteractionEnvelopePayload(interactionEnvelope),
                        items = items?.Select(static item => item.Payload.Clone()).ToArray(),
                        error = SerializeTurnErrorPayload(error),
                        startedAtUnixMs = (startedAt ?? now).ToUnixTimeMilliseconds(),
                        completedAtUnixMs = (completedAt ?? now).ToUnixTimeMilliseconds(),
                        timestampUnixMs = now.ToUnixTimeMilliseconds(),
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendPendingInputStateAsync(
        string threadId,
        JsonElement? pendingInputStatePayload,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ResolveExistingRolloutPath(threadId, preferArchived: false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var writer = GetOrCreateSessionWriterUnsafe(threadId, path);
            await AppendJsonLineUnsafeAsync(
                    writer,
                    new
                    {
                        type = "pending_input_state",
                        threadId,
                        pendingInputState = ClonePayload(pendingInputStatePayload),
                        timestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendExecutionRequestAsync(
        string threadId,
        ExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(request);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ResolveExistingRolloutPath(threadId, preferArchived: false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var writer = GetOrCreateSessionWriterUnsafe(threadId, path);
            executionRequests[request.ExecutionId.ToString()] = request;
            await AppendJsonLineUnsafeAsync(
                    writer,
                    SerializeExecutionRequestPayload(request),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendExecutionEventAsync(
        string threadId,
        IExecutionEvent @event,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(@event);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ResolveExistingRolloutPath(threadId, preferArchived: false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var writer = GetOrCreateSessionWriterUnsafe(threadId, path);
            executionRequests.TryGetValue(@event.ExecutionId.ToString(), out var request);
            await AppendJsonLineUnsafeAsync(
                    writer,
                    SerializeExecutionEventPayload(threadId, request, @event),
                    cancellationToken)
                .ConfigureAwait(false);
            if (@event.Kind is ExecutionEventKind.Completed or ExecutionEventKind.Failed or ExecutionEventKind.Cancelled)
            {
                executionRequests.Remove(@event.ExecutionId.ToString());
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RewriteThreadSnapshotAsync(KernelRolloutThreadRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = record.IsArchived
                ? GetArchivedRolloutPath(record.ThreadId)
                : GetRolloutPath(record.ThreadId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await CloseThreadWriterUnsafeAsync(record.ThreadId).ConfigureAwait(false);

            await using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                options: FileOptions.Asynchronous);
            using var writer = new StreamWriter(stream, Utf8NoBom, bufferSize: 4096, leaveOpen: true);
            await AppendJsonLineUnsafeAsync(
                    writer,
                    stream,
                    BuildThreadSnapshotPayload(record),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelRolloutThreadRecord?> RehydrateThreadAsync(string rolloutPath, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadThreadFromFileUnsafeAsync(rolloutPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string?> MoveThreadRolloutAsync(
        string threadId,
        bool archived,
        bool bumpLastWriteTime,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CloseThreadWriterUnsafeAsync(threadId).ConfigureAwait(false);

            var sourcePath = archived
                ? GetRolloutPath(threadId)
                : GetArchivedRolloutPath(threadId);
            var destinationPath = archived
                ? GetArchivedRolloutPath(threadId)
                : GetRolloutPath(threadId);
            if (!File.Exists(sourcePath))
            {
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(sourcePath, destinationPath);
            if (bumpLastWriteTime)
            {
                File.SetLastWriteTimeUtc(destinationPath, DateTime.UtcNow);
            }

            return destinationPath;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CloseThreadWriterUnsafeAsync(threadId).ConfigureAwait(false);
            var path = GetRolloutPath(threadId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var archivedPath = GetArchivedRolloutPath(threadId);
            if (File.Exists(archivedPath))
            {
                File.Delete(archivedPath);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ClearThreadsAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var threadIds = new List<string>(openWriters.Keys);
            foreach (var threadId in threadIds)
            {
                await CloseThreadWriterUnsafeAsync(threadId).ConfigureAwait(false);
            }

            DeleteRolloutFilesUnsafe(sessionsDirectoryPath);
            DeleteRolloutFilesUnsafe(archivedSessionsDirectoryPath);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task CloseThreadWriterAsync(string threadId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await CloseThreadWriterUnsafeAsync(threadId).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task CloseAllThreadWritersAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var threadIds = new List<string>(openWriters.Keys);
            foreach (var threadId in threadIds)
            {
                await CloseThreadWriterUnsafeAsync(threadId).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, KernelRolloutThreadRecord>> RehydrateThreadsAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(sessionsDirectoryPath) && !Directory.Exists(archivedSessionsDirectoryPath))
            {
                return new Dictionary<string, KernelRolloutThreadRecord>(StringComparer.Ordinal);
            }

            var threads = new Dictionary<string, KernelRolloutThreadRecord>(StringComparer.Ordinal);
            foreach (var file in EnumerateRolloutFiles())
            {
                var record = await ReadThreadFromFileUnsafeAsync(file, cancellationToken).ConfigureAwait(false);
                if (record is null || string.IsNullOrWhiteSpace(record.ThreadId))
                {
                    continue;
                }

                record.IsArchived = string.Equals(
                    Path.GetDirectoryName(file),
                    archivedSessionsDirectoryPath,
                    StringComparison.OrdinalIgnoreCase);
                threads[record.ThreadId] = record;
            }

            return threads;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<KernelRolloutThreadRecord?> ReadThreadFromFileUnsafeAsync(
        string rolloutPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rolloutPath) || !File.Exists(rolloutPath))
        {
            return null;
        }

        KernelRolloutThreadRecord? record = null;
        foreach (var line in await ReadAllLinesSharedAsync(rolloutPath, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            var type = ReadString(root, "type");
            if (string.Equals(type, "session_meta", StringComparison.Ordinal)
                || string.Equals(type, "session_state", StringComparison.Ordinal))
            {
                var threadId = ReadString(root, "threadId") ?? Path.GetFileNameWithoutExtension(rolloutPath);
                var createdAt = ReadUnixTime(root, "createdAtUnixMs") ?? DateTimeOffset.UtcNow;
                var updatedAt = ReadUnixTime(root, "updatedAtUnixMs") ?? createdAt;
                record ??= new KernelRolloutThreadRecord
                {
                    ThreadId = threadId,
                    Cwd = ReadString(root, "cwd"),
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                };

                record.Cwd = ReadString(root, "cwd") ?? record.Cwd;
                record.ForkedFromThreadId = ReadString(root, "forkedFromId") ?? record.ForkedFromThreadId;
                record.CreatedAt = record.CreatedAt == default ? createdAt : record.CreatedAt;
                if (updatedAt > record.UpdatedAt)
                {
                    record.UpdatedAt = updatedAt;
                }

                if (root.TryGetProperty("configSnapshot", out var configSnapshotElement))
                {
                    record.ConfigSnapshotPayload = configSnapshotElement.Clone();
                }

                if (root.TryGetProperty("pendingInputState", out var pendingInputStateElement))
                {
                    record.PendingInputStatePayload = pendingInputStateElement.Clone();
                }

                if (root.TryGetProperty("sessionState", out var sessionStateElement))
                {
                    record.SessionState = ReadSessionState(sessionStateElement);
                }

                if (root.TryGetProperty("seedHistory", out _))
                {
                    record.SeedHistoryPayloads = ReadPayloadArray(root, "seedHistory");
                }

                if (root.TryGetProperty("turns", out _))
                {
                    record.Turns = ReadTurns(root, "turns", updatedAt);
                }

                continue;
            }

            if (string.Equals(type, "pending_input_state", StringComparison.Ordinal))
            {
                record ??= CreateFallbackRecord(root, rolloutPath);
                record.PendingInputStatePayload = ReadPayload(root, "pendingInputState");
                var pendingInputTimestamp = ReadUnixTime(root, "timestampUnixMs");
                if (pendingInputTimestamp.HasValue && pendingInputTimestamp.Value > record.UpdatedAt)
                {
                    record.UpdatedAt = pendingInputTimestamp.Value;
                }

                continue;
            }

            if (string.Equals(type, "compacted", StringComparison.Ordinal))
            {
                record ??= CreateFallbackRecord(root, rolloutPath);
                var compactionTimestamp = ReadUnixTime(root, "timestampUnixMs") ?? DateTimeOffset.UtcNow;
                record.SeedHistoryPayloads = ReadPayloadArray(root, "replacementHistory");
                var turns = ReadTurns(root, "turns", compactionTimestamp);
                if (turns.Count == 0)
                {
                    turns.Add(new KernelRolloutTurnRecord
                    {
                        Id = ReadString(root, "turnId") ?? $"compact_{compactionTimestamp.ToUnixTimeMilliseconds():x}",
                        StartedAt = compactionTimestamp,
                        CompletedAt = compactionTimestamp,
                        Status = "completed",
                        IsContextCompaction = true,
                    });
                }

                record.Turns = turns;
                record.UpdatedAt = compactionTimestamp;
                continue;
            }

            if (!string.Equals(type, "turn", StringComparison.Ordinal))
            {
                continue;
            }

            record ??= CreateFallbackRecord(root, rolloutPath);
            var timestamp = ReadUnixTime(root, "timestampUnixMs") ?? DateTimeOffset.UtcNow;
            UpsertTurn(record.Turns, ReadTurn(root, timestamp));
            if (record.CreatedAt == default)
            {
                record.CreatedAt = timestamp;
            }

            if (timestamp > record.UpdatedAt)
            {
                record.UpdatedAt = timestamp;
            }
        }

        if (record is null)
        {
            return null;
        }

        RefreshRecord(record);
        return record;
    }

    private IEnumerable<string> EnumerateRolloutFiles()
    {
        if (Directory.Exists(sessionsDirectoryPath))
        {
            foreach (var file in Directory.EnumerateFiles(sessionsDirectoryPath, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }
        }

        if (Directory.Exists(archivedSessionsDirectoryPath))
        {
            foreach (var file in Directory.EnumerateFiles(archivedSessionsDirectoryPath, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }
        }
    }

    private static void DeleteRolloutFilesUnsafe(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.jsonl", SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
        }
    }

    private static void RefreshRecord(KernelRolloutThreadRecord record)
    {
        var lastTurn = record.Turns.LastOrDefault();
        if (record.CreatedAt == default)
        {
            record.CreatedAt = record.UpdatedAt == default
                ? DateTimeOffset.UtcNow
                : record.UpdatedAt;
        }

        if (record.UpdatedAt == default)
        {
            record.UpdatedAt = lastTurn is null
                ? record.CreatedAt
                : IsTerminalTurnStatus(lastTurn.Status)
                    ? lastTurn.CompletedAt
                    : lastTurn.StartedAt;
        }
    }

    private static bool IsTerminalTurnStatus(string? status)
        => string.Equals(ReadNormalizedStatus(status), "completed", StringComparison.Ordinal)
           || string.Equals(ReadNormalizedStatus(status), "failed", StringComparison.Ordinal)
           || string.Equals(ReadNormalizedStatus(status), "interrupted", StringComparison.Ordinal);

    private static string? ReadNormalizedStatus(string? status)
        => string.IsNullOrWhiteSpace(status) ? null : status.Trim();

    private static void UpsertTurn(List<KernelRolloutTurnRecord> turns, KernelRolloutTurnRecord turn)
    {
        var index = turns.FindIndex(existing => string.Equals(existing.Id, turn.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            turns[index] = turn;
            return;
        }

        turns.Add(turn);
    }

    private static KernelRolloutThreadRecord CreateFallbackRecord(JsonElement root, string rolloutPath)
    {
        var timestamp = ReadUnixTime(root, "timestampUnixMs") ?? DateTimeOffset.UtcNow;
        return new KernelRolloutThreadRecord
        {
            ThreadId = ReadString(root, "threadId") ?? Path.GetFileNameWithoutExtension(rolloutPath),
            ForkedFromThreadId = ReadString(root, "forkedFromId"),
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
    }

    private string ResolveExistingRolloutPath(string threadId, bool preferArchived)
    {
        var activePath = GetRolloutPath(threadId);
        var archivedPath = GetArchivedRolloutPath(threadId);
        if (preferArchived)
        {
            if (File.Exists(archivedPath))
            {
                return archivedPath;
            }

            return File.Exists(activePath) ? activePath : archivedPath;
        }

        if (File.Exists(activePath))
        {
            return activePath;
        }

        return File.Exists(archivedPath) ? archivedPath : activePath;
    }

    private static object BuildSessionPayload(string eventType, KernelRolloutThreadRecord record)
        => new
        {
            type = eventType,
            threadId = record.ThreadId,
            cwd = record.Cwd,
            forkedFromId = record.ForkedFromThreadId,
            createdAtUnixMs = record.CreatedAt.ToUnixTimeMilliseconds(),
            updatedAtUnixMs = record.UpdatedAt.ToUnixTimeMilliseconds(),
            configSnapshot = ClonePayload(record.ConfigSnapshotPayload),
            pendingInputState = ClonePayload(record.PendingInputStatePayload),
            sessionState = SerializeSessionStatePayload(record.SessionState),
            seedHistory = ClonePayloadArray(record.SeedHistoryPayloads),
        };

    private static object BuildThreadSnapshotPayload(KernelRolloutThreadRecord record)
        => new
        {
            type = "session_meta",
            threadId = record.ThreadId,
            cwd = record.Cwd,
            forkedFromId = record.ForkedFromThreadId,
            createdAtUnixMs = record.CreatedAt.ToUnixTimeMilliseconds(),
            updatedAtUnixMs = record.UpdatedAt.ToUnixTimeMilliseconds(),
            configSnapshot = ClonePayload(record.ConfigSnapshotPayload),
            pendingInputState = ClonePayload(record.PendingInputStatePayload),
            sessionState = SerializeSessionStatePayload(record.SessionState),
            seedHistory = ClonePayloadArray(record.SeedHistoryPayloads),
            turns = record.Turns.Select(static turn => SerializeTurnPayload(turn)).ToArray(),
        };

    private static object? SerializeSessionStatePayload(KernelRolloutSessionProjectionStateRecord? state)
    {
        if (state is null)
        {
            return null;
        }

        return new
        {
            sessionId = state.SessionId,
            title = state.Title,
            collaborationSpaceId = state.CollaborationSpaceId,
            collaborationSpaceKey = state.CollaborationSpaceKey,
            collaborationSpaceDisplayName = state.CollaborationSpaceDisplayName,
            sessionMode = state.SessionMode,
            isClosed = state.IsClosed,
            activeThreadId = state.ActiveThreadId,
            hasActiveTurn = state.HasActiveTurn,
            orchestration = SerializeOrchestrationStatePayload(state.Orchestration),
        };
    }

    private static object? SerializeOrchestrationStatePayload(KernelRolloutOrchestrationStateRecord? state)
        => state is null
            ? null
            : JsonSerializer.SerializeToElement(state, JsonLineSerializerOptions);

    private static KernelRolloutSessionProjectionStateRecord? ReadSessionState(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return new KernelRolloutSessionProjectionStateRecord
        {
            SessionId = ReadString(element, "sessionId") ?? string.Empty,
            Title = ReadString(element, "title") ?? string.Empty,
            CollaborationSpaceId = ReadString(element, "collaborationSpaceId") ?? "tianshu-runtime",
            CollaborationSpaceKey = ReadString(element, "collaborationSpaceKey") ?? "tianshu-runtime",
            CollaborationSpaceDisplayName = ReadString(element, "collaborationSpaceDisplayName") ?? "TianShu Runtime",
            SessionMode = ReadString(element, "sessionMode") ?? "interactive",
            IsClosed = ReadBoolean(element, "isClosed") ?? false,
            ActiveThreadId = ReadString(element, "activeThreadId"),
            HasActiveTurn = ReadBoolean(element, "hasActiveTurn") ?? false,
            Orchestration = ReadOrchestrationState(element),
        };
    }

    private static KernelRolloutOrchestrationStateRecord? ReadOrchestrationState(JsonElement element)
    {
        if (!element.TryGetProperty("orchestration", out var orchestration)
            || orchestration.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<KernelRolloutOrchestrationStateRecord>(
                orchestration.GetRawText(),
                JsonLineSerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static object SerializeTurnPayload(KernelRolloutTurnRecord turn)
        => new
        {
            turnId = turn.Id,
            status = turn.Status,
            userMessage = turn.UserMessage,
            assistantMessage = turn.AssistantMessage,
            interactionEnvelope = SerializeInteractionEnvelopePayload(turn.InteractionEnvelope),
            items = turn.Items.Select(static item => item.Payload.Clone()).ToArray(),
            error = SerializeTurnErrorPayload(turn.Error),
            startedAtUnixMs = turn.StartedAt.ToUnixTimeMilliseconds(),
            completedAtUnixMs = turn.CompletedAt.ToUnixTimeMilliseconds(),
            isContextCompaction = turn.IsContextCompaction,
        };

    private static object? SerializeTurnErrorPayload(KernelRolloutTurnErrorRecord? error)
    {
        if (error is null)
        {
            return null;
        }

        return new
        {
            message = error.Message,
            additionalDetails = error.AdditionalDetails,
        };
    }

    private static object SerializeExecutionRequestPayload(ExecutionRequest request)
        => new
        {
            type = "execution_request",
            executionId = request.ExecutionId.ToString(),
            executionKind = ResolveExecutionKind(request),
            action = request.Action,
            threadId = request.Context.ThreadId?.ToString(),
            turnId = request.Context.TurnId?.ToString(),
            itemId = TryGetMetadataString(request.Context.Metadata, "itemId"),
            workingDirectory = request.Context.WorkingDirectory,
            createdAtUnixMs = request.CreatedAt.ToUnixTimeMilliseconds(),
            input = request.Input?.ToPlainObject(),
            timestampUnixMs = request.CreatedAt.ToUnixTimeMilliseconds(),
        };

    private static object SerializeExecutionEventPayload(string threadId, ExecutionRequest? request, IExecutionEvent @event)
        => new
        {
            type = "execution_event",
            executionId = @event.ExecutionId.ToString(),
            executionKind = request is null ? null : ResolveExecutionKind(request),
            eventKind = @event.Kind.ToString(),
            action = request?.Action,
            threadId = request?.Context.ThreadId?.ToString() ?? threadId,
            turnId = request?.Context.TurnId?.ToString(),
            itemId = request is null ? null : TryGetMetadataString(request.Context.Metadata, "itemId"),
            message = @event.Message,
            success = ResolveExecutionSuccess(@event.Kind),
            data = @event.Data?.ToPlainObject(),
            timestampUnixMs = @event.Timestamp.ToUnixTimeMilliseconds(),
        };

    private static string ResolveExecutionKind(ExecutionRequest request)
        => TryGetMetadataString(request.Context.Metadata, "executionKind") ?? request.Kind.ToString();

    private static string? TryGetMetadataString(MetadataBag metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.Kind == StructuredValueKind.String
            ? value.StringValue
            : value.ToPlainObject()?.ToString();
    }

    private static bool? ResolveExecutionSuccess(ExecutionEventKind kind)
        => kind switch
        {
            ExecutionEventKind.Completed => true,
            ExecutionEventKind.Failed => false,
            _ => null,
        };

    private SessionWriteHandle GetOrCreateSessionWriterUnsafe(string threadId, string path)
    {
        if (openWriters.TryGetValue(threadId, out var existing))
        {
            return existing;
        }

        var stream = new FileStream(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite,
            bufferSize: 4096,
            options: FileOptions.Asynchronous);
        stream.Seek(0, SeekOrigin.End);
        var writer = new StreamWriter(stream, Utf8NoBom, bufferSize: 4096, leaveOpen: true);
        var handle = new SessionWriteHandle(stream, writer);
        openWriters[threadId] = handle;
        return handle;
    }

    private async Task CloseThreadWriterUnsafeAsync(string threadId)
    {
        if (!openWriters.Remove(threadId, out var handle))
        {
            return;
        }

        await handle.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task AppendJsonLineUnsafeAsync(
        SessionWriteHandle handle,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonLineSerializerOptions);
        await handle.Writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await handle.Writer.WriteAsync(Environment.NewLine.AsMemory(), cancellationToken).ConfigureAwait(false);
        await handle.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        await handle.Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task AppendJsonLineUnsafeAsync(
        StreamWriter writer,
        FileStream stream,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonLineSerializerOptions);
        await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(Environment.NewLine.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string[]> ReadAllLinesSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);
        var lines = new List<string>();
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            lines.Add(line);
        }

        return lines.ToArray();
    }

    private static JsonElement? ClonePayload(JsonElement? payload)
        => payload is null ? null : payload.Value.Clone();

    private static JsonElement? ReadPayload(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.Clone();
    }

    private static JsonElement[] ClonePayloadArray(IReadOnlyList<JsonElement> payloads)
        => payloads.Count == 0
            ? []
            : payloads.Select(static payload => payload.Clone()).ToArray();

    private static List<JsonElement> ReadPayloadArray(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<JsonElement>();
        foreach (var item in property.EnumerateArray())
        {
            list.Add(item.Clone());
        }

        return list;
    }

    private static List<KernelRolloutTurnRecord> ReadTurns(
        JsonElement json,
        string propertyName,
        DateTimeOffset fallbackTimestamp)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<KernelRolloutTurnRecord>();
        foreach (var item in property.EnumerateArray())
        {
            list.Add(ReadTurn(item, fallbackTimestamp));
        }

        return list;
    }

    private static KernelRolloutTurnRecord ReadTurn(JsonElement json, DateTimeOffset fallbackTimestamp)
    {
        var startedAt = ReadUnixTime(json, "startedAtUnixMs") ?? fallbackTimestamp;
        var completedAt = ReadUnixTime(json, "completedAtUnixMs") ?? startedAt;
        return new KernelRolloutTurnRecord
        {
            Id = ReadString(json, "turnId") ?? $"turn_{startedAt.ToUnixTimeMilliseconds():x}",
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Status = ReadString(json, "status") ?? "completed",
            UserMessage = ReadString(json, "userMessage"),
            AssistantMessage = ReadString(json, "assistantMessage"),
            InteractionEnvelope = ReadInteractionEnvelope(json, "interactionEnvelope"),
            Items = ReadTurnItems(json, "items"),
            Error = ReadTurnError(json, "error"),
            IsContextCompaction = json.TryGetProperty("isContextCompaction", out var isContextCompaction)
                && isContextCompaction.ValueKind == JsonValueKind.True,
        };
    }

    private static List<KernelRolloutTurnItemRecord> ReadTurnItems(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<KernelRolloutTurnItemRecord>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var payload = item.Clone();
            var itemId = ReadString(payload, "id") ?? string.Empty;
            var itemType = ReadString(payload, "type") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(itemType))
            {
                continue;
            }

            items.Add(new KernelRolloutTurnItemRecord
            {
                Id = itemId,
                Type = itemType,
                Payload = payload,
            });
        }

        return items;
    }

    private static KernelRolloutTurnErrorRecord? ReadTurnError(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var message = ReadString(property, "message");
        var additionalDetails = ReadString(property, "additionalDetails");
        if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(additionalDetails))
        {
            return null;
        }

        return new KernelRolloutTurnErrorRecord
        {
            Message = message ?? string.Empty,
            AdditionalDetails = additionalDetails,
        };
    }

    private static object? SerializeInteractionEnvelopePayload(InteractionEnvelopeRef? interactionEnvelope)
    {
        if (interactionEnvelope is null)
        {
            return null;
        }

        return new
        {
            id = interactionEnvelope.Id.ToString(),
            sourceKind = interactionEnvelope.SourceKind.ToString(),
            surface = interactionEnvelope.Surface,
            createdAtUnixMs = interactionEnvelope.CreatedAt.ToUnixTimeMilliseconds(),
        };
    }

    private static InteractionEnvelopeRef? ReadInteractionEnvelope(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = ReadString(property, "id");
        var surface = ReadString(property, "surface");
        if (string.IsNullOrWhiteSpace(id)
            || string.IsNullOrWhiteSpace(surface)
            || !TryReadInteractionSourceKind(property, "sourceKind", out var sourceKind))
        {
            return null;
        }

        var createdAt = ReadUnixTime(property, "createdAtUnixMs") ?? DateTimeOffset.UtcNow;
        return new InteractionEnvelopeRef(
            new InteractionEnvelopeId(id),
            sourceKind,
            surface,
            createdAt);
    }

    private static bool TryReadInteractionSourceKind(
        JsonElement json,
        string propertyName,
        out InteractionSourceKind sourceKind)
    {
        var raw = ReadString(json, propertyName);
        if (string.IsNullOrWhiteSpace(raw)
            || !Enum.TryParse(raw, ignoreCase: true, out sourceKind))
        {
            sourceKind = default;
            return false;
        }

        return true;
    }

    private sealed class SessionWriteHandle : IAsyncDisposable
    {
        public SessionWriteHandle(FileStream stream, StreamWriter writer)
        {
            Stream = stream;
            Writer = writer;
        }

        public FileStream Stream { get; }

        public StreamWriter Writer { get; }

        public async ValueTask DisposeAsync()
        {
            await Writer.FlushAsync().ConfigureAwait(false);
            Writer.Dispose();
            await Stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Null => null,
            _ => property.GetRawText(),
        };
    }

    private static DateTimeOffset? ReadUnixTime(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value);
        }

        return null;
    }

    private static bool? ReadBoolean(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}
