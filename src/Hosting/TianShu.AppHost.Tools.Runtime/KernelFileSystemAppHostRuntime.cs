using System.Text.Json;
using TianShu.AppHost.Tools;
using static TianShu.AppHost.Tools.KernelFileSystemUtilities;
using static TianShu.AppHost.Tools.KernelFuzzyFileSearchUtilities;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelFileSystemAppHostRuntime : IAsyncDisposable
{
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly KernelFsWatchManager fsWatchManager = new();
    private readonly Dictionary<string, KernelFuzzyFileSearchSession> fuzzyFileSearchSessions = new(StringComparer.Ordinal);

    public KernelFileSystemAppHostRuntime(
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync)
    {
        this.writeErrorAsync = writeErrorAsync;
        this.writeResultAsync = writeResultAsync;
        this.writeNotificationAsync = writeNotificationAsync;
    }

    public ValueTask DisposeAsync()
        => fsWatchManager.DisposeAsync();

    public async Task HandleFsReadFileAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        if (!TryReadRequiredAbsolutePath(@params, "path", out var path, out var errorMessage))
        {
            await writeErrorAsync(id, -32600, errorMessage!, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path!, cancellationToken).ConfigureAwait(false);
            await writeResultAsync(
                    id,
                    new
                    {
                        dataBase64 = Convert.ToBase64String(bytes),
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await writeErrorAsync(id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleFsWriteFileAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        if (!TryReadRequiredAbsolutePath(@params, "path", out var path, out var errorMessage))
        {
            await writeErrorAsync(id, -32600, errorMessage!, cancellationToken).ConfigureAwait(false);
            return;
        }

        var dataBase64 = Normalize(ReadString(@params, "dataBase64"));
        if (string.IsNullOrWhiteSpace(dataBase64))
        {
            await writeErrorAsync(id, -32600, "fs/writeFile requires dataBase64", cancellationToken).ConfigureAwait(false);
            return;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(dataBase64!);
        }
        catch (FormatException ex)
        {
            await writeErrorAsync(
                    id,
                    -32600,
                    $"fs/writeFile requires valid base64 dataBase64: {ex.Message}",
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path!);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(path!, bytes, cancellationToken).ConfigureAwait(false);
            await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await writeErrorAsync(id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleFsCreateDirectoryAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        if (!TryReadRequiredAbsolutePath(@params, "path", out var path, out var errorMessage))
        {
            await writeErrorAsync(id, -32600, errorMessage!, cancellationToken).ConfigureAwait(false);
            return;
        }

        _ = ReadBool(@params, "recursive");
        try
        {
            Directory.CreateDirectory(path!);
            await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await writeErrorAsync(id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleFsGetMetadataAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        if (!TryReadRequiredAbsolutePath(@params, "path", out var path, out var errorMessage))
        {
            await writeErrorAsync(id, -32600, errorMessage!, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var isDirectory = Directory.Exists(path);
            var isFile = File.Exists(path);
            if (!isDirectory && !isFile)
            {
                throw new FileNotFoundException($"Could not find file or directory '{path}'.");
            }

            await writeResultAsync(
                    id,
                    new
                    {
                        isDirectory,
                        isFile,
                        createdAtMs = GetFileSystemTimeUtc(path!, creationTime: true),
                        modifiedAtMs = GetFileSystemTimeUtc(path!, creationTime: false),
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await writeErrorAsync(id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleFsReadDirectoryAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        if (!TryReadRequiredAbsolutePath(@params, "path", out var path, out var errorMessage))
        {
            await writeErrorAsync(id, -32600, errorMessage!, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"Could not find a part of the path '{path}'.");
            }

            var entries = Directory.EnumerateFileSystemEntries(path!)
                .Select(static entry => new
                {
                    fileName = Path.GetFileName(entry),
                    isDirectory = Directory.Exists(entry),
                    isFile = File.Exists(entry),
                })
                .ToArray();

            await writeResultAsync(id, new { entries }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await writeErrorAsync(id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleFsRemoveAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        if (!TryReadRequiredAbsolutePath(@params, "path", out var path, out var errorMessage))
        {
            await writeErrorAsync(id, -32600, errorMessage!, cancellationToken).ConfigureAwait(false);
            return;
        }

        var recursive = ReadBool(@params, "recursive") ?? true;
        var force = ReadBool(@params, "force") ?? true;

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path!, recursive);
            }
            else if (File.Exists(path))
            {
                File.Delete(path!);
            }
            else if (!force)
            {
                throw new FileNotFoundException($"Could not find file or directory '{path}'.");
            }

            await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await writeErrorAsync(id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleFsCopyAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        if (!TryReadRequiredAbsolutePath(@params, "sourcePath", out var sourcePath, out var sourceError))
        {
            await writeErrorAsync(id, -32600, sourceError!, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryReadRequiredAbsolutePath(@params, "destinationPath", out var destinationPath, out var destinationError))
        {
            await writeErrorAsync(id, -32600, destinationError!, cancellationToken).ConfigureAwait(false);
            return;
        }

        var recursive = ReadBool(@params, "recursive") ?? false;

        try
        {
            await CopyFileSystemEntryAsync(sourcePath!, destinationPath!, recursive, cancellationToken).ConfigureAwait(false);
            await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            await writeErrorAsync(id, -32600, ex.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await writeErrorAsync(id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleFsWatchAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        if (!TryReadRequiredAbsolutePath(@params, "path", out var path, out var errorMessage))
        {
            await writeErrorAsync(id, -32600, errorMessage!, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var handle = fsWatchManager.Register(
                path!,
                async (watchId, changedPaths) =>
                {
                    try
                    {
                        await writeNotificationAsync(
                                "fs/changed",
                                new
                                {
                                    watchId,
                                    changedPaths,
                                },
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                });

            await writeResultAsync(
                    id,
                    new
                    {
                        watchId = handle.WatchId,
                        path = handle.Path,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            await writeErrorAsync(id, -32600, ex.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await writeErrorAsync(id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleFsUnwatchAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var watchId = Normalize(ReadString(@params, "watchId"));
        if (!string.IsNullOrWhiteSpace(watchId))
        {
            await fsWatchManager.UnregisterAsync(watchId!).ConfigureAwait(false);
        }

        await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleFuzzyFileSearchLegacyAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var query = ReadString(@params, "query");
        var roots = NormalizeFuzzyFileSearchRoots(KernelToolJsonHelpers.ReadStringArray(@params, "roots"), ReadString(@params, "cwd"));
        var limit = Math.Clamp(ReadInt(@params, "limit") ?? 50, 1, 200);
        var results = SearchFilesAcrossRoots(query, roots, limit);

        await writeResultAsync(id, new
        {
            files = results,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleFuzzyFileSearchSessionStartAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var sessionId = ReadString(@params, "sessionId");
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            await writeErrorAsync(id, -32600, "sessionId must not be empty", CancellationToken.None).ConfigureAwait(false);
            return;
        }

        fuzzyFileSearchSessions[sessionId] = CreateFuzzyFileSearchSession(
            sessionId,
            KernelToolJsonHelpers.ReadStringArray(@params, "roots"),
            query: null,
            fallbackRoot: Environment.CurrentDirectory);

        await writeResultAsync(id, new { }, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task HandleFuzzyFileSearchSessionUpdateAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var sessionId = ReadString(@params, "sessionId");
        if (string.IsNullOrWhiteSpace(sessionId) || !fuzzyFileSearchSessions.TryGetValue(sessionId, out var session))
        {
            await writeErrorAsync(id, -32600, $"fuzzy file search session not found: {sessionId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var query = ReadString(@params, "query");
        session = UpdateFuzzyFileSearchSessionQuery(session, query);
        fuzzyFileSearchSessions[sessionId] = session;
        var files = SearchFilesAcrossRoots(session.Query, session.Roots, 50);

        await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);

        await writeNotificationAsync("fuzzyFileSearch/sessionUpdated", new
        {
            sessionId,
            query = session.Query,
            files,
        }, cancellationToken).ConfigureAwait(false);

        await writeNotificationAsync("fuzzyFileSearch/sessionCompleted", new
        {
            sessionId,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleFuzzyFileSearchSessionStopAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var sessionId = ReadString(@params, "sessionId");
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            fuzzyFileSearchSessions.Remove(sessionId);
        }

        await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }
}
