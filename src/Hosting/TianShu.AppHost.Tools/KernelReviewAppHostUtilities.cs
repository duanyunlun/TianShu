using System.Text.Json;
using TianShu.AppHost.Configuration;
using TianShuPromptConfiguration = TianShu.Configuration.TianShuPromptConfiguration;
using static TianShu.AppHost.Configuration.KernelTomlTextParsingUtilities;

namespace TianShu.AppHost.Tools;

/// <summary>
/// review 期间 git 命令执行结果。
/// Command result carrier used by review-target git context collection.
/// </summary>
internal sealed record KernelReviewCommandResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

/// <summary>
/// review prompt、目标上下文采集与结果投影辅助件。
/// Host-side helpers for review prompt shaping, target context capture, and result projection.
/// </summary>
internal static class KernelReviewAppHostUtilities
{
    public static async Task<string?> ResolveDetachedReviewModelAsync(
        string? cwd,
        Func<string?, CancellationToken, Task<Dictionary<string, string>>> loadEffectiveConfigValuesAsync,
        CancellationToken cancellationToken)
    {
        var values = await loadEffectiveConfigValuesAsync(cwd, cancellationToken).ConfigureAwait(false);
        foreach (var key in new[] { "review_model", "reviewModel", "review.model" })
        {
            if (!values.TryGetValue(key, out var raw))
            {
                continue;
            }

            var scalar = KernelToolJsonHelpers.Normalize(ReadScalarConfigValue(raw));
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                return scalar;
            }
        }

