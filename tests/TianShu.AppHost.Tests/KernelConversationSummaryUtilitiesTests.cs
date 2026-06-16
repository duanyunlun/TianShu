namespace TianShu.AppHost.Tests;

public sealed class KernelConversationSummaryUtilitiesTests
{
    [Fact]
    public void ReadRolloutPreview_ShouldReturnFirstNonEmptyTrimmedLine()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tianshu-rollout-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, Environment.NewLine + "  first line  " + Environment.NewLine + "second");

        try
        {
            var preview = KernelConversationSummaryUtilities.ReadRolloutPreview(path);

            Assert.Equal("first line", preview);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void BuildConversationSummaryPayload_ShouldShapeFieldsAndGitInfo()
    {
        var payload = KernelConversationSummaryUtilities.BuildConversationSummaryPayload(
            conversationId: "thread-001",
            path: "D:/repo/rollout.md",
            preview: "summary",
            timestamp: new DateTime(2026, 4, 25, 1, 2, 3, DateTimeKind.Utc),
            updatedAt: new DateTime(2026, 4, 25, 4, 5, 6, DateTimeKind.Utc),
            modelProvider: "openai",
            cwd: "D:/repo",
            source: "rollout",
            cliVersion: "0.1.0",
            gitSha: "abc123",
            gitBranch: "main",
            gitOriginUrl: "https://example.com/repo.git");

        Assert.Equal("thread-001", payload.ConversationId);
        Assert.Equal("D:/repo/rollout.md", payload.Path);
        Assert.Equal("summary", payload.Preview);
        Assert.Equal("2026-04-25T01:02:03.0000000+00:00", payload.Timestamp);
        Assert.Equal("2026-04-25T04:05:06.0000000+00:00", payload.UpdatedAt);
        Assert.Equal("openai", payload.ModelProvider);
        Assert.Equal("D:/repo", payload.Cwd);
        Assert.Equal("0.1.0", payload.CliVersion);
        Assert.Equal("rollout", payload.Source);
        Assert.NotNull(payload.GitInfo);
        Assert.Equal("abc123", payload.GitInfo!.Sha);
        Assert.Equal("main", payload.GitInfo.Branch);
        Assert.Equal("https://example.com/repo.git", payload.GitInfo.OriginUrl);
    }

    [Fact]
    public void BuildConversationSummaryPayload_ShouldOmitGitInfo_WhenMissing()
    {
        var payload = KernelConversationSummaryUtilities.BuildConversationSummaryPayload(
            conversationId: "thread-002",
            path: "D:/repo/thread",
            preview: string.Empty,
            timestamp: new DateTime(2026, 4, 25, 1, 2, 3, DateTimeKind.Utc),
            updatedAt: new DateTime(2026, 4, 25, 4, 5, 6, DateTimeKind.Utc),
            modelProvider: "openai",
            cwd: "D:/repo",
            source: "appServer",
            cliVersion: "0.1.0");

        Assert.Null(payload.GitInfo);
    }
}
