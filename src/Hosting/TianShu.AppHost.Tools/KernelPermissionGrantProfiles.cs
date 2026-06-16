using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 权限授予的作用域。
/// Scope used when granted permissions are persisted by the host.
/// </summary>
internal enum KernelPermissionGrantScope
{
    Turn = 0,
    Session = 1,
}

/// <summary>
/// request_permissions 发起时的宿主请求载荷。
/// Host-facing permission request payload for request_permissions.
/// </summary>
internal sealed record KernelRequestPermissionsRequest(
    string ItemId,
    string Cwd,
    string? Reason,
    KernelPermissionGrantProfile Permissions);

/// <summary>
/// request_permissions 返回后的宿主响应载荷。
/// Host-facing permission response payload returned by request_permissions.
/// </summary>
internal sealed record KernelRequestPermissionsResponse(
    KernelPermissionGrantProfile Permissions,
    KernelPermissionGrantScope Scope);

/// <summary>
/// 等待用户或前端确认时暂存的权限请求记录。
/// Pending permission request record held while waiting for approval.
/// </summary>
internal sealed record KernelPendingPermissionRequest(
    string CallId,
    string ThreadId,
    string TurnId,
    string Cwd,
    KernelPermissionGrantProfile RequestedPermissions);

/// <summary>
/// 宿主内部使用的统一权限授予画像。
/// Unified host-side permission grant profile used by sandbox and approval flows.
/// </summary>
internal sealed class KernelPermissionGrantProfile
{
    public static KernelPermissionGrantProfile Empty { get; } = new();

    public bool NetworkEnabled { get; init; }

    public string[] ReadRoots { get; init; } = [];

    public string[] WriteRoots { get; init; } = [];

    public bool HasMacOsPermissions { get; init; }

    public bool IsEmpty => !NetworkEnabled
        && ReadRoots.Length == 0
        && WriteRoots.Length == 0
        && !HasMacOsPermissions;

    public object BuildToolPayload()
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (NetworkEnabled)
        {
            payload["network"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["enabled"] = true,
            };
        }

        if (ReadRoots.Length > 0 || WriteRoots.Length > 0)
        {
            var fileSystem = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (ReadRoots.Length > 0)
            {
                fileSystem["read"] = ReadRoots;
            }

            if (WriteRoots.Length > 0)
            {
                fileSystem["write"] = WriteRoots;
            }

            payload["file_system"] = fileSystem;
        }

