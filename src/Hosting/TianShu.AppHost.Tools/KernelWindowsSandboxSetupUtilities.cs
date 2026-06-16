using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// Windows Sandbox setup probe 命令执行结果。
/// Command result used by Windows Sandbox availability probing.
/// </summary>
internal sealed record KernelWindowsSandboxProbeCommandResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Windows Sandbox setup 的宿主辅助方法。
/// Host-side helpers for Windows Sandbox setup orchestration.
/// </summary>
internal static class KernelWindowsSandboxSetupUtilities
{
    public static bool TryNormalizeSetupMode(string? rawMode, out string? mode)
    {
        mode = null;
        var normalized = KernelToolJsonHelpers.Normalize(rawMode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (string.Equals(normalized, "elevated", StringComparison.OrdinalIgnoreCase))
        {
            mode = "elevated";
            return true;
        }

        if (string.Equals(normalized, "unelevated", StringComparison.OrdinalIgnoreCase))
        {
            mode = "unelevated";
            return true;
        }

        return false;
    }

    public static async Task<(bool Success, string? Error)> RunWindowsSandboxSetupAsync(
        string mode,
        string? cwd,
        string storageRootDirectory,
        Func<IReadOnlyList<string>, CancellationToken, Task<KernelWindowsSandboxProbeCommandResult>> executeProbeCommandAsync,
        CancellationToken cancellationToken)
    {
        var (probeSuccess, probeError) = await ProbeWindowsSandboxAvailabilityAsync(executeProbeCommandAsync, cancellationToken).ConfigureAwait(false);
        if (!probeSuccess)
        {
            return (false, probeError);
        }

        try
        {
            _ = await PrepareWindowsSandboxArtifactsAsync(storageRootDirectory, mode, cwd, cancellationToken).ConfigureAwait(false);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"setup_failed:{KernelToolJsonHelpers.Normalize(ex.Message) ?? "unknown"}");
        }
    }

    public static async Task<string> PrepareWindowsSandboxArtifactsAsync(
        string storageRootDirectory,
        string mode,
        string? cwd,
        CancellationToken cancellationToken,
        string? windowsRootOverride = null)
    {
        var windowsRoot = !string.IsNullOrWhiteSpace(windowsRootOverride)
            ? Path.GetFullPath(windowsRootOverride!)
            : Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var sandboxExe = Path.Combine(windowsRoot, "System32", "WindowsSandbox.exe");
        if (string.IsNullOrWhiteSpace(windowsRoot) || !File.Exists(sandboxExe))
        {
            throw new InvalidOperationException("WindowsSandbox.exe 不存在，无法继续 setup。");
        }

        var setupRoot = Path.Combine(storageRootDirectory, "windows-sandbox", mode.ToLowerInvariant());
        Directory.CreateDirectory(setupRoot);

        var bootstrapPath = Path.Combine(setupRoot, "bootstrap.ps1");
        var bootstrapScript = $$"""
            Write-Host "TianShu Windows Sandbox setup bootstrap"
            Write-Host "Mode: {{mode}}"
            Write-Host "Cwd: {{cwd ?? string.Empty}}"
            Write-Host "Timestamp: {{DateTimeOffset.UtcNow:O}}"
            """;
        await File.WriteAllTextAsync(bootstrapPath, bootstrapScript, cancellationToken).ConfigureAwait(false);

        var wsbPath = Path.Combine(setupRoot, "tianshu.wsb");
        var wsbContent = BuildWindowsSandboxConfigXml(setupRoot, mode);
        await File.WriteAllTextAsync(wsbPath, wsbContent, cancellationToken).ConfigureAwait(false);

        var metadataPath = Path.Combine(setupRoot, "setup-metadata.json");
        var metadata = JsonSerializer.Serialize(new
        {
            mode,
            cwd,
            sandboxExe,
            wsbPath,
            generatedAt = DateTimeOffset.UtcNow,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        });
        await File.WriteAllTextAsync(metadataPath, metadata, cancellationToken).ConfigureAwait(false);

        return setupRoot;
    }

    public static string BuildWindowsSandboxConfigXml(string setupRoot, string mode)
    {
        static string EscapeXml(string value)
        {
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&apos;", StringComparison.Ordinal);
        }

        var hostFolder = EscapeXml(Path.GetFullPath(setupRoot));
        var networking = string.Equals(mode, "elevated", StringComparison.OrdinalIgnoreCase)
            ? "Enable"
            : "Disable";
        var startupCommand = EscapeXml(@"powershell -NoProfile -ExecutionPolicy Bypass -File C:\TianShuSandbox\bootstrap.ps1");

        return $$"""
            <Configuration>
              <Networking>{{networking}}</Networking>
              <MappedFolders>
                <MappedFolder>
                  <HostFolder>{{hostFolder}}</HostFolder>
                  <SandboxFolder>C:\TianShuSandbox</SandboxFolder>
                  <ReadOnly>false</ReadOnly>
                </MappedFolder>
              </MappedFolders>
              <LogonCommand>
                <Command>{{startupCommand}}</Command>
              </LogonCommand>
            </Configuration>
            """;
    }

    public static IReadOnlyList<string> BuildWindowsSandboxProbeCommand()
    {
        return
        [
            "powershell",
            "-NoProfile",
            "-Command",
            "(Get-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -ErrorAction Stop).State",
        ];
    }

    public static async Task<(bool Success, string? Error)> ProbeWindowsSandboxAvailabilityAsync(
        Func<IReadOnlyList<string>, CancellationToken, Task<KernelWindowsSandboxProbeCommandResult>> executeProbeCommandAsync,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return (false, "windows_sandbox_only_supported_on_windows");
        }

        KernelWindowsSandboxProbeCommandResult result;
        try
        {
            result = await executeProbeCommandAsync(BuildWindowsSandboxProbeCommand(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (false, $"probe_failed:{KernelToolJsonHelpers.Normalize(ex.Message) ?? "unknown"}");
        }

        if (result.ExitCode != 0)
        {
            var detail = KernelToolJsonHelpers.Normalize(result.StdErr)
                         ?? KernelToolJsonHelpers.Normalize(result.StdOut)
                         ?? "unknown";
            return (false, $"probe_failed:{detail}");
        }

        var state = KernelToolJsonHelpers.Normalize(result.StdOut) ?? string.Empty;
        if (!state.Contains("Enabled", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"feature_not_enabled:{state}");
        }

        return (true, null);
    }
}
