using System.Text.Json;
using TianShu.Cli.Interaction.Projection;

namespace TianShu.Cli.Interaction.Recording;

internal sealed class ChatCommandArtifactsWriter(JsonSerializerOptions jsonOptions)
{
    private static readonly JsonSerializerOptions JsonlOptions = new(JsonSerializerDefaults.Web);

    public async Task<ChatCommandArtifactResult> WriteAsync(
        string artifactsRoot,
        ChatCommandSummary summary,
        ProbeResolvedOptions resolvedOptions,
        IReadOnlyList<ProbeEventRecord> events,
        IReadOnlyList<ChatProjectionRecord> projectionRecords,
        IReadOnlyList<CliTranscriptRecord> transcriptRecords,
        string commandsText,
        string transcriptText,
        CancellationToken cancellationToken)
    {
        var runDirectory = CreateRunDirectory(artifactsRoot);
        Directory.CreateDirectory(runDirectory);

        var summaryPath = Path.Combine(runDirectory, "summary.json");
        var resolvedOptionsPath = Path.Combine(runDirectory, "resolved-options.json");
        var eventsPath = Path.Combine(runDirectory, "events.jsonl");
        var projectionRecordsPath = Path.Combine(runDirectory, "projection-records.jsonl");
        var transcriptRecordsPath = Path.Combine(runDirectory, "transcript-records.jsonl");
        var commandsPath = Path.Combine(runDirectory, "commands.txt");
        var transcriptPath = Path.Combine(runDirectory, "transcript.txt");

        await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary, jsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(resolvedOptionsPath, JsonSerializer.Serialize(resolvedOptions, jsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(commandsPath, commandsText, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(transcriptPath, transcriptText, cancellationToken).ConfigureAwait(false);

        await WriteJsonLinesAsync(eventsPath, events, serializerOptions: null, cancellationToken).ConfigureAwait(false);
        await WriteJsonLinesAsync(projectionRecordsPath, projectionRecords, JsonlOptions, cancellationToken).ConfigureAwait(false);
        await WriteJsonLinesAsync(transcriptRecordsPath, transcriptRecords, JsonlOptions, cancellationToken).ConfigureAwait(false);

        return new ChatCommandArtifactResult(runDirectory);
    }

    public Task RewriteSummaryAsync(string runDirectory, ChatCommandSummary summary, CancellationToken cancellationToken)
    {
        var summaryPath = Path.Combine(runDirectory, "summary.json");
        var json = JsonSerializer.Serialize(summary, jsonOptions);
        return File.WriteAllTextAsync(summaryPath, json, cancellationToken);
    }

    private static string CreateRunDirectory(string artifactsRoot)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmssfff");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(Path.GetFullPath(artifactsRoot), $"{timestamp}-{suffix}");
    }

    private static async Task WriteJsonLinesAsync<T>(
        string path,
        IReadOnlyList<T> records,
        JsonSerializerOptions? serializerOptions,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream);
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = serializerOptions is null
                ? JsonSerializer.Serialize(record)
                : JsonSerializer.Serialize(record, serializerOptions);
            await writer.WriteLineAsync(json).ConfigureAwait(false);
        }
    }
}

internal sealed record ChatCommandArtifactResult(string RunDirectory);

internal sealed class ChatCommandSummary
{
    public bool Success { get; set; }

    public int ExitCode { get; set; }

    public string ExitCodeName { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public long DurationMs { get; set; }

    public string WorkingDirectory { get; set; } = string.Empty;

    public string ConfigFilePath { get; set; } = string.Empty;

    public string? ProfileName { get; set; }

    public bool ApproveAll { get; set; }

    public string? PermissionsJsonPath { get; set; }

    public string? UserInputJsonPath { get; set; }

    public string? CollaborationMode { get; set; }

    public string? RequestedResumeThreadId { get; set; }

    public bool ResumeLatestThread { get; set; }

    public bool ResumeLatestMatchCwd { get; set; } = true;

    public string? AppHostProjectPath { get; set; }

    public string? ThreadId { get; set; }

    public string? TurnId { get; set; }

    public string? TurnStatus { get; set; }

    public string? OutputProtocol { get; set; }

    public string? InitialMessage { get; set; }

    public string? ScriptPath { get; set; }

    public int CommandCount { get; set; }

    public int EventCount { get; set; }

    public int ProjectionRecordCount { get; set; }

    public int TranscriptRecordCount { get; set; }

    public int FailureCount { get; set; }

    public string ResultText { get; set; } = string.Empty;

    public string? FailureMessage { get; set; }

    public string ArtifactsDirectory { get; set; } = string.Empty;
}
