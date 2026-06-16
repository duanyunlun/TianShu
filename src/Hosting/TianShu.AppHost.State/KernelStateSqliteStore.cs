using System.Runtime.InteropServices;
using System.Text.Json;

namespace TianShu.AppHost.State;

/// <summary>
/// 表示宿主侧线程当前态在 sqlite 中的镜像记录。
/// Represents the host-side mirrored thread state row stored in sqlite.
/// </summary>
internal sealed record KernelStoredThreadStateRecord(
    string ThreadId,
    string Cwd,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string StatusType,
    bool IsArchived,
    string? Name,
    string PayloadJson);

internal sealed record KernelStateTurnLogRecord(
    long Id,
    string ThreadId,
    string? TurnId,
    string Phase,
    string Status,
    string? Summary,
    string PayloadJson,
    DateTimeOffset CreatedAt);

internal sealed record KernelThreadMemoryRecord(
    string ThreadId,
    DateTimeOffset SourceUpdatedAt,
    string RawMemory,
    string RolloutSummary,
    DateTimeOffset GeneratedAt,
    long UsageCount,
    DateTimeOffset? LastUsageAt);

internal sealed record KernelAgentJobRecord(
    string Id,
    string Name,
    string Status,
    string Instruction,
    string? OutputSchemaJson,
    string InputHeadersJson,
    string InputCsvPath,
    string OutputCsvPath,
    bool AutoExport,
    int? MaxRuntimeSeconds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? LastError);

internal sealed record KernelAgentJobItemRecord(
    string JobId,
    string ItemId,
    int RowIndex,
    string? SourceId,
    string RowJson,
    string Status,
    string? AssignedThreadId,
    int AttemptCount,
    string? ResultJson,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ReportedAt);

internal sealed record KernelAgentJobProgressRecord(
    string JobId,
    int TotalItems,
    int PendingItems,
    int RunningItems,
    int CompletedItems,
    int FailedItems);

