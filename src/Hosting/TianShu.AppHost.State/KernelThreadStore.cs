using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using TianShu.Contracts.Memory;
using TianShu.Execution.Runtime;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;

namespace TianShu.AppHost.State;

internal sealed record KernelThreadListQuery(
    int Limit,
    string? Cursor,
    string? Cwd,
    bool Archived,
    string SortKey,
    IReadOnlyList<string>? ModelProviders,
    IReadOnlyList<KernelThreadSourceKind> SourceKinds,
    string? SearchTerm,
    string DefaultModelProvider,
    KernelSessionSource DefaultSource);

internal sealed record KernelThreadListPage(
    IReadOnlyList<KernelThreadRecord> Data,
    string? NextCursor);

internal sealed class KernelThreadStore
{
    private const string DefaultSessionCollaborationSpaceId = "tianshu-runtime";
    private const string DefaultSessionCollaborationSpaceKey = "tianshu-runtime";
    private const string DefaultSessionCollaborationSpaceDisplayName = "TianShu Runtime";
    private const string InteractiveSessionMode = "interactive";
    private const string PlanningSessionMode = "planning";

    private static readonly StringComparer GuardStatePathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly ConcurrentDictionary<string, KernelSpawnAgentGuardState> SharedSpawnAgentGuardStates = new(GuardStatePathComparer);

    private readonly string filePath;
    private readonly KernelStateSqliteStore stateStore;
    private readonly KernelRolloutRecorder rolloutRecorder;
    private readonly KernelSpawnAgentGuardState spawnAgentGuardState;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private Dictionary<string, KernelThreadRecord> threads = new(StringComparer.Ordinal);
    private readonly HashSet<string> ephemeralThreadIds = new(StringComparer.Ordinal);
    private bool initialized;

    internal KernelStateSqliteStore StateStore => stateStore;

    internal KernelRolloutRecorder RolloutRecorder => rolloutRecorder;

    internal KernelSpawnAgentGuardState SpawnAgentGuardState => spawnAgentGuardState;

    internal string FilePath => filePath;

    public KernelThreadStore(
        string filePath,
        string? sessionsDirectoryPath = null,
        string? archivedSessionsDirectoryPath = null)
    {
        this.filePath = Path.GetFullPath(filePath);
        serializerOptions.Converters.Add(new ThreadStoreInteractionEnvelopeRefJsonConverter());
        var directory = Path.GetDirectoryName(this.filePath) ?? Environment.CurrentDirectory;
        stateStore = new KernelStateSqliteStore(Path.Combine(directory, "state.db"));
        rolloutRecorder = !string.IsNullOrWhiteSpace(sessionsDirectoryPath)
            ? new KernelRolloutRecorder(
                sessionsDirectoryPath,
                string.IsNullOrWhiteSpace(archivedSessionsDirectoryPath)
                    ? Path.Combine(sessionsDirectoryPath, "archived")
                    : archivedSessionsDirectoryPath)
            : new KernelRolloutRecorder(directory);
        spawnAgentGuardState = SharedSpawnAgentGuardStates.GetOrAdd(
            this.filePath,
            static _ => new KernelSpawnAgentGuardState());
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await stateStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(filePath))
            {
                threads = (await rolloutRecorder.RehydrateThreadsAsync(cancellationToken).ConfigureAwait(false))
                    .ToDictionary(
                        x => x.Key,
                        x => NormalizeRecord(KernelRolloutStateMapper.FromRolloutThreadRecord(x.Value)),
                        StringComparer.Ordinal);
                foreach (var record in threads.Values)
                {
                    await stateStore.UpsertThreadAsync(BuildStoredThreadStateRecord(record), cancellationToken).ConfigureAwait(false);
                }

                if (threads.Count > 0)
                {
                    await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
                }

                initialized = true;
                return;
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var list = JsonSerializer.Deserialize<List<KernelThreadRecord>>(json, serializerOptions) ?? [];
                threads = list
                    .Select(NormalizeRecord)
                    .Where(static x => !string.IsNullOrWhiteSpace(x.Id))
                    .GroupBy(x => x.Id, StringComparer.Ordinal)
                    .Select(x => x.OrderByDescending(y => y.UpdatedAt).First())
                    .ToDictionary(x => x.Id, StringComparer.Ordinal);
                foreach (var record in threads.Values)
                {
                    await stateStore.UpsertThreadAsync(BuildStoredThreadStateRecord(record), cancellationToken).ConfigureAwait(false);
                }
            }

            initialized = true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord> CreateThreadAsync(string id, string? cwd, CancellationToken cancellationToken, bool ephemeral = false)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var record = new KernelThreadRecord
            {
                Id = id,
                Cwd = Normalize(cwd),
                CreatedAt = now,
                UpdatedAt = now,
                MemoryMode = "enabled",
                StatusType = "idle",
                Turns = [],
                SeedHistory = [],
                ActiveFlags = [],
                PendingInputState = null,
            };

