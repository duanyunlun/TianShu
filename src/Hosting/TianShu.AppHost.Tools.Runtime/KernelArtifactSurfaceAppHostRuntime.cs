using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Artifact surface 宿主运行时。
/// Host runtime for artifact surfaces.
/// </summary>
internal sealed class KernelArtifactSurfaceAppHostRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly Func<string> resolveTianShuHomePath;
    private readonly Func<KernelThreadRecord, string> buildThreadPreview;
    private readonly Func<string, CancellationToken, Task<string>> captureThreadGitDiffAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;

    public KernelArtifactSurfaceAppHostRuntime(
        KernelThreadStore threadStore,
        Func<string> resolveTianShuHomePath,
        Func<KernelThreadRecord, string> buildThreadPreview,
        Func<string, CancellationToken, Task<string>> captureThreadGitDiffAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync)
    {
        this.threadStore = threadStore;
        this.resolveTianShuHomePath = resolveTianShuHomePath;
        this.buildThreadPreview = buildThreadPreview;
        this.captureThreadGitDiffAsync = captureThreadGitDiffAsync;
        this.writeResultAsync = writeResultAsync;
        this.writeErrorAsync = writeErrorAsync;
    }

    public async Task HandleConversationSummaryReadAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        if (TryReadPatchString(@params, "rolloutPath", out var rolloutPathRaw) && !string.IsNullOrWhiteSpace(Normalize(rolloutPathRaw)))
        {
            var rolloutPath = Normalize(rolloutPathRaw)!;
            var absolutePath = Path.IsPathRooted(rolloutPath)
                ? Path.GetFullPath(rolloutPath)
                : Path.GetFullPath(Path.Combine(resolveTianShuHomePath(), rolloutPath));
            if (!File.Exists(absolutePath))
            {
                await writeErrorAsync(id, -32600, $"未找到会话回放文件：{absolutePath}", cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                var info = new FileInfo(absolutePath);
                var summary = KernelConversationSummaryUtilities.BuildConversationSummaryPayload(
                    conversationId: Normalize(Path.GetFileNameWithoutExtension(absolutePath)) ?? "rollout",
                    path: absolutePath,
                    preview: KernelConversationSummaryUtilities.ReadRolloutPreview(absolutePath),
                    timestamp: info.CreationTimeUtc,
                    updatedAt: info.LastWriteTimeUtc,
                    modelProvider: "openai",
                    cwd: info.DirectoryName ?? Environment.CurrentDirectory,
                    source: "rollout",
                    cliVersion: "0.1.0");
                await writeResultAsync(id, new { summary }, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                await writeErrorAsync(id, -32603, $"读取会话摘要失败：{ex.Message}", cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var threadId = Normalize(ReadString(@params, "threadId"));
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await writeErrorAsync(id, -32602, "threadId 或 rolloutPath 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var threadRecord = await threadStore.GetThreadAsync(threadId!, cancellationToken).ConfigureAwait(false);
        if (threadRecord is null)
        {
            await writeErrorAsync(id, -32600, $"未找到会话：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var summaryPayload = KernelConversationSummaryUtilities.BuildConversationSummaryPayload(
            conversationId: threadRecord.Id,
            path: threadRecord.Cwd ?? Environment.CurrentDirectory,
            preview: buildThreadPreview(threadRecord),
            timestamp: threadRecord.CreatedAt.UtcDateTime,
            updatedAt: threadRecord.UpdatedAt.UtcDateTime,
            modelProvider: "openai",
            cwd: threadRecord.Cwd ?? Environment.CurrentDirectory,
            source: "appServer",
            cliVersion: "0.1.0",
            gitSha: threadRecord.GitInfo?.Sha,
            gitBranch: threadRecord.GitInfo?.Branch,
            gitOriginUrl: threadRecord.GitInfo?.OriginUrl);

        await writeResultAsync(id, new
        {
            summary = summaryPayload,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleGitDiffToRemoteReadAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var threadId = ReadString(@params, "threadId");
        var diff = string.IsNullOrWhiteSpace(threadId)
            ? string.Empty
            : await captureThreadGitDiffAsync(threadId!, cancellationToken).ConfigureAwait(false);
        await writeResultAsync(id, new
        {
            diff,
            hasChanges = !string.IsNullOrWhiteSpace(diff),
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadString(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static bool TryReadPatchString(JsonElement json, string propertyName, out string? value)
    {
        value = null;
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        };

        return true;
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
}
