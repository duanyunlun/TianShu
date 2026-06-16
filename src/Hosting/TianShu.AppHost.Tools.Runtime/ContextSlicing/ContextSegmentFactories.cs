using System.Text;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record ContextSummaryTrace(
    string SummaryId,
    string GenerationMode,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ContextSourceRef> SourceRefs,
    double Confidence);

internal sealed record ContextToolArtifactSliceRequest
{
    public required string SegmentId { get; init; }

    public string? ToolName { get; init; }

    public string? Command { get; init; }

    public string? Cwd { get; init; }

    public int? ExitCode { get; init; }

    public TimeSpan? Duration { get; init; }

    public string? Stdout { get; init; }

    public string? Stderr { get; init; }

    public string? Path { get; init; }

    public string? ArtifactRef { get; init; }

    public int MaxTextChars { get; init; } = 4_000;

    public IReadOnlyList<ContextSourceRef> SourceRefs { get; init; } = [];
}

internal static class ContextSegmentFactories
{
    public static ContextSegment CreateHistoricalSummary(
        string id,
        string text,
        ContextSummaryTrace trace,
        int estimatedTokens = 0)
        => new()
        {
            Id = id,
            Kind = ContextSegmentKind.HistoricalSummary,
            Priority = ContextSegmentPriority.Medium,
            RetentionPolicy = ContextRetentionPolicy.KeepIfRelevant,
            Text = text,
            EstimatedTokens = estimatedTokens,
            Confidence = trace.Confidence,
            AuthorityWeight = 10,
            RelevanceScore = 0.65d,
            SourceRefs = trace.SourceRefs,
            Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["summaryId"] = trace.SummaryId,
                ["generationMode"] = trace.GenerationMode,
                ["generatedAtUtc"] = trace.GeneratedAtUtc,
                ["summaryIsFact"] = false,
            },
        };

    public static ContextSegment CreateMemoryOverlay(
        string id,
        string text,
        IReadOnlyList<ContextSourceRef> sourceRefs,
        string? contextSignature,
        bool isCounterexample = false,
        double confidence = 1.0d,
        int estimatedTokens = 0)
        => new()
        {
            Id = id,
            Kind = ContextSegmentKind.MemoryOverlay,
            Priority = ContextSegmentPriority.MediumHigh,
            RetentionPolicy = ContextRetentionPolicy.KeepIfRelevant,
            Text = text,
            EstimatedTokens = estimatedTokens,
            Confidence = confidence,
            AuthorityWeight = isCounterexample ? 70 : 50,
            RelevanceScore = 0.85d,
            SourceRefs = sourceRefs,
            Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contextSignature"] = contextSignature,
                ["isCounterexample"] = isCounterexample,
            },
        };

    public static ContextSegment CreateToolArtifactSlice(
        ContextToolArtifactSliceRequest request,
        IContextTokenEstimator? estimator = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = BuildToolArtifactSliceText(request);
        var segment = new ContextSegment
        {
            Id = request.SegmentId,
            Kind = ContextSegmentKind.ToolResult,
            Priority = ContextSegmentPriority.High,
            RetentionPolicy = ContextRetentionPolicy.KeepIfRelevant,
            Text = text,
            AuthorityWeight = 80,
            RelevanceScore = request.ExitCode is > 0 ? 1.0d : 0.8d,
            SourceRefs = request.SourceRefs,
            Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["toolName"] = request.ToolName,
                ["command"] = request.Command,
                ["cwd"] = request.Cwd,
                ["exitCode"] = request.ExitCode,
                ["durationMs"] = request.Duration?.TotalMilliseconds,
                ["path"] = request.Path,
                ["artifactRef"] = request.ArtifactRef,
            },
        };

        var effectiveEstimator = estimator ?? new ApproximateContextTokenEstimator();
        return segment with { EstimatedTokens = effectiveEstimator.Estimate(segment).Tokens };
    }

    private static string BuildToolArtifactSliceText(ContextToolArtifactSliceRequest request)
    {
        var builder = new StringBuilder();
        AppendLineIfPresent(builder, "tool", request.ToolName);
        AppendLineIfPresent(builder, "command", request.Command);
        AppendLineIfPresent(builder, "cwd", request.Cwd);
        if (request.ExitCode is not null)
        {
            builder.Append("exitCode: ").Append(request.ExitCode.Value).AppendLine();
        }

        if (request.Duration is not null)
        {
            builder.Append("durationMs: ").Append((long)request.Duration.Value.TotalMilliseconds).AppendLine();
        }

        AppendLineIfPresent(builder, "path", request.Path);
        AppendLineIfPresent(builder, "artifactRef", request.ArtifactRef);
        AppendSection(builder, "stderr", request.Stderr, request.MaxTextChars / 2);
        AppendSection(builder, "stdout", request.Stdout, request.MaxTextChars / 2);
        return builder.ToString().TrimEnd();
    }

    private static void AppendLineIfPresent(StringBuilder builder, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append(name).Append(": ").Append(value).AppendLine();
        }
    }

    private static void AppendSection(StringBuilder builder, string name, string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        builder.Append(name).AppendLine(":");
        builder.AppendLine(SliceTextHeadTail(value, Math.Max(200, maxChars)));
    }

    private static string SliceTextHeadTail(string value, int maxChars)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        var headLength = Math.Max(1, maxChars / 2);
        var tailLength = Math.Max(1, maxChars - headLength);
        var omitted = value.Length - headLength - tailLength;
        return string.Concat(
            value[..headLength],
            Environment.NewLine,
            $"[... omitted {omitted} chars; full output is available via artifact/ref ...]",
            Environment.NewLine,
            value[^tailLength..]);
    }
}