            threads[id] = record;
            SetEphemeralThreadStateUnsafe(id, ephemeral);
            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord> UpsertThreadAsync(KernelThreadRecord source, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = NormalizeRecord(Clone(source));
            record.UpdatedAt = record.UpdatedAt == default ? DateTimeOffset.UtcNow : record.UpdatedAt;
            record.CreatedAt = record.CreatedAt == default ? record.UpdatedAt : record.CreatedAt;
            threads[record.Id] = record;
            SetEphemeralThreadStateUnsafe(record.Id, IsEphemeralThreadUnsafe(record.Id, record));
            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> ApplySessionOrchestrationStepAsync(
        string threadId,
        OrchestratorDecision decision,
        StageContextPackage contextPackage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(contextPackage);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(threadId, out var record))
            {
                return null;
            }

            record.SessionState = NormalizeSessionState(record.SessionState, record);
            record.SessionState.Orchestration = KernelThreadOrchestrationStateNormalizer.ApplyOrchestrationStep(
                record.SessionState.Orchestration,
                decision,
                contextPackage);
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> AppendStageCheckpointAsync(
        string threadId,
        StageCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(threadId, out var record))
            {
                return null;
            }

            record.SessionState = NormalizeSessionState(record.SessionState, record);
            record.SessionState.Orchestration = KernelThreadOrchestrationStateNormalizer.ApplyCheckpoint(
                record.SessionState.Orchestration,
                checkpoint);
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 写入终态 turn 投影 checkpoint。
    /// Appends a terminal turn projection checkpoint.
    /// </summary>
    public Task<KernelThreadRecord?> AppendTerminalTurnProjectionAsync(
        string threadId,
        StageCheckpoint checkpoint,
        CancellationToken cancellationToken)
        => AppendStageCheckpointAsync(threadId, checkpoint, cancellationToken);

    public async Task<KernelThreadRecord?> ForkThreadAsync(
        string sourceThreadId,
        string newThreadId,
        string? cwdOverride,
        CancellationToken cancellationToken,
        bool ephemeral = false,
        KernelTurnRecord? liveTurn = null)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(sourceThreadId, out var source))
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            var record = new KernelThreadRecord
            {
                Id = newThreadId,
                Cwd = Normalize(cwdOverride) ?? source.Cwd,
                CreatedAt = now,
                UpdatedAt = now,
                MemoryMode = "enabled",
                ForkedFromThreadId = sourceThreadId,
                LastUserMessage = source.LastUserMessage,
                LastAssistantMessage = source.LastAssistantMessage,
                Name = null,
                IsArchived = false,
                StatusType = "idle",
                ActiveFlags = [],
                GitInfo = CloneGitInfo(source.GitInfo),
                Turns = BuildForkTurns(source.Turns, liveTurn),
                SeedHistory = source.SeedHistory.Select(CloneConversationHistory).ToList(),
                ConfigSnapshot = CloneConfigSnapshot(source.ConfigSnapshot, ephemeral),
                PendingInputState = null,
            };

            threads[newThreadId] = record;
            SetEphemeralThreadStateUnsafe(newThreadId, ephemeral);
            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<KernelThreadRecord>> ListThreadsAsync(
        int limit,
        string? cwd,
        bool? archived,
        CancellationToken cancellationToken)
    {
        var page = await ListThreadsAsync(
                new KernelThreadListQuery(
                    Limit: limit,
                    Cursor: null,
                    Cwd: cwd,
                    Archived: archived ?? false,
                    SortKey: "updated_at",
                    ModelProviders: Array.Empty<string>(),
                    SourceKinds: Array.Empty<KernelThreadSourceKind>(),
                    SearchTerm: null,
                    DefaultModelProvider: "openai",
                    DefaultSource: KernelSessionSource.VsCode),
                cancellationToken)
            .ConfigureAwait(false);
        return page.Data;
    }

    public async Task<KernelThreadListPage> ListThreadsAsync(KernelThreadListQuery request, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IEnumerable<KernelThreadRecord> records = threads.Values
                .Where(item => !IsEphemeralThreadUnsafe(item.Id, item))
                .Select(item =>
                {
                    var clone = Clone(item);
                    clone.IsArchived = ResolveThreadArchivedState(item);
                    if (clone.IsArchived)
                    {
                        clone.StatusType = "notLoaded";
                        clone.ActiveFlags = [];
                    }

                    return clone;
                });

            if (request.Archived)
            {
                records = records.Where(static x => x.IsArchived);
            }
            else
            {
                records = records.Where(static x => !x.IsArchived);
            }

            var normalizedCwd = Normalize(request.Cwd);
            if (normalizedCwd is not null)
            {
                records = records.Where(x => string.Equals(Normalize(x.Cwd), normalizedCwd, StringComparison.OrdinalIgnoreCase));
            }

            var normalizedDefaultModelProvider = Normalize(request.DefaultModelProvider);
            var normalizedModelProviders = request.ModelProviders?
                .Select(Normalize)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (normalizedModelProviders is { Count: > 0 })
            {
                records = records.Where(item => normalizedModelProviders.Contains(Normalize(item.ConfigSnapshot?.ModelProviderId) ?? normalizedDefaultModelProvider ?? string.Empty));
            }
            else if (request.ModelProviders is null && !string.IsNullOrWhiteSpace(normalizedDefaultModelProvider))
            {
                records = records.Where(item => string.Equals(
                    Normalize(item.ConfigSnapshot?.ModelProviderId) ?? normalizedDefaultModelProvider,
                    normalizedDefaultModelProvider,
                    StringComparison.OrdinalIgnoreCase));
            }

            var normalizedSourceKinds = request.SourceKinds
                .Where(static value => value is not null)
                .Distinct()
                .ToArray();
            if (normalizedSourceKinds.Length > 0)
            {
                records = records.Where(item =>
                {
                    var effectiveSource = item.ConfigSnapshot?.SessionSource ?? request.DefaultSource;
                    return normalizedSourceKinds.Any(effectiveSource.Matches);
                });
            }

            var searchTerm = Normalize(request.SearchTerm);
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                records = records.Where(item => MatchesSearchTerm(item, searchTerm!));
            }

            var sortKey = string.Equals(Normalize(request.SortKey), "updated_at", StringComparison.OrdinalIgnoreCase)
                ? "updated_at"
                : "created_at";
            var orderedList = records.ToList();
            if (sortKey == "updated_at")
            {
                foreach (var item in orderedList)
                {
                    item.UpdatedAt = ResolveThreadListUpdatedAt(item);
                }
            }