        return null;
    }

    public static object[] BuildReviewTurnItems(string turnId, string displayText)
    {
        var text = KernelToolJsonHelpers.Normalize(displayText);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<object>();
        }

        return
        [
            new
            {
                id = turnId,
                type = "userMessage",
                content = new object[]
                {
                    new
                    {
                        type = "text",
                        text,
                        textElements = Array.Empty<object>(),
                    },
                },
            },
        ];
    }

    public static async Task<string> EnrichReviewPromptWithTargetContextAsync(
        JsonElement @params,
        string basePrompt,
        TianShuPromptConfiguration? promptConfiguration,
        string cwd,
        Func<IReadOnlyList<string>, string, CancellationToken, Task<KernelReviewCommandResult>> executeGitCommandAsync,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(basePrompt)
            || !TryReadObject(@params, "target", out var target))
        {
            return basePrompt;
        }

        var type = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(target, "type"))?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(type)
            || string.Equals(type, "custom", StringComparison.Ordinal))
        {
            return basePrompt;
        }

        var contextText = await CaptureReviewTargetContextAsync(
                type!,
                target,
                cwd,
                executeGitCommandAsync,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(contextText))
        {
            return basePrompt;
        }

        var intro = promptConfiguration?.ReviewContextIntro
                    ?? "以下是自动采集的代码差异上下文，请优先基于这些变更输出审查结论：";
        return $$"""
            {{basePrompt}}

            {{intro}}

            {{contextText}}
            """;
    }

    public static Task<string> EnrichReviewPromptWithTargetContextAsync(
        JsonElement @params,
        string basePrompt,
        string cwd,
        Func<IReadOnlyList<string>, string, CancellationToken, Task<KernelReviewCommandResult>> executeGitCommandAsync,
        CancellationToken cancellationToken)
        => EnrichReviewPromptWithTargetContextAsync(
            @params,
            basePrompt,
            null,
            cwd,
            executeGitCommandAsync,
            cancellationToken);

    public static async Task<string?> CaptureReviewTargetContextAsync(
        string targetType,
        JsonElement target,
        string cwd,
        Func<IReadOnlyList<string>, string, CancellationToken, Task<KernelReviewCommandResult>> executeGitCommandAsync,
        CancellationToken cancellationToken)
    {
        switch (targetType)
        {
            case "uncommittedchanges":
                return await CaptureUncommittedReviewContextAsync(cwd, executeGitCommandAsync, cancellationToken).ConfigureAwait(false);

            case "basebranch":
                {
                    var branch = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(target, "branch"));
                    if (string.IsNullOrWhiteSpace(branch))
                    {
                        return null;
                    }

                    var diff = await RunGitCommandForReviewAsync(
                            ["git", "diff", "--no-ext-diff", "--patch", "--stat", $"{branch}...HEAD"],
                            cwd,
                            executeGitCommandAsync,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return string.IsNullOrWhiteSpace(diff)
                        ? null
                        : TrimReviewContext($$"""
                            [target]
                            baseBranch={{branch}}

                            [diff]
                            {{diff}}
                            """);
                }

            case "commit":
                {
                    var sha = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(target, "sha"));
                    if (string.IsNullOrWhiteSpace(sha))
                    {
                        return null;
                    }

                    var diff = await RunGitCommandForReviewAsync(
                            ["git", "show", "--no-ext-diff", "--patch", "--stat", sha],
                            cwd,
                            executeGitCommandAsync,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return string.IsNullOrWhiteSpace(diff)
                        ? null
                        : TrimReviewContext($$"""
                            [target]
                            commit={{sha}}

                            [diff]
                            {{diff}}
                            """);
                }

            default:
                return null;
        }
    }

    public static async Task<string?> CaptureUncommittedReviewContextAsync(
        string cwd,
        Func<IReadOnlyList<string>, string, CancellationToken, Task<KernelReviewCommandResult>> executeGitCommandAsync,
        CancellationToken cancellationToken)
    {
        var status = await RunGitCommandForReviewAsync(
                ["git", "status", "--short", "--untracked-files=all"],
                cwd,
                executeGitCommandAsync,
                cancellationToken)
            .ConfigureAwait(false);
        var staged = await RunGitCommandForReviewAsync(
                ["git", "diff", "--no-ext-diff", "--patch", "--stat", "--staged", "--", "."],
                cwd,
                executeGitCommandAsync,
                cancellationToken)
            .ConfigureAwait(false);
        var unstaged = await RunGitCommandForReviewAsync(
                ["git", "diff", "--no-ext-diff", "--patch", "--stat", "--", "."],
                cwd,
                executeGitCommandAsync,
                cancellationToken)
            .ConfigureAwait(false);

        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            sections.Add($"[status]{Environment.NewLine}{status}");
        }

        if (!string.IsNullOrWhiteSpace(staged))
        {
            sections.Add($"[staged]{Environment.NewLine}{staged}");
        }

        if (!string.IsNullOrWhiteSpace(unstaged))
        {
            sections.Add($"[unstaged]{Environment.NewLine}{unstaged}");
        }

        if (sections.Count == 0)
        {
            return null;
        }

        return TrimReviewContext(string.Join($"{Environment.NewLine}{Environment.NewLine}", sections));
    }

    public static async Task<string?> RunGitCommandForReviewAsync(
        IReadOnlyList<string> command,
        string cwd,
        Func<IReadOnlyList<string>, string, CancellationToken, Task<KernelReviewCommandResult>> executeGitCommandAsync,
        CancellationToken cancellationToken)
    {
        KernelReviewCommandResult result;
        try
        {
            result = await executeGitCommandAsync(command, cwd, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return $"[git command failed] {KernelToolJsonHelpers.Normalize(ex.Message) ?? "unknown"}";
        }

        if (result.TimedOut)
        {
            return "[git command timeout]";
        }

        if (result.ExitCode != 0)
        {
            return $"[git command failed] {KernelToolJsonHelpers.Normalize(result.StdErr) ?? KernelToolJsonHelpers.Normalize(result.StdOut) ?? $"exit_code={result.ExitCode}"}";
        }

        return KernelToolJsonHelpers.Normalize(result.StdOut);
    }

    public static string TrimReviewContext(string text)
    {
        const int maxChars = 24000;
        if (text.Length <= maxChars)
        {
            return text;
        }

        return $$"""
            {{text[..maxChars]}}

            [truncated] review context exceeded {{maxChars}} chars.
            """;
    }

    public static bool TryBuildReviewPrompt(
        JsonElement @params,
        TianShuPromptConfiguration? promptConfiguration,
        out string prompt,
        out string displayText,
        out string? error)
    {
        prompt = string.Empty;
        displayText = string.Empty;
        error = null;
        if (!TryReadObject(@params, "target", out var target))
        {
            error = "target 不能为空。";
            return false;
        }

        var type = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(target, "type"));
        switch (type?.ToLowerInvariant())
        {
            case "uncommittedchanges":
                displayText = "current changes";
                prompt = promptConfiguration?.ReviewUncommittedChangesPrompt
                         ?? "请审查当前工作区未提交变更，输出风险、缺陷与修复建议。";
                return true;
            case "basebranch":
                {
                    var branch = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(target, "branch"));
                    if (string.IsNullOrWhiteSpace(branch))
                    {
                        error = "branch must not be empty";
                        return false;
                    }

                    displayText = $"changes against '{branch}'";
                    prompt = RenderTemplate(
                        promptConfiguration?.ReviewBaseBranchPrompt
                        ?? "请审查当前分支相对基线分支 {branch} 的改动并给出发现。",
                        ("branch", branch));
                    return true;
                }
            case "commit":
                {
                    var sha = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(target, "sha"));
                    if (string.IsNullOrWhiteSpace(sha))
                    {
                        error = "sha must not be empty";
                        return false;
                    }

                    var title = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(target, "title"));
                    var shortSha = sha.Length > 7 ? sha[..7] : sha;
                    displayText = string.IsNullOrWhiteSpace(title)
                        ? $"commit {shortSha}"
                        : $"commit {shortSha}: {title}";
                    var template = promptConfiguration?.ReviewCommitPrompt
                                   ?? (string.IsNullOrWhiteSpace(title)
                                       ? "请审查提交 {sha} 的改动并给出发现。"
                                       : "请审查提交 {sha}（{title}）的改动并给出发现。");
                    prompt = RenderTemplate(
                        template,
                        ("sha", sha),
                        ("title", title ?? string.Empty));
                    return true;
                }
            case "custom":
                {
                    var instructions = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(target, "instructions"));
                    if (string.IsNullOrWhiteSpace(instructions))
                    {
                        error = "instructions must not be empty";
                        return false;
                    }

                    displayText = instructions!;
                    prompt = instructions!;
                    return true;
                }
            default:
                error = $"unsupported review target type: {type}";
                return false;
        }
    }

    public static bool TryBuildReviewPrompt(
        JsonElement @params,
        out string prompt,
        out string displayText,
        out string? error)
        => TryBuildReviewPrompt(@params, null, out prompt, out displayText, out error);

    private static string RenderTemplate(string template, params (string Key, string Value)[] values)
    {
        var rendered = template;
        foreach (var (key, value) in values)
        {
            rendered = rendered.Replace("{" + key + "}", value, StringComparison.Ordinal);
        }

        return rendered;
    }

    private static bool TryReadObject(JsonElement json, string propertyName, out JsonElement value)
    {
        value = default;
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(propertyName, out value)
            && value.ValueKind == JsonValueKind.Object;
    }
}
