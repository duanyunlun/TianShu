using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

internal interface IContextTokenEstimator
{
    ContextTokenEstimate Estimate(ContextSegment segment);

    int EstimateTextTokens(string? text);
}

internal sealed class ApproximateContextTokenEstimator : IContextTokenEstimator
{
    private const string DefaultEstimatorName = "approximate-v1";
    private readonly double asciiCharsPerToken;

    public ApproximateContextTokenEstimator(string? providerId = null)
    {
        asciiCharsPerToken = ResolveAsciiCharsPerToken(providerId);
    }

    public ContextTokenEstimate Estimate(ContextSegment segment)
    {
        if (segment.EstimatedTokens > 0)
        {
            return new ContextTokenEstimate(segment.EstimatedTokens, "provided", IsApproximate: false);
        }

        var tokens = EstimateTextTokens(segment.Text);
        if (segment.StructuredContent is not null)
        {
            tokens += EstimateTextTokens(JsonSerializer.Serialize(segment.StructuredContent));
        }

        if (tokens == 0)
        {
            tokens = 1;
        }

        return new ContextTokenEstimate(tokens, DefaultEstimatorName);
    }

    public int EstimateTextTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var cjkChars = 0;
        var asciiChars = 0;
        foreach (var ch in text)
        {
            if (ch <= 0x7f)
            {
                asciiChars++;
            }
            else
            {
                cjkChars++;
            }
        }

        var asciiTokens = asciiChars / asciiCharsPerToken;
        var cjkTokens = cjkChars / 1.8d;
        return Math.Max(1, (int)Math.Ceiling(asciiTokens + cjkTokens));
    }

    private static double ResolveAsciiCharsPerToken(string? providerId)
    {
        if (providerId is null)
        {
            return 3.0d;
        }

        if (providerId.Contains("openai", StringComparison.OrdinalIgnoreCase)
            || providerId.Contains("gpt", StringComparison.OrdinalIgnoreCase)
            || providerId.Contains("anthropic", StringComparison.OrdinalIgnoreCase)
            || providerId.Contains("claude", StringComparison.OrdinalIgnoreCase))
        {
            return 4.0d;
        }

        return 3.0d;
    }
}