            orderedList = (sortKey == "updated_at"
                    ? orderedList.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id, StringComparer.Ordinal)
                    : orderedList.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id, StringComparer.Ordinal))
                .ToList();
            var startIndex = 0;
            var normalizedCursor = Normalize(request.Cursor);
            if (!string.IsNullOrWhiteSpace(normalizedCursor))
            {
                if (!TryDecodeThreadCursor(normalizedCursor!, out var cursorThreadId))
                {
                    throw new InvalidOperationException($"invalid cursor: {request.Cursor}");
                }

                var anchorIndex = orderedList.FindIndex(item => string.Equals(item.Id, cursorThreadId, StringComparison.Ordinal));
                if (anchorIndex < 0)
                {
                    throw new InvalidOperationException($"invalid cursor: {request.Cursor}");
                }

                startIndex = anchorIndex + 1;
            }

            var page = orderedList
                .Skip(startIndex)
                .Take(Math.Clamp(request.Limit, 1, 200))
                .ToList();
            var nextCursor = startIndex + page.Count < orderedList.Count && page.Count > 0
                ? EncodeThreadCursor(page[^1].Id)
                : null;

            return new KernelThreadListPage(page, nextCursor);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> GetThreadAsync(string id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return threads.TryGetValue(id, out var record) ? Clone(record) : null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ThreadMemoryMode?> GetThreadMemoryModeAsync(string id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return threads.TryGetValue(id, out var record)
                ? KernelThreadMemoryModeMapper.ToThreadMemoryMode(record.MemoryMode, IsEphemeralThreadUnsafe(id, record))
                : null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> DeleteThreadAsync(string id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.Remove(id, out var record))
            {
                return null;
            }

            var isEphemeral = IsEphemeralThreadUnsafe(id, record);
            ephemeralThreadIds.Remove(id);
            if (!isEphemeral)
            {
                await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
                await stateStore.DeleteThreadAsync(id, cancellationToken).ConfigureAwait(false);
                await rolloutRecorder.DeleteThreadAsync(id, cancellationToken).ConfigureAwait(false);
            }

            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> ClearThreadsAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var deletedCount = threads.Count;
            threads.Clear();
            ephemeralThreadIds.Clear();
            await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
            await stateStore.ClearThreadsAsync(cancellationToken).ConfigureAwait(false);
            await rolloutRecorder.ClearThreadsAsync(cancellationToken).ConfigureAwait(false);
            return deletedCount;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> SetSeedHistoryAsync(
        string id,
        IReadOnlyList<KernelConversationHistoryItem> history,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(id, out var record))
            {
                return null;
            }

            record.SeedHistory = history
                .Select(KernelConversationHistoryUtilities.Clone)
                .Where(KernelConversationHistoryUtilities.HasReplayablePayload)
                .ToList();
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }
    public async Task<KernelThreadRecord?> SetThreadStatusAsync(
        string id,
        string statusType,
        IReadOnlyList<string>? activeFlags,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(id, out var record))
            {
                return null;
            }

            record.StatusType = NormalizeStatus(statusType);
            record.ActiveFlags = NormalizeActiveFlags(activeFlags);
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> UpsertActiveTurnAsync(
        string id,
        KernelTurnRecord activeTurn,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activeTurn);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(id, out var record))
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            var turn = record.Turns.LastOrDefault(existing => string.Equals(existing.Id, activeTurn.Id, StringComparison.Ordinal));
            if (turn is null)
            {
                turn = new KernelTurnRecord
                {
                    Id = Normalize(activeTurn.Id) ?? string.Empty,
                    StartedAt = activeTurn.StartedAt == default ? now : activeTurn.StartedAt,
                };
                record.Turns.Add(turn);
            }

            turn.StartedAt = activeTurn.StartedAt == default ? (turn.StartedAt == default ? now : turn.StartedAt) : activeTurn.StartedAt;
            turn.CompletedAt = activeTurn.CompletedAt == default ? turn.StartedAt : activeTurn.CompletedAt;
            turn.Status = NormalizeTurnStatus(activeTurn.Status);
            turn.UserMessage = Normalize(activeTurn.UserMessage) ?? turn.UserMessage;
            turn.AssistantMessage = Normalize(activeTurn.AssistantMessage) ?? turn.AssistantMessage;
            turn.InteractionEnvelope = activeTurn.InteractionEnvelope ?? turn.InteractionEnvelope;
            turn.Error = CloneTurnError(activeTurn.Error);
            turn.IsContextCompaction = activeTurn.IsContextCompaction;
            turn.Items = (activeTurn.Items ?? []).Select(CloneTurnItem).ToList();

            record.StatusType = "active";
            record.ActiveFlags = [];
            record.UpdatedAt = now;
            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> SetThreadArchivedAsync(
        string id,
        bool archived,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(id, out var record))
            {
                return null;
            }

            var isEphemeral = IsEphemeralThreadUnsafe(id, record);
            if (!isEphemeral)
            {
                if (archived)
                {
                    _ = await rolloutRecorder.MoveThreadRolloutAsync(
                            id,
                            archived: true,
                            bumpLastWriteTime: false,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    var activePath = await rolloutRecorder.MoveThreadRolloutAsync(
                            id,
                            archived: false,
                            bumpLastWriteTime: true,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (activePath is null)
                    {
                        throw new InvalidOperationException($"no archived rollout found for thread id {id}");
                    }
                }

                // 某些线程只有状态存储记录、尚未生成 rollout 快照；
                // 此时仍允许切换 archived 状态，并在后续 RewriteThreadSnapshotAsync 中补写首份快照。
                // Some threads only exist in the state store and do not have a rollout snapshot yet;
                // still allow archiving state transitions and let RewriteThreadSnapshotAsync create the first snapshot.
            }

            record.IsArchived = archived;
            record.StatusType = "notLoaded";
            record.ActiveFlags = [];
            record.UpdatedAt = DateTimeOffset.UtcNow;
            record.SessionState = NormalizeSessionState(record.SessionState, record);
            if (!isEphemeral)
            {
                await rolloutRecorder
                    .RewriteThreadSnapshotAsync(
                        KernelRolloutStateMapper.ToRolloutThreadRecord(record),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> SetThreadNameAsync(
        string id,
        string? name,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(id, out var record))
            {
                return null;
            }

            record.Name = Normalize(name);
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> UpdateThreadGitInfoAsync(
        string id,
        bool hasSha,
        string? sha,
        bool hasBranch,
        string? branch,
        bool hasOriginUrl,
        string? originUrl,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(id, out var record))
            {
                return null;
            }

            if (!hasSha && !hasBranch && !hasOriginUrl)
            {
                return Clone(record);
            }

            var git = CloneGitInfo(record.GitInfo) ?? new KernelGitInfoRecord();
            if (hasSha)
            {
                git.Sha = Normalize(sha);
            }

            if (hasBranch)
            {
                git.Branch = Normalize(branch);
            }

            if (hasOriginUrl)
            {
                git.OriginUrl = Normalize(originUrl);
            }

            record.GitInfo = IsGitInfoEmpty(git) ? null : git;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> AppendSeedHistoryItemAsync(
        string id,
        KernelConversationHistoryItem historyItem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(historyItem);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(id, out var record))
            {
                return null;
            }

            var cloned = KernelConversationHistoryUtilities.Clone(historyItem);
            if (!KernelConversationHistoryUtilities.HasReplayablePayload(cloned))
            {
                return Clone(record);
            }

            record.SeedHistory.Add(cloned);
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> DisableEnabledThreadMemoryModesAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var changedRecords = new List<KernelThreadRecord>();
            foreach (var record in threads.Values)
            {
                if (IsEphemeralThreadUnsafe(record.Id, record)
                    || !string.Equals(record.MemoryMode, "enabled", StringComparison.Ordinal))
                {
                    continue;
                }

                record.MemoryMode = "disabled";
                record.UpdatedAt = DateTimeOffset.UtcNow;
                changedRecords.Add(record);
            }

            if (changedRecords.Count == 0)
            {
                return 0;
            }

            await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
            foreach (var record in changedRecords)
            {
                await stateStore.UpsertThreadAsync(BuildStoredThreadStateRecord(record), cancellationToken).ConfigureAwait(false);
            }

            return changedRecords.Count;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> SetThreadPendingInputStateAsync(
        string id,
        KernelPendingInputStateRecord? pendingInputState,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(id, out var record))
            {
                return null;
            }

            var normalizedState = KernelPendingInputStateFactory.Normalize(pendingInputState);
            if (Equals(record.PendingInputState, normalizedState))
            {
                return Clone(record);
            }

            record.PendingInputState = normalizedState?.DeepClone();
            record.UpdatedAt = DateTimeOffset.UtcNow;
            if (!IsEphemeralThreadUnsafe(id, record))
            {
                await rolloutRecorder
                    .AppendPendingInputStateAsync(
                        record.Id,
                        KernelRolloutStateMapper.SerializePendingInputState(record.PendingInputState),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> SetThreadAgentMetadataAsync(
        string id,
        string? agentNickname,
        string? agentRole,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(id, out var record))
            {
                return null;
            }

            record.AgentNickname = Normalize(agentNickname);
            record.AgentRole = Normalize(agentRole);
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }
    public async Task<KernelThreadRecord?> RollbackThreadTurnsAsync(
        string id,
        int numTurns,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(id, out var record))
            {
                return null;
            }

            var isEphemeral = IsEphemeralThreadUnsafe(id, record);
            var rolloutRecord = isEphemeral
                ? null
                : await rolloutRecorder
                    .RehydrateThreadAsync(rolloutRecorder.ResolveRolloutPath(id, record.IsArchived), cancellationToken)
                    .ConfigureAwait(false);
            var sourceRecord = rolloutRecord is null
                ? record
                : MergeRolloutBackedRecordMetadata(
                    KernelRolloutStateMapper.FromRolloutThreadRecord(rolloutRecord),
                    record);

            if (numTurns <= 0 || sourceRecord.Turns.Count == 0)
            {
                return Clone(sourceRecord);
            }

            var updated = Clone(sourceRecord);
            var countToDrop = Math.Min(numTurns, updated.Turns.Count);
            updated.Turns.RemoveRange(updated.Turns.Count - countToDrop, countToDrop);

            var last = updated.Turns.LastOrDefault();
            updated.LastUserMessage = last?.UserMessage;
            updated.LastAssistantMessage = last?.AssistantMessage;
            updated.StatusType = "idle";
            updated.ActiveFlags = [];
            updated.PendingInputState = null;
            updated.UpdatedAt = DateTimeOffset.UtcNow;
            updated.SessionState = NormalizeSessionState(updated.SessionState, updated);

            threads[id] = updated;
            if (!isEphemeral)
            {
                await rolloutRecorder
                    .RewriteThreadSnapshotAsync(
                        KernelRolloutStateMapper.ToRolloutThreadRecord(updated),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await PersistRecordUnsafeAsync(updated, cancellationToken).ConfigureAwait(false);
            return Clone(updated);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> AppendCompletedTurnAsync(
        string id,
        string turnId,
        string? userMessage,
        string? assistantMessage,
        string turnStatus,
        CancellationToken cancellationToken,
        IReadOnlyList<KernelTurnItemRecord>? items = null,
        KernelTurnErrorRecord? error = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        TianShu.Contracts.Interactions.InteractionEnvelopeRef? interactionEnvelope = null)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(id, out var record))
            {
                return null;
            }

            var normalizedUserMessage = Normalize(userMessage);
            var normalizedAssistantMessage = Normalize(assistantMessage);
            var now = DateTimeOffset.UtcNow;
            var turn = record.Turns.LastOrDefault(existing => string.Equals(existing.Id, turnId, StringComparison.Ordinal));
            if (turn is null)
            {
                turn = new KernelTurnRecord
                {
                    Id = turnId,
                    StartedAt = startedAt ?? now,
                };
                record.Turns.Add(turn);
            }

            turn.StartedAt = startedAt ?? (turn.StartedAt == default ? now : turn.StartedAt);
            turn.CompletedAt = completedAt ?? now;
            turn.Status = NormalizeTurnStatus(turnStatus);
            turn.UserMessage = normalizedUserMessage;
            turn.AssistantMessage = normalizedAssistantMessage;
            turn.InteractionEnvelope = interactionEnvelope ?? turn.InteractionEnvelope;
            turn.Error = CloneTurnError(error);
            if (items is not null)
            {
                turn.Items = items.Select(CloneTurnItem).ToList();
            }

            record.LastUserMessage = normalizedUserMessage ?? record.LastUserMessage;
            record.LastAssistantMessage = normalizedAssistantMessage ?? record.LastAssistantMessage;
            record.StatusType = "idle";
            record.ActiveFlags = [];
            record.UpdatedAt = DateTimeOffset.UtcNow;
            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadRecord?> CompactThreadAsync(
        string id,
        int keepRecentTurns,
        CancellationToken cancellationToken,
        string? compactionTurnId = null)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!threads.TryGetValue(id, out var record))
            {
                return null;
            }

            var keepCount = Math.Clamp(keepRecentTurns, 1, 64);
            if (record.Turns.Count <= keepCount)
            {
                return Clone(record);
            }

            var prefixCount = record.Turns.Count - keepCount;
            var prefix = record.Turns.Take(prefixCount).ToList();
            var kept = record.Turns.Skip(prefixCount).Select(CloneTurn).ToList();
            var replacementHistory = BuildCompactionReplacementHistory(record.SeedHistory, prefix);
            if (replacementHistory.Count > 0)
            {
                record.SeedHistory = replacementHistory;
            }

            kept.Insert(0, new KernelTurnRecord
            {
                Id = string.IsNullOrWhiteSpace(compactionTurnId)
                    ? $"compact_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}"
                    : compactionTurnId,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                Status = "completed",
                IsContextCompaction = true,
            });

            record.Turns = kept;
            var lastTurn = kept.LastOrDefault();
            record.LastUserMessage = lastTurn?.UserMessage;
            record.LastAssistantMessage = lastTurn?.AssistantMessage;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            record.StatusType = "idle";
            record.ActiveFlags = [];

            if (!IsEphemeralThreadUnsafe(id, record))
            {
                await rolloutRecorder
                    .AppendCompactionAsync(
                        record.Id,
                        kept[0].Id,
                        KernelRolloutStateMapper.SerializeSeedHistory(record.SeedHistory),
                        record.Turns.Select(KernelRolloutStateMapper.ToRolloutTurnRecord).ToArray(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await PersistRecordUnsafeAsync(record, cancellationToken).ConfigureAwait(false);
            return Clone(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> IsEphemeralThreadAsync(string id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return IsEphemeralThreadUnsafe(id, threads.TryGetValue(id, out var record) ? record : null);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SaveUnsafeAsync(CancellationToken cancellationToken)
    {
        var payload = threads.Values
            .Where(item => !IsEphemeralThreadUnsafe(item.Id, item))
            .OrderByDescending(x => x.UpdatedAt)
            .ToList();
        var json = JsonSerializer.Serialize(payload, serializerOptions);
        var tempPath = $"{filePath}.tmp";

        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, filePath, overwrite: true);
    }

    private static KernelThreadRecord Clone(KernelThreadRecord source)
        => new()
        {
            Id = source.Id,
            Cwd = source.Cwd,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            MemoryMode = source.MemoryMode,
            ForkedFromThreadId = source.ForkedFromThreadId,
            LastUserMessage = source.LastUserMessage,
            LastAssistantMessage = source.LastAssistantMessage,
            Name = source.Name,
            AgentNickname = source.AgentNickname,
            AgentRole = source.AgentRole,
            IsArchived = source.IsArchived,
            StatusType = source.StatusType,
            ActiveFlags = source.ActiveFlags.ToList(),
            GitInfo = CloneGitInfo(source.GitInfo),
            Turns = source.Turns.Select(CloneTurn).ToList(),
            SeedHistory = source.SeedHistory.Select(CloneConversationHistory).ToList(),
            ConfigSnapshot = source.ConfigSnapshot?.DeepClone(),
            PendingInputState = source.PendingInputState?.DeepClone(),
            SessionState = CloneSessionState(source.SessionState),
        };

    private static KernelThreadRecord NormalizeRecord(KernelThreadRecord source)
    {
        source.Id = Normalize(source.Id) ?? source.Id;
        source.Cwd = Normalize(source.Cwd);
        source.MemoryMode = NormalizeMemoryMode(source.MemoryMode);
        source.ForkedFromThreadId = Normalize(source.ForkedFromThreadId);
        source.LastUserMessage = Normalize(source.LastUserMessage);
        source.LastAssistantMessage = Normalize(source.LastAssistantMessage);
        source.Name = Normalize(source.Name);
        source.AgentNickname = Normalize(source.AgentNickname);
        source.AgentRole = Normalize(source.AgentRole);
        source.StatusType = NormalizeStatus(source.StatusType);
        source.ActiveFlags = NormalizeActiveFlags(source.ActiveFlags);
        source.GitInfo = IsGitInfoEmpty(source.GitInfo) ? null : source.GitInfo;
        source.Turns = (source.Turns ?? []).Select(CloneTurn).ToList();
        source.SeedHistory = (source.SeedHistory ?? []).Select(CloneConversationHistory).ToList();
        source.ConfigSnapshot = NormalizeConfigSnapshot(source.ConfigSnapshot);
        source.PendingInputState = KernelPendingInputStateFactory.Normalize(source.PendingInputState);
        source.SessionState = NormalizeSessionState(source.SessionState, source);
        MigrateLegacyCompactionState(source);
        return source;
    }

    private static KernelTurnRecord CloneTurn(KernelTurnRecord source)
        => new()
        {
            Id = source.Id,
            StartedAt = source.StartedAt,
            CompletedAt = source.CompletedAt,
            Status = NormalizeTurnStatus(source.Status),
            UserMessage = Normalize(source.UserMessage),
            AssistantMessage = Normalize(source.AssistantMessage),
            InteractionEnvelope = source.InteractionEnvelope,
            Items = (source.Items ?? []).Select(CloneTurnItem).ToList(),
            Error = CloneTurnError(source.Error),
            IsContextCompaction = source.IsContextCompaction || string.Equals(Normalize(source.UserMessage), "上下文压缩摘要", StringComparison.Ordinal),
        };

    private static List<KernelTurnRecord> BuildForkTurns(IReadOnlyList<KernelTurnRecord>? persistedTurns, KernelTurnRecord? liveTurn)
    {
        var turns = (persistedTurns ?? []).Select(CloneTurn).ToList();
        if (liveTurn is null)
        {
            return turns;
        }

        var liveClone = CloneTurn(liveTurn);
        var existingIndex = turns.FindIndex(turn => string.Equals(turn.Id, liveClone.Id, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            turns[existingIndex] = liveClone;
        }
        else
        {
            turns.Add(liveClone);
        }

        return turns;
    }

    private static KernelConversationHistoryItem CloneConversationHistory(KernelConversationHistoryItem source)
        => KernelConversationHistoryUtilities.Clone(source);

    private static KernelThreadConfigSnapshot? CloneConfigSnapshot(KernelThreadConfigSnapshot? source, bool ephemeral)
    {
        if (source is null)
        {
            return null;
        }

        return source.DeepClone() with { Ephemeral = ephemeral };
    }

    private bool IsEphemeralThreadUnsafe(string threadId, KernelThreadRecord? record = null)
        => ephemeralThreadIds.Contains(threadId) || record?.ConfigSnapshot?.Ephemeral == true;

    private void SetEphemeralThreadStateUnsafe(string threadId, bool ephemeral)
    {
        if (ephemeral)
        {
            ephemeralThreadIds.Add(threadId);
            return;
        }

        ephemeralThreadIds.Remove(threadId);
    }

    private async Task PersistRecordUnsafeAsync(KernelThreadRecord record, CancellationToken cancellationToken)
    {
        if (IsEphemeralThreadUnsafe(record.Id, record))
        {
            return;
        }

        var shouldMaterializeRollout = ShouldMaterializeRollout(record);
        record.SessionState = NormalizeSessionState(record.SessionState, record);
        await SaveUnsafeAsync(cancellationToken).ConfigureAwait(false);
        await stateStore.UpsertThreadAsync(BuildStoredThreadStateRecord(record), cancellationToken).ConfigureAwait(false);
        var rolloutPath = rolloutRecorder.ResolveRolloutPath(record.Id, record.IsArchived);
        if (!File.Exists(rolloutPath) && !shouldMaterializeRollout)
        {
            return;
        }

        var rolloutRecord = KernelRolloutStateMapper.ToRolloutThreadRecord(record);
        await rolloutRecorder.EnsureSessionMetaAsync(record.Id, rolloutRecord, cancellationToken).ConfigureAwait(false);
        await rolloutRecorder.AppendSessionStateAsync(record.Id, rolloutRecord, cancellationToken).ConfigureAwait(false);
        await rolloutRecorder.CloseThreadWriterAsync(record.Id, cancellationToken).ConfigureAwait(false);
    }

    private static bool ShouldMaterializeRollout(KernelThreadRecord record)
        => !string.IsNullOrWhiteSpace(record.Name)
           || !string.IsNullOrWhiteSpace(record.LastUserMessage)
           || !string.IsNullOrWhiteSpace(record.LastAssistantMessage)
           || (record.Turns?.Count > 0)
           || (record.SeedHistory?.Count > 0)
           || record.PendingInputState is not null
           || record.SessionState?.Orchestration is not null;

    private KernelStoredThreadStateRecord BuildStoredThreadStateRecord(KernelThreadRecord record)
        => new(
            ThreadId: record.Id,
            Cwd: record.Cwd ?? string.Empty,
            CreatedAt: record.CreatedAt,
            UpdatedAt: record.UpdatedAt,
            StatusType: record.StatusType,
            IsArchived: record.IsArchived,
            Name: record.Name,
            PayloadJson: JsonSerializer.Serialize(record, serializerOptions));

    private static KernelThreadRecord MergeRolloutBackedRecordMetadata(KernelThreadRecord rolloutRecord, KernelThreadRecord storeRecord)
    {
        var merged = Clone(rolloutRecord);
        merged.MemoryMode = storeRecord.MemoryMode;
        merged.Name = storeRecord.Name;
        merged.AgentNickname = storeRecord.AgentNickname;
        merged.AgentRole = storeRecord.AgentRole;
        merged.IsArchived = storeRecord.IsArchived;
        merged.GitInfo = CloneGitInfo(storeRecord.GitInfo);
        if (string.IsNullOrWhiteSpace(merged.Cwd))
        {
            merged.Cwd = storeRecord.Cwd;
        }

        merged.ForkedFromThreadId ??= storeRecord.ForkedFromThreadId;
        merged.ConfigSnapshot ??= storeRecord.ConfigSnapshot?.DeepClone();
        merged.PendingInputState ??= storeRecord.PendingInputState?.DeepClone();
        merged.SessionState ??= CloneSessionState(storeRecord.SessionState);
        if (merged.Turns.Count == 0 && storeRecord.Turns.Count > 0)
        {
            merged.Turns = storeRecord.Turns
                .Select(CloneTurn)
                .ToList();
        }

        merged.LastUserMessage ??= storeRecord.LastUserMessage;
        merged.LastAssistantMessage ??= storeRecord.LastAssistantMessage;
        if (merged.SeedHistory.Count == 0 && storeRecord.SeedHistory.Count > 0)
        {
            merged.SeedHistory = storeRecord.SeedHistory
                .Select(CloneConversationHistory)
                .ToList();
        }

        return merged;
    }

    private static KernelTurnItemRecord CloneTurnItem(KernelTurnItemRecord source)
        => new()
        {
            Id = Normalize(source.Id) ?? string.Empty,
            Type = Normalize(source.Type) ?? string.Empty,
            Payload = source.Payload.Clone(),
        };

    private static KernelTurnErrorRecord? CloneTurnError(KernelTurnErrorRecord? source)
    {
        if (source is null)
        {
            return null;
        }

        return new KernelTurnErrorRecord
        {
            Message = Normalize(source.Message) ?? string.Empty,
            AdditionalDetails = Normalize(source.AdditionalDetails),
        };
    }

    private sealed class ThreadStoreInteractionEnvelopeRefJsonConverter : JsonConverter<InteractionEnvelopeRef>
    {
        public override InteractionEnvelopeRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var id = TryReadString(root, "id")
                ?? TryReadString(root, "interactionId")
                ?? TryReadString(root, "value");
            var surface = TryReadString(root, "surface");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(surface))
            {
                return null;
            }

            var sourceKind = TryReadSourceKind(root, "sourceKind")
                ?? TryReadSourceKind(root, "kind")
                ?? InteractionSourceKind.Host;
            var createdAt = TryReadUnixTime(root, "createdAtUnixMs")
                ?? TryReadDateTime(root, "createdAt")
                ?? DateTimeOffset.UtcNow;
            return new InteractionEnvelopeRef(
                new InteractionEnvelopeId(id),
                sourceKind,
                surface,
                createdAt);
        }

        public override void Write(Utf8JsonWriter writer, InteractionEnvelopeRef value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id.Value);
            writer.WriteString("sourceKind", value.SourceKind.ToString());
            writer.WriteString("surface", value.Surface);
            writer.WriteNumber("createdAtUnixMs", value.CreatedAt.ToUnixTimeMilliseconds());
            writer.WriteEndObject();
        }

        private static string? TryReadString(JsonElement element, string propertyName)
            => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

        private static InteractionSourceKind? TryReadSourceKind(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            if (property.ValueKind == JsonValueKind.String
                && Enum.TryParse<InteractionSourceKind>(property.GetString(), ignoreCase: true, out var parsedFromString))
            {
                return parsedFromString;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var parsedFromNumber))
            {
                return (InteractionSourceKind)parsedFromNumber;
            }

            return null;
        }

        private static DateTimeOffset? TryReadUnixTime(JsonElement element, string propertyName)
            => element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetInt64(out var unixTime)
                ? DateTimeOffset.FromUnixTimeMilliseconds(unixTime)
                : null;

        private static DateTimeOffset? TryReadDateTime(JsonElement element, string propertyName)
            => element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.String
               && property.TryGetDateTimeOffset(out var dateTimeOffset)
                ? dateTimeOffset
                : null;
    }

    private static KernelGitInfoRecord? CloneGitInfo(KernelGitInfoRecord? source)
    {
        if (source is null)
        {
            return null;
        }

        return new KernelGitInfoRecord
        {
            Sha = Normalize(source.Sha),
            Branch = Normalize(source.Branch),
            OriginUrl = Normalize(source.OriginUrl),
        };
    }

    private static KernelThreadSessionProjectionStateRecord? CloneSessionState(KernelThreadSessionProjectionStateRecord? source)
    {
        if (source is null)
        {
            return null;
        }

        return new KernelThreadSessionProjectionStateRecord
        {
            SessionId = Normalize(source.SessionId) ?? string.Empty,
            Title = Normalize(source.Title) ?? string.Empty,
            CollaborationSpaceId = Normalize(source.CollaborationSpaceId) ?? DefaultSessionCollaborationSpaceId,
            CollaborationSpaceKey = Normalize(source.CollaborationSpaceKey) ?? DefaultSessionCollaborationSpaceKey,
            CollaborationSpaceDisplayName = Normalize(source.CollaborationSpaceDisplayName) ?? DefaultSessionCollaborationSpaceDisplayName,
            SessionMode = NormalizeSessionMode(source.SessionMode) ?? InteractiveSessionMode,
            IsClosed = source.IsClosed,
            ActiveThreadId = Normalize(source.ActiveThreadId),
            HasActiveTurn = source.HasActiveTurn,
            Orchestration = KernelThreadOrchestrationStateNormalizer.Clone(source.Orchestration),
        };
    }

    private static bool IsGitInfoEmpty(KernelGitInfoRecord? source)
        => source is null
           || (string.IsNullOrWhiteSpace(source.Sha)
               && string.IsNullOrWhiteSpace(source.Branch)
               && string.IsNullOrWhiteSpace(source.OriginUrl));

    private static KernelThreadSessionProjectionStateRecord NormalizeSessionState(
        KernelThreadSessionProjectionStateRecord? source,
        KernelThreadRecord record)
    {
        var normalizedThreadId = Normalize(record.Id) ?? string.Empty;
        var normalizedName = Normalize(record.Name);
        var normalizedLastUserMessage = Normalize(record.LastUserMessage);
        var generatedTitle = BuildGeneratedSessionTitle(normalizedThreadId);
        var currentTitle = Normalize(source?.Title);
        string title;

        if (!string.IsNullOrWhiteSpace(normalizedName))
        {
            title = normalizedName;
        }
        else if (string.Equals(currentTitle, generatedTitle, StringComparison.Ordinal)
                 && !string.IsNullOrWhiteSpace(normalizedLastUserMessage))
        {
            title = TruncateSessionTitle(normalizedLastUserMessage);
        }
        else
        {
            title = currentTitle
                ?? (!string.IsNullOrWhiteSpace(normalizedLastUserMessage)
                    ? TruncateSessionTitle(normalizedLastUserMessage)
                    : generatedTitle);
        }

        var isClosed = record.IsArchived;
        var hasActiveTurn = !isClosed
                            && string.Equals(NormalizeStatus(record.StatusType), "active", StringComparison.OrdinalIgnoreCase);
        var sourceSessionMode = NormalizeSessionMode(source?.SessionMode);
        var resolvedSessionMode = ResolveSessionMode(record);
        var effectiveSessionMode = sourceSessionMode is "review" or "automation"
            ? sourceSessionMode
            : resolvedSessionMode;

        return new KernelThreadSessionProjectionStateRecord
        {
            SessionId = Normalize(source?.SessionId) ?? normalizedThreadId,
            Title = title,
            CollaborationSpaceId = Normalize(source?.CollaborationSpaceId) ?? DefaultSessionCollaborationSpaceId,
            CollaborationSpaceKey = Normalize(source?.CollaborationSpaceKey) ?? DefaultSessionCollaborationSpaceKey,
            CollaborationSpaceDisplayName = Normalize(source?.CollaborationSpaceDisplayName) ?? DefaultSessionCollaborationSpaceDisplayName,
            SessionMode = effectiveSessionMode,
            IsClosed = isClosed,
            ActiveThreadId = isClosed ? null : Normalize(source?.ActiveThreadId) ?? normalizedThreadId,
            HasActiveTurn = hasActiveTurn,
            Orchestration = KernelThreadOrchestrationStateNormalizer.Clone(source?.Orchestration),
        };
    }

    private static string ResolveSessionMode(KernelThreadRecord record)
    {
        var collaborationMode = Normalize(record.ConfigSnapshot?.CollaborationMode?.Mode);
        if (string.Equals(collaborationMode, KernelCollaborationModeState.PlanMode, StringComparison.OrdinalIgnoreCase))
        {
            return PlanningSessionMode;
        }

        return InteractiveSessionMode;
    }

    private static string? NormalizeSessionMode(string? mode)
        => Normalize(mode)?.ToLowerInvariant() switch
        {
            InteractiveSessionMode => InteractiveSessionMode,
            PlanningSessionMode => PlanningSessionMode,
            "review" => "review",
            "automation" => "automation",
            _ => null,
        };

    private static string BuildGeneratedSessionTitle(string threadId)
        => string.IsNullOrWhiteSpace(threadId)
            ? "TianShu Session"
            : $"Session {threadId}";

    private static string TruncateSessionTitle(string value)
        => value.Length <= 80 ? value : value[..80];

    private static string NormalizeStatus(string? status)
    {
        var text = Normalize(status);
        return text?.ToLowerInvariant() switch
        {
            "notloaded" => "notLoaded",
            "idle" => "idle",
            "systemerror" => "systemError",
            "active" => "active",
            _ => "idle",
        };
    }

    private static string NormalizeTurnStatus(string? status)
    {
        var text = Normalize(status);
        return text?.ToLowerInvariant() switch
        {
            "completed" => "completed",
            "interrupted" => "interrupted",
            "failed" => "failed",
            "inprogress" => "inProgress",
            _ => "completed",
        };
    }

    private static string NormalizeMemoryMode(string? memoryMode)
        => KernelThreadMemoryModeMapper.NormalizeStorageMode(memoryMode);

    private static List<string> NormalizeActiveFlags(IReadOnlyList<string>? activeFlags)
    {
        if (activeFlags is null || activeFlags.Count == 0)
        {
            return [];
        }

        var list = new List<string>(activeFlags.Count);
        foreach (var flag in activeFlags)
        {
            var normalized = Normalize(flag);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            switch (normalized.ToLowerInvariant())
            {
                case "waitingonapproval":
                    list.Add("waitingOnApproval");
                    break;
                case "waitingonuserinput":
                    list.Add("waitingOnUserInput");
                    break;
            }
        }

        return list.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool MatchesSearchTerm(KernelThreadRecord record, string searchTerm)
    {
        return (!string.IsNullOrWhiteSpace(record.Name) && record.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
               || (!string.IsNullOrWhiteSpace(record.LastUserMessage) && record.LastUserMessage.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
               || (!string.IsNullOrWhiteSpace(record.LastAssistantMessage) && record.LastAssistantMessage.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
               || (!string.IsNullOrWhiteSpace(record.Id) && record.Id.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
    }

    private static string EncodeThreadCursor(string threadId)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(threadId));

    private static bool TryDecodeThreadCursor(string cursor, out string threadId)
    {
        threadId = string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(cursor);
            threadId = System.Text.Encoding.UTF8.GetString(bytes);
            if (!string.IsNullOrWhiteSpace(threadId))
            {
                return true;
            }
        }
        catch
        {
            // 兼容旧测试与人工输入，允许直接传 threadId。
        }

        threadId = cursor;
        return !string.IsNullOrWhiteSpace(threadId);
    }

    private DateTimeOffset ResolveThreadListUpdatedAt(KernelThreadRecord record)
    {
        var rolloutPath = rolloutRecorder.ResolveRolloutPath(record.Id, record.IsArchived);
        if (!File.Exists(rolloutPath))
        {
            return record.UpdatedAt;
        }

        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(rolloutPath), TimeSpan.Zero);
        }
        catch
        {
            return record.UpdatedAt;
        }
    }

    private bool ResolveThreadArchivedState(KernelThreadRecord record)
        => rolloutRecorder.TryResolveArchivedState(record.Id) ?? record.IsArchived;

    private static void MigrateLegacyCompactionState(KernelThreadRecord source)
    {
        var legacySummary = source.Turns
            .LastOrDefault(static turn => !turn.IsContextCompaction
                && string.Equals(Normalize(turn.UserMessage), "上下文压缩摘要", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(turn.AssistantMessage));

        if (legacySummary is not null && source.SeedHistory.Count == 0)
        {
            source.SeedHistory =
            [
                new KernelConversationHistoryItem
                {
                    Role = "user",
                    Content = "上下文压缩摘要",
                },
                new KernelConversationHistoryItem
                {
                    Role = "assistant",
                    Content = Normalize(legacySummary.AssistantMessage) ?? string.Empty,
                },
            ];
        }

        foreach (var turn in source.Turns)
        {
            if (turn.IsContextCompaction)
            {
                turn.UserMessage = null;
                turn.AssistantMessage = null;
                continue;
            }

            if (!string.Equals(Normalize(turn.UserMessage), "上下文压缩摘要", StringComparison.Ordinal))
            {
                continue;
            }

            turn.IsContextCompaction = true;
            turn.UserMessage = null;
            turn.AssistantMessage = null;
        }
    }

    private static KernelThreadConfigSnapshot? NormalizeConfigSnapshot(KernelThreadConfigSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var normalized = snapshot.DeepClone();
        return normalized.SessionSource is null
            ? normalized with { SessionSource = KernelSessionSource.VsCode }
            : normalized;
    }

    private static List<KernelConversationHistoryItem> BuildCompactionReplacementHistory(
        IReadOnlyList<KernelConversationHistoryItem> existingSeedHistory,
        IReadOnlyList<KernelTurnRecord> turns)
    {
        var summary = BuildCompactionSummary(existingSeedHistory, turns);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return existingSeedHistory.Select(CloneConversationHistory).ToList();
        }

        return
        [
            new KernelConversationHistoryItem
            {
                Role = "user",
                Content = "上下文压缩摘要",
            },
            new KernelConversationHistoryItem
            {
                Role = "assistant",
                Content = summary,
            },
        ];
    }

    private static string BuildCompactionSummary(
        IReadOnlyList<KernelConversationHistoryItem> seedHistory,
        IReadOnlyList<KernelTurnRecord> turns)
    {
        var segments = new List<string>(seedHistory.Count + turns.Count);
        foreach (var item in seedHistory)
        {
            var content = KernelConversationHistoryUtilities.BuildDisplayText(item);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var prefix = Normalize(item.Role)?.ToLowerInvariant() switch
            {
                "assistant" => "A",
                "system" => "S",
                _ => "Q",
            };
            segments.Add($"{prefix}: {content}");
        }

        foreach (var turn in turns)
        {
            var user = Normalize(turn.UserMessage);
            var assistant = Normalize(turn.AssistantMessage);
            if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(assistant))
            {
                continue;
            }

            var snippet = $"Q: {user ?? "（空）"}{Environment.NewLine}A: {assistant ?? "（空）"}";
            segments.Add(snippet);
        }

        if (segments.Count == 0)
        {
            return string.Empty;
        }

        var merged = string.Join(Environment.NewLine + Environment.NewLine, segments);
        const int maxLength = 4000;
        if (merged.Length > maxLength)
        {
            return merged[..maxLength];
        }

        return merged;
    }
}

internal sealed class KernelSpawnAgentGuardState
{
    private readonly object gate = new();
    private readonly HashSet<string> activeThreadIds = new(StringComparer.Ordinal);
    private int reservedCount;

    public void Reserve(int maxThreads)
    {
        lock (gate)
        {
            if (reservedCount >= maxThreads)
            {
                throw new InvalidOperationException($"agent thread limit reached (max {maxThreads})");
            }

            reservedCount++;
        }
    }

    public void Commit(string threadId)
    {
        var normalizedThreadId = NormalizeThreadId(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        lock (gate)
        {
            activeThreadIds.Add(normalizedThreadId!);
        }
    }

    public void ReleaseReserved()
    {
        lock (gate)
        {
            if (reservedCount > 0)
            {
                reservedCount--;
            }
        }
    }

    public bool IsTracked(string threadId)
    {
        var normalizedThreadId = NormalizeThreadId(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return false;
        }

        lock (gate)
        {
            return activeThreadIds.Contains(normalizedThreadId!);
        }
    }

    public void ReleaseTracked(string threadId)
    {
        var normalizedThreadId = NormalizeThreadId(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return;
        }

        lock (gate)
        {
            if (activeThreadIds.Remove(normalizedThreadId!) && reservedCount > 0)
            {
                reservedCount--;
            }
        }
    }

    private static string? NormalizeThreadId(string? threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        var trimmed = threadId.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}


