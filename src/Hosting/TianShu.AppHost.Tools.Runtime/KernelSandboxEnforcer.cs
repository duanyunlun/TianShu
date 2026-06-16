using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelSandboxDecision(bool Allowed, string? Reason);

internal static class KernelSandboxEnforcer
{
    public static KernelSandboxDecision EvaluateCommand(
        IReadOnlyList<string> commandArgs,
        string commandPreview,
        string? cwd,
        JsonElement? sandboxPolicy,
        string? sandboxMode,
        bool bypassSandbox,
        bool allowManagedNetwork = false)
    {
        if (bypassSandbox)
        {
            return new KernelSandboxDecision(true, null);
        }

        var effectiveCwd = NormalizePath(cwd) ?? Environment.CurrentDirectory;
        var writableDecision = EnsureWorkingDirectoryAllowed(effectiveCwd, sandboxPolicy, sandboxMode);
        if (!writableDecision.Allowed)
        {
            return writableDecision;
        }

        return EnsureNetworkCommandAllowed(commandArgs, commandPreview, sandboxPolicy, sandboxMode, allowManagedNetwork);
    }

    public static KernelSandboxDecision EnsureWritePathAllowed(
        string path,
        string cwd,
        JsonElement? sandboxPolicy,
        string? sandboxMode)
    {
        var fullPath = NormalizePath(path) ?? NormalizePath(Path.Combine(cwd, path)) ?? path;
        var mode = ResolveSandboxMode(sandboxPolicy, sandboxMode);
        if (HasUnrestrictedFilesystem(mode))
        {
            return new KernelSandboxDecision(true, null);
        }

        if (IsReadOnly(mode))
        {
            return new KernelSandboxDecision(false, $"sandbox_policy_denied:{mode}");
        }

        var roots = GetWritableRoots(cwd, sandboxPolicy);
        return roots.Any(root => IsPathWithinRoot(fullPath, root))
            ? new KernelSandboxDecision(true, null)
            : new KernelSandboxDecision(false, "sandbox_policy_denied:writableRoots");
    }

    public static KernelSandboxDecision EnsureWorkingDirectoryAllowed(
        string cwd,
        JsonElement? sandboxPolicy,
        string? sandboxMode)
    {
        var mode = ResolveSandboxMode(sandboxPolicy, sandboxMode);
        if (HasUnrestrictedFilesystem(mode) || IsReadOnly(mode))
        {
            return new KernelSandboxDecision(true, null);
        }

        var roots = GetWritableRoots(cwd, sandboxPolicy);
        var normalizedCwd = NormalizePath(cwd) ?? cwd;
        return roots.Any(root => IsPathWithinRoot(normalizedCwd, root))
            ? new KernelSandboxDecision(true, null)
            : new KernelSandboxDecision(false, "sandbox_policy_denied:writableRoots");
    }

    public static KernelSandboxDecision EnsureNetworkCommandAllowed(
        IReadOnlyList<string> commandArgs,
        string commandPreview,
        JsonElement? sandboxPolicy,
        string? sandboxMode,
        bool allowManagedNetwork = false)
    {
        var mode = ResolveSandboxMode(sandboxPolicy, sandboxMode);
        if (allowManagedNetwork || IsDangerFullAccess(mode) || IsNetworkAccessAllowed(sandboxPolicy))
        {
            return new KernelSandboxDecision(true, null);
        }

        if (!LooksNetworkCommand(commandArgs, commandPreview))
        {
            return new KernelSandboxDecision(true, null);
        }

        return new KernelSandboxDecision(false, "sandbox_policy_denied:networkAccess");
    }

    private static string ResolveSandboxMode(JsonElement? sandboxPolicy, string? sandboxMode)
    {
        var explicitMode = Normalize(sandboxMode);
        if (!string.IsNullOrWhiteSpace(explicitMode))
        {
            return explicitMode!;
        }

        if (sandboxPolicy is { ValueKind: JsonValueKind.Object } policy)
        {
            var mode = ReadString(policy, "type");
            if (!string.IsNullOrWhiteSpace(mode))
            {
                return mode!;
            }
        }

        return "workspaceWrite";
    }

