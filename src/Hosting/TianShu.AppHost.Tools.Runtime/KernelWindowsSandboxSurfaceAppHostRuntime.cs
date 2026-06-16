using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// windowsSandbox/* northbound surface 宿主运行时。
/// Host runtime for windowsSandbox/* northbound surfaces.
/// </summary>
internal sealed class KernelWindowsSandboxSurfaceAppHostRuntime
{
    private readonly Func<IReadOnlyList<string>, string?, int?, IReadOnlyDictionary<string, string>?, CancellationToken, Task<KernelCommandRunResult>> executeCommandAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;

    public KernelWindowsSandboxSurfaceAppHostRuntime(
        Func<IReadOnlyList<string>, string?, int?, IReadOnlyDictionary<string, string>?, CancellationToken, Task<KernelCommandRunResult>> executeCommandAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync)
    {
        this.executeCommandAsync = executeCommandAsync;
        this.writeResultAsync = writeResultAsync;
        this.writeErrorAsync = writeErrorAsync;
        this.writeNotificationAsync = writeNotificationAsync;
    }

    public async Task HandleWindowsSandboxSetupStartAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var rawMode = Normalize(ReadString(@params, "mode"));
        if (string.IsNullOrWhiteSpace(rawMode))
        {
            await writeErrorAsync(id, -32602, "mode 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!KernelWindowsSandboxSetupUtilities.TryNormalizeSetupMode(rawMode, out var mode))
        {
            await writeErrorAsync(id, -32600, $"invalid mode: {rawMode}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var setupCwd = Normalize(ReadString(@params, "cwd"));
        if (!string.IsNullOrWhiteSpace(setupCwd) && !Path.IsPathRooted(setupCwd))
        {
            await writeErrorAsync(id, -32600, "Invalid request: windowsSandbox/setupStart cwd must be an absolute path", cancellationToken).ConfigureAwait(false);
            return;
        }

        await writeResultAsync(id, new
        {
            started = true,
        }, cancellationToken).ConfigureAwait(false);

        _ = Task.Run(async () =>
        {
            var storage = KernelStoragePaths.ResolveDefault();
            var setupResult = await KernelWindowsSandboxSetupUtilities.RunWindowsSandboxSetupAsync(
                    mode!,
                    setupCwd,
                    storage.StateDirectory,
                    async (command, innerCancellationToken) =>
                    {
                        var result = await executeCommandAsync(command, null, 20000, null, innerCancellationToken).ConfigureAwait(false);
                        return new KernelWindowsSandboxProbeCommandResult(result.ExitCode, result.StdOut, result.StdErr);
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
            await writeNotificationAsync("windowsSandbox/setupCompleted", new
            {
                mode = mode!,
                success = setupResult.Success,
                error = setupResult.Error,
            }, CancellationToken.None).ConfigureAwait(false);
        }, CancellationToken.None);
    }

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null => null,
            _ => null,
        };
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