        return payload;
    }

    public object BuildServerPayload()
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (NetworkEnabled)
        {
            payload["network"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["enabled"] = true,
            };
        }

        if (ReadRoots.Length > 0 || WriteRoots.Length > 0)
        {
            var fileSystem = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (ReadRoots.Length > 0)
            {
                fileSystem["read"] = ReadRoots;
            }

            if (WriteRoots.Length > 0)
            {
                fileSystem["write"] = WriteRoots;
            }

            payload["file_system"] = fileSystem;
        }

        if (HasMacOsPermissions)
        {
            payload["macos"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["enabled"] = true,
            };
        }

        return payload;
    }

    public object BuildResponsePayload(KernelPermissionGrantScope scope)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["permissions"] = BuildServerPayload(),
            ["scope"] = scope switch
            {
                KernelPermissionGrantScope.Session => "session",
                _ => "turn",
            },
        };
    }

    public bool CoversAllWritePaths(IReadOnlyList<string>? paths)
    {
        if (paths is null || paths.Count == 0)
        {
            return false;
        }

        foreach (var path in paths)
        {
            if (!IsPathCoveredByAnyRoot(path, WriteRoots))
            {
                return false;
            }
        }

        return true;
    }

    public bool Covers(KernelPermissionGrantProfile? requested)
    {
        if (requested is null || requested.IsEmpty)
        {
            return false;
        }

        if (requested.NetworkEnabled && !NetworkEnabled)
        {
            return false;
        }

        if (requested.HasMacOsPermissions && !HasMacOsPermissions)
        {
            return false;
        }

        if (!CoversRoots(requested.ReadRoots, ReadRoots))
        {
            return false;
        }

        return CoversRoots(requested.WriteRoots, WriteRoots);
    }

    public static KernelPermissionGrantProfile Merge(
        KernelPermissionGrantProfile? left,
        KernelPermissionGrantProfile? right)
    {
        if (left is null || left.IsEmpty)
        {
            return right is null ? Empty : Clone(right);
        }

        if (right is null || right.IsEmpty)
        {
            return Clone(left);
        }

        return new KernelPermissionGrantProfile
        {
            NetworkEnabled = left.NetworkEnabled || right.NetworkEnabled,
            ReadRoots = MergePaths(left.ReadRoots, right.ReadRoots),
            WriteRoots = MergePaths(left.WriteRoots, right.WriteRoots),
            HasMacOsPermissions = left.HasMacOsPermissions || right.HasMacOsPermissions,
        };
    }

    public static KernelPermissionGrantProfile Intersect(
        KernelPermissionGrantProfile? requested,
        KernelPermissionGrantProfile? granted)
    {
        if (requested is null || requested.IsEmpty || granted is null || granted.IsEmpty)
        {
            return Empty;
        }

        return new KernelPermissionGrantProfile
        {
            NetworkEnabled = requested.NetworkEnabled && granted.NetworkEnabled,
            ReadRoots = IntersectRoots(requested.ReadRoots, granted.ReadRoots),
            WriteRoots = IntersectRoots(requested.WriteRoots, granted.WriteRoots),
            HasMacOsPermissions = requested.HasMacOsPermissions && granted.HasMacOsPermissions,
        };
    }

    public static KernelPermissionGrantProfile Clone(KernelPermissionGrantProfile source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new KernelPermissionGrantProfile
        {
            NetworkEnabled = source.NetworkEnabled,
            ReadRoots = source.ReadRoots.ToArray(),
            WriteRoots = source.WriteRoots.ToArray(),
            HasMacOsPermissions = source.HasMacOsPermissions,
        };
    }

    public static bool TryCreateFromAdditionalPermissions(
        KernelAdditionalPermissionArguments? additionalPermissions,
        string cwd,
        out KernelPermissionGrantProfile profile,
        out string? errorMessage)
    {
        profile = Empty;
        errorMessage = null;

        if (additionalPermissions is null)
        {
            errorMessage = "missing `additional_permissions`; provide at least one of `network`, `file_system`, or `macos` when using `with_additional_permissions`";
            return false;
        }

        if (!TryResolvePermissionRoots(additionalPermissions.FileSystem?.Read, "read", cwd, out var readRoots, out errorMessage)
            || !TryResolvePermissionRoots(additionalPermissions.FileSystem?.Write, "write", cwd, out var writeRoots, out errorMessage))
        {
            return false;
        }

        profile = new KernelPermissionGrantProfile
        {
            NetworkEnabled = additionalPermissions.Network?.Enabled == true,
            ReadRoots = readRoots,
            WriteRoots = writeRoots,
            HasMacOsPermissions = HasRequestedMacOsPermissions(additionalPermissions.MacOs),
        };

        if (profile.HasMacOsPermissions && !OperatingSystem.IsMacOS())
        {
            errorMessage = "`permissions.macos` is only supported on macOS";
            return false;
        }

        return true;
    }

    public static bool TryCreateFromRequestPermissions(
        KernelRequestPermissionArguments? permissions,
        string cwd,
        out KernelPermissionGrantProfile profile,
        out string? errorMessage)
    {
        profile = Empty;
        errorMessage = null;

        if (permissions is null)
        {
            errorMessage = "request_permissions requires a permissions object";
            return false;
        }

        if (!TryResolvePermissionRoots(permissions.FileSystem?.Read, "read", cwd, out var readRoots, out errorMessage)
            || !TryResolvePermissionRoots(permissions.FileSystem?.Write, "write", cwd, out var writeRoots, out errorMessage))
        {
            return false;
        }

        profile = new KernelPermissionGrantProfile
        {
            NetworkEnabled = permissions.Network?.Enabled == true,
            ReadRoots = readRoots,
            WriteRoots = writeRoots,
            HasMacOsPermissions = false,
        };
        return true;
    }

    public static bool TryParseAdditionalPermissions(
        JsonElement additionalPermissions,
        string cwd,
        out KernelPermissionGrantProfile profile,
        out string? errorMessage)
    {
        return TryParsePermissionsObject(additionalPermissions, cwd, allowMacOs: true, out profile, out errorMessage);
    }

    public static bool TryParseRequestPermissions(
        JsonElement arguments,
        string cwd,
        out string? reason,
        out KernelPermissionGrantProfile profile,
        out string? errorMessage)
    {
        reason = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(arguments, "reason"));
        profile = Empty;
        errorMessage = null;

        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("permissions", out var permissionsElement)
            || permissionsElement.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "request_permissions requires a permissions object";
            return false;
        }

        if (!TryParsePermissionsObject(permissionsElement, cwd, allowMacOs: false, out profile, out errorMessage))
        {
            return false;
        }

        if (profile.IsEmpty)
        {
            errorMessage = "request_permissions requires at least one permission";
            return false;
        }

        return true;
    }

    private static bool TryResolvePermissionRoots(
        IReadOnlyList<string>? requestedRoots,
        string propertyName,
        string cwd,
        out string[] roots,
        out string? errorMessage)
    {
        roots = Array.Empty<string>();
        errorMessage = null;
        if (requestedRoots is null || requestedRoots.Count == 0)
        {
            return true;
        }

        var resolved = new List<string>();
        foreach (var rawRoot in requestedRoots)
        {
            var normalized = KernelToolJsonHelpers.Normalize(rawRoot);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var fullPath = Path.IsPathRooted(normalized)
                ? Path.GetFullPath(normalized)
                : Path.GetFullPath(Path.Combine(cwd, normalized));
            resolved.Add(fullPath);
        }

        roots = DeduplicatePaths(resolved);
        return true;
    }

    public static bool TryParseResponse(
        JsonElement response,
        string cwd,
        out KernelPermissionGrantProfile permissions,
        out KernelPermissionGrantScope scope,
        out string? errorMessage)
    {
        permissions = Empty;
        scope = KernelPermissionGrantScope.Turn;
        errorMessage = null;

        if (response.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "permissions response must be an object";
            return false;
        }

        if (response.TryGetProperty("scope", out var scopeElement))
        {
            scope = ParseScope(scopeElement.GetString());
        }

        if (!response.TryGetProperty("permissions", out var permissionsElement)
            || permissionsElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            permissions = Empty;
            return true;
        }

        if (!TryParsePermissionsObject(permissionsElement, cwd, allowMacOs: true, out permissions, out errorMessage))
        {
            return false;
        }

        return true;
    }

    private static bool CoversRoots(IReadOnlyList<string> requestedRoots, IReadOnlyList<string> grantedRoots)
    {
        if (requestedRoots.Count == 0)
        {
            return true;
        }

        foreach (var requestedRoot in requestedRoots)
        {
            if (!IsPathCoveredByAnyRoot(requestedRoot, grantedRoots))
            {
                return false;
            }
        }

        return true;
    }

    public static KernelPermissionGrantScope ParseScope(string? value)
    {
        var normalized = KernelToolJsonHelpers.Normalize(value);
        return string.Equals(normalized, "session", StringComparison.OrdinalIgnoreCase)
            ? KernelPermissionGrantScope.Session
            : KernelPermissionGrantScope.Turn;
    }

    private static bool TryParsePermissionsObject(
        JsonElement permissions,
        string cwd,
        bool allowMacOs,
        out KernelPermissionGrantProfile profile,
        out string? errorMessage)
    {
        profile = Empty;
        errorMessage = null;

        if (permissions.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "permissions must be an object";
            return false;
        }

        var networkEnabled = false;
        if (ReadObject(permissions, "network") is { } network)
        {
            networkEnabled = ReadBoolean(network, "enabled") ?? false;
        }

        var readRoots = Array.Empty<string>();
        var writeRoots = Array.Empty<string>();
        if (ReadObject(permissions, "file_system") is { } fileSystem)
        {
            if (!TryResolvePermissionRoots(fileSystem, "read", cwd, out readRoots, out errorMessage)
                || !TryResolvePermissionRoots(fileSystem, "write", cwd, out writeRoots, out errorMessage))
            {
                return false;
            }
        }

        var hasMacOsPermissions = false;
        if (ReadObject(permissions, "macos") is { } macos)
        {
            if (!allowMacOs)
            {
                errorMessage = "request_permissions only supports network and file_system permissions";
                return false;
            }

            hasMacOsPermissions = HasRequestedMacOsPermissions(macos);
            if (hasMacOsPermissions && !OperatingSystem.IsMacOS())
            {
                errorMessage = "`permissions.macos` is only supported on macOS";
                return false;
            }
        }

        profile = new KernelPermissionGrantProfile
        {
            NetworkEnabled = networkEnabled,
            ReadRoots = readRoots,
            WriteRoots = writeRoots,
            HasMacOsPermissions = hasMacOsPermissions,
        };
        return true;
    }

    private static bool TryResolvePermissionRoots(
        JsonElement parent,
        string propertyName,
        string cwd,
        out string[] roots,
        out string? errorMessage)
    {
        roots = Array.Empty<string>();
        errorMessage = null;

        if (ReadArray(parent, propertyName) is not { } array)
        {
            return true;
        }

        var resolved = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                errorMessage = $"`permissions.file_system.{propertyName}` must only contain strings";
                return false;
            }

            var rawPath = KernelToolJsonHelpers.Normalize(item.GetString());
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            var fullPath = Path.IsPathRooted(rawPath)
                ? Path.GetFullPath(rawPath)
                : Path.GetFullPath(Path.Combine(cwd, rawPath));
            resolved.Add(fullPath);
        }

        roots = DeduplicatePaths(resolved);
        return true;
    }

    private static bool HasRequestedMacOsPermissions(JsonElement macos)
    {
        if (!string.IsNullOrWhiteSpace(KernelToolJsonHelpers.ReadString(macos, "preferences")))
        {
            return true;
        }

        if (ReadBoolean(macos, "accessibility") == true
            || ReadBoolean(macos, "calendar") == true
            || ReadBoolean(macos, "launch_services") == true
            || ReadBoolean(macos, "reminders") == true)
        {
            return true;
        }

        if (HasRequestedMacOsContactsPermission(KernelToolJsonHelpers.ReadString(macos, "contacts")))
        {
            return true;
        }

        var automations = ReadArray(macos, "automations");
        return automations.HasValue && automations.Value.GetArrayLength() > 0;
    }

    private static bool HasRequestedMacOsPermissions(KernelMacOsPermissionArguments? macos)
    {
        if (macos is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(KernelToolJsonHelpers.Normalize(macos.Preferences)))
        {
            return true;
        }

        if (macos.Accessibility == true
            || macos.Calendar == true
            || macos.LaunchServices == true
            || macos.Reminders == true)
        {
            return true;
        }

        if (HasRequestedMacOsContactsPermission(macos.Contacts))
        {
            return true;
        }

        return macos.Automations is { Length: > 0 };
    }

    private static bool HasRequestedMacOsContactsPermission(string? value)
    {
        var normalized = NormalizeMacOsContactsPermission(value);
        return !string.IsNullOrWhiteSpace(normalized)
            && !string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeMacOsContactsPermission(string? value)
    {
        var normalized = KernelToolJsonHelpers.Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.ToLowerInvariant().Replace('-', '_') switch
        {
            "readonly" => "read_only",
            "readwrite" => "read_write",
            var text => text,
        };
    }

    private static string[] IntersectRoots(IReadOnlyList<string> requestedRoots, IReadOnlyList<string> grantedRoots)
    {
        if (requestedRoots.Count == 0 || grantedRoots.Count == 0)
        {
            return Array.Empty<string>();
        }

        var intersected = new List<string>();
        foreach (var requestedRoot in requestedRoots)
        {
            foreach (var grantedRoot in grantedRoots)
            {
                var overlap = TryIntersectRoot(requestedRoot, grantedRoot);
                if (!string.IsNullOrWhiteSpace(overlap))
                {
                    intersected.Add(overlap!);
                }
            }
        }

        return DeduplicatePaths(intersected);
    }

    private static string? TryIntersectRoot(string requestedRoot, string grantedRoot)
    {
        var normalizedRequested = NormalizeRoot(requestedRoot);
        var normalizedGranted = NormalizeRoot(grantedRoot);
        if (normalizedRequested is null || normalizedGranted is null)
        {
            return null;
        }

        if (IsPathCoveredByRoot(normalizedRequested, normalizedGranted))
        {
            return normalizedRequested;
        }

        if (IsPathCoveredByRoot(normalizedGranted, normalizedRequested))
        {
            return normalizedGranted;
        }

        return null;
    }

    private static bool IsPathCoveredByAnyRoot(string path, IReadOnlyList<string> roots)
    {
        var normalizedPath = NormalizeRoot(path);
        if (normalizedPath is null)
        {
            return false;
        }

        foreach (var root in roots)
        {
            if (IsPathCoveredByRoot(normalizedPath, root))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPathCoveredByRoot(string path, string root)
    {
        var normalizedPath = NormalizeRoot(path);
        var normalizedRoot = NormalizeRoot(root);
        if (normalizedPath is null || normalizedRoot is null)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(normalizedPath, normalizedRoot, comparison))
        {
            return true;
        }

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison)
               || normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, comparison);
    }

    private static string[] MergePaths(IReadOnlyList<string> left, IReadOnlyList<string> right)
        => DeduplicatePaths(left.Concat(right));

    private static string[] DeduplicatePaths(IEnumerable<string> paths)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return paths
            .Select(NormalizeRoot)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(comparer)
            .Cast<string>()
            .ToArray();
    }

    private static string? NormalizeRoot(string? path)
    {
        var normalized = KernelToolJsonHelpers.Normalize(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(normalized));
    }

    private static JsonElement? ReadObject(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return value.Clone();
    }

    private static JsonElement? ReadArray(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value.Clone();
    }

    private static bool? ReadBoolean(JsonElement json, string propertyName)
        => KernelToolJsonHelpers.ReadBool(json, propertyName);
}