    private static string[] GetWritableRoots(string cwd, JsonElement? sandboxPolicy)
    {
        var roots = new List<string>();
        var explicitRootsFound = false;

        if (sandboxPolicy is { ValueKind: JsonValueKind.Object } policy
            && policy.TryGetProperty("writableRoots", out var writableRoots)
            && writableRoots.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in writableRoots.EnumerateArray())
            {
                var root = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                var normalized = NormalizePath(root);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    explicitRootsFound = true;
                    roots.Add(normalized!);
                }
            }
        }

        if (!explicitRootsFound)
        {
            var normalizedCwd = NormalizePath(cwd);
            if (!string.IsNullOrWhiteSpace(normalizedCwd))
            {
                roots.Add(normalizedCwd!);
            }
        }

        return roots
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsNetworkAccessAllowed(JsonElement? sandboxPolicy)
    {
        if (sandboxPolicy is not { ValueKind: JsonValueKind.Object } policy)
        {
            return false;
        }

        if (!policy.TryGetProperty("networkAccess", out var networkAccess))
        {
            return false;
        }

        return networkAccess.ValueKind == JsonValueKind.True
               || (networkAccess.ValueKind == JsonValueKind.String
                   && IsEnabledNetworkValue(networkAccess.GetString()));
    }

    private static bool LooksNetworkCommand(IReadOnlyList<string> commandArgs, string commandPreview)
    {
        var preview = Normalize(commandPreview)?.ToLowerInvariant() ?? string.Empty;
        if (preview.Contains("http://", StringComparison.Ordinal)
            || preview.Contains("https://", StringComparison.Ordinal))
        {
            return true;
        }

        var first = commandArgs.Count == 0 ? string.Empty : Normalize(commandArgs[0])?.ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(first))
        {
            return false;
        }

        return first is "curl" or "wget" or "ping" or "nslookup" or "ftp" or "scp" or "ssh"
            || preview.Contains("invoke-webrequest", StringComparison.Ordinal)
            || preview.Contains("iwr ", StringComparison.Ordinal)
            || preview.Contains("irm ", StringComparison.Ordinal)
            || preview.Contains("git clone", StringComparison.Ordinal)
            || preview.Contains("git fetch", StringComparison.Ordinal)
            || preview.Contains("git pull", StringComparison.Ordinal)
            || preview.Contains("npm install", StringComparison.Ordinal)
            || preview.Contains("pnpm add", StringComparison.Ordinal)
            || preview.Contains("yarn add", StringComparison.Ordinal);
    }

    private static bool IsPathWithinRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedPath = NormalizePath(path) ?? path;
        var normalizedRoot = NormalizePath(root) ?? root;
        return normalizedPath.StartsWith(normalizedRoot, comparison);
    }

    private static bool HasUnrestrictedFilesystem(string mode)
        => IsDangerFullAccess(mode) || IsExternalSandbox(mode);

    private static bool IsDangerFullAccess(string mode)
        => mode.Equals("danger-full-access", StringComparison.OrdinalIgnoreCase)
           || mode.Equals("dangerFullAccess", StringComparison.OrdinalIgnoreCase);

    private static bool IsExternalSandbox(string mode)
        => mode.Equals("externalSandbox", StringComparison.OrdinalIgnoreCase)
           || mode.Equals("external-sandbox", StringComparison.OrdinalIgnoreCase)
           || mode.Equals("external_sandbox", StringComparison.OrdinalIgnoreCase);

    private static bool IsReadOnly(string mode)
        => mode.Contains("read", StringComparison.OrdinalIgnoreCase);

    private static bool IsEnabledNetworkValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("enabled", StringComparison.OrdinalIgnoreCase)
               || (bool.TryParse(value, out var parsed) && parsed);
    }

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
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

    private static string? NormalizePath(string? value)
    {
        var normalized = Normalize(value);
        return normalized is null ? null : Path.GetFullPath(normalized);
    }
}