internal sealed record KernelStateRolloutRecord(
    string RolloutKey,
    string ThreadId,
    string? TurnId,
    string Source,
    string RolloutPath,
    string? Preview,
    string PayloadJson,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 提供 AppHost 本地 sqlite 状态存储。
/// Provides the app host local sqlite-backed state store.
/// </summary>
internal sealed class KernelStateSqliteStore
{
    private readonly string databasePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;

    public KernelStateSqliteStore(string databasePath)
    {
        this.databasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath => databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            connection.ExecuteBatch(
                "PRAGMA journal_mode=WAL;" + Environment.NewLine +
                "CREATE TABLE IF NOT EXISTS thread_state (" +
                "  thread_id TEXT PRIMARY KEY," +
                "  cwd TEXT NOT NULL," +
                "  created_at_unix_ms INTEGER NOT NULL," +
                "  updated_at_unix_ms INTEGER NOT NULL," +
                "  status_type TEXT NOT NULL," +
                "  is_archived INTEGER NOT NULL," +
                "  name TEXT NULL," +
                "  payload_json TEXT NOT NULL" +
                ");" + Environment.NewLine +
                "CREATE TABLE IF NOT EXISTS turn_log (" +
                "  id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "  thread_id TEXT NOT NULL," +
                "  turn_id TEXT NULL," +
                "  phase TEXT NOT NULL," +
                "  status TEXT NOT NULL," +
                "  summary TEXT NULL," +
                "  payload_json TEXT NOT NULL," +
                "  created_at_unix_ms INTEGER NOT NULL" +
                ");" + Environment.NewLine +
                "CREATE INDEX IF NOT EXISTS idx_turn_log_thread_created ON turn_log(thread_id, created_at_unix_ms DESC);" + Environment.NewLine +
                "CREATE TABLE IF NOT EXISTS rollout_index (" +
                "  rollout_key TEXT PRIMARY KEY," +
                "  thread_id TEXT NOT NULL," +
                "  turn_id TEXT NULL," +
                "  source TEXT NOT NULL," +
                "  rollout_path TEXT NOT NULL," +
                "  preview TEXT NULL," +
                "  payload_json TEXT NOT NULL," +
                "  updated_at_unix_ms INTEGER NOT NULL" +
                ");" + Environment.NewLine +
                "CREATE INDEX IF NOT EXISTS idx_rollout_index_thread_updated ON rollout_index(thread_id, updated_at_unix_ms DESC);" + Environment.NewLine +
                "CREATE TABLE IF NOT EXISTS stage1_outputs (" +
                "  thread_id TEXT PRIMARY KEY," +
                "  source_updated_at_unix_ms INTEGER NOT NULL," +
                "  raw_memory TEXT NOT NULL," +
                "  rollout_summary TEXT NOT NULL," +
                "  generated_at_unix_ms INTEGER NOT NULL," +
                "  usage_count INTEGER NOT NULL DEFAULT 0," +
                "  last_usage_at_unix_ms INTEGER NULL," +
                "  FOREIGN KEY(thread_id) REFERENCES thread_state(thread_id) ON DELETE CASCADE" +
                ");" + Environment.NewLine +
                "CREATE INDEX IF NOT EXISTS idx_stage1_outputs_source_updated ON stage1_outputs(source_updated_at_unix_ms DESC, thread_id DESC);" + Environment.NewLine +
                "CREATE TABLE IF NOT EXISTS agent_jobs (" +
                "  id TEXT PRIMARY KEY," +
                "  name TEXT NOT NULL," +
                "  status TEXT NOT NULL," +
                "  instruction TEXT NOT NULL," +
                "  output_schema_json TEXT NULL," +
                "  input_headers_json TEXT NOT NULL," +
                "  input_csv_path TEXT NOT NULL," +
                "  output_csv_path TEXT NOT NULL," +
                "  auto_export INTEGER NOT NULL DEFAULT 1," +
                "  max_runtime_seconds INTEGER NULL," +
                "  created_at_unix_ms INTEGER NOT NULL," +
                "  updated_at_unix_ms INTEGER NOT NULL," +
                "  started_at_unix_ms INTEGER NULL," +
                "  completed_at_unix_ms INTEGER NULL," +
                "  last_error TEXT NULL" +
                ");" + Environment.NewLine +
                "CREATE INDEX IF NOT EXISTS idx_agent_jobs_status ON agent_jobs(status, updated_at_unix_ms DESC);" + Environment.NewLine +
                "CREATE TABLE IF NOT EXISTS agent_job_items (" +
                "  job_id TEXT NOT NULL," +
                "  item_id TEXT NOT NULL," +
                "  row_index INTEGER NOT NULL," +
                "  source_id TEXT NULL," +
                "  row_json TEXT NOT NULL," +
                "  status TEXT NOT NULL," +
                "  assigned_thread_id TEXT NULL," +
                "  attempt_count INTEGER NOT NULL DEFAULT 0," +
                "  result_json TEXT NULL," +
                "  last_error TEXT NULL," +
                "  created_at_unix_ms INTEGER NOT NULL," +
                "  updated_at_unix_ms INTEGER NOT NULL," +
                "  completed_at_unix_ms INTEGER NULL," +
                "  reported_at_unix_ms INTEGER NULL," +
                "  PRIMARY KEY (job_id, item_id)," +
                "  FOREIGN KEY(job_id) REFERENCES agent_jobs(id) ON DELETE CASCADE" +
                ");" + Environment.NewLine +
                "CREATE INDEX IF NOT EXISTS idx_agent_job_items_status ON agent_job_items(job_id, status, row_index ASC);");
            EnsureAgentJobSchema(connection);
            initialized = true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpsertThreadAsync(KernelStoredThreadStateRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            connection.Execute(
                "INSERT INTO thread_state(thread_id, cwd, created_at_unix_ms, updated_at_unix_ms, status_type, is_archived, name, payload_json) " +
                "VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8) " +
                "ON CONFLICT(thread_id) DO UPDATE SET " +
                "cwd=excluded.cwd, updated_at_unix_ms=excluded.updated_at_unix_ms, status_type=excluded.status_type, is_archived=excluded.is_archived, name=excluded.name, payload_json=excluded.payload_json;",
                record.ThreadId,
                record.Cwd,
                record.CreatedAt.ToUnixTimeMilliseconds(),
                record.UpdatedAt.ToUnixTimeMilliseconds(),
                record.StatusType,
                record.IsArchived ? 1L : 0L,
                record.Name,
                record.PayloadJson);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendTurnLogAsync(
        string threadId,
        string? turnId,
        string phase,
        string status,
        string? summary,
        object payload,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            connection.Execute(
                "INSERT INTO turn_log(thread_id, turn_id, phase, status, summary, payload_json, created_at_unix_ms) VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7);",
                threadId,
                turnId,
                phase,
                status,
                summary,
                JsonSerializer.Serialize(payload),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpsertRolloutAsync(
        string rolloutKey,
        string threadId,
        string? turnId,
        string source,
        string rolloutPath,
        string? preview,
        object payload,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            connection.Execute(
                "INSERT INTO rollout_index(rollout_key, thread_id, turn_id, source, rollout_path, preview, payload_json, updated_at_unix_ms) " +
                "VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8) " +
                "ON CONFLICT(rollout_key) DO UPDATE SET " +
                "thread_id=excluded.thread_id, turn_id=excluded.turn_id, source=excluded.source, rollout_path=excluded.rollout_path, preview=excluded.preview, payload_json=excluded.payload_json, updated_at_unix_ms=excluded.updated_at_unix_ms;",
                rolloutKey,
                threadId,
                turnId,
                source,
                rolloutPath,
                preview,
                JsonSerializer.Serialize(payload),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelStoredThreadStateRecord?> GetThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            var row = connection.QuerySingle(
                "SELECT thread_id, cwd, created_at_unix_ms, updated_at_unix_ms, status_type, is_archived, name, payload_json FROM thread_state WHERE thread_id = ?1;",
                threadId);
            return row is null ? null : ReadThreadState(row);
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
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            connection.Execute("DELETE FROM rollout_index WHERE thread_id = ?1;", threadId);
            connection.Execute("DELETE FROM turn_log WHERE thread_id = ?1;", threadId);
            connection.Execute("DELETE FROM stage1_outputs WHERE thread_id = ?1;", threadId);
            connection.Execute("DELETE FROM thread_state WHERE thread_id = ?1;", threadId);
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
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            connection.Execute("DELETE FROM rollout_index;");
            connection.Execute("DELETE FROM turn_log;");
            connection.Execute("DELETE FROM stage1_outputs;");
            connection.Execute("DELETE FROM thread_state;");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<KernelStateTurnLogRecord>> ListTurnLogsAsync(string threadId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            return connection.QueryMany(
                    "SELECT id, thread_id, turn_id, phase, status, summary, payload_json, created_at_unix_ms FROM turn_log WHERE thread_id = ?1 ORDER BY id ASC;",
                    static row => new KernelStateTurnLogRecord(
                        Id: row.GetInt64(0),
                        ThreadId: row.GetString(1) ?? string.Empty,
                        TurnId: row.GetString(2),
                        Phase: row.GetString(3) ?? string.Empty,
                        Status: row.GetString(4) ?? string.Empty,
                        Summary: row.GetString(5),
                        PayloadJson: row.GetString(6) ?? string.Empty,
                        CreatedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(7))),
                    threadId)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<KernelStateTurnLogRecord>> ListTurnLogsByTurnIdAsync(string turnId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            return connection.QueryMany(
                    "SELECT id, thread_id, turn_id, phase, status, summary, payload_json, created_at_unix_ms FROM turn_log WHERE turn_id = ?1 ORDER BY id ASC;",
                    static row => new KernelStateTurnLogRecord(
                        Id: row.GetInt64(0),
                        ThreadId: row.GetString(1) ?? string.Empty,
                        TurnId: row.GetString(2),
                        Phase: row.GetString(3) ?? string.Empty,
                        Status: row.GetString(4) ?? string.Empty,
                        Summary: row.GetString(5),
                        PayloadJson: row.GetString(6) ?? string.Empty,
                        CreatedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(7))),
                    turnId)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<KernelStateRolloutRecord>> ListRolloutsAsync(string threadId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            return connection.QueryMany(
                    "SELECT rollout_key, thread_id, turn_id, source, rollout_path, preview, payload_json, updated_at_unix_ms FROM rollout_index WHERE thread_id = ?1 ORDER BY updated_at_unix_ms ASC;",
                    static row => new KernelStateRolloutRecord(
                        RolloutKey: row.GetString(0) ?? string.Empty,
                        ThreadId: row.GetString(1) ?? string.Empty,
                        TurnId: row.GetString(2),
                        Source: row.GetString(3) ?? string.Empty,
                        RolloutPath: row.GetString(4) ?? string.Empty,
                        Preview: row.GetString(5),
                        PayloadJson: row.GetString(6) ?? string.Empty,
                        UpdatedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(7))),
                    threadId)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpsertMemoryAsync(
        KernelThreadMemoryRecord memory,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            connection.Execute(
                "INSERT INTO stage1_outputs(thread_id, source_updated_at_unix_ms, raw_memory, rollout_summary, generated_at_unix_ms, usage_count, last_usage_at_unix_ms) " +
                "VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7) " +
                "ON CONFLICT(thread_id) DO UPDATE SET " +
                "source_updated_at_unix_ms=excluded.source_updated_at_unix_ms, raw_memory=excluded.raw_memory, rollout_summary=excluded.rollout_summary, generated_at_unix_ms=excluded.generated_at_unix_ms, usage_count=excluded.usage_count, last_usage_at_unix_ms=excluded.last_usage_at_unix_ms;",
                memory.ThreadId,
                memory.SourceUpdatedAt.ToUnixTimeMilliseconds(),
                memory.RawMemory,
                memory.RolloutSummary,
                memory.GeneratedAt.ToUnixTimeMilliseconds(),
                memory.UsageCount,
                memory.LastUsageAt?.ToUnixTimeMilliseconds());
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelThreadMemoryRecord?> GetMemoryAsync(string threadId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            var row = connection.QuerySingle(
                "SELECT thread_id, source_updated_at_unix_ms, raw_memory, rollout_summary, generated_at_unix_ms, usage_count, last_usage_at_unix_ms FROM stage1_outputs WHERE thread_id = ?1;",
                threadId);
            return row is null
                ? null
                : new KernelThreadMemoryRecord(
                    ThreadId: row.GetString(0) ?? string.Empty,
                    SourceUpdatedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(1)),
                    RawMemory: row.GetString(2) ?? string.Empty,
                    RolloutSummary: row.GetString(3) ?? string.Empty,
                    GeneratedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(4)),
                    UsageCount: row.GetInt64(5),
                    LastUsageAt: row.IsNull(6) ? null : DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(6)));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task MarkMemoryUsedAsync(string threadId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            connection.Execute(
                "UPDATE stage1_outputs SET usage_count = COALESCE(usage_count, 0) + 1, last_usage_at_unix_ms = ?2 WHERE thread_id = ?1;",
                threadId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<long> ClearMemoriesAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            var countRow = connection.QuerySingle("SELECT COUNT(*) FROM stage1_outputs;");
            var clearedCount = countRow?.GetInt64(0) ?? 0;
            connection.Execute("DELETE FROM stage1_outputs;");
            return clearedCount;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpsertAgentJobAsync(KernelAgentJobRecord job, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            connection.Execute(
                "INSERT INTO agent_jobs(id, name, status, instruction, output_schema_json, input_headers_json, input_csv_path, output_csv_path, auto_export, max_runtime_seconds, created_at_unix_ms, updated_at_unix_ms, started_at_unix_ms, completed_at_unix_ms, last_error) " +
                "VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15) " +
                "ON CONFLICT(id) DO UPDATE SET " +
                "name=excluded.name, status=excluded.status, instruction=excluded.instruction, output_schema_json=excluded.output_schema_json, input_headers_json=excluded.input_headers_json, input_csv_path=excluded.input_csv_path, output_csv_path=excluded.output_csv_path, auto_export=excluded.auto_export, max_runtime_seconds=excluded.max_runtime_seconds, updated_at_unix_ms=excluded.updated_at_unix_ms, started_at_unix_ms=excluded.started_at_unix_ms, completed_at_unix_ms=excluded.completed_at_unix_ms, last_error=excluded.last_error;",
                job.Id,
                job.Name,
                job.Status,
                job.Instruction,
                job.OutputSchemaJson,
                job.InputHeadersJson,
                job.InputCsvPath,
                job.OutputCsvPath,
                job.AutoExport ? 1L : 0L,
                job.MaxRuntimeSeconds,
                job.CreatedAt.ToUnixTimeMilliseconds(),
                job.UpdatedAt.ToUnixTimeMilliseconds(),
                job.StartedAt?.ToUnixTimeMilliseconds(),
                job.CompletedAt?.ToUnixTimeMilliseconds(),
                job.LastError);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelAgentJobRecord?> GetAgentJobAsync(string jobId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            var row = connection.QuerySingle(
                "SELECT id, name, status, instruction, output_schema_json, input_headers_json, input_csv_path, output_csv_path, auto_export, max_runtime_seconds, created_at_unix_ms, updated_at_unix_ms, started_at_unix_ms, completed_at_unix_ms, last_error FROM agent_jobs WHERE id = ?1;",
                jobId);
            return row is null ? null : ReadAgentJob(row);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<KernelAgentJobRecord>> ListAgentJobsAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            return connection.QueryMany(
                    "SELECT id, name, status, instruction, output_schema_json, input_headers_json, input_csv_path, output_csv_path, auto_export, max_runtime_seconds, created_at_unix_ms, updated_at_unix_ms, started_at_unix_ms, completed_at_unix_ms, last_error FROM agent_jobs ORDER BY updated_at_unix_ms DESC, created_at_unix_ms DESC;",
                    static row => ReadAgentJob(row))
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelAgentJobItemRecord?> GetAgentJobItemAsync(
        string jobId,
        string itemId,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            var row = connection.QuerySingle(
                "SELECT job_id, item_id, row_index, source_id, row_json, status, assigned_thread_id, attempt_count, result_json, last_error, created_at_unix_ms, updated_at_unix_ms, completed_at_unix_ms, reported_at_unix_ms FROM agent_job_items WHERE job_id = ?1 AND item_id = ?2;",
                jobId,
                itemId);
            return row is null ? null : ReadAgentJobItem(row);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpsertAgentJobItemAsync(KernelAgentJobItemRecord item, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            connection.Execute(
                "INSERT INTO agent_job_items(job_id, item_id, row_index, source_id, row_json, status, assigned_thread_id, attempt_count, result_json, last_error, created_at_unix_ms, updated_at_unix_ms, completed_at_unix_ms, reported_at_unix_ms) " +
                "VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14) " +
                "ON CONFLICT(job_id, item_id) DO UPDATE SET " +
                "row_index=excluded.row_index, source_id=excluded.source_id, row_json=excluded.row_json, status=excluded.status, assigned_thread_id=excluded.assigned_thread_id, attempt_count=excluded.attempt_count, result_json=excluded.result_json, last_error=excluded.last_error, updated_at_unix_ms=excluded.updated_at_unix_ms, completed_at_unix_ms=excluded.completed_at_unix_ms, reported_at_unix_ms=excluded.reported_at_unix_ms;",
                item.JobId,
                item.ItemId,
                item.RowIndex,
                item.SourceId,
                item.RowJson,
                item.Status,
                item.AssignedThreadId,
                item.AttemptCount,
                item.ResultJson,
                item.LastError,
                item.CreatedAt.ToUnixTimeMilliseconds(),
                item.UpdatedAt.ToUnixTimeMilliseconds(),
                item.CompletedAt?.ToUnixTimeMilliseconds(),
                item.ReportedAt?.ToUnixTimeMilliseconds());
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<KernelAgentJobItemRecord>> ListAgentJobItemsAsync(string jobId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            return connection.QueryMany(
                    "SELECT job_id, item_id, row_index, source_id, row_json, status, assigned_thread_id, attempt_count, result_json, last_error, created_at_unix_ms, updated_at_unix_ms, completed_at_unix_ms, reported_at_unix_ms FROM agent_job_items WHERE job_id = ?1 ORDER BY row_index ASC;",
                    static row => ReadAgentJobItem(row),
                    jobId)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<KernelAgentJobItemRecord>> ListAgentJobItemsByStatusAsync(
        string jobId,
        string status,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            return connection.QueryMany(
                    "SELECT job_id, item_id, row_index, source_id, row_json, status, assigned_thread_id, attempt_count, result_json, last_error, created_at_unix_ms, updated_at_unix_ms, completed_at_unix_ms, reported_at_unix_ms FROM agent_job_items WHERE job_id = ?1 AND status = ?2 ORDER BY row_index ASC;",
                    static row => ReadAgentJobItem(row),
                    jobId,
                    status)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<KernelAgentJobProgressRecord> GetAgentJobProgressAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            using var connection = KernelNativeSqliteConnection.Open(databasePath);
            var row = connection.QuerySingle(
                "SELECT COUNT(*), " +
                "COALESCE(SUM(CASE WHEN status = 'pending' THEN 1 ELSE 0 END), 0), " +
                "COALESCE(SUM(CASE WHEN status = 'running' THEN 1 ELSE 0 END), 0), " +
                "COALESCE(SUM(CASE WHEN status = 'completed' THEN 1 ELSE 0 END), 0), " +
                "COALESCE(SUM(CASE WHEN status = 'failed' THEN 1 ELSE 0 END), 0) " +
                "FROM agent_job_items WHERE job_id = ?1;",
                jobId);
            return row is null
                ? new KernelAgentJobProgressRecord(jobId, 0, 0, 0, 0, 0)
                : new KernelAgentJobProgressRecord(
                    JobId: jobId,
                    TotalItems: (int)row.GetInt64(0),
                    PendingItems: (int)row.GetInt64(1),
                    RunningItems: (int)row.GetInt64(2),
                    CompletedItems: (int)row.GetInt64(3),
                    FailedItems: (int)row.GetInt64(4));
        }
        finally
        {
            gate.Release();
        }
    }

    private void EnsureInitialized()
    {
        if (!initialized)
        {
            throw new InvalidOperationException("KernelStateSqliteStore is not initialized.");
        }
    }

    private static void EnsureAgentJobSchema(KernelNativeSqliteConnection connection)
    {
        EnsureTableColumn(connection, "agent_jobs", "max_runtime_seconds", "INTEGER NULL");
    }

    private static void EnsureTableColumn(
        KernelNativeSqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        var columns = connection.QueryMany(
                $"PRAGMA table_info({tableName});",
                static row => row.GetString(1) ?? string.Empty)
            .ToArray();
        if (columns.Any(column => string.Equals(column, columnName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        connection.Execute($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};");
    }

    private static KernelAgentJobItemRecord ReadAgentJobItem(KernelNativeSqliteConnection.KernelNativeSqliteRow row)
        => new(
            JobId: row.GetString(0) ?? string.Empty,
            ItemId: row.GetString(1) ?? string.Empty,
            RowIndex: (int)row.GetInt64(2),
            SourceId: row.GetString(3),
            RowJson: row.GetString(4) ?? string.Empty,
            Status: row.GetString(5) ?? string.Empty,
            AssignedThreadId: row.GetString(6),
            AttemptCount: (int)row.GetInt64(7),
            ResultJson: row.GetString(8),
            LastError: row.GetString(9),
            CreatedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(10)),
            UpdatedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(11)),
            CompletedAt: row.IsNull(12) ? null : DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(12)),
            ReportedAt: row.IsNull(13) ? null : DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(13)));

    private static KernelAgentJobRecord ReadAgentJob(KernelNativeSqliteConnection.KernelNativeSqliteRow row)
        => new(
            Id: row.GetString(0) ?? string.Empty,
            Name: row.GetString(1) ?? string.Empty,
            Status: row.GetString(2) ?? string.Empty,
            Instruction: row.GetString(3) ?? string.Empty,
            OutputSchemaJson: row.GetString(4),
            InputHeadersJson: row.GetString(5) ?? string.Empty,
            InputCsvPath: row.GetString(6) ?? string.Empty,
            OutputCsvPath: row.GetString(7) ?? string.Empty,
            AutoExport: row.GetInt64(8) != 0,
            MaxRuntimeSeconds: row.IsNull(9) ? null : (int)row.GetInt64(9),
            CreatedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(10)),
            UpdatedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(11)),
            StartedAt: row.IsNull(12) ? null : DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(12)),
            CompletedAt: row.IsNull(13) ? null : DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(13)),
            LastError: row.GetString(14));

    private static KernelStoredThreadStateRecord ReadThreadState(KernelNativeSqliteConnection.KernelNativeSqliteRow row)
        => new(
            ThreadId: row.GetString(0) ?? string.Empty,
            Cwd: row.GetString(1) ?? string.Empty,
            CreatedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(2)),
            UpdatedAt: DateTimeOffset.FromUnixTimeMilliseconds(row.GetInt64(3)),
            StatusType: row.GetString(4) ?? string.Empty,
            IsArchived: row.GetInt64(5) != 0,
            Name: row.GetString(6),
            PayloadJson: row.GetString(7) ?? string.Empty);
}

internal sealed class KernelNativeSqliteConnection : IDisposable
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;
    private const int SqliteInteger = 1;
    private const int SqliteText = 3;
    private const int SqliteNull = 5;
    private const int OpenReadWrite = 0x00000002;
    private const int OpenCreate = 0x00000004;
    private const int OpenFullMutex = 0x00010000;
    private static readonly IntPtr SqliteTransient = new(-1);

    private IntPtr db;

    private KernelNativeSqliteConnection(IntPtr db)
    {
        this.db = db;
    }

    public static KernelNativeSqliteConnection Open(string databasePath)
    {
        var rc = sqlite3_open_v2(databasePath, out var db, OpenReadWrite | OpenCreate | OpenFullMutex, IntPtr.Zero);
        if (rc != SqliteOk)
        {
            var message = db == IntPtr.Zero ? $"sqlite open failed: {rc}" : GetErrorMessage(db);
            if (db != IntPtr.Zero)
            {
                sqlite3_close(db);
            }

            throw new InvalidOperationException(message);
        }

        sqlite3_busy_timeout(db, 5000);
        return new KernelNativeSqliteConnection(db);
    }

    public void ExecuteBatch(string sql)
    {
        var rc = sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, out var errMsg);
        if (rc != SqliteOk)
        {
            var message = errMsg == IntPtr.Zero ? GetErrorMessage(db) : Marshal.PtrToStringUTF8(errMsg) ?? GetErrorMessage(db);
            if (errMsg != IntPtr.Zero)
            {
                sqlite3_free(errMsg);
            }

            throw new InvalidOperationException(message);
        }
    }

    public void Execute(string sql, params object?[] args)
    {
        using var statement = Prepare(sql, args);
        var rc = sqlite3_step(statement.Handle);
        if (rc != SqliteDone)
        {
            throw new InvalidOperationException(GetErrorMessage(db));
        }
    }

    public KernelNativeSqliteRow? QuerySingle(string sql, params object?[] args)
    {
        using var statement = Prepare(sql, args);
        var rc = sqlite3_step(statement.Handle);
        return rc switch
        {
            SqliteRow => KernelNativeSqliteRow.Read(statement.Handle),
            SqliteDone => null,
            _ => throw new InvalidOperationException(GetErrorMessage(db)),
        };
    }

    public IEnumerable<T> QueryMany<T>(string sql, Func<KernelNativeSqliteRow, T> projector, params object?[] args)
    {
        using var statement = Prepare(sql, args);
        while (true)
        {
            var rc = sqlite3_step(statement.Handle);
            if (rc == SqliteDone)
            {
                yield break;
            }

            if (rc != SqliteRow)
            {
                throw new InvalidOperationException(GetErrorMessage(db));
            }

            yield return projector(KernelNativeSqliteRow.Read(statement.Handle));
        }
    }

    public void Dispose()
    {
        if (db == IntPtr.Zero)
        {
            return;
        }

        sqlite3_close(db);
        db = IntPtr.Zero;
    }

    private KernelNativeSqliteStatement Prepare(string sql, object?[] args)
    {
        var rc = sqlite3_prepare_v2(db, sql, -1, out var stmt, IntPtr.Zero);
        if (rc != SqliteOk)
        {
            throw new InvalidOperationException(GetErrorMessage(db));
        }

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                BindValue(stmt, i + 1, args[i]);
            }

            return new KernelNativeSqliteStatement(stmt);
        }
        catch
        {
            sqlite3_finalize(stmt);
            throw;
        }
    }

    private static void BindValue(IntPtr stmt, int index, object? value)
    {
        int rc;
        switch (value)
        {
            case null:
                rc = sqlite3_bind_null(stmt, index);
                break;
            case int intValue:
                rc = sqlite3_bind_int64(stmt, index, intValue);
                break;
            case long longValue:
                rc = sqlite3_bind_int64(stmt, index, longValue);
                break;
            case bool boolValue:
                rc = sqlite3_bind_int64(stmt, index, boolValue ? 1 : 0);
                break;
            default:
                rc = sqlite3_bind_text(stmt, index, value.ToString(), -1, SqliteTransient);
                break;
        }

        if (rc != SqliteOk)
        {
            throw new InvalidOperationException($"sqlite bind failed at index {index}: {rc}");
        }
    }

    private static string GetErrorMessage(IntPtr db)
        => Marshal.PtrToStringUTF8(sqlite3_errmsg(db)) ?? "sqlite error";

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
        out IntPtr db,
        int flags,
        IntPtr zvfs);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(
        IntPtr db,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
        IntPtr callback,
        IntPtr arg,
        out IntPtr errMsg);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern void sqlite3_free(IntPtr ptr);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(
        IntPtr db,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
        int numBytes,
        out IntPtr statement,
        IntPtr tail);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_null(IntPtr statement, int index);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_text(
        IntPtr statement,
        int index,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? value,
        int valueLength,
        IntPtr destructor);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_count(IntPtr statement);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_type(IntPtr statement, int index);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr statement, int index);

    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int index);

    private sealed class KernelNativeSqliteStatement : IDisposable
    {
        public KernelNativeSqliteStatement(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public void Dispose()
        {
            sqlite3_finalize(Handle);
        }
    }

    public sealed class KernelNativeSqliteRow
    {
        private readonly object?[] values;

        private KernelNativeSqliteRow(object?[] values)
        {
            this.values = values;
        }

        public static KernelNativeSqliteRow Read(IntPtr statement)
        {
            var count = sqlite3_column_count(statement);
            var values = new object?[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = sqlite3_column_type(statement, i) switch
                {
                    SqliteNull => null,
                    SqliteInteger => sqlite3_column_int64(statement, i),
                    SqliteText => Marshal.PtrToStringUTF8(sqlite3_column_text(statement, i)),
                    _ => Marshal.PtrToStringUTF8(sqlite3_column_text(statement, i)),
                };
            }

            return new KernelNativeSqliteRow(values);
        }

        public bool IsNull(int index) => values[index] is null;

        public long GetInt64(int index) => values[index] switch
        {
            long longValue => longValue,
            int intValue => intValue,
            string text when long.TryParse(text, out var parsed) => parsed,
            _ => 0,
        };

        public string? GetString(int index) => values[index]?.ToString();
    }
}









