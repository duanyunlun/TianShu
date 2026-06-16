namespace TianShu.AppHost.Tools;

/// <summary>
/// 会话摘要与 rollout preview 辅助件。
/// Helpers for conversation summary payload shaping and rollout preview reading.
/// </summary>
internal static class KernelConversationSummaryUtilities
{
    public static string ReadRolloutPreview(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var normalized = KernelToolJsonHelpers.Normalize(line);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            return normalized!.Length > 200 ? normalized[..200] : normalized;
        }

        return string.Empty;
    }

    public static KernelConversationSummaryPayload BuildConversationSummaryPayload(
        string conversationId,
        string path,
        string preview,
        DateTime timestamp,
        DateTime updatedAt,
        string modelProvider,
        string cwd,
        string source,
        string cliVersion,
        string? gitSha = null,
        string? gitBranch = null,
        string? gitOriginUrl = null)
        => new(
            conversationId,
            path,
            preview,
            new DateTimeOffset(timestamp, TimeSpan.Zero).ToString("o"),
            new DateTimeOffset(updatedAt, TimeSpan.Zero).ToString("o"),
            modelProvider,
            cwd,
            cliVersion,
            source,
            string.IsNullOrWhiteSpace(gitSha)
                && string.IsNullOrWhiteSpace(gitBranch)
                && string.IsNullOrWhiteSpace(gitOriginUrl)
                ? null
                : new KernelConversationSummaryGitInfoPayload(gitSha, gitBranch, gitOriginUrl));
}

internal sealed record KernelConversationSummaryPayload(
    string ConversationId,
    string Path,
    string Preview,
    string Timestamp,
    string UpdatedAt,
    string ModelProvider,
    string Cwd,
    string CliVersion,
    string Source,
    KernelConversationSummaryGitInfoPayload? GitInfo);

internal sealed record KernelConversationSummaryGitInfoPayload(
    string? Sha,
    string? Branch,
    string? OriginUrl);
