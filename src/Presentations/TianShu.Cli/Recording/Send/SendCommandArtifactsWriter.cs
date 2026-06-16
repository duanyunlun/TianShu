using System.Text.Json;

namespace TianShu.Cli;

internal sealed class SendCommandArtifactsWriter(JsonSerializerOptions jsonOptions)
{
    public async Task<SendCommandArtifactResult> WriteAsync(
        SendCommandOptions probeOptions,
        ProbeSummary summary,
        ProbeResolvedOptions resolvedOptions,
        IReadOnlyList<ProbeEventRecord> events,
        string requestText,
        string resultText,
        CancellationToken cancellationToken)
    {
        var runDirectory = CreateRunDirectory(probeOptions.ArtifactsRoot);
        Directory.CreateDirectory(runDirectory);

        var summaryPath = Path.Combine(runDirectory, "summary.json");
        var resolvedOptionsPath = Path.Combine(runDirectory, "resolved-options.json");
        var eventsPath = Path.Combine(runDirectory, "events.jsonl");
        var requestPath = Path.Combine(runDirectory, "request.txt");
        var resultPath = Path.Combine(runDirectory, "result.txt");

        await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary, jsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(resolvedOptionsPath, JsonSerializer.Serialize(resolvedOptions, jsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(requestPath, requestText, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(resultPath, resultText, cancellationToken).ConfigureAwait(false);

        await using (var stream = new FileStream(eventsPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        await using (var writer = new StreamWriter(stream))
        {
            foreach (var eventRecord in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(JsonSerializer.Serialize(eventRecord)).ConfigureAwait(false);
            }
        }

        return new SendCommandArtifactResult(runDirectory);
    }

    public Task RewriteSummaryAsync(string runDirectory, ProbeSummary summary, CancellationToken cancellationToken)
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
}

internal sealed record SendCommandArtifactResult(string RunDirectory);


