using System.Text.Json;
using TianShu.AppHost.Configuration;
using TianShu.AppHost.Tools;
using TianShu.Configuration;

namespace TianShu.AppHost.Tests;

public sealed class KernelReviewAppHostUtilitiesTests
{
    [Fact]
    public void TryBuildReviewPrompt_ShouldBuildCommitPromptWithTitle()
    {
        var @params = JsonSerializer.SerializeToElement(new
        {
            target = new
            {
                type = "commit",
                sha = "1234567890abcdef",
                title = "Fix review flow",
            },
        });

        var success = KernelReviewAppHostUtilities.TryBuildReviewPrompt(
            @params,
            out var prompt,
            out var displayText,
            out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("commit 1234567: Fix review flow", displayText);
        Assert.Equal("请审查提交 1234567890abcdef（Fix review flow）的改动并给出发现。", prompt);
    }

    [Fact]
    public void TryBuildReviewPrompt_WhenPromptConfigured_ShouldRenderConfiguredCommitTemplate()
    {
        var @params = JsonSerializer.SerializeToElement(new
        {
            target = new
            {
                type = "commit",
                sha = "1234567890abcdef",
                title = "Fix review flow",
            },
        });
        var promptConfiguration = TianShuPromptConfiguration.Empty with
        {
            ReviewCommitPrompt = "自定义审查提交 {sha} / {title}",
        };

        var success = KernelReviewAppHostUtilities.TryBuildReviewPrompt(
            @params,
            promptConfiguration,
            out var prompt,
            out _,
            out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("自定义审查提交 1234567890abcdef / Fix review flow", prompt);
    }

    [Fact]
    public void BuildReviewTurnItems_ShouldReturnUserMessagePayload()
    {
        var payload = KernelReviewAppHostUtilities.BuildReviewTurnItems("turn_123", "Review current diff");

        var item = Assert.Single(payload);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(item));
        Assert.Equal("turn_123", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("userMessage", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "Review current diff",
            json.RootElement.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ResolveDetachedReviewModelAsync_ShouldReadFirstConfiguredReviewModel()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["reviewModel"] = "\"o3\"",
            ["review.model"] = "\"ignored\"",
        };

        var model = await KernelReviewAppHostUtilities.ResolveDetachedReviewModelAsync(
            @"D:\Repo",
            (cwd, cancellationToken) =>
            {
                Assert.Equal(@"D:\Repo", cwd);
                return Task.FromResult(values);
            },
            CancellationToken.None);

        Assert.Equal("o3", model);
    }

    [Fact]
    public async Task EnrichReviewPromptWithTargetContextAsync_ShouldAppendUncommittedGitContext()
    {
        var @params = JsonSerializer.SerializeToElement(new
        {
            target = new
            {
                type = "uncommittedChanges",
            },
        });

        var commands = new List<string>();
        var prompt = await KernelReviewAppHostUtilities.EnrichReviewPromptWithTargetContextAsync(
            @params,
            "请审查当前变更。",
            @"D:\Repo",
            (command, cwd, cancellationToken) =>
            {
                Assert.Equal(@"D:\Repo", cwd);
                commands.Add(string.Join(" ", command));
                var text = command[1] switch
                {
                    "status" => "M src/Program.cs",
                    "diff" when command.Contains("--staged", StringComparer.Ordinal) => "staged diff",
                    _ => "unstaged diff",
                };

                return Task.FromResult(new KernelReviewCommandResult(0, text, string.Empty, false));
            },
            CancellationToken.None);

        Assert.Equal(3, commands.Count);
        Assert.Contains("[status]", prompt, StringComparison.Ordinal);
        Assert.Contains("M src/Program.cs", prompt, StringComparison.Ordinal);
        Assert.Contains("[staged]", prompt, StringComparison.Ordinal);
        Assert.Contains("[unstaged]", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnrichReviewPromptWithTargetContextAsync_WhenPromptConfigured_ShouldUseConfiguredIntro()
    {
        var @params = JsonSerializer.SerializeToElement(new
        {
            target = new
            {
                type = "uncommittedChanges",
            },
        });
        var promptConfiguration = TianShuPromptConfiguration.Empty with
        {
            ReviewContextIntro = "自定义上下文：",
        };

        var prompt = await KernelReviewAppHostUtilities.EnrichReviewPromptWithTargetContextAsync(
            @params,
            "请审查当前变更。",
            promptConfiguration,
            @"D:\Repo",
            (command, cwd, cancellationToken) =>
            {
                var text = command[1] switch
                {
                    "status" => "M src/Program.cs",
                    "diff" when command.Contains("--staged", StringComparer.Ordinal) => "staged diff",
                    _ => "unstaged diff",
                };

                return Task.FromResult(new KernelReviewCommandResult(0, text, string.Empty, false));
            },
            CancellationToken.None);

        Assert.Contains("自定义上下文：", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("以下是自动采集的代码差异上下文", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunGitCommandForReviewAsync_ShouldRenderFailureWhenExecutorThrows()
    {
        var text = await KernelReviewAppHostUtilities.RunGitCommandForReviewAsync(
            ["git", "status"],
            @"D:\Repo",
            static (command, cwd, cancellationToken) => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        Assert.Equal("[git command failed] boom", text);
    }
}
